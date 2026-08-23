using System;
using System.Collections.Generic;
using System.IO;

namespace Server.Andraxia;

public sealed class RegionalPressurePersistence : GenericPersistence
{
    internal const string PersistenceName = "AndraxiaRegionalPressure";
    internal const int CurrentVersion = 0;
    private readonly RegionalPressureStore _store;

    public RegionalPressurePersistence(RegionalPressureStore store) : base(PersistenceName, 10) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(CurrentVersion);
        writer.WriteEncodedInt(_store.Britain);
    }

    public override void Deserialize(string savePath, Dictionary<ulong, string> typesDb)
    {
        _store.Reset();
        base.Deserialize(savePath, typesDb);
    }

    public override void Deserialize(IGenericReader reader)
    {
        var version = reader.ReadEncodedInt();
        if (version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported {PersistenceName} format version {version}.");
        }
        var pressure = reader.ReadEncodedInt();
        if (pressure is < 0 or > RegionalPressureStore.MaximumPressure)
        {
            throw new InvalidDataException($"Invalid Britain pressure {pressure}.");
        }
        _store.SetBritain(pressure);
    }
}
