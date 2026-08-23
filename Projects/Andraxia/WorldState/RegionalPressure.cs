using System;

namespace Server.Andraxia;

public enum RegionalPressureClassification { Stable, Normal, Elevated, Severe }

public sealed class RegionalPressureStore
{
    public const int DefaultBritainPressure = 25;
    public const int MaximumPressure = 100;
    public int Britain { get; private set; } = DefaultBritainPressure;

    public int SetBritain(int value) => Britain = Math.Clamp(value, 0, MaximumPressure);
    public int AdjustBritain(int delta) => SetBritain(Britain + delta);
    public void Reset() => Britain = DefaultBritainPressure;

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
