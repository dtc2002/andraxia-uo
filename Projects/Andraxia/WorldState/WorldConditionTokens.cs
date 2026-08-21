using System;

namespace Server.Andraxia;

internal static class WorldConditionTokens
{
    public static string GetToken(WorldCondition condition) =>
        condition switch
        {
            WorldCondition.Normal     => "normal",
            WorldCondition.Threatened => "threatened",
            WorldCondition.Invaded    => "invaded",
            WorldCondition.Occupied   => "occupied",
            WorldCondition.Recovering => "recovering",
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown world condition.")
        };

    public static bool TryParse(string token, out WorldCondition condition)
    {
        condition = token switch
        {
            "normal"     => WorldCondition.Normal,
            "threatened" => WorldCondition.Threatened,
            "invaded"    => WorldCondition.Invaded,
            "occupied"   => WorldCondition.Occupied,
            "recovering" => WorldCondition.Recovering,
            _            => default
        };

        return token is "normal" or "threatened" or "invaded" or "occupied" or "recovering";
    }
}
