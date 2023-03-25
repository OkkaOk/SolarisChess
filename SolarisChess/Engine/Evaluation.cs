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

	const int KnightPhase = 1;
	const int BishopPhase = 1;
	const int RookPhase = 2;
	const int QueenPhase = 4;
	const int TotalPhase = KnightPhase * 4 + BishopPhase * 4 + RookPhase * 4 + QueenPhase * 2;

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
		if (position.IsDraw())
			return 0;

		int eval = 0;
		int whiteEval = 0;
		int blackEval = 0;

		float phase = CalculatePhase();

		//if (KpkBitBase.IsDraw(position)) // TODO ?
		//	return 0;

		CountMaterial(out int whiteMaterial, out int blackMaterial, out int whiteMaterialNoPawns, out int blackMaterialNoPawns);

		eval += whiteMaterial - blackMaterial;
		eval += EvaluatePieceSquareTables(phase);
		eval += CalculateMobilityScore();

		Player leadingSide = whiteEval > blackEval ? Player.White : Player.Black;
		MopUpEval(leadingSide, phase, ref eval);

		int perspective = position.SideToMove.IsWhite ? 1 : -1;
		return eval * perspective;
	}

	int MidGameEval(float phase)
	{
		return 0;
	}

	int EndGameEval(float phase)
	{
		return 0;
	}

	/// <summary>
	///	Calculates the current phase of the game
	/// </summary>
	/// <returns>Value between 0 and 1
	/// <br>1 -> Total endgame (only pawns)</br>
	/// <br>0 -> Every piece on the board</br></returns>
	float CalculatePhase()
	{
		int phase = TotalPhase;

		phase -= position.Pieces(PieceTypes.Knight).Count * KnightPhase;
		phase -= position.Pieces(PieceTypes.Bishop).Count * BishopPhase;
		phase -= position.Pieces(PieceTypes.Rook).Count * RookPhase;
		phase -= position.Pieces(PieceTypes.Queen).Count * QueenPhase;

		return Max(0, (phase * 256 + (TotalPhase / 2)) / (TotalPhase * 256));
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

	private int CalculateMobilityScore()
	{
		int GetScore(Player side)
		{
			int mobilityScore = 0;

			var attacks = position.AttacksBy(PieceTypes.AllPieces, side);
			foreach (var attackSquare in attacks)
			{
				mobilityScore += 1;

				if (position.AttackedByPawn(attackSquare, ~side))
					continue;

				mobilityScore += 3;
			}

			return mobilityScore;
		}

		return GetScore(Player.White) - GetScore(Player.Black);
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

	int EvaluatePieceSquareTables(float phase)
	{
		int GetScore(Player side)
		{
			int value = 0;
			bool isWhite = side == Player.White;

			value += EvaluatePieceSquareTable(PieceSquareTable.pawns, position.Pieces(PieceTypes.Pawn, side), isWhite);
			value += EvaluatePieceSquareTable(PieceSquareTable.rooks, position.Pieces(PieceTypes.Rook, side), isWhite);
			value += EvaluatePieceSquareTable(PieceSquareTable.knights, position.Pieces(PieceTypes.Knight, side), isWhite);
			value += EvaluatePieceSquareTable(PieceSquareTable.bishops, position.Pieces(PieceTypes.Bishop, side), isWhite);
			value += EvaluatePieceSquareTable(PieceSquareTable.queens, position.Pieces(PieceTypes.Queen, side), isWhite);

			int kingEarlyPhase = PieceSquareTable.Read(PieceSquareTable.kingMiddle, position.GetKingSquare(side), isWhite);
			value += (int)(kingEarlyPhase * (1 - phase));

			int kingLateGame = PieceSquareTable.Read(PieceSquareTable.kingEnd, position.GetKingSquare(side), isWhite);
			value += (int)(kingLateGame * phase);

			return value;
		}

		return GetScore(Player.White) - GetScore(Player.Black);
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
