using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Server.Logging;

namespace Server.Andraxia;

public sealed class AndraxiaEventPersistence : GenericPersistence
{
    internal const int CurrentVersion = 3;
    internal const string PersistenceName = "AndraxiaEvents";
    internal const int MaxEntryCount = 10_000;
    internal const int MaxOwnedMobileCount = 100;

    private static readonly ILogger logger = LogFactory.GetLogger(typeof(AndraxiaEventPersistence));
    private readonly EventStore _events;
    private readonly WorldStateStore _worldStates;
    private readonly AndraxiaEventService _service;

    public AndraxiaEventPersistence(
        EventStore events,
        WorldStateStore worldStates,
        AndraxiaEventService service
    ) : base(PersistenceName, 10)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _worldStates = worldStates ?? throw new ArgumentNullException(nameof(worldStates));
        _service = service ?? throw new ArgumentNullException(nameof(service));
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
        }
    }

    public override void Deserialize(string savePath, Dictionary<ulong, string> typesDb)
    {
        _events.Clear();
        base.Deserialize(savePath, typesDb);
    }

    public override void Deserialize(IGenericReader reader)
    {
        _events.Clear();

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
                selectedLocationId
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
        }
    }

    public override void PostDeserialize()
    {
        ReconcileWorldState();
        _service.RecoverOwnedMobiles(Core.Now);
        _service.Advance(Core.Now);
    }

    internal void ReconcileWorldState()
    {
        var active = _events.EnumerateInstances().FirstOrDefault(
            static instance =>
                instance.DefinitionId == KnownEvents.BritainDisturbance &&
                instance.State == EventLifecycleState.Active
        );

        if (!_worldStates.TryGetState(KnownWorldStates.Britain, out var condition))
        {
            logger.Error("Cannot reconcile Andraxia events because Britain world state is missing");
            return;
        }

        if (active != null)
        {
            if (condition == WorldCondition.Threatened)
            {
                return;
            }

            var result = _worldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened);
            if (result.Succeeded)
            {
                logger.Information(
                    "Reconciled active Andraxia event {Identifier}: Britain transitioned from {Previous} to Threatened",
                    active.Id,
                    result.PreviousCondition
                );
            }
            else
            {
                logger.Error(
                    "Active Andraxia event {Identifier} is inconsistent with Britain state {Condition}; " +
                    "reconciliation was rejected: {Failure}",
                    active.Id,
                    condition,
                    result.Failure
                );
            }

            return;
        }

        if (condition == WorldCondition.Threatened)
        {
            logger.Warning(
                "Britain is Threatened with no active Andraxia Britain-disturbance event; " +
                "leaving world state unchanged for administrative inspection"
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
