using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Rudzoft.ChessLib.Fen;
using Rudzoft.ChessLib.Hash.Tables.Transposition;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.ObjectPoolPolicies;
using Rudzoft.ChessLib.Protocol.UCI;
using Rudzoft.ChessLib.Types;
using Rudzoft.ChessLib.Validation;
using Rudzoft.ChessLib;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SolarisChess;
using Rudzoft.ChessLib.Factories;

namespace SolarisChess.Benchmark;

/*

| Method |    Mean |    Error |   StdDev |        Gen0 |       Gen1 | Allocated |
|------- |--------:|---------:|---------:|------------:|-----------:|----------:|
| Search | 3.921 s | 0.0524 s | 0.0490 s | 306500.0000 | 11500.0000 |   2.39 GB |

*/
[MemoryDiagnoser]
public class SearchBenchmark
{
	//public int depth = 6;
	//public Search search;
	//TimeControl time = new TimeControl();

	//[GlobalSetup]
	//public void Setup()
	//{
	//	var game = GameFactory.Create();
	//	game.NewGame();

	//	search = new Search(game, time);
	//}

	//[Benchmark(Description = "Search")]
	//public void Search()
	//{
	//	time.Initialize(0, 0, 0, 0, depth, 0, 0);
	//	search.IterativeDeepeningSearch();
	//}
}
