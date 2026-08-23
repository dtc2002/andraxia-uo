using System;
using System.Linq;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class UndeadPersistenceTests
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly EventInstanceId InstanceId = new(
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")
    );

    [Fact]
    public void ActiveUndeadRoundTripPreservesDefinitionLocationAndOwnership()
    {
        using var source = new TestContext();
        using var loaded = new TestContext();
        Assert.True(
            source.Service.Trigger(
                KnownEvents.BritainUndeadDisturbance,
                InstanceId,
                StartUtc,
                KnownEncounterLocations.BritainUndeadGraveyardWest
            ).Succeeded
        );
        var writer = new BufferWriter(new byte[1024], true);
        source.Persistence.Serialize(writer);

        loaded.Persistence.Deserialize(new BufferReader(writer.Buffer));

        var instance = Assert.Single(loaded.Events.EnumerateInstances());
        Assert.Equal(KnownEvents.BritainUndeadDisturbance, instance.DefinitionId);
        Assert.Equal(KnownEncounterLocations.BritainUndeadGraveyardWest, instance.SelectedLocationId);
        Assert.Equal(BritainUndeadEncounter.Size, instance.OwnedMobiles.Count);
    }

    [Fact]
    public void SurvivingOwnedUndeadKeepsRecoveredEventActive()
    {
        using var clock = new SimulationClock(StartUtc);
        using var source = CreateActiveSource(StartUtc);
        using var loaded = new TestContext();
        var writer = Serialize(source);
        var owned = Assert.Single(source.Events.EnumerateInstances()).OwnedMobiles.ToArray();
        loaded.Undead.Existing.UnionWith(owned);
        Assert.True(loaded.States.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);
        loaded.Persistence.Deserialize(new BufferReader(writer.Buffer));

        loaded.Persistence.PostDeserialize();

        var instance = Assert.Single(loaded.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Active, instance.State);
        Assert.Equal(owned, instance.OwnedMobiles);
        AssertState(loaded.States, WorldCondition.Threatened);
    }

    [Fact]
    public void AllMissingOwnedUndeadCompletesRecoveredEvent()
    {
        using var clock = new SimulationClock(StartUtc);
        using var source = CreateActiveSource(StartUtc);
        using var loaded = new TestContext();
        var writer = Serialize(source);
        Assert.True(loaded.States.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);
        loaded.Persistence.Deserialize(new BufferReader(writer.Buffer));

        loaded.Persistence.PostDeserialize();

        var instance = Assert.Single(loaded.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Succeeded, instance.State);
        Assert.Empty(instance.OwnedMobiles);
        AssertState(loaded.States, WorldCondition.Normal);
    }

    [Fact]
    public void OverdueRecoveredUndeadFailsAndCleansSurvivors()
    {
        using var clock = new SimulationClock(StartUtc);
        using var source = CreateActiveSource(StartUtc.AddMinutes(-10));
        using var loaded = new TestContext();
        var writer = Serialize(source);
        var owned = Assert.Single(source.Events.EnumerateInstances()).OwnedMobiles.ToArray();
        loaded.Undead.Existing.UnionWith(owned);
        Assert.True(loaded.States.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);
        loaded.Persistence.Deserialize(new BufferReader(writer.Buffer));

        loaded.Persistence.PostDeserialize();

        var instance = Assert.Single(loaded.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Failed, instance.State);
        Assert.Empty(instance.OwnedMobiles);
        Assert.All(owned, serial => Assert.Contains(serial, loaded.Undead.Deleted));
        AssertState(loaded.States, WorldCondition.Normal);
    }

    private static TestContext CreateActiveSource(DateTime startedUtc)
    {
        var context = new TestContext();
        Assert.True(
            context.Service.Trigger(KnownEvents.BritainUndeadDisturbance, InstanceId, startedUtc).Succeeded
        );
        return context;
    }

    private static BufferWriter Serialize(TestContext context)
    {
        var writer = new BufferWriter(new byte[1024], true);
        context.Persistence.Serialize(writer);
        return writer;
    }

    private static void AssertState(WorldStateStore states, WorldCondition expected)
    {
        Assert.True(states.TryGetState(KnownWorldStates.Britain, out var actual));
        Assert.Equal(expected, actual);
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext()
        {
            Events = new EventStore(KnownEvents.Definitions);
            States = new WorldStateStore(KnownWorldStates.Definitions);
            Brigands = new TestEventEncounterSpawner(1);
            Undead = new TestEventEncounterSpawner(100) { DefinitionId = KnownEvents.BritainUndeadDisturbance };
            Service = new AndraxiaEventService(
                Events,
                States,
                new IEventEncounterSpawner[] { Brigands, Undead },
                new DeterministicEncounterLocationSelector()
            );
            Generator = new AndraxiaAutoEventGenerator(Events, States, Service);
            Persistence = new AndraxiaEventPersistence(Events, States, Service, Generator);
        }

        public EventStore Events { get; }
        public WorldStateStore States { get; }
        public TestEventEncounterSpawner Brigands { get; }
        public TestEventEncounterSpawner Undead { get; }
        public AndraxiaEventService Service { get; }
        public AndraxiaAutoEventGenerator Generator { get; }
        public AndraxiaEventPersistence Persistence { get; }

        public void Dispose()
        {
            Generator.StopTimer();
            Service.StopExpirationTimer();
            Persistence.Unregister();
        }
    }
}
