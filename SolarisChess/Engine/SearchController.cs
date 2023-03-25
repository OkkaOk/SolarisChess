using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SolarisChess;

public class SearchController
{
	public static readonly int TIME_MARGIN = 20;
	public static readonly int BRANCHING_FACTOR_ESTIMATE = 3;
    public static readonly int MAX_TIME_REMAINING = int.MaxValue / 3; //large but not too large to cause overflow issues

    private int remaining;
    private int increment;
    private int movesToGo;
    private int searchDepth;
    private long maxNodes;
    private int moveTime;
    public bool isInfinite { get; private set; }

    private long t0 = -1;
    private long tN = -1;

	//public int AllocatedTimePerMove => TimeRemaining / movesToGo - TIME_MARGIN;
	private int TimeRemaining => remaining - TIME_MARGIN;

    private long Now => Stopwatch.GetTimestamp();
    public int Elapsed => MilliSeconds(Now - t0);
    public int ElapsedInterval => MilliSeconds(Now - tN);

	public int AllocatedTimePerMove
    {
        get
        {
            if (moveTime != 0)
                return moveTime - TIME_MARGIN;

            if (remaining != 0)
            {
                if (movesToGo != 0)
			        return (int)(Math.Pow(TimeRemaining, 1.2f) / (5 * movesToGo)) - TIME_MARGIN;

                return (int)(Math.Pow(TimeRemaining, 1.2f) / 200);
			}

            return MAX_TIME_REMAINING;
		}
    }

    private int MilliSeconds(long ticks)
    {
        double dt = ticks / (double)Stopwatch.Frequency;
        return (int)(1000 * dt);
    }

    private void Reset()
    {
        movesToGo = 1;
        increment = 0;
        remaining = MAX_TIME_REMAINING;
        t0 = Now;
        tN = t0;
    }

    public void StartInterval()
    {
        remaining += increment;
        tN = Now;
    }

    public void Stop()
    {
        //this will cause CanSearchDeeper() and CheckTimeBudget() to evaluate to 'false'
        remaining = 0;
    }

    public void Initialize(int remaining, int increment, int movesToGo, int searchDepth, long maxNodes, int moveTime)
    {
		t0 = Now;
        tN = Now;
		this.remaining = remaining;
		this.increment = increment;
		this.movesToGo = movesToGo;
        this.searchDepth = searchDepth;
        this.maxNodes = maxNodes;
        this.moveTime = moveTime;

        isInfinite = remaining == 0 && increment == 0 && movesToGo == 0 && moveTime == 0;
	}

    public bool CanSearchDeeper(int currentDepth, long currentNodeCount)
    {
        if (isInfinite)
        {
			if (searchDepth != 0 && currentDepth > searchDepth)
				return false;

			if (maxNodes != 0 && currentNodeCount > maxNodes)
				return false;

			return true;
		}

        int elapsed = Elapsed;

        int estimate = elapsed + ElapsedInterval * BRANCHING_FACTOR_ESTIMATE;

        //no increment... we need to stay within the per-move time budget
        if (increment == 0 && estimate > AllocatedTimePerMove)
            return false;

        //we have already exceeded the average move
        if (elapsed > AllocatedTimePerMove)
            return false;

        //shouldn't spend more then the 2x the average on a move
        if (estimate > 2 * AllocatedTimePerMove)
            return false;

        //can't afford the estimate
        if (estimate > TimeRemaining)
            return false;

        //all conditions fulfilled
        return true;
    }

    public bool CheckTimeBudget()
    {
        if (increment == 0)
            return Elapsed > AllocatedTimePerMove;
        else
            return Elapsed > TimeRemaining;
    }
}
