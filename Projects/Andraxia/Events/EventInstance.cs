namespace Server.Andraxia;

public sealed class EventInstance
{
    internal EventInstance(EventInstanceId id, EventDefinition definition) :
        this(id, definition.Id, definition.TargetId, EventLifecycleState.Active)
    {
    }

    internal EventInstance(
        EventInstanceId id,
        EventDefinitionId definitionId,
        EventTargetId targetId,
        EventLifecycleState state
    )
    {
        Id = id;
        DefinitionId = definitionId;
        TargetId = targetId;
        State = state;
    }

    public EventInstanceId Id { get; }
    public EventDefinitionId DefinitionId { get; }
    public EventTargetId TargetId { get; }
    public EventLifecycleState State { get; }
}
