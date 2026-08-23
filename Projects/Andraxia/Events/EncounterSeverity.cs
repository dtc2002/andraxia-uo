using System;
using System.Collections.Generic;

namespace Server.Andraxia;

public enum EncounterSeverity { Stable, Normal, Elevated, Severe }

internal static class EncounterSeverityPolicy
{
    internal static EncounterSeverity FromPressure(int pressure) =>
        RegionalPressureStore.Classify(pressure) switch
        {
            RegionalPressureClassification.Stable => EncounterSeverity.Stable,
            RegionalPressureClassification.Normal => EncounterSeverity.Normal,
            RegionalPressureClassification.Elevated => EncounterSeverity.Elevated,
            _ => EncounterSeverity.Severe
        };

    internal static string GetToken(EncounterSeverity severity) => severity switch
    {
        EncounterSeverity.Stable => "stable",
        EncounterSeverity.Normal => "normal",
        EncounterSeverity.Elevated => "elevated",
        _ => "severe"
    };

    internal static bool TryParse(string token, out EncounterSeverity severity)
    {
        severity = token switch
        {
            "stable" => EncounterSeverity.Stable,
            "normal" => EncounterSeverity.Normal,
            "elevated" => EncounterSeverity.Elevated,
            "severe" => EncounterSeverity.Severe,
            _ => (EncounterSeverity)(-1)
        };
        return severity >= EncounterSeverity.Stable;
    }

    internal static string Description(EncounterSeverity severity) => severity switch
    {
        EncounterSeverity.Stable => "Minor disturbance",
        EncounterSeverity.Normal => "Organized disturbance",
        EncounterSeverity.Elevated => "Dangerous disturbance",
        _ => "Major threat"
    };
}

internal static class EncounterCompositionPolicy
{
    internal static string DisplayName(Type type) => type == typeof(AndraxiaEncounterBrigand) ? "Brigand" :
        type == typeof(AndraxiaEncounterEvilMage) ? "EvilMage" :
        type == typeof(AndraxiaEncounterSkeleton) ? "Skeleton" :
        type == typeof(AndraxiaEncounterZombie) ? "Zombie" :
        type == typeof(AndraxiaEncounterGhoul) ? "Ghoul" :
        type == typeof(AndraxiaEncounterWraith) ? "Wraith" : type.Name;

    internal static IReadOnlyList<Type> Brigands(int size, EncounterSeverity severity)
    {
        var types = Filled(size, typeof(AndraxiaEncounterBrigand));
        var stronger = severity switch
        {
            EncounterSeverity.Elevated => 1,
            EncounterSeverity.Severe when size >= 5 => 2,
            EncounterSeverity.Severe => 1,
            _ => 0
        };
        ReplaceTail(types, stronger, typeof(AndraxiaEncounterEvilMage));
        return types;
    }

    internal static IReadOnlyList<Type> Undead(int size, EncounterSeverity severity)
    {
        var types = new Type[size];
        if (severity == EncounterSeverity.Stable)
        {
            Array.Fill(types, typeof(AndraxiaEncounterSkeleton));
            return types;
        }

        var skeletonCount = (size + 1) / 2;
        for (var i = 0; i < size; i++)
        {
            types[i] = i < skeletonCount ? typeof(AndraxiaEncounterSkeleton) : typeof(AndraxiaEncounterZombie);
        }
        if (severity == EncounterSeverity.Elevated)
        {
            ReplaceTail(types, 1, typeof(AndraxiaEncounterGhoul));
        }
        else if (severity == EncounterSeverity.Severe)
        {
            ReplaceTail(types, size >= 5 ? 2 : 1, typeof(AndraxiaEncounterWraith));
        }
        return types;
    }

    private static Type[] Filled(int size, Type type)
    {
        var types = new Type[size];
        Array.Fill(types, type);
        return types;
    }

    private static void ReplaceTail(Type[] types, int count, Type replacement)
    {
        for (var i = types.Length - count; i < types.Length; i++)
        {
            types[i] = replacement;
        }
    }
}
