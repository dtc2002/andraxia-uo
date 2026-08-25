using System;

namespace Server.Andraxia;

public sealed record AndraxiaRegionDefinition
{
    public AndraxiaRegionDefinition(AndraxiaRegionId id, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("A regional display name is required.", nameof(displayName));
        }

        Id = id;
        DisplayName = displayName;
    }

    public AndraxiaRegionId Id { get; }
    public string DisplayName { get; }
}
