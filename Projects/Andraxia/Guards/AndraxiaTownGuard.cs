using ModernUO.Serialization;
using Server.Items;
using Server.Mobiles;

namespace Server.Andraxia;

[SerializationGenerator(0, false)]
internal partial class AndraxiaTownGuard : BaseCreature
{
    [Constructible]
    public AndraxiaTownGuard(Mobile target = null) : base(AIType.AI_Melee, FightMode.Aggressor)
    {
        Title = "the guard";
        InitStats(100, 90, 50);
        SetHits(150);
        SetDamage(8, 12);
        SetSkill(SkillName.Anatomy, 90.0);
        SetSkill(SkillName.MagicResist, 80.0);
        SetSkill(SkillName.Parry, 90.0);
        SetSkill(SkillName.Swords, 90.0);
        SetSkill(SkillName.Tactics, 90.0);
        VirtualArmor = 35;

        Female = Utility.RandomBool();
        Body = Female ? 0x191 : 0x190;
        Name = NameList.RandomName(Female ? "female" : "male");
        Hue = Race.Human.RandomSkinHue();
        Utility.AssignRandomHair(this);

        AddItem(new PlateChest { Movable = false });
        AddItem(new PlateArms { Movable = false });
        AddItem(new PlateGloves { Movable = false });
        AddItem(new PlateGorget { Movable = false });
        AddItem(new PlateLegs { Movable = false });
        AddItem(new PlateHelm { Movable = false });
        AddItem(new MetalKiteShield { Movable = false });
        AddItem(new Longsword { Movable = false });

        if (target?.Deleted == false)
        {
            MoveToWorld(target.Location, target.Map);
            Combatant = target;
        }
    }

    public override bool ClickTitle => false;
}
