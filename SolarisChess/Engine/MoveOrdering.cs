using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Enums;
using Rudzoft.ChessLib.Extensions;
using Rudzoft.ChessLib.Hash.Tables.Transposition;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.Types;
using System;
using System.Runtime.CompilerServices;
using static System.Math;

namespace SolarisChess;

public class MoveOrdering
{
	public MoveOrdering()
	{

	}

	/// <summary>
	/// Generates and orders the moves for the current position.
	/// </summary>
	/// <param name="plyFromRoot">The ply of the search from the root.</param>
	/// <param name="type">The type of move generation to perform.</param>
	/// <returns>The array of moves, ordered by best to worst.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ValMove[] GenerateAndOrderMoves(IPosition position, int plyFromRoot, ValMove[]? pline = null, MoveGenerationType type = MoveGenerationType.Legal)
	{
		MoveList moves = new();
		moves.Generate(position, type);

		float phase = PositionEvaluator.CalculatePhase(position);
		var entry = Cluster.DefaultEntry;
		Engine.Table.Probe(position.State.Key, ref entry);

		return moves.OrderByDescending(valMove =>
		{
			int moveScoreGuess = 0;

			if (valMove.Move == entry.Move)
			{
				//Console.WriteLine("TT");
				return 100000000;
			}

			if (pline != null && pline[0] == valMove)
			{
				//Console.WriteLine(pline[0]);
				return 1000000;
			}

			if (valMove == Search.killerMoves[plyFromRoot, 0])
				return 10000;
			if (valMove == Search.killerMoves[plyFromRoot, 1])
				return 8000;

			var (from, to, type) = valMove.Move;

			var movePieceType = position.GetPiece(from).Type();
			var capturePieceType = position.GetPiece(to).Type();

			bool isCapture = capturePieceType != PieceTypes.NoPieceType;

			// MVV/LVA
			if (isCapture)
			{
				moveScoreGuess += 10 * PositionEvaluator.GetPieceValue(capturePieceType) - 5 * PositionEvaluator.GetPieceValue(movePieceType);
				moveScoreGuess = (int)(moveScoreGuess * (phase * 5 + 1));
			}
			else
			{
				moveScoreGuess += Search.history.Retrieve(position.SideToMove, from, to);
			}

			if (phase > 0.6f && position.GivesCheck(valMove))
				moveScoreGuess += (int)(PositionEvaluator.pawnValue * (phase * 5 + 1));

			if (movePieceType == PieceTypes.Pawn)
			{
				moveScoreGuess += (int)(PositionEvaluator.pawnValue * Pow(phase + 1, 4));

				if (type == MoveTypes.Promotion)
				{
					var promotionType = valMove.Move.PromotedPieceType();
					moveScoreGuess += PositionEvaluator.GetPieceValue(promotionType) * 5;

					//Console.WriteLine("Promotion: " + promotionType.GetPieceChar());
				}
			}
			else
			{
				// If the target square is attacked by opponent pawn
				if (position.AttackedByPawn(to, ~position.SideToMove))
				{
					moveScoreGuess -= 5 * PositionEvaluator.GetPieceValue(movePieceType) + 5 * PositionEvaluator.pawnValue;
				}
			}

			return moveScoreGuess;
		}).ToArray();
	}
}
