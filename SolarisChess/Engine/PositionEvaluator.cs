using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Evaluation;
using Rudzoft.ChessLib.Fen;
using Rudzoft.ChessLib.Hash.Tables.Transposition;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.Types;
using SolarisChess.Extensions;
using System;
using static System.Math;

namespace SolarisChess;

public class PositionEvaluator
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

	/// <summary>
	/// (Start) 0 - 1 (Endgame)
	/// </summary>
	static float phase = 0;
	
	// TODO: Remove static when implementing threading.

	// Performs static evaluation of the current position.
	// The position is assumed to be 'quiet', i.e no captures are available that could drastically affect the evaluation.
	// The score that's returned is given from the perspective of whoever's turn it is to move.
	// So a positive score means the player who's turn it is to move has an advantage, while a negative score indicates a disadvantage.
	public static int Evaluate(IPosition position)
	{
		int eval = 0;
		//int whiteEval = 0;
		//int blackEval = 0;

		phase = CalculatePhase(position);

		//if (KpkBitBase.IsDraw(position)) // TODO ?
		//	return 0;

		CountMaterial(position, out int whiteMaterial, out int blackMaterial, out int whiteMaterialNoPawns, out int blackMaterialNoPawns);

		eval += whiteMaterial - blackMaterial;
		eval += EvaluatePieceSquareTables(position);
		eval += CalculateMobilityScore(position);
		eval += EvaluatePawnStructure(position, Player.White) - EvaluatePawnStructure(position, Player.Black);

		if (Abs(eval) > 200 && phase > 0.5f)
		{
			Player leadingSide = eval > 0 ? Player.White : Player.Black;
			eval += MopUpEval(position, leadingSide);
		}

		int perspective = position.SideToMove.IsWhite ? 1 : -1;
		eval *= perspective;

		return eval;
	}

	static int MidGameEval(float phase)
	{
		return 0;
	}

	static int EndGameEval(float phase)
	{
		return 0;
	}

	/// <summary>
	///	Calculates the current phase of the Game
	/// </summary>
	/// <returns>Value between 0 and 1
	/// <br>1 -> Total endgame (only pawns)</br>
	/// <br>0 -> Every piece on the board</br></returns>
	public static float CalculatePhase(IPosition position)
	{
		int phase = TotalPhase;

		phase -= position.Pieces(PieceTypes.Knight).Count * KnightPhase;
		phase -= position.Pieces(PieceTypes.Bishop).Count * BishopPhase;
		phase -= position.Pieces(PieceTypes.Rook).Count * RookPhase;
		phase -= position.Pieces(PieceTypes.Queen).Count * QueenPhase;

		return (phase * 256f + (TotalPhase / 2f)) / (TotalPhase * 256f);
	}

	static void CountMaterial(IPosition position, out int whiteMaterial, out int blackMaterial, out int whiteMaterialNoPawns, out int blackMaterialNoPawns)
	{
		var whitePawnCount = position.PieceCount(PieceTypes.Pawn, Player.White);
		var whiteKnightCount = position.PieceCount(PieceTypes.Knight, Player.White);
		var whiteBishopCount = position.PieceCount(PieceTypes.Bishop, Player.White);
		var whiteRookCount = position.PieceCount(PieceTypes.Rook, Player.White);
		var whiteQueenCount = position.PieceCount(PieceTypes.Queen, Player.White);

		whiteMaterialNoPawns = whiteKnightCount * knightValue +
								whiteBishopCount * bishopValue +
								whiteRookCount * rookValue +
								whiteQueenCount * queenValue;
		whiteMaterial = whiteMaterialNoPawns + whitePawnCount * pawnValue;

		var blackPawnCount = position.PieceCount(PieceTypes.Pawn, Player.Black);
		var blackKnightCount = position.PieceCount(PieceTypes.Knight, Player.Black);
		var blackBishopCount = position.PieceCount(PieceTypes.Bishop, Player.Black);
		var blackRookCount = position.PieceCount(PieceTypes.Rook, Player.Black);
		var blackQueenCount = position.PieceCount(PieceTypes.Queen, Player.Black);

		blackMaterialNoPawns = blackKnightCount * knightValue +
								blackBishopCount * bishopValue +
								blackRookCount * rookValue +
								blackQueenCount * queenValue;
		blackMaterial = blackMaterialNoPawns + blackPawnCount * pawnValue;
	}

	static int EvaluatePawnStructure(IPosition position, Player side)
	{
		int score = 0;

		var friendlyPawns = position.Pieces(PieceTypes.Pawn, side);
		var opponentPawns = position.Pieces(PieceTypes.Pawn, ~side);

		while (friendlyPawns)
		{
			var square = BitBoards.PopLsb(ref friendlyPawns);
			var file = square.File;
			var rank = square.Rank;
			var relativeRank = square.RelativeRank(side);

			// Multiple pawns on the same file
			var pawnCountOnFile = (friendlyPawns & BitBoards.FileBB(file)).Count;
			if (pawnCountOnFile > 1)
			{
				score -= (pawnCountOnFile - 1) * 10;
			}

			int friendlyPawnCountOnAdjacentFiles = (friendlyPawns & BitBoards.AdjacentFiles(file)).Count;
			int opponentPawnCountOnAdjacentFiles = (opponentPawns & BitBoards.AdjacentFiles(file)).Count;

			// Isolated pawns
			if (friendlyPawnCountOnAdjacentFiles == 0)
			{
				score -= 10;
			}

			// Backward pawns
			if (friendlyPawnCountOnAdjacentFiles == 0 && opponentPawnCountOnAdjacentFiles > 0)
			{
				score -= 10;
			}

			// Get the number of pawns this pawn is connected to
			var connectedPawns = BitBoards.PopCount(friendlyPawns & square.PawnAttack(side));

			// Promote pawn chains
			if (connectedPawns > 0)
			{
				score += 10;
			}

			// Passed pawns
			if (position.IsPawnPassedAt(side, square))
				score += relativeRank.AsInt() * 10 * (connectedPawns + 1); // Higher score for pawns closer to promotion
		}

		return score;
	}

	public static int CalculateMobilityScore(IPosition position)
	{
		int GetScore(Player side)
		{
			//int mobilityScore = 0;

			var attacks = position.AttacksBy(PieceTypes.Knight, side) |
							position.AttacksBy(PieceTypes.Bishop, side) |
							position.AttacksBy(PieceTypes.Rook, side) |
							position.AttacksBy(PieceTypes.Queen, side);
			//while (attacks)
			//{
			//	var attackSquare = BitBoards.PopLsb(ref attacks);
			//	mobilityScore += 1;

			//	if (position.AttackedByPawn(attackSquare, ~side))
			//		continue;

			//	mobilityScore += 2;
			//}

			return attacks.Count;
		}
		//int GetScore(Player side)
		//{
		//	int mobilityScore = 0;

		//	if (position.InCheck)
		//		return 0;

		//	bool needToSwitch = side != position.SideToMove;
		//	if (needToSwitch)
		//		position.MakeNullMove(null);

		//	var moves = position.GenerateMoves();
		//	foreach (var move in moves)
		//	{
		//		mobilityScore += 1;

		//		if (position.AttackedByPawn(move.Move.ToSquare(), ~side))
		//			continue;

		//		mobilityScore += 3;
		//	}

		//	if (needToSwitch)
		//		position.TakeNullMove();

		//	return mobilityScore;
		//}

		return GetScore(Player.White) - GetScore(Player.Black);
	}

	static int MopUpEval(IPosition position, Player winningSide)
	{
		int mopUpScore = 0;
		int perspective = winningSide == Player.White ? 1 : -1;

		var friendlyKingSquare = position.GetKingSquare(winningSide);
		var opponentKingSquare = position.GetKingSquare(~winningSide);

		int cmd = opponentKingSquare.CenterManhattanDistance();
		int md = friendlyKingSquare.ManhattanDistance(opponentKingSquare);

		// Try to push opponent king to the edge
		mopUpScore += cmd * 10;

		// Try to get as close as possible to the opponent king
		mopUpScore += (14 - md) * 4;

		return (int)(phase * mopUpScore * perspective);
	}

	static int EvaluatePieceSquareTables(IPosition position)
	{
		int eval = 0;

		// Pawn to Queen (1-5)
		for (int i = 1; i < 6; i++)
		{
			var type = (PieceTypes)i;
			GetPieceSquareTable(type, out int[] middleTable, out int[] endTable);

			var whitePieces = position.Pieces(type, Player.White);
			var blackPieces = position.Pieces(type, Player.Black);

			while (whitePieces)
			{
				var sq = BitBoards.PopLsb(ref whitePieces);

				eval += (int)(PieceSquareTable.Read(middleTable, sq, true) * (1 - phase));
				eval += (int)(PieceSquareTable.Read(endTable, sq, true) * phase);
			}

			while (blackPieces)
			{
				var sq = BitBoards.PopLsb(ref blackPieces);

				eval -= (int)(PieceSquareTable.Read(middleTable, sq, false) * (1 - phase));
				eval -= (int)(PieceSquareTable.Read(endTable, sq, false) * phase);
			}
		}

		return eval;
	}

	static void GetPieceSquareTable(PieceTypes pieceType, out int[] middleTable, out int[] endTable)
	{
		switch (pieceType)
		{
			case PieceTypes.Pawn:
				middleTable = PieceSquareTable.pawnMiddle;
				endTable = PieceSquareTable.pawnEnd;
				break;
			case PieceTypes.Knight:
				middleTable = PieceSquareTable.knightMiddle;
				endTable = PieceSquareTable.knightEnd;
				break;
			case PieceTypes.Bishop:
				middleTable = PieceSquareTable.bishopMiddle;
				endTable = PieceSquareTable.bishopEnd;
				break;
			case PieceTypes.Rook:
				middleTable = PieceSquareTable.rookMiddle;
				endTable = PieceSquareTable.rookEnd;
				break;
			case PieceTypes.Queen:
				middleTable = PieceSquareTable.queenMiddle;
				endTable = PieceSquareTable.queenEnd;
				break;
			case PieceTypes.King:
				middleTable = PieceSquareTable.kingMiddle;
				endTable = PieceSquareTable.kingEnd;
				break;
			default:
				middleTable = new int[64];
				endTable = new int[64];
				break;
		}
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
		return (int)MathF.Ceiling((immediateMateScore - Abs(score)) / 2f);
	}
}
