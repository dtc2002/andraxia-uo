using System;
using System.IO;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public class AndraxiaEventPersistenceTests
{
    private static readonly EventInstanceId FirstId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Theory]
    [InlineData(EventLifecycleState.Active)]
    [InlineData(EventLifecycleState.Succeeded)]
    [InlineData(EventLifecycleState.Failed)]
    public void VersionZeroRoundTripsEventLifecycleState(EventLifecycleState state)
    {
        WithTemporaryDirectory(
            directory =>
            {
                var sourceStore = CreateEventStore(state);
                var source = CreatePersistence(sourceStore, CreateWorldStateStore());
                var loadedStore = new EventStore(KnownEvents.Definitions);
                var loaded = CreatePersistence(loadedStore, CreateWorldStateStore());

                try
                {
                    WritePayload(directory, source.Serialize);
                    loaded.Deserialize(directory, null);
                    loaded.PostDeserialize();

                    Assert.True(loadedStore.TryGetInstance(FirstId, out var instance));
                    Assert.Equal(KnownEvents.BritainDisturbance, instance.DefinitionId);
                    Assert.Equal(KnownEvents.Britain, instance.TargetId);
                    Assert.Equal(state, instance.State);
                }
                finally
                {
                    source.Unregister();
                    loaded.Unregister();
                }
            }
        );
    }

    [Fact]
    public void MissingPersistenceFileYieldsEmptyStore()
    {
        WithTemporaryDirectory(
            directory =>
            {
                var store = CreateEventStore(EventLifecycleState.Active);
                var persistence = CreatePersistence(store, CreateWorldStateStore());

                try
                {
                    persistence.Deserialize(directory, null);
                    persistence.PostDeserialize();

                    Assert.Empty(store.EnumerateInstances());
                }
                finally
                {
                    persistence.Unregister();
                }
            }
        );
    }

    [Fact]
    public void UnknownDefinitionIsIgnored()
    {
        AssertPayloadLoadsNoEvents(
            writer => WriteEntry(
                writer,
                FirstId.ToString(),
                "event.unknown",
                KnownEvents.Britain.Value,
                "active"
            )
        );
    }

    [Fact]
    public void UnknownLifecycleTokenIsIgnored()
    {
        AssertPayloadLoadsNoEvents(
            writer => WriteEntry(
                writer,
                FirstId.ToString(),
                KnownEvents.BritainDisturbance.Value,
                KnownEvents.Britain.Value,
                "future-state"
            )
        );
    }

    [Fact]
    public void MalformedInstanceIdentifierIsIgnored()
    {
        AssertPayloadLoadsNoEvents(
            writer => WriteEntry(
                writer,
                "not-a-guid",
                KnownEvents.BritainDisturbance.Value,
                KnownEvents.Britain.Value,
                "active"
            )
        );
    }

    [Fact]
    public void DuplicateInstanceIdentifierUsesFirstEntry()
    {
        WithTemporaryDirectory(
            directory =>
            {
                WritePayload(
                    directory,
                    writer =>
                    {
                        WriteHeader(writer, 2);
                        WriteEntry(
                            writer,
                            FirstId.ToString(),
                            KnownEvents.BritainDisturbance.Value,
                            KnownEvents.Britain.Value,
                            "succeeded"
                        );
                        WriteEntry(
                            writer,
                            FirstId.ToString(),
                            KnownEvents.BritainDisturbance.Value,
                            KnownEvents.Britain.Value,
                            "failed"
                        );
                    }
                );
                var store = new EventStore(KnownEvents.Definitions);
                var persistence = CreatePersistence(store, CreateWorldStateStore());

                try
                {
                    persistence.Deserialize(directory, null);
                    persistence.PostDeserialize();

                    var instance = Assert.Single(store.EnumerateInstances());
                    Assert.Equal(EventLifecycleState.Succeeded, instance.State);
                }
                finally
                {
                    persistence.Unregister();
                }
            }
        );
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(AndraxiaEventPersistence.MaxEntryCount + 1)]
    public void InvalidEntryCountIsRejected(int count)
    {
        AssertPayloadRejected(
            writer =>
            {
                writer.WriteEncodedInt(AndraxiaEventPersistence.CurrentVersion);
                writer.WriteEncodedInt(count);
            },
            "Invalid AndraxiaEvents entry count"
        );
    }

    [Fact]
    public void UnsupportedFutureVersionIsRejectedWithoutRewritingFile()
    {
        WithTemporaryDirectory(
            directory =>
            {
                var path = WritePayload(
                    directory,
                    writer =>
                    {
                        writer.WriteEncodedInt(AndraxiaEventPersistence.CurrentVersion + 1);
                        writer.WriteEncodedInt(0);
                    }
                );
                var original = File.ReadAllBytes(path);
                var persistence = CreatePersistence(new EventStore(KnownEvents.Definitions), CreateWorldStateStore());

                try
                {
                    var exception = Assert.Throws<InvalidDataException>(
                        () => persistence.Deserialize(new BufferReader(original))
                    );

                    Assert.Contains("Unsupported AndraxiaEvents format version", exception.Message);
                    Assert.Equal(original, File.ReadAllBytes(path));
                }
                finally
                {
                    persistence.Unregister();
                }
            }
        );
    }

    [Fact]
    public void ActiveAndThreatenedReconciliationIsNoOp()
    {
        var events = CreateEventStore(EventLifecycleState.Active);
        var worldStates = CreateWorldStateStore();
        Assert.True(worldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);
        var persistence = CreatePersistence(events, worldStates);

        try
        {
            persistence.ReconcileWorldState();

            AssertWorldState(worldStates, WorldCondition.Threatened);
            Assert.Equal(EventLifecycleState.Active, Assert.Single(events.EnumerateInstances()).State);
        }
        finally
        {
            persistence.Unregister();
        }
    }

    [Fact]
    public void ActiveAndNormalReconciliationRestoresThreatened()
    {
        var events = CreateEventStore(EventLifecycleState.Active);
        var worldStates = CreateWorldStateStore();
        var persistence = CreatePersistence(events, worldStates);

        try
        {
            persistence.ReconcileWorldState();

            AssertWorldState(worldStates, WorldCondition.Threatened);
            Assert.Equal(EventLifecycleState.Active, Assert.Single(events.EnumerateInstances()).State);
        }
        finally
        {
            persistence.Unregister();
        }
    }

    [Fact]
    public void MissingActiveAndThreatenedDoesNotNormalize()
    {
        var events = new EventStore(KnownEvents.Definitions);
        var worldStates = CreateWorldStateStore();
        Assert.True(worldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);
        var persistence = CreatePersistence(events, worldStates);

        try
        {
            persistence.ReconcileWorldState();

            Assert.Empty(events.EnumerateInstances());
            AssertWorldState(worldStates, WorldCondition.Threatened);
        }
        finally
        {
            persistence.Unregister();
        }
    }

    [Theory]
    [InlineData(EventLifecycleState.Succeeded)]
    [InlineData(EventLifecycleState.Failed)]
    public void TerminalAndThreatenedDoesNotNormalize(EventLifecycleState state)
    {
        var events = CreateEventStore(state);
        var worldStates = CreateWorldStateStore();
        Assert.True(worldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);
        var persistence = CreatePersistence(events, worldStates);

        try
        {
            persistence.ReconcileWorldState();

            Assert.Equal(state, Assert.Single(events.EnumerateInstances()).State);
            AssertWorldState(worldStates, WorldCondition.Threatened);
        }
        finally
        {
            persistence.Unregister();
        }
    }

    private static void AssertPayloadLoadsNoEvents(Action<IGenericWriter> writeEntry)
    {
        WithTemporaryDirectory(
            directory =>
            {
                WritePayload(
                    directory,
                    writer =>
                    {
                        WriteHeader(writer, 1);
                        writeEntry(writer);
                    }
                );
                var store = new EventStore(KnownEvents.Definitions);
                var persistence = CreatePersistence(store, CreateWorldStateStore());

                try
                {
                    persistence.Deserialize(directory, null);
                    persistence.PostDeserialize();
                    Assert.Empty(store.EnumerateInstances());
                }
                finally
                {
                    persistence.Unregister();
                }
            }
        );
    }

    private static void AssertPayloadRejected(Action<IGenericWriter> write, string expectedMessage)
    {
        WithTemporaryDirectory(
            directory =>
            {
                var path = WritePayload(directory, write);
                var persistence = CreatePersistence(new EventStore(KnownEvents.Definitions), CreateWorldStateStore());

                try
                {
                    var exception = Assert.Throws<InvalidDataException>(
                        () => persistence.Deserialize(new BufferReader(File.ReadAllBytes(path)))
                    );
                    Assert.Contains(expectedMessage, exception.Message);
                }
                finally
                {
                    persistence.Unregister();
                }
            }
        );
    }

    private static EventStore CreateEventStore(EventLifecycleState state)
    {
        var store = new EventStore(KnownEvents.Definitions);
        Assert.True(store.Trigger(KnownEvents.BritainDisturbance, FirstId).Succeeded);

        if (state == EventLifecycleState.Succeeded)
        {
            Assert.True(store.Complete(FirstId).Succeeded);
        }
        else if (state == EventLifecycleState.Failed)
        {
            Assert.True(store.Fail(FirstId).Succeeded);
        }

        return store;
    }

    private static AndraxiaEventPersistence CreatePersistence(EventStore events, WorldStateStore worldStates) =>
        new(events, worldStates);

    private static WorldStateStore CreateWorldStateStore() => new(KnownWorldStates.Definitions);

    private static void AssertWorldState(WorldStateStore store, WorldCondition expected)
    {
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
        Assert.Equal(expected, condition);
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

    private static void WriteHeader(IGenericWriter writer, int count)
    {
        writer.WriteEncodedInt(AndraxiaEventPersistence.CurrentVersion);
        writer.WriteEncodedInt(count);
    }

    private static void WriteEntry(
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
}
