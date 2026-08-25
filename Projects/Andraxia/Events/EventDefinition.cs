using System;

namespace Server.Andraxia;

public enum EventObjectiveKind { KillAllHostiles, ProtectTargetAndClearHostiles }
public enum EventCategory { Banditry, Undead, Raiders, Beasts, Distress }

public sealed record EventDefinition
{
    public EventDefinition(
        EventDefinitionId id,
        EventTargetId targetId,
        TimeSpan duration,
        string displayName = null,
        string description = null,
        string startBroadcast = null,
        string successBroadcast = null,
        string failureBroadcast = null,
        string rewardDescription = null,
        EventObjectiveKind objectiveKind = EventObjectiveKind.KillAllHostiles,
        EventCategory category = EventCategory.Banditry
    )
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Event duration must be positive.");
        }

        Id = id;
        TargetId = targetId;
        Duration = duration;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
        Description = description;
        StartBroadcast = startBroadcast;
        SuccessBroadcast = successBroadcast;
        FailureBroadcast = failureBroadcast;
        RewardDescription = rewardDescription;
        ObjectiveKind = objectiveKind;
        Category = category;
    }

    public EventDefinitionId Id { get; }
    public EventTargetId TargetId { get; }
    public TimeSpan Duration { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string StartBroadcast { get; }
    public string SuccessBroadcast { get; }
    public string FailureBroadcast { get; }
    public string RewardDescription { get; }
    public EventObjectiveKind ObjectiveKind { get; }
    public EventCategory Category { get; }
    public string ObjectiveLabel => ObjectiveKind == EventObjectiveKind.ProtectTargetAndClearHostiles
        ? "Protect the caravan"
        : "Eliminate the threat";
}
