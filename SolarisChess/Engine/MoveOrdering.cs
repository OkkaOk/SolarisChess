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
		float phase = PositionEvaluator.CalculatePhase(position);
		int mult1 = (int)(phase * 5 + 1);	// 1-6. Linear
		int mult2 = (int)Pow(phase + 1, 4); // 1-16. gradual rise

		var entry = Cluster.DefaultEntry;
		Engine.Table.Probe(position.State.Key, ref entry);

		MoveList ml = new();
		ml.Generate(position, type);

		ValMove[] orderedMoves = new ValMove[ml.Length];

		for (int i = 0; i < ml.Length; i++)
		{
			ValMove valMove = ml[i];

			int moveScoreGuess = 0;

			if (valMove.Move == entry.Move)
			{
				valMove.Score = 100000000;
				orderedMoves[i] = valMove;
				continue;
			}

			if (pline != null && pline[0] == valMove)
			{
				//Console.WriteLine(pline[0]);
				valMove.Score = 1000000;
				orderedMoves[i] = valMove;
				continue;
			}

			if (valMove == Search.killerMoves[plyFromRoot, 0])
			{
				valMove.Score = 10000;
				orderedMoves[i] = valMove;
				continue;
			}

			if (valMove == Search.killerMoves[plyFromRoot, 1])
			{
				valMove.Score = 8000;
				orderedMoves[i] = valMove;
				continue;
			}

			var (from, to, moveType) = valMove.Move;

			var movePieceType = position.GetPiece(from).Type();
			var capturePieceType = position.GetPiece(to).Type();

			bool isCapture = capturePieceType != PieceTypes.NoPieceType;

			// MVV/LVA
			if (isCapture)
			{
				moveScoreGuess += 10 * PositionEvaluator.GetPieceValue(capturePieceType) - 5 * PositionEvaluator.GetPieceValue(movePieceType);
				moveScoreGuess *= mult2;
			}
			else
			{
				moveScoreGuess += Search.history.Retrieve(position.SideToMove, from, to);
			}

			if (phase > 0.6f && position.GivesCheck(valMove))
				moveScoreGuess += PositionEvaluator.pawnValue * mult1;

			switch (movePieceType)
			{
				case PieceTypes.Pawn:
					moveScoreGuess += PositionEvaluator.pawnValue * mult2;

					if (moveType == MoveTypes.Promotion)
					{
						var promotionType = valMove.Move.PromotedPieceType();
						moveScoreGuess += PositionEvaluator.GetPieceValue(promotionType) * 5;
					}
					break;

				case PieceTypes.King:
					moveScoreGuess += (int)Math.Pow(20, phase * 3); // value between 1-8000. Explodes near the end. At 0.5 it's around 90
					break;

				default:
					// If the target square is attacked by opponent pawn
					if (position.AttackedByPawn(to, ~position.SideToMove))
						moveScoreGuess -= 5 * PositionEvaluator.GetPieceValue(movePieceType) + 5 * PositionEvaluator.pawnValue;
					break;
			}

			valMove.Score = moveScoreGuess;

			orderedMoves[i] = valMove;
		}

		Array.Sort(orderedMoves, (a, b) => b.Score.CompareTo(a.Score));

		for (int i = 0; i < orderedMoves.Length; i++)
			orderedMoves[i].Score = 0;

		return orderedMoves;
	}
}
