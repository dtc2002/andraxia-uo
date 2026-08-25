using System;

namespace Server.Andraxia;

internal sealed class RegionalPressureStabilizer
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private readonly RegionalPressureStore _pressure;
    private readonly RegionalConcernStore _concern;
    private TimerExecutionToken _token;

    internal RegionalPressureStabilizer(RegionalPressureStore pressure, RegionalConcernStore concern = null)
    { _pressure = pressure ?? throw new ArgumentNullException(nameof(pressure)); _concern = concern; }

    internal DateTime NextRecoveryUtc { get; private set; }
    internal bool TimerRunning => _token.Running;

    internal void Initialize(DateTime nowUtc)
    {
        ValidateUtc(nowUtc);
        NextRecoveryUtc = nowUtc + Interval;
    }

    internal void Restore(DateTime nextRecoveryUtc)
    {
        ValidateUtc(nextRecoveryUtc);
        NextRecoveryUtc = nextRecoveryUtc;
    }

    internal void Recover(DateTime nowUtc)
    {
        ValidateUtc(nowUtc);
        _token.Cancel();
        if (NextRecoveryUtc == default)
        {
            Initialize(nowUtc);
        }
        else if (NextRecoveryUtc <= nowUtc)
        {
            var elapsedIntervals = checked((long)((nowUtc - NextRecoveryUtc).Ticks / Interval.Ticks) + 1);
            MoveTowardBaseline(elapsedIntervals);
            _concern?.Stabilize(elapsedIntervals);
            NextRecoveryUtc += TimeSpan.FromTicks(checked(elapsedIntervals * Interval.Ticks));
        }
        Arm(nowUtc);
    }

    internal void StopTimer() => _token.Cancel();

    private void OnTick()
    {
        var nowUtc = Core.Now;
        MoveTowardBaseline(1);
        _concern?.Stabilize(1);
        NextRecoveryUtc = nowUtc + Interval;
        Arm(nowUtc);
    }

    private void MoveTowardBaseline(long intervals)
    {
        // Stabilization is independent of event lifecycle and world condition; it only adjusts pressure.
        var current = _pressure.Britain;
        var distance = RegionalPressureStore.DefaultBritainPressure - current;
        var movement = (int)Math.Min(Math.Abs((long)distance), intervals) * Math.Sign(distance);
        if (movement != 0)
        {
            _pressure.AdjustBritain(movement, "Natural stabilization");
        }
    }

    private void Arm(DateTime nowUtc)
    {
        var delay = NextRecoveryUtc > nowUtc ? NextRecoveryUtc - nowUtc : TimeSpan.Zero;
        Timer.StartTimer(delay, OnTick, out _token);
    }

    private static void ValidateUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Regional-pressure recovery time must be UTC.");
        }
    }
}
