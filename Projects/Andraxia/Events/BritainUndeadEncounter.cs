using System;
using System.Collections.Generic;
using ModernUO.Serialization;
using Server.Mobiles;

namespace Server.Andraxia;

internal sealed class BritainUndeadEncounter : IEventEncounterSpawner
{
    internal const int Size = 3;

    internal static IReadOnlyList<Type> MobileTypes { get; } = EncounterCompositionPolicy.Undead(3, EncounterSeverity.Normal);

    internal static IReadOnlyList<Type> GetMobileTypes(int encounterSize) =>
        EncounterCompositionPolicy.Undead(encounterSize, EncounterSeverity.Normal);

    public EventDefinitionId DefinitionId => KnownEvents.BritainUndeadDisturbance;
    public IReadOnlyList<EncounterLocation> Locations => KnownEncounterLocations.BritainUndeadDisturbance;
    public bool TrySpawn(
        EncounterLocation location,
        int encounterSize,
        EncounterSeverity severity,
        ICollection<Serial> spawned,
        ICollection<Serial> protectedMobiles,
        ICollection<Serial> alliedMobiles,
        out string failure
    )
    {
        try
        {
            var mobileTypes = EncounterCompositionPolicy.Undead(encounterSize, severity);
            for (var i = 0; i < encounterSize; i++)
            {
                Mobile undead = mobileTypes[i] == typeof(AndraxiaEncounterSkeleton)
                    ? new AndraxiaEncounterSkeleton()
                    : mobileTypes[i] == typeof(AndraxiaEncounterZombie)
                        ? new AndraxiaEncounterZombie()
                        : mobileTypes[i] == typeof(AndraxiaEncounterGhoul)
                            ? new AndraxiaEncounterGhoul()
                            : new AndraxiaEncounterWraith();
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
        if (undead is AndraxiaEncounterSkeleton or AndraxiaEncounterZombie or
            AndraxiaEncounterGhoul or AndraxiaEncounterWraith && !undead.Deleted)
        {
            undead.Delete();
        }
    }

    public bool Exists(Serial serial) =>
        World.FindMobile(serial) is (AndraxiaEncounterSkeleton or AndraxiaEncounterZombie or
            AndraxiaEncounterGhoul or AndraxiaEncounterWraith) and { Deleted: false };
}

[SerializationGenerator(0, false)]
internal partial class AndraxiaEncounterGhoul : Ghoul
{
    [Constructible]
    public AndraxiaEncounterGhoul()
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

[SerializationGenerator(0, false)]
internal partial class AndraxiaEncounterWraith : Wraith
{
    [Constructible]
    public AndraxiaEncounterWraith()
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

[SerializationGenerator(0, false)]
internal partial class AndraxiaEncounterSkeleton : Skeleton
{
    [Constructible]
    public AndraxiaEncounterSkeleton()
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

[SerializationGenerator(0, false)]
internal partial class AndraxiaEncounterZombie : Zombie
{
    [Constructible]
    public AndraxiaEncounterZombie()
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
