using System;
using System.IO;
using System.Linq;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class RegionalConcernTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private AndraxiaEventService _service;
    private AndraxiaAutoEventGenerator _generator;

    [Theory]
    [InlineData("none", RegionalConcern.None)]
    [InlineData("banditry", RegionalConcern.Banditry)]
    [InlineData("undead", RegionalConcern.Undead)]
    [InlineData("raiders", RegionalConcern.Raiders)]
    [InlineData("beasts", RegionalConcern.Beasts)]
    [InlineData("trade-routes", RegionalConcern.TradeRoutes)]
    public void StableTokensRoundTrip(string token, RegionalConcern expected)
    {
        Assert.True(RegionalConcernStore.TryParse(token, out var value));
        Assert.Equal(expected, value);
        Assert.Equal(token, RegionalConcernStore.Token(value));
        Assert.False(string.IsNullOrWhiteSpace(RegionalConcernStore.Description(value)));
    }

    [Fact]
    public void QuietConcernClearsOnFourthInterval()
    {
        var concern = new RegionalConcernStore();
        concern.Establish(RegionalConcern.Raiders, "test");
        concern.Stabilize(3);
        Assert.Equal(RegionalConcern.Raiders, concern.Britain);
        Assert.Equal(3, concern.QuietIntervals);
        concern.Stabilize(1);
        Assert.Equal(RegionalConcern.None, concern.Britain);
        Assert.Equal(0, concern.QuietIntervals);
    }

    [Fact]
    public void ConcernDefaultsToNoneAndQuietIntervalsAccumulateOneAtATime()
    {
        var concern = new RegionalConcernStore();
        Assert.Equal(RegionalConcern.None, concern.Britain);
        Assert.Equal(0, concern.QuietIntervals);

        concern.Establish(RegionalConcern.Banditry, "test");
        for (var expected = 1; expected <= 3; expected++)
        {
            concern.Stabilize(1);
            Assert.Equal(RegionalConcern.Banditry, concern.Britain);
            Assert.Equal(expected, concern.QuietIntervals);
        }
    }

    [Fact]
    public void FailureReplacesConcernAndResetsQuietCounter()
    {
        var concern = new RegionalConcernStore();
        concern.Establish(RegionalConcern.Banditry, "first failure");
        concern.Stabilize(3);

        concern.Establish(RegionalConcern.Undead, "second failure");

        Assert.Equal(RegionalConcern.Undead, concern.Britain);
        Assert.Equal(0, concern.QuietIntervals);
    }

    [Theory]
    [InlineData("event.test.britain-disturbance", RegionalConcern.Banditry)]
    [InlineData("event.britain.undead-disturbance", RegionalConcern.Undead)]
    [InlineData("event.britain.orc-raiding-party", RegionalConcern.Raiders)]
    [InlineData("event.britain.beast-outbreak", RegionalConcern.Beasts)]
    [InlineData("event.britain.caravan-ambush", RegionalConcern.TradeRoutes)]
    public void GenuineFailureEstablishesMappedConcern(string definitionToken, RegionalConcern expected)
    {
        var events = new EventStore(KnownEvents.Definitions);
        var id = EventInstanceId.New();
        events.Trigger(new EventDefinitionId(definitionToken), id, StartUtc);
        var concern = new RegionalConcernStore();
        var consequences = new EventOutcomeConsequences(new RegionalPressureStore(), events, concern);
        consequences.Apply(id, EventOutcomeSource.AutomaticFailure);
        Assert.Equal(expected, concern.Britain);
        Assert.Equal(0, concern.QuietIntervals);
    }

    [Fact]
    public void MatchingSuccessClearsButUnrelatedSuccessDoesNot()
    {
        var events = new EventStore(KnownEvents.Definitions);
        var beast = EventInstanceId.New();
        events.Trigger(KnownEvents.BritainBeastOutbreak, beast, StartUtc);
        var concern = new RegionalConcernStore();
        concern.Establish(RegionalConcern.Raiders, "test");
        var consequences = new EventOutcomeConsequences(new RegionalPressureStore(), events, concern);
        consequences.Apply(beast, EventOutcomeSource.CombatSuccess);
        Assert.Equal(RegionalConcern.Raiders, concern.Britain);

        var orc = EventInstanceId.New();
        events.Complete(beast, StartUtc.AddMinutes(1));
        events.Trigger(KnownEvents.BritainOrcRaidingParty, orc, StartUtc.AddMinutes(2));
        consequences.Apply(orc, EventOutcomeSource.CombatSuccess);
        Assert.Equal(RegionalConcern.None, concern.Britain);
    }

    [Fact]
    public void AdministrativeOutcomesDoNotAlterConcern()
    {
        var events = new EventStore(KnownEvents.Definitions);
        var id = EventInstanceId.New();
        events.Trigger(KnownEvents.BritainUndeadDisturbance, id, StartUtc);
        var concern = new RegionalConcernStore();
        concern.Establish(RegionalConcern.Raiders, "test");

        new EventOutcomeConsequences(new RegionalPressureStore(), events, concern)
            .Apply(id, EventOutcomeSource.Administrative);

        Assert.Equal(RegionalConcern.Raiders, concern.Britain);
    }

    [Fact]
    public void VersionTwoPersistenceRoundTripsConcernAndQuietIntervals()
    {
        using var clock = new SimulationClock(StartUtc);
        var directory = Path.Combine(Path.GetTempPath(), $"andraxia-concern-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePressure = new RegionalPressureStore();
        var sourceConcern = new RegionalConcernStore();
        var sourceStabilizer = new RegionalPressureStabilizer(sourcePressure, sourceConcern);
        var source = new RegionalPressurePersistence(sourcePressure, sourceStabilizer, sourceConcern);
        var loadedPressure = new RegionalPressureStore();
        var loadedConcern = new RegionalConcernStore();
        var loadedStabilizer = new RegionalPressureStabilizer(loadedPressure, loadedConcern);
        var loaded = new RegionalPressurePersistence(loadedPressure, loadedStabilizer, loadedConcern);
        try
        {
            sourceConcern.Establish(RegionalConcern.Beasts, "test");
            sourceConcern.Stabilize(2);
            var persistenceDirectory = Path.Combine(directory, RegionalPressurePersistence.PersistenceName);
            Directory.CreateDirectory(persistenceDirectory);
            using (var writer = new FileBufferWriter(Path.Combine(
                       persistenceDirectory, $"{RegionalPressurePersistence.PersistenceName}.bin")))
            {
                source.Serialize(writer);
            }

            loaded.Deserialize(directory, null);

            Assert.Equal(RegionalConcern.Beasts, loadedConcern.Britain);
            Assert.Equal(2, loadedConcern.QuietIntervals);
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LegacyPressureFormatsMigrateToNoConcern(int version)
    {
        using var clock = new SimulationClock(StartUtc);
        var directory = Path.Combine(Path.GetTempPath(), $"andraxia-concern-v{version}-{Guid.NewGuid():N}");
        var persistenceDirectory = Path.Combine(directory, RegionalPressurePersistence.PersistenceName);
        Directory.CreateDirectory(persistenceDirectory);
        var pressure = new RegionalPressureStore();
        var concern = new RegionalConcernStore();
        concern.Establish(RegionalConcern.TradeRoutes, "preload");
        var stabilizer = new RegionalPressureStabilizer(pressure, concern);
        var persistence = new RegionalPressurePersistence(pressure, stabilizer, concern);
        try
        {
            using (var writer = new FileBufferWriter(Path.Combine(
                       persistenceDirectory, $"{RegionalPressurePersistence.PersistenceName}.bin")))
            {
                writer.WriteEncodedInt(version);
                writer.WriteEncodedInt(60);
                if (version >= 1)
                {
                    writer.Write(StartUtc.AddMinutes(30));
                }
            }

            persistence.Deserialize(directory, null);

            Assert.Equal(RegionalConcern.None, concern.Britain);
            Assert.Equal(0, concern.QuietIntervals);
        }
        finally
        {
            stabilizer.StopTimer();
            persistence.Unregister();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void OfflineRecoveryClearsConcernMathematically()
    {
        var pressure = new RegionalPressureStore();
        var concern = new RegionalConcernStore();
        concern.Establish(RegionalConcern.Undead, "test");
        var stabilizer = new RegionalPressureStabilizer(pressure, concern);
        stabilizer.Restore(StartUtc.AddMinutes(30));
        try
        {
            stabilizer.Recover(StartUtc.AddHours(2));
            Assert.Equal(RegionalConcern.None, concern.Britain);
            Assert.Equal(0, concern.QuietIntervals);
        }
        finally
        {
            stabilizer.StopTimer();
        }
    }

    [Fact]
    public void ApplicableConcernBiasCanSelectMatchingDefinition()
    {
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var handlers = KnownEvents.AutomaticDefinitions.Select((id, index) =>
            (IEventEncounterSpawner)new TestEventEncounterSpawner((uint)(index * 100 + 1)) { DefinitionId = id }).ToArray();
        _service = new AndraxiaEventService(events, states, handlers, new DeterministicEncounterLocationSelector(),
            static () => 1, NullEventAwareness.Instance);
        var concern = new RegionalConcernStore();
        concern.Establish(RegionalConcern.Raiders, "test");
        _generator = new AndraxiaAutoEventGenerator(events, states, _service,
            new AutoEventGenerationTests.SequenceAutoEventRandom([0.0, 0.0, 0.1, 0.0]), concern: concern);
        _generator.Enable(StartUtc);

        var result = _generator.Evaluate(StartUtc.AddMinutes(5));

        Assert.Equal(KnownEvents.BritainOrcRaidingParty, result.SelectedDefinitionId);
    }

    [Fact]
    public void ConcernAlternatePathUsesNormalSelectionAndExactDrawOrder()
    {
        var context = CreateGenerator(RegionalConcern.Raiders, [0.0, 0.0, 0.75, 0.0, 0.0]);
        _service = context.Service;
        _generator = context.Generator;

        var before = context.Random.CallCount;
        var result = context.Generator.Evaluate(StartUtc.AddMinutes(5));

        Assert.NotEqual(KnownEvents.BritainOrcRaidingParty, result.SelectedDefinitionId);
        Assert.Equal(4, context.Random.CallCount - before); // probability, concern, definition, delay
    }

    [Fact]
    public void RepeatSuppressionOverridesConcernWithoutConsumingBiasDraw()
    {
        var context = CreateGenerator(RegionalConcern.Raiders, [0.0, 0.0, 0.6, 0.0]);
        _service = context.Service;
        _generator = context.Generator;
        context.Generator.Restore(true, StartUtc.AddMinutes(5), 0, KnownEvents.BritainOrcRaidingParty);

        var before = context.Random.CallCount;
        var result = context.Generator.Evaluate(StartUtc.AddMinutes(5));

        Assert.NotEqual(KnownEvents.BritainOrcRaidingParty, result.SelectedDefinitionId);
        Assert.Equal(3, context.Random.CallCount - before); // probability, normal definition, delay
    }

    private static GeneratorContext CreateGenerator(RegionalConcern value, double[] draws)
    {
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var handlers = KnownEvents.AutomaticDefinitions.Select((id, index) =>
            (IEventEncounterSpawner)new TestEventEncounterSpawner((uint)(index * 100 + 1)) { DefinitionId = id }).ToArray();
        var service = new AndraxiaEventService(events, states, handlers, new DeterministicEncounterLocationSelector(),
            static () => 1, NullEventAwareness.Instance);
        var concern = new RegionalConcernStore();
        concern.Establish(value, "test");
        var random = new AutoEventGenerationTests.SequenceAutoEventRandom(draws);
        var generator = new AndraxiaAutoEventGenerator(events, states, service, random, concern: concern);
        generator.Enable(StartUtc);
        return new GeneratorContext(service, generator, random);
    }

    public void Dispose() { _generator?.StopTimer(); _service?.StopExpirationTimer(); }

    private sealed record GeneratorContext(
        AndraxiaEventService Service,
        AndraxiaAutoEventGenerator Generator,
        AutoEventGenerationTests.SequenceAutoEventRandom Random
    );
}
