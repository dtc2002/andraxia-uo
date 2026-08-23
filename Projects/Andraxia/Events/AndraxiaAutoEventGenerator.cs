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

    internal AndraxiaAutoEventGenerator(
        EventStore events,
        WorldStateStore worldStates,
        AndraxiaEventService eventService,
        IAutoEventRandom random = null,
        RegionalPressureStore pressure = null
    )
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _worldStates = worldStates ?? throw new ArgumentNullException(nameof(worldStates));
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _random = random ?? new AutoEventRandom(DefaultRandomState);
        _pressure = pressure ?? eventService.Pressure;
        _scheduler = new AndraxiaAutoEventScheduler(nowUtc => Evaluate(nowUtc));
    }

    internal bool Enabled { get; private set; }
    internal DateTime? NextEvaluationUtc { get; private set; }
    internal ulong RandomState => _random.State;
    internal bool TimerRunning => _scheduler.TimerRunning;

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

    internal bool IsEligible() => GetEligibleDefinitions().Length != 0;

    internal AutoEventEvaluationResult Evaluate(DateTime nowUtc)
    {
        ValidateUtc(nowUtc);
        if (!Enabled)
        {
            return new AutoEventEvaluationResult(false, false, false, null, null);
        }

        var eligibleDefinitions = GetEligibleDefinitions();
        var eligible = eligibleDefinitions.Length != 0;
        var probabilityPassed = eligible &&
                                _random.NextDouble() < RegionalPressureStore.TriggerProbability(_pressure.Britain);
        EventDefinitionId? selectedDefinitionId = null;
        AndraxiaEventResult? triggerResult = null;
        if (probabilityPassed)
        {
            var selectedIndex = eligibleDefinitions.Length == 1
                ? 0
                : (int)Math.Floor(_random.NextDouble() * eligibleDefinitions.Length);
            selectedDefinitionId = eligibleDefinitions[selectedIndex];
            triggerResult = _eventService.Trigger(selectedDefinitionId.Value, EventInstanceId.New(), nowUtc);
        }

        ScheduleNext(nowUtc);
        return new AutoEventEvaluationResult(true, eligible, probabilityPassed, selectedDefinitionId, triggerResult);
    }

    internal void Restore(bool enabled, DateTime? nextEvaluationUtc, ulong randomState)
    {
        if (nextEvaluationUtc is { Kind: not DateTimeKind.Utc })
        {
            throw new ArgumentException("Next automatic-event evaluation must be UTC.", nameof(nextEvaluationUtc));
        }

        _scheduler.Cancel();
        Enabled = enabled;
        NextEvaluationUtc = enabled ? nextEvaluationUtc : null;
        _random.State = randomState;
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
    }

    internal void StopTimer() => _scheduler.Cancel();

    internal EventDefinitionId[] GetEligibleDefinitions()
    {
        if (!Enabled ||
            !_worldStates.TryGetState(KnownWorldStates.Britain, out var condition) ||
            condition != WorldCondition.Normal ||
            _events.EnumerateInstances().Any(
                static instance =>
                    instance.State == EventLifecycleState.Active && instance.TargetId == KnownEvents.Britain
            ))
        {
            return [];
        }

        return KnownEvents.AutomaticDefinitions
            .Where(definitionId =>
                _events.TryGetDefinition(definitionId, out _) && _eventService.HasEncounterHandler(definitionId)
            )
            .OrderBy(static definitionId => definitionId.Value, StringComparer.Ordinal)
            .ToArray();
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
