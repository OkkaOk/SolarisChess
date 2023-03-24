using ChessLib;
using ChessLib.Evaluation;
using ChessLib.Hash.Tables.Transposition;
using ChessLib.Types;
using System;
using static System.Math;

namespace SolarisChess;

public class Evaluation
{
	public const int pawnValue = 100;
	public const int knightValue = 325;
	public const int bishopValue = 350;
	public const int rookValue = 500;
	public const int queenValue = 900;

	public const int immediateMateScore = 100000;
	public const int positiveInfinity = 9999999;
	public const int negativeInfinity = -positiveInfinity;

	const float endgameMaterialStart = rookValue * 2 + bishopValue + knightValue;

	IPosition position;

	public Evaluation(IPosition position)
	{
		this.position = position;
	}

	// Performs static evaluation of the current position.
	// The position is assumed to be 'quiet', i.e no captures are available that could drastically affect the evaluation.
	// The score that's returned is given from the perspective of whoever's turn it is to move.
	// So a positive score means the player who's turn it is to move has an advantage, while a negative score indicates a disadvantage.
	public int Evaluate()
	{
		int eval = 0;
		int whiteEval = 0;
		int blackEval = 0;

		//if (KpkBitBase.IsDraw(position)) // TODO ?
		//	return 0;

		CountMaterial(out int whiteMaterial, out int blackMaterial, out int whiteMaterialNoPawns, out int blackMaterialNoPawns);
		eval += whiteMaterial - blackMaterial;
		
		int whiteMaterialWithoutPawns = whiteMaterial - position.Pieces(PieceTypes.Pawn, Player.White).Count * pawnValue;
		int blackMaterialWithoutPawns = blackMaterial - position.Pieces(PieceTypes.Pawn, Player.Black).Count * pawnValue;
		float whiteEndgamePhaseWeight = EndgamePhaseWeight(whiteMaterialWithoutPawns);
		float blackEndgamePhaseWeight = EndgamePhaseWeight(blackMaterialWithoutPawns);

		whiteEval += whiteMaterial;
		blackEval += blackMaterial;

		Player leadingSide = whiteEval > blackEval ? Player.White : Player.Black;
		float leadingSideEndGameWeight = leadingSide.IsWhite ? whiteEndgamePhaseWeight : blackEndgamePhaseWeight;
		MopUpEval(leadingSide, leadingSideEndGameWeight, ref eval);


		whiteEval += MopUpEval(position, Player.White, whiteMaterial, blackMaterial, blackEndgamePhaseWeight);
		blackEval += MopUpEval(position, Player.Black, blackMaterial, whiteMaterial, whiteEndgamePhaseWeight);

		whiteEval += EvaluatePieceSquareTables(position, blackEndgamePhaseWeight, Player.White);
		blackEval += EvaluatePieceSquareTables(position, whiteEndgamePhaseWeight, Player.Black);
		whiteEval += CalculateMobilityScore(Player.White);
		blackEval += CalculateMobilityScore(Player.Black);

		eval += whiteEval - blackEval;


		int perspective = position.SideToMove.IsWhite ? 1 : -1;
		return eval * perspective;
	}

	void CalculateEndGameEval(ref int eval)
	{
		
	}

	void CountMaterial(out int whiteMaterial, out int blackMaterial, out int whiteMaterialNoPawns, out int blackMaterialNoPawns)
	{
		whiteMaterial = blackMaterial = whiteMaterialNoPawns = blackMaterialNoPawns = 0;

		for (short squareIndex = 0; squareIndex < 64; squareIndex++)
		{
			var piece = position.GetPiece(squareIndex);

			int value = GetPieceValue(piece.Type());

			if (piece.IsWhite)
			{
				whiteMaterial += value;
				if (piece.Type() != PieceTypes.Pawn)
					whiteMaterialNoPawns += value;
			}
			else
			{
				blackMaterial += value;
				if (piece.Type() != PieceTypes.Pawn)
					blackMaterialNoPawns += value;
			}
		}
	}

	static int CalculateMaterialScore()
	{
		int whiteMaterial = 0;
		int blackMaterial = 0;

		PieceValue pval = new();

		for (short squareIndex = 0; squareIndex < 64; squareIndex++)
		{
			var piece = pos.GetPiece(squareIndex);

			int value = GetPieceValue(piece.Type());

			if (piece.IsWhite)
				whiteMaterial += value;
			else
				blackMaterial += value;
		}

		return whiteMaterial - blackMaterial;
	}

	private int CalculateMobilityScore(Player side)
	{
		int mobilityScore = 0;

		var attacks = position.AllAttacksBy(side);
		foreach (var attackSquare in attacks)
		{
			mobilityScore += 1;

			if (position.AttackedByPawn(attackSquare, ~side))
				continue;

			mobilityScore += 3;
		}

		return mobilityScore;
	}

	static int MopUpEval(IPosition position, Player winningSide, int myMaterial, int opponentMaterial, float endgameWeight)
	{
		int mopUpScore = 0;
		if (myMaterial > opponentMaterial + pawnValue * 2 && endgameWeight > 0)
		{
			var friendlyKingSquare = position.GetKingSquare(winningSide);
			var opponentKingSquare = position.GetKingSquare(~winningSide);

			int cmd = opponentKingSquare.CenterManhattanDistance();
			int md = friendlyKingSquare.ManhattanDistance(opponentKingSquare);

			// Try to push opponent king to the edge
			mopUpScore += cmd * 10;

			// Try to get as close as possible to the opponent king
			mopUpScore += (14 - md) * 4;

			return (int)(endgameWeight * mopUpScore);
		}
		return 0;
	}

	void MopUpEval(Player winningSide, float endgameWeight, ref int eval)
	{
		if (Abs(eval) < 2)
			return;

		int mopUpScore = 0;
		var friendlyKingSquare = position.GetKingSquare(position.SideToMove);
		var opponentKingSquare = position.GetKingSquare(~position.SideToMove);

		int cmd = opponentKingSquare.CenterManhattanDistance();
		int md = friendlyKingSquare.ManhattanDistance(opponentKingSquare);

		// Try to push opponent king to the edge
		mopUpScore += cmd * 10;

		// Try to get as close as possible to the opponent king
		mopUpScore += (14 - md) * 4;

		eval += (int)(endgameWeight * mopUpScore);
	}

	static int EvaluatePieceSquareTables(IPosition position, float endgamePhaseWeight, Player side)
	{
		int value = 0;
		bool isWhite = side == Player.White;

		value += EvaluatePieceSquareTable(PieceSquareTable.pawns, position.Pieces(PieceTypes.Pawn, side), isWhite);
		value += EvaluatePieceSquareTable(PieceSquareTable.rooks, position.Pieces(PieceTypes.Rook, side), isWhite);
		value += EvaluatePieceSquareTable(PieceSquareTable.knights, position.Pieces(PieceTypes.Knight, side), isWhite);
		value += EvaluatePieceSquareTable(PieceSquareTable.bishops, position.Pieces(PieceTypes.Bishop, side), isWhite);
		value += EvaluatePieceSquareTable(PieceSquareTable.queens, position.Pieces(PieceTypes.Queen, side), isWhite);

		int kingEarlyPhase = PieceSquareTable.Read(PieceSquareTable.kingMiddle, position.GetKingSquare(side), isWhite);
		value += (int)(kingEarlyPhase * (1 - endgamePhaseWeight));

		int kingLateGame = PieceSquareTable.Read(PieceSquareTable.kingEnd, position.GetKingSquare(side), isWhite);
		value += (int)(kingLateGame * endgamePhaseWeight);

		return value;
	}

	static int EvaluatePieceSquareTable(int[] table, BitBoard squares, bool isWhite)
	{
		int value = 0;
		foreach (var square in squares)
		{
			value += PieceSquareTable.Read(table, square, isWhite);
		}
		return value;
	}

	public static int GetPieceValue(PieceTypes pieceType)
	{
		switch (pieceType)
		{
			case PieceTypes.Pawn:
				return pawnValue;
			case PieceTypes.Knight:
				return knightValue;
			case PieceTypes.Bishop:
				return bishopValue;
			case PieceTypes.Rook:
				return rookValue;
			case PieceTypes.Queen:
				return queenValue;
			default:
				return 0;
		}
	}

	static float EndgamePhaseWeight(int materialCountWithoutPawns)
	{
		const float multiplier = 1 / endgameMaterialStart;
		return 1 - Min(1, materialCountWithoutPawns * multiplier);
	}

	public static bool IsMateScore(int score)
	{
		const int maxMateDepth = 1000;
		return Abs(score) > immediateMateScore - maxMateDepth;
	}

	public static int NumPlyToMateFromScore(int score)
	{
		return immediateMateScore - Abs(score);
	}

	public static int NumMovesToMateFromScore(int score)
	{
		return (int)MathF.Ceiling((immediateMateScore - Abs(score)) / 2);
	}
}
