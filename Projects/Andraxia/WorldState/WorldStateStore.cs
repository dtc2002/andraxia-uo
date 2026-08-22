using System;
using System.Collections.Generic;

namespace Server.Andraxia;

public enum WorldStateTransitionFailure
{
    None,
    UnknownState,
    SameCondition,
    TransitionNotAllowed
}

public readonly record struct WorldStateTransitionResult(
    bool Succeeded,
    WorldStateTransitionFailure Failure,
    WorldCondition? PreviousCondition,
    WorldCondition RequestedCondition
);

public sealed class WorldStateStore
{
    private readonly Dictionary<WorldStateId, WorldStateDefinition> _definitions = [];
    private readonly Dictionary<WorldStateId, WorldCondition> _states = [];

    public WorldStateStore(IEnumerable<WorldStateDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        foreach (var definition in definitions)
        {
            if (!_definitions.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException($"Duplicate world-state definition '{definition.Id}'.", nameof(definitions));
            }
        }

        EnsureDefaults();
    }

    public bool TryGetState(WorldStateId id, out WorldCondition condition) => _states.TryGetValue(id, out condition);

    public WorldStateTransitionResult Transition(WorldStateId id, WorldCondition requested)
    {
        if (!_states.TryGetValue(id, out var current))
        {
            return new WorldStateTransitionResult(
                false,
                WorldStateTransitionFailure.UnknownState,
                null,
                requested
            );
        }

        if (current == requested)
        {
            return new WorldStateTransitionResult(
                false,
                WorldStateTransitionFailure.SameCondition,
                current,
                requested
            );
        }

        var transitionAllowed =
            current is WorldCondition.Normal && requested is WorldCondition.Threatened ||
            current is WorldCondition.Threatened && requested is WorldCondition.Normal;

        if (!transitionAllowed)
        {
            return new WorldStateTransitionResult(
                false,
                WorldStateTransitionFailure.TransitionNotAllowed,
                current,
                requested
            );
        }

        _states[id] = requested;
        return new WorldStateTransitionResult(true, WorldStateTransitionFailure.None, current, requested);
    }

    public bool Reset(WorldStateId id)
    {
        if (!_definitions.TryGetValue(id, out var definition))
        {
            return false;
        }

        _states[id] = definition.DefaultCondition;
        return true;
    }

    internal void ResetAll()
    {
        _states.Clear();
        EnsureDefaults();
    }

    internal void EnsureDefaults()
    {
        foreach (var (id, definition) in _definitions)
        {
            _states.TryAdd(id, definition.DefaultCondition);
        }
    }

    internal bool Restore(WorldStateId id, WorldCondition condition)
    {
        if (!_definitions.ContainsKey(id))
        {
            return false;
        }

        _states[id] = condition;
        return true;
    }

    internal IEnumerable<KeyValuePair<WorldStateId, WorldCondition>> EnumerateStates() => _states;
}
