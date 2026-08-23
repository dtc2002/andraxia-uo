using System;
using System.Collections.Generic;
using ModernUO.Serialization;
using Server.Mobiles;

namespace Server.Andraxia;

internal sealed class BritainUndeadEncounter : IEventEncounterSpawner
{
    internal const int Size = 3;

    internal static IReadOnlyList<Type> MobileTypes { get; } =
        [typeof(AndraxiaEncounterSkeleton), typeof(AndraxiaEncounterSkeleton), typeof(AndraxiaEncounterZombie)];

    internal static IReadOnlyList<Type> GetMobileTypes(int encounterSize)
    {
        var skeletonCount = (encounterSize + 1) / 2;
        var types = new Type[encounterSize];
        for (var i = 0; i < types.Length; i++)
        {
            types[i] = i < skeletonCount ? typeof(AndraxiaEncounterSkeleton) : typeof(AndraxiaEncounterZombie);
        }

        return types;
    }

    public EventDefinitionId DefinitionId => KnownEvents.BritainUndeadDisturbance;
    public IReadOnlyList<EncounterLocation> Locations => KnownEncounterLocations.BritainUndeadDisturbance;
    public bool TrySpawn(
        EncounterLocation location,
        int encounterSize,
        ICollection<Serial> spawned,
        out string failure
    )
    {
        try
        {
            var mobileTypes = GetMobileTypes(encounterSize);
            for (var i = 0; i < encounterSize; i++)
            {
                Mobile undead = mobileTypes[i] == typeof(AndraxiaEncounterSkeleton)
                    ? new AndraxiaEncounterSkeleton()
                    : new AndraxiaEncounterZombie();
                spawned.Add(undead.Serial);
                var offset = EncounterFormation.Offsets[i];
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
