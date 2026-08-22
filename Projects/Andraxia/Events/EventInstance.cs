using System;

namespace Server.Andraxia;

public sealed class EventInstance
{
    internal EventInstance(EventInstanceId id, EventDefinition definition, DateTime startedUtc) :
        this(
            id,
            definition.Id,
            definition.TargetId,
            EventLifecycleState.Active,
            startedUtc,
            startedUtc + definition.Duration,
            null
        )
    {
    }

    internal EventInstance(
        EventInstanceId id,
        EventDefinitionId definitionId,
        EventTargetId targetId,
        EventLifecycleState state,
        DateTime startedUtc,
        DateTime expiresUtc,
        DateTime? completedUtc
    )
    {
        ValidateTimestamp(startedUtc, nameof(startedUtc));
        ValidateTimestamp(expiresUtc, nameof(expiresUtc));

        if (expiresUtc <= startedUtc)
        {
            throw new ArgumentException("Event expiration must be after its start.", nameof(expiresUtc));
        }

        if (completedUtc is { } completed)
        {
            ValidateTimestamp(completed, nameof(completedUtc));
            if (completed < startedUtc)
            {
                throw new ArgumentException("Event completion cannot precede its start.", nameof(completedUtc));
            }
        }

        if (state == EventLifecycleState.Active && completedUtc != null)
        {
            throw new ArgumentException("An active event cannot have a completion time.", nameof(completedUtc));
        }

        if (state != EventLifecycleState.Active && completedUtc == null)
        {
            throw new ArgumentException("A terminal event requires a completion time.", nameof(completedUtc));
        }

        Id = id;
        DefinitionId = definitionId;
        TargetId = targetId;
        State = state;
        StartedUtc = startedUtc;
        ExpiresUtc = expiresUtc;
        CompletedUtc = completedUtc;
    }

    public EventInstanceId Id { get; }
    public EventDefinitionId DefinitionId { get; }
    public EventTargetId TargetId { get; }
    public EventLifecycleState State { get; }
    public DateTime StartedUtc { get; }
    public DateTime ExpiresUtc { get; }
    public DateTime? CompletedUtc { get; }

    private static void ValidateTimestamp(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Event timestamps must be UTC.", parameterName);
        }
    }
}
