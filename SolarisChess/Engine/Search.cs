
using ChessLib;
using ChessLib.MoveGeneration;
using ChessLib.Notation;
using ChessLib.Types;
using ChessLib.Enums;
using ChessLib.Hash;
using System;
using System.Diagnostics;
using static System.Math;
using ChessLib.Hash.Tables.Transposition;

namespace SolarisChess;

public class Search
{
	const int transpositionTableSize = 64000;
	const int SearchInvalid = 12345;

	public event Action<ExtMove>? OnSearchComplete;
	public event Action<int, int, long, int>? OnInfo;

	private ExtMove bestMoveThisIteration = ExtMove.Empty;
	private ExtMove bestMove = ExtMove.Empty;

	private bool abortSearch;

	TranspositionTable Table => Game.Table;
	MoveOrdering moveOrdering;
	TimeControl time;
	Evaluation evaluation;

	Stopwatch timer = new Stopwatch();
	long negamaxTime = 0;
	long quiescenceTime = 0;

	IGame game;
	IPosition position => game.Pos;

	// Diagnostics
	int numNodes;
	int numQNodes;
	int numCutoffs;
	int numTranspositions;
	int furthestNode;

	public Search(IGame game, TimeControl time)
	{
		this.game = game;
		this.time = time;

		//Table.SetSize(20);
		moveOrdering = new MoveOrdering();
		evaluation = new Evaluation(position);
	}

	// Iterative deepening search
	public void IterativeDeepeningSearch(int maxDepth)
	{
		timer.Start();
		negamaxTime = 0;
		quiescenceTime = 0;

		InitDebugInfo();
		abortSearch = false;

		bestMoveThisIteration = bestMove = Move.EmptyMove;
		Stopwatch watch = Stopwatch.StartNew();

		Engine.Log($"Search scheduled to take {time.AllocatedTimePerMove}ms!");

		int searchDepth = 0;

		Task.Factory.StartNew(async () =>
		{
			while (time.CanSearchDeeper(searchDepth, numNodes))
			{
				await Task.Delay(200);
			}
			abortSearch = true;
		});

		while (time.CanSearchDeeper(searchDepth, numNodes))
		{
			searchDepth++;

			Table.NewSearch();
			time.StartInterval();

			long beginTime = timer.ElapsedMilliseconds;
			Negamax(searchDepth, 0, Evaluation.negativeInfinity, Evaluation.positiveInfinity);
			negamaxTime += timer.ElapsedMilliseconds - beginTime;

			if (abortSearch)
				break;

			bestMove = bestMoveThisIteration;

			// Update diagnostics
			OnInfo?.Invoke(searchDepth, bestMove.Score, numNodes, time.Elapsed);

			// Exit search if found a mate
			if (Evaluation.IsMateScore(bestMove.Score))
				break;
		}

		if (bestMove == ExtMove.Empty)
		{
			var moves = position.GenerateMoves().ToArray();
			bestMove = moveOrdering.OrderMoves(moves, position)[0];
			Engine.Log("-------------Had to choose a stupid move-------------");
		}

		watch.Stop();
		Engine.Log("Time spent on this move: " + watch.ElapsedMilliseconds);
		Engine.Log("Total branches cut off: " + numCutoffs);
		Engine.Log("Total transpositions: " + numTranspositions);
		Engine.Log("Current Zobrist: " + position.State.Key.Key);
		Engine.Log("Negamax time: " + (negamaxTime - quiescenceTime));
		Engine.Log("Quiescence time: " + quiescenceTime);
		if (position.HasGameCycle(position.Ply)) Engine.Log("Apparently there is a cuckoo cycle");
		OnSearchComplete?.Invoke(bestMove);
	}


	// Alpha is the highest (best) evaluation that the maximizing player (this engine) has found so far
	// Beta is the lowest (best) evaluation that the minimizing player (opponent) has found so far
	// In a more general sense, "alpha" could be thought of as the lower bound on the best possible evaluation found so far,
	// and "beta" could be thought of as the upper bound on the worst possible evaluation found so far.
	int Negamax(int depth, int plyFromRoot, int alpha, int beta)
	{
		if (abortSearch)
			return SearchInvalid;

		var Hash = position.State.Key;
		
		if (plyFromRoot > 0)
		{
			//if (position.zobristKeyHistory.Contains(Hash))
			//	return 0;
			//if (position.IsDraw())
			//	return 0;
			//if (position.IsInsufficientMaterial())
			//	return 0;

			//if (position.IsThreeFoldRepetition())
			//	return 0;

			// Skip this position if a mating sequence has already been found earlier in
			// the search, which would be shorter than any mate we could find from here.
			// This is done by observing that alpha can't possibly be worse (and likewise
			// beta can't  possibly be better) than being mated in the current position.
			alpha = Max(alpha, -Evaluation.immediateMateScore + plyFromRoot);
			beta = Min(beta, Evaluation.immediateMateScore - plyFromRoot);
			if (alpha >= beta)
			{
				return alpha;
			}
		}

		int alphaOrig = alpha;
		if (Table.Probe(Hash, out var entry) && entry.Depth >= depth)
		{
			numTranspositions++;

			if (entry.Type == Bound.Exact)
				return entry.StaticValue;
			else if (entry.Type == Bound.Beta)
				alpha = Max(alpha, entry.StaticValue);
			else if (entry.Type == Bound.Alpha)
				beta = Min(beta, entry.StaticValue);

			if (alpha >= beta)
				return entry.StaticValue;
		}

		if (depth <= 0)
		{
			long beginTime = timer.ElapsedMilliseconds;
			int eval = QuiescenceSearch(alpha, beta, plyFromRoot);
			//int eval = evaluation.Evaluate();
			quiescenceTime += timer.ElapsedMilliseconds - beginTime;
			return eval;
		}

		var moves = position.GenerateMoves(MoveGenerationType.Legal).ToArray();
		moves = moveOrdering.OrderMoves(moves, position);

		if (moves.Length == 0)
		{
			if (position.InCheck)
			{
				int mateScore = Evaluation.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			return plyFromRoot; // Return a little bit of score, trying to prolong the draw.
		}

		int value = Evaluation.negativeInfinity;

		for (int i = 0; i < moves.Length; i++)
		{
			numNodes++;

			// Take the evaluation on opponent's turn and negate it, because what's bad for them is good for us and vice versa
			position.MakeMove(moves[i], new State());
			value = -Negamax(depth - 1, plyFromRoot + 1, -beta, -alpha);
			position.TakeMove(moves[i]);

			if (value == SearchInvalid || value == -SearchInvalid)
			{
				Console.WriteLine("info string INVALID");
				return SearchInvalid;
			}


			moves[i].Score = value;
			// Found a new best move in this position
			if (value > alpha)
			{
				alpha = value;
				if (plyFromRoot == 0)
				{
					bestMoveThisIteration = moves[i];
				}
			}

			// Move was *too* good, so opponent won't allow this position to be reached
			if (alpha >= beta)
			{
				numCutoffs++;
				break;
				//return beta; // Beta-cutoff
			}
		}

		entry.StaticValue = value;
		entry.Key32 = Hash.LowerKey;

		if (value <= alphaOrig)
			entry.Type = Bound.Alpha;
		else if (value >= beta)
			entry.Type = Bound.Beta;
		else
			entry.Type = Bound.Exact;

		entry.Depth = (sbyte)depth;
		Table.Save(Hash, entry);

		return value;
	}

	// Search capture moves until a 'quiet' position is reached.
	int QuiescenceSearch(int alpha, int beta, int plyFromRoot)
	{
		if (abortSearch)
			return SearchInvalid;

		// A player isn't forced to make a capture (typically), so see what the evaluation is without capturing anything.
		// This prevents situations where a player only has bad captures available from being evaluated as bad,
		// when the player might have good non-capture moves available.
		int eval = evaluation.Evaluate();

		if (eval >= beta)
			return beta;

		if (alpha < eval)
			alpha = eval;

		var moves = position.GenerateMoves(MoveGenerationType.Legal).ToArray();
		moves = moveOrdering.OrderMoves(moves, position);

		if (moves.Length == 0)
		{
			if (position.InCheck)
			{
				int mateScore = Evaluation.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			return plyFromRoot; // Return a little bit of score, trying to prolong the draw.
		}

		foreach (var move in moves)
		{
			if (!position.IsCapture(move))
				continue;

			position.MakeMove(move, new State());
			eval = -QuiescenceSearch(-beta, -alpha, plyFromRoot + 1);
			position.TakeMove(move);
			numQNodes++;

			if (eval >= beta)
			{
				numCutoffs++;
				return beta;
			}

			if (eval > alpha)
			{
				alpha = eval;
			}

		}

		return alpha;
	}

	public void CancelSearch()
	{
		abortSearch = true;
	}

	void InitDebugInfo()
	{
		numNodes = 0;
		numQNodes = 0;
		numCutoffs = 0;
		numTranspositions = 0;
	}
}

public class SearchResult
{
	public Move BestMove { get; set; }
	public int BestScore { get; set; }
	public SearchStatus Status { get; set; }

	public SearchResult(int score)
	{
		BestMove = Move.EmptyMove;
		BestScore = score;
		Status = SearchStatus.Valid;
	}

	private SearchResult()
	{
		BestMove = Move.EmptyMove;
		BestScore = 0;
		Status = SearchStatus.Invalid;
	}

	public static readonly SearchResult Invalid = new();
}
public enum SearchStatus
{
	/// <summary>
	/// The search was completed and the result is valid
	/// </summary>
	Valid,

	/// <summary>
	/// The search was stopped and the result is invalid
	/// </summary>
	Invalid
}