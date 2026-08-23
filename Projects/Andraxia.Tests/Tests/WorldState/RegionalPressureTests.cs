using System;
using System.IO;
using System.Linq;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class RegionalPressureTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private AndraxiaEventService _service;

    [Theory]
    [InlineData(0, RegionalPressureClassification.Stable, 0.20)]
    [InlineData(24, RegionalPressureClassification.Stable, 0.20)]
    [InlineData(25, RegionalPressureClassification.Normal, 0.35)]
    [InlineData(49, RegionalPressureClassification.Normal, 0.35)]
    [InlineData(50, RegionalPressureClassification.Elevated, 0.50)]
    [InlineData(74, RegionalPressureClassification.Elevated, 0.50)]
    [InlineData(75, RegionalPressureClassification.Severe, 0.65)]
    [InlineData(100, RegionalPressureClassification.Severe, 0.65)]
    public void ClassificationAndProbabilityTiersAreExact(
        int value,
        RegionalPressureClassification classification,
        double probability
    )
    {
        Assert.Equal(classification, RegionalPressureStore.Classify(value));
        Assert.Equal(probability, RegionalPressureStore.TriggerProbability(value));
    }

    [Fact]
    public void DefaultsAndClampingAreBounded()
    {
        var pressure = new RegionalPressureStore();
        Assert.Equal(25, pressure.Britain);
        Assert.Equal(0, pressure.SetBritain(-10));
        Assert.Equal(100, pressure.SetBritain(110));
    }

    [Fact]
    public void CombatSuccessDecreasesOnceAndAdministrativeOutcomesDoNothing()
    {
        var context = CreateContext();
        var result = context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);
        foreach (var serial in result.EventResult.Instance.OwnedMobiles)
        {
            context.Service.HandleOwnedMobileRemoved(serial, StartUtc.AddSeconds(1));
        }
        Assert.Equal(20, context.Pressure.Britain);
        context.Service.HandleOwnedMobileRemoved(result.EventResult.Instance.OwnedMobiles[0], StartUtc.AddSeconds(2));
        Assert.Equal(20, context.Pressure.Britain);

        context.Pressure.SetBritain(25);
        var second = context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc.AddMinutes(1));
        Assert.True(context.Service.Complete(second.EventResult.Instance.Id, StartUtc.AddMinutes(2)).Succeeded);
        Assert.Equal(25, context.Pressure.Britain);
    }

    [Fact]
    public void ExpirationRaisesPressureAndClampsWhileOwnerFailureDoesNot()
    {
        var context = CreateContext();
        context.Pressure.SetBritain(96);
        context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);
        context.Service.Advance(StartUtc.AddMinutes(5));
        Assert.Equal(100, context.Pressure.Britain);

        context.Pressure.SetBritain(25);
        var second = context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc.AddMinutes(6));
        Assert.True(context.Service.Fail(second.EventResult.Instance.Id, StartUtc.AddMinutes(7)).Succeeded);
        Assert.Equal(25, context.Pressure.Britain);
    }

    [Fact]
    public void SuccessClampsAtZero()
    {
        var context = CreateContext();
        context.Pressure.SetBritain(3);
        var result = context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);
        foreach (var serial in result.EventResult.Instance.OwnedMobiles)
        {
            context.Service.HandleOwnedMobileRemoved(serial, StartUtc.AddSeconds(1));
        }
        Assert.Equal(0, context.Pressure.Britain);
    }

    [Fact]
    public void PressureRoundTripsInIsolatedPersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"andraxia-pressure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourceStore = new RegionalPressureStore();
        sourceStore.SetBritain(73);
        var source = new RegionalPressurePersistence(sourceStore);
        var loadedStore = new RegionalPressureStore();
        var loaded = new RegionalPressurePersistence(loadedStore);
        try
        {
            var persistenceDirectory = Path.Combine(directory, RegionalPressurePersistence.PersistenceName);
            Directory.CreateDirectory(persistenceDirectory);
            using (var writer = new FileBufferWriter(Path.Combine(
                       persistenceDirectory, $"{RegionalPressurePersistence.PersistenceName}.bin")))
            {
                source.Serialize(writer);
            }
            loaded.Deserialize(directory, null);
            Assert.Equal(73, loadedStore.Britain);
        }
        finally
        {
            source.Unregister();
            loaded.Unregister();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void GeneratorUsesUpdatedPressureWithOneProbabilityDraw()
    {
        var pressure = new RegionalPressureStore();
        pressure.SetBritain(50);
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var encounter = new TestEventEncounterSpawner();
        _service = new AndraxiaEventService(
            events, states, [encounter], new DeterministicEncounterLocationSelector(),
            static () => 0, NullEventAwareness.Instance, pressure
        );
        var random = new CountingRandom(0.40);
        var generator = new AndraxiaAutoEventGenerator(events, states, _service, random, pressure);
        try
        {
            generator.Enable(StartUtc);
            var callsBefore = random.CallCount;
            var result = generator.Evaluate(StartUtc.AddMinutes(5));
            Assert.True(result.ProbabilityPassed);
            Assert.True(result.TriggerResult?.Succeeded);
            Assert.Equal(2, random.CallCount - callsBefore); // probability and next delay; one eligible definition
        }
        finally
        {
            generator.StopTimer();
        }
    }

    private Context CreateContext()
    {
        var pressure = new RegionalPressureStore();
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var encounter = new TestEventEncounterSpawner();
        _service = new AndraxiaEventService(
            events, states, [encounter], new DeterministicEncounterLocationSelector(),
            static () => 0, NullEventAwareness.Instance, pressure
        );
        return new Context(_service, pressure);
    }

    public void Dispose() => _service?.StopExpirationTimer();
    private sealed record Context(AndraxiaEventService Service, RegionalPressureStore Pressure);

    private sealed class CountingRandom(double value) : IAutoEventRandom
    {
        public ulong State { get; set; }
        public int CallCount { get; private set; }
        public double NextDouble() { CallCount++; return value; }
    }
}
