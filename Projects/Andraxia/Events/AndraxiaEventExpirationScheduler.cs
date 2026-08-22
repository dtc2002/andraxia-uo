using System;
using System.Linq;

namespace Server.Andraxia;

internal sealed class AndraxiaEventExpirationScheduler
{
    private readonly EventStore _events;
    private readonly Action<DateTime> _advance;
    private TimerExecutionToken _token;

    public AndraxiaEventExpirationScheduler(EventStore events, Action<DateTime> advance)
    {
        _events = events;
        _advance = advance;
    }

    internal bool TimerRunning => _token.Running;
    internal DateTime? NextExpirationUtc { get; private set; }

    internal void Rearm(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Scheduler time must be UTC.", nameof(nowUtc));
        }

        Cancel();

        var next = _events.EnumerateInstances()
            .Where(static instance => instance.State == EventLifecycleState.Active)
            .OrderBy(static instance => instance.ExpiresUtc)
            .ThenBy(static instance => instance.Id.Value)
            .FirstOrDefault();

        if (next == null)
        {
            return;
        }

        NextExpirationUtc = next.ExpiresUtc;
        var delay = next.ExpiresUtc > nowUtc ? next.ExpiresUtc - nowUtc : TimeSpan.Zero;
        Timer.StartTimer(delay, OnTimer, out _token);
    }

    internal void Cancel()
    {
        _token.Cancel();
        NextExpirationUtc = null;
    }

    private void OnTimer() => _advance(Core.Now);
}
