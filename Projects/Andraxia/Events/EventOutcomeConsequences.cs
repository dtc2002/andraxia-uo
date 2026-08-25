using System.Collections.Generic;

namespace Server.Andraxia;

internal enum EventOutcomeSource { None, CombatSuccess, AutomaticFailure, Administrative }
internal readonly record struct EventConsequence(EventOutcomeSource Source, bool Applied);

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
