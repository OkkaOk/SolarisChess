using ChessLib.Factories;
using ChessLib.Protocol.UCI;
using ChessLib.Types;
using SolarisChess;
using System.Diagnostics;

namespace SolarisChessTest;

internal class Program
{
	static void Main(string[] args)
	{

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

		var attacks = game.Pos.AllAttacksBy(Player.White);

		foreach (var att in attacks)
		{
			Console.WriteLine(att.ToString());
		}
	}
}