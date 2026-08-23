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

    private static readonly ReadOnlyCollection<EncounterLocation> britainDisturbance = new(
        [
            new(BritainCrossroadsWest, Map.Trammel, 1260, 1744, 0, "West Britain Crossroads"),
            new(BritainFarmlandNorthwest, Map.Trammel, 1187, 1636, 0, "Northwest Britain Farmland"),
            new(BritainFarmlandSouthwest, Map.Trammel, 1199, 1823, 0, "Southwest Britain Farmland"),
            new(BritainGraveyardEast, Map.Trammel, 1402, 1510, 10, "East Britain Graveyard"),
            new(BritainRoadNorth, Map.Trammel, 1664, 1490, 0, "North Britain Road"),
            new(BritainRoadSouth, Map.Trammel, 1430, 1800, 0, "South Britain Road")
        ]
    );

    private static readonly ReadOnlyCollection<EncounterLocation> britainUndeadDisturbance = new(
        [
            new(BritainUndeadGraveyardEast, Map.Trammel, 1408, 1492, 10, "East Britain Graveyard"),
            new(BritainUndeadGraveyardWest, Map.Trammel, 1350, 1490, 10, "West Britain Graveyard"),
            new(BritainUndeadRuinsNorth, Map.Trammel, 1510, 1400, 0, "North Britain Ruins"),
            new(BritainUndeadWildernessEast, Map.Trammel, 1750, 1580, 0, "East Britain Wilderness"),
            new(BritainUndeadWildernessSouth, Map.Trammel, 1450, 1840, 0, "South Britain Wilderness")
        ]
    );

    public static IReadOnlyList<EncounterLocation> BritainDisturbance => britainDisturbance;
    public static IReadOnlyList<EncounterLocation> BritainUndeadDisturbance => britainUndeadDisturbance;

    public static IReadOnlyList<EncounterLocation> GetForDefinition(EventDefinitionId definitionId) =>
        definitionId == KnownEvents.BritainDisturbance
            ? britainDisturbance
            : definitionId == KnownEvents.BritainUndeadDisturbance
                ? britainUndeadDisturbance
                : [];

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

        location = null;
        return false;
    }
}
