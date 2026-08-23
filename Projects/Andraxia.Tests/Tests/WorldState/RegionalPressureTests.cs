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
            static () => 1, NullEventAwareness.Instance, pressure
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

    [Theory]
    [InlineData(25, 25)]
    [InlineData(26, 25)]
    [InlineData(24, 25)]
    [InlineData(70, 69)]
    [InlineData(0, 1)]
    public void StabilizationMovesOnePointTowardBaseline(int initial, int expected)
    {
        var pressure = new RegionalPressureStore();
        pressure.SetBritain(initial);
        var stabilizer = new RegionalPressureStabilizer(pressure);
        stabilizer.Restore(StartUtc);
        try
        {
            stabilizer.Recover(StartUtc);
            Assert.Equal(expected, pressure.Britain);
            Assert.Equal(StartUtc.AddMinutes(30), stabilizer.NextRecoveryUtc);
        }
        finally
        {
            stabilizer.StopTimer();
        }
    }

    [Theory]
    [InlineData(70, 6, 64)]
    [InlineData(27, 20, 25)]
    [InlineData(0, 100, 25)]
    public void OfflineRecoveryAppliesElapsedIntervalsMathematically(int initial, int intervals, int expected)
    {
        var pressure = new RegionalPressureStore();
        pressure.SetBritain(initial);
        var stabilizer = new RegionalPressureStabilizer(pressure);
        stabilizer.Restore(StartUtc.AddMinutes(30));
        try
        {
            stabilizer.Recover(StartUtc.AddMinutes(30 * intervals));
            Assert.Equal(expected, pressure.Britain);
            Assert.True(stabilizer.NextRecoveryUtc > StartUtc.AddMinutes(30 * intervals));
        }
        finally
        {
            stabilizer.StopTimer();
        }
    }

    [Fact]
    public void FutureRecoveryRearmsWithoutApplyingEarly()
    {
        var pressure = new RegionalPressureStore();
        pressure.SetBritain(70);
        var stabilizer = new RegionalPressureStabilizer(pressure);
        stabilizer.Restore(StartUtc.AddMinutes(30));
        try
        {
            stabilizer.Recover(StartUtc);
            Assert.Equal(70, pressure.Britain);
            Assert.True(stabilizer.TimerRunning);
        }
        finally
        {
            stabilizer.StopTimer();
        }
    }

    [Fact]
    public void StabilizationDoesNotAlterActiveEventOrWorldCondition()
    {
        var context = CreateContext();
        context.Pressure.SetBritain(70);
        var result = context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);
        var stabilizer = new RegionalPressureStabilizer(context.Pressure);
        stabilizer.Restore(StartUtc.AddMinutes(30));
        try
        {
            stabilizer.Recover(StartUtc.AddMinutes(30));
            Assert.Equal(69, context.Pressure.Britain);
            Assert.Equal(EventLifecycleState.Active, result.EventResult.Instance.State);
            Assert.True(context.WorldStates.TryGetState(KnownWorldStates.Britain, out var condition));
            Assert.Equal(WorldCondition.Threatened, condition);
        }
        finally
        {
            stabilizer.StopTimer();
        }
    }

    [Fact]
    public void VersionZeroMigrationRetainsPressureAndStartsNewSchedule()
    {
        using var clock = new SimulationClock(StartUtc);
        var directory = Path.Combine(Path.GetTempPath(), $"andraxia-pressure-v0-{Guid.NewGuid():N}");
        var persistenceDirectory = Path.Combine(directory, RegionalPressurePersistence.PersistenceName);
        Directory.CreateDirectory(persistenceDirectory);
        var store = new RegionalPressureStore();
        var stabilizer = new RegionalPressureStabilizer(store);
        var persistence = new RegionalPressurePersistence(store, stabilizer);
        try
        {
            using (var writer = new FileBufferWriter(Path.Combine(
                       persistenceDirectory, $"{RegionalPressurePersistence.PersistenceName}.bin")))
            {
                writer.WriteEncodedInt(0);
                writer.WriteEncodedInt(70);
            }
            persistence.Deserialize(directory, null);
            Assert.Equal(70, store.Britain);
            Assert.Equal(StartUtc.AddMinutes(30), stabilizer.NextRecoveryUtc);
        }
        finally
        {
            stabilizer.StopTimer();
            persistence.Unregister();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void MissingPersistenceDefaultsPressureAndStartsRecoverySchedule()
    {
        using var clock = new SimulationClock(StartUtc);
        var directory = Path.Combine(Path.GetTempPath(), $"andraxia-pressure-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var store = new RegionalPressureStore();
        store.SetBritain(70);
        var stabilizer = new RegionalPressureStabilizer(store);
        var persistence = new RegionalPressurePersistence(store, stabilizer);
        try
        {
            persistence.Deserialize(directory, null);
            Assert.Equal(25, store.Britain);
            Assert.Equal(StartUtc.AddMinutes(30), stabilizer.NextRecoveryUtc);
        }
        finally
        {
            stabilizer.StopTimer();
            persistence.Unregister();
            Directory.Delete(directory, true);
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
        return new Context(_service, pressure, states);
    }

    public void Dispose() => _service?.StopExpirationTimer();
    private sealed record Context(
        AndraxiaEventService Service,
        RegionalPressureStore Pressure,
        WorldStateStore WorldStates
    );

    private sealed class CountingRandom(double value) : IAutoEventRandom
    {
        public ulong State { get; set; }
        public int CallCount { get; private set; }
        public double NextDouble() { CallCount++; return value; }
    }
}
