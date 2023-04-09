using Microsoft.Extensions.ObjectPool;
using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Enums;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.ObjectPoolPolicies;
using Rudzoft.ChessLib.Types;
using Rudzoft.ChessLib.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SolarisChess.Extensions;

public static class PositionExtensions
{
	public static bool IsDraw(this IPosition position, bool disableStalemate = false, bool disableRepetition = false, bool disableMaterial = false)
	{
		if (position.State.Rule50 > 99 && position.State.Checkers.IsEmpty)
			return true;

		if (!disableStalemate && !position.InCheck && position.GenerateMoves(MoveGenerationType.Legal).Length == 0)
			return true;

		if (!disableRepetition && position.IsThreeFoldRepetition())
			return true;

		if (!disableMaterial && position.IsInsufficientMaterial())
			return true;

		return false;
	}

	//public static void MakeMove(this IPosition position, Move move, State state)
	//{
	//	Engine.positionHistory.Push(position.State.Key.Key);
	//	position.MakeMove(move, in state);
	//}

	//public static bool IsThreeFoldRepetition(this IPosition position)
	//{
	//	return position.positionHistory.Values.Contains(3);
	//}

	public static bool IsThreeFoldRepetition(this IPosition position)
	{
		return position.State.Repetition < 0;
		//var state = position.State;
		//Dictionary<ulong, int> keyCount = new();

		//for (var i = 0; i < position.Ply; i++)
		//{
		//	if (state == null)
		//		break;

		//	if (keyCount.TryGetValue(state.Key.Key, out int count))
		//	{
		//		if (count >= 2)
		//			return true;

		//		keyCount.Remove(state.Key.Key);
		//		keyCount.Add(state.Key.Key, count + 1);
		//	}
		//	else
		//	{
		//		keyCount.Add(state.Key.Key, 1);
		//	}

		//	state = state.Previous;
		//}

		//return false;
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
		if ((knightCount == 1 && bishopCount == 0) || (knightCount == 0 && bishopCount == 1))
			return true;

		var whiteBishops = position.Pieces(PieceTypes.Bishop, Player.White);
		var blackBishops = position.Pieces(PieceTypes.Bishop, Player.Black);
		int wbCount = whiteBishops.Count;
		int bbCount = blackBishops.Count;

		if (knightCount == 0 && whiteBishops.Count == 1 && blackBishops.Count == 1)
			return true;

		if (knightCount == 0 && wbCount > 1)
		{
			// If all of white's bishops are on the same color, then it's insufficient material
			bool wbFirstDark = BitBoards.PopLsb(ref whiteBishops).IsDark;
			bool wFoundDifferent = false;
			while (whiteBishops)
			{
				var square = BitBoards.PopLsb(ref whiteBishops);
				wFoundDifferent = square.IsDark != wbFirstDark;
			}

			if (!wFoundDifferent)
				return true;
		}

		if (knightCount == 0 && bbCount > 1)
		{
			// If all of black's bishops are on the same color, then it's insufficient material
			bool bbFirstDark = BitBoards.PopLsb(ref blackBishops).IsDark;
			bool bFoundDifferent = false;
			while (blackBishops)
			{
				var square = BitBoards.PopLsb(ref blackBishops);
				bFoundDifferent = square.IsDark != bbFirstDark;
			}

			if (!bFoundDifferent)
				return true;
		}

		return false;
	}
}
