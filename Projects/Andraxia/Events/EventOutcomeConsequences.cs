using System.Collections.Generic;

namespace Server.Andraxia;

internal enum EventOutcomeSource { None, CombatSuccess, AutomaticFailure, Administrative }
internal readonly record struct EventConsequence(EventOutcomeSource Source, bool Applied);

internal sealed class EventOutcomeConsequences(RegionalPressureStore pressure)
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
        _states[id] = new EventConsequence(source, true);
    }

    internal void Restore(EventInstanceId id, EventOutcomeSource source, bool applied) =>
        _states[id] = new EventConsequence(source, applied);
    internal void Clear() => _states.Clear();
}
