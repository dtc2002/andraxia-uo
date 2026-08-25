using System;
using System.Collections.Generic;
using System.IO;

namespace Server.Andraxia;

public sealed class RegionalPressurePersistence : GenericPersistence
{
    internal const string PersistenceName = "AndraxiaRegionalPressure";
    internal const int CurrentVersion = 2;
    private readonly RegionalPressureStore _store;
    private readonly RegionalPressureStabilizer _stabilizer;
    private readonly RegionalConcernStore _concern;

    public RegionalPressurePersistence(RegionalPressureStore store) :
        this(store, new RegionalPressureStabilizer(store), new RegionalConcernStore())
    {
    }

    internal RegionalPressurePersistence(RegionalPressureStore store, RegionalPressureStabilizer stabilizer, RegionalConcernStore concern = null) :
        base(PersistenceName, 10)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _stabilizer = stabilizer ?? throw new ArgumentNullException(nameof(stabilizer));
        _concern = concern ?? new RegionalConcernStore();
        _stabilizer.Initialize(Core.Now.Kind == DateTimeKind.Utc ? Core.Now : DateTime.UtcNow);
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(CurrentVersion);
        writer.WriteEncodedInt(_store.Britain);
        writer.Write(_stabilizer.NextRecoveryUtc);
        writer.Write(RegionalConcernStore.Token(_concern.Britain));
        writer.WriteEncodedInt(_concern.QuietIntervals);
    }

    public override void Deserialize(string savePath, Dictionary<ulong, string> typesDb)
    {
        _store.Reset();
        _concern.Restore(RegionalConcern.None, 0);
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
        var pressure = reader.ReadEncodedInt();
        if (pressure is < 0 or > RegionalPressureStore.MaximumPressure)
        {
            throw new InvalidDataException($"Invalid Britain pressure {pressure}.");
        }
        _store.SetBritain(pressure);
        if (version >= 1)
        {
            var nextRecoveryUtc = reader.ReadDateTime();
            if (nextRecoveryUtc.Kind != DateTimeKind.Utc)
            {
                throw new InvalidDataException("Persisted pressure recovery time must be UTC.");
            }
            _stabilizer.Restore(nextRecoveryUtc);
        }
        else
        {
            var nowUtc = Core.Now.Kind == DateTimeKind.Utc ? Core.Now : DateTime.UtcNow;
            _stabilizer.Initialize(nowUtc);
        }
        if (version >= 2)
        {
            if (!RegionalConcernStore.TryParse(reader.ReadString(), out var concern)) throw new InvalidDataException("Unknown regional concern token.");
            var quiet = reader.ReadEncodedInt();
            if (quiet is < 0 or > 3) throw new InvalidDataException("Invalid concern quiet interval count.");
            _concern.Restore(concern, quiet);
        }
    }

    public override void PostDeserialize() => _stabilizer.Recover(Core.Now);
}
