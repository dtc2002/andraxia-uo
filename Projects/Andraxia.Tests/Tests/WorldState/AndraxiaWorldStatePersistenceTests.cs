using System;
using System.IO;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public class AndraxiaWorldStatePersistenceTests
{
    [Fact]
    public void VersionZeroRoundTripsThroughPersistenceFile()
    {
        WithTemporaryDirectory(
            directory =>
            {
                var sourceStore = CreateStore();
                Assert.True(sourceStore.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);

                var source = new AndraxiaWorldStatePersistence(sourceStore);
                var loadedStore = CreateStore();
                var loaded = new AndraxiaWorldStatePersistence(loadedStore);

                try
                {
                    WritePayload(directory, source.Serialize);
                    loaded.Deserialize(directory, null);
                    loaded.PostDeserialize();

                    Assert.True(loadedStore.TryGetState(KnownWorldStates.Britain, out var condition));
                    Assert.Equal(WorldCondition.Threatened, condition);
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
    public void MissingPersistenceFileRetainsDefaults()
    {
        WithTemporaryDirectory(
            directory =>
            {
                var store = CreateStore();
                var persistence = new AndraxiaWorldStatePersistence(store);

                try
                {
                    persistence.Deserialize(directory, null);
                    persistence.PostDeserialize();

                    Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
                    Assert.Equal(WorldCondition.Normal, condition);
                }
                finally
                {
                    persistence.Unregister();
                }
            }
        );
    }

    [Fact]
    public void MissingKnownEntryRetainsDefault()
    {
        AssertPayloadLoadsBritainAs(
            writer =>
            {
                writer.WriteEncodedInt(AndraxiaWorldStatePersistence.CurrentVersion);
                writer.WriteEncodedInt(0);
            },
            WorldCondition.Normal
        );
    }

    [Fact]
    public void UnknownPersistedIdentifierIsIgnored()
    {
        AssertPayloadLoadsBritainAs(
            writer =>
            {
                WriteHeader(writer, 1);
                WriteEntry(writer, "region.unknown", "threatened");
            },
            WorldCondition.Normal
        );
    }

    [Fact]
    public void UnknownConditionTokenRetainsDefault()
    {
        AssertPayloadLoadsBritainAs(
            writer =>
            {
                WriteHeader(writer, 1);
                WriteEntry(writer, KnownWorldStates.Britain.Value, "future-condition");
            },
            WorldCondition.Normal
        );
    }

    [Fact]
    public void DuplicateIdentifierUsesFirstEntry()
    {
        AssertPayloadLoadsBritainAs(
            writer =>
            {
                WriteHeader(writer, 2);
                WriteEntry(writer, KnownWorldStates.Britain.Value, "threatened");
                WriteEntry(writer, KnownWorldStates.Britain.Value, "normal");
            },
            WorldCondition.Threatened
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
                        writer.WriteEncodedInt(AndraxiaWorldStatePersistence.CurrentVersion + 1);
                        writer.WriteEncodedInt(0);
                    }
                );
                var original = File.ReadAllBytes(path);
                var store = CreateStore();
                var persistence = new AndraxiaWorldStatePersistence(store);

                try
                {
                    var reader = new BufferReader(original);

                    var exception = Assert.Throws<InvalidDataException>(() => persistence.Deserialize(reader));

                    Assert.Contains("Unsupported AndraxiaWorldState format version", exception.Message);
                    Assert.Equal(original, File.ReadAllBytes(path));
                    Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
                    Assert.Equal(WorldCondition.Normal, condition);
                }
                finally
                {
                    persistence.Unregister();
                }
            }
        );
    }

    private static void AssertPayloadLoadsBritainAs(Action<IGenericWriter> write, WorldCondition expected)
    {
        WithTemporaryDirectory(
            directory =>
            {
                WritePayload(directory, write);
                var store = CreateStore();
                var persistence = new AndraxiaWorldStatePersistence(store);

                try
                {
                    persistence.Deserialize(directory, null);
                    persistence.PostDeserialize();

                    Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
                    Assert.Equal(expected, condition);
                }
                finally
                {
                    persistence.Unregister();
                }
            }
        );
    }

    private static string WritePayload(string directory, Action<IGenericWriter> write)
    {
        var persistenceDirectory = Path.Combine(directory, AndraxiaWorldStatePersistence.PersistenceName);
        Directory.CreateDirectory(persistenceDirectory);
        var path = Path.Combine(
            persistenceDirectory,
            $"{AndraxiaWorldStatePersistence.PersistenceName}.bin"
        );

        using var writer = new FileBufferWriter(path);
        write(writer);
        return path;
    }

    private static void WriteHeader(IGenericWriter writer, int count)
    {
        writer.WriteEncodedInt(AndraxiaWorldStatePersistence.CurrentVersion);
        writer.WriteEncodedInt(count);
    }

    private static void WriteEntry(IGenericWriter writer, string id, string condition)
    {
        writer.Write(id);
        writer.Write(condition);
    }

    private static WorldStateStore CreateStore() => new(KnownWorldStates.Definitions);

    private static void WithTemporaryDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"andraxia-world-state-{Guid.NewGuid():N}");
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
