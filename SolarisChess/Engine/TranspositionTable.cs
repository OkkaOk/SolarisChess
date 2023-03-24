//using ChessLib;
//using ChessLib.Hash.Tables.Transposition;
//using ChessLib.Types;
//using System.Drawing;
//using static System.Math;

//namespace SolarisChess;

//public class TranspositionTable
//{
//	private readonly int size;
//	private readonly TranspositionEntry[] entries;

//	private IPosition position;

//	private ulong Zobrist => position.State.Key.Key;
//	private int Index => (int)(Zobrist % (ulong)size);

//	public TranspositionTable(IPosition position, int size)
//	{
//		this.size = size;
//		this.position = position;

//		entries = new TranspositionEntry[size];
//	}

//	public void IncreaseAge()
//	{
//		for (int i = 0; i < size; i++)
//			entries[i].Age++;
//	}

//	public void Store(TranspositionEntry entry)
//	{
//		entries[Index] = entry;
//	}

//	public void Store(byte depth, int score, NodeType nodeType, ExtMove bestMove)
//	{
//		entries[Index] = new(Zobrist, depth, score, nodeType, bestMove);
//	}

//	public bool TryGet(out TranspositionEntry entry)
//	{
//		entry = entries[Index];
//		return entry.ZobristKey == Zobrist;
//	}
//}

//public struct TranspositionEntry
//{
//	public ulong ZobristKey { get; set; }
//	public int Depth { get; set; }
//	public int Score { get; set; }
//	public NodeType NodeType { get; set; }
//	public Move BestMove { get; set; }
//	public int Age { get; set; }

//	public TranspositionEntry(ulong key, byte depth, int score, NodeType nodeType, ExtMove bestMove)
//	{
//		ZobristKey = key;
//		Depth = depth;
//		Score = score;
//		NodeType = nodeType;
//		BestMove = bestMove;
//		Age = 0;
//	}

//	public static bool operator ==(TranspositionEntry left, TranspositionEntry right)
//	=> left.Equals(right);

//	public static bool operator !=(TranspositionEntry left, TranspositionEntry right)
//		=> !(left == right);

//	public static int GetSize()
//	{
//		return System.Runtime.InteropServices.Marshal.SizeOf<TranspositionEntry>();
//	}

//	public override bool Equals(object obj)
//		=> obj is TranspositionEntry other && Equals(other);

//	public override int GetHashCode()
//		=> HashCode.Combine(ZobristKey, Depth, Score, NodeType, BestMove);
//}

//public enum NodeType
//{
//	Exact,
//	UpperBound,
//	LowerBound
//}