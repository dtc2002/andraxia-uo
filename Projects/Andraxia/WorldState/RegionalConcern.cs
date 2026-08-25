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
    internal event Action<AndraxiaRegionId> Changed;
    internal RegionalStateStore States { get; }

    public RegionalConcernStore(RegionalStateStore states = null) => States = states ?? new RegionalStateStore();

    public RegionalConcern Britain => Get(KnownAndraxiaRegions.Britain);
    public int QuietIntervals => GetQuietIntervals(KnownAndraxiaRegions.Britain);
    public string LastChange => GetLastChange(KnownAndraxiaRegions.Britain);

    public RegionalConcern Get(AndraxiaRegionId id) => States.TryGet(id, out var state) ? state.Concern :
        throw new ArgumentException($"Unknown regional identifier '{id}'.", nameof(id));
    public int GetQuietIntervals(AndraxiaRegionId id) => States.TryGet(id, out var state) ? state.ConcernQuietIntervals :
        throw new ArgumentException($"Unknown regional identifier '{id}'.", nameof(id));
    public string GetLastChange(AndraxiaRegionId id) => States.TryGet(id, out var state) ? state.LastConcernChange : null;

    internal void Establish(RegionalConcern concern, string reason)
    {
        if (!Enum.IsDefined(concern))
        {
            throw new ArgumentOutOfRangeException(nameof(concern));
        }

        Establish(KnownAndraxiaRegions.Britain, concern, reason);
    }

    internal bool Establish(AndraxiaRegionId id, RegionalConcern concern, string reason)
    {
        if (!Enum.IsDefined(concern)) throw new ArgumentOutOfRangeException(nameof(concern));
        var changed = States.EstablishConcern(id, concern, reason);
        if (changed) Changed?.Invoke(id);
        return changed;
    }

    internal void Clear(string reason)
    {
        Clear(KnownAndraxiaRegions.Britain, reason);
    }

    internal bool Clear(AndraxiaRegionId id, string reason)
    {
        var changed = States.ClearConcern(id, reason);
        if (changed) Changed?.Invoke(id);
        return changed;
    }

    internal void Stabilize(long intervals)
    {
        if (intervals < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervals));
        }

        Stabilize(KnownAndraxiaRegions.Britain, intervals);
    }

    internal bool Stabilize(AndraxiaRegionId id, long intervals)
    {
        var before = States.TryGet(id, out var state) ? state.Concern : RegionalConcern.None;
        var changed = States.Stabilize(id, intervals);
        if (changed && before != RegionalConcern.None && Get(id) == RegionalConcern.None) Changed?.Invoke(id);
        return changed;
    }

    internal void Restore(RegionalConcern concern, int quiet)
    {
        if (!Enum.IsDefined(concern) || quiet is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException();
        }

        Restore(KnownAndraxiaRegions.Britain, concern, quiet);
    }

    internal bool Restore(AndraxiaRegionId id, RegionalConcern concern, int quiet)
    {
        if (!Enum.IsDefined(concern) || quiet is < 0 or > 3) throw new ArgumentOutOfRangeException();
        if (!States.TryGet(id, out var state)) return false;
        var restored = States.Restore(id, state.Pressure, concern, quiet);
        if (restored) Changed?.Invoke(id);
        return restored;
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

    public static string Description(RegionalConcern value, string regionName) => value switch
    {
        RegionalConcern.None => $"No particular threat dominates reports from {regionName}.",
        RegionalConcern.Banditry => $"Reports of organized lawlessness continue around {regionName}.",
        RegionalConcern.Undead => $"Rumors of restless dead continue to trouble {regionName}.",
        RegionalConcern.Raiders => $"Reports suggest raiding parties remain a concern near {regionName}.",
        RegionalConcern.Beasts => $"Dangerous wildlife remains a concern around {regionName}.",
        RegionalConcern.TradeRoutes => $"Travelers remain uneasy about the roads around {regionName}.",
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
