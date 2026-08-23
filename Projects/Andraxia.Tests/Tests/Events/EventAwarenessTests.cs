using System;
using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Andraxia;
using Server.Mobiles;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class EventAwarenessTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private AndraxiaEventService _service;

    [Fact]
    public void PresentationMetadataIsCompleteAndNonPersistent()
    {
        Assert.All(
            KnownEvents.Definitions,
            static definition =>
            {
                Assert.False(string.IsNullOrWhiteSpace(definition.Description));
                Assert.False(string.IsNullOrWhiteSpace(definition.StartBroadcast));
                Assert.False(string.IsNullOrWhiteSpace(definition.SuccessBroadcast));
                Assert.False(string.IsNullOrWhiteSpace(definition.FailureBroadcast));
            }
        );
        Assert.All(
            KnownEvents.Definitions.SelectMany(static definition =>
                KnownEncounterLocations.GetForDefinition(definition.Id)),
            static location => Assert.False(string.IsNullOrWhiteSpace(location.RumorText))
        );
        Assert.Equal(4, AndraxiaEventPersistence.CurrentVersion);
    }

    [Theory]
    [InlineData("event.test.britain-disturbance", "location.britain.road-north")]
    [InlineData("event.britain.undead-disturbance", "location.britain.undead.graveyard-east")]
    public void SuccessfulActivationBroadcastsAndRegistersExactlyOnce(string definitionToken, string locationToken)
    {
        var context = CreateContext();
        var definitionId = new EventDefinitionId(definitionToken);
        var locationId = new EncounterLocationId(locationToken);

        var result = context.Service.Trigger(definitionId, EventInstanceId.New(), StartUtc, locationId);

        Assert.True(result.Succeeded);
        Assert.Single(context.Awareness.Broadcasts);
        Assert.Equal(context.Definition(definitionId).StartBroadcast, context.Awareness.Broadcasts[0]);
        var rumor = Assert.Single(context.Awareness.Rumors);
        Assert.Equal(result.EventResult.Instance.Id, rumor.Key);
        Assert.Equal(context.Location(definitionId, locationId).RumorText, rumor.Value);
    }

    [Fact]
    public void AutomaticActivationUsesTheSameSingleStartBroadcastPath()
    {
        var context = CreateContext();
        var generator = new AndraxiaAutoEventGenerator(
            context.Events,
            context.States,
            context.Service,
            new FixedAutoEventRandom(0.0)
        );
        Assert.True(generator.Enable(StartUtc));

        var result = generator.Evaluate(StartUtc.AddMinutes(5));

        Assert.True(result.TriggerResult?.Succeeded);
        Assert.Single(context.Awareness.Broadcasts);
        generator.StopTimer();
    }

    [Fact]
    public void ProductionAdapterRegistersInStockNewsCollectionAndRemovesCleanly()
    {
        var crier = new TestTownCrierEntryList();
        var awareness = new ModernUOEventAwareness(() => [crier]);
        var instanceId = EventInstanceId.New();
        try
        {
            awareness.RegisterRumor(instanceId, "Travelers bring test news.");

            Assert.True(awareness.IsRumorRegistered(instanceId));
            var entry = Assert.Single(crier.Entries);
            Assert.Equal(new[] { "Travelers bring test news." }, entry.Lines);
            Assert.False(entry.Expired);
            Assert.Same(entry, crier.GetRandomEntry());

            awareness.RemoveRumor(instanceId);
            Assert.False(awareness.IsRumorRegistered(instanceId));
            Assert.Empty(crier.Entries);
        }
        finally
        {
            awareness.RemoveRumor(instanceId);
        }
    }

    [Fact]
    public void FailedActivationPublishesNothing()
    {
        var encounter = new TestEventEncounterSpawner { SpawnSucceeds = false, SpawnBeforeFailure = 2 };
        var context = CreateContext(brigands: encounter);

        var result = context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);

        Assert.False(result.Succeeded);
        Assert.Empty(context.Awareness.Broadcasts);
        Assert.Empty(context.Awareness.Rumors);
    }

    [Fact]
    public void OnlyFinalOwnedRemovalResolvesAwareness()
    {
        var context = CreateContext();
        var result = context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);
        var owned = result.EventResult.Instance.OwnedMobiles.ToArray();

        context.Service.HandleOwnedMobileRemoved(owned[0], StartUtc.AddSeconds(1));
        context.Service.HandleOwnedMobileRemoved(owned[1], StartUtc.AddSeconds(2));
        Assert.Single(context.Awareness.Broadcasts);
        Assert.Single(context.Awareness.Rumors);

        context.Service.HandleOwnedMobileRemoved(owned[2], StartUtc.AddSeconds(3));

        Assert.Empty(context.Awareness.Rumors);
        Assert.Equal(2, context.Awareness.Broadcasts.Count);
        Assert.Equal(context.Definition(KnownEvents.BritainDisturbance).SuccessBroadcast, context.Awareness.Broadcasts[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureAndExpirationRemoveRumorAndBroadcastOnce(bool expire)
    {
        var context = CreateContext();
        var result = context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);

        if (expire)
        {
            context.Service.Advance(StartUtc.AddMinutes(5));
        }
        else
        {
            Assert.True(context.Service.Fail(result.EventResult.Instance.Id, StartUtc.AddMinutes(1)).Succeeded);
        }

        Assert.Empty(context.Awareness.Rumors);
        Assert.Equal(2, context.Awareness.Broadcasts.Count);
        Assert.Equal(context.Definition(KnownEvents.BritainDisturbance).FailureBroadcast, context.Awareness.Broadcasts[1]);
    }

    [Fact]
    public void ActiveRecoveryRestoresOneRumorWithoutStartBroadcastOrRescaling()
    {
        var initial = CreateContext();
        var result = initial.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);
        var recoveredAwareness = new TestEventAwareness();
        var countCalls = 0;
        _service = new AndraxiaEventService(
            initial.Events,
            initial.States,
            initial.Encounters,
            new DeterministicEncounterLocationSelector(),
            () =>
            {
                countCalls++;
                return 20;
            },
            recoveredAwareness
        );

        _service.RecoverOwnedMobiles(StartUtc.AddSeconds(1));
        _service.RestoreActiveRumors();
        _service.RestoreActiveRumors();

        Assert.Empty(recoveredAwareness.Broadcasts);
        Assert.Single(recoveredAwareness.Rumors);
        Assert.Equal(result.EventResult.Instance.OwnedMobiles, Assert.Single(initial.Events.EnumerateInstances()).OwnedMobiles);
        Assert.Equal(0, countCalls);
    }

    [Fact]
    public void RumorRemovalIsIsolatedByEventInstance()
    {
        var awareness = new TestEventAwareness();
        var first = EventInstanceId.New();
        var second = EventInstanceId.New();
        awareness.RegisterRumor(first, "First rumor");
        awareness.RegisterRumor(second, "Second rumor");

        awareness.RemoveRumor(first);

        Assert.DoesNotContain(first, awareness.Rumors.Keys);
        Assert.Equal("Second rumor", Assert.Single(awareness.Rumors).Value);
    }

    [Fact]
    public void TerminalAndOverdueRecoveryProduceNoAwarenessSpam()
    {
        var terminal = CreateContext();
        var completed = terminal.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);
        Assert.True(terminal.Service.Complete(completed.EventResult.Instance.Id, StartUtc.AddMinutes(1)).Succeeded);
        var recoveredTerminal = new TestEventAwareness();
        _service = NewService(terminal, recoveredTerminal);
        _service.RestoreActiveRumors();
        Assert.Empty(recoveredTerminal.Broadcasts);
        Assert.Empty(recoveredTerminal.Rumors);

        var overdue = CreateContext();
        overdue.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);
        var recoveredOverdue = new TestEventAwareness();
        _service = NewService(overdue, recoveredOverdue);
        _service.AdvanceAfterDeserialize(StartUtc.AddMinutes(6));
        _service.RestoreActiveRumors();
        Assert.Empty(recoveredOverdue.Broadcasts);
        Assert.Empty(recoveredOverdue.Rumors);
    }

    private Context CreateContext(TestEventEncounterSpawner brigands = null)
    {
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        brigands ??= new TestEventEncounterSpawner();
        var undead = new TestEventEncounterSpawner(100) { DefinitionId = KnownEvents.BritainUndeadDisturbance };
        var encounters = new IEventEncounterSpawner[] { brigands, undead };
        var awareness = new TestEventAwareness();
        _service = new AndraxiaEventService(
            events,
            states,
            encounters,
            new DeterministicEncounterLocationSelector(),
            static () => 0,
            awareness
        );
        return new Context(_service, events, states, encounters, awareness);
    }

    private AndraxiaEventService NewService(Context context, IEventAwareness awareness) => new(
        context.Events,
        context.States,
        context.Encounters,
        new DeterministicEncounterLocationSelector(),
        static () => 0,
        awareness
    );

    public void Dispose() => _service?.StopExpirationTimer();

    private sealed record Context(
        AndraxiaEventService Service,
        EventStore Events,
        WorldStateStore States,
        IReadOnlyList<IEventEncounterSpawner> Encounters,
        TestEventAwareness Awareness
    )
    {
        public EventDefinition Definition(EventDefinitionId id) =>
            KnownEvents.Definitions.Single(definition => definition.Id == id);

        public EncounterLocation Location(EventDefinitionId definitionId, EncounterLocationId locationId)
        {
            Assert.True(KnownEncounterLocations.TryGetForDefinition(definitionId, locationId, out var location));
            return location;
        }
    }

    private sealed class TestEventAwareness : IEventAwareness
    {
        public List<string> Broadcasts { get; } = [];
        public Dictionary<EventInstanceId, string> Rumors { get; } = [];

        public void Broadcast(string text) => Broadcasts.Add(text);
        public void RegisterRumor(EventInstanceId instanceId, string text) => Rumors.TryAdd(instanceId, text);
        public void RemoveRumor(EventInstanceId instanceId) => Rumors.Remove(instanceId);
        public bool IsRumorRegistered(EventInstanceId instanceId) => Rumors.ContainsKey(instanceId);
    }

    private sealed class FixedAutoEventRandom(double value) : IAutoEventRandom
    {
        public ulong State { get; set; }
        public double NextDouble() => value;
    }

    private sealed class TestTownCrierEntryList : ITownCrierEntryList
    {
        public List<TownCrierEntry> Entries { get; } = [];
        public TownCrierEntry GetRandomEntry() => Entries.SingleOrDefault();

        public TownCrierEntry AddEntry(string[] lines, TimeSpan duration)
        {
            var entry = new TownCrierEntry(lines, duration);
            Entries.Add(entry);
            return entry;
        }

        public void RemoveEntry(TownCrierEntry entry) => Entries.Remove(entry);
    }
}
