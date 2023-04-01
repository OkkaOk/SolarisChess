using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SolarisChess.Extensions;

public static class MathExtensions
{
	public static double Map(double value, double fromLow, double fromHigh, double toLow, double toHigh)
	{
		double fromRange = fromHigh - fromLow;
		double toRange = toHigh - toLow;
		double scaleFactor = toRange / fromRange;
		return toLow + ((value - fromLow) * scaleFactor);
	}

	public static int Map(int value, int fromLow, int fromHigh, int toLow, int toHigh)
	{
		int fromRange = fromHigh - fromLow;
		int toRange = toHigh - toLow;
		float scaleFactor = toRange / fromRange;
		return toLow + (int)((value - fromLow) * scaleFactor);
	}
}
