using System;
using System.Linq;
using Server;
using Server.Andraxia;
using Server.Mobiles;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class UndeadEncounterTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly EventInstanceId InstanceId = new(
        Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")
    );

    private AndraxiaEventService _service;

    [Fact]
    public void DefinitionsAreUniqueRegisteredAndTargetBritain()
    {
        Assert.Equal(2, KnownEvents.Definitions.Count);
        Assert.Equal(2, KnownEvents.Definitions.Select(static definition => definition.Id).Distinct().Count());
        Assert.Contains(KnownEvents.Definitions, static definition => definition.Id == KnownEvents.BritainDisturbance);
        Assert.Contains(
            KnownEvents.Definitions,
            static definition => definition.Id == KnownEvents.BritainUndeadDisturbance
        );
        Assert.All(KnownEvents.Definitions, static definition => Assert.Equal(KnownEvents.Britain, definition.TargetId));
        Assert.All(KnownEvents.Definitions, static definition => Assert.Equal(TimeSpan.FromMinutes(5), definition.Duration));
    }

    [Theory]
    [InlineData("location.britain.undead.graveyard-east", 1408, 1492, 10)]
    [InlineData("location.britain.undead.graveyard-west", 1350, 1490, 10)]
    [InlineData("location.britain.undead.ruins-north", 1510, 1400, 0)]
    [InlineData("location.britain.undead.wilderness-east", 1750, 1580, 0)]
    [InlineData("location.britain.undead.wilderness-south", 1450, 1840, 0)]
    public void UndeadCatalogContainsExpectedCandidate(string id, int x, int y, int z)
    {
        Assert.True(
            KnownEncounterLocations.TryGetForDefinition(
                KnownEvents.BritainUndeadDisturbance,
                new EncounterLocationId(id),
                out var location
            )
        );
        Assert.Same(Map.Trammel, location.Map);
        Assert.Equal(new Point3D(x, y, z), location.Anchor);
    }

    [Fact]
    public void UndeadLocationPoolIsUniqueDistinctAndValid()
    {
        var undead = KnownEncounterLocations.BritainUndeadDisturbance;
        var brigandIds = KnownEncounterLocations.BritainDisturbance.Select(static location => location.Id).ToHashSet();

        Assert.Equal(5, undead.Count);
        Assert.Equal(undead.Count, undead.Select(static location => location.Id).Distinct().Count());
        Assert.All(
            undead,
            location =>
            {
                Assert.StartsWith("location.britain.undead.", location.Id.Value, StringComparison.Ordinal);
                Assert.Same(Map.Trammel, location.Map);
                Assert.DoesNotContain(location.Id, brigandIds);
                Assert.True(
                    KnownEncounterLocations.TryGetForDefinition(
                        KnownEvents.BritainUndeadDisturbance,
                        location.Id,
                        out var resolved
                    )
                );
                Assert.Same(location, resolved);
            }
        );
    }

    [Fact]
    public void ProductionUndeadHandlerUsesTwoStockSkeletonsAndOneStockZombie()
    {
        Assert.Equal(BritainUndeadEncounter.Size, BritainUndeadEncounter.MobileTypes.Count);
        Assert.Equal(typeof(AndraxiaEncounterSkeleton), BritainUndeadEncounter.MobileTypes[0]);
        Assert.Equal(typeof(AndraxiaEncounterSkeleton), BritainUndeadEncounter.MobileTypes[1]);
        Assert.Equal(typeof(AndraxiaEncounterZombie), BritainUndeadEncounter.MobileTypes[2]);
        Assert.True(typeof(Skeleton).IsAssignableFrom(BritainUndeadEncounter.MobileTypes[0]));
        Assert.True(typeof(Skeleton).IsAssignableFrom(BritainUndeadEncounter.MobileTypes[1]));
        Assert.True(typeof(Zombie).IsAssignableFrom(BritainUndeadEncounter.MobileTypes[2]));
    }

    [Fact]
    public void UndeadTriggerOwnsThreeMobilesAtUndeadLocation()
    {
        var context = CreateContext();

        var result = context.Service.Trigger(
            KnownEvents.BritainUndeadDisturbance,
            InstanceId,
            StartUtc,
            KnownEncounterLocations.BritainUndeadGraveyardEast
        );

        Assert.True(result.Succeeded);
        Assert.Equal(BritainUndeadEncounter.Size, result.EventResult.Instance.OwnedMobiles.Count);
        Assert.Equal(KnownEncounterLocations.BritainUndeadGraveyardEast, result.EventResult.Instance.SelectedLocationId);
        Assert.Equal(KnownEncounterLocations.BritainUndeadGraveyardEast, context.Undead.SelectedLocation.Id);
        AssertState(context.States, WorldCondition.Threatened);
    }

    [Fact]
    public void RemovingOnlyOwnedUndeadCompletesAfterTheLastOne()
    {
        var context = CreateContext();
        Assert.True(context.Service.Trigger(KnownEvents.BritainUndeadDisturbance, InstanceId, StartUtc).Succeeded);
        var owned = Assert.Single(context.Events.EnumerateInstances()).OwnedMobiles.ToArray();
        var unrelated = (Serial)999u;

        context.Service.HandleOwnedMobileRemoved(unrelated, StartUtc.AddSeconds(1));
        context.Service.HandleOwnedMobileRemoved(owned[0], StartUtc.AddSeconds(2));
        Assert.Equal(2, Assert.Single(context.Events.EnumerateInstances()).OwnedMobiles.Count);
        context.Service.HandleOwnedMobileRemoved(owned[1], StartUtc.AddSeconds(3));
        context.Service.HandleOwnedMobileRemoved(owned[2], StartUtc.AddSeconds(4));

        var completed = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Succeeded, completed.State);
        Assert.Empty(completed.OwnedMobiles);
        AssertState(context.States, WorldCondition.Normal);
    }

    [Fact]
    public void UndeadFailureCleansOnlyUndeadHandlerOwnership()
    {
        var context = CreateContext();
        Assert.True(context.Service.Trigger(KnownEvents.BritainUndeadDisturbance, InstanceId, StartUtc).Succeeded);
        var owned = Assert.Single(context.Events.EnumerateInstances()).OwnedMobiles.ToArray();
        var unrelatedBrigand = (Serial)50u;
        context.Brigands.Existing.Add(unrelatedBrigand);

        Assert.True(context.Service.Fail(InstanceId, StartUtc.AddMinutes(1)).Succeeded);

        Assert.All(owned, serial => Assert.Contains(serial, context.Undead.Deleted));
        Assert.DoesNotContain(unrelatedBrigand, context.Brigands.Deleted);
        Assert.Contains(unrelatedBrigand, context.Brigands.Existing);
    }

    [Fact]
    public void PartialUndeadSpawnFailureCompensatesAndCleansPartialSpawn()
    {
        var undead = new TestEventEncounterSpawner(100)
        {
            DefinitionId = KnownEvents.BritainUndeadDisturbance,
            SpawnSucceeds = false,
            SpawnBeforeFailure = 2
        };
        var context = CreateContext(undead);

        var result = context.Service.Trigger(KnownEvents.BritainUndeadDisturbance, InstanceId, StartUtc);

        Assert.False(result.Succeeded);
        Assert.Equal(EventTransitionFailure.EncounterSpawnFailed, result.EventResult.Failure);
        Assert.Empty(context.Events.EnumerateInstances());
        Assert.Equal(2, undead.Deleted.Count);
        AssertState(context.States, WorldCondition.Normal);
    }

    [Fact]
    public void UndeadRejectsBrigandLocationWithoutMutation()
    {
        var context = CreateContext();

        var result = context.Service.Trigger(
            KnownEvents.BritainUndeadDisturbance,
            InstanceId,
            StartUtc,
            KnownEncounterLocations.BritainRoadNorth
        );

        Assert.False(result.Succeeded);
        Assert.Equal(EventTransitionFailure.UnknownEncounterLocation, result.EventResult.Failure);
        Assert.Empty(context.Events.EnumerateInstances());
        AssertState(context.States, WorldCondition.Normal);
    }

    [Fact]
    public void ActiveBrigandPreventsUndeadOnSameTarget()
    {
        var context = CreateContext();
        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, InstanceId, StartUtc).Succeeded);

        var result = context.Service.Trigger(
            KnownEvents.BritainUndeadDisturbance,
            new EventInstanceId(Guid.Parse("abababab-abab-abab-abab-abababababab")),
            StartUtc
        );

        Assert.False(result.Succeeded);
        Assert.Equal(EventTransitionFailure.DuplicateActiveDefinitionOrTarget, result.EventResult.Failure);
        Assert.Single(context.Events.EnumerateInstances());
    }

    [Fact]
    public void UndeadExpirationFailsAndCleansSurvivors()
    {
        var context = CreateContext();
        Assert.True(context.Service.Trigger(KnownEvents.BritainUndeadDisturbance, InstanceId, StartUtc).Succeeded);
        var owned = Assert.Single(context.Events.EnumerateInstances()).OwnedMobiles.ToArray();

        context.Service.Advance(StartUtc.AddMinutes(5));

        Assert.Equal(EventLifecycleState.Failed, Assert.Single(context.Events.EnumerateInstances()).State);
        Assert.All(owned, serial => Assert.Contains(serial, context.Undead.Deleted));
        AssertState(context.States, WorldCondition.Normal);
    }

    [Fact]
    public void BrigandCleanupDoesNotDeleteUnrelatedUndead()
    {
        var context = CreateContext();
        var unrelatedUndead = (Serial)150u;
        context.Undead.Existing.Add(unrelatedUndead);
        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, InstanceId, StartUtc).Succeeded);

        Assert.True(context.Service.Fail(InstanceId, StartUtc.AddMinutes(1)).Succeeded);

        Assert.DoesNotContain(unrelatedUndead, context.Undead.Deleted);
        Assert.Contains(unrelatedUndead, context.Undead.Existing);
    }

    private Context CreateContext(TestEventEncounterSpawner undead = null)
    {
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var brigands = new TestEventEncounterSpawner(1);
        undead ??= new TestEventEncounterSpawner(100) { DefinitionId = KnownEvents.BritainUndeadDisturbance };
        _service = new AndraxiaEventService(
            events,
            states,
            new IEventEncounterSpawner[] { brigands, undead },
            new DeterministicEncounterLocationSelector()
        );
        return new Context(_service, events, states, brigands, undead);
    }

    private static void AssertState(WorldStateStore states, WorldCondition expected)
    {
        Assert.True(states.TryGetState(KnownWorldStates.Britain, out var actual));
        Assert.Equal(expected, actual);
    }

    public void Dispose() => _service?.StopExpirationTimer();

    private sealed record Context(
        AndraxiaEventService Service,
        EventStore Events,
        WorldStateStore States,
        TestEventEncounterSpawner Brigands,
        TestEventEncounterSpawner Undead
    );
}
