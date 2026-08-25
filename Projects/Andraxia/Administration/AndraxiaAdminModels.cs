using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Andraxia;

internal enum AndraxiaAdminPanelId
{
    Overview,
    WorldState,
    Events,
    RegionalState,
    Automation,
    History,
    Diagnostics
}

internal sealed record AndraxiaAdminPanel(AndraxiaAdminPanelId Id, string DisplayName);

internal static class AndraxiaAdminPanels
{
    internal static IReadOnlyList<AndraxiaAdminPanel> All { get; } =
    [
        new(AndraxiaAdminPanelId.Overview, "Overview"),
        new(AndraxiaAdminPanelId.WorldState, "World State"),
        new(AndraxiaAdminPanelId.Events, "Events"),
        new(AndraxiaAdminPanelId.RegionalState, "Regional State"),
        new(AndraxiaAdminPanelId.Automation, "Automation"),
        new(AndraxiaAdminPanelId.History, "History"),
        new(AndraxiaAdminPanelId.Diagnostics, "Diagnostics")
    ];
}

internal sealed record AdminEntityView(
    Serial Serial,
    string Role,
    string RuntimeType,
    string MapName,
    Point3D Location,
    bool? Alive,
    bool? Deleted
);

internal sealed record AdminParticipantView(
    string Name,
    int Damage,
    double Percentage,
    bool Qualified,
    string RewardState
);

internal sealed record AdminEventView(
    EventInstanceId Id,
    EventDefinitionId DefinitionId,
    string DisplayName,
    EventLifecycleState State,
    EventCategory Category,
    string Objective,
    EncounterSeverity Severity,
    string SeverityDescription,
    EncounterLocationId? LocationId,
    string LocationName,
    Map Map,
    Point3D Anchor,
    DateTime StartedUtc,
    DateTime ExpiresUtc,
    DateTime? CompletedUtc,
    int TotalHostiles,
    int RemainingHostiles,
    int ProtectedCount,
    int RemainingProtected,
    int AlliedCount,
    int RemainingAllies,
    string Rumor,
    bool RumorRegistered,
    IReadOnlyList<AdminEntityView> Entities,
    IReadOnlyList<AdminParticipantView> Participants,
    string Composition,
    string Consequence
);

internal sealed record AdminHistoryPage(IReadOnlyList<AdminEventView> Entries, int Page, int PageCount);

internal sealed record AdminRegionView(
    AndraxiaRegionId Id,
    string DisplayName,
    int Pressure,
    RegionalPressureClassification Classification,
    RegionalConcern Concern,
    int QuietIntervals,
    int Security,
    RegionalSecurityClassification SecurityClassification,
    int Prosperity,
    RegionalProsperityClassification ProsperityClassification,
    string LastPressureChange,
    string LastConcernChange,
    string LastSecurityChange,
    string LastProsperityChange
);

internal sealed class AndraxiaAdminQueries(
    WorldStateStore worldStates,
    EventStore events,
    AndraxiaEventService eventService,
    AndraxiaAutoEventGenerator autoEvents,
    RegionalPressureStore pressure,
    RegionalPressureStabilizer stabilizer,
    RegionalConcernStore concern,
    Func<Serial, Mobile> findMobile = null
)
{
    internal const int HistoryPageSize = 10;
    private readonly Func<Serial, Mobile> _findMobile = findMobile ?? (serial => World.FindMobile(serial, true));

    internal WorldCondition BritainCondition =>
        worldStates.TryGetState(KnownWorldStates.Britain, out var condition) ? condition : WorldCondition.Normal;
    internal int Pressure => pressure.Britain;
    internal RegionalPressureClassification PressureClassification => RegionalPressureStore.Classify(pressure.Britain);
    internal RegionalConcern Concern => concern.Britain;
    internal int ConcernQuietIntervals => concern.QuietIntervals;
    internal int Security => pressure.States.TryGet(KnownAndraxiaRegions.Britain, out var state) ? state.Security : 60;
    internal int Prosperity => pressure.States.TryGet(KnownAndraxiaRegions.Britain, out var state) ? state.Prosperity : 60;
    internal int OrdinaryPlayers => autoEvents.OrdinaryPlayerCount;
    internal AutoEventEligibility AutomationEligibility => autoEvents.Eligibility;
    internal bool AutomationEnabled => autoEvents.Enabled;
    internal DateTime? NextEvaluationUtc => autoEvents.NextEvaluationUtc;
    internal DateTime NextStabilizationUtc => stabilizer.NextRecoveryUtc;
    internal string LastPressureChange => pressure.LastChange is { } change ? $"{change.Delta:+#;-#;0}: {change.Reason}" : "None";
    internal string LastConcernChange => concern.LastChange ?? "None";
    internal IReadOnlyList<AndraxiaRegionDefinition> Regions => pressure.States.Enumerate()
        .Select(static state => state.Definition).ToArray();

    internal bool TryRegion(AndraxiaRegionId id, out AdminRegionView view)
    {
        if (!pressure.States.TryGet(id, out var state))
        {
            view = null;
            return false;
        }
        view = new AdminRegionView(
            id,
            state.Definition.DisplayName,
            state.Pressure,
            RegionalPressureStore.Classify(state.Pressure),
            state.Concern,
            state.ConcernQuietIntervals,
            state.Security,
            RegionalSecurity.Classify(state.Security),
            state.Prosperity,
            RegionalProsperity.Classify(state.Prosperity),
            state.LastPressureChange is { } change ? $"{change.Delta:+#;-#;0}: {change.Reason}" : "None",
            state.LastConcernChange ?? "None",
            state.LastSecurityChange is { } security ? $"{security.Delta:+#;-#;0}: {security.Reason}" : "None",
            state.LastProsperityChange is { } prosperity ? $"{prosperity.Delta:+#;-#;0}: {prosperity.Reason}" : "None"
        );
        return true;
    }

    internal IReadOnlyList<EventDefinition> Definitions => KnownEvents.Definitions;
    internal IReadOnlyList<EncounterLocation> Locations(EventDefinitionId definitionId) =>
        KnownEncounterLocations.GetForDefinition(definitionId);

    internal IReadOnlyList<AdminEventView> ActiveEvents() => events.EnumerateInstances()
        .Where(static instance => instance.State == EventLifecycleState.Active)
        .OrderBy(static instance => instance.ExpiresUtc)
        .ThenBy(static instance => instance.Id.Value)
        .Select(BuildEvent)
        .ToArray();

    internal bool TryEvent(EventInstanceId id, out AdminEventView view)
    {
        if (!events.TryGetInstance(id, out var instance))
        {
            view = null;
            return false;
        }

        view = BuildEvent(instance);
        return true;
    }

    internal AdminHistoryPage History(int page)
    {
        var terminal = events.EnumerateInstances()
            .Where(static instance => instance.State != EventLifecycleState.Active)
            .OrderByDescending(static instance => instance.CompletedUtc)
            .ThenByDescending(static instance => instance.Id.Value)
            .ToArray();
        var pageCount = Math.Max(1, (terminal.Length + HistoryPageSize - 1) / HistoryPageSize);
        page = Math.Clamp(page, 0, pageCount - 1);
        return new AdminHistoryPage(
            terminal.Skip(page * HistoryPageSize).Take(HistoryPageSize).Select(BuildEvent).ToArray(),
            page,
            pageCount
        );
    }

    internal bool TryGoTo(EventInstanceId id, out Map map, out Point3D anchor)
    {
        map = null;
        anchor = default;
        if (!events.TryGetInstance(id, out var instance) || instance.SelectedLocationId is not { } locationId ||
            !KnownEncounterLocations.TryGetForDefinition(instance.DefinitionId, locationId, out var location))
        {
            return false;
        }

        map = location.Map;
        anchor = location.Anchor;
        return true;
    }

    internal int EventRumorCount() => events.EnumerateInstances().Count(instance =>
        instance.State == EventLifecycleState.Active && eventService.IsRumorRegistered(instance.Id));

    private AdminEventView BuildEvent(EventInstance instance)
    {
        events.TryGetDefinition(instance.DefinitionId, out var definition);
        EncounterLocation location = null;
        if (instance.SelectedLocationId is { } locationId)
        {
            KnownEncounterLocations.TryGetForDefinition(instance.DefinitionId, locationId, out location);
        }

        var entities = instance.OwnedMobiles.Select(serial =>
        {
            var mobile = _findMobile(serial);
            return new AdminEntityView(
                serial,
                instance.ProtectedMobiles.Contains(serial) ? "Protected" :
                    instance.AlliedMobiles.Contains(serial) ? "Ally" : "Hostile",
                mobile?.GetType().Name ?? "Missing",
                mobile?.Map?.Name ?? "-",
                mobile?.Location ?? default,
                mobile?.Alive,
                mobile?.Deleted
            );
        }).ToArray();
        var snapshot = eventService.Participation.Get(instance.Id);
        var participants = eventService.Participation.Participants(instance.Id).Select(participant =>
        {
            var qualified = eventService.Participation.Qualifies(instance.Id, participant);
            return new AdminParticipantView(
                _findMobile(participant.MobileSerial)?.Name ?? participant.MobileSerial.ToString(),
                participant.Damage,
                snapshot.TotalDamage == 0 ? 0 : participant.Damage * 100.0 / snapshot.TotalDamage,
                qualified,
                !qualified ? "Not Qualified" : participant.RewardDelivered ? "Delivered" : "Pending"
            );
        }).ToArray();
        var composition = string.Join(", ", entities.GroupBy(static entity => entity.RuntimeType)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key} x{group.Count()}"));
        var consequence = eventService.Consequences.Get(instance.Id);

        return new AdminEventView(
            instance.Id,
            instance.DefinitionId,
            definition?.DisplayName ?? instance.DefinitionId.Value,
            instance.State,
            definition?.Category ?? default,
            definition?.ObjectiveLabel ?? "-",
            instance.Severity,
            EncounterSeverityPolicy.Description(instance.Severity),
            instance.SelectedLocationId,
            location?.DisplayName ?? "Unknown location",
            location?.Map,
            location?.Anchor ?? default,
            instance.StartedUtc,
            instance.ExpiresUtc,
            instance.CompletedUtc,
            instance.InitialHostileCount,
            instance.HostileMobiles.Count(serial => _findMobile(serial) is { Deleted: false }),
            instance.InitialProtectedCount,
            instance.ProtectedMobiles.Count(serial => _findMobile(serial) is { Deleted: false }),
            instance.InitialAlliedCount,
            instance.AlliedMobiles.Count(serial => _findMobile(serial) is { Deleted: false }),
            location?.RumorText ?? "-",
            eventService.IsRumorRegistered(instance.Id),
            entities,
            participants,
            composition,
            !consequence.Applied ? "Pending" : consequence.Source == EventOutcomeSource.Administrative ?
                "Not Applicable" : "Applied"
        );
    }
}
