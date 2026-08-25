using System.Collections.Generic;
using Server.Regions;

namespace Server.Andraxia;

internal static class AndraxiaGuardSystem
{
    internal static void Install() => Install(Region.Regions);

    internal static void Install(IEnumerable<Region> regions)
    {
        foreach (var region in regions)
        {
            if (region is GuardedRegion guarded)
            {
                guarded.GuardType = typeof(AndraxiaTownGuard);
            }
        }
    }
}
