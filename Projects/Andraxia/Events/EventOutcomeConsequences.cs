using System.Collections.Generic;

namespace Server.Andraxia;

internal enum EventOutcomeSource { None, CombatSuccess, AutomaticFailure, Administrative }
internal readonly record struct EventConsequence(EventOutcomeSource Source, bool Applied);
internal readonly record struct RegionalWellbeingImpact(
    int SuccessSecurity,
    int SuccessProsperity,
    int FailureSecurity,
    int FailureProsperity
);

internal static class RegionalWellbeingConsequences
{
    internal static RegionalWellbeingImpact For(EventDefinitionId id) => id == KnownEvents.BritainDisturbance
        ? new(2, 0, -4, -1)
        : id == KnownEvents.BritainUndeadDisturbance ? new(1, 0, -3, 0)
        : id == KnownEvents.BritainOrcRaidingParty ? new(2, 0, -5, -2)
        : id == KnownEvents.BritainBeastOutbreak ? new(1, 0, -2, -1)
        : id == KnownEvents.BritainCaravanAmbush ? new(1, 2, -2, -5)
        : default;
}

internal sealed class EventOutcomeConsequences(
    RegionalPressureStore pressure,
    EventStore events,
    RegionalConcernStore concern = null
)
{
    private readonly Dictionary<EventInstanceId, EventConsequence> _states = [];

    internal EventConsequence Get(EventInstanceId id) => _states.GetValueOrDefault(id);

    internal void Apply(EventInstanceId id, EventOutcomeSource source)
    {
        var current = Get(id);
        if (current.Applied) return;
        if (events.TryGetInstance(id, out var instance) &&
            events.TryGetDefinition(instance.DefinitionId, out var definition))
        {
            var regionId = new AndraxiaRegionId(instance.TargetId.Value);
            if (!pressure.TryGet(regionId, out _))
            {
                _states[id] = new EventConsequence(source, true);
                return;
            }
            if (source == EventOutcomeSource.CombatSuccess) pressure.Adjust(regionId, -5, "Event cleared");
            else if (source == EventOutcomeSource.AutomaticFailure) pressure.Adjust(regionId, 10, "Event expired");

            var impact = RegionalWellbeingConsequences.For(definition.Id);
            var security = source == EventOutcomeSource.CombatSuccess ? impact.SuccessSecurity :
                source == EventOutcomeSource.AutomaticFailure ? impact.FailureSecurity : 0;
            var prosperity = source == EventOutcomeSource.CombatSuccess ? impact.SuccessProsperity :
                source == EventOutcomeSource.AutomaticFailure ? impact.FailureProsperity : 0;
            var reason = $"{definition.DisplayName} {(source == EventOutcomeSource.CombatSuccess ? "cleared" : "failed")}";
            if (security != 0) pressure.States.AdjustSecurity(regionId, security, reason);
            if (prosperity != 0) pressure.States.AdjustProsperity(regionId, prosperity, reason);

            var mapped = RegionalConcernMapping.FromCategory(definition.Category);
            if (concern != null && source == EventOutcomeSource.AutomaticFailure)
                concern.Establish(regionId, mapped, $"{definition.DisplayName} failed");
            else if (concern != null && source == EventOutcomeSource.CombatSuccess && concern.Get(regionId) == mapped)
                concern.Clear(regionId, $"{definition.DisplayName} cleared");
        }
        _states[id] = new EventConsequence(source, true);
    }

    internal void Restore(EventInstanceId id, EventOutcomeSource source, bool applied) =>
        _states[id] = new EventConsequence(source, applied);
    internal void Clear() => _states.Clear();
}
