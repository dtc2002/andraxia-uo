using System;
using System.Reflection;
using Server;

namespace Andraxia.Tests;

/// <summary>
/// Controls ModernUO's process-wide clock and timer wheel for deterministic tests.
/// </summary>
/// <remarks>
/// A simulation clock assumes exclusive ownership of ModernUO's process-wide timer wheel for the duration of the test.
/// Tests must run sequentially and stop any recurring timers they create before disposing the clock.
/// </remarks>
internal sealed class SimulationClock : IDisposable
{
    private static readonly FieldInfo _coreNowField = typeof(Core).GetField(
        "_now",
        BindingFlags.Static | BindingFlags.NonPublic
    ) ?? throw new InvalidOperationException("Core._now was not found.");

    private readonly DateTime _previousNow;
    private long _tickCount;
    private bool _disposed;

    public SimulationClock(DateTime start)
    {
        if (start.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Simulation time must be UTC.", nameof(start));
        }

        _previousNow = Core.Now;
        SetNow(start);
        Timer.Init(0);
    }

    public DateTime Now => Core.Now;

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed), "Simulation time cannot move backward.");
        }

        if (elapsed.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ArgumentException("Simulation time must advance in whole milliseconds.", nameof(elapsed));
        }

        SetNow(Core.Now + elapsed);
        _tickCount = checked(_tickCount + (long)elapsed.TotalMilliseconds);
        Timer.Slice(_tickCount);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SetNow(_previousNow);
        _disposed = true;
    }

    private static void SetNow(DateTime value) => _coreNowField.SetValue(null, value);
}
