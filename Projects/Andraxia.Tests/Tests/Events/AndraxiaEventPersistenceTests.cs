using System;
using System.IO;
using System.Linq;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public class AndraxiaEventPersistenceTests
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly EventInstanceId FirstId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Theory]
    [InlineData(EventLifecycleState.Active)]
    [InlineData(EventLifecycleState.Succeeded)]
    [InlineData(EventLifecycleState.Failed)]
    public void VersionFourRoundTripsEventAndUtcTimestamps(EventLifecycleState state)
    {
        WithTemporaryDirectory(
            directory =>
            {
                using var source = new TestContext(CreateEventStore(state));
                using var loaded = new TestContext();
                WritePayload(directory, source.Persistence.Serialize);

                loaded.Persistence.Deserialize(directory, null);

                var instance = Assert.Single(loaded.Events.EnumerateInstances());
                Assert.Equal(state, instance.State);
                Assert.Equal(StartUtc, instance.StartedUtc);
                Assert.Equal(StartUtc.AddMinutes(5), instance.ExpiresUtc);
                Assert.Equal(state == EventLifecycleState.Active ? null : StartUtc.AddMinutes(1), instance.CompletedUtc);
                Assert.Equal(DateTimeKind.Utc, instance.StartedUtc.Kind);
                Assert.Equal(DateTimeKind.Utc, instance.ExpiresUtc.Kind);
            }
        );
    }

    [Fact]
    public void VersionFourRoundTripsOwnedMobilesAndSelectedLocation()
    {
        using var source = new TestContext();
        using var loaded = new TestContext();
        source.Service.Pressure.SetBritain(75);
        Assert.True(source.Service.Trigger(KnownEvents.BritainDisturbance, FirstId, StartUtc).Succeeded);
        var expected = Assert.Single(source.Events.EnumerateInstances()).OwnedMobiles.ToArray();
        var expectedLocation = Assert.Single(source.Events.EnumerateInstances()).SelectedLocationId;
        var writer = new BufferWriter(new byte[512], true);
        source.Persistence.Serialize(writer);

        loaded.Persistence.Deserialize(new BufferReader(writer.Buffer));

        var instance = Assert.Single(loaded.Events.EnumerateInstances());
        Assert.Equal(expected, instance.OwnedMobiles);
        Assert.Equal(expectedLocation, instance.SelectedLocationId);
        Assert.Equal(EncounterSeverity.Severe, instance.Severity);
    }

    [Fact]
    public void MissingPersistenceFileYieldsEmptyStore()
    {
        WithTemporaryDirectory(
            directory =>
            {
                using var clock = new SimulationClock(StartUtc);
                using var context = new TestContext(CreateEventStore(EventLifecycleState.Active));
                context.Persistence.Deserialize(directory, null);
                context.Persistence.PostDeserialize();

                Assert.Empty(context.Events.EnumerateInstances());
                Assert.False(context.Service.Scheduler.TimerRunning);
            }
        );
    }

    [Theory]
    [InlineData("not-a-guid", "event.test.britain-disturbance", "region.britain", "active")]
    [InlineData("11111111111111111111111111111111", "event.unknown", "region.britain", "active")]
    [InlineData("11111111111111111111111111111111", "event.test.britain-disturbance", "region.britain", "future")]
    public void InvalidVersionZeroRecordIsIgnored(
        string instanceId,
        string definitionId,
        string targetId,
        string lifecycle
    )
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        var reader = CreateReader(
            writer =>
            {
                WriteHeader(writer, 0, 1);
                WriteVersionZeroEntry(writer, instanceId, definitionId, targetId, lifecycle);
            }
        );

        context.Persistence.Deserialize(reader);

        Assert.Empty(context.Events.EnumerateInstances());
    }

    [Fact]
    public void DuplicateInstanceIdentifierUsesFirstEntry()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        var reader = CreateReader(
            writer =>
            {
                WriteHeader(writer, 0, 2);
                WriteVersionZeroEntry(writer, FirstId.ToString(), KnownEvents.BritainDisturbance.Value, KnownEvents.Britain.Value, "succeeded");
                WriteVersionZeroEntry(writer, FirstId.ToString(), KnownEvents.BritainDisturbance.Value, KnownEvents.Britain.Value, "failed");
            }
        );

        context.Persistence.Deserialize(reader);

        Assert.Equal(EventLifecycleState.Succeeded, Assert.Single(context.Events.EnumerateInstances()).State);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(AndraxiaEventPersistence.MaxEntryCount + 1)]
    public void InvalidEntryCountIsRejected(int count)
    {
        using var context = new TestContext();
        var reader = CreateReader(
            writer =>
            {
                writer.WriteEncodedInt(AndraxiaEventPersistence.CurrentVersion);
                writer.WriteEncodedInt(count);
            }
        );

        Assert.Throws<InvalidDataException>(() => context.Persistence.Deserialize(reader));
    }

    [Fact]
    public void UnsupportedFutureVersionIsRejectedWithoutRewritingFile()
    {
        WithTemporaryDirectory(
            directory =>
            {
                var path = WritePayload(
                    directory,
                    writer => WriteHeader(writer, AndraxiaEventPersistence.CurrentVersion + 1, 0)
                );
                var original = File.ReadAllBytes(path);
                using var context = new TestContext();

                var exception = Assert.Throws<InvalidDataException>(
                    () => context.Persistence.Deserialize(new BufferReader(original))
                );

                Assert.Contains("Unsupported AndraxiaEvents format version", exception.Message);
                Assert.Equal(original, File.ReadAllBytes(path));
            }
        );
    }

    [Fact]
    public void VersionZeroActiveReceivesFreshDurationAtMigration()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();

        context.Persistence.Deserialize(CreateVersionZeroReader(EventLifecycleState.Active));

        var instance = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(StartUtc, instance.StartedUtc);
        Assert.Equal(StartUtc.AddMinutes(5), instance.ExpiresUtc);
        Assert.Null(instance.CompletedUtc);
        Assert.Empty(instance.OwnedMobiles);
        Assert.Null(instance.SelectedLocationId);
        Assert.Equal(EncounterSeverity.Normal, instance.Severity);
    }

    [Fact]
    public void VersionSevenActiveMigratesToNormalSeverity()
    {
        using var context = new TestContext();
        var reader = CreateReader(writer =>
        {
            WriteHeader(writer, 7, 1);
            WriteVersionZeroEntry(
                writer, FirstId.ToString(), KnownEvents.BritainDisturbance.Value,
                KnownEvents.Britain.Value, "active"
            );
            writer.Write(StartUtc);
            writer.Write(StartUtc.AddMinutes(5));
            writer.Write(false);
            writer.WriteEncodedInt(0);
            writer.Write(false);
            writer.Write(false);
            writer.WriteEncodedInt(0);
            writer.WriteEncodedInt(0);
            writer.Write("none");
            writer.Write(false);
            writer.Write(false);
            writer.Write(false);
            writer.Write(AndraxiaAutoEventGenerator.DefaultRandomState);
        });

        context.Persistence.Deserialize(reader);

        Assert.Equal(EncounterSeverity.Normal, Assert.Single(context.Events.EnumerateInstances()).Severity);
    }

    [Fact]
    public void VersionOneActiveMigratesWithNoOwnedMobiles()
    {
        using var context = new TestContext();

        context.Persistence.Deserialize(
            CreateVersionOneReader(EventLifecycleState.Active, StartUtc, StartUtc.AddMinutes(5), null)
        );

        Assert.Empty(Assert.Single(context.Events.EnumerateInstances()).OwnedMobiles);
        Assert.Null(Assert.Single(context.Events.EnumerateInstances()).SelectedLocationId);
    }

    [Fact]
    public void VersionTwoMigratesWithNoSelectedLocation()
    {
        using var context = new TestContext();

        context.Persistence.Deserialize(
            CreateVersionTwoReader(
                EventLifecycleState.Active,
                StartUtc,
                StartUtc.AddMinutes(5),
                null,
                (Serial)42u
            )
        );

        Assert.Null(Assert.Single(context.Events.EnumerateInstances()).SelectedLocationId);
    }

    [Fact]
    public void VersionThreeRestartPreservesSelectedLocationWithoutReroll()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        var survivor = (Serial)42u;
        context.Encounter.Existing.Add(survivor);
        Assert.True(context.WorldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);
        context.Persistence.Deserialize(
            CreateVersionThreeReader(
                EventLifecycleState.Active,
                StartUtc,
                StartUtc.AddMinutes(5),
                KnownEncounterLocations.BritainGraveyardEast,
                survivor
            )
        );

        context.Persistence.PostDeserialize();

        Assert.Equal(
            KnownEncounterLocations.BritainGraveyardEast,
            Assert.Single(context.Events.EnumerateInstances()).SelectedLocationId
        );
        Assert.Null(context.Encounter.SelectedLocation);
    }

    [Fact]
    public void UnknownVersionThreeLocationIsRetainedWithoutReplacement()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        var survivor = (Serial)42u;
        var unknown = new EncounterLocationId("location.britain.retired");
        context.Encounter.Existing.Add(survivor);
        Assert.True(context.WorldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);
        context.Persistence.Deserialize(
            CreateVersionThreeReader(
                EventLifecycleState.Active,
                StartUtc,
                StartUtc.AddMinutes(5),
                unknown,
                survivor
            )
        );

        context.Persistence.PostDeserialize();

        var instance = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Active, instance.State);
        Assert.Equal(unknown, instance.SelectedLocationId);
        Assert.Null(context.Encounter.SelectedLocation);
    }

    [Theory]
    [InlineData(EventLifecycleState.Succeeded)]
    [InlineData(EventLifecycleState.Failed)]
    public void VersionZeroTerminalUsesMigrationInstantAsSyntheticCompletion(EventLifecycleState state)
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();

        context.Persistence.Deserialize(CreateVersionZeroReader(state));

        var instance = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(StartUtc.AddMinutes(-5), instance.StartedUtc);
        Assert.Equal(StartUtc, instance.ExpiresUtc);
        Assert.Equal(StartUtc, instance.CompletedUtc);
        Assert.Empty(instance.OwnedMobiles);
    }

    [Fact]
    public void RecoveryKeepsActiveEventWhenOneOwnedMobileSurvives()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        var missing = (Serial)41u;
        var survivor = (Serial)42u;
        context.Encounter.Existing.Add(survivor);
        Assert.True(context.WorldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);
        context.Persistence.Deserialize(
            CreateVersionTwoReader(EventLifecycleState.Active, StartUtc, StartUtc.AddMinutes(5), null, missing, survivor)
        );

        context.Persistence.PostDeserialize();

        var active = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Active, active.State);
        Assert.Equal(new[] { survivor }, active.OwnedMobiles);
        AssertWorldState(context.WorldStates, WorldCondition.Threatened);
    }

    [Fact]
    public void RecoveryCompletesActiveEventWhenAllOwnedMobilesAreMissing()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        Assert.True(context.WorldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);
        context.Persistence.Deserialize(
            CreateVersionTwoReader(
                EventLifecycleState.Active,
                StartUtc,
                StartUtc.AddMinutes(5),
                null,
                (Serial)41u,
                (Serial)42u
            )
        );

        context.Persistence.PostDeserialize();

        var completed = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Succeeded, completed.State);
        Assert.Equal(StartUtc, completed.CompletedUtc);
        AssertWorldState(context.WorldStates, WorldCondition.Normal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(AndraxiaEventPersistence.MaxOwnedMobileCount + 1)]
    public void InvalidOwnedMobileCountIsRejected(int count)
    {
        using var context = new TestContext();
        var reader = CreateReader(
            writer =>
            {
                WriteHeader(writer, 2, 1);
                WriteVersionZeroEntry(
                    writer,
                    FirstId.ToString(),
                    KnownEvents.BritainDisturbance.Value,
                    KnownEvents.Britain.Value,
                    "active"
                );
                writer.Write(StartUtc);
                writer.Write(StartUtc.AddMinutes(5));
                writer.Write(false);
                writer.WriteEncodedInt(count);
            }
        );

        Assert.Throws<InvalidDataException>(() => context.Persistence.Deserialize(reader));
    }

    [Fact]
    public void OverdueVersionTwoEventFailsDuringRecoveryAndCleansSurvivor()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        Assert.True(context.WorldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);
        var survivor = (Serial)42u;
        context.Encounter.Existing.Add(survivor);
        context.Persistence.Deserialize(
            CreateVersionTwoReader(
                EventLifecycleState.Active,
                StartUtc.AddMinutes(-10),
                StartUtc.AddMinutes(-5),
                null,
                survivor
            )
        );

        context.Persistence.PostDeserialize();

        var instance = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Failed, instance.State);
        Assert.Equal(StartUtc, instance.CompletedUtc);
        AssertWorldState(context.WorldStates, WorldCondition.Normal);
        Assert.Contains(survivor, context.Encounter.Deleted);
        Assert.False(context.Service.Scheduler.TimerRunning);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void MalformedVersionOneTimestampInvariantIsRejected(bool expiresBeforeStart, bool activeHasCompletion)
    {
        using var context = new TestContext();
        var expires = expiresBeforeStart ? StartUtc.AddMinutes(-1) : StartUtc.AddMinutes(5);
        DateTime? completed = activeHasCompletion ? StartUtc.AddMinutes(1) : null;

        Assert.Throws<InvalidDataException>(
            () => context.Persistence.Deserialize(
                CreateVersionOneReader(EventLifecycleState.Active, StartUtc, expires, completed)
            )
        );
    }

    [Fact]
    public void ActiveAndNormalReconciliationRestoresThreatenedAndArmsTimer()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        var survivor = (Serial)42u;
        context.Encounter.Existing.Add(survivor);
        context.Persistence.Deserialize(
            CreateVersionTwoReader(EventLifecycleState.Active, StartUtc, StartUtc.AddMinutes(5), null, survivor)
        );

        context.Persistence.PostDeserialize();

        AssertWorldState(context.WorldStates, WorldCondition.Threatened);
        Assert.True(context.Service.Scheduler.TimerRunning);
        Assert.Equal(StartUtc.AddMinutes(5), context.Service.Scheduler.NextExpirationUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(EventLifecycleState.Succeeded)]
    [InlineData(EventLifecycleState.Failed)]
    public void NoActiveEventDoesNotNormalizeThreatened(EventLifecycleState? terminalState)
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext();
        Assert.True(context.WorldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);

        if (terminalState is { } state)
        {
            context.Persistence.Deserialize(
                CreateVersionOneReader(state, StartUtc.AddMinutes(-5), StartUtc, StartUtc)
            );
        }

        context.Persistence.PostDeserialize();

        AssertWorldState(context.WorldStates, WorldCondition.Threatened);
        Assert.False(context.Service.Scheduler.TimerRunning);
    }

    private static EventStore CreateEventStore(EventLifecycleState state)
    {
        var store = new EventStore(KnownEvents.Definitions);
        Assert.True(store.Trigger(KnownEvents.BritainDisturbance, FirstId, StartUtc).Succeeded);

        if (state == EventLifecycleState.Succeeded)
        {
            Assert.True(store.Complete(FirstId, StartUtc.AddMinutes(1)).Succeeded);
        }
        else if (state == EventLifecycleState.Failed)
        {
            Assert.True(store.Fail(FirstId, StartUtc.AddMinutes(1)).Succeeded);
        }

        return store;
    }

    private static BufferReader CreateVersionZeroReader(EventLifecycleState state) =>
        CreateReader(
            writer =>
            {
                WriteHeader(writer, 0, 1);
                WriteVersionZeroEntry(
                    writer,
                    FirstId.ToString(),
                    KnownEvents.BritainDisturbance.Value,
                    KnownEvents.Britain.Value,
                    EventLifecycleTokens.GetToken(state)
                );
            }
        );

    private static BufferReader CreateVersionOneReader(
        EventLifecycleState state,
        DateTime startedUtc,
        DateTime expiresUtc,
        DateTime? completedUtc
    ) => CreateReader(
        writer =>
        {
            WriteHeader(writer, 1, 1);
            WriteVersionZeroEntry(
                writer,
                FirstId.ToString(),
                KnownEvents.BritainDisturbance.Value,
                KnownEvents.Britain.Value,
                EventLifecycleTokens.GetToken(state)
            );
            writer.Write(startedUtc);
            writer.Write(expiresUtc);
            writer.Write(completedUtc.HasValue);
            if (completedUtc is { } completed)
            {
                writer.Write(completed);
            }
        }
    );

    private static BufferReader CreateVersionTwoReader(
        EventLifecycleState state,
        DateTime startedUtc,
        DateTime expiresUtc,
        DateTime? completedUtc,
        params Serial[] ownedMobiles
    ) => CreateReader(
        writer =>
        {
            WriteHeader(writer, 2, 1);
            WriteVersionZeroEntry(
                writer,
                FirstId.ToString(),
                KnownEvents.BritainDisturbance.Value,
                KnownEvents.Britain.Value,
                EventLifecycleTokens.GetToken(state)
            );
            writer.Write(startedUtc);
            writer.Write(expiresUtc);
            writer.Write(completedUtc.HasValue);
            if (completedUtc is { } completed)
            {
                writer.Write(completed);
            }
            writer.WriteEncodedInt(ownedMobiles.Length);
            foreach (var serial in ownedMobiles)
            {
                writer.Write(serial);
            }
        }
    );

    private static BufferReader CreateVersionThreeReader(
        EventLifecycleState state,
        DateTime startedUtc,
        DateTime expiresUtc,
        EncounterLocationId? selectedLocationId,
        params Serial[] ownedMobiles
    ) => CreateReader(
        writer =>
        {
            WriteHeader(writer, 3, 1);
            WriteVersionZeroEntry(
                writer,
                FirstId.ToString(),
                KnownEvents.BritainDisturbance.Value,
                KnownEvents.Britain.Value,
                EventLifecycleTokens.GetToken(state)
            );
            writer.Write(startedUtc);
            writer.Write(expiresUtc);
            writer.Write(false);
            writer.WriteEncodedInt(ownedMobiles.Length);
            foreach (var serial in ownedMobiles)
            {
                writer.Write(serial);
            }
            writer.Write(selectedLocationId.HasValue);
            if (selectedLocationId is { } locationId)
            {
                writer.Write(locationId.Value);
            }
        }
    );

    private static BufferReader CreateReader(Action<IGenericWriter> write)
    {
        var writer = new BufferWriter(new byte[256], true);
        write(writer);
        return new BufferReader(writer.Buffer);
    }

    private static string WritePayload(string directory, Action<IGenericWriter> write)
    {
        var persistenceDirectory = Path.Combine(directory, AndraxiaEventPersistence.PersistenceName);
        Directory.CreateDirectory(persistenceDirectory);
        var path = Path.Combine(persistenceDirectory, $"{AndraxiaEventPersistence.PersistenceName}.bin");
        using var writer = new FileBufferWriter(path);
        write(writer);
        return path;
    }

    private static void WriteHeader(IGenericWriter writer, int version, int count)
    {
        writer.WriteEncodedInt(version);
        writer.WriteEncodedInt(count);
    }

    private static void WriteVersionZeroEntry(
        IGenericWriter writer,
        string instanceId,
        string definitionId,
        string targetId,
        string lifecycle
    )
    {
        writer.Write(instanceId);
        writer.Write(definitionId);
        writer.Write(targetId);
        writer.Write(lifecycle);
    }

    private static void AssertWorldState(WorldStateStore store, WorldCondition expected)
    {
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
        Assert.Equal(expected, condition);
    }

    private static void WithTemporaryDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"andraxia-events-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            test(directory);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(EventStore events = null)
        {
            Events = events ?? new EventStore(KnownEvents.Definitions);
            WorldStates = new WorldStateStore(KnownWorldStates.Definitions);
            Encounter = new TestEventEncounterSpawner();
            Service = new AndraxiaEventService(Events, WorldStates, Encounter);
            Persistence = new AndraxiaEventPersistence(Events, WorldStates, Service);
        }

        public EventStore Events { get; }
        public WorldStateStore WorldStates { get; }
        public AndraxiaEventService Service { get; }
        public TestEventEncounterSpawner Encounter { get; }
        public AndraxiaEventPersistence Persistence { get; }

        public void Dispose()
        {
            Service.StopExpirationTimer();
            Persistence.Unregister();
        }
    }
}
