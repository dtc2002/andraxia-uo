using System;
using System.Collections.Generic;
using ModernUO.Serialization;
using Server.Mobiles;

namespace Server.Andraxia;

internal interface IEventEncounterSpawner
{
    EventDefinitionId DefinitionId { get; }
    IReadOnlyList<EncounterLocation> Locations { get; }
    bool TrySpawn(EncounterLocation location, int encounterSize, ICollection<Serial> spawned, out string failure);
    void Delete(Serial serial);
    bool Exists(Serial serial);
}

internal sealed class BritainBrigandEncounter : IEventEncounterSpawner
{
    internal const int Size = EncounterScalingPolicy.MinimumSize;

    public EventDefinitionId DefinitionId => KnownEvents.BritainDisturbance;
    public IReadOnlyList<EncounterLocation> Locations => KnownEncounterLocations.BritainDisturbance;
    public bool TrySpawn(
        EncounterLocation location,
        int encounterSize,
        ICollection<Serial> spawned,
        out string failure
    )
    {
        try
        {
            for (var i = 0; i < encounterSize; i++)
            {
                var offset = EncounterFormation.Offsets[i];
                var brigand = new AndraxiaEncounterBrigand();
                spawned.Add(brigand.Serial);
                brigand.MoveToWorld(
                    new Point3D(location.X + offset.X, location.Y + offset.Y, location.Z + offset.Z),
                    location.Map
                );
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
        EventEncounterLifecycle.OnCreatureDefeated(this);
        base.OnDeath(corpse);
        EventEncounterLifecycle.OnCreatureRemoved(this);
    }

    public override void OnDelete()
    {
        EventEncounterLifecycle.OnCreatureRemoved(this);
        base.OnDelete();
    }
}
