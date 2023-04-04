using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Factories;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.Protocol.UCI;
using Rudzoft.ChessLib.Types;
using System.Text;
using Rudzoft.ChessLib.Extensions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Rudzoft.ChessLib.Fen;
using Rudzoft.ChessLib.Hash.Tables.Transposition;
using Rudzoft.ChessLib.ObjectPoolPolicies;
using Rudzoft.ChessLib.Validation;
using SolarisChess.Extensions;
using Rudzoft.ChessLib.Polyglot;
using Microsoft.Extensions.Primitives;

namespace SolarisChess;

public static class Engine
{
    public const string name = "SolarisChess 1.2";
    public const string author = "Okka";

	public readonly static TimeControl Controller = new TimeControl();
    public readonly static Game Game;
	public readonly static TranspositionTable Table;
	public readonly static Search search;

	public static bool Ponder { get; private set; }
	public static bool Running { get; private set; }
    public static bool WhiteToPlay => Game.Pos.SideToMove.IsWhite;

    static Engine()
    {
		var ttConfig = new TranspositionTableConfiguration { DefaultSize = 128 };
		var options = Options.Create(ttConfig);
		Table = new TranspositionTable(options);

		var uci = new Uci();
		uci.Initialize();

		var cpu = new Cpu();

		var moveListObjectPool = new DefaultObjectPool<IMoveList>(new MoveListPolicy());

		var sp = new SearchParameters();

		var board = new Board();
		var values = new Values();
		var validator = new PositionValidator();

		var pos = new Position(board, values, validator, moveListObjectPool);

		Game = new Game(Table, uci, cpu, sp, pos, moveListObjectPool);

		search = new Search();
	}

	private static void HandleUCICommand(string? command)
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
				Console.WriteLine("option name Hash type spin default 128 min 8 max 2048");
				Console.WriteLine("option name Ponder type check default false");
				//Console.WriteLine("option name OwnBook type check default false");
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
			case "ponderhit":
				Task.Run(search.PonderHit);
				break;
			case "ucinewgame":
				Controller.Reset();
				Table.Clear();
				//Transpositions.Clear();
				break;
			case "stop":
				Stop();
				break;
			case "quit":
				Quit();
				break;
			case "setoption":
				SetOption(tokens);
				break;
			case "printpos":
				Console.WriteLine(ToAscii(Game.Pos));
				break;
			case "test":
				//Console.WriteLine($"Moves:          {engine.Game.Moves().Length}");
				//Console.WriteLine($"Capture Moves:  {engine.Game.CaptureMoves().Length}");
				TestFunction();
				break;
			default:
				Console.WriteLine("UNKNOWN INPUT " + command);
				return;
		}
	}

	public static async Task Start()
    {
        Running = true;
		await Read();
    }

    public static void Stop()
    {
		search.CancelSearch();
        Controller.Stop();
    }

    public static void Quit()
    {
		Task.Delay(1000).ContinueWith((t) =>
		{
			search.CancelSearch();
			Controller.Stop();

			Log("QUITTING");
			Running = false;
			Environment.Exit(0);
		});
    }

	public static async Task Read()
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
				Console.WriteLine("info string " + e.StackTrace);
			}
		}
	}

	public static void ParseUCIGo(string[] tokens)
    {
		if (Game.Pos.State.Key == 0)
		{
			Console.WriteLine("Set the board to starting position.");
			//Game.NewGame("8/k7/3p4/p2P1p2/P2P1P2/8/8/K7 w - - 0 1");
			Game.NewGame();
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

		bool ponderGo = tokens[1] == "ponder";

		if (ponderGo && !Ponder)
		{
			Log("I was told to ponder but pondering isn't allowed!");
			return;
		}

		//Console.WriteLine(ponderGo ? "I'm going to ponder" : "I'm going to search");

		int myTime = WhiteToPlay ? whiteTime : blackTime;
		int opponentTime = WhiteToPlay ? blackTime : whiteTime;
		int myIncrement = WhiteToPlay ? whiteIncrement : blackIncrement;
		//int myTime			= ponderGo ? (WhiteToPlay ? blackTime : whiteTime) : (WhiteToPlay ? whiteTime : blackTime);
		//int opponentTime	= ponderGo ? (WhiteToPlay ? whiteTime : blackTime) : (WhiteToPlay ? blackTime : whiteTime);
		//int myIncrement		= ponderGo ? (WhiteToPlay ? blackIncrement : whiteIncrement) : (WhiteToPlay ? whiteIncrement : blackIncrement);

		Controller.Initialize(myTime, opponentTime, myIncrement, movesToGo, maxDepth, maxNodes, moveTime, ponderGo);

		if (Controller.IsInfinite && Array.IndexOf(tokens, "infinite") == -1 && maxDepth == 0 && maxNodes == 0)
		{
			Console.WriteLine("Invalid 'go' parameters!");
			return;
		}

		//Task.Factory.StartNew(() => search.IterativeDeepeningSearch(Game.Pos), TaskCreationOptions.LongRunning);
		Task.Factory.StartNew(() => search.IterativeDeepeningSearch(Game.Pos));
		//search.IterativeDeepeningSearch(Game.Pos);
	}

	private static void LoadPosition(string[] tokens)
	{
		//position [fen <fenstring> | startpos ]  moves <move1> .... <movei>
		if (tokens.Length > 1 && tokens[1] == "startpos")
		{
			Game.NewGame();
		}
		else if (tokens.Length > 1 && tokens[1] == "fen") //rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1
		{
			string fen = string.Join(' ', tokens[2..8]);
			Game.NewGame(fen);
		}
		else
		{
			Log("'position' parameters missing or not understood. Assuming 'startpos'.");
			Game.NewGame();
		}

		int firstMove = Array.IndexOf(tokens, "moves") + 1;
		if (firstMove == 0)
			return;

		for (int i = firstMove; i < tokens.Length; i++)
		{
			MakeUCIMove(tokens[i]);
		}
	}

	private static void SetOption(string[] tokens)
	{
		var nameIndex = Array.IndexOf(tokens, "name") + 1;
		var name = tokens[nameIndex];
		var valueIndex = Array.IndexOf(tokens, "value") + 1;
		var value = tokens[valueIndex];

		if (name == "Ponder")
			Ponder = value == "true";
		else if (name == "Hash")
			Table.SetSize(int.Parse(value));
	}

	private static void MakeUCIMove(string uciMove)
	{
		Move move = Game.Uci.MoveFromUci(Game.Pos, uciMove);

		Game.Pos.MakeMove(move, new());
	}

	public static void OnSearchComplete(ValMove bestMove, ValMove ponderMove)
	{
		var uciBestMove = Game.Uci.MoveToString(bestMove);
		var uciPonderMove = Game.Uci.MoveToString(ponderMove);

		if (ponderMove.Move.IsNullMove())
		{
			Console.WriteLine("bestmove " + uciBestMove);
		}
		else
		{
			Game.Pos.MakeMove(bestMove, null);
			Game.Pos.MakeMove(ponderMove, null);
			
			if (Game.Pos.IsMate || Game.Pos.IsDraw())
				Console.WriteLine("bestmove " + uciBestMove);
			else
				Console.WriteLine("bestmove " + uciBestMove + " ponder " + uciPonderMove);

			Game.Pos.TakeMove(ponderMove);
			Game.Pos.TakeMove(bestMove);
		}
	}

	public static void OnInfo(int depth, int score, long nodes, int timeMs, int hashPerMille, ValMove[] pvLine)
	{
		double tS = Math.Max(1, timeMs) / 1000.0;
		int nps = (int)(nodes / tS);
		StringBuilder sb = new();
		sb.Append($"info depth {depth} score {ScoreToString(score)} nodes {nodes} nps {nps} time {timeMs} hashfull {hashPerMille} multipv 1 pv");
		//sb.Append($"info depth {depth} score {ScoreToString(score)} nodes {nodes} nps {nps} time {timeMs} hashfull {hashPerMille}");

		List<ValMove> tempMoves = new();

		foreach (var move in pvLine)
		{
			if (move.Move.IsNullMove() || !move.Move.IsValidMove() || !Game.Pos.IsLegal(move.Move))
				break;

			sb.Append(" " + Game.Uci.MoveToString(move.Move));

			Game.Pos.MakeMove(move, null);
			tempMoves.Add(move);

			if (Game.Pos.IsMate || Game.Pos.IsDraw() || !Game.Pos.Validate().IsOk)
				break;
		}

		for (int i = tempMoves.Count - 1; i >= 0; i--)
		{
			Game.Pos.TakeMove(tempMoves[i]);
		}

		Console.WriteLine(sb);
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
		if (PositionEvaluator.IsMateScore(score))
		{
			int sign = Math.Sign(score);
			int moves = PositionEvaluator.NumMovesToMateFromScore(score);
			return $"mate {sign * moves}";
		}

		return $"cp {score}";
	}

	private static void TestFunction()
	{
		var moveList = Game.Pos.GenerateMoves();
		foreach (var move in moveList.Get())
			Console.WriteLine(move.Move.ToString());
	}
}
