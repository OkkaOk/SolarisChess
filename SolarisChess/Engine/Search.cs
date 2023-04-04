
using Rudzoft.ChessLib;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.Notation;
using Rudzoft.ChessLib.Types;
using Rudzoft.ChessLib.Enums;
using Rudzoft.ChessLib.Hash;
using System;
using System.Diagnostics;
using static System.Math;
using Rudzoft.ChessLib.Hash.Tables.Transposition;
using SolarisChess.Extensions;
using Rudzoft.ChessLib.Tables.History;
using System.Runtime.CompilerServices;
using static System.Formats.Asn1.AsnWriter;

namespace SolarisChess;

/// <summary>
/// Class that performs a search for the best move in a given position.
/// </summary>
public class Search
{
	const int SearchInvalid = 20000;
	const int AspirationWindowSize = 60;
	const int DeltaMargin = 200;
	const int DeltaValue = DeltaMargin + PositionEvaluator.queenValue;
	const int ProbCutDepth = 4;
	const int ProbCutReduction = 3;
	const int ProbCutMargin = PositionEvaluator.pawnValue;

	private ValMove bestMove, ponderMove;
	public static readonly ValMove[,] killerMoves = new ValMove[Values.MAX_PLY, 2];
	public static readonly HistoryHeuristic history = new HistoryHeuristic();

	public CancellationTokenSource SearchCancellationTokenSource { get; private set; }
	private CancellationToken CancellationToken => SearchCancellationTokenSource.Token;

	private int maxDepth;

	static TranspositionTable Table => Engine.Table;
	static TimeControl Controller => Engine.Controller;
	static IGame Game => Engine.Game;

	readonly Stopwatch timer = new();
	long normalSearchTime = 0;
	long quiescenceTime = 0;

	// Diagnostics
	int numNodes;
	int numQNodes;
	int numCutoffs;
	int numTranspositions;

	int TotalNodes => numNodes + numQNodes;

	public Search()
	{
		SearchCancellationTokenSource = new CancellationTokenSource();
	}

	/// <summary>
	/// Searches for the best move by performing an iterative deepening search with alpha-beta pruning.
	/// <br>Raises the OnSearchComplete event when finished with the best move found.</br>
	/// </summary>
	public void IterativeDeepeningSearch(IPosition position)
	{
		// Start the timer and initialize diagnostic variables.
		Stopwatch watch = Stopwatch.StartNew();
		timer.Start();

		SearchCancellationTokenSource = new CancellationTokenSource(Controller.AllocatedTimePerMove);

		InitDebugInfo();

		bestMove = ValMove.Empty;
		ponderMove = ValMove.Empty;

		ValMove[] pvline = new ValMove[Values.MAX_PLY];

		Engine.Log($"Search scheduled to take {(Controller.IsInfinite ? "as long as needed" : Controller.AllocatedTimePerMove + "ms")}!");
		//StartMonitor();
		maxDepth = 1;

		Table.NewSearch();
		history.AgeTable();

		while (!CancellationToken.IsCancellationRequested)
		{
			Controller.StartInterval();

			long beginTime = timer.ElapsedMilliseconds;
			int alpha = bestMove.Score - AspirationWindowSize;
			int beta = bestMove.Score + AspirationWindowSize;

			//var task = Task.Factory.StartNew(() => PVSearch(position, maxDepth, 0, PositionEvaluator.negativeInfinity, PositionEvaluator.positiveInfinity, pvline), CancellationToken);
			//task.Wait();
			//int score = task.Result;

			var task = Task.Factory.StartNew(() =>
			{
				int score = PVSearch(position, maxDepth, 0, alpha, beta, pvline);

				//Fail - low or fail-high->widen the search window
				while (score <= alpha || score >= beta)
				{
					if (score <= alpha)
						alpha -= AspirationWindowSize;
					if (score >= beta)
						beta += AspirationWindowSize;

					score = PVSearch(position, maxDepth, 0, alpha, beta, pvline);

					if (CancellationToken.IsCancellationRequested)
						break;
				}

				return score;

			}, CancellationToken);

			//task.Wait();
			int score = task.Result;

			normalSearchTime += timer.ElapsedMilliseconds - beginTime;

			if (CancellationToken.IsCancellationRequested)
				break;

			bestMove = pvline[0];
			ponderMove = pvline[1];

			// Update diagnostics
			Engine.OnInfo(maxDepth, score, TotalNodes, (int)watch.ElapsedMilliseconds, Table.Fullness(), pvline);

			if (PositionEvaluator.IsMateScore(score))
			{
				var plyToMate = PositionEvaluator.NumPlyToMateFromScore(score);
				// Exit search if found a mate
				if (!Controller.isPondering && plyToMate < maxDepth)
					break;

				// Happens only when pondering the very last move of the game
				//if (movesToMate == 0)
				//{
				//	Console.WriteLine("Returned from search because i'm mated");
				//	return;
				//}
			}

			maxDepth++;

			if (maxDepth >= Values.MAX_PLY)
				break;

			if (!Controller.CanSearchDeeper(maxDepth, numNodes))
				SearchCancellationTokenSource.Cancel();
		}
		maxDepth--;

		// If no move has been found, choose the first generated move.
		if (bestMove == ValMove.Empty || !position.IsLegal(bestMove))
		{
			var entry = Cluster.DefaultEntry;
			if (Table.Probe(position.State.Key, ref entry) && !entry.Move.IsNullMove() && position.IsLegal(entry.Move))
			{
				Engine.Log("-------------Had to choose a move from tt-------------");
				bestMove.Move = entry.Move;
			}
			else
			{
				Engine.Log("-------------Had to choose a stupid move-------------");
				bestMove = MoveOrdering.GenerateAndOrderMoves(position, 0, pvline)[0];
			}
		}

		// Log diagnostic information and invoke the OnSearchComplete event.
		watch.Stop();
		Engine.Log("Time spent on this move: " + watch.ElapsedMilliseconds);
		Engine.Log("Total nodes visited: " + TotalNodes);
		Engine.Log("Normal nodes visited: " + numNodes);
		Engine.Log("Qnodes visited: " + numQNodes);
		Engine.Log("Total branches cut off: " + numCutoffs);
		Engine.Log("Total tt hits: " + numTranspositions);
		Engine.Log("PVSearch time: " + (normalSearchTime - quiescenceTime));
		Engine.Log("Quiescence time: " + quiescenceTime);

		//string pv = "";
		//for (int i = 0; i < maxDepth; i++)
		//	pv += Game.Uci.MoveToString(pvline[i].Move) + " ";

		//Engine.Log("PV line: " + pv);

		Engine.OnSearchComplete(bestMove, ponderMove);
	}


	/// <summary>
	/// Performs principal variation search with alpha-beta pruning to determine the best move.
	/// </summary>
	/// <param name="depth">Depth left to search.</param>
	/// <param name="plyFromRoot">The ply count from the root node.</param>
	/// <param name="alpha">Alpha (α) is the lower bound, representing the minimum score that a node must reach in order to change the value of a previous node</param>
	/// <param name="beta">Beta (β) is the upper bound of a score for the node.<br></br> If the node value exceeds or equals beta, it means that the opponent will avoid this node, since his guaranteed score (Alpha of the parent node) is already greater.</param>
	/// <param name="lastMoveWasCapture">Indicates whether the last move made was a capture.</param>
	/// <returns>The evaluation score for the current position.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	int PVSearch(IPosition position, int depth, int plyFromRoot, int alpha, int beta, ValMove[] pline)
	{
		if (CancellationToken.IsCancellationRequested)
			return SearchInvalid;

		ValMove[] line = new ValMove[maxDepth];

		numNodes++;

		if (plyFromRoot > 0 && ProbeTranspositionTable(position.State.Key, depth, ref alpha, ref beta, out TranspositionTableEntry entry))
		{
			pline[0].Move = entry.Move;
			pline[0].Score = entry.Value;
			for (int j = 1; j < maxDepth; j++)
				pline[j] = line[j - 1];

			return entry.Value;
		}

		if (position.HasRepetition() || position.IsThreeFoldRepetition() || position.IsInsufficientMaterial())
			return 0;

		// Skip this position if a mating sequence has already been found earlier in
		// the search, which would be shorter than any mate we could find from here.
		// This is done by observing that alpha can't possibly be worse (and likewise
		// beta can't  possibly be better) than being mated in the current position.
		alpha = Max(alpha, -PositionEvaluator.immediateMateScore + plyFromRoot);
		beta = Min(beta, PositionEvaluator.immediateMateScore - plyFromRoot);
		if (alpha >= beta)
			return alpha;

		if (depth <= 0)
		{
			long beginTime = timer.ElapsedMilliseconds;
			int eval = QuiescenceSearch(position, 0, plyFromRoot, alpha, beta);
			quiescenceTime += timer.ElapsedMilliseconds - beginTime;

			return eval;
		}

		#region test
		//if (depth >= ProbCutDepth && Abs(alpha) < PositionEvaluator.immediateMateScore)
		//{
		//	int shallowSearchDepth = depth - ProbCutReduction;
		//	int probCutScore = -ZWSearch(position, -alpha, shallowSearchDepth, plyFromRoot, line);
		//	if (probCutScore >= beta + ProbCutMargin)
		//	{
		//		return probCutScore;
		//	}
		//}
		#endregion

		var moves = MoveOrdering.GenerateAndOrderMoves(position, plyFromRoot, pline);

		if (moves.Length == 0)
		{
			if (position.InCheck)
			{
				int mateScore = PositionEvaluator.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			// Return a little bit of score, trying to prolong the draw.
			return 0; 
		}

		var type = Bound.Alpha;

		for (int i = 0; i < moves.Length; i++)
		{
			bool moveIsQuiet = !position.IsCaptureOrPromotion(moves[i]);
			//bool moveGivesCheck = position.GivesCheck(moves[i]);

			position.MakeMove(moves[i], null);

			int score;
			if (i == 0)
			{
				if (position.InCheck)
					depth++;

				score = -PVSearch(position, depth - 1, plyFromRoot + 1, -beta, -alpha, line);
			}
			else
			{
				score = -ZWSearch(position, -alpha, depth - 1, plyFromRoot + 1, line);

				if (score > alpha)
				{
					score = -PVSearch(position, depth - 1, plyFromRoot + 1, -beta, -alpha, line);
				}
			}

			position.TakeMove(moves[i]);

			// Search was stopped because the allocated time ran out.
			if (CancellationToken.IsCancellationRequested)
				return SearchInvalid;

			moves[i].Score = score;

			if (score >= beta)
			{
				if (moveIsQuiet)
				{
					// TODO: Mate killers
					killerMoves[plyFromRoot, 1] = killerMoves[plyFromRoot, 0];
					killerMoves[plyFromRoot, 0] = moves[i];

					var (from, to) = moves[i].Move;
					history.Add(position.SideToMove, from, to, depth * depth);
				}

				numCutoffs++;

				Table.Store(position.State.Key, score, Bound.Beta, (sbyte)depth, moves[i], 0);
				return beta;
			}

			if (score > alpha)
			{

				// Found a new best move in this position
				alpha = score;

				type = Bound.Exact;
				pline[0] = moves[i];
				for (int j = 1; j < maxDepth; j++)
				{
					pline[j] = line[j - 1];
				}
			}
		}

		Table.Store(position.State.Key, alpha, type, (sbyte)depth, pline[0], 0);

		return alpha;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	int ZWSearch(IPosition position, int beta, int depth, int plyFromRoot, ValMove[] pline, bool allowNullMove = true)
	{
		if (CancellationToken.IsCancellationRequested)
			return SearchInvalid;

		if (position.IsThreeFoldRepetition())
			return 0;

		var entry = Cluster.DefaultEntry;
		if (plyFromRoot > 0 && Table.Probe(position.State.Key, ref entry) && entry.Depth >= depth)
		{
			numTranspositions++;

			if (entry.Type == Bound.Exact && entry.Value >= beta)
				return entry.Value;

			if (entry.Type == Bound.Beta && entry.Value < beta)
				beta = entry.Value; // Update beta with the upper bound value
		}

		// alpha is beta - 1
		// this is either a cut- or all-node
		if (depth <= 0)
		{
			long beginTime = timer.ElapsedMilliseconds;
			int eval = QuiescenceSearch(position, 0, plyFromRoot, beta - 1, beta);
			quiescenceTime += timer.ElapsedMilliseconds - beginTime;

			return eval;
		}

		// Null move pruning
		if (allowNullMove && depth >= 2 && !position.InCheck && plyFromRoot > 0)
		{
			position.MakeNullMove(null);

			// Reduce depth by R + 1, where R is the reduction factor, often set to 2 or 3
			int nullMoveSearchDepth = depth - 3;

			// Search with the opposite side to move, disallowing further null moves
			int nullMoveScore = -ZWSearch(position, 1 - beta, nullMoveSearchDepth, plyFromRoot + 1, pline, false);

			position.TakeNullMove();

			if (nullMoveScore >= beta)
			{
				numCutoffs++;
				return beta;
			}
		}

		var moves = MoveOrdering.GenerateAndOrderMoves(position, plyFromRoot, pline);

		if (moves.Length == 0)
		{
			if (position.InCheck)
			{
				int mateScore = PositionEvaluator.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			return 0;
		}

		bool inCheck = position.InCheck;

		for (int i = 0; i < moves.Length; i++)
		{
			numNodes++;

			bool moveIsQuiet = !position.IsCaptureOrPromotion(moves[i]);
			bool isKiller = killerMoves[plyFromRoot, 0] == moves[i] || killerMoves[plyFromRoot, 1] == moves[i];

			int newDepth = depth - 1;

			// Late Move Reductions
			if (depth > 3 && moveIsQuiet && !isKiller && !inCheck && allowNullMove)
			{
				newDepth -= (int)Sqrt(i);

				newDepth = Max(0, newDepth);
			}

			position.MakeMove(moves[i], null);
			int score = -ZWSearch(position, 1 - beta, newDepth, plyFromRoot + 1, pline);
			position.TakeMove(moves[i]);

			if (CancellationToken.IsCancellationRequested)
				return SearchInvalid;

			// fail-hard beta-cutoff
			if (score >= beta)
			{
				if (moveIsQuiet)
				{
					//TODO: Mate killers
					killerMoves[plyFromRoot, 1] = killerMoves[plyFromRoot, 0];
					killerMoves[plyFromRoot, 0] = moves[i];
					var (from, to) = moves[i].Move;
					history.Add(position.SideToMove, from, to, depth * depth);
				}

				Table.Store(position.State.Key, score, Bound.Beta, (sbyte)newDepth, moves[i], 0);

				numCutoffs++;
				return beta;
			}
		}

		return beta - 1; // fail-hard, return alpha
	}

	/// <summary>
	/// Searches capture moves until a 'quiet' position is reached.
	/// </summary>
	/// <param name="plyFromRoot">The ply count from the root node.</param>
	/// <param name="alpha">The highest (best) evaluation that the maximizing player (this engine) has found so far.</param>
	/// <param name="beta">The lowest (best) evaluation that the minimizing player (opponent) has found so far.</param>
	/// <returns>The evaluation score for the current position.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	int QuiescenceSearch(IPosition position, int depth, int plyFromRoot, int alpha, int beta)
	{
		if (CancellationToken.IsCancellationRequested)
			return SearchInvalid;

		int alphaOrig = alpha;

		if (ProbeTranspositionTable(position.State.Key, (sbyte)depth, ref alpha, ref beta, out TranspositionTableEntry entry))
			return entry.Value;

		if (position.IsInsufficientMaterial())
			return 0;

		// A player isn't forced to make a capture (typically), so see what the evaluation is without capturing anything.
		// This prevents situations where a player only has bad captures available from being evaluated as bad,
		// when the player might have good non-capture moves available.
		int standPat = PositionEvaluator.Evaluate(position);

		if (standPat >= beta)
		{
			numCutoffs++;
			//Table.Store(position.State.Key, standPat, Bound.Beta, (sbyte)depth, ValMove.Empty, 0);
			return beta;
		}

		//int delta = PositionEvaluator.queenValue;
		//if (position.State.LastMove.IsPromotionMove())
		//	delta += 775;

		//if (standPat < alpha - delta)
		//	return alpha;

		if (standPat > alpha)
			alpha = standPat;

		var moves = MoveOrdering.GenerateAndOrderMoves(position, plyFromRoot);

		if (moves.Length == 0)
		{
			if (position.InCheck) 
			{
				int mateScore = PositionEvaluator.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			return 0; // Return a little bit of score, trying to prolong the draw.
		}

		float phase = PositionEvaluator.CalculatePhase(position);
		bool endGame = phase > 0.7f;

		ValMove bestMove = ValMove.Empty;

		foreach (var move in moves)
		{
			if (!position.IsCaptureOrPromotion(move))
				continue;

			if (!endGame)
			{
				var capturePieceType = position.GetPieceType(move.Move.ToSquare());
				if (standPat + DeltaValue + PositionEvaluator.GetPieceValue(capturePieceType) <= alpha)
					continue;
			}

			numQNodes++;
			position.MakeMove(move, null);
			int score = -QuiescenceSearch(position, depth - 1, plyFromRoot + 1, -beta, -alpha);
			position.TakeMove(move);

			if (CancellationToken.IsCancellationRequested)
				return SearchInvalid;

			if (score >= beta)
			{
				numCutoffs++;
				//Table.Store(position.State.Key, score, Bound.Beta, (sbyte)depth, move, 0);
				return beta;
			}

			if (score > alpha)
			{
				alpha = score;
				bestMove = move;
			}

		}

		Bound type = GetBoundType(alphaOrig, beta, alpha);
		Table.Store(position.State.Key, alpha, type, (sbyte)depth, bestMove, 0);

		return alpha;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool ProbeTranspositionTable(HashKey key, int depth, ref int alpha, ref int beta, out TranspositionTableEntry entry)
	{
		entry = new TranspositionTableEntry();

		if (Table.Probe(key, ref entry) && entry.Depth >= depth)
		{
			numTranspositions++;

			if (entry.Type == Bound.Exact)
			{
				return true;
			}
			else if (entry.Type == Bound.Alpha)
			{
				alpha = Max(alpha, entry.Value);
			}
			else if (entry.Type == Bound.Beta)
			{
				beta = Min(beta, entry.Value);
			}

			if (alpha >= beta)
			{
				return true;
			}
		}

		return false;
	}

	private Bound GetBoundType(int alphaOrig, int beta, int score)
	{
		Bound type = Bound.Exact;
		if (score <= alphaOrig)
			type = Bound.Beta;
		else if (score >= beta)
			type = Bound.Alpha;

		return type;
	}

	public void PonderHit()
	{
		Controller.PonderHit();

		if (!Controller.HasTimeForNextDepth())
			SearchCancellationTokenSource.Cancel();
	}

	public void CancelSearch()
	{
		SearchCancellationTokenSource.Cancel();
	}

	void InitDebugInfo()
	{
		numNodes = 0;
		numQNodes = 0;
		numCutoffs = 0;
		numTranspositions = 0;
		normalSearchTime = 0;
		quiescenceTime = 0;
	}
}