using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Andraxia;

public sealed record RegionalStateSnapshot(
    AndraxiaRegionDefinition Definition,
    int Pressure,
    RegionalConcern Concern,
    int ConcernQuietIntervals,
    int Security,
    int Prosperity,
    RegionalPressureChange? LastPressureChange,
    string LastConcernChange,
    RegionalValueChange? LastSecurityChange,
    RegionalValueChange? LastProsperityChange
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

    public bool SetSecurity(AndraxiaRegionId id, int value, string reason = null) =>
        SetValue(id, value, reason, true);

    internal bool AdjustSecurity(AndraxiaRegionId id, int delta, string reason = null) =>
        _states.TryGetValue(id, out var state) && SetSecurity(id, state.Security + delta, reason);

    public bool SetProsperity(AndraxiaRegionId id, int value, string reason = null) =>
        SetValue(id, value, reason, false);

    internal bool AdjustProsperity(AndraxiaRegionId id, int delta, string reason = null) =>
        _states.TryGetValue(id, out var state) && SetProsperity(id, state.Prosperity + delta, reason);

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

    internal bool Restore(
        AndraxiaRegionId id,
        int pressure,
        RegionalConcern concern,
        int quietIntervals,
        int? security = null,
        int? prosperity = null
    )
    {
        if (!_states.TryGetValue(id, out var state) || pressure is < 0 or > RegionalPressureStore.MaximumPressure ||
            !Enum.IsDefined(concern) || quietIntervals is < 0 or > 3 || security is < 0 or > 100 ||
            prosperity is < 0 or > 100)
        {
            return false;
        }

        state.Pressure = pressure;
        state.Concern = concern;
        state.QuietIntervals = concern == RegionalConcern.None ? 0 : quietIntervals;
        state.Security = security ?? state.Definition.SecurityBaseline;
        state.Prosperity = prosperity ?? state.Definition.ProsperityBaseline;
        state.LastPressureChange = null;
        state.LastConcernChange = null;
        state.LastSecurityChange = null;
        state.LastProsperityChange = null;
        return true;
    }

    internal void Reset()
    {
        foreach (var state in _states.Values)
        {
            state.Pressure = state.Definition.PressureBaseline;
            state.Concern = RegionalConcern.None;
            state.QuietIntervals = 0;
            state.Security = state.Definition.SecurityBaseline;
            state.Prosperity = state.Definition.ProsperityBaseline;
            state.LastPressureChange = null;
            state.LastConcernChange = null;
            state.LastSecurityChange = null;
            state.LastProsperityChange = null;
        }
    }

    internal bool NormalizeWellbeing(AndraxiaRegionId id, long intervals)
    {
        if (intervals < 0) throw new ArgumentOutOfRangeException(nameof(intervals));
        if (!_states.TryGetValue(id, out var state)) return false;
        Normalize(state, intervals, true);
        Normalize(state, intervals, false);
        return true;
    }

    private bool SetValue(AndraxiaRegionId id, int value, string reason, bool security)
    {
        if (!_states.TryGetValue(id, out var state)) return false;
        value = Math.Clamp(value, 0, 100);
        var previous = security ? state.Security : state.Prosperity;
        if (security) state.Security = value;
        else state.Prosperity = value;
        if (value != previous && reason != null)
        {
            var change = new RegionalValueChange(value - previous, reason);
            if (security) state.LastSecurityChange = change;
            else state.LastProsperityChange = change;
        }
        return true;
    }

    private static void Normalize(RegionalState state, long intervals, bool security)
    {
        var current = security ? state.Security : state.Prosperity;
        var baseline = security ? state.Definition.SecurityBaseline : state.Definition.ProsperityBaseline;
        var distance = baseline - current;
        var movement = (int)Math.Min(Math.Abs((long)distance), intervals) * Math.Sign(distance);
        if (movement == 0) return;
        var change = new RegionalValueChange(movement, "Natural stabilization");
        if (security)
        {
            state.Security += movement;
            state.LastSecurityChange = change;
        }
        else
        {
            state.Prosperity += movement;
            state.LastProsperityChange = change;
        }
    }

    private sealed class RegionalState(AndraxiaRegionDefinition definition)
    {
        internal AndraxiaRegionDefinition Definition { get; } = definition;
        internal int Pressure { get; set; } = definition.PressureBaseline;
        internal RegionalConcern Concern { get; set; }
        internal int QuietIntervals { get; set; }
        internal int Security { get; set; } = definition.SecurityBaseline;
        internal int Prosperity { get; set; } = definition.ProsperityBaseline;
        internal RegionalPressureChange? LastPressureChange { get; set; }
        internal string LastConcernChange { get; set; }
        internal RegionalValueChange? LastSecurityChange { get; set; }
        internal RegionalValueChange? LastProsperityChange { get; set; }

        internal RegionalStateSnapshot Snapshot() => new(
            Definition, Pressure, Concern, QuietIntervals, Security, Prosperity, LastPressureChange,
            LastConcernChange, LastSecurityChange, LastProsperityChange
        );
    }
}
