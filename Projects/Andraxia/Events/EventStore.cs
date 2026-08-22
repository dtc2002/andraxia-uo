using System;
using System.Collections.Generic;

namespace Server.Andraxia;

public enum EventTransitionFailure
{
    None,
    UnknownDefinition,
    DuplicateInstance,
    DuplicateActiveDefinitionOrTarget,
    UnknownInstance,
    SameState,
    TerminalInstance
}

internal enum EventRestoreFailure
{
    None,
    UnknownDefinition,
    DuplicateInstance,
    DuplicateActiveDefinitionOrTarget,
    TargetMismatch
}

public readonly record struct EventTransitionResult(
    bool Succeeded,
    EventTransitionFailure Failure,
    EventInstance Instance,
    EventLifecycleState? PreviousState,
    EventLifecycleState RequestedState
);

public sealed class EventStore
{
    private readonly Dictionary<EventDefinitionId, EventDefinition> _definitions = [];
    private readonly Dictionary<EventInstanceId, EventInstance> _instances = [];

    public EventStore(IEnumerable<EventDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        foreach (var definition in definitions)
        {
            if (!_definitions.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException($"Duplicate event definition '{definition.Id}'.", nameof(definitions));
            }
        }
    }

    public bool TryGetInstance(EventInstanceId id, out EventInstance instance) => _instances.TryGetValue(id, out instance);

    public IEnumerable<EventInstance> EnumerateInstances() => _instances.Values;

    public EventTransitionResult Trigger(EventDefinitionId definitionId) => Trigger(definitionId, EventInstanceId.New());

    public EventTransitionResult Trigger(EventDefinitionId definitionId, EventInstanceId instanceId)
    {
        var validation = ValidateTrigger(definitionId, instanceId);
        return validation.Succeeded ? TriggerValidated(definitionId, instanceId) : validation;
    }

    public EventTransitionResult Complete(EventInstanceId instanceId) =>
        Transition(instanceId, EventLifecycleState.Succeeded);

    public EventTransitionResult Fail(EventInstanceId instanceId) =>
        Transition(instanceId, EventLifecycleState.Failed);

    internal EventTransitionResult ValidateTrigger(EventDefinitionId definitionId, EventInstanceId instanceId)
    {
        if (!_definitions.TryGetValue(definitionId, out var definition))
        {
            return Failure(EventTransitionFailure.UnknownDefinition, null, null, EventLifecycleState.Active);
        }

        if (_instances.ContainsKey(instanceId))
        {
            return Failure(EventTransitionFailure.DuplicateInstance, null, null, EventLifecycleState.Active);
        }

        foreach (var instance in _instances.Values)
        {
            if (instance.State == EventLifecycleState.Active &&
                (instance.DefinitionId == definitionId || instance.TargetId == definition.TargetId))
            {
                return Failure(
                    EventTransitionFailure.DuplicateActiveDefinitionOrTarget,
                    instance,
                    instance.State,
                    EventLifecycleState.Active
                );
            }
        }

        return new EventTransitionResult(true, EventTransitionFailure.None, null, null, EventLifecycleState.Active);
    }

    internal EventTransitionResult ValidateTransition(EventInstanceId instanceId, EventLifecycleState requested)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            return Failure(EventTransitionFailure.UnknownInstance, null, null, requested);
        }

        if (instance.State == requested)
        {
            return Failure(EventTransitionFailure.SameState, instance, instance.State, requested);
        }

        if (instance.State is not EventLifecycleState.Active)
        {
            return Failure(EventTransitionFailure.TerminalInstance, instance, instance.State, requested);
        }

        return new EventTransitionResult(true, EventTransitionFailure.None, instance, instance.State, requested);
    }

    internal EventTransitionResult TriggerValidated(EventDefinitionId definitionId, EventInstanceId instanceId)
    {
        var instance = new EventInstance(instanceId, _definitions[definitionId]);
        _instances.Add(instanceId, instance);
        return new EventTransitionResult(true, EventTransitionFailure.None, instance, null, EventLifecycleState.Active);
    }

    internal EventTransitionResult TransitionValidated(EventInstanceId instanceId, EventLifecycleState requested)
    {
        var previous = _instances[instanceId];
        var current = new EventInstance(instanceId, previous.DefinitionId, previous.TargetId, requested);
        _instances[instanceId] = current;
        return new EventTransitionResult(true, EventTransitionFailure.None, current, previous.State, requested);
    }

    internal EventRestoreFailure Restore(
        EventInstanceId instanceId,
        EventDefinitionId definitionId,
        EventTargetId targetId,
        EventLifecycleState state
    )
    {
        if (!_definitions.TryGetValue(definitionId, out var definition))
        {
            return EventRestoreFailure.UnknownDefinition;
        }

        if (_instances.ContainsKey(instanceId))
        {
            return EventRestoreFailure.DuplicateInstance;
        }

        if (definition.TargetId != targetId)
        {
            return EventRestoreFailure.TargetMismatch;
        }

        if (state == EventLifecycleState.Active)
        {
            foreach (var instance in _instances.Values)
            {
                if (instance.State == EventLifecycleState.Active &&
                    (instance.DefinitionId == definitionId || instance.TargetId == targetId))
                {
                    return EventRestoreFailure.DuplicateActiveDefinitionOrTarget;
                }
            }
        }

        _instances.Add(instanceId, new EventInstance(instanceId, definitionId, targetId, state));
        return EventRestoreFailure.None;
    }

    internal void Clear() => _instances.Clear();

    private EventTransitionResult Transition(EventInstanceId instanceId, EventLifecycleState requested)
    {
        var validation = ValidateTransition(instanceId, requested);
        return validation.Succeeded ? TransitionValidated(instanceId, requested) : validation;
    }

    private static EventTransitionResult Failure(
        EventTransitionFailure failure,
        EventInstance instance,
        EventLifecycleState? previous,
        EventLifecycleState requested
    ) => new(false, failure, instance, previous, requested);
}
