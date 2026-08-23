using System;
using System.Collections.Generic;
using System.Linq;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class AutoEventGenerationTests
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly EventInstanceId InstanceId = new(
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")
    );

    [Fact]
    public void DefaultsDisabledWithNoTimer()
    {
        using var context = new TestContext(0.0);

        Assert.False(context.Generator.Enabled);
        Assert.False(context.Generator.IsEligible());
        Assert.False(context.Generator.TimerRunning);
        Assert.Null(context.Generator.NextEvaluationUtc);
    }

    [Fact]
    public void NormalBritainWithoutActiveEventIsEligibleWhenEnabled()
    {
        using var context = new TestContext(0.0);

        Assert.True(context.Generator.Enable(StartUtc));

        Assert.True(context.Generator.IsEligible());
    }

    [Fact]
    public void ThreatenedBritainIsIneligible()
    {
        using var context = new TestContext(0.0);
        Assert.True(context.Generator.Enable(StartUtc));
        Assert.True(context.WorldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);

        Assert.False(context.Generator.IsEligible());
    }

    [Fact]
    public void ActiveBritainEventIsIneligibleEvenIfBritainIsNormal()
    {
        using var context = new TestContext(0.0);
        Assert.True(context.Generator.Enable(StartUtc));
        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, InstanceId, StartUtc).Succeeded);
        Assert.True(context.WorldStates.Transition(KnownWorldStates.Britain, WorldCondition.Normal).Succeeded);

        Assert.False(context.Generator.IsEligible());
    }

    [Fact]
    public void DisabledEvaluationDoesNotConsumeRandomnessOrMutateState()
    {
        using var context = new TestContext(0.0);

        var result = context.Generator.Evaluate(StartUtc);

        Assert.False(result.Evaluated);
        Assert.Equal(0, context.Random.CallCount);
        Assert.Empty(context.Events.EnumerateInstances());
        AssertState(context.WorldStates, WorldCondition.Normal);
    }

    [Fact]
    public void EligiblePassingEvaluationUsesExistingEventService()
    {
        using var context = new TestContext(0.0, 0.34, 0.0);
        Assert.True(context.Generator.Enable(StartUtc));

        var result = context.Generator.Evaluate(StartUtc.AddMinutes(5));

        Assert.True(result.Evaluated);
        Assert.True(result.Eligible);
        Assert.True(result.ProbabilityPassed);
        Assert.True(result.TriggerResult?.Succeeded);
        var instance = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Active, instance.State);
        Assert.Equal(BritainBrigandEncounter.Size, instance.OwnedMobiles.Count);
        Assert.NotNull(instance.SelectedLocationId);
        AssertState(context.WorldStates, WorldCondition.Threatened);
    }

    [Fact]
    public void EligibleFailingProbabilityDoesNotTrigger()
    {
        using var context = new TestContext(0.0, 0.35, 0.0);
        Assert.True(context.Generator.Enable(StartUtc));

        var result = context.Generator.Evaluate(StartUtc.AddMinutes(5));

        Assert.True(result.Evaluated);
        Assert.True(result.Eligible);
        Assert.False(result.ProbabilityPassed);
        Assert.Null(result.TriggerResult);
        Assert.Empty(context.Events.EnumerateInstances());
        AssertState(context.WorldStates, WorldCondition.Normal);
    }

    [Fact]
    public void SameRandomSeedProducesSameSequence()
    {
        var first = new AutoEventRandom(123456789);
        var second = new AutoEventRandom(123456789);

        Assert.Equal(
            Enumerable.Range(0, 10).Select(_ => first.NextDouble()),
            Enumerable.Range(0, 10).Select(_ => second.NextDouble())
        );
        Assert.Equal(first.State, second.State);
    }

    [Fact]
    public void EnableAndEvaluationMaintainExactlyOneBoundedOneShotTimer()
    {
        using var context = new TestContext(0.0, 0.9, 0.999999);
        Assert.True(context.Generator.Enable(StartUtc));
        var first = context.Generator.NextEvaluationUtc;

        Assert.True(context.Generator.TimerRunning);
        AssertDelayInRange(StartUtc, first);

        context.Generator.Evaluate(StartUtc.AddMinutes(5));
        var second = context.Generator.NextEvaluationUtc;

        Assert.True(context.Generator.TimerRunning);
        Assert.NotEqual(first, second);
        AssertDelayInRange(StartUtc.AddMinutes(5), second);
    }

    [Fact]
    public void DisableCancelsGenerationWithoutCancellingEventExpiration()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext(0.0);
        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, InstanceId, clock.Now).Succeeded);
        Assert.True(context.Generator.Enable(clock.Now));

        Assert.True(context.Generator.Disable());

        Assert.False(context.Generator.TimerRunning);
        Assert.True(context.Service.Scheduler.TimerRunning);
    }

    [Fact]
    public void TimerEvaluationRearmsOnceWithinRange()
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext(0.0, 0.9, 0.0);
        Assert.True(context.Generator.Enable(clock.Now));

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True(context.Generator.TimerRunning);
        Assert.Equal(StartUtc.AddMinutes(10), context.Generator.NextEvaluationUtc);
        Assert.Empty(context.Events.EnumerateInstances());
    }

    [Fact]
    public void IneligibleEvaluationRearmsWithoutMutation()
    {
        using var context = new TestContext(0.0, 0.0);
        Assert.True(context.Generator.Enable(StartUtc));
        Assert.True(context.WorldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);

        var result = context.Generator.Evaluate(StartUtc.AddMinutes(5));

        Assert.True(result.Evaluated);
        Assert.False(result.Eligible);
        Assert.Empty(context.Events.EnumerateInstances());
        AssertState(context.WorldStates, WorldCondition.Threatened);
        Assert.True(context.Generator.TimerRunning);
    }

    [Fact]
    public void EventCompletionDoesNotRescheduleGeneration()
    {
        using var context = new TestContext(0.5);
        Assert.True(context.Generator.Enable(StartUtc));
        var scheduled = context.Generator.NextEvaluationUtc;
        Assert.True(context.Service.Trigger(KnownEvents.BritainDisturbance, InstanceId, StartUtc).Succeeded);

        Assert.True(context.Service.Complete(InstanceId, StartUtc.AddMinutes(1)).Succeeded);

        Assert.Equal(scheduled, context.Generator.NextEvaluationUtc);
        Assert.True(context.Generator.TimerRunning);
    }

    [Theory]
    [InlineData(0.34, true)]
    [InlineData(0.35, false)]
    public void ImmediateManualEvaluationMatchesScheduledEvaluation(double decision, bool triggered)
    {
        var manual = RunEvaluation(false, decision);
        var scheduled = RunEvaluation(true, decision);

        Assert.Equal(manual, scheduled);
        Assert.Equal(triggered, manual.Triggered);
    }

    [Fact]
    public void EligibleDefinitionsAreSortedOrdinally()
    {
        using var context = new DualContext(0.0);
        Assert.True(context.Generator.Enable(StartUtc));

        Assert.Equal(
            new[] { KnownEvents.BritainUndeadDisturbance, KnownEvents.BritainDisturbance },
            context.Generator.GetEligibleDefinitions()
        );
    }

    [Theory]
    [InlineData(0.0, "event.britain.undead-disturbance")]
    [InlineData(0.999999, "event.test.britain-disturbance")]
    public void EqualSelectionCanChooseEitherEligibleDefinition(double selection, string expectedDefinition)
    {
        using var context = new DualContext(0.0, 0.34, selection, 0.0);
        Assert.True(context.Generator.Enable(StartUtc));

        var result = context.Generator.Evaluate(StartUtc.AddMinutes(5));

        Assert.True(result.TriggerResult?.Succeeded);
        Assert.Equal(new EventDefinitionId(expectedDefinition), result.SelectedDefinitionId);
        Assert.Equal(new EventDefinitionId(expectedDefinition), Assert.Single(context.Events.EnumerateInstances()).DefinitionId);
    }

    [Fact]
    public void SameRandomSequenceProducesSameDefinitionSequence()
    {
        var first = RunDefinitionSequence();
        var second = RunDefinitionSequence();

        Assert.Equal(first, second);
        Assert.Equal(
            new[] { KnownEvents.BritainUndeadDisturbance, KnownEvents.BritainDisturbance },
            first
        );
    }

    [Fact]
    public void ActiveBritainEncounterBlocksBothAutomaticDefinitions()
    {
        using var context = new DualContext(0.0);
        Assert.True(context.Generator.Enable(StartUtc));
        Assert.True(context.Service.Trigger(KnownEvents.BritainUndeadDisturbance, InstanceId, StartUtc).Succeeded);

        Assert.Empty(context.Generator.GetEligibleDefinitions());
    }

    private static EventDefinitionId[] RunDefinitionSequence()
    {
        using var context = new DualContext(0.0, 0.1, 0.0, 0.2, 0.1, 0.999999, 0.2);
        Assert.True(context.Generator.Enable(StartUtc));
        var first = context.Generator.Evaluate(StartUtc.AddMinutes(5));
        Assert.True(first.TriggerResult?.Succeeded);
        Assert.True(context.Service.Complete(first.TriggerResult.Value.EventResult.Instance.Id, StartUtc.AddMinutes(6)).Succeeded);
        var second = context.Generator.Evaluate(StartUtc.AddMinutes(10));
        Assert.True(second.TriggerResult?.Succeeded);
        return [first.SelectedDefinitionId.Value, second.SelectedDefinitionId.Value];
    }

    private static EvaluationSnapshot RunEvaluation(bool throughTimer, double decision)
    {
        using var clock = new SimulationClock(StartUtc);
        using var context = new TestContext(0.0, decision, 0.25);
        Assert.True(context.Generator.Enable(clock.Now));

        DateTime evaluationUtc;
        if (throughTimer)
        {
            clock.Advance(TimeSpan.FromMinutes(5));
            evaluationUtc = clock.Now;
        }
        else
        {
            evaluationUtc = clock.Now;
            context.Generator.Evaluate(evaluationUtc);
        }

        var instance = context.Events.EnumerateInstances().SingleOrDefault();
        return new EvaluationSnapshot(
            instance != null,
            context.WorldStates.TryGetState(KnownWorldStates.Britain, out var condition) ? condition : null,
            context.Random.CallCount,
            context.Random.State,
            context.Generator.NextEvaluationUtc - evaluationUtc,
            context.Generator.TimerRunning,
            context.Service.Scheduler.TimerRunning
        );
    }

    private static void AssertDelayInRange(DateTime fromUtc, DateTime? scheduledUtc)
    {
        Assert.NotNull(scheduledUtc);
        var delay = scheduledUtc.Value - fromUtc;
        Assert.InRange(delay, AndraxiaAutoEventGenerator.MinimumDelay, AndraxiaAutoEventGenerator.MaximumDelay);
    }

    private readonly record struct EvaluationSnapshot(
        bool Triggered,
        WorldCondition? Britain,
        int RandomCalls,
        ulong RandomState,
        TimeSpan? NextDelay,
        bool GenerationTimerRunning,
        bool ExpirationTimerRunning
    );

    private static void AssertState(WorldStateStore store, WorldCondition expected)
    {
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
        Assert.Equal(expected, condition);
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(params double[] randomValues)
        {
            Events = new EventStore(KnownEvents.Definitions);
            WorldStates = new WorldStateStore(KnownWorldStates.Definitions);
            Encounter = new TestEventEncounterSpawner();
            Service = new AndraxiaEventService(Events, WorldStates, Encounter);
            Random = new SequenceAutoEventRandom(randomValues);
            Generator = new AndraxiaAutoEventGenerator(Events, WorldStates, Service, Random);
        }

        public EventStore Events { get; }
        public WorldStateStore WorldStates { get; }
        public TestEventEncounterSpawner Encounter { get; }
        public AndraxiaEventService Service { get; }
        public SequenceAutoEventRandom Random { get; }
        public AndraxiaAutoEventGenerator Generator { get; }

        public void Dispose()
        {
            Generator.StopTimer();
            Service.StopExpirationTimer();
        }
    }

    private sealed class DualContext : IDisposable
    {
        public DualContext(params double[] randomValues)
        {
            Events = new EventStore(KnownEvents.Definitions);
            var states = new WorldStateStore(KnownWorldStates.Definitions);
            Service = new AndraxiaEventService(
                Events,
                states,
                new IEventEncounterSpawner[]
                {
                    new TestEventEncounterSpawner(1),
                    new TestEventEncounterSpawner(100) { DefinitionId = KnownEvents.BritainUndeadDisturbance }
                },
                new DeterministicEncounterLocationSelector()
            );
            Generator = new AndraxiaAutoEventGenerator(
                Events,
                states,
                Service,
                new SequenceAutoEventRandom(randomValues)
            );
        }

        public EventStore Events { get; }
        public AndraxiaEventService Service { get; }
        public AndraxiaAutoEventGenerator Generator { get; }

        public void Dispose()
        {
            Generator.StopTimer();
            Service.StopExpirationTimer();
        }
    }

    internal sealed class SequenceAutoEventRandom(IEnumerable<double> values) : IAutoEventRandom
    {
        private readonly Queue<double> _values = new(values);

        public ulong State { get; set; }
        public int CallCount { get; private set; }

        public double NextDouble()
        {
            CallCount++;
            State++;
            return _values.Dequeue();
        }
    }
}
