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

	public ExtMove[] OrderMoves(ExtMove[] moves, IPosition position)
	{
		return moves.OrderByDescending(extMove =>
		{
			int moveScoreGuess = 0;

			var movePieceType = position.GetPiece(extMove.Move.FromSquare()).Type();
			var capturePieceType = position.GetPiece(extMove.Move.ToSquare()).Type();

			if (Game.Table.Probe(position.State.Key, out TranspositionTableEntry entry) && entry.Move.Equals(extMove.Move))
			{
				Console.WriteLine("Found move in tt and using it to order moves");
				moveScoreGuess += 10000;
			}

			if (position.IsCapture(extMove))
				moveScoreGuess += 10 * Evaluation.GetPieceValue(capturePieceType) - Evaluation.GetPieceValue(movePieceType);

			if (position.GivesCheck(extMove))
				moveScoreGuess += Evaluation.pawnValue;

			if (extMove.Move.IsPromotionMove())
			{
				var promotionType = extMove.Move.PromotedPieceType();
				moveScoreGuess += Evaluation.GetPieceValue(promotionType);
			}

			// If the target square is attacked by opponent pawn
			if (position.AttackedByPawn(extMove.Move.ToSquare(), ~position.SideToMove))
			{
				moveScoreGuess -= Evaluation.GetPieceValue(movePieceType) + Evaluation.pawnValue;
			}

			return moveScoreGuess;
		}).ToArray();
	}
}
