using System;
using System.Collections.Generic;
using System.Linq;
using Server.Logging;

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
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(AndraxiaEventService));
    private readonly EventStore _events;
    private readonly WorldStateStore _worldStates;
    private readonly AndraxiaEventExpirationScheduler _scheduler;
    private readonly Dictionary<EventDefinitionId, IEventEncounterSpawner> _encounters = [];
    private readonly IEncounterLocationSelector _locationSelector;

    public AndraxiaEventService(EventStore events, WorldStateStore worldStates) :
        this(
            events,
            worldStates,
            [new BritainBrigandEncounter(), new BritainUndeadEncounter()],
            new DeterministicEncounterLocationSelector()
        )
    {
    }

    internal AndraxiaEventService(
        EventStore events,
        WorldStateStore worldStates,
        IEventEncounterSpawner encounter
    ) : this(events, worldStates, [encounter], new DeterministicEncounterLocationSelector())
    {
    }

    internal AndraxiaEventService(
        EventStore events,
        WorldStateStore worldStates,
        IEventEncounterSpawner encounter,
        IEncounterLocationSelector locationSelector
    ) : this(events, worldStates, [encounter], locationSelector)
    {
    }

    internal AndraxiaEventService(
        EventStore events,
        WorldStateStore worldStates,
        IEnumerable<IEventEncounterSpawner> encounters,
        IEncounterLocationSelector locationSelector
    )
    {
        _events = events;
        _worldStates = worldStates;
        ArgumentNullException.ThrowIfNull(encounters);
        foreach (var encounter in encounters)
        {
            if (!_encounters.TryAdd(encounter.DefinitionId, encounter))
            {
                throw new ArgumentException($"Duplicate encounter handler '{encounter.DefinitionId}'.", nameof(encounters));
            }
        }
        _locationSelector = locationSelector ?? throw new ArgumentNullException(nameof(locationSelector));
        _scheduler = new AndraxiaEventExpirationScheduler(events, Advance);
    }

    public AndraxiaEventResult Trigger(EventDefinitionId definitionId) =>
        Trigger(definitionId, EventInstanceId.New(), Core.Now);

    public AndraxiaEventResult Trigger(EventDefinitionId definitionId, EncounterLocationId locationId) =>
        Trigger(definitionId, EventInstanceId.New(), Core.Now, locationId);

    public AndraxiaEventResult Trigger(
        EventDefinitionId definitionId,
        EventInstanceId instanceId,
        DateTime nowUtc
    ) => Trigger(definitionId, instanceId, nowUtc, null);

    internal AndraxiaEventResult Trigger(
        EventDefinitionId definitionId,
        EventInstanceId instanceId,
        DateTime nowUtc,
        EncounterLocationId? forcedLocationId
    )
    {
        ValidateUtc(nowUtc);
        var validation = _events.ValidateTrigger(definitionId, instanceId);
        if (!validation.Succeeded)
        {
            return new AndraxiaEventResult(validation, null);
        }

        if (!_encounters.TryGetValue(definitionId, out var encounter))
        {
            return new AndraxiaEventResult(
                validation with { Succeeded = false, Failure = EventTransitionFailure.EncounterUnavailable },
                null
            );
        }

        EncounterLocation location;
        if (forcedLocationId is { } locationId)
        {
            if (!encounter.Locations.Any(candidate => candidate.Id == locationId) ||
                !KnownEncounterLocations.TryGetForDefinition(definitionId, locationId, out location))
            {
                return new AndraxiaEventResult(
                    validation with { Succeeded = false, Failure = EventTransitionFailure.UnknownEncounterLocation },
                    null
                );
            }
        }
        else
        {
            location = _locationSelector.Select(
                definitionId,
                instanceId,
                encounter.Locations
            );
        }

        var worldStateResult = _worldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened);
        if (!worldStateResult.Succeeded)
        {
            return new AndraxiaEventResult(validation with { Succeeded = false }, worldStateResult);
        }

        var spawned = new List<Serial>(encounter.EncounterSize);
        if (!encounter.TrySpawn(location, spawned, out var spawnFailure))
        {
            foreach (var serial in spawned)
            {
                encounter.Delete(serial);
            }

            var compensation = _worldStates.Transition(KnownWorldStates.Britain, WorldCondition.Normal);
            if (!compensation.Succeeded)
            {
                logger.Error(
                    "Andraxia event {Definition} spawn failed and Britain compensation was rejected: {Failure}",
                    definitionId,
                    compensation.Failure
                );
            }

            logger.Error("Andraxia event {Definition} encounter spawn failed: {Failure}", definitionId, spawnFailure);
            return new AndraxiaEventResult(
                validation with { Succeeded = false, Failure = EventTransitionFailure.EncounterSpawnFailed },
                worldStateResult
            );
        }

        var result = new AndraxiaEventResult(
            _events.TriggerValidated(definitionId, instanceId, nowUtc, spawned, location.Id),
            worldStateResult
        );
        _scheduler.Rearm(nowUtc);
        return result;
    }

    public AndraxiaEventResult Complete(EventInstanceId instanceId) =>
        Complete(instanceId, Core.Now);

    public AndraxiaEventResult Complete(EventInstanceId instanceId, DateTime nowUtc) =>
        Transition(instanceId, EventLifecycleState.Succeeded, nowUtc, true);

    public AndraxiaEventResult Fail(EventInstanceId instanceId) =>
        Fail(instanceId, Core.Now);

    public AndraxiaEventResult Fail(EventInstanceId instanceId, DateTime nowUtc) =>
        Transition(instanceId, EventLifecycleState.Failed, nowUtc, true);

    public void Advance(DateTime nowUtc)
    {
        ValidateUtc(nowUtc);

        var due = _events.EnumerateInstances()
            .Where(instance => instance.State == EventLifecycleState.Active && instance.ExpiresUtc <= nowUtc)
            .OrderBy(static instance => instance.ExpiresUtc)
            .ThenBy(static instance => instance.Id.Value)
            .Select(static instance => instance.Id)
            .ToArray();

        foreach (var instanceId in due)
        {
            var result = Transition(instanceId, EventLifecycleState.Failed, nowUtc, false);
            if (!result.Succeeded)
            {
                logger.Error(
                    "Expiration of Andraxia event {Identifier} at {ExpirationUtc} was rejected; " +
                    "the event remains Active. Event failure: {EventFailure}; world-state failure: {WorldStateFailure}",
                    instanceId,
                    nowUtc,
                    result.EventResult.Failure,
                    result.WorldStateResult?.Failure
                );
            }
        }

        _scheduler.Rearm(nowUtc);
    }

    internal AndraxiaEventExpirationScheduler Scheduler => _scheduler;
    internal bool HasEncounterHandler(EventDefinitionId definitionId) => _encounters.ContainsKey(definitionId);

    internal void RearmExpirationTimer(DateTime nowUtc) => _scheduler.Rearm(nowUtc);

    internal void StopExpirationTimer() => _scheduler.Cancel();

    internal void HandleOwnedMobileRemoved(Serial serial, DateTime nowUtc)
    {
        ValidateUtc(nowUtc);
        if (!_events.TryRemoveOwnedMobile(serial, out var instance) || instance.OwnedMobiles.Count != 0)
        {
            return;
        }

        var result = Complete(instance.Id, nowUtc);
        if (!result.Succeeded)
        {
            logger.Error(
                "All owned mobiles for Andraxia event {Identifier} were cleared, but completion was rejected: " +
                "event {EventFailure}; world state {WorldStateFailure}",
                instance.Id,
                result.EventResult.Failure,
                result.WorldStateResult?.Failure
            );
        }
    }

    internal void RecoverOwnedMobiles(DateTime nowUtc)
    {
        ValidateUtc(nowUtc);
        var active = _events.EnumerateInstances()
            .Where(static instance => instance.State == EventLifecycleState.Active)
            .ToArray();

        foreach (var instance in active)
        {
            if (!_encounters.TryGetValue(instance.DefinitionId, out var encounter))
            {
                logger.Error(
                    "Cannot recover Andraxia event {Identifier}: no encounter handler for {Definition}",
                    instance.Id,
                    instance.DefinitionId
                );
                continue;
            }

            var remaining = instance.OwnedMobiles.Where(encounter.Exists).ToArray();
            var restored = remaining.Length == instance.OwnedMobiles.Count
                ? instance
                : _events.ReplaceOwnedMobiles(instance, remaining);

            if (restored.OwnedMobiles.Count == 0)
            {
                var result = Complete(restored.Id, nowUtc);
                if (!result.Succeeded)
                {
                    logger.Error(
                        "Recovered Andraxia event {Identifier} has no surviving encounter mobiles, but completion " +
                        "was rejected: event {EventFailure}; world state {WorldStateFailure}",
                        restored.Id,
                        result.EventResult.Failure,
                        result.WorldStateResult?.Failure
                    );
                }
            }
        }
    }

    private AndraxiaEventResult Transition(
        EventInstanceId instanceId,
        EventLifecycleState requested,
        DateTime nowUtc,
        bool rearm
    )
    {
        ValidateUtc(nowUtc);
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

        var result = new AndraxiaEventResult(
            _events.TransitionValidated(instanceId, requested, nowUtc),
            worldStateResult
        );

        if (!_encounters.TryGetValue(result.EventResult.Instance.DefinitionId, out var encounter))
        {
            logger.Error(
                "Cannot clean Andraxia event {Identifier}: no encounter handler for {Definition}",
                result.EventResult.Instance.Id,
                result.EventResult.Instance.DefinitionId
            );
        }
        else
        {
            foreach (var serial in result.EventResult.Instance.OwnedMobiles)
            {
                encounter.Delete(serial);
            }
            _events.ClearOwnedMobiles(instanceId);
        }

        if (rearm)
        {
            _scheduler.Rearm(nowUtc);
        }

        return result;
    }

    private static void ValidateUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Event time must be UTC.", nameof(value));
        }
    }
}
