using ChessLib;
using ChessLib.Hash.Tables.Transposition;
using ChessLib.MoveGeneration;
using ChessLib.Types;
using System;

namespace SolarisChess;

public class MoveOrdering
{
	public MoveOrdering()
	{

	}

	public ExtMove[] OrderMoves(MoveList moves, IPosition position)
	{
		Game.Table.Probe(position.State.Key, out TranspositionTableEntry entry);

		return moves.OrderByDescending(extMove =>
		{
			int moveScoreGuess = 0;

			var (from, to, type) = extMove.Move;

			var movePieceType = position.GetPiece(from).Type();
			var capturePieceType = position.GetPiece(to).Type();

			if (entry.Move.Equals(extMove.Move))
			{
				Console.WriteLine("Found move in tt and using it to order moves");
				return 1000000;
			}

			if (position.IsCapture(extMove))
				moveScoreGuess += 10 * Evaluation.GetPieceValue(capturePieceType) - Evaluation.GetPieceValue(movePieceType);

			//if (position.GivesCheck(extMove))
			//	moveScoreGuess += Evaluation.pawnValue;

			if (type == MoveTypes.Promotion)
			{
				var promotionType = extMove.Move.PromotedPieceType();
				moveScoreGuess += Evaluation.GetPieceValue(promotionType) * 2;
			}

			// If the target square is attacked by opponent pawn
			//if (position.AttackedByPawn(to, ~position.SideToMove))
			//{
			//	moveScoreGuess -= Evaluation.GetPieceValue(movePieceType) + Evaluation.pawnValue;
			//}

			return moveScoreGuess;
		}).ToArray();
	}
}
