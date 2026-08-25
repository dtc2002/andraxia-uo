using System;

namespace Server.Andraxia;

public enum RegionalPressureClassification { Stable, Normal, Elevated, Severe }

public readonly record struct RegionalPressureChange(int Delta, string Reason);

public sealed class RegionalPressureStore
{
    public const int DefaultPressure = 25;
    public const int DefaultBritainPressure = DefaultPressure;
    public const int MaximumPressure = 100;
    internal RegionalStateStore States { get; }

    public RegionalPressureStore(RegionalStateStore states = null) => States = states ?? new RegionalStateStore();

    public int Britain => Get(KnownAndraxiaRegions.Britain);
    public RegionalPressureChange? LastChange => GetLastChange(KnownAndraxiaRegions.Britain);

    public int Get(AndraxiaRegionId id) => States.TryGet(id, out var state) ? state.Pressure :
        throw new ArgumentException($"Unknown regional identifier '{id}'.", nameof(id));

    public bool TryGet(AndraxiaRegionId id, out int pressure)
    {
        if (States.TryGet(id, out var state))
        {
            pressure = state.Pressure;
            return true;
        }
        pressure = default;
        return false;
    }

    public RegionalPressureChange? GetLastChange(AndraxiaRegionId id) =>
        States.TryGet(id, out var state) ? state.LastPressureChange : null;

    public int SetBritain(int value, string reason = null)
    {
        States.SetPressure(KnownAndraxiaRegions.Britain, value, reason);
        return Britain;
    }

    public int AdjustBritain(int delta, string reason = null) => SetBritain(Britain + delta, reason);
    public bool Set(AndraxiaRegionId id, int value, string reason = null) => States.SetPressure(id, value, reason);
    public bool Adjust(AndraxiaRegionId id, int delta, string reason = null) => States.AdjustPressure(id, delta, reason);
    public void Reset() => States.Reset();

    public static RegionalPressureClassification Classify(int value) => value switch
    {
        <= 24 => RegionalPressureClassification.Stable,
        <= 49 => RegionalPressureClassification.Normal,
        <= 74 => RegionalPressureClassification.Elevated,
        _ => RegionalPressureClassification.Severe
    };

    public static string Description(RegionalPressureClassification classification) => classification switch
    {
        RegionalPressureClassification.Stable => "The region is relatively secure.",
        RegionalPressureClassification.Normal => "Ordinary dangers trouble the region.",
        RegionalPressureClassification.Elevated => "Reports of unrest are becoming frequent.",
        _ => "The region is under sustained pressure."
    };

    public static double TriggerProbability(int value) => value switch
    {
        <= 24 => 0.20,
        <= 49 => 0.35,
        <= 74 => 0.50,
        _ => 0.65
    };
}
