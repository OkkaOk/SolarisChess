using Rudzoft.ChessLib.Types;
using System.Runtime.CompilerServices;
using static System.Math;

namespace SolarisChess.Extensions;

public static class SquareExtensions
{
	private static readonly int[][] ManhattanDistances;
	private static readonly int[] CenterManhattanDistances =
	{
		6, 5, 4, 3, 3, 4, 5, 6,
		5, 4, 3, 2, 2, 3, 4, 5,
		4, 3, 2, 1, 1, 2, 3, 4,
		3, 2, 1, 0, 0, 1, 2, 3,
		3, 2, 1, 0, 0, 1, 2, 3,
		4, 3, 2, 1, 1, 2, 3, 4,
		5, 4, 3, 2, 2, 3, 4, 5,
		6, 5, 4, 3, 3, 4, 5, 6
	};

	// local helper functions to calculate distance
	static int Distance(int x, int y) => Math.Abs(x - y);
	static int DistanceFile(Square x, Square y) => Distance(x.File.AsInt(), y.File.AsInt());
	static int DistanceRank(Square x, Square y) => Distance(x.Rank.AsInt(), y.Rank.AsInt());

	static SquareExtensions()
	{
		ManhattanDistances = new int[Square.Count][];
		for (var i = 0; i < ManhattanDistances.Length; i++)
			ManhattanDistances[i] = new int[Square.Count];

		var bb = BitBoards.AllSquares;
		while (bb)
		{
			var s1 = BitBoards.PopLsb(ref bb);
			var sq = s1.AsInt();

			var bb2 = BitBoards.AllSquares & ~s1;
			// distance computation
			while (bb2)
			{
				var s2 = BitBoards.PopLsb(ref bb2);
				var manhattanDistance = DistanceFile(s1, s2) + DistanceRank(s1, s2);
				ManhattanDistances[sq][s2.AsInt()] = manhattanDistance;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int ManhattanDistance(this Square sq1, Square sq2)
		=> ManhattanDistances[sq1.AsInt()][sq2.AsInt()];

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int CenterManhattanDistance(this Square sq1)
		=> CenterManhattanDistances[sq1.AsInt()];
}
