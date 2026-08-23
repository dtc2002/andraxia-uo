using System;

namespace Server.Andraxia;

public enum RegionalPressureClassification { Stable, Normal, Elevated, Severe }

public readonly record struct RegionalPressureChange(int Delta, string Reason);

public sealed class RegionalPressureStore
{
    public const int DefaultBritainPressure = 25;
    public const int MaximumPressure = 100;
    public int Britain { get; private set; } = DefaultBritainPressure;
    public RegionalPressureChange? LastChange { get; private set; }

    public int SetBritain(int value, string reason = null)
    {
        var previous = Britain;
        Britain = Math.Clamp(value, 0, MaximumPressure);
        if (Britain != previous && reason != null)
        {
            LastChange = new RegionalPressureChange(Britain - previous, reason);
        }
        return Britain;
    }

    public int AdjustBritain(int delta, string reason = null) => SetBritain(Britain + delta, reason);
    public void Reset()
    {
        Britain = DefaultBritainPressure;
        LastChange = null;
    }

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
