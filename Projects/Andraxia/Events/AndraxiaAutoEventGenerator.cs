using System;
using System.Linq;

namespace Server.Andraxia;

internal interface IAutoEventRandom
{
    ulong State { get; set; }
    double NextDouble();
}

internal sealed class AutoEventRandom(ulong state) : IAutoEventRandom
{
    public ulong State { get; set; } = state;

    public double NextDouble()
    {
        State = unchecked(State * 6364136223846793005UL + 1442695040888963407UL);
        return (State >> 11) * (1.0 / (1UL << 53));
    }
}

internal readonly record struct AutoEventEvaluationResult(
    bool Evaluated,
    bool Eligible,
    bool ProbabilityPassed,
    EventDefinitionId? SelectedDefinitionId,
    AndraxiaEventResult? TriggerResult
);

internal enum AutoEventEligibility { Disabled, NoPlayers, RegionNotNormal, ActiveTargetEvent, Eligible }
internal enum AutoEventSelectionReason { None, InitialSelection, RepeatSuppressed, OnlyEligibleDefinition }

internal sealed class AndraxiaAutoEventScheduler(Action<DateTime> evaluate)
{
    private TimerExecutionToken _token;

    internal bool TimerRunning => _token.Running;

    internal void Arm(DateTime evaluationUtc, DateTime nowUtc)
    {
        ValidateUtc(evaluationUtc, nameof(evaluationUtc));
        ValidateUtc(nowUtc, nameof(nowUtc));
        Cancel();
        var delay = evaluationUtc > nowUtc ? evaluationUtc - nowUtc : TimeSpan.Zero;
        Timer.StartTimer(delay, () => evaluate(Core.Now), out _token);
    }

    internal void Cancel() => _token.Cancel();

    private static void ValidateUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Automatic-event scheduler time must be UTC.", parameterName);
        }
    }
}

internal sealed class AndraxiaAutoEventGenerator
{
    internal const ulong DefaultRandomState = 0x414E445241584941UL;
    internal static readonly TimeSpan MinimumDelay = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(10);

    private readonly EventStore _events;
    private readonly WorldStateStore _worldStates;
    private readonly AndraxiaEventService _eventService;
    private readonly IAutoEventRandom _random;
    private readonly AndraxiaAutoEventScheduler _scheduler;
    private readonly RegionalPressureStore _pressure;
    private readonly RegionalConcernStore _concern;
    private readonly AndraxiaRegionId _regionId = KnownAndraxiaRegions.Britain;
    private readonly System.Collections.Generic.Dictionary<EventDefinitionId, EncounterLocationId> _recentLocations = [];

    internal AndraxiaAutoEventGenerator(
        EventStore events,
        WorldStateStore worldStates,
        AndraxiaEventService eventService,
        IAutoEventRandom random = null,
        RegionalPressureStore pressure = null,
        RegionalConcernStore concern = null
    )
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _worldStates = worldStates ?? throw new ArgumentNullException(nameof(worldStates));
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _random = random ?? new AutoEventRandom(DefaultRandomState);
        _pressure = pressure ?? eventService.Pressure;
        _concern = concern ?? AndraxiaAssembly.Concern;
        _scheduler = new AndraxiaAutoEventScheduler(nowUtc => Evaluate(nowUtc));
    }

    internal bool Enabled { get; private set; }
    internal DateTime? NextEvaluationUtc { get; private set; }
    internal ulong RandomState => _random.State;
    internal bool TimerRunning => _scheduler.TimerRunning;
    internal int OrdinaryPlayerCount => _eventService.OrdinaryPlayerCount;
    internal AutoEventEligibility Eligibility => GetEligibility();
    internal EventDefinitionId? LastAutomaticDefinitionId { get; private set; }
    internal AutoEventSelectionReason LastSelectionReason { get; private set; }

    internal EncounterLocationId? GetLastAutomaticLocation(EventDefinitionId definitionId) =>
        _recentLocations.TryGetValue(definitionId, out var locationId) ? locationId : null;
    internal System.Collections.Generic.IReadOnlyDictionary<EventDefinitionId, EncounterLocationId> RecentLocations =>
        _recentLocations;

    internal bool Enable(DateTime nowUtc)
    {
        ValidateUtc(nowUtc);
        if (Enabled)
        {
            return false;
        }

        Enabled = true;
        ScheduleNext(nowUtc);
        return true;
    }

    internal bool Disable()
    {
        if (!Enabled)
        {
            return false;
        }

        Enabled = false;
        NextEvaluationUtc = null;
        _scheduler.Cancel();
        return true;
    }

    internal bool IsEligible() => GetEligibility() == AutoEventEligibility.Eligible;

    internal AutoEventEvaluationResult Evaluate(DateTime nowUtc)
    {
        ValidateUtc(nowUtc);
        if (!Enabled)
        {
            return new AutoEventEvaluationResult(false, false, false, null, null);
        }

        var eligibility = GetEligibility();
        var eligibleDefinitions = eligibility == AutoEventEligibility.Eligible ? GetKnownEligibleDefinitions() : [];
        var eligible = eligibility == AutoEventEligibility.Eligible && eligibleDefinitions.Length != 0;
        var probabilityPassed = eligible &&
                                _random.NextDouble() < RegionalPressureStore.TriggerProbability(_pressure.Get(_regionId));
        EventDefinitionId? selectedDefinitionId = null;
        AndraxiaEventResult? triggerResult = null;
        if (probabilityPassed)
        {
            var preferred = eligibleDefinitions.Length > 1 && LastAutomaticDefinitionId is { } previous
                ? eligibleDefinitions.Where(definitionId => definitionId != previous).ToArray()
                : eligibleDefinitions;
            if (preferred.Length == 0)
            {
                preferred = eligibleDefinitions;
            }
            LastSelectionReason = eligibleDefinitions.Length == 1
                ? AutoEventSelectionReason.OnlyEligibleDefinition
                : preferred.Length < eligibleDefinitions.Length
                    ? AutoEventSelectionReason.RepeatSuppressed
                    : AutoEventSelectionReason.InitialSelection;
            var concernDefinition = _concern == null ? null : RegionalConcernMapping.Definition(_concern.Get(_regionId));
            if (preferred.Length > 1 && concernDefinition is { } biased && preferred.Contains(biased))
            {
                if (_random.NextDouble() < 0.5) selectedDefinitionId = biased;
                else preferred = preferred.Where(id => id != biased).ToArray();
            }
            if (!selectedDefinitionId.HasValue)
            {
                var selectedIndex = preferred.Length == 1 ? 0 : (int)Math.Floor(_random.NextDouble() * preferred.Length);
                selectedDefinitionId = preferred[selectedIndex];
            }
            triggerResult = _eventService.TriggerAutomatic(
                selectedDefinitionId.Value,
                EventInstanceId.New(),
                nowUtc,
                GetLastAutomaticLocation(selectedDefinitionId.Value)
            );
            if (triggerResult.Value.Succeeded)
            {
                LastAutomaticDefinitionId = selectedDefinitionId;
                if (triggerResult.Value.EventResult.Instance.SelectedLocationId is { } locationId)
                {
                    _recentLocations[selectedDefinitionId.Value] = locationId;
                }
            }
        }
        else
        {
            LastSelectionReason = AutoEventSelectionReason.None;
        }

        ScheduleNext(nowUtc);
        return new AutoEventEvaluationResult(true, eligible, probabilityPassed, selectedDefinitionId, triggerResult);
    }

    internal void Restore(
        bool enabled,
        DateTime? nextEvaluationUtc,
        ulong randomState,
        EventDefinitionId? lastAutomaticDefinitionId = null,
        System.Collections.Generic.IReadOnlyDictionary<EventDefinitionId, EncounterLocationId> recentLocations = null
    )
    {
        if (nextEvaluationUtc is { Kind: not DateTimeKind.Utc })
        {
            throw new ArgumentException("Next automatic-event evaluation must be UTC.", nameof(nextEvaluationUtc));
        }

        _scheduler.Cancel();
        Enabled = enabled;
        NextEvaluationUtc = enabled ? nextEvaluationUtc : null;
        _random.State = randomState;
        LastAutomaticDefinitionId = lastAutomaticDefinitionId;
        _recentLocations.Clear();
        if (recentLocations != null)
        {
            foreach (var entry in recentLocations)
            {
                _recentLocations[entry.Key] = entry.Value;
            }
        }
    }

    internal void Recover(DateTime nowUtc)
    {
        ValidateUtc(nowUtc);
        _scheduler.Cancel();
        if (!Enabled)
        {
            NextEvaluationUtc = null;
            return;
        }

        if (NextEvaluationUtc is { } next && next > nowUtc)
        {
            _scheduler.Arm(next, nowUtc);
            return;
        }

        if (NextEvaluationUtc.HasValue)
        {
            Evaluate(nowUtc);
        }
        else
        {
            ScheduleNext(nowUtc);
        }
    }

    internal void ResetDefaults()
    {
        _scheduler.Cancel();
        Enabled = false;
        NextEvaluationUtc = null;
        _random.State = DefaultRandomState;
        LastAutomaticDefinitionId = null;
        LastSelectionReason = AutoEventSelectionReason.None;
        _recentLocations.Clear();
    }

    internal void StopTimer() => _scheduler.Cancel();

    internal EventDefinitionId[] GetEligibleDefinitions()
    {
        if (GetEligibility() != AutoEventEligibility.Eligible)
        {
            return [];
        }

        return GetKnownEligibleDefinitions();
    }

    private EventDefinitionId[] GetKnownEligibleDefinitions() =>
        KnownEvents.AutomaticDefinitions
            .Where(definitionId =>
                _events.TryGetDefinition(definitionId, out _) && _eventService.HasEncounterHandler(definitionId)
            )
            .OrderBy(static definitionId => definitionId.Value, StringComparer.Ordinal)
            .ToArray();

    private AutoEventEligibility GetEligibility()
    {
        if (!Enabled)
        {
            return AutoEventEligibility.Disabled;
        }
        if (_eventService.OrdinaryPlayerCount == 0)
        {
            return AutoEventEligibility.NoPlayers;
        }
        if (!_worldStates.TryGetState(KnownAndraxiaRegions.WorldStateId(_regionId), out var condition) ||
            condition != WorldCondition.Normal)
        {
            return AutoEventEligibility.RegionNotNormal;
        }
        if (_events.EnumerateInstances().Any(
                instance => instance.State == EventLifecycleState.Active && instance.TargetId.Value == _regionId.Value
            ))
        {
            return AutoEventEligibility.ActiveTargetEvent;
        }
        return AutoEventEligibility.Eligible;
    }

    private void ScheduleNext(DateTime nowUtc)
    {
        var rangeMilliseconds = (MaximumDelay - MinimumDelay).TotalMilliseconds;
        var delay = MinimumDelay + TimeSpan.FromMilliseconds(Math.Floor(_random.NextDouble() * rangeMilliseconds));
        NextEvaluationUtc = nowUtc + delay;
        _scheduler.Arm(NextEvaluationUtc.Value, nowUtc);
    }

    private static void ValidateUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Automatic-event time must be UTC.", nameof(value));
        }
    }
}
