using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Factories;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.ObjectPoolPolicies;
using Rudzoft.ChessLib.Protocol.UCI;
using Rudzoft.ChessLib.Types;
using Microsoft.Extensions.ObjectPool;
using SolarisChess;
using System.Diagnostics;
using System.Drawing;
using Microsoft.Extensions.Options;
using Rudzoft.ChessLib.Hash.Tables.Transposition;
using Rudzoft.ChessLib.Validation;
using Rudzoft.ChessLib.Enums;
using SolarisChess.Extensions;
using System.Linq;

namespace SolarisChessTest;

internal class Program
{
	//private static readonly ObjectPool<IMoveList> _moveLists = new DefaultObjectPool<IMoveList>(new MoveListPolicy());
	static Position pos;
	static Game game;

	static int nodes;

	static void Main(string[] args)
	{
		var ttConfig = new TranspositionTableConfiguration { DefaultSize = 0 };
		var options = Options.Create(ttConfig);
		var table = new TranspositionTable(options);
		var uci = new Uci();
		uci.Initialize();
		var cpu = new Cpu();
		var moveListObjectPool = new DefaultObjectPool<IMoveList>(new MoveListPolicy());
		var sp = new SearchParameters();
		var board = new Board();
		var values = new Values();
		var validator = new PositionValidator();
		pos = new Position(board, values, validator, moveListObjectPool);
		game = new Game(table, uci, cpu, sp, pos, moveListObjectPool);
		//game.NewGame("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 0");
		game.NewGame();

		Stopwatch stopwatch = Stopwatch.StartNew();
		//var res = game.Perft(6);
		//ulong res = PerftTest(6);
		//Console.WriteLine($"Result: {res}. Took {stopwatch.ElapsedMilliseconds}ms. NPS: {res*1000/(ulong)stopwatch.ElapsedMilliseconds}");

		SearchTest(7, 0, PositionEvaluator.negativeInfinity, PositionEvaluator.positiveInfinity);
		Console.WriteLine($"Result: {nodes}. Took {stopwatch.ElapsedMilliseconds}ms. NPS: {(ulong)nodes * 1000 / (ulong)stopwatch.ElapsedMilliseconds}");
	}

	static int SearchTest(int depth, int plyFromRoot, int alpha, int beta)
	{
		if (depth <= 0)
			return QuiescenceSearch(pos, depth, plyFromRoot, alpha, beta);
		//return PositionEvaluator.Evaluate(pos);
		//return 0;

		if (pos.IsThreeFoldRepetition())
			return 0;

		nodes++;

		var moves = GenerateAndOrderMoves(pos);
		//var moves = pos.GenerateMoves();
		if (moves.Count() == 0)
		{
			if (pos.InCheck)
			{
				int mateScore = PositionEvaluator.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			return 0;
		}

		var state = new State();

		int score = int.MinValue;

		foreach (var em in moves)
		{
			pos.MakeMove(em, in state);
			score = -SearchTest(depth - 1, plyFromRoot + 1, -beta, -alpha);
			pos.TakeMove(em);

			if (score > alpha)
				alpha = score;

			if (score >= beta)
				break;
		}

		return score;
	}

	static int QuiescenceSearch(IPosition position, int depth, int plyFromRoot, int alpha, int beta)
	{
		nodes++;
		if (position.IsInsufficientMaterial())
			return 0;

		int standPat = PositionEvaluator.Evaluate(position);

		if (standPat >= beta)
			return beta;

		if (standPat > alpha)
			alpha = standPat;

		var moves = GenerateAndOrderMoves(position);

		if (moves.Count() == 0)
		{
			if (position.InCheck)
			{
				int mateScore = PositionEvaluator.immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			return 0; // Return a little bit of score, trying to prolong the draw.
		}

		var state = new State();
		foreach (var move in moves)
		{
			if (!position.IsCaptureOrPromotion(move))
				continue;

			position.MakeMove(move, in state);
			int score = -QuiescenceSearch(position, depth - 1, plyFromRoot + 1, -beta, -alpha);
			position.TakeMove(move);

			if (score >= beta)
				return beta;

			if (score > alpha)
				alpha = score;
		}
		return alpha;
	}

	public static ValMove[] GenerateAndOrderMoves(IPosition position, MoveGenerationType type = MoveGenerationType.Legal)
	{
		MoveList ml = new();
		ml.Generate(position, type);

		float phase = PositionEvaluator.CalculatePhase(position);
		int mult1 = (int)(phase * 5 + 1);   // 1-6. Linear
		int mult2 = (int)Math.Pow(phase + 1, 4); // 1-16. gradual rise

		ValMove[] orderedMoves = new ValMove[ml.Length];

		for (int i = 0; i < ml.Length; i++)
		{
			ValMove valMove = ml[i];

			int moveScoreGuess = 0;

			var (from, to, moveType) = valMove.Move;

			var movePieceType = position.GetPiece(from).Type();
			var capturePieceType = position.GetPiece(to).Type();

			bool isCapture = capturePieceType != PieceTypes.NoPieceType;

			// MVV/LVA
			if (isCapture)
			{
				moveScoreGuess += 10 * PositionEvaluator.GetPieceValue(capturePieceType) - 5 * PositionEvaluator.GetPieceValue(movePieceType);
				moveScoreGuess *= mult2;
			}
			else
			{
				moveScoreGuess += Search.history.Retrieve(position.SideToMove, from, to);
			}

			if (phase > 0.6f && position.GivesCheck(valMove))
				moveScoreGuess += PositionEvaluator.pawnValue * mult1;

			switch (movePieceType)
			{
				case PieceTypes.Pawn:
					moveScoreGuess += PositionEvaluator.pawnValue * mult2;

					if (moveType == MoveTypes.Promotion)
					{
						var promotionType = valMove.Move.PromotedPieceType();
						moveScoreGuess += PositionEvaluator.GetPieceValue(promotionType) * 5;
					}
					break;

				case PieceTypes.King:
					moveScoreGuess += (int)Math.Pow(20, phase * 3); // value between 1-8000. Explodes near the end. At 0.5 it's around 90
					break;

				default:
					// If the target square is attacked by opponent pawn
					if (position.AttackedByPawn(to, ~position.SideToMove))
						moveScoreGuess -= 5 * PositionEvaluator.GetPieceValue(movePieceType) + 5 * PositionEvaluator.pawnValue;
					break;
			}

			valMove.Score = moveScoreGuess;

			orderedMoves[i] = valMove;
		}

		Array.Sort(orderedMoves, (a, b) => b.Score.CompareTo(a.Score));

		//orderedMoves.Sort((a, b) => b.Score.CompareTo(a.Score));

		//var ordered = orderedMoves.ToArray();

		for (int i = 0; i < orderedMoves.Length; i++)
			orderedMoves[i].Score = 0;

		return orderedMoves;
	}

	static ulong PerftTest(int depth, bool root = true)
	{
		var moves = pos.GenerateMoves();

		var state = new State();

		var tot = ulong.MinValue;

		if (root && depth <= 1)
		{
			tot += (ulong)moves.Length;
			return tot;
		}

		foreach (var em in moves)
		{
			var m = em.Move;
			pos.MakeMove(m, in state);

			if (depth <= 2)
			{
				var ml2 = pos.GenerateMoves();
				tot += (ulong)ml2.Length;
			}
			else
				tot += PerftTest(depth - 1, false);

			pos.TakeMove(m);
		}

		return tot;
	}

	//static void MoveGenerationSpeedTest()
	//{
	//	var Game = GameFactory.Create();
	//	Game.NewGame("8/6pk/pb5p/8/1P2qP2/P7/2r2pNP/1QR4K b - - 1 2");

	//	var watch = new Stopwatch();
	//	long fastest = long.MaxValue;
	//	long lastElapsedTicks = 0;
	//	int count = 10000;

	//	watch.Start();
	//	for (int i = 0; i < count; i++)
	//	{
	//		Game.Pos.GenerateMoves();
	//		long took = watch.ElapsedTicks - lastElapsedTicks;
	//		if (took < fastest)
	//			fastest = took;

	//		lastElapsedTicks = watch.ElapsedTicks;
	//	}

	//	Console.WriteLine("Generated moves " + count + " times. Took " + watch.ElapsedTicks + " ticks -> " + (1000000 * watch.ElapsedTicks / Stopwatch.Frequency) + "μs");
	//	Console.WriteLine("Fastest: " + fastest + " ticks -> " + (1000000 * fastest / Stopwatch.Frequency) + "μs");
	//	Console.WriteLine("Average: " + watch.ElapsedTicks / count + " ticks -> " + (1000000 * watch.ElapsedTicks / count / Stopwatch.Frequency) + "μs");
	//}

	//void Issue60Test()
	//{
	//	// Testing Issue #60. Works as intended and therefore issue is on their end.
	//	var game = GameFactory.Create();
	//	game.NewGame("8/6pk/pb5p/8/1P2qP2/P7/2r2pNP/1QR4K b - - 1 2");

	//	Uci uci = new();
	//	Move move = uci.MoveFromUci(game.Pos, "f2f1q");

	//	Console.WriteLine(Engine.ToAscii(game.Pos));
	//	Console.WriteLine($"Move {move} {(game.Pos.GivesCheck(move) ? "gives check" : "doesn't give check")}");
	//}

	//void ThreadTest()
	//{
	//	int maxDepth = 6;

	//	var game = GameFactory.Create();
	//	game.NewGame("rnkq1bnr/p3ppp1/1ppp3p/3B4/6b1/2PQ3P/PP1PPP2/RNB1K1NR w KQ - 0 0");

	//	Stopwatch stopwatch = Stopwatch.StartNew();

	//	for (int depth = 1; depth <= maxDepth; depth++)
	//	{
	//		Console.WriteLine("Depth: " + depth + " - " + game.Perft(depth) + " - " + stopwatch.ElapsedMilliseconds + "ms");
	//	}

	//	Console.WriteLine("Took: " + stopwatch.ElapsedMilliseconds + "ms");
	//}

	//void AllAttacksTest()
	//{
	//	var game = GameFactory.Create();
	//	game.NewGame();

	//	var attacks = game.Pos.AttacksBy(PieceTypes.AllPieces, Player.White);

	//	foreach (var att in attacks)
	//	{
	//		Console.WriteLine(att.ToString());
	//	}
	//}
}