using System;
using System.Linq;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class EventEncounterTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly EventInstanceId InstanceId = new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
    );

    private AndraxiaEventService _service;

    [Fact]
    public void ActivationOwnsFixedEncounterSize()
    {
        var (service, events, worldStates, _) = CreateService();

        var result = service.Trigger(KnownEvents.BritainDisturbance, InstanceId, StartUtc);

        Assert.True(result.Succeeded);
        Assert.True(events.TryGetInstance(InstanceId, out var instance));
        Assert.Equal(BritainBrigandEncounter.EncounterSize, instance.OwnedMobiles.Count);
        Assert.Equal(EventLifecycleState.Active, instance.State);
        AssertState(worldStates, WorldCondition.Threatened);
    }

    [Fact]
    public void PartialSpawnFailureCleansSpawnedMobilesAndCompensatesWorldState()
    {
        var encounter = new TestEventEncounterSpawner { SpawnSucceeds = false, SpawnBeforeFailure = 2 };
        var (service, events, worldStates, _) = CreateService(encounter);

        var result = service.Trigger(KnownEvents.BritainDisturbance, InstanceId, StartUtc);

        Assert.False(result.Succeeded);
        Assert.Equal(EventTransitionFailure.EncounterSpawnFailed, result.EventResult.Failure);
        Assert.Empty(events.EnumerateInstances());
        Assert.Equal(2, encounter.Deleted.Count);
        AssertState(worldStates, WorldCondition.Normal);
    }

    [Fact]
    public void FinalOwnedMobileRemovalCompletesEvent()
    {
        var (service, events, worldStates, _) = CreateService();
        Assert.True(service.Trigger(KnownEvents.BritainDisturbance, InstanceId, StartUtc).Succeeded);
        var serials = Assert.Single(events.EnumerateInstances()).OwnedMobiles.ToArray();

        service.HandleOwnedMobileRemoved(serials[0], StartUtc.AddSeconds(1));
        service.HandleOwnedMobileRemoved(serials[1], StartUtc.AddSeconds(2));
        Assert.Equal(EventLifecycleState.Active, Assert.Single(events.EnumerateInstances()).State);

        service.HandleOwnedMobileRemoved(serials[2], StartUtc.AddSeconds(3));

        var completed = Assert.Single(events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Succeeded, completed.State);
        Assert.Equal(StartUtc.AddSeconds(3), completed.CompletedUtc);
        Assert.Empty(completed.OwnedMobiles);
        AssertState(worldStates, WorldCondition.Normal);
    }

    [Theory]
    [InlineData(EventLifecycleState.Succeeded)]
    [InlineData(EventLifecycleState.Failed)]
    public void ExplicitTerminalTransitionCleansOnlyOwnedMobiles(EventLifecycleState terminalState)
    {
        var (service, events, _, encounter) = CreateService();
        var foreign = (Serial)999u;
        encounter.Existing.Add(foreign);
        Assert.True(service.Trigger(KnownEvents.BritainDisturbance, InstanceId, StartUtc).Succeeded);
        var owned = Assert.Single(events.EnumerateInstances()).OwnedMobiles.ToArray();

        var result = terminalState == EventLifecycleState.Succeeded
            ? service.Complete(InstanceId, StartUtc.AddSeconds(1))
            : service.Fail(InstanceId, StartUtc.AddSeconds(1));

        Assert.True(result.Succeeded);
        Assert.All(owned, serial => Assert.Contains(serial, encounter.Deleted));
        Assert.DoesNotContain(foreign, encounter.Deleted);
        Assert.Contains(foreign, encounter.Existing);
        Assert.Empty(Assert.Single(events.EnumerateInstances()).OwnedMobiles);
    }

    private (AndraxiaEventService, EventStore, WorldStateStore, TestEventEncounterSpawner) CreateService(
        TestEventEncounterSpawner encounter = null
    )
    {
        encounter ??= new TestEventEncounterSpawner();
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        _service = new AndraxiaEventService(events, states, encounter);
        return (_service, events, states, encounter);
    }

    private static void AssertState(WorldStateStore states, WorldCondition expected)
    {
        Assert.True(states.TryGetState(KnownWorldStates.Britain, out var actual));
        Assert.Equal(expected, actual);
    }

    public void Dispose() => _service?.StopExpirationTimer();
}
