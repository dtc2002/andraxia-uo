using System;

namespace Server.Andraxia;

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
        string rewardDescription = null
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
}
