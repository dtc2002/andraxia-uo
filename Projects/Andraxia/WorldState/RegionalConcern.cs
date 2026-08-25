using System;

namespace Server.Andraxia;

public enum RegionalConcern
{
    None,
    Banditry,
    Undead,
    Raiders,
    Beasts,
    TradeRoutes
}

public sealed class RegionalConcernStore
{
    internal event System.Action Changed;
    public RegionalConcern Britain { get; private set; }
    public int QuietIntervals { get; private set; }
    public string LastChange { get; private set; }

    internal void Establish(RegionalConcern concern, string reason)
    {
        if (!Enum.IsDefined(concern))
        {
            throw new ArgumentOutOfRangeException(nameof(concern));
        }

        Britain = concern;
        QuietIntervals = 0;
        LastChange = reason;
        Changed?.Invoke();
    }

    internal void Clear(string reason)
    {
        Britain = RegionalConcern.None;
        QuietIntervals = 0;
        LastChange = reason;
        Changed?.Invoke();
    }

    internal void Stabilize(long intervals)
    {
        if (intervals < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervals));
        }

        if (Britain == RegionalConcern.None)
        {
            QuietIntervals = 0;
            return;
        }

        QuietIntervals += (int)System.Math.Min(intervals, 4 - QuietIntervals);
        if (QuietIntervals >= 4)
        {
            Clear("Natural stabilization");
        }
    }

    internal void Restore(RegionalConcern concern, int quiet)
    {
        if (!Enum.IsDefined(concern) || quiet is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException();
        }

        Britain = concern;
        QuietIntervals = concern == RegionalConcern.None ? 0 : quiet;
        Changed?.Invoke();
    }

    internal static string Token(RegionalConcern value) => value switch
    {
        RegionalConcern.None => "none",
        RegionalConcern.Banditry => "banditry",
        RegionalConcern.Undead => "undead",
        RegionalConcern.Raiders => "raiders",
        RegionalConcern.Beasts => "beasts",
        RegionalConcern.TradeRoutes => "trade-routes",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    internal static bool TryParse(string token, out RegionalConcern value)
    {
        value = token switch
        {
            "none" => RegionalConcern.None,
            "banditry" => RegionalConcern.Banditry,
            "undead" => RegionalConcern.Undead,
            "raiders" => RegionalConcern.Raiders,
            "beasts" => RegionalConcern.Beasts,
            "trade-routes" => RegionalConcern.TradeRoutes,
            _ => (RegionalConcern)(-1)
        };
        return Enum.IsDefined(value);
    }

    public static string Description(RegionalConcern value) => value switch
    {
        RegionalConcern.None => "No particular threat dominates local reports.",
        RegionalConcern.Banditry => "Reports of organized lawlessness continue around Britain.",
        RegionalConcern.Undead => "Rumors of restless dead continue to trouble the region.",
        RegionalConcern.Raiders => "Reports suggest raiding parties remain a concern.",
        RegionalConcern.Beasts => "Dangerous wildlife remains a concern in the countryside.",
        RegionalConcern.TradeRoutes => "Travelers remain uneasy about the roads around Britain.",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

internal static class RegionalConcernMapping
{
    internal static RegionalConcern FromCategory(EventCategory category) => category switch
    {
        EventCategory.Banditry => RegionalConcern.Banditry,
        EventCategory.Undead => RegionalConcern.Undead,
        EventCategory.Raiders => RegionalConcern.Raiders,
        EventCategory.Beasts => RegionalConcern.Beasts,
        EventCategory.Distress => RegionalConcern.TradeRoutes,
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    internal static EventDefinitionId? Definition(RegionalConcern concern) => concern switch
    {
        RegionalConcern.None => null,
        RegionalConcern.Banditry => KnownEvents.BritainDisturbance,
        RegionalConcern.Undead => KnownEvents.BritainUndeadDisturbance,
        RegionalConcern.Raiders => KnownEvents.BritainOrcRaidingParty,
        RegionalConcern.Beasts => KnownEvents.BritainBeastOutbreak,
        RegionalConcern.TradeRoutes => KnownEvents.BritainCaravanAmbush,
        _ => throw new ArgumentOutOfRangeException(nameof(concern))
    };
}
