using System;

namespace Server.Andraxia;

public sealed record AndraxiaRegionDefinition
{
    public AndraxiaRegionDefinition(
        AndraxiaRegionId id,
        string displayName,
        int pressureBaseline = 25,
        int securityBaseline = 60,
        int prosperityBaseline = 60
    )
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("A regional display name is required.", nameof(displayName));
        }
        if (pressureBaseline is < 0 or > 100 || securityBaseline is < 0 or > 100 ||
            prosperityBaseline is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(pressureBaseline), "Regional baselines must be from 0 through 100.");
        }

        Id = id;
        DisplayName = displayName;
        PressureBaseline = pressureBaseline;
        SecurityBaseline = securityBaseline;
        ProsperityBaseline = prosperityBaseline;
    }

    public AndraxiaRegionId Id { get; }
    public string DisplayName { get; }
    public int PressureBaseline { get; }
    public int SecurityBaseline { get; }
    public int ProsperityBaseline { get; }
}
