using System;
using System.Collections.Generic;
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
        var (service, events, worldStates, encounter) = CreateService();

        var result = service.Trigger(KnownEvents.BritainDisturbance, InstanceId, StartUtc);

        Assert.True(result.Succeeded);
        Assert.True(events.TryGetInstance(InstanceId, out var instance));
        Assert.Equal(BritainBrigandEncounter.EncounterSize, instance.OwnedMobiles.Count);
        Assert.Equal(EventLifecycleState.Active, instance.State);
        Assert.Equal(encounter.SelectedLocation.Id, instance.SelectedLocationId);
        Assert.Equal(
            new[]
            {
                encounter.SelectedLocation.Anchor,
                new Point3D(encounter.SelectedLocation.X + 3, encounter.SelectedLocation.Y + 2, encounter.SelectedLocation.Z),
                new Point3D(encounter.SelectedLocation.X + 6, encounter.SelectedLocation.Y, encounter.SelectedLocation.Z)
            },
            encounter.SpawnedPositions
        );
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
    public void ForcedKnownLocationUsesNormalActivationPathWithoutAutomaticSelection()
    {
        var encounter = new TestEventEncounterSpawner();
        var automatic = new CountingLocationSelector(KnownEncounterLocations.BritainDisturbance[0]);
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        _service = new AndraxiaEventService(events, states, encounter, automatic);

        var result = _service.Trigger(
            KnownEvents.BritainDisturbance,
            InstanceId,
            StartUtc,
            KnownEncounterLocations.BritainRoadNorth
        );

        Assert.True(result.Succeeded);
        Assert.Equal(0, automatic.CallCount);
        Assert.Equal(KnownEncounterLocations.BritainRoadNorth, result.EventResult.Instance.SelectedLocationId);
        Assert.Equal(KnownEncounterLocations.BritainRoadNorth, encounter.SelectedLocation.Id);
        Assert.Equal(BritainBrigandEncounter.EncounterSize, result.EventResult.Instance.OwnedMobiles.Count);
        AssertState(states, WorldCondition.Threatened);
    }

    [Fact]
    public void ForcedUnknownLocationFailsWithoutMutationOrSelection()
    {
        var encounter = new TestEventEncounterSpawner();
        var automatic = new CountingLocationSelector(KnownEncounterLocations.BritainDisturbance[0]);
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        _service = new AndraxiaEventService(events, states, encounter, automatic);

        var result = _service.Trigger(
            KnownEvents.BritainDisturbance,
            InstanceId,
            StartUtc,
            new EncounterLocationId("location.britain.unknown")
        );

        Assert.False(result.Succeeded);
        Assert.Equal(EventTransitionFailure.UnknownEncounterLocation, result.EventResult.Failure);
        Assert.Null(result.WorldStateResult);
        Assert.Empty(events.EnumerateInstances());
        Assert.Null(encounter.SelectedLocation);
        Assert.Empty(encounter.Existing);
        Assert.Equal(0, automatic.CallCount);
        AssertState(states, WorldCondition.Normal);
    }

    [Fact]
    public void ForcedLocationRegisteredForDifferentEventIsRejected()
    {
        var otherDefinition = new EventDefinition(
            new EventDefinitionId("event.test.other"),
            new EventTargetId("region.other"),
            TimeSpan.FromMinutes(5)
        );
        var encounter = new TestEventEncounterSpawner();
        var events = new EventStore([.. KnownEvents.Definitions, otherDefinition]);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        _service = new AndraxiaEventService(events, states, encounter);

        var result = _service.Trigger(
            otherDefinition.Id,
            InstanceId,
            StartUtc,
            KnownEncounterLocations.BritainRoadNorth
        );

        Assert.False(result.Succeeded);
        Assert.Equal(EventTransitionFailure.UnknownEncounterLocation, result.EventResult.Failure);
        Assert.Empty(events.EnumerateInstances());
        Assert.Null(encounter.SelectedLocation);
        AssertState(states, WorldCondition.Normal);
    }

    [Fact]
    public void AutomaticTriggerStillUsesDeterministicSelector()
    {
        var encounter = new TestEventEncounterSpawner();
        Assert.True(KnownEncounterLocations.TryGet(KnownEncounterLocations.BritainRoadSouth, out var location));
        var selector = new CountingLocationSelector(location);
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        _service = new AndraxiaEventService(events, states, encounter, selector);

        var result = _service.Trigger(KnownEvents.BritainDisturbance, InstanceId, StartUtc);

        Assert.True(result.Succeeded);
        Assert.Equal(1, selector.CallCount);
        Assert.Equal(KnownEncounterLocations.BritainRoadSouth, result.EventResult.Instance.SelectedLocationId);
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

    [Fact]
    public void LaterEventSelectionDoesNotChangeEarlierInstanceLocation()
    {
        var encounter = new TestEventEncounterSpawner();
        var locations = KnownEncounterLocations.BritainDisturbance;
        var selector = new SequenceLocationSelector(locations[0], locations[1]);
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        _service = new AndraxiaEventService(events, states, encounter, selector);
        var secondId = new EventInstanceId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        Assert.True(_service.Trigger(KnownEvents.BritainDisturbance, InstanceId, StartUtc).Succeeded);
        Assert.True(_service.Complete(InstanceId, StartUtc.AddSeconds(1)).Succeeded);
        Assert.True(_service.Trigger(KnownEvents.BritainDisturbance, secondId, StartUtc.AddSeconds(2)).Succeeded);

        Assert.True(events.TryGetInstance(InstanceId, out var first));
        Assert.True(events.TryGetInstance(secondId, out var second));
        Assert.Equal(locations[0].Id, first.SelectedLocationId);
        Assert.Equal(locations[1].Id, second.SelectedLocationId);
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

    private sealed class SequenceLocationSelector(params EncounterLocation[] locations) : IEncounterLocationSelector
    {
        private readonly Queue<EncounterLocation> _locations = new(locations);

        public EncounterLocation Select(
            EventDefinitionId definitionId,
            EventInstanceId instanceId,
            IReadOnlyList<EncounterLocation> candidates
        ) => _locations.Dequeue();
    }

    private sealed class CountingLocationSelector(EncounterLocation location) : IEncounterLocationSelector
    {
        public int CallCount { get; private set; }

        public EncounterLocation Select(
            EventDefinitionId definitionId,
            EventInstanceId instanceId,
            IReadOnlyList<EncounterLocation> candidates
        )
        {
            CallCount++;
            return location;
        }
    }
}
