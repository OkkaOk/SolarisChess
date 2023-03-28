
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

namespace SolarisChess;

/// <summary>
/// Class that performs a search for the best move in a given position.
/// </summary>
public class Search
{
	const int transpositionTableSize = 64000;
	const int SearchInvalid = 12345;
	const int AspirationWindowSize = 50;

	public event Action<ValMove>? OnSearchComplete;
	public event Action<int, int, long, int, int, ValMove[]>? OnInfo;

	private ValMove bestMove = ValMove.Empty;
	private ValMove[] pvMoves = new ValMove[Values.MAX_PLY]; 
	private ValMove[,] killerMoves = new ValMove[Values.MAX_PLY, 2]; 

	private bool abortSearch;
	private int maxDepth;

	TranspositionTable Table => Engine.Table;
	readonly TimeControl controller = Engine.controller;

	readonly Stopwatch timer = new();
	long negamaxTime = 0;
	long quiescenceTime = 0;

	IGame Game => Engine.Game;
	IPosition position => Game.Pos;

	// Diagnostics
	int numNodes;
	int numCutoffs;
	int numTranspositions;

	public Search()
	{

	}

	/// <summary>
	/// Searches for the best move by performing an iterative deepening search with alpha-beta pruning.
	/// <br>Raises the OnSearchComplete event when finished with the best move found.</br>
	/// </summary>
	public void IterativeDeepeningSearch()
	{
		// Start the timer and initialize diagnostic variables.
		timer.Start();
		abortSearch = false;
		maxDepth = 1;
		InitDebugInfo();

		bestMove = ValMove.Empty;
		pvMoves = new ValMove[Values.MAX_PLY];
		killerMoves = new ValMove[Values.MAX_PLY, 2];

		Stopwatch watch = Stopwatch.StartNew();

		Engine.Log($"Search scheduled to take {(controller.isInfinite ? "as long as needed" : controller.AllocatedTimePerMove + "ms")}!");

		// Start a task that periodically checks if the search should be aborted.
		Task.Factory.StartNew(async () =>
		{
			while (controller.CanSearchDeeper(maxDepth, numNodes, true))
			{
				await Task.Delay(200);
			}
			abortSearch = true;
		});

		while (controller.CanSearchDeeper(maxDepth++, numNodes))
		{
			Table.NewSearch();
			controller.StartInterval();

			long beginTime = timer.ElapsedMilliseconds;
			int aspirationWindow = AspirationWindowSize;
			int alpha = bestMove.Score - aspirationWindow;
			int beta = bestMove.Score + aspirationWindow;

			//Negascout(searchDepth, 0, PositionEvaluator.negativeInfinity, PositionEvaluator.positiveInfinity, false);
			int score = Negascout(0, alpha, beta, false);

			while (score <= alpha || score >= beta)
			{
				alpha = Max(PositionEvaluator.negativeInfinity, alpha - aspirationWindow);
				beta = Min(PositionEvaluator.positiveInfinity, beta + aspirationWindow);
				score = Negascout(0, alpha, beta, false);
			}

			negamaxTime += timer.ElapsedMilliseconds - beginTime;

			if (abortSearch)
				break;

			// Update diagnostics
			Engine.OnInfo(maxDepth, pvMoves[0].Score, numNodes, controller.Elapsed, Table.Fullness() * 10, pvMoves);
			//OnInfo?.Invoke(searchDepth, pvMoves[0].Score, numNodes, controller.Elapsed, Table.Fullness() * 10, pvMoves);

			//if (searchDepth > 5 && pvMoves[0].Score > bestMove.Score + 5)
			//{
			//	bestMove = pvMoves[0];
			//	break;
			//}

			bestMove = pvMoves[0];

			// Exit search if found a mate
			if (PositionEvaluator.IsMateScore(bestMove.Score))
				break;
		}

		// If no move has been found, choose the first generated move.
		if (bestMove == ValMove.Empty)
		{
			bestMove = GenerateAndOrderMoves(0)[0];
			Engine.Log("-------------Had to choose a stupid move-------------");
		}

		// Log diagnostic information and invoke the OnSearchComplete event.
		watch.Stop();
		Engine.Log("Time spent on this move: " + watch.ElapsedMilliseconds);
		Engine.Log("Total nodes visited: " + numNodes);
		Engine.Log("Total branches cut off: " + numCutoffs);
		Engine.Log("Total tt hits: " + numTranspositions);
		Engine.Log("Negascout time: " + (negamaxTime - quiescenceTime));
		Engine.Log("Quiescence time: " + quiescenceTime);
		OnSearchComplete?.Invoke(bestMove);
	}


	/// <summary>
	/// Performs negascout search with alpha-beta pruning to determine the best move.
	/// </summary>
	/// <param name="depth">The remaining depth to search.</param>
	/// <param name="plyFromRoot">The ply count from the root node.</param>
	/// <param name="bestScore">The highest (best) evaluation that the maximizing player (this engine) has found so far.</param>
	/// <param name="worstScore">The lowest (best) evaluation that the minimizing player (opponent) has found so far.</param>
	/// <param name="lastMoveWasCapture">Indicates whether the last move made was a capture.</param>
	/// <returns>The evaluation score for the current position.</returns>
	int Negascout(int depth, int bestScore, int worstScore, bool lastMoveWasCapture)
	{
		if (abortSearch)
			return SearchInvalid;

		numNodes++;

		int alphaOrig = bestScore;

		if (ProbeTranspositionTable(depth, ref bestScore, ref worstScore, out int entryValue))
			return entryValue;

		if (CheckEndConditions(depth, ref bestScore, ref worstScore, out int value))
			return value;

		if (depth >= maxDepth)
		{
			long beginTime = timer.ElapsedMilliseconds;
			int eval = QuiescenceSearch(depth, bestScore, worstScore);
			//int eval = evaluation.Evaluate();
			quiescenceTime += timer.ElapsedMilliseconds - beginTime;
			return eval;
		}

		if (position.State.PliesFromNull != 0 && maxDepth - depth > 2 && !position.InCheck && !lastMoveWasCapture)
		{
			position.MakeNullMove(new());
			int nullScore = -Negascout(depth + 3, -worstScore, -bestScore, false);
			position.TakeNullMove();
			if (nullScore >= worstScore)
			{
				//Console.WriteLine("Used null move to cut off a branch!");
				return nullScore;
			}
		}

		int score = PositionEvaluator.negativeInfinity;

		var moves = GenerateAndOrderMoves(depth);

		if (moves.Length == 0)
		{
			if (position.InCheck)
			{
				int mateScore = PositionEvaluator.immediateMateScore - depth;
				return -mateScore;
			}
			// Return a little bit of score, trying to prolong the draw.
			return depth; 
		}

		for (int i = 0; i < moves.Length; i++)
		{
			Engine.positionHistory.Push(position.State.Key.Key);
			position.MakeMove(moves[i], null);

			// LMR
			int newDepth = depth + 1;
			if (i > 4 && !lastMoveWasCapture)
				newDepth++;

			bool moveIsCapture = position.IsCapture(moves[i]);
			if (i == 0)
			{
				score = -Negascout(newDepth, -worstScore, -bestScore, moveIsCapture);
			}
			else
			{
				// zero-width window
				score = -Negascout(newDepth, -bestScore - 1, -bestScore, moveIsCapture);

				if (score > bestScore && score < worstScore)
				{
					// full-width search
					score = -Negascout(newDepth, -worstScore, -score, moveIsCapture);
				}
			}

			Engine.positionHistory.Pop();
			position.TakeMove(moves[i]);

			// Search was stopped because the allocated time ran out.
			if (score == -SearchInvalid)
				return SearchInvalid;

			moves[i].Score = score;

			// Found a new best move in this position
			if (score > bestScore)
			{
				bestScore = score;
				pvMoves[depth] = moves[i];
				//while (!pvMoves[plyFromRoot + 1].Move.IsNullMove())
				//	pvMoves[plyFromRoot + 1] = ValMove.Empty;
			}

			// Move was *too* good, so opponent won't allow this position to be reached
			if (bestScore >= worstScore)
			{
				if (!position.IsCaptureOrPromotion(moves[i]))
				{
					killerMoves[depth, 1] = killerMoves[depth, 0];
					killerMoves[depth, 0] = moves[i];
				}

				numCutoffs++;
				break;
			}
		}

		StoreTranspositionTable(depth, score, alphaOrig, worstScore);

		return score;
	}

	/// <summary>
	/// Searches capture moves until a 'quiet' position is reached.
	/// </summary>
	/// <param name="depth">The depth from the root node.</param>
	/// <param name="bestScore">The highest (best) evaluation that the maximizing player (this engine) has found so far.</param>
	/// <param name="worstScore">The lowest (best) evaluation that the minimizing player (opponent) has found so far.</param>
	/// <returns>The evaluation score for the current position.</returns>
	int QuiescenceSearch(int depth, int bestScore, int worstScore)
	{
		if (abortSearch)
			return SearchInvalid;

		// No need to check for repetition.
		if (position.IsInsufficientMaterial())
			return 0;

		// A player isn't forced to make a capture (typically), so see what the evaluation is without capturing anything.
		// This prevents situations where a player only has bad captures available from being evaluated as bad,
		// when the player might have good non-capture moves available.
		int eval = PositionEvaluator.Evaluate(position);

		if (eval >= worstScore)
			return worstScore;

		if (eval > bestScore)
			bestScore = eval;

		var moves = GenerateAndOrderMoves(depth);

		if (moves.Length == 0)
		{
			if (position.InCheck)
			{
				int mateScore = PositionEvaluator.immediateMateScore - depth;
				return -mateScore;
			}
			return depth; // Return a little bit of score, trying to prolong the draw.
		}

		foreach (var move in moves)
		{
			if (!position.IsCaptureOrPromotion(move))
				continue;

			numNodes++;
			position.MakeMove(move, new());
			eval = -QuiescenceSearch(depth + 1, -worstScore, -bestScore);
			position.TakeMove(move);

			if (eval >= worstScore)
			{
				numCutoffs++;
				return worstScore;
			}

			if (eval > bestScore)
			{
				bestScore = eval;
			}

		}

		return bestScore;
	}

	private bool ProbeTranspositionTable(int depth, ref int bestScore, ref int worstScore, out int entryValue)
	{
		var entry = new TranspositionTableEntry();
		entryValue = 0;

		if (Table.Probe(position.State.Key.Key, ref entry) && entry.Depth >= depth)
		{
			numTranspositions++;

			if (entry.Type == Bound.Exact)
			{
				entryValue = entry.Value;
				return true;
			}
			else if (entry.Type == Bound.Beta)
			{
				bestScore = Max(bestScore, entry.Value);
			}
			else if (entry.Type == Bound.Alpha)
			{
				worstScore = Min(worstScore, entry.Value);
			}

			if (bestScore >= worstScore)
			{
				entryValue = entry.Value;
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Stores an entry in the transposition table.
	/// </summary>
	/// <param name="depth">The depth of the search.</param>
	/// <param name="depth">The ply of the search.</param>
	/// <param name="evaluation">The evaluation score for the position.</param>
	/// <param name="alphaOrig">The original alpha value.</param>
	/// <param name="worstScore">The beta value.</param>
	private void StoreTranspositionTable(int depth, int evaluation, int alphaOrig, int worstScore)
	{
		var entry = new TranspositionTableEntry
		{
			Value = evaluation,
			Key32 = position.State.Key.LowerKey
		};

		if (evaluation <= alphaOrig)
			entry.Type = Bound.Alpha;
		else if (evaluation >= worstScore)
			entry.Type = Bound.Beta;
		else
			entry.Type = Bound.Exact;

		entry.Depth = (sbyte)depth;
		entry.Move = pvMoves[depth];

		Table.Save(position.State.Key, entry);
	}

	/// <summary>
	/// Checks the end conditions of the search to see if the search can be terminated early.
	/// </summary>
	/// <param name="depth">The depth of the search from the root.</param>
	/// <param name="bestScore">The alpha value.</param>
	/// <param name="worstScore">The beta value.</param>
	/// <param name="value">The output value for the search.</param>
	/// <returns>True if the search can be terminated, false otherwise.</returns>
	private bool CheckEndConditions(int depth, ref int bestScore, ref int worstScore, out int value)
	{
		value = 0;

		if (depth >= maxDepth)
			return false;

		//position.IsRepetition ||
		if (position.IsInsufficientMaterial())
		{
			value = depth;
			return true;
		}

		// Skip this position if a mating sequence has already been found earlier in
		// the search, which would be shorter than any mate we could find from here.
		// This is done by observing that bestScore can't possibly be worse (and likewise
		// worstScore can't  possibly be better) than being mated in the current position.
		bestScore = Max(bestScore, -PositionEvaluator.immediateMateScore + depth);
		worstScore = Min(worstScore, PositionEvaluator.immediateMateScore - depth);
		if (bestScore >= worstScore)
		{
			value = bestScore;
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
		negamaxTime = 0;
		quiescenceTime = 0;
	}

	/// <summary>
	/// Generates and orders the moves for the current position.
	/// </summary>
	/// <param name="depth">The ply of the search from the root.</param>
	/// <param name="type">The type of move generation to perform.</param>
	/// <returns>The array of moves, ordered by best to worst.</returns>
	ValMove[] GenerateAndOrderMoves(int depth, MoveGenerationType type = MoveGenerationType.Legal)
	{
		MoveList moves = position.GenerateMoves(type);

		return moves.OrderByDescending(valMove =>
		{
			int moveScoreGuess = 0;

			if (pvMoves[depth].Equals(valMove))
			{
				valMove.Score = pvMoves[depth].Score;
				return 10000000;
			}

			if (valMove == killerMoves[depth, 0])
				return 1000000;
			else if (valMove == killerMoves[depth, 1])
				return 100000;

			var (from, to, type) = valMove.Move;

			var movePieceType = position.GetPiece(from).Type();
			var capturePieceType = position.GetPiece(to).Type();

			// MVV/LVA
			if (position.IsCapture(valMove))
				moveScoreGuess += 10 * PositionEvaluator.GetPieceValue(capturePieceType) - 10 * PositionEvaluator.GetPieceValue(movePieceType);

			//if (position.GivesCheck(extMove))
			//	moveScoreGuess += Evaluation.pawnValue;

			if (type == MoveTypes.Promotion)
			{
				var promotionType = valMove.Move.PromotedPieceType();
				moveScoreGuess += PositionEvaluator.GetPieceValue(promotionType) * 2;
			}

			// If the target square is attacked by opponent pawn
			//if (position.AttackedByPawn(to, ~position.SideToMove))
			//{
			//	moveScoreGuess -= Evaluation.GetPieceValue(movePieceType) + Evaluation.pawnValue;
			//}

			return moveScoreGuess;
		}).ToArray();
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

	int PVSearch(int bestScore, int worstScore, int depth, int plyFromRoot)
	{
		numNodes++;

		if (abortSearch)
			return SearchInvalid;

		if (ProbeTranspositionTable(depth, ref bestScore, ref worstScore, out int entryValue))
			return entryValue;

		if (CheckEndConditions(plyFromRoot, ref bestScore, ref worstScore, out int value))
			return value;

		if (depth <= 0)
			return QuiescenceSearch(worstScore - 1, worstScore, plyFromRoot);

		bool bSearchPV = true;

		var moves = GenerateAndOrderMoves(plyFromRoot);
		for (int i = 0; i < moves.Length; i++)
		{
			Engine.positionHistory.Push(position.State.Key.Key);
			position.MakeMove(moves[i], null);

			int score;
			if (bSearchPV)
			{
				score = -PVSearch(-worstScore, -bestScore, depth - 1, plyFromRoot + 1);
			}
			else
			{
				score = -ZwSearch(-bestScore, depth - 1, plyFromRoot + 1);
				if (score > bestScore)
					score = -PVSearch(-worstScore, -bestScore, depth - 1, plyFromRoot + 1);
			}

			Engine.positionHistory.Pop();
			position.TakeMove(moves[i]);

			if (score == -SearchInvalid)
				return SearchInvalid;

			moves[i].Score = score;

			// fail-hard beta-cutoff
			if (score >= worstScore)
			{
				numCutoffs++;
				return worstScore;
			}

			if (score > bestScore)
			{
				bestScore = score;
				pvMoves[plyFromRoot] = moves[i];
			}

			bSearchPV = false;
		}

		return bestScore;
	}

	// fail-hard zero window search, returns either worstScore-1 or worstScore
	int ZwSearch(int worstScore, int depth, int plyFromRoot)
	{
		// bestScore is worstScore - 1
		// this is either a cut- or all-node
		if (depth <= 0)
			return QuiescenceSearch(worstScore - 1, worstScore, plyFromRoot);

		var moves = GenerateAndOrderMoves(plyFromRoot);
		foreach (var move in moves)
		{
			Engine.positionHistory.Push(position.State.Key.Key);
			position.MakeMove(move, null);

			int score = -ZwSearch(1 - worstScore, depth - 1, plyFromRoot + 1);

			Engine.positionHistory.Pop();
			position.TakeMove(move);

			// fail-hard beta-cutoff
			if (score >= worstScore)
			{
				numCutoffs++;
				return worstScore;
			}
		}

		return worstScore - 1; // fail-hard, return alpha (bestScore)
	}

}