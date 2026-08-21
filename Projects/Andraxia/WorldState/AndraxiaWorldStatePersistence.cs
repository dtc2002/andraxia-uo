using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Server.Logging;

namespace Server.Andraxia;

public sealed class AndraxiaWorldStatePersistence : GenericPersistence
{
    internal const int CurrentVersion = 0;
    internal const string PersistenceName = "AndraxiaWorldState";
    private const int MaxEntryCount = 10_000;

    private static readonly ILogger logger = LogFactory.GetLogger(typeof(AndraxiaWorldStatePersistence));
    private readonly WorldStateStore _store;

    public AndraxiaWorldStatePersistence(WorldStateStore store) : base(PersistenceName, 10) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(CurrentVersion);

        var entries = _store.EnumerateStates().OrderBy(static entry => entry.Key.Value, StringComparer.Ordinal).ToArray();
        writer.WriteEncodedInt(entries.Length);

        foreach (var (id, condition) in entries)
        {
            writer.Write(id.Value);
            writer.Write(WorldConditionTokens.GetToken(condition));
        }
    }

    public override void Deserialize(IGenericReader reader)
    {
        _store.ResetAll();

        var version = reader.ReadEncodedInt();
        if (version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported {PersistenceName} format version {version}; expected {CurrentVersion}."
            );
        }

        var count = reader.ReadEncodedInt();
        if (count is < 0 or > MaxEntryCount)
        {
            throw new InvalidDataException($"Invalid {PersistenceName} entry count {count}.");
        }

        var seen = new HashSet<WorldStateId>();
        for (var i = 0; i < count; i++)
        {
            var idToken = reader.ReadString();
            var conditionToken = reader.ReadString();

            if (string.IsNullOrWhiteSpace(idToken))
            {
                logger.Warning("Ignoring persisted world state with an empty identifier");
                continue;
            }

            var id = new WorldStateId(idToken);
            if (!seen.Add(id))
            {
                logger.Warning("Ignoring duplicate persisted world-state identifier {Identifier}; first entry wins", id);
                continue;
            }

            if (!_store.TryGetState(id, out _))
            {
                logger.Warning("Ignoring unknown persisted world-state identifier {Identifier}", id);
                continue;
            }

            if (!WorldConditionTokens.TryParse(conditionToken, out var condition))
            {
                logger.Warning(
                    "Ignoring unknown condition token {Condition} for world state {Identifier}; retaining its default",
                    conditionToken,
                    id
                );
                _store.Reset(id);
                continue;
            }

            _store.Restore(id, condition);
        }
    }

    public override void PostDeserialize() => _store.EnsureDefaults();
}
