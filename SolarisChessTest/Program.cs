using ChessLib;
using ChessLib.Factories;
using ChessLib.MoveGeneration;
using ChessLib.ObjectPoolPolicies;
using ChessLib.Protocol.UCI;
using ChessLib.Types;
using Microsoft.Extensions.ObjectPool;
using SolarisChess;
using System.Diagnostics;
using System.Drawing;

namespace SolarisChessTest;

internal class Program
{
	private static readonly ObjectPool<IMoveList> _moveLists = new DefaultObjectPool<IMoveList>(new MoveListPolicy());
	static IPosition pos;

	static void Main(string[] args)
	{
		var game = GameFactory.Create();
		game.NewGame();

		pos = game.Pos;

		Stopwatch stopwatch = Stopwatch.StartNew();
		var res = game.Perft(6);
		//ulong res = PerftTest(6);
		Console.WriteLine($"Result: {res}. Took {stopwatch.ElapsedMilliseconds}ms");
	}

	static ulong PerftTest(int depth, bool root = true)
	{
		var ml = _moveLists.Get();
		ml.Generate(in pos, ChessLib.Enums.MoveGenerationType.Quiets);
		var state = new State();

		var tot = ulong.MinValue;

		foreach (var em in ml.Get())
			if (root && depth <= 1)
				tot++;
			else
			{
				var m = em.Move;
				pos.MakeMove(m, in state);

				if (depth <= 2)
				{
					var ml2 = _moveLists.Get();
					ml2.Generate(in pos, ChessLib.Enums.MoveGenerationType.Quiets);
					tot += (ulong)ml2.Length;
					_moveLists.Return(ml2);
				}
				else
					tot += PerftTest(depth - 1, false);

				pos.TakeMove(m);
			}

		_moveLists.Return(ml);

		return tot;
	}

	static void MoveGenerationSpeedTest()
	{
		var Game = GameFactory.Create();
		Game.NewGame("8/6pk/pb5p/8/1P2qP2/P7/2r2pNP/1QR4K b - - 1 2");

		var watch = new Stopwatch();
		long fastest = long.MaxValue;
		long lastElapsedTicks = 0;
		int count = 10000;

		watch.Start();
		for (int i = 0; i < count; i++)
		{
			Game.Pos.GenerateMoves();
			long took = watch.ElapsedTicks - lastElapsedTicks;
			if (took < fastest)
				fastest = took;

			lastElapsedTicks = watch.ElapsedTicks;
		}

		Console.WriteLine("Generated moves " + count + " times. Took " + watch.ElapsedTicks + " ticks -> " + (1000000 * watch.ElapsedTicks / Stopwatch.Frequency) + "μs");
		Console.WriteLine("Fastest: " + fastest + " ticks -> " + (1000000 * fastest / Stopwatch.Frequency) + "μs");
		Console.WriteLine("Average: " + watch.ElapsedTicks / count + " ticks -> " + (1000000 * watch.ElapsedTicks / count / Stopwatch.Frequency) + "μs");
	}

	void Issue60Test()
	{
		// Testing Issue #60. Works as intended and therefore issue is on their end.
		var game = GameFactory.Create();
		game.NewGame("8/6pk/pb5p/8/1P2qP2/P7/2r2pNP/1QR4K b - - 1 2");

		Uci uci = new();
		Move move = uci.MoveFromUci(game.Pos, "f2f1q");

		Console.WriteLine(Engine.ToAscii(game.Pos));
		Console.WriteLine($"Move {move} {(game.Pos.GivesCheck(move) ? "gives check" : "doesn't give check")}");
	}

	void ThreadTest()
	{
		int maxDepth = 6;

		var game = GameFactory.Create();
		game.NewGame("rnkq1bnr/p3ppp1/1ppp3p/3B4/6b1/2PQ3P/PP1PPP2/RNB1K1NR w KQ - 0 0");

		Stopwatch stopwatch = Stopwatch.StartNew();

		for (int depth = 1; depth <= maxDepth; depth++)
		{
			Console.WriteLine("Depth: " + depth + " - " + game.Perft(depth) + " - " + stopwatch.ElapsedMilliseconds + "ms");
		}

		Console.WriteLine("Took: " + stopwatch.ElapsedMilliseconds + "ms");
	}

	void AllAttacksTest()
	{
		var game = GameFactory.Create();
		game.NewGame();

		var attacks = game.Pos.AttacksBy(PieceTypes.AllPieces, Player.White);

		foreach (var att in attacks)
		{
			Console.WriteLine(att.ToString());
		}
	}
}