using System;

namespace Server.Andraxia;

public sealed record EventDefinition
{
    public EventDefinition(EventDefinitionId id, EventTargetId targetId, TimeSpan duration, string displayName = null)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Event duration must be positive.");
        }

        Id = id;
        TargetId = targetId;
        Duration = duration;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
    }

    public EventDefinitionId Id { get; }
    public EventTargetId TargetId { get; }
    public TimeSpan Duration { get; }
    public string DisplayName { get; }
}
