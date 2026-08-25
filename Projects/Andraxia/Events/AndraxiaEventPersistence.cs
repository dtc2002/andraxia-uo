using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Server.Logging;

namespace Server.Andraxia;

public sealed class AndraxiaEventPersistence : GenericPersistence
{
    internal const int CurrentVersion = 11;
    internal const string PersistenceName = "AndraxiaEvents";
    internal const int MaxEntryCount = 10_000;
    internal const int MaxOwnedMobileCount = 100;

    private static readonly ILogger logger = LogFactory.GetLogger(typeof(AndraxiaEventPersistence));
    private readonly EventStore _events;
    private readonly WorldStateStore _worldStates;
    private readonly AndraxiaEventService _service;
    private readonly AndraxiaAutoEventGenerator _generator;

    public AndraxiaEventPersistence(
        EventStore events,
        WorldStateStore worldStates,
        AndraxiaEventService service
    ) : this(events, worldStates, service, new AndraxiaAutoEventGenerator(events, worldStates, service))
    {
    }

    internal AndraxiaEventPersistence(
        EventStore events,
        WorldStateStore worldStates,
        AndraxiaEventService service,
        AndraxiaAutoEventGenerator generator
    ) : base(PersistenceName, 10)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _worldStates = worldStates ?? throw new ArgumentNullException(nameof(worldStates));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(CurrentVersion);

        var instances = _events.EnumerateInstances().OrderBy(static instance => instance.Id.Value).ToArray();
        writer.WriteEncodedInt(instances.Length);

        foreach (var instance in instances)
        {
            writer.Write(instance.Id.ToString());
            writer.Write(instance.DefinitionId.Value);
            writer.Write(instance.TargetId.Value);
            writer.Write(EventLifecycleTokens.GetToken(instance.State));
            writer.Write(instance.StartedUtc);
            writer.Write(instance.ExpiresUtc);
            writer.Write(instance.CompletedUtc.HasValue);

            if (instance.CompletedUtc is { } completedUtc)
            {
                writer.Write(completedUtc);
            }

            writer.WriteEncodedInt(instance.OwnedMobiles.Count);
            foreach (var serial in instance.OwnedMobiles)
            {
                writer.Write(serial);
            }

            writer.Write(instance.SelectedLocationId.HasValue);
            if (instance.SelectedLocationId is { } locationId)
            {
                writer.Write(locationId.Value);
            }

            var participation = _service.Participation.Get(instance.Id);
            writer.Write(participation.CombatCompletionEligible);
            writer.WriteEncodedInt(participation.TotalDamage);
            writer.WriteEncodedInt(participation.Participants.Count);
            foreach (var participant in participation.Participants)
            {
                writer.Write(participant.MobileSerial);
                writer.WriteEncodedInt(participant.Damage);
                writer.Write(participant.RewardDelivered);
            }
            var consequence = _service.Consequences.Get(instance.Id);
            writer.Write(consequence.Source switch
            {
                EventOutcomeSource.CombatSuccess => "combat-success",
                EventOutcomeSource.AutomaticFailure => "automatic-failure",
                EventOutcomeSource.Administrative => "administrative",
                _ => "none"
            });
            writer.Write(consequence.Applied);
            writer.Write(EncounterSeverityPolicy.GetToken(instance.Severity));
            writer.WriteEncodedInt(instance.ProtectedMobiles.Count);
            foreach (var serial in instance.ProtectedMobiles) writer.Write(serial);
            writer.WriteEncodedInt(instance.AlliedMobiles.Count);
            foreach (var serial in instance.AlliedMobiles) writer.Write(serial);
            writer.WriteEncodedInt(instance.InitialHostileCount);
            writer.WriteEncodedInt(instance.InitialProtectedCount);
            writer.WriteEncodedInt(instance.InitialAlliedCount);
        }

        writer.Write(_generator.Enabled);
        writer.Write(_generator.NextEvaluationUtc.HasValue);
        if (_generator.NextEvaluationUtc is { } nextEvaluationUtc)
        {
            writer.Write(nextEvaluationUtc);
        }
        writer.Write(_generator.RandomState);
        writer.Write(_generator.LastAutomaticDefinitionId.HasValue);
        if (_generator.LastAutomaticDefinitionId is { } lastDefinitionId)
        {
            writer.Write(lastDefinitionId.Value);
        }
        var recentLocations = _generator.RecentLocations.OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal).ToArray();
        writer.WriteEncodedInt(recentLocations.Length);
        foreach (var pair in recentLocations)
        {
            writer.Write(pair.Key.Value);
            writer.Write(pair.Value.Value);
        }
    }

    public override void Deserialize(string savePath, Dictionary<ulong, string> typesDb)
    {
        _events.Clear();
        _service.Participation.Clear();
        _service.Consequences.Clear();
        _generator.ResetDefaults();
        base.Deserialize(savePath, typesDb);
    }

    public override void Deserialize(IGenericReader reader)
    {
        _events.Clear();
        _service.Participation.Clear();
        _service.Consequences.Clear();
        _generator.ResetDefaults();

        var version = reader.ReadEncodedInt();
        if (version is < 0 or > CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported {PersistenceName} format version {version}; latest supported version is {CurrentVersion}."
            );
        }

        var count = reader.ReadEncodedInt();
        if (count is < 0 or > MaxEntryCount)
        {
            throw new InvalidDataException($"Invalid {PersistenceName} entry count {count}.");
        }

        var seen = new HashSet<EventInstanceId>();
        var migrationUtc = Core.Now;
        for (var i = 0; i < count; i++)
        {
            var instanceToken = reader.ReadString();
            var definitionToken = reader.ReadString();
            var targetToken = reader.ReadString();
            var lifecycleToken = reader.ReadString();
            var startedUtc = default(DateTime);
            var expiresUtc = default(DateTime);
            DateTime? completedUtc = null;
            Serial[] ownedMobiles = [];
            EncounterLocationId? selectedLocationId = null;
            var rewardsProcessed = false;
            var combatCompletionEligible = false;
            var totalDamage = 0;
            Dictionary<Serial, int> participantDamage = [];
            Dictionary<Serial, bool> participantDelivery = [];
            var outcomeSource = EventOutcomeSource.None;
            var consequenceApplied = false;
            var severity = EncounterSeverity.Normal;
            Serial[] protectedMobiles = [];
            Serial[] alliedMobiles = [];
            var initialHostileCount = -1;
            var initialProtectedCount = -1;
            var initialAlliedCount = -1;

            if (version >= 1)
            {
                startedUtc = reader.ReadDateTime();
                expiresUtc = reader.ReadDateTime();
                if (reader.ReadBool())
                {
                    completedUtc = reader.ReadDateTime();
                }
            }

            if (version >= 2)
            {
                var ownedCount = reader.ReadEncodedInt();
                if (ownedCount is < 0 or > MaxOwnedMobileCount)
                {
                    throw new InvalidDataException(
                        $"Invalid owned-mobile count {ownedCount} for persisted event {instanceToken}."
                    );
                }

                ownedMobiles = new Serial[ownedCount];
                for (var ownedIndex = 0; ownedIndex < ownedCount; ownedIndex++)
                {
                    ownedMobiles[ownedIndex] = reader.ReadSerial();
                }
            }

            if (version >= 3 && reader.ReadBool())
            {
                var locationToken = reader.ReadString();
                if (string.IsNullOrWhiteSpace(locationToken))
                {
                    throw new InvalidDataException(
                        $"Invalid empty encounter-location identifier for persisted event {instanceToken}."
                    );
                }

                selectedLocationId = new EncounterLocationId(locationToken);
                if (!KnownEncounterLocations.TryGet(selectedLocationId.Value, out _))
                {
                    logger.Error(
                        "Persisted event {Identifier} references unknown encounter location {Location}; " +
                        "retaining the event and location without selecting a replacement",
                        instanceToken,
                        selectedLocationId.Value
                    );
                }
            }

            if (version == 5)
            {
                rewardsProcessed = reader.ReadBool();
                totalDamage = reader.ReadEncodedInt();
                var participantCount = reader.ReadEncodedInt();
                if (totalDamage < 0 || participantCount is < 0 or > MaxOwnedMobileCount)
                {
                    throw new InvalidDataException($"Invalid participation payload for event {instanceToken}.");
                }
                for (var participantIndex = 0; participantIndex < participantCount; participantIndex++)
                {
                    var serial = reader.ReadSerial();
                    var damage = reader.ReadEncodedInt();
                    if (damage <= 0 || !participantDamage.TryAdd(serial, damage))
                    {
                        throw new InvalidDataException($"Invalid participant entry for event {instanceToken}.");
                    }
                }
            }
            else if (version >= 6)
            {
                combatCompletionEligible = reader.ReadBool();
                totalDamage = reader.ReadEncodedInt();
                var participantCount = reader.ReadEncodedInt();
                if (totalDamage < 0 || participantCount is < 0 or > MaxOwnedMobileCount)
                {
                    throw new InvalidDataException($"Invalid participation payload for event {instanceToken}.");
                }
                for (var participantIndex = 0; participantIndex < participantCount; participantIndex++)
                {
                    var serial = reader.ReadSerial();
                    var damage = reader.ReadEncodedInt();
                    var delivered = reader.ReadBool();
                    if (damage <= 0 || !participantDamage.TryAdd(serial, damage))
                    {
                        throw new InvalidDataException($"Invalid participant entry for event {instanceToken}.");
                    }
                    participantDelivery[serial] = delivered;
                }
            }
            if (version >= 7)
            {
                outcomeSource = reader.ReadString() switch
                {
                    "none" => EventOutcomeSource.None,
                    "combat-success" => EventOutcomeSource.CombatSuccess,
                    "automatic-failure" => EventOutcomeSource.AutomaticFailure,
                    "administrative" => EventOutcomeSource.Administrative,
                    var token => throw new InvalidDataException($"Unknown event outcome token '{token}'.")
                };
                consequenceApplied = reader.ReadBool();
            }
            if (version >= 8 && !EncounterSeverityPolicy.TryParse(reader.ReadString(), out severity))
            {
                throw new InvalidDataException($"Unknown event severity token for event {instanceToken}.");
            }
            if (version >= 10)
            {
                var protectedCount = reader.ReadEncodedInt();
                if (protectedCount is < 0 or > MaxOwnedMobileCount)
                    throw new InvalidDataException($"Invalid protected-mobile count for event {instanceToken}.");
                protectedMobiles = new Serial[protectedCount];
                for (var p = 0; p < protectedCount; p++) protectedMobiles[p] = reader.ReadSerial();
            }
            if (version >= 11)
            {
                var alliedCount = reader.ReadEncodedInt();
                if (alliedCount is < 0 or > MaxOwnedMobileCount)
                    throw new InvalidDataException($"Invalid allied-mobile count for event {instanceToken}.");
                alliedMobiles = new Serial[alliedCount];
                for (var a = 0; a < alliedCount; a++) alliedMobiles[a] = reader.ReadSerial();
                initialHostileCount = reader.ReadEncodedInt();
                if (initialHostileCount is < 0 or > MaxOwnedMobileCount)
                    throw new InvalidDataException($"Invalid initial-hostile count for event {instanceToken}.");
                initialProtectedCount = reader.ReadEncodedInt();
                initialAlliedCount = reader.ReadEncodedInt();
                if (initialProtectedCount is < 0 or > MaxOwnedMobileCount ||
                    initialAlliedCount is < 0 or > MaxOwnedMobileCount)
                    throw new InvalidDataException($"Invalid initial role count for event {instanceToken}.");
            }

            if (!EventInstanceId.TryParse(instanceToken, out var instanceId))
            {
                logger.Warning("Ignoring persisted event with malformed instance identifier {Identifier}", instanceToken);
                continue;
            }

            if (!seen.Add(instanceId))
            {
                logger.Warning("Ignoring duplicate persisted event instance {Identifier}; first entry wins", instanceId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(definitionToken))
            {
                logger.Warning("Ignoring persisted event {Identifier} with an empty definition identifier", instanceId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(targetToken))
            {
                logger.Warning("Ignoring persisted event {Identifier} with an empty target identifier", instanceId);
                continue;
            }

            if (!EventLifecycleTokens.TryParse(lifecycleToken, out var state))
            {
                logger.Warning(
                    "Ignoring persisted event {Identifier} with unknown lifecycle token {Lifecycle}",
                    instanceId,
                    lifecycleToken
                );
                continue;
            }

            var definitionId = new EventDefinitionId(definitionToken);
            var targetId = new EventTargetId(targetToken);

            if (!_events.TryGetDefinition(definitionId, out var definition))
            {
                logger.Warning(
                    "Ignoring persisted event {Identifier} for unknown definition {Definition}",
                    instanceId,
                    definitionId
                );
                continue;
            }

            if (version == 0)
            {
                if (state == EventLifecycleState.Active)
                {
                    startedUtc = migrationUtc;
                    expiresUtc = migrationUtc + definition.Duration;
                }
                else
                {
                    startedUtc = migrationUtc - definition.Duration;
                    expiresUtc = migrationUtc;
                    completedUtc = migrationUtc;
                }
            }

            ValidateTimestamps(instanceId, state, startedUtc, expiresUtc, completedUtc);

            var failure = _events.Restore(
                instanceId,
                definitionId,
                targetId,
                state,
                startedUtc,
                expiresUtc,
                completedUtc,
                ownedMobiles,
                selectedLocationId,
                severity,
                protectedMobiles,
                alliedMobiles,
                initialHostileCount,
                initialProtectedCount,
                initialAlliedCount,
                false
            );

            if (failure != EventRestoreFailure.None)
            {
                logger.Warning(
                    "Ignoring persisted event {Identifier} for definition {Definition}: {Failure}",
                    instanceId,
                    definitionId,
                    failure
                );
            }
            else
            {
                _service.Participation.Restore(
                    instanceId,
                    totalDamage,
                    participantDamage.Select(pair => new EventParticipant(
                        pair.Key,
                        pair.Value,
                        version >= 6
                            ? participantDelivery.GetValueOrDefault(pair.Key)
                            : state != EventLifecycleState.Active || rewardsProcessed
                    )),
                    version >= 6 && combatCompletionEligible
                );
                _service.Consequences.Restore(
                    instanceId,
                    version >= 7 ? outcomeSource : state == EventLifecycleState.Active
                        ? EventOutcomeSource.None
                        : EventOutcomeSource.Administrative,
                    version >= 7 ? consequenceApplied : state != EventLifecycleState.Active
                );
            }
        }

        if (version >= 4)
        {
            var enabled = reader.ReadBool();
            DateTime? nextEvaluationUtc = reader.ReadBool() ? reader.ReadDateTime() : null;
            var randomState = reader.ReadULong();
            if (nextEvaluationUtc is { Kind: not DateTimeKind.Utc })
            {
                throw new InvalidDataException("Persisted automatic-event evaluation time must be UTC.");
            }

            EventDefinitionId? lastDefinitionId = null;
            Dictionary<EventDefinitionId, EncounterLocationId> recentLocations = [];
            if (version >= 9)
            {
                if (reader.ReadBool())
                {
                    var token = reader.ReadString();
                    var candidate = new EventDefinitionId(token);
                    if (KnownEvents.AutomaticDefinitions.Contains(candidate))
                    {
                        lastDefinitionId = candidate;
                    }
                    else
                    {
                        logger.Warning("Ignoring unknown recent automatic event definition {Definition}", token);
                    }
                }
                if (version == 9)
                {
                    ReadRecentLocation(reader, KnownEvents.BritainDisturbance, recentLocations);
                    ReadRecentLocation(reader, KnownEvents.BritainUndeadDisturbance, recentLocations);
                }
                else
                {
                    var recentCount = reader.ReadEncodedInt();
                    if (recentCount is < 0 or > 100) throw new InvalidDataException("Invalid recent-location count.");
                    for (var i = 0; i < recentCount; i++)
                    {
                        var definitionId = new EventDefinitionId(reader.ReadString());
                        var locationId = new EncounterLocationId(reader.ReadString());
                        if (KnownEvents.AutomaticDefinitions.Contains(definitionId) &&
                            KnownEncounterLocations.TryGetForDefinition(definitionId, locationId, out _))
                            recentLocations.TryAdd(definitionId, locationId);
                    }
                }
            }

            _generator.Restore(enabled, nextEvaluationUtc, randomState, lastDefinitionId, recentLocations);
        }
    }

    private void WriteRecentLocation(IGenericWriter writer, EventDefinitionId definitionId)
    {
        var locationId = _generator.GetLastAutomaticLocation(definitionId);
        writer.Write(locationId.HasValue);
        if (locationId is { } value)
        {
            writer.Write(value.Value);
        }
    }

    private static void ReadRecentLocation(
        IGenericReader reader,
        EventDefinitionId definitionId,
        IDictionary<EventDefinitionId, EncounterLocationId> locations
    )
    {
        if (!reader.ReadBool())
        {
            return;
        }
        var token = reader.ReadString();
        var locationId = new EncounterLocationId(token);
        if (KnownEncounterLocations.TryGetForDefinition(definitionId, locationId, out _))
        {
            locations[definitionId] = locationId;
        }
        else
        {
            logger.Warning("Ignoring invalid recent location {Location} for {Definition}", token, definitionId);
        }
    }

    public override void PostDeserialize()
    {
        var pruned = _events.PruneTerminalHistory();
        if (pruned != 0)
        {
            logger.Information("Pruned {Count} old Andraxia terminal event records after loading", pruned);
        }

        ReconcileWorldState();
        _service.RecoverOwnedMobiles(Core.Now);
        _service.AdvanceAfterDeserialize(Core.Now);
        _service.RestoreActiveRumors();
        _service.RetryPendingRewards();
        _generator.Recover(Core.Now);
    }

    internal void ReconcileWorldState()
    {
        foreach (var region in KnownAndraxiaRegions.Definitions.OrderBy(
                     static definition => definition.Id.Value, StringComparer.Ordinal
                 ))
        {
            var active = _events.EnumerateInstances().FirstOrDefault(instance =>
                instance.TargetId.Value == region.Id.Value && instance.State == EventLifecycleState.Active
            );
            var worldStateId = KnownAndraxiaRegions.WorldStateId(region.Id);
            if (!_worldStates.TryGetState(worldStateId, out var condition))
            {
                logger.Error("Cannot reconcile Andraxia events because regional world state {Region} is missing", region.Id);
                continue;
            }

            if (active != null)
            {
                if (condition == WorldCondition.Threatened) continue;

                var result = _worldStates.Transition(worldStateId, WorldCondition.Threatened);
                if (result.Succeeded)
                    logger.Information(
                        "Reconciled active Andraxia event {Identifier}: {Region} transitioned from {Previous} to Threatened",
                        active.Id, region.Id, result.PreviousCondition
                    );
                else
                    logger.Error(
                        "Active Andraxia event {Identifier} is inconsistent with {Region} state {Condition}; " +
                        "reconciliation was rejected: {Failure}", active.Id, region.Id, condition, result.Failure
                    );
                continue;
            }

            if (condition == WorldCondition.Threatened)
                logger.Warning(
                    "{Region} is Threatened with no active Andraxia regional event; " +
                    "leaving world state unchanged for administrative inspection", region.Id
                );
        }
    }

    private static void ValidateTimestamps(
        EventInstanceId instanceId,
        EventLifecycleState state,
        DateTime startedUtc,
        DateTime expiresUtc,
        DateTime? completedUtc
    )
    {
        var valid =
            startedUtc.Kind == DateTimeKind.Utc &&
            expiresUtc.Kind == DateTimeKind.Utc &&
            expiresUtc > startedUtc &&
            (completedUtc == null || completedUtc.Value.Kind == DateTimeKind.Utc) &&
            (completedUtc == null || completedUtc.Value >= startedUtc) &&
            (state == EventLifecycleState.Active ? completedUtc == null : completedUtc != null);

        if (!valid)
        {
            throw new InvalidDataException($"Invalid timestamp data for persisted event {instanceId}.");
        }
    }
}
