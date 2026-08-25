using System;
using System.Collections.Generic;
using System.IO;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class AutoEventPersistenceTests
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void VersionFourRoundTripsEnabledScheduleAndRandomState()
    {
        using var source = new TestContext();
        using var loaded = new TestContext();
        var next = StartUtc.AddMinutes(7);
        source.Generator.Restore(true, next, 123456789);
        var writer = new BufferWriter(new byte[512], true);
        source.Persistence.Serialize(writer);

        loaded.Persistence.Deserialize(new BufferReader(writer.Buffer));

        Assert.True(loaded.Generator.Enabled);
        Assert.Equal(next, loaded.Generator.NextEvaluationUtc);
        Assert.Equal(123456789UL, loaded.Generator.RandomState);
        Assert.False(loaded.Generator.TimerRunning);
    }

    [Fact]
    public void VersionFourRoundTripsDisabledState()
    {
        using var source = new TestContext();
        using var loaded = new TestContext();
        var writer = new BufferWriter(new byte[512], true);
        source.Persistence.Serialize(writer);

        loaded.Persistence.Deserialize(new BufferReader(writer.Buffer));

        Assert.False(loaded.Generator.Enabled);
        Assert.Null(loaded.Generator.NextEvaluationUtc);
        Assert.False(loaded.Generator.TimerRunning);
    }

    [Fact]
    public void VersionTenRoundTripsRecentAutomaticChoices()
    {
        using var source = new TestContext();
        using var loaded = new TestContext();
        source.Generator.Restore(
            false,
            null,
            42,
            KnownEvents.BritainDisturbance,
            new Dictionary<EventDefinitionId, EncounterLocationId>
            {
                [KnownEvents.BritainDisturbance] = KnownEncounterLocations.BritainRoadNorth,
                [KnownEvents.BritainUndeadDisturbance] = KnownEncounterLocations.BritainUndeadGraveyardEast,
                [KnownEvents.BritainOrcRaidingParty] = KnownEncounterLocations.BritainOrcCampWest,
                [KnownEvents.BritainBeastOutbreak] = KnownEncounterLocations.BritainBeastForestWest,
                [KnownEvents.BritainCaravanAmbush] = KnownEncounterLocations.BritainCaravanRoadNorth
            }
        );
        var writer = new BufferWriter(new byte[1024], true);
        source.Persistence.Serialize(writer);

        loaded.Persistence.Deserialize(new BufferReader(writer.Buffer));

        Assert.Equal(KnownEvents.BritainDisturbance, loaded.Generator.LastAutomaticDefinitionId);
        Assert.Equal(
            KnownEncounterLocations.BritainRoadNorth,
            loaded.Generator.GetLastAutomaticLocation(KnownEvents.BritainDisturbance)
        );
        Assert.Equal(
            KnownEncounterLocations.BritainUndeadGraveyardEast,
            loaded.Generator.GetLastAutomaticLocation(KnownEvents.BritainUndeadDisturbance)
        );
        Assert.Equal(KnownEncounterLocations.BritainOrcCampWest,
            loaded.Generator.GetLastAutomaticLocation(KnownEvents.BritainOrcRaidingParty));
        Assert.Equal(KnownEncounterLocations.BritainBeastForestWest,
            loaded.Generator.GetLastAutomaticLocation(KnownEvents.BritainBeastOutbreak));
        Assert.Equal(KnownEncounterLocations.BritainCaravanRoadNorth,
            loaded.Generator.GetLastAutomaticLocation(KnownEvents.BritainCaravanAmbush));
    }

    [Fact]
    public void VersionEightMigratesWithEmptyRecentMemory()
    {
        using var context = new TestContext();
        var writer = new BufferWriter(new byte[64], true);
        writer.WriteEncodedInt(8);
        writer.WriteEncodedInt(0);
        writer.Write(false);
        writer.Write(false);
        writer.Write(42UL);

        context.Persistence.Deserialize(new BufferReader(writer.Buffer));

        Assert.Null(context.Generator.LastAutomaticDefinitionId);
        Assert.Null(context.Generator.GetLastAutomaticLocation(KnownEvents.BritainDisturbance));
        Assert.Null(context.Generator.GetLastAutomaticLocation(KnownEvents.BritainUndeadDisturbance));
    }

    [Fact]
    public void VersionNineMigratesFixedRecentLocations()
    {
        using var context = new TestContext();
        var writer = new BufferWriter(new byte[512], true);
        writer.WriteEncodedInt(9);
        writer.WriteEncodedInt(0);
        writer.Write(false);
        writer.Write(false);
        writer.Write(42UL);
        writer.Write(true);
        writer.Write(KnownEvents.BritainDisturbance.Value);
        writer.Write(true);
        writer.Write(KnownEncounterLocations.BritainRoadNorth.Value);
        writer.Write(true);
        writer.Write(KnownEncounterLocations.BritainUndeadGraveyardEast.Value);

        context.Persistence.Deserialize(new BufferReader(writer.Buffer));

        Assert.Equal(KnownEvents.BritainDisturbance, context.Generator.LastAutomaticDefinitionId);
        Assert.Equal(KnownEncounterLocations.BritainRoadNorth,
            context.Generator.GetLastAutomaticLocation(KnownEvents.BritainDisturbance));
    }

    [Fact]
    public void LegacyVersionThreeDefaultsDisabled()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        var writer = new BufferWriter(new byte[64], true);
        writer.WriteEncodedInt(3);
        writer.WriteEncodedInt(0);

        context.Persistence.Deserialize(new BufferReader(writer.Buffer));
        context.Persistence.PostDeserialize();

        Assert.False(context.Generator.Enabled);
        Assert.Null(context.Generator.NextEvaluationUtc);
        Assert.False(context.Generator.TimerRunning);
    }

    [Fact]
    public void FutureEvaluationRearmsAtPersistedTime()
    {
        using var clock = new SimulationClock(StartUtc);
        using var source = new TestContext();
        using var loaded = new TestContext();
        var next = StartUtc.AddMinutes(7);
        source.Generator.Restore(true, next, 42);
        var writer = new BufferWriter(new byte[512], true);
        source.Persistence.Serialize(writer);
        loaded.Persistence.Deserialize(new BufferReader(writer.Buffer));

        loaded.Persistence.PostDeserialize();

        Assert.True(loaded.Generator.TimerRunning);
        Assert.Equal(next, loaded.Generator.NextEvaluationUtc);
        Assert.Equal(0, loaded.Random.CallCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-525600)]
    public void OverdueRecoveryEvaluatesAtMostOnceAndSchedulesFromNow(int overdueMinutes)
    {
        using var clock = new SimulationClock(StartUtc);
        using var source = new TestContext();
        using var loaded = new TestContext(0.9, 0.0);
        source.Generator.Restore(true, StartUtc.AddMinutes(overdueMinutes), 42);
        var writer = new BufferWriter(new byte[512], true);
        source.Persistence.Serialize(writer);
        loaded.Persistence.Deserialize(new BufferReader(writer.Buffer));

        loaded.Persistence.PostDeserialize();

        Assert.Equal(2, loaded.Random.CallCount);
        Assert.Empty(loaded.Events.EnumerateInstances());
        Assert.Equal(StartUtc.AddMinutes(5), loaded.Generator.NextEvaluationUtc);
        Assert.True(loaded.Generator.TimerRunning);
    }

    [Fact]
    public void MissingPersistenceFileRestoresDisabledDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"andraxia-auto-events-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var clock = new SimulationClock(StartUtc);
            using var context = new TestContext(0.0);
            Assert.True(context.Generator.Enable(StartUtc));

            context.Persistence.Deserialize(directory, null);
            context.Persistence.PostDeserialize();

            Assert.False(context.Generator.Enabled);
            Assert.Null(context.Generator.NextEvaluationUtc);
            Assert.False(context.Generator.TimerRunning);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(params double[] values)
        {
            Events = new EventStore(KnownEvents.Definitions);
            WorldStates = new WorldStateStore(KnownWorldStates.Definitions);
            Service = new AndraxiaEventService(Events, WorldStates, new TestEventEncounterSpawner());
            Random = new SequenceRandom(values);
            Generator = new AndraxiaAutoEventGenerator(Events, WorldStates, Service, Random);
            Persistence = new AndraxiaEventPersistence(Events, WorldStates, Service, Generator);
        }

        public EventStore Events { get; }
        public WorldStateStore WorldStates { get; }
        public AndraxiaEventService Service { get; }
        public SequenceRandom Random { get; }
        public AndraxiaAutoEventGenerator Generator { get; }
        public AndraxiaEventPersistence Persistence { get; }

        public void Dispose()
        {
            Generator.StopTimer();
            Service.StopExpirationTimer();
            Persistence.Unregister();
        }
    }

    private sealed class SequenceRandom(IEnumerable<double> values) : IAutoEventRandom
    {
        private readonly Queue<double> _values = new(values);

        public ulong State { get; set; }
        public int CallCount { get; private set; }

        public double NextDouble()
        {
            CallCount++;
            State++;
            return _values.Dequeue();
        }
    }
}
