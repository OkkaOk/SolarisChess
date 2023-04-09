using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Factories;
using Rudzoft.ChessLib.Hash.Tables.Transposition;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.ObjectPoolPolicies;
using Rudzoft.ChessLib.Protocol.UCI;
using Rudzoft.ChessLib.Types;
using Rudzoft.ChessLib.Validation;
using SolarisChess.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace SolarisChess.Test;

public sealed class DrawTest
{
	private readonly ITestOutputHelper output;

	public DrawTest(ITestOutputHelper output)
	{
		this.output = output;
	}

	[Fact]
	public void ThreeFoldRepetition()
	{
		var ttConfig = new TranspositionTableConfiguration { DefaultSize = 32 };
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

		var pos = new Position(board, values, validator, moveListObjectPool);

		var game = new Game(table, uci, cpu, sp, pos, moveListObjectPool);

		game.NewGame("8/8/1Q6/1p6/5k2/8/2P3P1/7K b - - 5 101");
		output.WriteLine("0 " + game.Pos.State.Key.Key.ToString());

		game.Pos.MakeMove(new Move(Square.F4, Square.G5), null);
		output.WriteLine("1 " + game.Pos.State.Key.Key.ToString());

		game.Pos.MakeMove(new Move(Square.H1, Square.H2), null);
		output.WriteLine("2 " + game.Pos.State.Key.Key.ToString());
		game.Pos.MakeMove(new Move(Square.G5, Square.F5), null);
		output.WriteLine("3 " + game.Pos.State.Key.Key.ToString());
		game.Pos.MakeMove(new Move(Square.H2, Square.H1), null);
		output.WriteLine("4 " + game.Pos.State.Key.Key.ToString());
		game.Pos.MakeMove(new Move(Square.F5, Square.G5), null);
		output.WriteLine("1 " + game.Pos.State.Key.Key.ToString());

		output.WriteLine(game.Pos.IsThreeFoldRepetition() + "");
		Assert.False(game.Pos.IsThreeFoldRepetition());

		game.Pos.MakeMove(new Move(Square.H1, Square.H2), null);
		output.WriteLine("2 " + game.Pos.State.Key.Key.ToString());
		game.Pos.MakeMove(new Move(Square.G5, Square.F5), null);
		output.WriteLine("3 " + game.Pos.State.Key.Key.ToString());

		output.WriteLine(game.Pos.IsThreeFoldRepetition() + "");
		Assert.False(game.Pos.IsThreeFoldRepetition());

		game.Pos.MakeMove(new Move(Square.H2, Square.H1), null);
		output.WriteLine("4 " + game.Pos.State.Key.Key.ToString());
		game.Pos.MakeMove(new Move(Square.F5, Square.G5), null);
		output.WriteLine("1 " + game.Pos.State.Key.Key.ToString());

		output.WriteLine(game.Pos.IsThreeFoldRepetition() + "");
		Assert.True(game.Pos.IsThreeFoldRepetition());

		game.Pos.TakeMove(new Move(Square.F5, Square.G5));
		output.WriteLine("4 " + game.Pos.State.Key.Key.ToString());
		game.Pos.TakeMove(new Move(Square.H2, Square.H1));
		output.WriteLine("3 " + game.Pos.State.Key.Key.ToString());

		output.WriteLine(game.Pos.IsThreeFoldRepetition() + "");
		Assert.False(game.Pos.IsThreeFoldRepetition());

		game.Pos.MakeMove(new Move(Square.H2, Square.H1), null);
		output.WriteLine("4 " + game.Pos.State.Key.Key.ToString());
		game.Pos.MakeMove(new Move(Square.F5, Square.G5), null);
		output.WriteLine("1 " + game.Pos.State.Key.Key.ToString());

		output.WriteLine(game.Pos.IsThreeFoldRepetition() + "");
		Assert.True(game.Pos.IsThreeFoldRepetition());

		game.Pos.TakeMove(new Move(Square.F5, Square.G5));
		output.WriteLine("4 " + game.Pos.State.Key.Key.ToString());
		game.Pos.TakeMove(new Move(Square.H2, Square.H1));
		output.WriteLine("3 " + game.Pos.State.Key.Key.ToString());

		output.WriteLine(game.Pos.IsThreeFoldRepetition() + "");
		Assert.False(game.Pos.IsThreeFoldRepetition());

		game.Pos.TakeMove(new Move(Square.G5, Square.F5));
		output.WriteLine("2 " + game.Pos.State.Key.Key.ToString());
		game.Pos.TakeMove(new Move(Square.H1, Square.H2));
		output.WriteLine("1 " + game.Pos.State.Key.Key.ToString());

		output.WriteLine(game.Pos.IsThreeFoldRepetition() + "");
		Assert.False(game.Pos.IsThreeFoldRepetition());
	}

	[Fact]
	public void ThreeFoldRepetition2()
	{
		var ttConfig = new TranspositionTableConfiguration { DefaultSize = 32 };
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

		var pos = new Position(board, values, validator, moveListObjectPool);

		var game = new Game(table, uci, cpu, sp, pos, moveListObjectPool);

		game.NewGame("r3k1nr/pppb2p1/4p3/2P2p1p/8/2PnBN1P/PP3PP1/R4RK1 w kq - 0 15");

		game.Pos.MakeMove(new Move(Square.A1, Square.D1), null);
		game.Pos.MakeMove(new Move(Square.F5, Square.F4), null);
		game.Pos.MakeMove(new Move(Square.D1, Square.A1), null);

		output.WriteLine(game.Pos.IsThreeFoldRepetition() + "");
		Assert.False(game.Pos.IsThreeFoldRepetition());

	}
}