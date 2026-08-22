using System;
using System.Collections.Generic;
using ModernUO.Serialization;
using Server.Mobiles;

namespace Server.Andraxia;

internal interface IEventEncounterSpawner
{
    bool TrySpawn(EncounterLocation location, ICollection<Serial> spawned, out string failure);
    void Delete(Serial serial);
    bool Exists(Serial serial);
}

internal sealed class BritainBrigandEncounter : IEventEncounterSpawner
{
    internal const int EncounterSize = 3;

    private static readonly Point3D[] formationOffsets =
    [
        new(0, 0, 0),
        new(3, 2, 0),
        new(6, 0, 0)
    ];

    public bool TrySpawn(EncounterLocation location, ICollection<Serial> spawned, out string failure)
    {
        try
        {
            foreach (var offset in formationOffsets)
            {
                var brigand = new AndraxiaEncounterBrigand();
                brigand.MoveToWorld(
                    new Point3D(location.X + offset.X, location.Y + offset.Y, location.Z + offset.Z),
                    location.Map
                );
                spawned.Add(brigand.Serial);
            }

            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    public void Delete(Serial serial)
    {
        if (World.FindMobile(serial) is AndraxiaEncounterBrigand brigand)
        {
            brigand.Delete();
        }
    }

    public bool Exists(Serial serial) => World.FindMobile(serial) is AndraxiaEncounterBrigand { Deleted: false };
}

[SerializationGenerator(0, false)]
internal partial class AndraxiaEncounterBrigand : Brigand
{
    [Constructible]
    public AndraxiaEncounterBrigand()
    {
    }

    public override void OnDeath(Items.Container corpse)
    {
        base.OnDeath(corpse);
        EventEncounterLifecycle.OnCreatureRemoved(this);
    }

    public override void OnDelete()
    {
        EventEncounterLifecycle.OnCreatureRemoved(this);
        base.OnDelete();
    }
}
