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
        if (source == EventOutcomeSource.CombatSuccess)
        {
            pressure.AdjustBritain(-5, "Event cleared");
        }
        else if (source == EventOutcomeSource.AutomaticFailure)
        {
            pressure.AdjustBritain(10, "Event expired");
        }
        if (concern != null && events.TryGetInstance(id, out var instance) &&
            events.TryGetDefinition(instance.DefinitionId, out var definition))
        {
            var mapped = RegionalConcernMapping.FromCategory(definition.Category);
            if (source == EventOutcomeSource.AutomaticFailure) concern.Establish(mapped, $"{definition.DisplayName} failed");
            else if (source == EventOutcomeSource.CombatSuccess && concern.Britain == mapped) concern.Clear($"{definition.DisplayName} cleared");
        }
        _states[id] = new EventConsequence(source, true);
    }

    internal void Restore(EventInstanceId id, EventOutcomeSource source, bool applied) =>
        _states[id] = new EventConsequence(source, applied);
    internal void Clear() => _states.Clear();
}
