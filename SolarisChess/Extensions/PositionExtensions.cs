using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Enums;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//namespace Rudzoft.ChessLib;
namespace SolarisChess.Extensions;

public static class PositionExtensions
{
	public static bool IsDraw(this IPosition position)
	{
		if (position.State.Rule50 > 99 && position.State.Checkers.IsEmpty)
			return true;

		if (MoveFactory.GenerateMoves(position, MoveGenerationType.Legal).Length == 0 && !position.InCheck)
			return true;

		if (position.IsThreeFoldRepetition())
			return true;

		if (position.IsInsufficientMaterial())
			return true;

		return false;
	}

	public static bool IsThreeFoldRepetition(this IPosition position)
	{
		//var counts = new Dictionary<HashKey, int>();
		//foreach (var key in ZobristKeyHistory)
		//{
		//	if (counts.ContainsKey(key))
		//		counts[key]++;
		//	else
		//		counts[key] = 1;

		//	if (counts[key] >= 3)
		//		return true;
		//}
		//counts.Clear();
		return false;
	}

	public static bool IsInsufficientMaterial(this IPosition position)
	{
		int pawnCount = position.PieceCount(PieceTypes.Pawn);
		int knightCount = position.PieceCount(PieceTypes.Knight);
		int bishopCount = position.PieceCount(PieceTypes.Bishop);
		int rookCount = position.PieceCount(PieceTypes.Rook);
		int queenCount = position.PieceCount(PieceTypes.Queen);

		// If there are any pawns, rooks, or queens, then there is sufficient material
		if (pawnCount != 0 || rookCount != 0 || queenCount != 0)
			return false;

		// If there are no bishops or knights, then there is insufficient material
		if (bishopCount == 0 && knightCount == 0)
			return true;

		// If there is only one knight or bishop, then there is insufficient material
		if (knightCount == 1 || bishopCount == 1)
			return true;

		var whiteBishopSquares = position.Squares(PieceTypes.Bishop, Player.White);
		var blackBishopSquares = position.Squares(PieceTypes.Bishop, Player.Black);

		if (knightCount == 0 && whiteBishopSquares.Length == 1 && blackBishopSquares.Length == 1)
			return true;

		// If all of white's bishops are on the same color, then it's insufficient material
		bool wbFirstDark = whiteBishopSquares[0].IsDark;
		bool wFoundDifferent = false;
		foreach (var wbSquare in whiteBishopSquares)
		{
			wFoundDifferent = wbSquare.IsDark != wbFirstDark;
		}

		if (!wFoundDifferent)
			return true;

		// If all of black's bishops are on the same color, then it's insufficient material
		bool bbFirstDark = blackBishopSquares[0].IsDark;
		bool bFoundDifferent = false;
		foreach (var bbSquare in whiteBishopSquares)
		{
			bFoundDifferent = bbSquare.IsDark != bbFirstDark;
		}

		if (!bFoundDifferent)
			return true;

		return false;
	}
}
