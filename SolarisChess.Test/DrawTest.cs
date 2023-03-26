using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Factories;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.Types;
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
		//var game = GameFactory.Create();
		//game.NewGame("8/8/1Q6/1p6/5k2/8/2P3P1/7K b - - 5 101");

		//game.Pos.MakeMove(new Move(Square.F4, Square.G5), game.Pos.State);

		//game.Pos.MakeMove(new Move(Square.H1, Square.H2), game.Pos.State);
		//game.Pos.MakeMove(new Move(Square.G5, Square.F5), game.Pos.State);
		//game.Pos.MakeMove(new Move(Square.H2, Square.H1), game.Pos.State);
		//game.Pos.MakeMove(new Move(Square.F5, Square.G5), game.Pos.State);

		//output.WriteLine(game.Pos.IsDraw() + "");
		//Assert.False(game.Pos.IsDraw());

		//game.Pos.MakeMove(new Move(Square.H1, Square.H2), game.Pos.State);
		//game.Pos.MakeMove(new Move(Square.G5, Square.F5), game.Pos.State);

		//output.WriteLine(game.Pos.IsDraw() + "");
		//Assert.False(game.Pos.IsDraw());

		//game.Pos.MakeMove(new Move(Square.H2, Square.H1), game.Pos.State);
		//game.Pos.MakeMove(new Move(Square.F5, Square.G5), game.Pos.State);

		//output.WriteLine(game.Pos.IsDraw() + "");
		//Assert.True(game.Pos.IsDraw());

		//game.Pos.TakeMove(new Move(Square.F5, Square.G5));
		//game.Pos.TakeMove(new Move(Square.H2, Square.H1));

		//output.WriteLine(game.Pos.IsDraw() + "");
		//Assert.False(game.Pos.IsDraw());
	}
}