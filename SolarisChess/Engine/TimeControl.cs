﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using static System.Math;

namespace SolarisChess;

public class TimeControl
{
	public static readonly int TIME_MARGIN = 20;
	public static readonly int BRANCHING_FACTOR_ESTIMATE = 3;
    public static readonly int MAX_TIME_REMAINING = int.MaxValue / 3; //large but not too large to cause overflow issues

    private int startTime;
    private int remaining;
    private int opponentRemaining;
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
                // Add some of the time lead to the available time
                int lead = (int)(Max(0, Abs(remaining) - Abs(opponentRemaining)) * 0.2f);

                if (movesToGo != 0)
			        return remaining / movesToGo - TIME_MARGIN + lead;

                float percentage = Max(0.3f, TimeRemaining / startTime);
				float divisor = 400 * percentage;

				return (int)(Pow(TimeRemaining, 1.2f) / divisor) + lead;
			}

            return MAX_TIME_REMAINING;
		}
    }

    private int MilliSeconds(long ticks)
    {
        double dt = ticks / (double)Stopwatch.Frequency;
        return (int)(1000 * dt);
    }

    public void Reset()
    {
        startTime = 0;
        remaining = MAX_TIME_REMAINING;
        opponentRemaining = MAX_TIME_REMAINING;
        increment = 0;
        movesToGo = 1;
        searchDepth = 0;
        maxNodes = 0;
        moveTime = 0;
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

    public void Initialize(int remaining, int opponentRemaining, int increment, int movesToGo, int searchDepth, long maxNodes, int moveTime)
    {
        if (startTime == 0)
            startTime = remaining;

		t0 = Now;
        tN = Now;
		this.remaining = Max(remaining, 0);
		this.opponentRemaining = Max(opponentRemaining, 0);
		this.increment = increment;
		this.movesToGo = movesToGo;
        this.searchDepth = searchDepth;
        this.maxNodes = maxNodes;
        this.moveTime = moveTime;

        isInfinite = remaining == 0 && increment == 0 && movesToGo == 0 && moveTime == 0;
	}

    public bool CanSearchDeeper(int currentDepth, long currentNodeCount, bool alreadySearching = false)
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

        //we have already exceeded the average move
        if (elapsed > AllocatedTimePerMove)
            return false;

        if (!HasTimeForNextDepth() && !alreadySearching)
            return false;

        //all conditions fulfilled
        return true;
    }

    bool HasTimeForNextDepth()
    {
		int estimate = Elapsed + ElapsedInterval * BRANCHING_FACTOR_ESTIMATE;

		//no increment... we need to stay within the per-move time budget
		if (increment == 0 && estimate > AllocatedTimePerMove)
			return false;

		//shouldn't spend more then the 2x the average on a move
		if (estimate > 2 * AllocatedTimePerMove)
			return false;

		//can't afford the estimate
		if (estimate > TimeRemaining)
			return false;

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