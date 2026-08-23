using System;
using System.Collections.Generic;
using System.Linq;
using Server;

namespace Server.Andraxia;

public enum EventTransitionFailure
{
    None,
    UnknownDefinition,
    DuplicateInstance,
    DuplicateActiveDefinitionOrTarget,
    UnknownEncounterLocation,
    EncounterUnavailable,
    EncounterSpawnFailed,
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
    internal const int MaximumTerminalHistory = 32;
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

    public EventTransitionResult Trigger(EventDefinitionId definitionId, EventInstanceId instanceId, DateTime nowUtc)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        var validation = ValidateTrigger(definitionId, instanceId);
        return validation.Succeeded ? TriggerValidated(definitionId, instanceId, nowUtc) : validation;
    }

    public EventTransitionResult Complete(EventInstanceId instanceId, DateTime nowUtc) =>
        Transition(instanceId, EventLifecycleState.Succeeded, nowUtc);

    public EventTransitionResult Fail(EventInstanceId instanceId, DateTime nowUtc) =>
        Transition(instanceId, EventLifecycleState.Failed, nowUtc);

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

    internal EventTransitionResult TriggerValidated(
        EventDefinitionId definitionId,
        EventInstanceId instanceId,
        DateTime nowUtc,
        IReadOnlyCollection<Serial> ownedMobiles = null,
        EncounterLocationId? selectedLocationId = null,
        EncounterSeverity severity = EncounterSeverity.Normal
    )
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        var instance = new EventInstance(
            instanceId, _definitions[definitionId], nowUtc, ownedMobiles, selectedLocationId, severity
        );
        _instances.Add(instanceId, instance);
        return new EventTransitionResult(true, EventTransitionFailure.None, instance, null, EventLifecycleState.Active);
    }

    internal EventTransitionResult TransitionValidated(
        EventInstanceId instanceId,
        EventLifecycleState requested,
        DateTime nowUtc
    )
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        var previous = _instances[instanceId];
        var current = new EventInstance(
            instanceId,
            previous.DefinitionId,
            previous.TargetId,
            requested,
            previous.StartedUtc,
            previous.ExpiresUtc,
            nowUtc,
            previous.OwnedMobiles,
            previous.SelectedLocationId,
            previous.Severity
        );
        _instances[instanceId] = current;
        PruneTerminalHistory();
        return new EventTransitionResult(true, EventTransitionFailure.None, current, previous.State, requested);
    }

    internal EventRestoreFailure Restore(
        EventInstanceId instanceId,
        EventDefinitionId definitionId,
        EventTargetId targetId,
        EventLifecycleState state,
        DateTime startedUtc,
        DateTime expiresUtc,
        DateTime? completedUtc,
        IReadOnlyCollection<Serial> ownedMobiles = null,
        EncounterLocationId? selectedLocationId = null,
        EncounterSeverity severity = EncounterSeverity.Normal,
        bool pruneTerminalHistory = true
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

        _instances.Add(
            instanceId,
            new EventInstance(
                instanceId,
                definitionId,
                targetId,
                state,
                startedUtc,
                expiresUtc,
                completedUtc,
                ownedMobiles,
                selectedLocationId,
                severity
            )
        );
        if (pruneTerminalHistory)
        {
            PruneTerminalHistory();
        }
        return EventRestoreFailure.None;
    }

    internal int PruneTerminalHistory()
    {
        var remove = _instances.Values
            .Where(static instance => instance.State != EventLifecycleState.Active)
            .OrderByDescending(static instance => instance.CompletedUtc)
            .ThenByDescending(static instance => instance.Id.Value)
            .Skip(MaximumTerminalHistory)
            .Select(static instance => instance.Id)
            .ToArray();

        foreach (var instanceId in remove)
        {
            _instances.Remove(instanceId);
        }

        return remove.Length;
    }

    internal bool TryGetDefinition(EventDefinitionId id, out EventDefinition definition) =>
        _definitions.TryGetValue(id, out definition);

    internal void Clear() => _instances.Clear();

    internal bool TryRemoveOwnedMobile(Serial serial, out EventInstance updated)
    {
        var instance = _instances.Values.FirstOrDefault(
            candidate => candidate.State == EventLifecycleState.Active && candidate.OwnedMobiles.Contains(serial)
        );
        if (instance == null)
        {
            updated = null;
            return false;
        }

        updated = ReplaceOwnedMobiles(instance, instance.OwnedMobiles.Where(owned => owned != serial).ToArray());
        return true;
    }

    internal void ClearOwnedMobiles(EventInstanceId instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance) && instance.OwnedMobiles.Count != 0)
        {
            ReplaceOwnedMobiles(instance, []);
        }
    }

    internal EventInstance ReplaceOwnedMobiles(EventInstance instance, IReadOnlyCollection<Serial> ownedMobiles)
    {
        var updated = new EventInstance(
            instance.Id,
            instance.DefinitionId,
            instance.TargetId,
            instance.State,
            instance.StartedUtc,
            instance.ExpiresUtc,
            instance.CompletedUtc,
            ownedMobiles,
            instance.SelectedLocationId,
            instance.Severity
        );
        _instances[instance.Id] = updated;
        return updated;
    }

    private EventTransitionResult Transition(
        EventInstanceId instanceId,
        EventLifecycleState requested,
        DateTime nowUtc
    )
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        var validation = ValidateTransition(instanceId, requested);
        return validation.Succeeded ? TransitionValidated(instanceId, requested, nowUtc) : validation;
    }

    private static void ValidateUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Event time must be UTC.", parameterName);
        }
    }

    private static EventTransitionResult Failure(
        EventTransitionFailure failure,
        EventInstance instance,
        EventLifecycleState? previous,
        EventLifecycleState requested
    ) => new(false, failure, instance, previous, requested);
}
