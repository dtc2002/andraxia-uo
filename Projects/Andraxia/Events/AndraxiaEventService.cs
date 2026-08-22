namespace Server.Andraxia;

public readonly record struct AndraxiaEventResult(
    EventTransitionResult EventResult,
    WorldStateTransitionResult? WorldStateResult
)
{
    public bool Succeeded => EventResult.Succeeded && WorldStateResult?.Succeeded != false;
}

public sealed class AndraxiaEventService
{
    private readonly EventStore _events;
    private readonly WorldStateStore _worldStates;

    public AndraxiaEventService(EventStore events, WorldStateStore worldStates)
    {
        _events = events;
        _worldStates = worldStates;
    }

    public AndraxiaEventResult Trigger(EventDefinitionId definitionId) =>
        Trigger(definitionId, EventInstanceId.New());

    public AndraxiaEventResult Trigger(EventDefinitionId definitionId, EventInstanceId instanceId)
    {
        var validation = _events.ValidateTrigger(definitionId, instanceId);
        if (!validation.Succeeded)
        {
            return new AndraxiaEventResult(validation, null);
        }

        var worldStateResult = _worldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened);
        if (!worldStateResult.Succeeded)
        {
            return new AndraxiaEventResult(validation with { Succeeded = false }, worldStateResult);
        }

        return new AndraxiaEventResult(_events.TriggerValidated(definitionId, instanceId), worldStateResult);
    }

    public AndraxiaEventResult Complete(EventInstanceId instanceId) =>
        Transition(instanceId, EventLifecycleState.Succeeded);

    public AndraxiaEventResult Fail(EventInstanceId instanceId) =>
        Transition(instanceId, EventLifecycleState.Failed);

    private AndraxiaEventResult Transition(EventInstanceId instanceId, EventLifecycleState requested)
    {
        var validation = _events.ValidateTransition(instanceId, requested);
        if (!validation.Succeeded)
        {
            return new AndraxiaEventResult(validation, null);
        }

        var worldStateResult = _worldStates.Transition(KnownWorldStates.Britain, WorldCondition.Normal);
        if (!worldStateResult.Succeeded)
        {
            return new AndraxiaEventResult(validation with { Succeeded = false }, worldStateResult);
        }

        return new AndraxiaEventResult(_events.TransitionValidated(instanceId, requested), worldStateResult);
    }
}
