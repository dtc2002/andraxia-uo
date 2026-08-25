using System;
using System.Collections.Generic;
using System.IO;
using Server.Logging;

namespace Server.Andraxia;

public sealed class RegionalPressurePersistence : GenericPersistence
{
    internal const string PersistenceName = "AndraxiaRegionalPressure";
    internal const int CurrentVersion = 3;
    internal const int MaximumRegionCount = 64;
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(RegionalPressurePersistence));
    private readonly RegionalPressureStore _pressure;
    private readonly RegionalPressureStabilizer _stabilizer;
    private readonly RegionalConcernStore _concern;

    public RegionalPressurePersistence(RegionalPressureStore store) :
        this(store, new RegionalPressureStabilizer(store), new RegionalConcernStore(store.States))
    {
    }

    internal RegionalPressurePersistence(
        RegionalPressureStore store,
        RegionalPressureStabilizer stabilizer,
        RegionalConcernStore concern = null
    ) : base(PersistenceName, 10)
    {
        _pressure = store ?? throw new ArgumentNullException(nameof(store));
        _stabilizer = stabilizer ?? throw new ArgumentNullException(nameof(stabilizer));
        _concern = concern ?? new RegionalConcernStore(store.States);
        _stabilizer.Initialize(Core.Now.Kind == DateTimeKind.Utc ? Core.Now : DateTime.UtcNow);
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(CurrentVersion);
        var states = _pressure.States.Enumerate();
        writer.WriteEncodedInt(states.Count);
        foreach (var state in states)
        {
            writer.Write(state.Definition.Id.Value);
            writer.WriteEncodedInt(state.Pressure);
            writer.Write(RegionalConcernStore.Token(_concern.Get(state.Definition.Id)));
            writer.WriteEncodedInt(_concern.GetQuietIntervals(state.Definition.Id));
        }
        writer.Write(_stabilizer.NextRecoveryUtc);
    }

    public override void Deserialize(string savePath, Dictionary<ulong, string> typesDb)
    {
        _pressure.Reset();
        if (!ReferenceEquals(_pressure.States, _concern.States))
        {
            _concern.States.Reset();
        }
        var nowUtc = Core.Now.Kind == DateTimeKind.Utc ? Core.Now : DateTime.UtcNow;
        _stabilizer.Initialize(nowUtc);
        base.Deserialize(savePath, typesDb);
    }

    public override void Deserialize(IGenericReader reader)
    {
        var version = reader.ReadEncodedInt();
        if (version is < 0 or > CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported {PersistenceName} format version {version}.");
        }

        if (version < 3)
        {
            DeserializeLegacy(reader, version);
            return;
        }

        var count = reader.ReadEncodedInt();
        if (count is < 0 or > MaximumRegionCount)
        {
            throw new InvalidDataException($"Invalid persisted regional-state count {count}.");
        }

        HashSet<AndraxiaRegionId> restored = [];
        for (var i = 0; i < count; i++)
        {
            var idToken = reader.ReadString();
            var pressure = reader.ReadEncodedInt();
            var concernToken = reader.ReadString();
            var quiet = reader.ReadEncodedInt();
            if (string.IsNullOrWhiteSpace(idToken))
            {
                logger.Warning("Ignoring persisted regional state with an empty identifier");
                continue;
            }
            var id = new AndraxiaRegionId(idToken);
            if (!_pressure.States.TryGet(id, out _))
            {
                logger.Warning("Ignoring persisted state for unknown Andraxia region {Region}", id);
                continue;
            }
            if (!restored.Add(id))
            {
                logger.Warning("Ignoring duplicate persisted state for Andraxia region {Region}", id);
                continue;
            }
            if (pressure is < 0 or > RegionalPressureStore.MaximumPressure ||
                !RegionalConcernStore.TryParse(concernToken, out var concern) || quiet is < 0 or > 3)
            {
                logger.Warning("Ignoring malformed persisted state for Andraxia region {Region}", id);
                continue;
            }

            _pressure.Set(id, pressure);
            _concern.Restore(id, concern, quiet);
        }

        RestoreSchedule(reader.ReadDateTime());
    }

    public override void PostDeserialize() => _stabilizer.Recover(Core.Now);

    private void DeserializeLegacy(IGenericReader reader, int version)
    {
        var pressure = reader.ReadEncodedInt();
        if (pressure is < 0 or > RegionalPressureStore.MaximumPressure)
        {
            throw new InvalidDataException($"Invalid Britain pressure {pressure}.");
        }
        _pressure.Set(KnownAndraxiaRegions.Britain, pressure);

        if (version >= 1)
        {
            RestoreSchedule(reader.ReadDateTime());
        }
        else
        {
            _stabilizer.Initialize(Core.Now.Kind == DateTimeKind.Utc ? Core.Now : DateTime.UtcNow);
        }

        if (version >= 2)
        {
            if (!RegionalConcernStore.TryParse(reader.ReadString(), out var concern))
            {
                throw new InvalidDataException("Unknown regional concern token.");
            }
            var quiet = reader.ReadEncodedInt();
            if (quiet is < 0 or > 3)
            {
                throw new InvalidDataException("Invalid concern quiet interval count.");
            }
            _concern.Restore(KnownAndraxiaRegions.Britain, concern, quiet);
        }
    }

    private void RestoreSchedule(DateTime nextRecoveryUtc)
    {
        if (nextRecoveryUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidDataException("Persisted pressure recovery time must be UTC.");
        }
        _stabilizer.Restore(nextRecoveryUtc);
    }
}
