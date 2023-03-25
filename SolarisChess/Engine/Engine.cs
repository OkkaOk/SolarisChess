using ChessLib;
using ChessLib.Factories;
using ChessLib.MoveGeneration;
using ChessLib.Protocol.UCI;
using ChessLib.Types;
using System.Text;
using ChessLib.Extensions;

namespace SolarisChess;

public class Engine
{
    public const string name = "SolarisChess 1.0.0";
    public const string author = "Okka";

	public SearchController controller = new SearchController();
    private Search search;

	private bool positionLoaded;

    public IGame game;

	public bool Running { get; private set; }
    public bool WhiteToPlay => game.CurrentPlayer().IsWhite;
    public Engine()
    {
        game = GameFactory.Create();
        search = new Search(game, controller);
        search.OnSearchComplete += OnSearchComplete;
		search.OnInfo += OnInfo;
    }

	private void HandleUCICommand(string? command)
	{
		if (command == null)
			return;

		//remove leading & trailing whitecases and split using ' ' as delimiter
		string[] tokens = command.Trim().Split();
		switch (tokens[0])
		{
			case "uci":
				Console.WriteLine($"id name {name}");
				Console.WriteLine($"id author {author}");
				Console.WriteLine("option name Hash type spin default 32 max 2048");
				Console.WriteLine(Game.Uci.UciOk());
				break;
			case "isready":
				Console.WriteLine(Game.Uci.ReadyOk());
				break;
			case "position":
				LoadPosition(tokens);
				break;
			case "go":
				ParseUCIGo(tokens);
				break;
			case "ucinewgame":
				positionLoaded = false;
				//Transpositions.Clear();
				break;
			case "stop":
				Stop();
				break;
			case "quit":
				Quit();
				break;
			case "setoption":
				//SetOption(tokens);
				break;
			case "printpos":
				Console.WriteLine(ToAscii(game.Pos));
				break;
			case "test":
				//Console.WriteLine($"Moves:          {engine.game.Moves().Length}");
				//Console.WriteLine($"Capture Moves:  {engine.game.CaptureMoves().Length}");
				TestFunction();
				break;
			default:
				Console.WriteLine("UNKNOWN INPUT " + command);
				return;
		}
	}

	public async Task Start()
    {
        Running = true;
		await Read();
    }

    public void Stop()
    {
        controller.Stop();
		search.CancelSearch();
    }

    public void Quit()
    {
		Task.Delay(1000).ContinueWith((t) =>
		{
			controller.Stop();

			Log("QUITTING");
			Running = false;
			Environment.Exit(0);
		});
    }

	public async Task Read()
	{
		while (Running)
		{
			string? input = await Task.Run(Console.ReadLine);

			try
			{
				HandleUCICommand(input);
			}
			catch (Exception e)
			{
				Log(e.Message);
				Console.WriteLine("uci info " + e.StackTrace);
			}
		}
	}

	public void ParseUCIGo(string[] tokens)
    {
		if (game.Pos.State.Key == 0)
		{
			Console.WriteLine("Can't start searching because the board isn't set yet!");
			return;
		}

		//Searching on a budget that may increase at certain intervals
		//40 Moves in 5 Minutes = go wtime 300000 btime 300000 movestogo 40
		//40 Moves in 5 Minutes, 1 second increment per Move =  go wtime 300000 btime 300000 movestogo 40 winc 1000 binc 1000 movestogo 40
		//5 Minutes total, no increment (sudden death) = go wtime 300000 btime 300000

		TryParse(tokens, "depth", out int maxDepth);
		TryParse(tokens, "movetime", out int moveTime);
		TryParse(tokens, "nodes", out long maxNodes);
		TryParse(tokens, "movestogo", out int movesToGo);
		TryParse(tokens, "wtime", out int whiteTime);
		TryParse(tokens, "btime", out int blackTime);
		TryParse(tokens, "winc", out int whiteIncrement);
		TryParse(tokens, "binc", out int blackIncrement);

		int myTime = WhiteToPlay ? whiteTime : blackTime;
		int myIncrement = WhiteToPlay ? whiteIncrement: blackIncrement;

		controller.Initialize(myTime, myIncrement, movesToGo, maxDepth, maxNodes, moveTime);

		if (controller.isInfinite && Array.IndexOf(tokens, "infinite") == -1 && maxDepth == 0 && maxNodes == 0)
		{
			Console.WriteLine("Invalid 'go' parameters!");
			return;
		}

		Task.Factory.StartNew(() => search.IterativeDeepeningSearch(), TaskCreationOptions.LongRunning);
	}

	private void LoadPosition(string[] tokens)
	{
		if (positionLoaded)
		{
			//MakeUCIMove(tokens[tokens.Length - 2]);
			MakeUCIMove(tokens[tokens.Length - 1]);
			return;
		}

		positionLoaded = true;

		//position [fen <fenstring> | startpos ]  moves <move1> .... <movei>
		if (tokens[1] == "startpos")
		{
			game.NewGame();
		}
		else if (tokens[1] == "fen") //rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1
		{
			string fen = string.Join(' ', tokens[2..8]);
			game.NewGame(fen);
		}
		else
		{
			Log("'position' parameters missing or not understood. Assuming 'startpos'.");
			game.NewGame();
		}

		int firstMove = Array.IndexOf(tokens, "moves") + 1;
		if (firstMove == 0)
			return;

		for (int i = firstMove; i < tokens.Length; i++)
		{
			MakeUCIMove(tokens[i]);
		}
	}

	private void MakeUCIMove(string uciMove)
	{
		Move move = Game.Uci.MoveFromUci(game.Pos, uciMove);

		if (!move.IsNullMove() && game.Pos.State.LastMove != move)
			game.Pos.MakeMove(move, game.Pos.State);

		if (game.Pos.IsMate)
		{
			Quit();
			Log("Checkmate");
		}
		else if (game.Pos.IsDraw())
		{
			Quit();
			Log("Draw");
		}
	}

	public void OnSearchComplete(ExtMove extMove)
	{
		game.Pos.MakeMove(extMove, game.Pos.State);

		var bestMove = Game.Uci.MoveToString(extMove.Move);
		Console.WriteLine("bestmove " + bestMove);

		if (game.Pos.IsMate)
		{
			Quit();
			Log("Checkmate");
		}
		else if (game.Pos.IsDraw())
		{
			Quit();
			Log("Draw");
		}

		Console.WriteLine("Current repetition count: " + RepetitionCount());

		int RepetitionCount()
		{
			var counts = new Dictionary<HashKey, int>();
			int max = 0;
			foreach (var key in game.Pos.ZobristKeyHistory)
			{
				if (counts.ContainsKey(key))
					counts[key]++;
				else
					counts[key] = 1;

				if (counts[key] > max)
					max = counts[key];
			}
			counts.Clear();
			return max;
		}
	}

	private void OnInfo(int depth, int score, long nodes, int timeMs)
	{
		double tS = Math.Max(1, timeMs) / 1000.0;
		int nps = (int)(nodes / tS);
		Console.WriteLine($"info depth {depth} score {ScoreToString(score)} nodes {nodes} nps {nps} time {timeMs}");
	}

	public static void Log(string message)
	{
		Console.WriteLine($"info string {message}");
	}

	public static bool TryParse(string[] tokens, string name, out int value, int defaultValue = 0)
	{
		if (int.TryParse(Token(tokens, name), out value))
			return true;
		//token couldn't be parsed. use default value
		value = defaultValue;

		return false;
	}

	public static bool TryParse(string[] tokens, string name, out long value, long defaultValue = 0)
	{
		if (long.TryParse(Token(tokens, name), out value))
			return true;
		//token couldn't be parsed. use default value
		value = defaultValue;

		return false;
	}

	public static string? Token(string[] tokens, string name)
	{
		int iParam = Array.IndexOf(tokens, name);
		if (iParam < 0) return null;

		int iValue = iParam + 1;
		return iValue < tokens.Length ? tokens[iValue] : null;
	}

	public static string ToAscii(IPosition pos)
	{
		StringBuilder builder = new("   ┌────────────────────────┐\n");
		char[,] chars = new char[8, 8];


		foreach (var sq in pos.Pieces())
		{
			var piece = pos.GetPiece(sq);
			int file = sq.File.AsInt();
			int rank = sq.Rank.AsInt();
			chars[rank, file] = piece.GetPieceChar();
		}

		for (int i = 8 - 1; i >= 0; i--)
		{
			builder.Append(" " + (i + 1) + " │");
			for (int j = 0; j < 8; j++)
			{
				builder.Append(' ');
				
				if (chars[i, j] != 0)
					builder.Append(chars[i, j]);
				else
					builder.Append('.');


				builder.Append(' ');
			}
			builder.Append("│\n");
		}

		builder.Append("   └────────────────────────┘\n");
		builder.Append("     a  b  c  d  e  f  g  h  \n");

		return builder.ToString();
	}

	private static string ScoreToString(int score)
	{
		if (Evaluation.IsMateScore(score))
		{
			int sign = Math.Sign(score);
			int moves = Evaluation.NumMovesToMateFromScore(score);
			return $"mate {sign * moves}";
		}

		return $"cp {score}";
	}

	private void TestFunction()
	{
		var moveList = game.Pos.GenerateMoves();
		foreach (var move in moveList.Get())
			Console.WriteLine(move.Move.ToString());
	}
}
