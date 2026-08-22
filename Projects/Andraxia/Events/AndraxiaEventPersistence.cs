using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Server.Logging;

namespace Server.Andraxia;

public sealed class AndraxiaEventPersistence : GenericPersistence
{
    internal const int CurrentVersion = 0;
    internal const string PersistenceName = "AndraxiaEvents";
    internal const int MaxEntryCount = 10_000;

    private static readonly ILogger logger = LogFactory.GetLogger(typeof(AndraxiaEventPersistence));
    private readonly EventStore _events;
    private readonly WorldStateStore _worldStates;

    public AndraxiaEventPersistence(EventStore events, WorldStateStore worldStates) : base(PersistenceName, 10)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _worldStates = worldStates ?? throw new ArgumentNullException(nameof(worldStates));
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
        if (version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported {PersistenceName} format version {version}; expected {CurrentVersion}."
            );
        }

        var count = reader.ReadEncodedInt();
        if (count is < 0 or > MaxEntryCount)
        {
            throw new InvalidDataException($"Invalid {PersistenceName} entry count {count}.");
        }

        var seen = new HashSet<EventInstanceId>();
        for (var i = 0; i < count; i++)
        {
            var instanceToken = reader.ReadString();
            var definitionToken = reader.ReadString();
            var targetToken = reader.ReadString();
            var lifecycleToken = reader.ReadString();

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
            var failure = _events.Restore(instanceId, definitionId, targetId, state);

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

    public override void PostDeserialize() => ReconcileWorldState();

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
}
