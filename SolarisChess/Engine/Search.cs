
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
	const int AspirationWindowSize = 25;
	const int DeltaMargin = 200;
	const int DeltaValue = DeltaMargin + PositionEvaluator.queenValue;
	const int ProbCutDepth = 4;
	const int ProbCutReduction = 3;
	const int ProbCutMargin = PositionEvaluator.pawnValue;
	static readonly int[] AspirationDelta = { 50, 97, 307, 965, 3036, 9546, PositionEvaluator.positiveInfinity };

	private ValMove bestMove, ponderMove;
	public static readonly ValMove[,] killerMoves = new ValMove[Values.MAX_PLY, 2];
	public static readonly HistoryHeuristic history = new HistoryHeuristic();

	public CancellationTokenSource SearchCancellationTokenSource { get; private set; }
	private CancellationToken CancellationToken => SearchCancellationTokenSource.Token;

	private int maxDepth;
	private int selectiveDepth;

	static TranspositionTable Table => Engine.Table;
	static TimeControl Controller => Engine.Controller;
	static IGame Game => Engine.Game;

	readonly Stopwatch timer = new();

	// Diagnostics
	public long totalSearchTime = 0;
	public long quiescenceTime = 0;
	public int numNodes;
	public int numQNodes;
	public int numCutoffs;
	public int numTranspositions;

	public int TotalNodes => numNodes + numQNodes;

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

		history.AgeTable();
		Table.NewSearch();

		while (!CancellationToken.IsCancellationRequested)
		{
			Controller.StartInterval();
			selectiveDepth = maxDepth;

			long beginTime = timer.ElapsedMilliseconds;
			int alpha = bestMove.Score - AspirationDelta[0];
			int beta = bestMove.Score + AspirationDelta[0];

			if (maxDepth == 1)
			{
				alpha = PositionEvaluator.negativeInfinity;
				beta = PositionEvaluator.positiveInfinity;
			}

			//var task = Task.Factory.StartNew(() => PVSearch(position, maxDepth, 0, PositionEvaluator.negativeInfinity, PositionEvaluator.positiveInfinity, pvline), CancellationToken);

			var task = Task.Factory.StartNew(() =>
			{
				int score = PVSearch(position, maxDepth, 0, alpha, beta, pvline);

				//Fail - low or fail-high->widen the search window
				var failCount = 0;
				while (score <= alpha || score >= beta)
				{
					//Console.WriteLine("Trying depth " + maxDepth + " again");
					if (score <= alpha)
						alpha -= AspirationDelta[failCount];
					if (score >= beta)
						beta += AspirationDelta[failCount];

					score = PVSearch(position, maxDepth, 0, alpha, beta, pvline);

					if (failCount < AspirationDelta.Length - 1)
						failCount++;

					if (CancellationToken.IsCancellationRequested)
						break;
				}

				return score;

			}, CancellationToken);

			int score = task.Result;

			totalSearchTime += timer.ElapsedMilliseconds - beginTime;

			if (CancellationToken.IsCancellationRequested)
				break;

			bestMove = pvline[0];
			ponderMove = pvline[1];

			// Update diagnostics
			var micros = (int)(watch.ElapsedTicks * 1000000 / Stopwatch.Frequency);
			Engine.OnInfo(maxDepth, selectiveDepth, score, TotalNodes, micros, Table.Fullness(), pvline);

			if (PositionEvaluator.IsMateScore(score))
			{
				var plyToMate = PositionEvaluator.NumPlyToMateFromScore(score);
				// Exit search if found a mate
				if (!Controller.isPondering && plyToMate < maxDepth)
					break;
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
		Engine.Log("PVSearch time: " + (totalSearchTime - quiescenceTime));
		Engine.Log("Quiescence time: " + quiescenceTime);

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

		if (position.IsThreeFoldRepetition() || position.IsInsufficientMaterial())
			return 0;

		var entry = Cluster.DefaultEntry;
		//if (plyFromRoot > 0 && Table.Probe(position.State.Key, ref entry) && entry.Depth >= depth)
		//{
			//if (entry.Type == Bound.Exact)
			//{
			//	GetPVFromTranspositionTable(position, pline);
			//	//pline[0] = entry.Move;
			//	//for (int j = 1; j < maxDepth; j++)
			//	//	pline[j] = ValMove.Empty;

			//	return entry.Value;
			//}
			//if (entry.Type == Bound.Alpha)
			//	alpha = Max(alpha, entry.Value);
			//else if (entry.Type == Bound.Beta)
			//	beta = Min(beta, entry.Value);

			//if (alpha >= beta)
			//{
			//	//GetPVFromTranspositionTable(position, pline);
			//	return entry.Value;
			//}
		//}

		ValMove[] line = new ValMove[maxDepth];

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
			if (plyFromRoot > selectiveDepth)
				selectiveDepth = plyFromRoot;

			long beginTime = timer.ElapsedMilliseconds;
			int eval = QuiescenceSearch(position, 0, plyFromRoot, alpha, beta);
			quiescenceTime += timer.ElapsedMilliseconds - beginTime;

			return eval;
		}

		numNodes++;

		int bestScore = alpha;
		var boundType = Bound.Alpha;

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

		float phase = PositionEvaluator.CalculatePhase(position);
		bool endGame = phase > 0.7f;
		bool inCheck = position.InCheck;
		var state = new State();

		// Null move reductions
		if (!endGame && !inCheck && depth > 3 && plyFromRoot > 0)
		{
			int R = 3;
			if (depth > 6)
				R = 4;

			// Give the turn to the opponent and evaluate the position with a lower depth
			position.MakeNullMove(in state);
			int nullMoveScore = -ZWSearch(position, 1 - beta, depth - R - 1, plyFromRoot + 1);
			//int nullMoveScore = -PVSearch(position, depth - R - 1, plyFromRoot + 1, -beta, -alpha, line, false);
			position.TakeNullMove();

			if (nullMoveScore >= beta)
			{
				//Console.WriteLine(nullMoveScore + " " + beta + " " + depth);
				//depth -= 4;
				//if (depth <= 0)
				//{
				//	long beginTime = timer.ElapsedMilliseconds;
				//	int eval = QuiescenceSearch(position, 0, plyFromRoot, alpha, beta);
				//	quiescenceTime += timer.ElapsedMilliseconds - beginTime;
				//	return eval;
				//}
				numCutoffs++;
				return nullMoveScore;
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

			return 0; // Stalemate.
		}

		for (int i = 0; i < moves.Length; i++)
		{
			bool moveIsQuiet = !position.IsCaptureOrPromotion(moves[i]);
			//bool moveGivesCheck = position.GivesCheck(moves[i]);
			bool isKiller = killerMoves[plyFromRoot, 0] == moves[i] || killerMoves[plyFromRoot, 1] == moves[i];

			position.MakeMove(moves[i], in state);

			int score;
			if (i == 0)
			{
				if (position.InCheck)
					depth++;

				score = -PVSearch(position, depth - 1, plyFromRoot + 1, -beta, -alpha, line);

				if (score > alpha)
					alpha = score;
			}
			else
			{
				int newDepth = depth - 1;

				// Late Move Reductions
				if (depth > 3 && moveIsQuiet && !isKiller && !inCheck)
				{
					newDepth -= (int)Sqrt(i);

					newDepth = Max(0, newDepth);
				}

				score = -ZWSearch(position, -alpha, newDepth, plyFromRoot + 1);
				//score = -PVSearch(position, depth - 1, plyFromRoot + 1, -alpha - 1, -alpha, line);
				if (score > alpha && score < beta)
				{
					if (position.InCheck)
						depth++;

					score = -PVSearch(position, depth - 1, plyFromRoot + 1, -beta, -alpha, line);
					if (score > alpha)
						alpha = score;
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

				return score;
			}

			if (score > bestScore)
			{
				// Found a new best move in this position
				bestScore = score;
				boundType = Bound.Exact;

				pline[0] = moves[i];
				for (int j = 1; j < maxDepth; j++)
				{
					pline[j] = line[j - 1];
				}
			}
		}

		Table.Store(position.State.Key, bestScore, boundType, (sbyte)depth, pline[0], 0);

		return bestScore;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	int ZWSearch(IPosition position, int beta, int depth, int plyFromRoot)
	{
		if (CancellationToken.IsCancellationRequested)
			return SearchInvalid;

		if (position.IsThreeFoldRepetition() || position.IsInsufficientMaterial())
			return 0;

		int alpha = beta - 1;
		var entry = Cluster.DefaultEntry;
		if (plyFromRoot > 0 && Table.Probe(position.State.Key, ref entry) && entry.Depth >= depth)
		{

			if (entry.Type == Bound.Exact)
				return entry.Value;
			//else if (entry.Type == Bound.Alpha)
			//	alpha = Max(alpha, entry.Value);
			else if (entry.Type == Bound.Beta)
				beta = Min(beta, entry.Value);

			//if (alpha >= beta)
			//	return entry.Value;
		}

		// this is either a cut- or all-node
		if (depth <= 0)
		{
			long beginTime = timer.ElapsedMilliseconds;
			int eval = QuiescenceSearch(position, 0, plyFromRoot, alpha, beta);
			quiescenceTime += timer.ElapsedMilliseconds - beginTime;

			return eval;
		}

		numNodes++;

		var moves = MoveOrdering.GenerateAndOrderMoves(position, plyFromRoot);
		if (moves.Length == 0)
		{
			if (position.InCheck)
			{
				int mateScore = PositionEvaluator.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			return 0;
		}

		int bestScore = PositionEvaluator.negativeInfinity;
		//ValMove bestZWMove = ValMove.Empty;

		var state = new State();
		for (int i = 0; i < moves.Length; i++)
		{
			bool moveIsQuiet = !position.IsCaptureOrPromotion(moves[i]);

			position.MakeMove(moves[i], in state);
			int score = -ZWSearch(position, -alpha, depth - 1, plyFromRoot + 1);
			position.TakeMove(moves[i]);

			if (CancellationToken.IsCancellationRequested)
				return SearchInvalid;

			// fail-soft beta-cutoff
			if (score >= beta)
			{
				if (moveIsQuiet)
				{
					//TODO: Mate killers
					killerMoves[plyFromRoot, 1] = killerMoves[plyFromRoot, 0];
					killerMoves[plyFromRoot, 0] = moves[i];
					//var (from, to) = moves[i].Move;
					//history.Add(position.SideToMove, from, to, depth * depth);
				}

				//Table.Store(position.State.Key, score, Bound.Beta, (sbyte)depth, moves[i], 0);

				numCutoffs++;
				return score;
			}

			if (score > bestScore)
			{
				bestScore = score;
			//	bestZWMove = moves[i];
			}
		}

		return bestScore;
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

		numQNodes++;

		if (ProbeTranspositionTable(position.State.Key, (sbyte)depth, ref alpha, ref beta, out TranspositionTableEntry entry))
			return entry.Value;

		if (position.IsInsufficientMaterial())
			return 0;

		int standPat = PositionEvaluator.Evaluate(position);

		if (standPat >= beta)
		{
			numCutoffs++;
			//Table.Store(position.State.Key, standPat, Bound.Beta, (sbyte)depth, ValMove.Empty, 0);
			return standPat;
		}

		if (standPat > alpha)
			alpha = standPat;

		var moves = MoveOrdering.GenerateAndOrderMoves(position, plyFromRoot);
		//var moves = position.GenerateMoves();

		if (moves.Length == 0)
		{
			if (position.InCheck) 
			{
				int mateScore = PositionEvaluator.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			return 0; // Return a little bit of score, trying to prolong the draw.
		}

		//float phase = PositionEvaluator.CalculatePhase(position);
		//bool endGame = phase > 0.7f;

		//ValMove bestQMove = ValMove.Empty;
		//var boundType = Bound.Alpha;

		var state = new State();
		foreach (var move in moves)
		{
			if (!position.IsCaptureOrPromotion(move))
				continue;

			var capturePieceType = position.GetPieceType(move.Move.ToSquare());
			if (standPat + DeltaValue + PositionEvaluator.GetPieceValue(capturePieceType) <= alpha)
			{
				numCutoffs++;
				continue;
			}

			position.MakeMove(move, in state);
			int score = -QuiescenceSearch(position, depth - 1, plyFromRoot + 1, -beta, -alpha);
			position.TakeMove(move);

			if (CancellationToken.IsCancellationRequested)
				return SearchInvalid;

			if (score >= beta)
			{
				numCutoffs++;
				//Table.Store(position.State.Key, score, Bound.Beta, (sbyte)depth, move, 0);

				return score;
			}

			if (score > alpha)
			{
				alpha = score;
				//bestQMove = move;
				//boundType = Bound.Exact;
			}

		}

		//Table.Store(position.State.Key, alpha, boundType, (sbyte)depth, bestQMove, 0);

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

	private void GetPVFromTranspositionTable(IPosition position, ValMove[] pline)
	{
		TranspositionTableEntry entry = new TranspositionTableEntry();

		int count = 0;
		List<Move> movesMade = new();

		for (int depth = 0; depth < maxDepth; depth++)
		{
			if (!Table.Probe(position.State.Key, ref entry) || entry.Move == Move.EmptyMove || entry.Type != Bound.Exact)
				break;

			if (!position.IsLegal(entry.Move) || position.MovedPiece(entry.Move).ColorOf() != position.SideToMove)
				break;

			pline[depth] = entry.Move;
			pline[depth].Score = entry.Value;
			movesMade.Add(entry.Move);

			position.MakeMove(entry.Move, null);
			count++;
		}

		//for (int i = count; i < maxDepth; i++)
		//{
		//	pline[i] = ValMove.Empty;
		//}

		// Undo the moves made to restore the original position
		for (int i = movesMade.Count - 1; i >= 0; i--)
		{
			position.TakeMove(movesMade[i]);
		}
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
		totalSearchTime = 0;
		quiescenceTime = 0;
	}
}