using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Hash.Tables.Transposition;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.Types;
using System;

namespace SolarisChess;

public class MoveOrdering
{
	public MoveOrdering()
	{

	}

	public ValMove[] OrderMoves(MoveList moves, IPosition position, ValMove[] pvMoves, int plyFromRoot)
	{
		var entry = new TranspositionTableEntry();
		Engine.Table.Probe(position.State.Key, ref entry);

		return moves.OrderByDescending(valMove =>
		{
			int moveScoreGuess = 0;

			var (from, to, type) = valMove.Move;

			var movePieceType = position.GetPiece(from).Type();
			var capturePieceType = position.GetPiece(to).Type();

			if (valMove.Move.Equals(entry.Move))
			{
				Console.WriteLine("Found move in tt and using it to order moves");
				return 1000000;
			}

			if (pvMoves[plyFromRoot].Equals(valMove))
			{
				return 100000000;
			}

			// MVV/LVA
			if (position.IsCapture(valMove))
				moveScoreGuess += 10 * Evaluation.GetPieceValue(capturePieceType) - 10 * Evaluation.GetPieceValue(movePieceType);

			//if (position.GivesCheck(extMove))
			//	moveScoreGuess += Evaluation.pawnValue;

			if (type == MoveTypes.Promotion)
			{
				var promotionType = valMove.Move.PromotedPieceType();
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
