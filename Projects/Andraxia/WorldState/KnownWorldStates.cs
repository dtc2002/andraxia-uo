using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Server.Andraxia;

public static class KnownWorldStates
{
    public static readonly WorldStateId Britain = new("region.britain");

    private static readonly ReadOnlyCollection<WorldStateDefinition> _definitions = new(
        [new WorldStateDefinition(Britain, WorldCondition.Normal)]
    );

    public static IReadOnlyList<WorldStateDefinition> Definitions => _definitions;
}
