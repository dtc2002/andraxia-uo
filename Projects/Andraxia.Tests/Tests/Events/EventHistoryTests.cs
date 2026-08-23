using System;
using System.Linq;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class EventHistoryTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private AndraxiaEventService _service;
    private AndraxiaAutoEventGenerator _generator;

    [Fact]
    public void ThirtyThirdTerminalEventEvictsOldestAndRetainsActive()
    {
        var store = new EventStore(KnownEvents.Definitions);
        var activeId = EventInstanceId.New();
        Assert.Equal(EventRestoreFailure.None, Restore(store, activeId, EventLifecycleState.Active, null));
        var terminalIds = Enumerable.Range(0, 33)
            .Select(index => new EventInstanceId(Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}")))
            .ToArray();
        for (var index = 0; index < terminalIds.Length; index++)
        {
            Assert.Equal(
                EventRestoreFailure.None,
                Restore(store, terminalIds[index], EventLifecycleState.Succeeded, StartUtc.AddMinutes(index))
            );
        }

        Assert.Equal(EventStore.MaximumTerminalHistory, store.EnumerateInstances().Count(instance =>
            instance.State != EventLifecycleState.Active));
        Assert.True(store.TryGetInstance(activeId, out _));
        Assert.False(store.TryGetInstance(terminalIds[0], out _));
    }

    [Fact]
    public void TerminalTieEvictsLowestInstanceIdDeterministically()
    {
        var store = new EventStore(KnownEvents.Definitions);
        var ids = Enumerable.Range(1, 33)
            .Select(index => new EventInstanceId(Guid.Parse($"10000000-0000-0000-0000-{index:D12}")))
            .ToArray();
        foreach (var id in ids)
        {
            Assert.Equal(EventRestoreFailure.None, Restore(store, id, EventLifecycleState.Failed, StartUtc));
        }

        Assert.False(store.TryGetInstance(ids[0], out _));
        Assert.All(ids.Skip(1), id => Assert.True(store.TryGetInstance(id, out _)));
    }

    [Fact]
    public void OversizedLoadedHistoryPrunesOnceToNewestThirtyTwo()
    {
        var store = new EventStore(KnownEvents.Definitions);
        for (var index = 0; index < 40; index++)
        {
            Assert.Equal(
                EventRestoreFailure.None,
                Restore(
                    store,
                    new EventInstanceId(Guid.Parse($"20000000-0000-0000-0000-{index + 1:D12}")),
                    EventLifecycleState.Succeeded,
                    StartUtc.AddMinutes(index),
                    false
                )
            );
        }

        Assert.Equal(8, store.PruneTerminalHistory());
        Assert.Equal(32, store.EnumerateInstances().Count());
        Assert.Equal(0, store.PruneTerminalHistory());
    }

    [Fact]
    public void CommandViewsSeparateSummaryHistoryAndMobileDetails()
    {
        var store = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var encounter = new TestEventEncounterSpawner();
        _service = new AndraxiaEventService(store, states, encounter);
        _generator = new AndraxiaAutoEventGenerator(store, states, _service);
        var active = _service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);

        var summary = EventCommands.BuildSummaryLines(store, _service, _generator);
        var detail = EventCommands.BuildDetailLines(store, _service, active.EventResult.Instance.Id);
        Assert.Contains(summary, static line => line.Contains("Britain Brigand Disturbance"));
        Assert.DoesNotContain(summary, static line => line.Contains("Type="));
        Assert.Contains(detail, static line => line.Contains("Type="));

        Assert.True(_service.Complete(active.EventResult.Instance.Id, StartUtc.AddMinutes(1)).Succeeded);
        var history = EventCommands.BuildHistoryLines(store);
        Assert.Contains(history, line => line.Contains(active.EventResult.Instance.Id.ToString()));
    }

    [Fact]
    public void HistoryIsNewestFirst()
    {
        var store = new EventStore(KnownEvents.Definitions);
        var older = new EventInstanceId(Guid.Parse("30000000-0000-0000-0000-000000000001"));
        var newer = new EventInstanceId(Guid.Parse("30000000-0000-0000-0000-000000000002"));
        Restore(store, older, EventLifecycleState.Succeeded, StartUtc);
        Restore(store, newer, EventLifecycleState.Failed, StartUtc.AddMinutes(1));

        var lines = EventCommands.BuildHistoryLines(store);

        Assert.Contains(newer.ToString(), lines[1]);
        Assert.Contains(older.ToString(), lines[2]);
    }

    private static EventRestoreFailure Restore(
        EventStore store,
        EventInstanceId id,
        EventLifecycleState state,
        DateTime? completedUtc,
        bool prune = true
    ) => store.Restore(
        id,
        KnownEvents.BritainDisturbance,
        KnownEvents.Britain,
        state,
        StartUtc,
        StartUtc.AddMinutes(5),
        completedUtc,
        [],
        KnownEncounterLocations.BritainRoadNorth,
        prune
    );

    public void Dispose()
    {
        _generator?.StopTimer();
        _service?.StopExpirationTimer();
    }
}
