using Rudzoft.ChessLib.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using static System.Math;

namespace SolarisChess;

public class TimeControl
{
	public static readonly int TIME_MARGIN = 20;
	public static readonly int BRANCHING_FACTOR_ESTIMATE = 3;
    public static readonly int MAX_TIME_REMAINING = int.MaxValue / 3; //large but not too large to cause overflow issues
    public static readonly int MIN_MOVE_TIME = 200;

    private int startTime;
    private int remaining;
    private int opponentRemaining;
    private int increment;
    private int movesToGo;
    private int searchDepth;
    private long maxNodes;
    private int moveTime;
    public bool isPondering;

    //public CancellationTokenSource? monitorTokenSource;

    public bool IsInfinite { get; private set; }

    private long t0 = -1;
    private long tN = -1;

	//public int AllocatedTimePerMove => TimeRemaining / movesToGo - TIME_MARGIN;
	private int TimeRemaining => moveTime == 0 ? remaining - TIME_MARGIN : moveTime;

    private static long Now => Stopwatch.GetTimestamp();
    public int Elapsed => MilliSeconds(Now - t0);
    public int ElapsedInterval => MilliSeconds(Now - tN);

	public int AllocatedTimePerMove
    {
        get
        {
            if (moveTime != 0)
                return moveTime - TIME_MARGIN;

            if (startTime != 0)
            {
                // Add some of the time lead to the available time
                //int lead = (int)(Max(0, Abs(remaining) - Abs(opponentRemaining)) * 0.2f);
				int lead = (int)MathExtensions.Clamp((remaining - opponentRemaining) * 0.2d, 0, 4000);

                if (movesToGo != 0)
			        return remaining / movesToGo - TIME_MARGIN + lead;

                //float percentage = Max(0.2f, TimeRemaining / startTime);
                //float divisor = 500 * percentage;
                double phaseMultiplier = Max(0.6f, PositionEvaluator.CalculatePhase(Engine.Game.Pos));

                //return (int)(Pow(TimeRemaining, 1.2f) * phaseMultiplier / divisor) + lead;

                var time = MathExtensions.Clamp(TimeRemaining * phaseMultiplier, MIN_MOVE_TIME, 500000);
                return Max(MIN_MOVE_TIME, (int)(-0.0000001d * Pow(time, 2d) + 0.07d * time + 100) + lead);
			}

            return MAX_TIME_REMAINING;
		}
    }

    private static int MilliSeconds(long ticks)
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
        isPondering = false;
        //monitorTokenSource = null;
        t0 = Now;
        tN = t0;
    }

    public void StartInterval()
    {
        tN = Now;
    }

    public void Stop()
    {
        //monitorTokenSource?.Cancel();
        isPondering = false;
        remaining = 0;
    }

	public void PonderHit()
	{
		t0 = Now;
		isPondering = false;
	}

	public void Initialize(int remaining, int opponentRemaining, int increment, int movesToGo, int searchDepth, long maxNodes, int moveTime, bool isPondering = false)
    {
        if (startTime == 0)
            startTime = remaining;

		t0 = Now;
        tN = Now;
		this.remaining = remaining;
		this.opponentRemaining = opponentRemaining;
		this.increment = increment;
		this.movesToGo = movesToGo;
        this.searchDepth = searchDepth;
        this.maxNodes = maxNodes;
        this.moveTime = moveTime;
        this.isPondering = isPondering;

        IsInfinite = remaining == 0 && increment == 0 && movesToGo == 0 && moveTime == 0;
	}

    public bool CanSearchDeeper(int currentDepth, long currentNodeCount, bool alreadySearching = false)
    {
        if (isPondering)
            return true;

        if (IsInfinite)
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

    public bool HasTimeForNextDepth()
    {
		int estimate = Elapsed + ElapsedInterval * BRANCHING_FACTOR_ESTIMATE;

		//we need to stay within the per-move time budget
		if (estimate > AllocatedTimePerMove + increment)
			return false;

		//shouldn't spend more then the 2x the average on a move
		//if (estimate > 2 * AllocatedTimePerMove)
		//	return false;

		//can't afford the estimate
		if (estimate > TimeRemaining)
			return false;

        return true;
	}
}
