using System;
using System.IO;
using System.Linq;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class RegionalFoundationTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private RegionalPressureStabilizer _stabilizer;

    [Fact]
    public void ProductionCatalogContainsOnlyStableBritainDefinition()
    {
        var definition = Assert.Single(KnownAndraxiaRegions.Definitions);
        Assert.Equal("region.britain", definition.Id.Value);
        Assert.Equal("Britain", definition.DisplayName);
        Assert.True(KnownAndraxiaRegions.TryResolve(KnownEvents.Britain, out var regionId));
        Assert.Equal(definition.Id, regionId);
    }

    [Fact]
    public void StoreRejectsUnknownRegionAndEnumeratesByOrdinalStableId()
    {
        var alpha = new AndraxiaRegionDefinition(new AndraxiaRegionId("region.alpha"), "Alpha");
        var zeta = new AndraxiaRegionDefinition(new AndraxiaRegionId("region.zeta"), "Zeta");
        var store = new RegionalStateStore([zeta, alpha]);

        Assert.Equal([alpha.Id, zeta.Id], store.Enumerate().Select(static state => state.Definition.Id));
        Assert.False(store.TryGet(new AndraxiaRegionId("region.unknown"), out _));
        Assert.False(store.SetPressure(new AndraxiaRegionId("region.unknown"), 50));
    }

    [Fact]
    public void PressureAndConcernAreIndependentBetweenRegions()
    {
        var (states, pressure, concern, first, second) = CreateTwoRegions();
        pressure.Set(first.Id, 80, "test");
        concern.Establish(first.Id, RegionalConcern.Raiders, "test");

        Assert.True(states.TryGet(first.Id, out var changed));
        Assert.True(states.TryGet(second.Id, out var unchanged));
        Assert.Equal(80, changed.Pressure);
        Assert.Equal(RegionalConcern.Raiders, changed.Concern);
        Assert.Equal(25, unchanged.Pressure);
        Assert.Equal(RegionalConcern.None, unchanged.Concern);
    }

    [Fact]
    public void OneStabilizerRecoversEveryRegionMathematicallyAndClearsConcern()
    {
        var (_, pressure, concern, first, second) = CreateTwoRegions();
        pressure.Set(first.Id, 80);
        pressure.Set(second.Id, 0);
        concern.Establish(first.Id, RegionalConcern.Raiders, "test");
        concern.Establish(second.Id, RegionalConcern.Beasts, "test");
        _stabilizer = new RegionalPressureStabilizer(pressure, concern);
        _stabilizer.Restore(StartUtc.AddMinutes(30));

        _stabilizer.Recover(StartUtc.AddHours(2));

        Assert.Equal(76, pressure.Get(first.Id));
        Assert.Equal(4, pressure.Get(second.Id));
        Assert.Equal(RegionalConcern.None, concern.Get(first.Id));
        Assert.Equal(RegionalConcern.None, concern.Get(second.Id));
        Assert.True(_stabilizer.TimerRunning);
    }

    [Fact]
    public void EventConsequencesRouteToDefinitionTargetRegionOnly()
    {
        var (states, pressure, concern, first, second) = CreateTwoRegions();
        var definition = new EventDefinition(
            new EventDefinitionId("event.test.second"), new EventTargetId(second.Id.Value), TimeSpan.FromMinutes(5)
        );
        var events = new EventStore([definition]);
        var id = EventInstanceId.New();
        events.Trigger(definition.Id, id, StartUtc);

        new EventOutcomeConsequences(pressure, events, concern).Apply(id, EventOutcomeSource.AutomaticFailure);

        Assert.True(states.TryGet(first.Id, out var untouched));
        Assert.True(states.TryGet(second.Id, out var affected));
        Assert.Equal(25, untouched.Pressure);
        Assert.Equal(35, affected.Pressure);
        Assert.Equal(RegionalConcern.Banditry, affected.Concern);
    }

    [Fact]
    public void RegionalConcernRumorKeysAreStableAndIsolated()
    {
        var britain = ModernUOEventAwareness.ConcernRumorKeyFor(KnownAndraxiaRegions.Britain);
        var test = ModernUOEventAwareness.ConcernRumorKeyFor(new AndraxiaRegionId("region.test"));

        Assert.Equal(ModernUOEventAwareness.ConcernRumorKey, britain);
        Assert.Equal("region.test.concern", test);
        Assert.NotEqual(britain, test);
    }

    [Fact]
    public void VersionThreeRoundTripsMultipleRegionsInStableLayout()
    {
        using var clock = new SimulationClock(StartUtc);
        var definitions = new[]
        {
            new AndraxiaRegionDefinition(new AndraxiaRegionId("region.zeta"), "Zeta"),
            new AndraxiaRegionDefinition(new AndraxiaRegionId("region.alpha"), "Alpha")
        };
        var sourceStates = new RegionalStateStore(definitions);
        var sourcePressure = new RegionalPressureStore(sourceStates);
        var sourceConcern = new RegionalConcernStore(sourceStates);
        sourcePressure.Set(definitions[0].Id, 70);
        sourceConcern.Establish(definitions[1].Id, RegionalConcern.Undead, "test");
        sourceConcern.Stabilize(definitions[1].Id, 2);
        var loadedStates = new RegionalStateStore(definitions);
        var loadedPressure = new RegionalPressureStore(loadedStates);
        var loadedConcern = new RegionalConcernStore(loadedStates);

        WithPersistence(sourcePressure, sourceConcern, loadedPressure, loadedConcern, directory =>
        {
            Assert.Equal(70, loadedPressure.Get(definitions[0].Id));
            Assert.Equal(RegionalConcern.Undead, loadedConcern.Get(definitions[1].Id));
            Assert.Equal(2, loadedConcern.GetQuietIntervals(definitions[1].Id));
        });
    }

    [Fact]
    public void PhaseTwoVersionTwoMigratesBritainWithoutLoss()
    {
        using var clock = new SimulationClock(StartUtc);
        var directory = CreateDirectory("phase2-migration");
        var states = new RegionalStateStore();
        var pressure = new RegionalPressureStore(states);
        var concern = new RegionalConcernStore(states);
        _stabilizer = new RegionalPressureStabilizer(pressure, concern);
        var persistence = new RegionalPressurePersistence(pressure, _stabilizer, concern);
        try
        {
            WritePayload(directory, writer =>
            {
                writer.WriteEncodedInt(2);
                writer.WriteEncodedInt(73);
                writer.Write(StartUtc.AddHours(1));
                writer.Write("trade-routes");
                writer.WriteEncodedInt(3);
            });
            persistence.Deserialize(directory, null);
            Assert.Equal(73, pressure.Britain);
            Assert.Equal(RegionalConcern.TradeRoutes, concern.Britain);
            Assert.Equal(3, concern.QuietIntervals);
            Assert.Equal(StartUtc.AddHours(1), _stabilizer.NextRecoveryUtc);
        }
        finally
        {
            persistence.Unregister();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void UnknownAndMalformedVersionThreeRecordsRetainKnownDefaults()
    {
        using var clock = new SimulationClock(StartUtc);
        var directory = CreateDirectory("regional-invalid-records");
        var pressure = new RegionalPressureStore();
        var concern = new RegionalConcernStore(pressure.States);
        _stabilizer = new RegionalPressureStabilizer(pressure, concern);
        var persistence = new RegionalPressurePersistence(pressure, _stabilizer, concern);
        try
        {
            WritePayload(directory, writer =>
            {
                writer.WriteEncodedInt(3);
                writer.WriteEncodedInt(2);
                WriteRecord(writer, "region.unknown", 80, "raiders", 2);
                WriteRecord(writer, "region.britain", 101, "invalid", 7);
                writer.Write(StartUtc.AddMinutes(30));
            });
            persistence.Deserialize(directory, null);
            Assert.Equal(25, pressure.Britain);
            Assert.Equal(RegionalConcern.None, concern.Britain);
        }
        finally
        {
            persistence.Unregister();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void UnsupportedFutureRegionalVersionIsRejected()
    {
        using var clock = new SimulationClock(StartUtc);
        var directory = CreateDirectory("regional-future");
        var pressure = new RegionalPressureStore();
        _stabilizer = new RegionalPressureStabilizer(pressure);
        var persistence = new RegionalPressurePersistence(pressure, _stabilizer);
        try
        {
            var path = Path.Combine(directory, RegionalPressurePersistence.PersistenceName,
                $"{RegionalPressurePersistence.PersistenceName}.bin");
            WritePayload(directory, writer => writer.WriteEncodedInt(RegionalPressurePersistence.CurrentVersion + 1));
            var payload = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => persistence.Deserialize(new BufferReader(payload)));
        }
        finally
        {
            persistence.Unregister();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DuplicateRegionIsFirstEntryWinsAndExcessiveCountIsRejected()
    {
        using var clock = new SimulationClock(StartUtc);
        var directory = CreateDirectory("regional-duplicates");
        var pressure = new RegionalPressureStore();
        var concern = new RegionalConcernStore(pressure.States);
        _stabilizer = new RegionalPressureStabilizer(pressure, concern);
        var persistence = new RegionalPressurePersistence(pressure, _stabilizer, concern);
        try
        {
            WritePayload(directory, writer =>
            {
                writer.WriteEncodedInt(3);
                writer.WriteEncodedInt(2);
                WriteRecord(writer, "region.britain", 40, "banditry", 1);
                WriteRecord(writer, "region.britain", 90, "raiders", 3);
                writer.Write(StartUtc.AddMinutes(30));
            });
            var path = Path.Combine(directory, RegionalPressurePersistence.PersistenceName,
                $"{RegionalPressurePersistence.PersistenceName}.bin");
            persistence.Deserialize(new BufferReader(File.ReadAllBytes(path)));
            Assert.Equal(40, pressure.Britain);
            Assert.Equal(RegionalConcern.Banditry, concern.Britain);

            WritePayload(directory, writer =>
            {
                writer.WriteEncodedInt(3);
                writer.WriteEncodedInt(RegionalPressurePersistence.MaximumRegionCount + 1);
            });
            Assert.Throws<InvalidDataException>(() =>
                persistence.Deserialize(new BufferReader(File.ReadAllBytes(path))));
        }
        finally
        {
            persistence.Unregister();
            Directory.Delete(directory, true);
        }
    }

    public void Dispose() => _stabilizer?.StopTimer();

    private static (RegionalStateStore, RegionalPressureStore, RegionalConcernStore,
        AndraxiaRegionDefinition, AndraxiaRegionDefinition) CreateTwoRegions()
    {
        var first = new AndraxiaRegionDefinition(new AndraxiaRegionId("region.alpha"), "Alpha");
        var second = new AndraxiaRegionDefinition(new AndraxiaRegionId("region.zeta"), "Zeta");
        var states = new RegionalStateStore([second, first]);
        return (states, new RegionalPressureStore(states), new RegionalConcernStore(states), first, second);
    }

    private static void WithPersistence(
        RegionalPressureStore sourcePressure,
        RegionalConcernStore sourceConcern,
        RegionalPressureStore loadedPressure,
        RegionalConcernStore loadedConcern,
        Action<string> assertion
    )
    {
        var directory = CreateDirectory("regional-roundtrip");
        var sourceStabilizer = new RegionalPressureStabilizer(sourcePressure, sourceConcern);
        var loadedStabilizer = new RegionalPressureStabilizer(loadedPressure, loadedConcern);
        var source = new RegionalPressurePersistence(sourcePressure, sourceStabilizer, sourceConcern);
        var loaded = new RegionalPressurePersistence(loadedPressure, loadedStabilizer, loadedConcern);
        try
        {
            WritePayload(directory, source.Serialize);
            loaded.Deserialize(directory, null);
            assertion(directory);
        }
        finally
        {
            sourceStabilizer.StopTimer();
            loadedStabilizer.StopTimer();
            source.Unregister();
            loaded.Unregister();
            Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory(string label)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"andraxia-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, RegionalPressurePersistence.PersistenceName));
        return directory;
    }

    private static void WritePayload(string directory, Action<IGenericWriter> write)
    {
        using var writer = new FileBufferWriter(Path.Combine(
            directory, RegionalPressurePersistence.PersistenceName,
            $"{RegionalPressurePersistence.PersistenceName}.bin"
        ));
        write(writer);
    }

    private static void WriteRecord(IGenericWriter writer, string id, int pressure, string concern, int quiet)
    {
        writer.Write(id);
        writer.WriteEncodedInt(pressure);
        writer.Write(concern);
        writer.WriteEncodedInt(quiet);
    }
}
