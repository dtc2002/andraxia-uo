using System;
using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class EncounterScalingTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private AndraxiaEventService _service;

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    [InlineData(3, 5)]
    [InlineData(4, 7)]
    [InlineData(20, 7)]
    public void PlayerCountMapsToExplicitCappedTier(int players, int expected) =>
        Assert.Equal(expected, EncounterScalingPolicy.GetEncounterSize(players));

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    [InlineData(4, 7)]
    public void ActivationSnapshotsScaledOwnership(int players, int expected)
    {
        var encounter = new TestEventEncounterSpawner();
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var currentPlayers = players;
        var countCalls = 0;
        _service = new AndraxiaEventService(
            events,
            states,
            [encounter],
            new DeterministicEncounterLocationSelector(),
            () =>
            {
                countCalls++;
                return currentPlayers;
            }
        );

        var result = _service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);
        currentPlayers = 20;
        _service.RecoverOwnedMobiles(StartUtc.AddSeconds(1));

        Assert.True(result.Succeeded);
        Assert.Equal(1, countCalls);
        Assert.Equal(expected, encounter.RequestedEncounterSize);
        Assert.Equal(expected, result.EventResult.Instance.OwnedMobiles.Count);
        Assert.Equal(expected, encounter.Existing.Count);
    }

    [Theory]
    [InlineData(3, 2, 1)]
    [InlineData(5, 3, 2)]
    [InlineData(7, 4, 3)]
    public void UndeadCompositionIsDeterministic(int size, int skeletons, int zombies)
    {
        var types = BritainUndeadEncounter.GetMobileTypes(size);

        Assert.Equal(size, types.Count);
        Assert.Equal(skeletons, types.Count(static type => type == typeof(AndraxiaEncounterSkeleton)));
        Assert.Equal(zombies, types.Count(static type => type == typeof(AndraxiaEncounterZombie)));
    }

    [Fact]
    public void SevenSlotFormationIsUniqueAndConsumedInOrder()
    {
        Assert.Equal(7, EncounterFormation.Offsets.Count);
        Assert.Equal(new Point3D(0, 0, 0), EncounterFormation.Offsets[0]);
        Assert.Equal(7, EncounterFormation.Offsets.Distinct().Count());

        foreach (var size in new[] { 3, 5, 7 })
        {
            var encounter = new TestEventEncounterSpawner();
            var spawned = new List<Serial>();
            var location = KnownEncounterLocations.BritainDisturbance[0];

            Assert.True(encounter.TrySpawn(location, size, EncounterSeverity.Normal, spawned, out _));
            Assert.Equal(size, encounter.SpawnedPositions.Count);
            Assert.Equal(
                EncounterFormation.Offsets.Take(size).Select(
                    offset => new Point3D(location.X + offset.X, location.Y + offset.Y, location.Z + offset.Z)
                ),
                encounter.SpawnedPositions
            );
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void PartialFailureCleansEverySpawnedEntityAtEachTier(int size)
    {
        var encounter = new TestEventEncounterSpawner { SpawnSucceeds = false, SpawnBeforeFailure = size - 1 };
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        _service = new AndraxiaEventService(
            events,
            states,
            [encounter],
            new DeterministicEncounterLocationSelector(),
            () => size == 3 ? 1 : size == 5 ? 2 : 4
        );

        var result = _service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);

        Assert.False(result.Succeeded);
        Assert.Equal(size - 1, encounter.Deleted.Count);
        Assert.Empty(encounter.Existing);
        Assert.Empty(events.EnumerateInstances());
        Assert.True(states.TryGetState(KnownWorldStates.Britain, out var condition));
        Assert.Equal(WorldCondition.Normal, condition);
    }

    [Fact]
    public void CatalogMetadataIsNonEmptyAndStableIdsRemainAuthoritative()
    {
        Assert.All(KnownEvents.Definitions, static definition => Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName)));
        Assert.Contains(KnownEvents.Definitions, static definition =>
            definition.Id.Value == "event.test.britain-disturbance" &&
            definition.DisplayName == "Britain Brigand Disturbance");
        Assert.All(
            KnownEvents.Definitions.SelectMany(static definition => KnownEncounterLocations.GetForDefinition(definition.Id)),
            static location => Assert.False(string.IsNullOrWhiteSpace(location.DisplayName))
        );
    }

    public void Dispose() => _service?.StopExpirationTimer();
}
