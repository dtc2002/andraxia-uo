using System;
using System.Collections.Generic;
using System.IO;

namespace Server.Andraxia;

public sealed class RegionalPressurePersistence : GenericPersistence
{
    internal const string PersistenceName = "AndraxiaRegionalPressure";
    internal const int CurrentVersion = 1;
    private readonly RegionalPressureStore _store;
    private readonly RegionalPressureStabilizer _stabilizer;

    public RegionalPressurePersistence(RegionalPressureStore store) :
        this(store, new RegionalPressureStabilizer(store))
    {
    }

    internal RegionalPressurePersistence(RegionalPressureStore store, RegionalPressureStabilizer stabilizer) :
        base(PersistenceName, 10)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _stabilizer = stabilizer ?? throw new ArgumentNullException(nameof(stabilizer));
        _stabilizer.Initialize(Core.Now.Kind == DateTimeKind.Utc ? Core.Now : DateTime.UtcNow);
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(CurrentVersion);
        writer.WriteEncodedInt(_store.Britain);
        writer.Write(_stabilizer.NextRecoveryUtc);
    }

    public override void Deserialize(string savePath, Dictionary<ulong, string> typesDb)
    {
        _store.Reset();
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
        if (version == 1)
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
    }

    public override void PostDeserialize() => _stabilizer.Recover(Core.Now);
}
