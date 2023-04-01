
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

namespace SolarisChess;

/// <summary>
/// Class that performs a search for the best move in a given position.
/// </summary>
public class Search
{
	const int transpositionTableSize = 64000;
	const int SearchInvalid = 20000;
	const int AspirationWindowSize = 30;
	const int DeltaValue = 200;
	const int ProbCutDepth = 4;
	const int ProbCutReduction = 3;
	const int ProbCutMargin = PositionEvaluator.pawnValue;

	public event Action<Move>? OnSearchComplete;

	private ValMove bestMove = ValMove.Empty;
	private ValMove[,] pvMoves = new ValMove[Values.MAX_PLY, Values.MAX_PLY];
	private ValMove[,] killerMoves = new ValMove[Values.MAX_PLY, 2];
	private HistoryHeuristic history;

	private bool abortSearch;
	private int maxDepth;

	TranspositionTable Table => Engine.Table;
	TimeControl controller => Engine.controller;

	readonly Stopwatch timer = new();
	long normalSearchTime = 0;
	long quiescenceTime = 0;

	IGame Game => Engine.Game;

	// Diagnostics
	int numNodes;
	int numCutoffs;
	int numTranspositions;

	public Search()
	{
		history = new HistoryHeuristic();
	}

	/// <summary>
	/// Searches for the best move by performing an iterative deepening search with alpha-beta pruning.
	/// <br>Raises the OnSearchComplete event when finished with the best move found.</br>
	/// </summary>
	public void IterativeDeepeningSearch(IPosition position)
	{
		// Start the timer and initialize diagnostic variables.
		timer.Start();
		abortSearch = false;
		InitDebugInfo();

		bestMove = ValMove.Empty;
		pvMoves = new ValMove[Values.MAX_PLY, Values.MAX_PLY];
		killerMoves = new ValMove[Values.MAX_PLY, 2];

		Stopwatch watch = Stopwatch.StartNew();

		Engine.Log($"Search scheduled to take {(controller.IsInfinite ? "as long as needed" : controller.AllocatedTimePerMove + "ms")}!");

		StartMonitor();
		maxDepth = 0;

		while (controller.CanSearchDeeper(maxDepth++, numNodes))
		{
			Table.NewSearch();
			controller.StartInterval();

			long beginTime = timer.ElapsedMilliseconds;
			int alpha = bestMove.Score - AspirationWindowSize;
			int beta = bestMove.Score + AspirationWindowSize;

			//int score = PVSearch(position, maxDepth, 0, PositionEvaluator.negativeInfinity, PositionEvaluator.positiveInfinity);
			int score = PVSearch(position, maxDepth, 0, alpha, beta);

			//Fail-low or fail-high -> widen the search window
			while (score <= alpha || score >= beta)
			{
				alpha -= AspirationWindowSize;
				beta += AspirationWindowSize;
				score = PVSearch(position, maxDepth, 0, alpha, beta);

				if (abortSearch)
					break;
			}

			normalSearchTime += timer.ElapsedMilliseconds - beginTime;

			if (abortSearch)
			{
				break;
			}

			if (position.IsLegal(pvMoves[maxDepth, 0]))
				bestMove = pvMoves[maxDepth, 0];

			ValMove[] pvLine = new ValMove[maxDepth];
			for (int i = 0; i < maxDepth; i++)
			{
				if (pvMoves[maxDepth, i].Move.IsNullMove())
					break;

				pvLine[i] = pvMoves[maxDepth, i];
			}


			// Update diagnostics
			Engine.OnInfo(maxDepth, score, numNodes, controller.Elapsed, Table.Fullness() * 10, pvLine);
			//OnInfo?.Invoke(searchDepth, pvMoves[0].Score, numNodes, controller.Elapsed, Table.Fullness() * 10, pvMoves);

			//if (searchDepth > 5 && pvMoves[0].Score > bestMove.Score + 5)
			//{
			//	bestMove = pvMoves[0];
			//	break;
			//}

			// Exit search if found a mate
			if (PositionEvaluator.IsMateScore(score) && position.IsLegal(bestMove) && maxDepth > 3)
				break;

		}
		maxDepth--;

		// If no move has been found, choose the first generated move.
		if (bestMove == ValMove.Empty || !position.IsLegal(bestMove))
		{
			var entry = Cluster.DefaultEntry;
			if (Table.Probe(position.State.Key, ref entry) && position.IsLegal(entry.Move))
				bestMove.Move = entry.Move;
			else
				bestMove = GenerateAndOrderMoves(position, 0)[0];

			Engine.Log("-------------Had to choose a stupid move-------------");
		}

		// Log diagnostic information and invoke the OnSearchComplete event.
		watch.Stop();
		Engine.Log("Time spent on this move: " + watch.ElapsedMilliseconds);
		Engine.Log("Total nodes visited: " + numNodes);
		Engine.Log("Total branches cut off: " + numCutoffs);
		Engine.Log("Total tt hits: " + numTranspositions);
		Engine.Log("PVSearch time: " + (normalSearchTime - quiescenceTime));
		Engine.Log("Quiescence time: " + quiescenceTime);

		string pv = "";
		for (int i = 0; i < maxDepth; i++)
			pv += Game.Uci.MoveToString(pvMoves[maxDepth, i].Move) + " ";

		Engine.Log("PV line: " + pv);

		OnSearchComplete?.Invoke(bestMove.Move);
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
	int PVSearch(IPosition position, int depth, int plyFromRoot, int alpha, int beta)
	{
		if (abortSearch)
			return SearchInvalid;

		numNodes++;

		int alphaOrig = alpha;

		if (IsTerminalNode(position, plyFromRoot, ref alpha, ref beta, out int value, true, false, false))
			return value;

		if (ProbeTranspositionTable(position.State.Key, depth, ref alpha, ref beta, out TranspositionTableEntry entry))
		{
			pvMoves[maxDepth, plyFromRoot] = entry.Move;
			return entry.Value;
		}

		if (depth <= 0)
		{
			long beginTime = timer.ElapsedMilliseconds;
			int eval = QuiescenceSearch(position, plyFromRoot, alpha, beta);
			quiescenceTime += timer.ElapsedMilliseconds - beginTime;

			return eval;
		}

		#region test
		//if (position.State.PliesFromNull != 0 && depth > 3 && !position.InCheck)
		//{
		//	position.MakeNullMove(new());
		//	int nullScore = -ZWSearch(position, 1 - beta, depth - 3, plyFromRoot + 1);
		//	position.TakeNullMove();
		//	if (nullScore >= beta)
		//	{
		//		Console.WriteLine("Used null move to cut off a branch!");
		//		return nullScore;
		//	}
		//}

		//if (depth >= ProbCutDepth && maxDepth - depth > 3 && Abs(bestScore) < PositionEvaluator.immediateMateScore)
		//{
		//	int shallowSearchDepth = depth - ProbCutReduction;
		//	int probCutScore = -Negascout(position, shallowSearchDepth, -(worstScore + ProbCutMargin), -worstScore, lastMoveWasCapture);
		//	if (probCutScore >= worstScore + ProbCutMargin)
		//	{
		//		return probCutScore;
		//	}
		//}
		#endregion

		var moves = GenerateAndOrderMoves(position, plyFromRoot);

		if (moves.Length == 0)
		{
			if (position.InCheck)
			{
				int mateScore = PositionEvaluator.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			// Return a little bit of score, trying to prolong the draw.
			return plyFromRoot; 
		}

		bool firstMoveQuiet = !position.IsCaptureOrPromotion(moves[0]);

		position.MakeMove(moves[0], null);
		int bestScore = -PVSearch(position, depth - 1, plyFromRoot + 1, -beta, -alpha);
		position.TakeMove(moves[0]);

		moves[0].Score = bestScore;

		if (bestScore > alpha)
		{
			if (bestScore >= beta)
			{
				if (firstMoveQuiet)
				{
					killerMoves[plyFromRoot, 1] = killerMoves[plyFromRoot, 0];
					killerMoves[plyFromRoot, 0] = moves[0];
				}

				var (from, to) = moves[0].Move;
				history.Set(position.SideToMove, from, to, depth * depth);

				numCutoffs++;
				return bestScore;
			}

			alpha = bestScore;
			pvMoves[maxDepth, plyFromRoot] = moves[0];
		}

		for (int i = 1; i < moves.Length; i++)
		{
			bool moveIsQuiet = !position.IsCaptureOrPromotion(moves[i]);
			//bool moveGivesCheck = position.GivesCheck(moves[i]);

			position.MakeMove(moves[i], null);

			//int score = -PVSearch(position, newDepth, plyFromRoot + 1, -alpha - 1, -alpha, moveIsCapture);
			int score = -ZWSearch(position, -alpha, depth - 1, plyFromRoot + 1);

			// Found a better move, so we need to do a research with window [alpha, beta]
			if (score > alpha)
			{
				score = -PVSearch(position, depth - 1, plyFromRoot + 1, -beta, -alpha);
				if (score > alpha)
					alpha = score;
			}

			position.TakeMove(moves[i]);

			// Search was stopped because the allocated time ran out.
			if (score == -SearchInvalid)
				return SearchInvalid;

			moves[i].Score = score;

			if (score > bestScore)
			{
				if (score >= beta)
				{
					if (moveIsQuiet)
					{
						// TODO: Mate killers
						killerMoves[plyFromRoot, 1] = killerMoves[plyFromRoot, 0];
						killerMoves[plyFromRoot, 0] = moves[i];
					}

					var (from, to) = moves[i].Move;
					history.Set(position.SideToMove, from, to, depth * depth);

					numCutoffs++;
					return score;
				}

				// Found a new best move in this position
				bestScore = score;
				pvMoves[maxDepth, plyFromRoot] = moves[i];
			}
		}

		Bound type = GetBoundType(alphaOrig, beta, bestScore);
		Table.Store(position.State.Key, bestScore, type, (sbyte)depth, pvMoves[maxDepth, plyFromRoot], 0);
		//StoreTranspositionTable(position, depth, bestScore, alphaOrig, beta);

		return bestScore;
	}

	int ZWSearch(IPosition position, int beta, int depth, int plyFromRoot)
	{
		if (abortSearch)
			return SearchInvalid;

		if (position.IsThreeFoldRepetition())
			return plyFromRoot;

		var entry = Cluster.DefaultEntry;
		if (Table.Probe(position.State.Key, ref entry) && entry.Depth >= depth)
		{
			//pvMoves[maxDepth, plyFromRoot] = entry.Move;
			if (entry.Type == Bound.Beta && entry.Value < beta)
				beta = entry.Value; // Update beta with the upper bound value
		}

		// alpha is beta - 1
		// this is either a cut- or all-node
		if (depth <= 0)
		{
			long beginTime = timer.ElapsedMilliseconds;
			int eval = QuiescenceSearch(position, plyFromRoot, beta - 1, beta);
			quiescenceTime += timer.ElapsedMilliseconds - beginTime;

			return eval;
		}

		var moves = GenerateAndOrderMoves(position, plyFromRoot);

		if (moves.Length == 0)
		{
			if (position.InCheck)
			{
				int mateScore = PositionEvaluator.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			return plyFromRoot;
		}

		for (int i = 0; i < moves.Length; i++)
		{
			bool moveIsQuiet = !position.IsCaptureOrPromotion(moves[i]);

			int newDepth = depth - 1;
			if (depth > 3 && moveIsQuiet)
				newDepth--;

			numNodes++;
			position.MakeMove(moves[i], null);
			int score = -ZWSearch(position, 1 - beta, newDepth, plyFromRoot + 1);
			position.TakeMove(moves[i]);

			// fail-hard beta-cutoff
			if (score >= beta)
			{
				if (moveIsQuiet)
				{
					//TODO: Mate killers
					killerMoves[plyFromRoot, 1] = killerMoves[plyFromRoot, 0];
					killerMoves[plyFromRoot, 0] = moves[i];
				}

				var (from, to) = moves[i].Move;
				history.Set(position.SideToMove, from, to, plyFromRoot * plyFromRoot);

				numCutoffs++;
				return beta;
			}
		}

		Table.Store(position.State.Key, beta - 1, Bound.Beta, (sbyte)depth, pvMoves[maxDepth, plyFromRoot], 0);
		return beta - 1; // fail-hard, return alpha (bestScore)
	}

	/// <summary>
	/// Searches capture moves until a 'quiet' position is reached.
	/// </summary>
	/// <param name="plyFromRoot">The ply count from the root node.</param>
	/// <param name="alpha">The highest (best) evaluation that the maximizing player (this engine) has found so far.</param>
	/// <param name="beta">The lowest (best) evaluation that the minimizing player (opponent) has found so far.</param>
	/// <returns>The evaluation score for the current position.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	int QuiescenceSearch(IPosition position, int plyFromRoot, int alpha, int beta)
	{
		if (abortSearch)
			return SearchInvalid;

		int alphaOrig = alpha;

		//if (ProbeTranspositionTable(position.State.Key, 0, ref alpha, ref beta, out TranspositionTableEntry entry))
		//	return entry.Value;

		if (IsTerminalNode(position, plyFromRoot, ref alpha, ref beta, out int value, true, true, false))
			return value;

		// A player isn't forced to make a capture (typically), so see what the evaluation is without capturing anything.
		// This prevents situations where a player only has bad captures available from being evaluated as bad,
		// when the player might have good non-capture moves available.
		int standPat = PositionEvaluator.Evaluate(position);

		if (standPat >= beta)
		{
			numCutoffs++;
			return beta;
		}

		if (standPat > alpha)
			alpha = standPat;

		var moves = GenerateAndOrderMoves(position, plyFromRoot);

		if (moves.Length == 0)
		{
			if (position.InCheck)
			{
				int mateScore = PositionEvaluator.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			return plyFromRoot; // Return a little bit of score, trying to prolong the draw.
		}

		float phase = PositionEvaluator.CalculatePhase(position);

		foreach (var move in moves)
		{
			if (!position.IsCaptureOrPromotion(move))
				continue;

			if (phase < 0.7f)
			{
				var capturePieceType = position.GetPieceType(move.Move.ToSquare());
				if (standPat + DeltaValue + PositionEvaluator.GetPieceValue(capturePieceType) < alpha)
					return alpha;
			}

			numNodes++;
			position.MakeMove(move, new());
			int score = -QuiescenceSearch(position, plyFromRoot + 1, -beta, -alpha);
			position.TakeMove(move);

			if (score == -SearchInvalid)
				return SearchInvalid;

			if (score >= beta)
			{
				numCutoffs++;
				return beta;
			}

			if (score > alpha)
			{
				alpha = score;
			}

		}

		//Bound type = GetBoundType(alphaOrig, beta, alpha);
		//Table.Store(position.State.Key, alpha, type, 0, pvMoves[maxDepth, plyFromRoot], 0);

		//StoreTranspositionTable(position, 0, alpha, alphaOrig, beta);
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

	/// <summary>
	/// Stores an entry in the transposition table.
	/// </summary>
	/// <param name="depth">The ply of the search.</param>
	/// <param name="evaluation">The evaluation score for the position.</param>
	/// <param name="alphaOrig">The original alpha value.</param>
	/// <param name="beta">The beta value.</param>
	private void StoreTranspositionTable(IPosition position, int depth, int evaluation, int alphaOrig, int beta)
	{
		//var entry = new TranspositionTableEntry
		//{
		//	Value = evaluation,
		//	Key32 = position.State.Key.UpperKey
		//};

		Bound type = Bound.Exact;
		if (evaluation <= alphaOrig)
			type = Bound.Beta;
		else if (evaluation >= beta)
			type = Bound.Alpha;

		//entry.Depth = (sbyte)depth;
		//entry.Move = pvMoves[depth];
		//entry.Type = type;

		Table.Store(position.State.Key, evaluation, type, (sbyte)depth, pvMoves[maxDepth, maxDepth - depth], 0);
		//Table.Save(position.State.Key, entry);
	}

	private Bound GetBoundType(int alpha, int beta, int score)
	{
		Bound type = Bound.Exact;
		if (score <= alpha)
			type = Bound.Beta;
		else if (score >= beta)
			type = Bound.Alpha;

		return type;
	}

	/// <summary>
	/// Checks the end conditions of the search to see if the search can be terminated early.
	/// </summary>
	/// <param name="plyFromRoot">The ply of the search from the root.</param>
	/// <param name="alpha">The alpha value.</param>
	/// <param name="beta">The beta value.</param>
	/// <param name="value">The output value for the search.</param>
	/// <returns>True if the search can be terminated, false otherwise.</returns>
	private bool IsTerminalNode(IPosition position, int plyFromRoot, ref int alpha, ref int beta, out int value, bool disableStalemate, bool disableRepetition, bool disableMaterial)
	{
		value = 0;

		if (position.IsDraw(disableStalemate, disableRepetition, disableMaterial))
		{
			value = plyFromRoot;
			return true;
		}

		// Skip this position if a mating sequence has already been found earlier in
		// the search, which would be shorter than any mate we could find from here.
		// This is done by observing that bestScore can't possibly be worse (and likewise
		// worstScore can't  possibly be better) than being mated in the current position.
		alpha = Max(alpha, -PositionEvaluator.immediateMateScore + plyFromRoot);
		beta = Min(beta, PositionEvaluator.immediateMateScore - plyFromRoot);
		if (alpha >= beta)
		{
			value = alpha;
			return true;
		}

		return false;
	}

	public void CancelSearch()
	{
		abortSearch = true;
	}

	void InitDebugInfo()
	{
		numNodes = 0;
		numCutoffs = 0;
		numTranspositions = 0;
		normalSearchTime = 0;
		quiescenceTime = 0;
	}

	/// <summary>
	/// Generates and orders the moves for the current position.
	/// </summary>
	/// <param name="plyFromRoot">The ply of the search from the root.</param>
	/// <param name="type">The type of move generation to perform.</param>
	/// <returns>The array of moves, ordered by best to worst.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	ValMove[] GenerateAndOrderMoves(IPosition position, int plyFromRoot, MoveGenerationType type = MoveGenerationType.Legal)
	{
		MoveList moves = position.GenerateMoves(type);
		float phase = PositionEvaluator.CalculatePhase(position);
		var entry = Cluster.DefaultEntry;
		Table.Probe(position.State.Key, ref entry);

		return moves.OrderByDescending(valMove =>
		{
			int moveScoreGuess = 0;

			if (pvMoves[maxDepth - 1, plyFromRoot] == valMove)
				return 10000000;

			if (valMove == killerMoves[plyFromRoot, 0])
				return 10000;
			if (valMove == killerMoves[plyFromRoot, 1])
				return 5000;

			
			if (valMove.Move == entry.Move)
			{
				//Console.WriteLine("TT");
				return 100000;
			}

			var (from, to, type) = valMove.Move;

			var movePieceType = position.GetPiece(from).Type();
			var capturePieceType = position.GetPiece(to).Type();

			bool isCapture = capturePieceType != PieceTypes.NoPieceType;

			// MVV/LVA
			if (isCapture)
			{
				moveScoreGuess += 10 * PositionEvaluator.GetPieceValue(capturePieceType) - 5 * PositionEvaluator.GetPieceValue(movePieceType);
				moveScoreGuess = (int)(moveScoreGuess * (phase + 1));
			}
			else
			{
				moveScoreGuess += history.Retrieve(position.SideToMove, from, to) * 10;
			}

			if (position.GivesCheck(valMove))
				moveScoreGuess += (int)(PositionEvaluator.pawnValue * (phase * 5 + 1));

			if (movePieceType == PieceTypes.Pawn)
			{
				moveScoreGuess += (int)(PositionEvaluator.pawnValue * Pow(phase + 1, 4));

				if (type == MoveTypes.Promotion)
				{
					var promotionType = valMove.Move.PromotedPieceType();
					moveScoreGuess += PositionEvaluator.GetPieceValue(promotionType) * 5;
				}
			}
			else
			{
				// If the target square is attacked by opponent pawn
				if (position.AttackedByPawn(to, ~position.SideToMove))
				{
					moveScoreGuess -= 5 * PositionEvaluator.GetPieceValue(movePieceType) + 5 * PositionEvaluator.pawnValue;
				}
			}

			return moveScoreGuess;
		}).ToArray();
	}

	void StartMonitor()
	{
		// Start a task that periodically checks if the search should be aborted.
		var t = Task.Factory.StartNew(async () =>
		{
			while (controller.CanSearchDeeper(maxDepth, numNodes, true))
			{
				await Task.Delay(200);
			}
			abortSearch = true;
		});
	}

	//IEnumerator<ValMove> GenerateMoves(int plyFromRoot, MoveGenerationType type = MoveGenerationType.Legal)
	//{
	//	if (!pvMoves[plyFromRoot].Move.IsNullMove())
	//	{
	//		yield return pvMoves[plyFromRoot];
	//	}
	//	else
	//	{
	//		MoveList moves = position.GenerateMoves(type);
	//		var ordered = moveOrdering.OrderMoves(moves, position, pvMoves, plyFromRoot);

	//		foreach (var move in ordered)
	//			yield return move;
	//	}
	//}

	//int PVSearch(int bestScore, int worstScore, int depth, int plyFromRoot)
	//{
	//	numNodes++;

	//	if (abortSearch)
	//		return SearchInvalid;

	//	if (ProbeTranspositionTable(depth, ref bestScore, ref worstScore, out int entryValue))
	//		return entryValue;

	//	if (CheckEndConditions(plyFromRoot, ref bestScore, ref worstScore, out int value))
	//		return value;

	//	if (depth <= 0)
	//		return QuiescenceSearch(worstScore - 1, worstScore, plyFromRoot);

	//	bool bSearchPV = true;

	//	var moves = GenerateAndOrderMoves(plyFromRoot);
	//	for (int i = 0; i < moves.Length; i++)
	//	{
	//		Engine.positionHistory.Push(position.State.Key.Key);
	//		position.MakeMove(moves[i], null);

	//		int score;
	//		if (bSearchPV)
	//		{
	//			score = -PVSearch(-worstScore, -bestScore, depth - 1, plyFromRoot + 1);
	//		}
	//		else
	//		{
	//			score = -ZwSearch(-bestScore, depth - 1, plyFromRoot + 1);
	//			if (score > bestScore)
	//				score = -PVSearch(-worstScore, -bestScore, depth - 1, plyFromRoot + 1);
	//		}

	//		Engine.positionHistory.Pop();
	//		position.TakeMove(moves[i]);

	//		if (score == -SearchInvalid)
	//			return SearchInvalid;

	//		moves[i].Score = score;

	//		// fail-hard beta-cutoff
	//		if (score >= worstScore)
	//		{
	//			numCutoffs++;
	//			return worstScore;
	//		}

	//		if (score > bestScore)
	//		{
	//			bestScore = score;
	//			pvMoves[plyFromRoot] = moves[i];
	//		}

	//		bSearchPV = false;
	//	}

	//	return bestScore;
	//}

	// fail-hard zero window search, returns either worstScore-1 or worstScore
	

}