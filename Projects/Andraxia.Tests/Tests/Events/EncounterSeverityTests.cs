using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class EncounterSeverityTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private AndraxiaEventService _service;

    [Theory]
    [InlineData(0, EncounterSeverity.Stable)]
    [InlineData(24, EncounterSeverity.Stable)]
    [InlineData(25, EncounterSeverity.Normal)]
    [InlineData(49, EncounterSeverity.Normal)]
    [InlineData(50, EncounterSeverity.Elevated)]
    [InlineData(74, EncounterSeverity.Elevated)]
    [InlineData(75, EncounterSeverity.Severe)]
    [InlineData(100, EncounterSeverity.Severe)]
    public void SeverityUsesPressureClassificationBoundaries(int pressure, EncounterSeverity expected) =>
        Assert.Equal(expected, EncounterSeverityPolicy.FromPressure(pressure));

    public static IEnumerable<object[]> CompositionCases()
    {
        foreach (var size in new[] { 3, 5, 7 })
        {
            yield return [false, EncounterSeverity.Stable, size, "B".PadRight(size, 'B')];
            yield return [false, EncounterSeverity.Normal, size, "B".PadRight(size, 'B')];
            yield return [false, EncounterSeverity.Elevated, size, new string('B', size - 1) + "M"];
            yield return [false, EncounterSeverity.Severe, size,
                new string('B', size - (size >= 5 ? 2 : 1)) + new string('M', size >= 5 ? 2 : 1)];

            yield return [true, EncounterSeverity.Stable, size, new string('S', size)];
            var normal = new string('S', (size + 1) / 2) + new string('Z', size / 2);
            yield return [true, EncounterSeverity.Normal, size, normal];
            yield return [true, EncounterSeverity.Elevated, size, normal[..^1] + "G"];
            var strong = size >= 5 ? 2 : 1;
            yield return [true, EncounterSeverity.Severe, size, normal[..^strong] + new string('W', strong)];
        }
    }

    [Theory]
    [MemberData(nameof(CompositionCases))]
    public void CompositionTablesAreExactAndPreserveCount(
        bool undead,
        EncounterSeverity severity,
        int size,
        string expected
    )
    {
        var types = undead
            ? EncounterCompositionPolicy.Undead(size, severity)
            : EncounterCompositionPolicy.Brigands(size, severity);
        var actual = string.Concat(types.Select(TypeToken));
        Assert.Equal(expected, actual);
        Assert.Equal(size, types.Count);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 5)]
    [InlineData(10, 7)]
    public void PopulationControlsCountWhilePressureControlsSeverity(int players, int expectedSize)
    {
        var pressure = new RegionalPressureStore();
        pressure.SetBritain(100);
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var encounter = new TestEventEncounterSpawner();
        _service = new AndraxiaEventService(
            events, states, [encounter], new DeterministicEncounterLocationSelector(),
            () => players, NullEventAwareness.Instance, pressure
        );

        var result = _service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedSize, encounter.RequestedEncounterSize);
        Assert.Equal(EncounterSeverity.Severe, encounter.RequestedSeverity);
        Assert.Equal(expectedSize, result.EventResult.Instance.OwnedMobiles.Count);
    }

    [Fact]
    public void ActivationSnapshotsSeverityAndPopulation()
    {
        var pressure = new RegionalPressureStore();
        pressure.SetBritain(50);
        var players = 3;
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var encounter = new TestEventEncounterSpawner();
        _service = new AndraxiaEventService(
            events, states, [encounter], new DeterministicEncounterLocationSelector(),
            () => players, NullEventAwareness.Instance, pressure
        );
        var result = _service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);

        pressure.SetBritain(0);
        players = 10;

        Assert.Equal(EncounterSeverity.Elevated, result.EventResult.Instance.Severity);
        Assert.Equal(5, result.EventResult.Instance.OwnedMobiles.Count);
        Assert.Equal(5, encounter.RequestedEncounterSize);
        Assert.True(_service.Complete(result.EventResult.Instance.Id, StartUtc.AddMinutes(1)).Succeeded);
        Assert.True(events.TryGetInstance(result.EventResult.Instance.Id, out var terminal));
        Assert.Equal(EncounterSeverity.Elevated, terminal.Severity);
    }

    [Theory]
    [InlineData(typeof(AndraxiaEncounterEvilMage))]
    [InlineData(typeof(AndraxiaEncounterGhoul))]
    [InlineData(typeof(AndraxiaEncounterWraith))]
    public void StrongerEncounterTypesForwardRemovalLifecycle(Type type)
    {
        Assert.Equal(type, type.GetMethod(nameof(Mobile.OnDeath))?.DeclaringType);
        Assert.Equal(type, type.GetMethod(nameof(Mobile.OnDelete))?.DeclaringType);
    }

    private static char TypeToken(Type type) => type == typeof(AndraxiaEncounterBrigand) ? 'B' :
        type == typeof(AndraxiaEncounterEvilMage) ? 'M' :
        type == typeof(AndraxiaEncounterSkeleton) ? 'S' :
        type == typeof(AndraxiaEncounterZombie) ? 'Z' :
        type == typeof(AndraxiaEncounterGhoul) ? 'G' : 'W';

    public void Dispose() => _service?.StopExpirationTimer();
}
