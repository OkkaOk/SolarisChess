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
using static System.Formats.Asn1.AsnWriter;
using System.Drawing;
using System.Diagnostics;
using System.Numerics;

namespace SolarisChess;

public static class Engine
{
    public const string name = "SolarisChess 1.3";
    public const string author = "Okka";

	public readonly static TimeControl Controller = new TimeControl();
    public readonly static Game Game;
	public readonly static TranspositionTable Table;
	public readonly static Search search;
	static bool Logging = true;

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
				Console.WriteLine("option name Ponder type check default true");
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
				//Console.WriteLine($"Moves:          {Game.Moves().Length}");
				//Console.WriteLine($"Capture Moves:  {Game.CaptureMoves().Length}");
				TestFunction();
				break;
			case "bench":
				BenchEngine();
				break;
			default:
				Console.WriteLine("UNKNOWN INPUT " + command);
				return;
		}
	}

	public static async Task Start()
    {
        Running = true;
		Ponder = true;

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

	public static void OnInfo(int depth, int selDepth, int score, long nodes, int timeUs, int hashPerMille, ValMove[] pvLine)
	{
		if (!Logging)
			return;

		double tS = Math.Max(1, timeUs) / 1000000.0;
		int nps = (int)(nodes / tS);
		
		var infoString = string.Format("info depth {0} seldepth {1} score {2} nodes {3} nps {4} time {5} hashfull {6} multipv 1 pv {7}",
							depth, selDepth, ScoreToString(score), nodes, nps, timeUs/1000, hashPerMille, PVToString(pvLine));

		Console.WriteLine(infoString);
	}

	public static void Log(string message)
	{
		if (Logging)
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

	private static string PVToString(ValMove[] pv)
	{
		List<string> validUCIMoves = new();
		List<ValMove> tempMoves = new();

		foreach (var move in pv)
		{
			try
			{
				var movePiece = Game.Pos.GetPiece(move.Move.FromSquare());
				var capturePiece = Game.Pos.GetPiece(move.Move.ToSquare());

				if (capturePiece.Type() != PieceTypes.NoPieceType && capturePiece.ColorOf() == Game.Pos.SideToMove && !move.Move.IsType(MoveTypes.Castling))
					break;

				if (movePiece.Type() == PieceTypes.NoPieceType || movePiece.ColorOf() != Game.Pos.SideToMove)
					break;

				if (move.Move.IsNullMove() || !move.Move.IsValidMove() || !Game.Pos.IsLegal(move.Move))
					break;


				validUCIMoves.Add(Game.Uci.MoveToString(move.Move));
				tempMoves.Add(move);
				Game.Pos.MakeMove(move, null);

				if (Game.Pos.IsMate || Game.Pos.IsDraw() || !Game.Pos.Validate().IsOk)
					break;

			}
			catch (Exception e)
			{
				Console.WriteLine("Exception: " + e.Message);
				Console.WriteLine("Trace: " + e.StackTrace);
				break;
			}
		}

		for (int i = tempMoves.Count - 1; i >= 0; i--)
		{
			Game.Pos.TakeMove(tempMoves[i]);
		}

		var pvLength = 0;
		foreach (var move in pv)
		{
			if (move.Move.IsNullMove())
				break;
			pvLength++;
		}

		if (tempMoves.Count != pvLength)
			Log("There were errors in the pv line. PV length: " + pvLength + ". What we got: " + tempMoves.Count);

		return string.Join(" ", validUCIMoves);
	}

	private static void TestFunction()
	{
		// position startpos moves d2d4 g8f6 c2c4 e7e6 g1f3 b7b6 g2g3 c8a6 b2b3 f8b4 c1d2 b4e7 f1g2 c7c6 d2c3 d7d5 f3e5 f6d7 e5d7 b8d7 b1d2 b6b5 c4d5 c6d5 e2e4 a8c8 c3b2 d7b6 a2a3 e8g8 f2f3 d8d7 h2h3 c8c7 d1e2 a6b7 e1g1 c7c2 a1b1 b5b4 e2d3 f8c8 a3b4 e7b4 f1f2 e6e5 d4e5 b4c5 b3b4 c5f2 g1f2 b7a6 d3d4 d5e4 d4d7 b6d7 f2e3 a6d3 f3e4 c2d2 e3d2 d3b1 b2c3 d7b6 g2f3 c8c4
		var pawns = Game.Pos.Pieces(PieceTypes.Pawn, Player.White);

		foreach (var square in pawns)
		{
			var file = square.File;
			var rank = square.Rank;

			// Multiple pawns on the same file
			var pawnCountOnFile = (pawns & BitBoards.FileBB(file)).Count;
			if (pawnCountOnFile > 1)
				Console.WriteLine("Stacked pawns at:" + square.ToString());

			// Isolated pawns
			if ((pawns & BitBoards.AdjacentFiles(file)).Count == 0)
				Console.WriteLine("Isolated pawn at:" + square.ToString());

			// TODO: Backward pawns

			// TODO: Pawn Chains

			// Passed pawns
			if (Game.Pos.IsPawnPassedAt(Player.White, square.ToString()))
				Console.WriteLine("Passed pawn at: " + square.ToString());
		}
	}

	private static void BenchEngine()
	{
		long totalNodes = 0;
		var timer = Stopwatch.StartNew();

		Logging = false;

		void SearchFen(string? fen, int depth)
		{
			if (fen == null)
				Game.NewGame();
			else
				Game.NewGame(fen);

			Controller.Initialize(0, 0, 0, 0, depth, 0, 0);
			search.IterativeDeepeningSearch(Game.Pos);
			totalNodes += search.TotalNodes;
			Table.Clear();
		}

		SearchFen(null, 8);

		// a1b1 a7a8
		SearchFen("8/k7/3p4/p2P1p2/P2P1P2/8/8/K7 w - - 0 1", 14);

		// f7e8 b7a7
		SearchFen("8/1K3b2/1n6/3n1k2/8/8/8/8 b - - 1 1", 10);

		// best: b4f4. Ponder: h4g3
		SearchFen("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 10);

		// Good bestmoves: b2b3, c1e3, e1f1
		SearchFen("3r1rk1/pp3ppp/2n5/2Q5/8/8/PPP2PPP/R1B1R1K1 w - - 0 1", 7);

		Logging = true;

		Console.WriteLine("\nNodes visited: " + totalNodes);
		Console.WriteLine("Took: " + timer.ElapsedMilliseconds + "ms. NPS: " + totalNodes * 1000 / timer.ElapsedMilliseconds);
	}
}
