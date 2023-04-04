
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

		AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

		await HandleArgs(args);
	}

	private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
	{
		Exception exception = (Exception)e.ExceptionObject;

		// Log the exception
		LogException(exception);

		// You can also show a message to the user, terminate the application, or perform other actions
		Environment.Exit(1);
	}

	private static void LogException(Exception exception)
	{
		// Implement your logging logic here, for example, write the exception details to a file or a database
		Console.WriteLine($"Unhandled exception: {exception}");
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
			Engine.Controller.Initialize(0, 0, 0, 0, 8, 0, 0);
			Engine.search.IterativeDeepeningSearch(Engine.Game.Pos);

			Engine.Table.Clear();
			Engine.Game.NewGame("8/k7/3p4/p2P1p2/P2P1P2/8/8/K7 w - - 0 1");
			Engine.Controller.Initialize(0, 0, 0, 0, 15, 0, 0);
			Engine.search.IterativeDeepeningSearch(Engine.Game.Pos);

			Engine.Table.Clear();
			Engine.Game.NewGame("K7/5b2/1n6/3n1k2/8/8/8/8 w - - 0 1");
			Engine.Controller.Initialize(0, 0, 0, 0, 15, 0, 0);
			Engine.search.IterativeDeepeningSearch(Engine.Game.Pos);
			
		}
	}
}