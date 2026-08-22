using System;
using System.Collections.Generic;
using ModernUO.Serialization;
using Server.Mobiles;

namespace Server.Andraxia;

internal interface IEventEncounterSpawner
{
    bool TrySpawn(ICollection<Serial> spawned, out string failure);
    void Delete(Serial serial);
    bool Exists(Serial serial);
}

internal sealed class BritainBrigandEncounter : IEventEncounterSpawner
{
    internal const int EncounterSize = 3;

    private static readonly Point3D[] locations =
    [
        new(1402, 1510, 10),
        new(1405, 1512, 10),
        new(1408, 1510, 10)
    ];

    public bool TrySpawn(ICollection<Serial> spawned, out string failure)
    {
        try
        {
            foreach (var location in locations)
            {
                var brigand = new AndraxiaEncounterBrigand();
                brigand.MoveToWorld(location, Map.Trammel);
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
