
using System.Diagnostics;
using System.Runtime;

namespace SolarisChess;

internal class Program
{
	private static async Task Main()
	{
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

		Engine engine = new Engine();
		await engine.Start();
	}
}