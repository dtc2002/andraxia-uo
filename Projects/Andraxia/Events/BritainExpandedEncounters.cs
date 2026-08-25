using System;
using System.Collections.Generic;
using System.Linq;
using ModernUO.Serialization;
using Server.Engines.Quests.Haven;
using Server.Mobiles;

namespace Server.Andraxia;

internal abstract class ExpandedEncounter : IEventEncounterSpawner
{
    public abstract EventDefinitionId DefinitionId { get; }
    public IReadOnlyList<EncounterLocation> Locations => KnownEncounterLocations.GetForDefinition(DefinitionId);
    protected abstract IReadOnlyList<Type> Composition(int size, EncounterSeverity severity);
    protected abstract Mobile Create(Type type);
    protected abstract bool Owns(Mobile mobile);
    protected virtual Mobile CreateProtected() => null;

    public virtual bool TrySpawn(EncounterLocation location, int encounterSize, EncounterSeverity severity,
        ICollection<Serial> spawned, ICollection<Serial> protectedMobiles, ICollection<Serial> alliedMobiles,
        out string failure)
    {
        try
        {
            var types = Composition(encounterSize, severity);
            for (var i = 0; i < types.Count; i++)
            {
                var mobile = Create(types[i]);
                spawned.Add(mobile.Serial);
                var offset = EncounterFormation.Offsets[i];
                mobile.MoveToWorld(new Point3D(location.X + offset.X, location.Y + offset.Y, location.Z + offset.Z), location.Map);
            }
            if (CreateProtected() is { } target)
            {
                spawned.Add(target.Serial);
                protectedMobiles.Add(target.Serial);
                target.MoveToWorld(new Point3D(location.X + 1, location.Y + 1, location.Z), location.Map);
            }
            failure = null;
            return true;
        }
        catch (Exception ex) { failure = ex.Message; return false; }
    }

    public void Delete(Serial serial) { if (World.FindMobile(serial) is { } mobile && Owns(mobile)) mobile.Delete(); }
    public bool Exists(Serial serial) => World.FindMobile(serial) is { Deleted: false } mobile && Owns(mobile) &&
        (mobile is not (AndraxiaCaravanMerchant or AndraxiaCaravanGuard) || mobile.Alive);
}

internal sealed class BritainOrcEncounter : ExpandedEncounter
{
    public override EventDefinitionId DefinitionId => KnownEvents.BritainOrcRaidingParty;
    protected override IReadOnlyList<Type> Composition(int size, EncounterSeverity severity) => EncounterCompositionPolicy.Orcs(size, severity);
    protected override Mobile Create(Type type) => type == typeof(AndraxiaEncounterOrc) ? new AndraxiaEncounterOrc() :
        type == typeof(AndraxiaEncounterOrcishLord) ? new AndraxiaEncounterOrcishLord() : new AndraxiaEncounterOrcishMage();
    protected override bool Owns(Mobile mobile) => mobile is AndraxiaEncounterOrc or AndraxiaEncounterOrcishLord or AndraxiaEncounterOrcishMage;
}

internal sealed class BritainBeastEncounter : ExpandedEncounter
{
    public override EventDefinitionId DefinitionId => KnownEvents.BritainBeastOutbreak;
    protected override IReadOnlyList<Type> Composition(int size, EncounterSeverity severity) => EncounterCompositionPolicy.Beasts(size, severity);
    protected override Mobile Create(Type type) => type == typeof(AndraxiaEncounterGreyWolf) ? new AndraxiaEncounterGreyWolf() :
        type == typeof(AndraxiaEncounterDireWolf) ? new AndraxiaEncounterDireWolf() : new AndraxiaEncounterGrizzlyBear();
    protected override bool Owns(Mobile mobile) => mobile is AndraxiaEncounterGreyWolf or AndraxiaEncounterDireWolf or AndraxiaEncounterGrizzlyBear;
}

internal sealed class BritainCaravanEncounter : ExpandedEncounter
{
    internal const int CaravanTeam = 1;
    internal const int AmbusherTeam = 2;
    internal static readonly Point3D[] MerchantOffsets = [new(-1, -2, 0), new(1, -2, 0)];
    internal static readonly Point3D[] GuardOffsets = [new(-1, 0, 0), new(1, 0, 0)];
    internal static readonly Point3D[] HostileOffsets =
    [
        new(-2, 2, 0), new(0, 2, 0), new(2, 2, 0), new(-3, 3, 0), new(-1, 3, 0), new(1, 3, 0),
        new(3, 3, 0)
    ];

    public override EventDefinitionId DefinitionId => KnownEvents.BritainCaravanAmbush;
    protected override IReadOnlyList<Type> Composition(int size, EncounterSeverity severity) => EncounterCompositionPolicy.Brigands(size, severity);
    protected override Mobile Create(Type type) => type == typeof(AndraxiaEncounterBrigand)
        ? new AndraxiaEncounterBrigand() : new AndraxiaEncounterEvilMage();
    protected override bool Owns(Mobile mobile) => mobile is AndraxiaEncounterBrigand or AndraxiaEncounterEvilMage or
        AndraxiaCaravanMerchant or AndraxiaCaravanGuard;

    public override bool TrySpawn(
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
            List<BaseCreature> hostiles = [];
            var composition = Composition(encounterSize, severity);
            for (var i = 0; i < composition.Count; i++)
            {
                var hostile = (BaseCreature)Create(composition[i]);
                hostile.Team = AmbusherTeam;
                hostiles.Add(hostile);
                spawned.Add(hostile.Serial);
                Move(hostile, location, HostileOffsets[i]);
            }

            for (var i = 0; i < MerchantOffsets.Length; i++)
            {
                var merchant = new AndraxiaCaravanMerchant { Team = CaravanTeam };
                spawned.Add(merchant.Serial);
                protectedMobiles.Add(merchant.Serial);
                Move(merchant, location, MerchantOffsets[i]);
            }

            List<AndraxiaCaravanGuard> guards = [];
            for (var i = 0; i < GuardOffsets.Length; i++)
            {
                var guard = new AndraxiaCaravanGuard { Team = CaravanTeam };
                guards.Add(guard);
                spawned.Add(guard.Serial);
                alliedMobiles.Add(guard.Serial);
                Move(guard, location, GuardOffsets[i]);
            }

            for (var i = 0; i < hostiles.Count; i++)
            {
                hostiles[i].Combatant = guards[i % guards.Count];
            }
            for (var i = 0; i < guards.Count; i++)
            {
                guards[i].Combatant = hostiles[i % hostiles.Count];
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

    private static void Move(Mobile mobile, EncounterLocation location, Point3D offset) => mobile.MoveToWorld(
        new Point3D(location.X + offset.X, location.Y + offset.Y, location.Z + offset.Z), location.Map
    );
}

internal static class ExpandedLifecycle
{
    internal static void Death(Mobile mobile) { EventEncounterLifecycle.OnCreatureDefeated(mobile); }
    internal static void Removed(Mobile mobile) { EventEncounterLifecycle.OnCreatureRemoved(mobile); }
}

internal static class CaravanCombatRules
{
    internal static bool AreOpponents(Mobile attacker, Mobile target) =>
        attacker != null && target != null && AreOpponents(attacker.Serial, target.Serial, AndraxiaAssembly.Events);

    internal static bool AreOpponents(Serial attacker, Serial target, EventStore events)
    {
        if (events == null)
        {
            return false;
        }

        foreach (var instance in events.EnumerateInstances())
        {
            if (instance.State != EventLifecycleState.Active ||
                instance.DefinitionId != KnownEvents.BritainCaravanAmbush)
            {
                continue;
            }

            var attackerHostile = instance.HostileMobiles.Contains(attacker);
            var targetHostile = instance.HostileMobiles.Contains(target);
            var attackerCaravan = instance.ProtectedMobiles.Contains(attacker) || instance.AlliedMobiles.Contains(attacker);
            var targetCaravan = instance.ProtectedMobiles.Contains(target) || instance.AlliedMobiles.Contains(target);
            if (attackerHostile && targetCaravan || attackerCaravan && targetHostile)
            {
                return true;
            }
        }

        return false;
    }
}

[SerializationGenerator(0, false)] internal partial class AndraxiaEncounterOrc : Orc { [Constructible] public AndraxiaEncounterOrc() {} public override void OnDeath(Items.Container c){ExpandedLifecycle.Death(this);base.OnDeath(c);ExpandedLifecycle.Removed(this);} public override void OnDelete(){ExpandedLifecycle.Removed(this);base.OnDelete();} }
[SerializationGenerator(0, false)] internal partial class AndraxiaEncounterOrcishLord : OrcishLord { [Constructible] public AndraxiaEncounterOrcishLord() {} public override void OnDeath(Items.Container c){ExpandedLifecycle.Death(this);base.OnDeath(c);ExpandedLifecycle.Removed(this);} public override void OnDelete(){ExpandedLifecycle.Removed(this);base.OnDelete();} }
[SerializationGenerator(0, false)] internal partial class AndraxiaEncounterOrcishMage : OrcishMage { [Constructible] public AndraxiaEncounterOrcishMage() {} public override void OnDeath(Items.Container c){ExpandedLifecycle.Death(this);base.OnDeath(c);ExpandedLifecycle.Removed(this);} public override void OnDelete(){ExpandedLifecycle.Removed(this);base.OnDelete();} }
[SerializationGenerator(0, false)] internal partial class AndraxiaEncounterGreyWolf : GreyWolf { [Constructible] public AndraxiaEncounterGreyWolf() {} public override void OnDeath(Items.Container c){ExpandedLifecycle.Death(this);base.OnDeath(c);ExpandedLifecycle.Removed(this);} public override void OnDelete(){ExpandedLifecycle.Removed(this);base.OnDelete();} }
[SerializationGenerator(0, false)] internal partial class AndraxiaEncounterDireWolf : DireWolf { [Constructible] public AndraxiaEncounterDireWolf() {} public override void OnDeath(Items.Container c){ExpandedLifecycle.Death(this);base.OnDeath(c);ExpandedLifecycle.Removed(this);} public override void OnDelete(){ExpandedLifecycle.Removed(this);base.OnDelete();} }
[SerializationGenerator(0, false)] internal partial class AndraxiaEncounterGrizzlyBear : GrizzlyBear { [Constructible] public AndraxiaEncounterGrizzlyBear() {} public override void OnDeath(Items.Container c){ExpandedLifecycle.Death(this);base.OnDeath(c);ExpandedLifecycle.Removed(this);} public override void OnDelete(){ExpandedLifecycle.Removed(this);base.OnDelete();} }
[SerializationGenerator(0, false)]
internal partial class AndraxiaCaravanMerchant : Merchant
{
    [Constructible]
    public AndraxiaCaravanMerchant()
    {
        FightMode = FightMode.None;
        CantWalk = true;
    }

    public override bool AcceptEscorter(Mobile mobile) => false;
    public override void OnDeath(Items.Container corpse) { base.OnDeath(corpse); ExpandedLifecycle.Removed(this); }
    public override void OnDelete() { ExpandedLifecycle.Removed(this); base.OnDelete(); }
}

[SerializationGenerator(0, false)]
internal partial class AndraxiaCaravanGuard : MilitiaFighter
{
    [Constructible]
    public AndraxiaCaravanGuard()
    {
        Title = "the caravan guard";
        FightMode = FightMode.Closest;
    }

    public override bool IsEnemy(Mobile mobile) =>
        mobile is (AndraxiaEncounterBrigand or AndraxiaEncounterEvilMage) and
            BaseCreature { Team: BritainCaravanEncounter.AmbusherTeam };
    public override bool IsHumanInTown() => false;
    public override bool IsHarmfulCriminal(Mobile target) =>
        !CaravanCombatRules.AreOpponents(this, target) && base.IsHarmfulCriminal(target);
    public override void OnMovement(Mobile mobile, Point3D oldLocation)
    {
        if (!mobile.Murderer)
        {
            base.OnMovement(mobile, oldLocation);
        }
    }
    public override void OnDeath(Items.Container corpse) { base.OnDeath(corpse); ExpandedLifecycle.Removed(this); }
    public override void OnDelete() { ExpandedLifecycle.Removed(this); base.OnDelete(); }
}
