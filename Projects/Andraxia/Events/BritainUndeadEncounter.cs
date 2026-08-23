using System;
using System.Collections.Generic;
using ModernUO.Serialization;
using Server.Mobiles;

namespace Server.Andraxia;

internal sealed class BritainUndeadEncounter : IEventEncounterSpawner
{
    internal const int Size = 3;

    private static readonly Point3D[] formationOffsets =
    [
        new(0, 0, 0),
        new(3, 2, 0),
        new(6, 0, 0)
    ];

    internal static IReadOnlyList<Type> MobileTypes { get; } =
        [typeof(AndraxiaEncounterSkeleton), typeof(AndraxiaEncounterSkeleton), typeof(AndraxiaEncounterZombie)];

    public EventDefinitionId DefinitionId => KnownEvents.BritainUndeadDisturbance;
    public IReadOnlyList<EncounterLocation> Locations => KnownEncounterLocations.BritainUndeadDisturbance;
    public int EncounterSize => Size;

    public bool TrySpawn(EncounterLocation location, ICollection<Serial> spawned, out string failure)
    {
        try
        {
            for (var i = 0; i < formationOffsets.Length; i++)
            {
                Mobile undead = i < 2 ? new AndraxiaEncounterSkeleton() : new AndraxiaEncounterZombie();
                spawned.Add(undead.Serial);
                var offset = formationOffsets[i];
                undead.MoveToWorld(
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
        var undead = World.FindMobile(serial);
        if (undead is AndraxiaEncounterSkeleton or AndraxiaEncounterZombie && !undead.Deleted)
        {
            undead.Delete();
        }
    }

    public bool Exists(Serial serial) =>
        World.FindMobile(serial) is (AndraxiaEncounterSkeleton or AndraxiaEncounterZombie) and { Deleted: false };
}

[SerializationGenerator(0, false)]
internal partial class AndraxiaEncounterSkeleton : Skeleton
{
    [Constructible]
    public AndraxiaEncounterSkeleton()
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

[SerializationGenerator(0, false)]
internal partial class AndraxiaEncounterZombie : Zombie
{
    [Constructible]
    public AndraxiaEncounterZombie()
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
