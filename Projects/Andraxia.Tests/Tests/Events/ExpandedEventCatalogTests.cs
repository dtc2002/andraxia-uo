using System;
using System.Linq;
using Server;
using Server.Andraxia;
using Server.Engines.Quests.Haven;
using Server.Mobiles;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class ExpandedEventCatalogTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private AndraxiaEventService _service;
    private AndraxiaAutoEventGenerator _generator;

    [Fact]
    public void FiveDefinitionCatalogIsCompleteAndValid()
    {
        Assert.Equal(5, KnownEvents.Definitions.Count);
        Assert.Equal(5, KnownEvents.Definitions.Select(static d => d.Id).Distinct().Count());
        Assert.All(KnownEvents.Definitions, definition =>
        {
            Assert.Equal(KnownEvents.Britain, definition.TargetId);
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
            Assert.False(string.IsNullOrWhiteSpace(definition.StartBroadcast));
            Assert.NotEmpty(KnownEncounterLocations.GetForDefinition(definition.Id));
        });
        Assert.Equal(EventObjectiveKind.ProtectTargetAndClearHostiles,
            KnownEvents.Definitions.Single(d => d.Id == KnownEvents.BritainCaravanAmbush).ObjectiveKind);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void OrcAndBeastCompositionsPreserveHostileCountAtEverySeverity(int size)
    {
        foreach (var severity in Enum.GetValues<EncounterSeverity>())
        {
            Assert.Equal(size, EncounterCompositionPolicy.Orcs(size, severity).Count);
            Assert.Equal(size, EncounterCompositionPolicy.Beasts(size, severity).Count);
        }
    }

    [Theory]
    [InlineData(EncounterSeverity.Stable, 0, 0)]
    [InlineData(EncounterSeverity.Normal, 0, 0)]
    [InlineData(EncounterSeverity.Elevated, 1, 0)]
    [InlineData(EncounterSeverity.Severe, 0, 2)]
    public void OrcCompositionIsExact(EncounterSeverity severity, int lords, int mages)
    {
        foreach (var size in new[] { 3, 5, 7 })
        {
            var types = EncounterCompositionPolicy.Orcs(size, severity);
            Assert.Equal(lords, types.Count(t => t == typeof(AndraxiaEncounterOrcishLord)));
            Assert.Equal(mages, types.Count(t => t == typeof(AndraxiaEncounterOrcishMage)));
            Assert.Equal(size - lords - mages, types.Count(t => t == typeof(AndraxiaEncounterOrc)));
        }
    }

    [Theory]
    [InlineData(EncounterSeverity.Stable, 0, 0)]
    [InlineData(EncounterSeverity.Normal, 0, 0)]
    [InlineData(EncounterSeverity.Elevated, 1, 0)]
    [InlineData(EncounterSeverity.Severe, 0, 2)]
    public void BeastCompositionIsExact(EncounterSeverity severity, int direWolves, int bears)
    {
        foreach (var size in new[] { 3, 5, 7 })
        {
            var types = EncounterCompositionPolicy.Beasts(size, severity);
            Assert.Equal(direWolves, types.Count(t => t == typeof(AndraxiaEncounterDireWolf)));
            Assert.Equal(bears, types.Count(t => t == typeof(AndraxiaEncounterGrizzlyBear)));
            Assert.Equal(size - direWolves - bears, types.Count(t => t == typeof(AndraxiaEncounterGreyWolf)));
        }
    }

    [Fact]
    public void CaravanClearsOnlyAfterLastHostileWhileProtectedTargetLives()
    {
        var context = CreateCaravanContext();
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        var instance = result.EventResult.Instance;
        Assert.Equal(3, instance.HostileMobiles.Count());
        Assert.Equal(2, instance.ProtectedMobiles.Count);
        Assert.Equal(2, instance.AlliedMobiles.Count);

        foreach (var hostile in instance.HostileMobiles.ToArray()[..^1])
            context.Service.HandleOwnedMobileRemoved(hostile, StartUtc.AddSeconds(1));
        Assert.Equal(EventLifecycleState.Active, context.Events.EnumerateInstances().Single().State);

        context.Service.HandleOwnedMobileRemoved(instance.HostileMobiles.Last(), StartUtc.AddSeconds(2));
        Assert.Equal(EventLifecycleState.Succeeded, context.Events.EnumerateInstances().Single().State);
        Assert.Equal(20, context.Pressure.Britain);
    }

    [Fact]
    public void FirstProtectedTargetRemovalRemainsActiveAndSecondFails()
    {
        var concern = new RegionalConcernStore();
        var context = CreateCaravanContext(concern);
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);

        var merchants = result.EventResult.Instance.ProtectedMobiles.ToArray();
        context.Service.HandleOwnedMobileRemoved(merchants[0], StartUtc.AddSeconds(1));

        Assert.Equal(EventLifecycleState.Active, context.Events.EnumerateInstances().Single().State);

        context.Service.HandleOwnedMobileRemoved(merchants[1], StartUtc.AddSeconds(2));

        Assert.Equal(EventLifecycleState.Failed, context.Events.EnumerateInstances().Single().State);
        Assert.Equal(35, context.Pressure.Britain);
        Assert.Equal(RegionalConcern.TradeRoutes, concern.Britain);
    }

    [Fact]
    public void CaravanRecoveryFailsWhenProtectedTargetIsMissing()
    {
        var context = CreateCaravanContext();
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        foreach (var merchant in result.EventResult.Instance.ProtectedMobiles)
            context.Spawner.Existing.Remove(merchant);

        context.Service.RecoverOwnedMobiles(StartUtc.AddSeconds(1));

        Assert.Equal(EventLifecycleState.Failed, context.Events.EnumerateInstances().Single().State);
    }

    [Fact]
    public void CaravanRecoverySucceedsWhenProtectedLivesAndHostilesAreMissing()
    {
        var context = CreateCaravanContext();
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        foreach (var hostile in result.EventResult.Instance.HostileMobiles) context.Spawner.Existing.Remove(hostile);

        context.Service.RecoverOwnedMobiles(StartUtc.AddSeconds(1));

        Assert.Equal(EventLifecycleState.Succeeded, context.Events.EnumerateInstances().Single().State);
    }

    [Fact]
    public void MissingGuardsDuringRecoveryDoNotAlterObjective()
    {
        var context = CreateCaravanContext();
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        foreach (var ally in result.EventResult.Instance.AlliedMobiles)
            context.Spawner.Existing.Remove(ally);

        context.Service.RecoverOwnedMobiles(StartUtc.AddSeconds(1));

        var recovered = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Active, recovered.State);
        Assert.Empty(recovered.AlliedMobiles);
        Assert.Equal(3, recovered.HostileMobiles.Count());
        Assert.Equal(2, recovered.ProtectedMobiles.Count);
    }

    [Fact]
    public void OneMissingMerchantDuringRecoveryRemainsActive()
    {
        var context = CreateCaravanContext();
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        context.Spawner.Existing.Remove(result.EventResult.Instance.ProtectedMobiles[0]);

        context.Service.RecoverOwnedMobiles(StartUtc.AddSeconds(1));

        var recovered = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Active, recovered.State);
        Assert.Single(recovered.ProtectedMobiles);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    [InlineData(5, 7)]
    public void CaravanCompositionAddsTwoMerchantsAndTwoGuardsWithoutChangingHostileScaling(
        int players,
        int hostiles
    )
    {
        var context = CreateCaravanContext(playerCount: players);

        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        var instance = result.EventResult.Instance;

        Assert.Equal(hostiles, instance.HostileMobiles.Count());
        Assert.Equal(2, instance.ProtectedMobiles.Count);
        Assert.Equal(2, instance.AlliedMobiles.Count);
        Assert.Equal(hostiles + 4, instance.OwnedMobiles.Count);
        Assert.Equal(hostiles, instance.InitialHostileCount);
        Assert.Equal(2, instance.InitialProtectedCount);
        Assert.Equal(2, instance.InitialAlliedCount);
    }

    [Fact]
    public void CaravanFormationOffsetsAreExplicitAndNonOverlapping()
    {
        Assert.Equal([new Point3D(-1, -2, 0), new Point3D(1, -2, 0)],
            BritainCaravanEncounter.MerchantOffsets);
        Assert.Equal([new Point3D(-1, 0, 0), new Point3D(1, 0, 0)],
            BritainCaravanEncounter.GuardOffsets);
        Assert.Equal(
            [
                new Point3D(-2, 2, 0), new Point3D(0, 2, 0), new Point3D(2, 2, 0),
                new Point3D(-3, 3, 0), new Point3D(-1, 3, 0), new Point3D(1, 3, 0),
                new Point3D(3, 3, 0)
            ],
            BritainCaravanEncounter.HostileOffsets
        );
        Assert.Equal(11, BritainCaravanEncounter.MerchantOffsets
            .Concat(BritainCaravanEncounter.GuardOffsets)
            .Concat(BritainCaravanEncounter.HostileOffsets)
            .Distinct()
            .Count());
        Assert.NotEqual(BritainCaravanEncounter.CaravanTeam, BritainCaravanEncounter.AmbusherTeam);
        Assert.Equal(typeof(MilitiaFighter), typeof(AndraxiaCaravanGuard).BaseType);
        Assert.False(typeof(HireFighter).IsAssignableFrom(typeof(AndraxiaCaravanGuard)));
    }

    [Fact]
    public void GuardRemovalNeverChangesObjectiveStateOrHostileCount()
    {
        var context = CreateCaravanContext();
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        var allies = result.EventResult.Instance.AlliedMobiles.ToArray();

        context.Service.HandleOwnedMobileRemoved(allies[0], StartUtc.AddSeconds(1));
        var afterFirst = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Active, afterFirst.State);
        Assert.Equal(3, afterFirst.HostileMobiles.Count());

        context.Service.HandleOwnedMobileRemoved(allies[1], StartUtc.AddSeconds(2));
        var afterSecond = Assert.Single(context.Events.EnumerateInstances());
        Assert.Equal(EventLifecycleState.Active, afterSecond.State);
        Assert.Equal(3, afterSecond.HostileMobiles.Count());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AllHostilesClearedSucceedsWithAtLeastOneMerchant(int survivingMerchants)
    {
        var context = CreateCaravanContext();
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        var instance = result.EventResult.Instance;
        if (survivingMerchants == 1)
            context.Service.HandleOwnedMobileRemoved(instance.ProtectedMobiles[0], StartUtc.AddSeconds(1));

        foreach (var hostile in instance.HostileMobiles)
            context.Service.HandleOwnedMobileRemoved(hostile, StartUtc.AddSeconds(2));

        Assert.Equal(EventLifecycleState.Succeeded, Assert.Single(context.Events.EnumerateInstances()).State);
    }

    [Fact]
    public void OnlyHostileOwnedMobilesCanCreateParticipationCredit()
    {
        var context = CreateCaravanContext();
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        var instance = result.EventResult.Instance;
        var player = (Serial)900u;

        Assert.False(context.Service.Participation.RecordOwnedMobileContribution(
            instance.ProtectedMobiles[0], player, 100));
        Assert.False(context.Service.Participation.RecordOwnedMobileContribution(
            instance.AlliedMobiles[0], player, 100));
        Assert.Empty(context.Service.Participation.Participants(instance.Id));

        Assert.True(context.Service.Participation.RecordOwnedMobileContribution(
            instance.HostileMobiles.First(), player, 100));
        Assert.Equal(100, Assert.Single(context.Service.Participation.Participants(instance.Id)).Damage);
    }

    [Fact]
    public void GuardDamageDoesNotReceiveCreditOrDilutePlayerParticipation()
    {
        var context = CreateCaravanContext();
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        var instance = result.EventResult.Instance;
        var hostile = instance.HostileMobiles.First();
        var guard = instance.AlliedMobiles[0];
        var player = (Serial)900u;

        Assert.True(context.Service.Participation.RecordOwnedMobileContribution(hostile, guard, 900, false));
        Assert.Empty(context.Service.Participation.Participants(instance.Id));
        Assert.Equal(0, context.Service.Participation.Get(instance.Id).TotalDamage);

        Assert.True(context.Service.Participation.RecordOwnedMobileContribution(hostile, player, 100));
        var participation = context.Service.Participation.Get(instance.Id);
        Assert.Equal(100, participation.TotalDamage);
        Assert.Equal(100, Assert.Single(participation.Participants).Damage);
    }

    [Fact]
    public void CaravanCombatIsNonCriminalOnlyBetweenOpposingOwnedRoles()
    {
        var context = CreateCaravanContext();
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        var instance = result.EventResult.Instance;
        var hostile = instance.HostileMobiles.First();

        Assert.True(CaravanCombatRules.AreOpponents(hostile, instance.AlliedMobiles[0], context.Events));
        Assert.True(CaravanCombatRules.AreOpponents(instance.AlliedMobiles[0], hostile, context.Events));
        Assert.True(CaravanCombatRules.AreOpponents(hostile, instance.ProtectedMobiles[0], context.Events));
        Assert.False(CaravanCombatRules.AreOpponents(hostile, (Serial)9999u, context.Events));
        Assert.False(CaravanCombatRules.AreOpponents(instance.AlliedMobiles[0], instance.ProtectedMobiles[0], context.Events));
    }

    [Fact]
    public void TerminalCleanupDeletesOnlyRemainingOwnedCaravanEntities()
    {
        var success = CreateCaravanContext();
        var succeeded = success.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        foreach (var hostile in succeeded.EventResult.Instance.HostileMobiles)
            success.Service.HandleOwnedMobileRemoved(hostile, StartUtc.AddSeconds(1));
        Assert.Equal(4, success.Spawner.Deleted.Count);

        var expired = CreateCaravanContext();
        var active = expired.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        var unrelated = (Serial)9999u;
        expired.Spawner.Existing.Add(unrelated);
        expired.Service.Advance(StartUtc.AddMinutes(5));
        Assert.Equal(active.EventResult.Instance.OwnedMobiles.Order(), expired.Spawner.Deleted.Order());
        Assert.DoesNotContain(unrelated, expired.Spawner.Deleted);
    }

    [Fact]
    public void ObjectiveFailureCleansRemainingHostilesAndGuards()
    {
        var context = CreateCaravanContext();
        var result = context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);
        var instance = result.EventResult.Instance;
        var expectedCleanup = instance.HostileMobiles.Concat(instance.AlliedMobiles).Order().ToArray();

        context.Service.HandleOwnedMobileRemoved(instance.ProtectedMobiles[0], StartUtc.AddSeconds(1));
        context.Service.HandleOwnedMobileRemoved(instance.ProtectedMobiles[1], StartUtc.AddSeconds(2));

        Assert.Equal(EventLifecycleState.Failed, Assert.Single(context.Events.EnumerateInstances()).State);
        Assert.Equal(expectedCleanup, context.Spawner.Deleted.Order());
    }

    [Theory]
    [InlineData(0.01, "event.britain.beast-outbreak")]
    [InlineData(0.21, "event.britain.caravan-ambush")]
    [InlineData(0.41, "event.britain.orc-raiding-party")]
    [InlineData(0.61, "event.britain.undead-disturbance")]
    [InlineData(0.81, "event.test.britain-disturbance")]
    public void AutomaticGeneratorCanSelectEveryDefinition(double selection, string expected)
    {
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var handlers = KnownEvents.AutomaticDefinitions.Select((id, index) =>
            (IEventEncounterSpawner)new TestEventEncounterSpawner((uint)(index * 100 + 1)) { DefinitionId = id }).ToArray();
        _service = new AndraxiaEventService(events, states, handlers, new DeterministicEncounterLocationSelector(),
            static () => 1, NullEventAwareness.Instance);
        _generator = new AndraxiaAutoEventGenerator(events, states, _service,
            new AutoEventGenerationTests.SequenceAutoEventRandom([0.0, 0.0, selection, 0.0]));
        _generator.Enable(StartUtc);

        var result = _generator.Evaluate(StartUtc.AddMinutes(5));

        Assert.Equal(new EventDefinitionId(expected), result.SelectedDefinitionId);
        Assert.True(result.TriggerResult?.Succeeded);
    }

    private Context CreateCaravanContext(RegionalConcernStore concern = null, int playerCount = 1)
    {
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var pressure = new RegionalPressureStore();
        var spawner = new TestEventEncounterSpawner
        {
            DefinitionId = KnownEvents.BritainCaravanAmbush,
            ProtectedSpawnCount = 2,
            AlliedSpawnCount = 2
        };
        _service = new AndraxiaEventService(events, states, [spawner], new DeterministicEncounterLocationSelector(),
            () => playerCount, NullEventAwareness.Instance, pressure, concern);
        return new Context(events, _service, pressure, spawner);
    }

    public void Dispose() { _generator?.StopTimer(); _service?.StopExpirationTimer(); }
    private sealed record Context(
        EventStore Events,
        AndraxiaEventService Service,
        RegionalPressureStore Pressure,
        TestEventEncounterSpawner Spawner
    );
}
