using System.Collections.Generic;
using Server.Network;

namespace Server.Andraxia;

internal static class EncounterScalingPolicy
{
    internal const int MinimumSize = 3;
    internal const int MiddleSize = 5;
    internal const int MaximumSize = 7;

    internal static int GetEncounterSize(int ordinaryPlayerCount) => ordinaryPlayerCount switch
    {
        < 0 => MinimumSize,
        <= 1 => MinimumSize,
        <= 3 => MiddleSize,
        _ => MaximumSize
    };
}

internal static class OnlinePlayerCounter
{
    // NetState.Instances is ModernUO's live connection set. Exact Player access excludes every staff tier.
    internal static int CountOrdinaryPlayers()
    {
        HashSet<Serial> players = [];
        foreach (var state in NetState.Instances)
        {
            if (state.Mobile is { Player: true, AccessLevel: AccessLevel.Player } mobile)
            {
                players.Add(mobile.Serial);
            }
        }

        return players.Count;
    }
}

internal static class EncounterFormation
{
    internal static IReadOnlyList<Point3D> Offsets { get; } =
    [
        new(0, 0, 0),
        new(3, 2, 0),
        new(6, 0, 0),
        new(3, -2, 0),
        new(-3, 2, 0),
        new(-6, 0, 0),
        new(-3, -2, 0)
    ];
}
