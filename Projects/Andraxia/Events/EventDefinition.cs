using System;

namespace Server.Andraxia;

public sealed record EventDefinition
{
    public EventDefinition(EventDefinitionId id, EventTargetId targetId, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Event duration must be positive.");
        }

        Id = id;
        TargetId = targetId;
        Duration = duration;
    }

    public EventDefinitionId Id { get; }
    public EventTargetId TargetId { get; }
    public TimeSpan Duration { get; }
}
