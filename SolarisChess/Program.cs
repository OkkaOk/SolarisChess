
using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Types;
using System.Diagnostics;
using System.Runtime;

namespace SolarisChess;

internal class Program
{
	private static async Task Main(string[] args)
	{
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		await HandleArgs(args);
	}

	private static async Task HandleArgs(string[] args)
	{
		Console.WriteLine(string.Join(" ", args));
		if (args.Length == 0)
		{
			await Engine.Start();
			return;
		}

		if (args[0] == "bench")
		{
			Engine.Game.NewGame();
			Engine.controller.Initialize(0, 0, 0, 0, 8, 0, 0);
			Engine.search.IterativeDeepeningSearch(Engine.Game.Pos);

			Engine.Table.Clear();
			Engine.Game.NewGame("8/k7/3p4/p2P1p2/P2P1P2/8/8/K7 w - - 0 1");
			Engine.controller.Initialize(0, 0, 0, 0, 15, 0, 0);
			Engine.search.IterativeDeepeningSearch(Engine.Game.Pos);

			Engine.Table.Clear();
			Engine.Game.NewGame("K7/5b2/1n6/3n1k2/8/8/8/8 w - - 0 1");
			Engine.controller.Initialize(0, 0, 0, 0, 15, 0, 0);
			Engine.search.IterativeDeepeningSearch(Engine.Game.Pos);
			
		}
	}
}