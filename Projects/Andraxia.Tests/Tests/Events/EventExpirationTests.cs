using System;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public class EventExpirationTests
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly EventInstanceId FirstId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public void AdvanceImmediatelyBeforeDeadlineLeavesEventActive()
    {
        using var context = new TestContext();
        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, FirstId, StartUtc).Succeeded);

        context.Service.Advance(StartUtc.AddMinutes(5).AddMilliseconds(-1));

        Assert.Equal(EventLifecycleState.Active, GetInstance(context).State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AdvanceAtOrAfterDeadlineFailsEventExactlyOnce(int millisecondsAfterDeadline)
    {
        using var context = new TestContext();
        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, FirstId, StartUtc).Succeeded);
        var expirationUtc = StartUtc.AddMinutes(5).AddMilliseconds(millisecondsAfterDeadline);

        context.Service.Advance(expirationUtc);
        var completedUtc = GetInstance(context).CompletedUtc;
        context.Service.Advance(expirationUtc.AddMinutes(1));

        Assert.Equal(EventLifecycleState.Failed, GetInstance(context).State);
        Assert.Equal(expirationUtc, completedUtc);
        Assert.Equal(completedUtc, GetInstance(context).CompletedUtc);
        Assert.Equal(BritainBrigandEncounter.EncounterSize, context.Encounter.Deleted.Count);
        Assert.Empty(GetInstance(context).OwnedMobiles);
        AssertWorldState(context.WorldStates, WorldCondition.Normal);
    }

    [Fact]
    public void RejectedExpirationWorldStateTransitionLeavesEventActive()
    {
        using var context = new TestContext();
        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, FirstId, StartUtc).Succeeded);
        Assert.True(context.WorldStates.Transition(KnownWorldStates.Britain, WorldCondition.Normal).Succeeded);

        context.Service.Advance(StartUtc.AddMinutes(5));

        Assert.Equal(EventLifecycleState.Active, GetInstance(context).State);
        Assert.Null(GetInstance(context).CompletedUtc);
        AssertWorldState(context.WorldStates, WorldCondition.Normal);
    }

    [Fact]
    public void NoActiveEventHasNoTimerAndTriggerArmsOne()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        Assert.False(context.Service.Scheduler.TimerRunning);

        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, FirstId, clock.Now).Succeeded);

        Assert.True(context.Service.Scheduler.TimerRunning);
        Assert.Equal(StartUtc.AddMinutes(5), context.Service.Scheduler.NextExpirationUtc);
    }

    [Theory]
    [InlineData(EventLifecycleState.Succeeded)]
    [InlineData(EventLifecycleState.Failed)]
    public void ExplicitTerminalTransitionRemovesExpirationTimer(EventLifecycleState state)
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, FirstId, clock.Now).Succeeded);

        var result = state == EventLifecycleState.Succeeded
            ? context.Service.Complete(FirstId, clock.Now.AddMinutes(1))
            : context.Service.Fail(FirstId, clock.Now.AddMinutes(1));

        Assert.True(result.Succeeded);
        Assert.False(context.Service.Scheduler.TimerRunning);
        Assert.Null(context.Service.Scheduler.NextExpirationUtc);
    }

    [Fact]
    public void NearestActiveExpirationDeterminesSingleTimer()
    {
        using var clock = new SimulationClock(StartUtc);
        var firstDefinition = new EventDefinition(
            new EventDefinitionId("event.test.first"),
            new EventTargetId("region.first"),
            TimeSpan.FromMinutes(10)
        );
        var secondDefinition = new EventDefinition(
            new EventDefinitionId("event.test.second"),
            new EventTargetId("region.second"),
            TimeSpan.FromMinutes(2)
        );
        var store = new EventStore([firstDefinition, secondDefinition]);
        Assert.True(store.Trigger(firstDefinition.Id, FirstId, StartUtc).Succeeded);
        Assert.True(
            store.Trigger(
                secondDefinition.Id,
                new EventInstanceId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
                StartUtc
            ).Succeeded
        );
        var scheduler = new AndraxiaEventExpirationScheduler(store, _ => { });

        try
        {
            scheduler.Rearm(clock.Now);

            Assert.True(scheduler.TimerRunning);
            Assert.Equal(StartUtc.AddMinutes(2), scheduler.NextExpirationUtc);
        }
        finally
        {
            scheduler.Cancel();
        }
    }

    [Fact]
    public void TimerCallbackAdvancesDueEventAndRemovesTimer()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, FirstId, clock.Now).Succeeded);

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(EventLifecycleState.Failed, GetInstance(context).State);
        Assert.Equal(clock.Now, GetInstance(context).CompletedUtc);
        Assert.False(context.Service.Scheduler.TimerRunning);
    }

    [Fact]
    public void CleanupCancelsOwnedTimerToken()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, FirstId, clock.Now).Succeeded);

        context.Service.StopExpirationTimer();

        Assert.False(context.Service.Scheduler.TimerRunning);
        Assert.Null(context.Service.Scheduler.NextExpirationUtc);
    }

    private static EventInstance GetInstance(TestContext context)
    {
        Assert.True(context.Events.TryGetInstance(FirstId, out var instance));
        return instance;
    }

    private static void AssertWorldState(WorldStateStore store, WorldCondition expected)
    {
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
        Assert.Equal(expected, condition);
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext()
        {
            Events = new EventStore(KnownEvents.Definitions);
            WorldStates = new WorldStateStore(KnownWorldStates.Definitions);
            Encounter = new TestEventEncounterSpawner();
            Service = new AndraxiaEventService(Events, WorldStates, Encounter);
        }

        public EventStore Events { get; }
        public WorldStateStore WorldStates { get; }
        public AndraxiaEventService Service { get; }
        public TestEventEncounterSpawner Encounter { get; }

        public void Dispose() => Service.StopExpirationTimer();
    }
}
