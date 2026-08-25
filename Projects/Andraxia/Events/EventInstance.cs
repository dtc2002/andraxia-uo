using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Server;

namespace Server.Andraxia;

public sealed class EventInstance
{
    internal EventInstance(
        EventInstanceId id,
        EventDefinition definition,
        DateTime startedUtc,
        IReadOnlyCollection<Serial> ownedMobiles = null,
        EncounterLocationId? selectedLocationId = null,
        EncounterSeverity severity = EncounterSeverity.Normal,
        IReadOnlyCollection<Serial> protectedMobiles = null,
        IReadOnlyCollection<Serial> alliedMobiles = null,
        int initialHostileCount = -1,
        int initialProtectedCount = -1,
        int initialAlliedCount = -1
    ) :
        this(
            id,
            definition.Id,
            definition.TargetId,
            EventLifecycleState.Active,
            startedUtc,
            startedUtc + definition.Duration,
            null,
            ownedMobiles,
            selectedLocationId,
            severity,
            protectedMobiles,
            alliedMobiles,
            initialHostileCount,
            initialProtectedCount,
            initialAlliedCount
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
        DateTime? completedUtc,
        IReadOnlyCollection<Serial> ownedMobiles = null,
        EncounterLocationId? selectedLocationId = null,
        EncounterSeverity severity = EncounterSeverity.Normal,
        IReadOnlyCollection<Serial> protectedMobiles = null,
        IReadOnlyCollection<Serial> alliedMobiles = null,
        int initialHostileCount = -1,
        int initialProtectedCount = -1,
        int initialAlliedCount = -1
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
        OwnedMobiles = new ReadOnlyCollection<Serial>([.. ownedMobiles ?? []]);
        SelectedLocationId = selectedLocationId;
        Severity = severity;
        ProtectedMobiles = new ReadOnlyCollection<Serial>([.. protectedMobiles ?? []]);
        AlliedMobiles = new ReadOnlyCollection<Serial>([.. alliedMobiles ?? []]);
        if (ProtectedMobiles.Distinct().Count() != ProtectedMobiles.Count ||
            AlliedMobiles.Distinct().Count() != AlliedMobiles.Count ||
            ProtectedMobiles.Any(serial => !OwnedMobiles.Contains(serial)) ||
            AlliedMobiles.Any(serial => !OwnedMobiles.Contains(serial)) ||
            ProtectedMobiles.Any(AlliedMobiles.Contains))
        {
            throw new ArgumentException("Event entity roles must be unique and refer to owned entities.");
        }
        var currentHostileCount = OwnedMobiles.Count - ProtectedMobiles.Count - AlliedMobiles.Count;
        InitialHostileCount = initialHostileCount < 0 ? currentHostileCount : initialHostileCount;
        InitialProtectedCount = initialProtectedCount < 0 ? ProtectedMobiles.Count : initialProtectedCount;
        InitialAlliedCount = initialAlliedCount < 0 ? AlliedMobiles.Count : initialAlliedCount;
        if (InitialHostileCount < currentHostileCount || InitialProtectedCount < ProtectedMobiles.Count ||
            InitialAlliedCount < AlliedMobiles.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(initialHostileCount));
        }
    }

    public EventInstanceId Id { get; }
    public EventDefinitionId DefinitionId { get; }
    public EventTargetId TargetId { get; }
    public EventLifecycleState State { get; }
    public DateTime StartedUtc { get; }
    public DateTime ExpiresUtc { get; }
    public DateTime? CompletedUtc { get; }
    public IReadOnlyList<Serial> OwnedMobiles { get; }
    public EncounterLocationId? SelectedLocationId { get; }
    public EncounterSeverity Severity { get; }
    public IReadOnlyList<Serial> ProtectedMobiles { get; }
    public IReadOnlyList<Serial> AlliedMobiles { get; }
    public int InitialHostileCount { get; }
    public int InitialProtectedCount { get; }
    public int InitialAlliedCount { get; }
    public IEnumerable<Serial> HostileMobiles => OwnedMobiles.Where(serial =>
        !ProtectedMobiles.Contains(serial) && !AlliedMobiles.Contains(serial));

    private static void ValidateTimestamp(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Event timestamps must be UTC.", parameterName);
        }
    }
}
