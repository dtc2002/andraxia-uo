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

    private static readonly ReadOnlyCollection<EncounterLocation> britainDisturbance = new(
        [
            new(BritainCrossroadsWest, Map.Trammel, 1260, 1744, 0),
            new(BritainFarmlandNorthwest, Map.Trammel, 1187, 1636, 0),
            new(BritainFarmlandSouthwest, Map.Trammel, 1199, 1823, 0),
            new(BritainGraveyardEast, Map.Trammel, 1402, 1510, 10),
            new(BritainRoadNorth, Map.Trammel, 1664, 1490, 0),
            new(BritainRoadSouth, Map.Trammel, 1430, 1800, 0)
        ]
    );

    public static IReadOnlyList<EncounterLocation> BritainDisturbance => britainDisturbance;

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

        location = null;
        return false;
    }
}
