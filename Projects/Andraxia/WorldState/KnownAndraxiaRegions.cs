using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Andraxia;

public static class KnownAndraxiaRegions
{
    public static readonly AndraxiaRegionId Britain = new("region.britain");

    public static IReadOnlyList<AndraxiaRegionDefinition> Definitions { get; } =
        [new(Britain, "Britain")];

    internal static bool TryResolve(EventTargetId targetId, out AndraxiaRegionId regionId)
    {
        var candidate = new AndraxiaRegionId(targetId.Value);
        regionId = candidate;
        return Definitions.Any(definition => definition.Id == candidate);
    }

    internal static WorldStateId WorldStateId(AndraxiaRegionId regionId) => new(regionId.Value);
}
