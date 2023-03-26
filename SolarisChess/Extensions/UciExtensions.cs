using Rudzoft.ChessLib.Enums;
using Rudzoft.ChessLib.Extensions;
using Rudzoft.ChessLib.Protocol.UCI;
using Rudzoft.ChessLib.Types;
using File = Rudzoft.ChessLib.Types.File;

namespace SolarisChess.Extensions;

public static class UciExtensions
{
	public static string MoveToString(this IUci uci, Move move)
	{
		if (move.IsNullMove())
			return "(none)";
		var (from, to, type) = move;

		if (type == MoveTypes.Castling)
			to = Square.Create(from.Rank, to > from ? File.FileG : File.FileC);

		Span<char> s = stackalloc char[5];
		var index = 0;

		s[index++] = from.FileChar;
		s[index++] = from.RankChar;
		s[index++] = to.FileChar;
		s[index++] = to.RankChar;

		if (type == MoveTypes.Promotion)
			s[index++] = move.PromotedPieceType().GetPieceChar();

		return new string(s[..index]);
	}
}
