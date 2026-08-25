using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Server.Andraxia;

public static class KnownEncounterLocations
{
    public static readonly EncounterLocationId BritainCrossroadsWest = new("location.britain.crossroads-west");
    public static readonly EncounterLocationId BritainFarmlandNorthwest = new("location.britain.farmland-northwest");
    public static readonly EncounterLocationId BritainFarmlandSouthwest = new("location.britain.farmland-southwest");
    public static readonly EncounterLocationId BritainGraveyardEast = new("location.britain.graveyard-east");
    public static readonly EncounterLocationId BritainRoadNorth = new("location.britain.road-north");
    public static readonly EncounterLocationId BritainRoadSouth = new("location.britain.road-south");
    public static readonly EncounterLocationId BritainUndeadGraveyardEast = new(
        "location.britain.undead.graveyard-east"
    );
    public static readonly EncounterLocationId BritainUndeadGraveyardWest = new(
        "location.britain.undead.graveyard-west"
    );
    public static readonly EncounterLocationId BritainUndeadRuinsNorth = new("location.britain.undead.ruins-north");
    public static readonly EncounterLocationId BritainUndeadWildernessEast = new(
        "location.britain.undead.wilderness-east"
    );
    public static readonly EncounterLocationId BritainUndeadWildernessSouth = new(
        "location.britain.undead.wilderness-south"
    );
    public static readonly EncounterLocationId BritainOrcCampWest = new("location.britain.orc.camp-west");
    public static readonly EncounterLocationId BritainOrcForestNorth = new("location.britain.orc.forest-north");
    public static readonly EncounterLocationId BritainOrcRoadEast = new("location.britain.orc.road-east");
    public static readonly EncounterLocationId BritainOrcOutskirtsSouth = new("location.britain.orc.outskirts-south");
    public static readonly EncounterLocationId BritainBeastForestWest = new("location.britain.beast.forest-west");
    public static readonly EncounterLocationId BritainBeastFarmlandNorth = new("location.britain.beast.farmland-north");
    public static readonly EncounterLocationId BritainBeastRoadSouth = new("location.britain.beast.road-south");
    public static readonly EncounterLocationId BritainBeastWildernessEast = new("location.britain.beast.wilderness-east");
    public static readonly EncounterLocationId BritainCaravanRoadNorth = new("location.britain.caravan.road-north");
    public static readonly EncounterLocationId BritainCaravanCrossroadsWest = new("location.britain.caravan.crossroads-west");
    public static readonly EncounterLocationId BritainCaravanRoadSouth = new("location.britain.caravan.road-south");
    public static readonly EncounterLocationId BritainCaravanFarmlandWest = new("location.britain.caravan.farmland-west");

    private static readonly ReadOnlyCollection<EncounterLocation> britainDisturbance = new(
        [
            new(
                BritainCrossroadsWest, Map.Trammel, 1260, 1744, 0, "West Britain Crossroads",
                "Travelers report brigands troubling the crossroads west of Britain."
            ),
            new(
                BritainFarmlandNorthwest, Map.Trammel, 1187, 1636, 0, "Northwest Britain Farmland",
                "Farmers warn of brigands in the fields northwest of Britain."
            ),
            new(
                BritainFarmlandSouthwest, Map.Trammel, 1199, 1823, 0, "Southwest Britain Farmland",
                "Farmers report brigands in the countryside southwest of Britain."
            ),
            new(
                BritainGraveyardEast, Map.Trammel, 1402, 1510, 10, "East Britain Graveyard",
                "Travelers report brigands near Britain's eastern graveyard."
            ),
            new(
                BritainRoadNorth, Map.Trammel, 1664, 1490, 0, "North Britain Road",
                "Travelers report brigands along the roads north of Britain."
            ),
            new(
                BritainRoadSouth, Map.Trammel, 1430, 1800, 0, "South Britain Road",
                "Travelers warn of brigands along the roads south of Britain."
            )
        ]
    );

    private static readonly ReadOnlyCollection<EncounterLocation> britainUndeadDisturbance = new(
        [
            new(
                BritainUndeadGraveyardEast, Map.Trammel, 1408, 1492, 10, "East Britain Graveyard",
                "The dead are said to be restless near Britain's eastern graveyard."
            ),
            new(
                BritainUndeadGraveyardWest, Map.Trammel, 1350, 1490, 10, "West Britain Graveyard",
                "The dead are said to wander near Britain's western graveyard."
            ),
            new(
                BritainUndeadRuinsNorth, Map.Trammel, 1510, 1400, 0, "North Britain Ruins",
                "Travelers speak of restless dead among the ruins north of Britain."
            ),
            new(
                BritainUndeadWildernessEast, Map.Trammel, 1750, 1580, 0, "East Britain Wilderness",
                "Hunters speak of strange activity in the wilderness east of Britain."
            ),
            new(
                BritainUndeadWildernessSouth, Map.Trammel, 1450, 1840, 0, "South Britain Wilderness",
                "Hunters report restless dead in the wilderness south of Britain."
            )
        ]
    );

    private static readonly ReadOnlyCollection<EncounterLocation> britainOrcRaidingParty = new([
        new(BritainOrcCampWest, Map.Trammel, 1105, 1680, 0, "West Britain Camp", "Scouts report orc raiders camped west of Britain."),
        new(BritainOrcForestNorth, Map.Trammel, 1450, 1325, 0, "North Britain Forest", "Woodcutters warn of orcs in the forest north of Britain."),
        new(BritainOrcRoadEast, Map.Trammel, 1775, 1605, 0, "East Britain Road", "Travelers report orc raiders along the eastern road."),
        new(BritainOrcOutskirtsSouth, Map.Trammel, 1375, 1900, 0, "South Britain Outskirts", "Farmers report orcs gathering south of Britain.")
    ]);
    private static readonly ReadOnlyCollection<EncounterLocation> britainBeastOutbreak = new([
        new(BritainBeastForestWest, Map.Trammel, 1120, 1560, 0, "West Britain Forest", "Hunters warn of dangerous beasts west of Britain."),
        new(BritainBeastFarmlandNorth, Map.Trammel, 1215, 1600, 0, "Northwest Farmland", "Farmers report prowling beasts northwest of Britain."),
        new(BritainBeastRoadSouth, Map.Trammel, 1460, 1835, 0, "South Britain Road", "Travelers warn of beasts along the southern road."),
        new(BritainBeastWildernessEast, Map.Trammel, 1810, 1660, 0, "East Britain Wilderness", "Hunters report predators in the eastern wilderness.")
    ]);
    private static readonly ReadOnlyCollection<EncounterLocation> britainCaravanAmbush = new([
        new(BritainCaravanRoadNorth, Map.Trammel, 1650, 1505, 0, "North Britain Road", "Travelers report a caravan under attack on the road north of Britain."),
        new(BritainCaravanCrossroadsWest, Map.Trammel, 1270, 1735, 0, "West Britain Crossroads", "Travelers report a caravan under attack at the crossroads west of Britain."),
        new(BritainCaravanRoadSouth, Map.Trammel, 1440, 1810, 0, "South Britain Road", "Travelers report a merchant caravan under attack south of Britain."),
        new(BritainCaravanFarmlandWest, Map.Trammel, 1205, 1770, 0, "West Britain Farmland", "Farmers report bandits attacking a caravan west of Britain.")
    ]);

    public static IReadOnlyList<EncounterLocation> BritainDisturbance => britainDisturbance;
    public static IReadOnlyList<EncounterLocation> BritainUndeadDisturbance => britainUndeadDisturbance;

    public static IReadOnlyList<EncounterLocation> GetForDefinition(EventDefinitionId definitionId) =>
        definitionId == KnownEvents.BritainDisturbance
            ? britainDisturbance
            : definitionId == KnownEvents.BritainUndeadDisturbance
                ? britainUndeadDisturbance
                : definitionId == KnownEvents.BritainOrcRaidingParty ? britainOrcRaidingParty
                : definitionId == KnownEvents.BritainBeastOutbreak ? britainBeastOutbreak
                : definitionId == KnownEvents.BritainCaravanAmbush ? britainCaravanAmbush : [];

    public static bool TryGetForDefinition(
        EventDefinitionId definitionId,
        EncounterLocationId locationId,
        out EncounterLocation location
    )
    {
        foreach (var candidate in GetForDefinition(definitionId))
        {
            if (candidate.Id == locationId)
            {
                location = candidate;
                return true;
            }
        }

        location = null;
        return false;
    }

    public static bool TryGet(EncounterLocationId id, out EncounterLocation location)
    {
        foreach (var candidate in britainDisturbance)
        {
            if (candidate.Id == id)
            {
                location = candidate;
                return true;
            }
        }

        foreach (var candidate in britainUndeadDisturbance)
        {
            if (candidate.Id == id)
            {
                location = candidate;
                return true;
            }
        }

        foreach (var definitionId in new[] { KnownEvents.BritainOrcRaidingParty, KnownEvents.BritainBeastOutbreak, KnownEvents.BritainCaravanAmbush })
        {
            foreach (var candidate in GetForDefinition(definitionId))
            {
                if (candidate.Id == id) { location = candidate; return true; }
            }
        }

        location = null;
        return false;
    }
}
