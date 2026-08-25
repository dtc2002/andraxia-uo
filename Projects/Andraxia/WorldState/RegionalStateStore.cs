using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Andraxia;

public sealed record RegionalStateSnapshot(
    AndraxiaRegionDefinition Definition,
    int Pressure,
    RegionalConcern Concern,
    int ConcernQuietIntervals,
    RegionalPressureChange? LastPressureChange,
    string LastConcernChange
);

public sealed class RegionalStateStore
{
    private readonly Dictionary<AndraxiaRegionId, RegionalState> _states;

    public RegionalStateStore(IEnumerable<AndraxiaRegionDefinition> definitions = null)
    {
        ArgumentNullException.ThrowIfNull(definitions ??= KnownAndraxiaRegions.Definitions);
        _states = new Dictionary<AndraxiaRegionId, RegionalState>();
        foreach (var definition in definitions.OrderBy(static definition => definition.Id.Value, StringComparer.Ordinal))
        {
            if (!_states.TryAdd(definition.Id, new RegionalState(definition)))
            {
                throw new ArgumentException($"Duplicate regional identifier '{definition.Id}'.", nameof(definitions));
            }
        }
    }

    public int Count => _states.Count;

    public bool TryGet(AndraxiaRegionId id, out RegionalStateSnapshot state)
    {
        if (_states.TryGetValue(id, out var value))
        {
            state = value.Snapshot();
            return true;
        }

        state = null;
        return false;
    }

    public IReadOnlyList<RegionalStateSnapshot> Enumerate() => _states.Values
        .OrderBy(static state => state.Definition.Id.Value, StringComparer.Ordinal)
        .Select(static state => state.Snapshot())
        .ToArray();

    internal bool SetPressure(AndraxiaRegionId id, int value, string reason = null)
    {
        if (!_states.TryGetValue(id, out var state))
        {
            return false;
        }

        var previous = state.Pressure;
        state.Pressure = Math.Clamp(value, 0, RegionalPressureStore.MaximumPressure);
        if (state.Pressure != previous && reason != null)
        {
            state.LastPressureChange = new RegionalPressureChange(state.Pressure - previous, reason);
        }
        return true;
    }

    internal bool AdjustPressure(AndraxiaRegionId id, int delta, string reason = null) =>
        _states.TryGetValue(id, out var state) && SetPressure(id, state.Pressure + delta, reason);

    internal bool EstablishConcern(AndraxiaRegionId id, RegionalConcern concern, string reason)
    {
        if (!_states.TryGetValue(id, out var state) || !Enum.IsDefined(concern))
        {
            return false;
        }

        state.Concern = concern;
        state.QuietIntervals = 0;
        state.LastConcernChange = reason;
        return true;
    }

    internal bool ClearConcern(AndraxiaRegionId id, string reason)
    {
        if (!_states.TryGetValue(id, out var state))
        {
            return false;
        }

        state.Concern = RegionalConcern.None;
        state.QuietIntervals = 0;
        state.LastConcernChange = reason;
        return true;
    }

    internal bool Stabilize(AndraxiaRegionId id, long intervals)
    {
        if (intervals < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervals));
        }
        if (!_states.TryGetValue(id, out var state))
        {
            return false;
        }
        if (state.Concern == RegionalConcern.None)
        {
            state.QuietIntervals = 0;
            return true;
        }

        state.QuietIntervals += (int)Math.Min(intervals, 4 - state.QuietIntervals);
        if (state.QuietIntervals >= 4)
        {
            ClearConcern(id, "Natural stabilization");
        }
        return true;
    }

    internal bool Restore(AndraxiaRegionId id, int pressure, RegionalConcern concern, int quietIntervals)
    {
        if (!_states.TryGetValue(id, out var state) || pressure is < 0 or > RegionalPressureStore.MaximumPressure ||
            !Enum.IsDefined(concern) || quietIntervals is < 0 or > 3)
        {
            return false;
        }

        state.Pressure = pressure;
        state.Concern = concern;
        state.QuietIntervals = concern == RegionalConcern.None ? 0 : quietIntervals;
        state.LastPressureChange = null;
        state.LastConcernChange = null;
        return true;
    }

    internal void Reset()
    {
        foreach (var state in _states.Values)
        {
            state.Pressure = RegionalPressureStore.DefaultPressure;
            state.Concern = RegionalConcern.None;
            state.QuietIntervals = 0;
            state.LastPressureChange = null;
            state.LastConcernChange = null;
        }
    }

    private sealed class RegionalState(AndraxiaRegionDefinition definition)
    {
        internal AndraxiaRegionDefinition Definition { get; } = definition;
        internal int Pressure { get; set; } = RegionalPressureStore.DefaultPressure;
        internal RegionalConcern Concern { get; set; }
        internal int QuietIntervals { get; set; }
        internal RegionalPressureChange? LastPressureChange { get; set; }
        internal string LastConcernChange { get; set; }

        internal RegionalStateSnapshot Snapshot() => new(
            Definition, Pressure, Concern, QuietIntervals, LastPressureChange, LastConcernChange
        );
    }
}
