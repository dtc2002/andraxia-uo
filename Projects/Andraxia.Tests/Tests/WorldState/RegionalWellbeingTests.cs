using System;
using System.IO;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class RegionalWellbeingTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private RegionalPressureStabilizer _stabilizer;

    [Theory]
    [InlineData(0, RegionalSecurityClassification.Lawless)]
    [InlineData(24, RegionalSecurityClassification.Lawless)]
    [InlineData(25, RegionalSecurityClassification.Unstable)]
    [InlineData(49, RegionalSecurityClassification.Unstable)]
    [InlineData(50, RegionalSecurityClassification.Secure)]
    [InlineData(74, RegionalSecurityClassification.Secure)]
    [InlineData(75, RegionalSecurityClassification.WellGuarded)]
    [InlineData(100, RegionalSecurityClassification.WellGuarded)]
    public void SecurityClassificationBoundariesAreExact(int value, RegionalSecurityClassification expected)
    {
        var classification = RegionalSecurity.Classify(value);
        Assert.Equal(expected, classification);
        Assert.False(string.IsNullOrWhiteSpace(RegionalSecurity.Label(classification)));
        Assert.False(string.IsNullOrWhiteSpace(RegionalSecurity.Description(classification)));
    }

    [Theory]
    [InlineData(0, RegionalProsperityClassification.Impoverished)]
    [InlineData(24, RegionalProsperityClassification.Impoverished)]
    [InlineData(25, RegionalProsperityClassification.Struggling)]
    [InlineData(49, RegionalProsperityClassification.Struggling)]
    [InlineData(50, RegionalProsperityClassification.Prosperous)]
    [InlineData(74, RegionalProsperityClassification.Prosperous)]
    [InlineData(75, RegionalProsperityClassification.Thriving)]
    [InlineData(100, RegionalProsperityClassification.Thriving)]
    public void ProsperityClassificationBoundariesAreExact(int value, RegionalProsperityClassification expected)
    {
        var classification = RegionalProsperity.Classify(value);
        Assert.Equal(expected, classification);
        Assert.False(string.IsNullOrWhiteSpace(RegionalProsperity.Label(classification)));
        Assert.False(string.IsNullOrWhiteSpace(RegionalProsperity.Description(classification)));
    }

    [Fact]
    public void DefaultsClampingAndRegionalIndependenceArePreserved()
    {
        var first = new AndraxiaRegionDefinition(new AndraxiaRegionId("region.alpha"), "Alpha");
        var second = new AndraxiaRegionDefinition(new AndraxiaRegionId("region.beta"), "Beta", 30, 55, 65);
        var states = new RegionalStateStore([second, first]);

        Assert.True(states.TryGet(first.Id, out var alpha));
        Assert.Equal(60, alpha.Security);
        Assert.Equal(60, alpha.Prosperity);
        Assert.True(states.TryGet(second.Id, out var beta));
        Assert.Equal(55, beta.Security);
        Assert.Equal(65, beta.Prosperity);

        states.SetSecurity(first.Id, -20, "test");
        states.SetProsperity(first.Id, 120, "test");
        Assert.True(states.TryGet(first.Id, out alpha));
        Assert.True(states.TryGet(second.Id, out beta));
        Assert.Equal(0, alpha.Security);
        Assert.Equal(100, alpha.Prosperity);
        Assert.Equal(55, beta.Security);
        Assert.Equal(65, beta.Prosperity);
        Assert.False(states.SetSecurity(new AndraxiaRegionId("region.unknown"), 50));
        Assert.False(states.SetProsperity(new AndraxiaRegionId("region.unknown"), 50));
    }

    [Theory]
    [InlineData("event.test.britain-disturbance", 2, 0, -4, -1)]
    [InlineData("event.britain.undead-disturbance", 1, 0, -3, 0)]
    [InlineData("event.britain.orc-raiding-party", 2, 0, -5, -2)]
    [InlineData("event.britain.beast-outbreak", 1, 0, -2, -1)]
    [InlineData("event.britain.caravan-ambush", 1, 2, -2, -5)]
    public void EveryEventDefinitionAppliesExactSuccessAndFailureWellbeingDeltas(
        string definitionToken,
        int successSecurity,
        int successProsperity,
        int failureSecurity,
        int failureProsperity
    )
    {
        AssertImpact(definitionToken, EventOutcomeSource.CombatSuccess, successSecurity, successProsperity);
        AssertImpact(definitionToken, EventOutcomeSource.AutomaticFailure, failureSecurity, failureProsperity);
    }

    [Fact]
    public void AdministrativeOutcomeIsNeutralAndRepeatedProcessingIsIdempotent()
    {
        var (states, pressure, concern, events, id) = CreateConsequenceContext(KnownEvents.BritainCaravanAmbush);
        var consequences = new EventOutcomeConsequences(pressure, events, concern);
        consequences.Apply(id, EventOutcomeSource.Administrative);
        consequences.Apply(id, EventOutcomeSource.CombatSuccess);

        Assert.True(states.TryGet(KnownAndraxiaRegions.Britain, out var state));
        Assert.Equal(60, state.Security);
        Assert.Equal(60, state.Prosperity);

        events.Complete(id, StartUtc.AddSeconds(30));
        var second = EventInstanceId.New();
        events.Trigger(KnownEvents.BritainCaravanAmbush, second, StartUtc.AddMinutes(1));
        consequences.Apply(second, EventOutcomeSource.CombatSuccess);
        consequences.Apply(second, EventOutcomeSource.CombatSuccess);
        Assert.True(states.TryGet(KnownAndraxiaRegions.Britain, out state));
        Assert.Equal(61, state.Security);
        Assert.Equal(62, state.Prosperity);
    }

    [Fact]
    public void StabilizationMovesAllDimensionsTowardDefinitionBaselinesWithoutOvershoot()
    {
        var first = new AndraxiaRegionDefinition(new AndraxiaRegionId("region.alpha"), "Alpha", 25, 60, 60);
        var second = new AndraxiaRegionDefinition(new AndraxiaRegionId("region.beta"), "Beta", 30, 55, 65);
        var states = new RegionalStateStore([first, second]);
        var pressure = new RegionalPressureStore(states);
        var concern = new RegionalConcernStore(states);
        states.SetSecurity(first.Id, 62);
        states.SetProsperity(first.Id, 59);
        states.SetSecurity(second.Id, 55);
        states.SetProsperity(second.Id, 100);
        pressure.Set(first.Id, 27);
        _stabilizer = new RegionalPressureStabilizer(pressure, concern);
        _stabilizer.Restore(StartUtc.AddMinutes(30));

        _stabilizer.Recover(StartUtc.AddHours(5));

        Assert.True(states.TryGet(first.Id, out var alpha));
        Assert.True(states.TryGet(second.Id, out var beta));
        Assert.Equal(25, alpha.Pressure);
        Assert.Equal(60, alpha.Security);
        Assert.Equal(60, alpha.Prosperity);
        Assert.Equal(55, beta.Security);
        Assert.Equal(90, beta.Prosperity);
    }

    [Fact]
    public void VersionFourRoundTripPersistsSecurityAndProsperity()
    {
        using var clock = new SimulationClock(StartUtc);
        var sourceStates = new RegionalStateStore();
        sourceStates.SetSecurity(KnownAndraxiaRegions.Britain, 72);
        sourceStates.SetProsperity(KnownAndraxiaRegions.Britain, 44);
        var loadedStates = new RegionalStateStore();

        WithPersistence(sourceStates, loadedStates, () =>
        {
            Assert.True(loadedStates.TryGet(KnownAndraxiaRegions.Britain, out var state));
            Assert.Equal(72, state.Security);
            Assert.Equal(44, state.Prosperity);
        });
    }

    [Fact]
    public void VersionThreeMigrationInitializesDefinitionWellbeingDefaults()
    {
        using var clock = new SimulationClock(StartUtc);
        var directory = CreateDirectory();
        var states = new RegionalStateStore();
        var pressure = new RegionalPressureStore(states);
        var concern = new RegionalConcernStore(states);
        _stabilizer = new RegionalPressureStabilizer(pressure, concern);
        var persistence = new RegionalPressurePersistence(pressure, _stabilizer, concern);
        try
        {
            WritePayload(directory, writer =>
            {
                writer.WriteEncodedInt(3);
                writer.WriteEncodedInt(1);
                writer.Write("region.britain");
                writer.WriteEncodedInt(73);
                writer.Write("raiders");
                writer.WriteEncodedInt(2);
                writer.Write(StartUtc.AddMinutes(30));
            });
            persistence.Deserialize(directory, null);
            Assert.True(states.TryGet(KnownAndraxiaRegions.Britain, out var state));
            Assert.Equal(73, state.Pressure);
            Assert.Equal(RegionalConcern.Raiders, state.Concern);
            Assert.Equal(2, state.ConcernQuietIntervals);
            Assert.Equal(60, state.Security);
            Assert.Equal(60, state.Prosperity);
        }
        finally
        {
            persistence.Unregister();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void MalformedVersionFourWellbeingRetainsRegionalDefaults()
    {
        using var clock = new SimulationClock(StartUtc);
        var directory = CreateDirectory();
        var states = new RegionalStateStore();
        var pressure = new RegionalPressureStore(states);
        var concern = new RegionalConcernStore(states);
        _stabilizer = new RegionalPressureStabilizer(pressure, concern);
        var persistence = new RegionalPressurePersistence(pressure, _stabilizer, concern);
        try
        {
            WritePayload(directory, writer =>
            {
                writer.WriteEncodedInt(4);
                writer.WriteEncodedInt(1);
                writer.Write("region.britain");
                writer.WriteEncodedInt(30);
                writer.Write("none");
                writer.WriteEncodedInt(0);
                writer.WriteEncodedInt(101);
                writer.WriteEncodedInt(-1);
                writer.Write(StartUtc.AddMinutes(30));
            });
            persistence.Deserialize(directory, null);
            Assert.True(states.TryGet(KnownAndraxiaRegions.Britain, out var state));
            Assert.Equal(25, state.Pressure);
            Assert.Equal(60, state.Security);
            Assert.Equal(60, state.Prosperity);
        }
        finally
        {
            persistence.Unregister();
            Directory.Delete(directory, true);
        }
    }

    public void Dispose() => _stabilizer?.StopTimer();

    private static void AssertImpact(
        string definitionToken,
        EventOutcomeSource source,
        int expectedSecurity,
        int expectedProsperity
    )
    {
        var definitionId = new EventDefinitionId(definitionToken);
        var (states, pressure, concern, events, id) = CreateConsequenceContext(definitionId);
        new EventOutcomeConsequences(pressure, events, concern).Apply(id, source);
        Assert.True(states.TryGet(KnownAndraxiaRegions.Britain, out var state));
        Assert.Equal(60 + expectedSecurity, state.Security);
        Assert.Equal(60 + expectedProsperity, state.Prosperity);
    }

    private static (RegionalStateStore, RegionalPressureStore, RegionalConcernStore, EventStore, EventInstanceId)
        CreateConsequenceContext(EventDefinitionId definitionId)
    {
        var states = new RegionalStateStore();
        var pressure = new RegionalPressureStore(states);
        var concern = new RegionalConcernStore(states);
        var events = new EventStore(KnownEvents.Definitions);
        var id = EventInstanceId.New();
        events.Trigger(definitionId, id, StartUtc);
        return (states, pressure, concern, events, id);
    }

    private static void WithPersistence(RegionalStateStore sourceStates, RegionalStateStore loadedStates, Action assertion)
    {
        var directory = CreateDirectory();
        var sourcePressure = new RegionalPressureStore(sourceStates);
        var sourceConcern = new RegionalConcernStore(sourceStates);
        var sourceStabilizer = new RegionalPressureStabilizer(sourcePressure, sourceConcern);
        var source = new RegionalPressurePersistence(sourcePressure, sourceStabilizer, sourceConcern);
        var loadedPressure = new RegionalPressureStore(loadedStates);
        var loadedConcern = new RegionalConcernStore(loadedStates);
        var loadedStabilizer = new RegionalPressureStabilizer(loadedPressure, loadedConcern);
        var loaded = new RegionalPressurePersistence(loadedPressure, loadedStabilizer, loadedConcern);
        try
        {
            WritePayload(directory, source.Serialize);
            loaded.Deserialize(directory, null);
            assertion();
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

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"andraxia-wellbeing-{Guid.NewGuid():N}");
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
}
