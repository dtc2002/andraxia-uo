using System;
using System.Linq;
using Server;
using Server.Andraxia;
using Server.Commands;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class AndraxiaAdminConsoleTests : IDisposable
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private Context _context;

    [Fact]
    public void MasterCommandContractIsOwnerOnly()
    {
        Assert.Equal("Andraxia", AndraxiaAdminConsole.CommandName);
        Assert.Equal(AccessLevel.Owner, AndraxiaAdminConsole.RequiredAccess);
        Assert.False(AndraxiaAdminConsole.CanOpen(AccessLevel.Player));
        Assert.True(AndraxiaAdminConsole.CanOpen(AccessLevel.Owner));
        AndraxiaAdminConsole.RegisterCommand();
        Assert.True(CommandSystem.Entries.TryGetValue("Andraxia", out var entry));
        Assert.Equal(AccessLevel.Owner, entry.AccessLevel);
    }

    [Fact]
    public void RegistryContainsExactlySevenInitialPanels()
    {
        Assert.Equal(
            new[] { "Overview", "World State", "Events", "Regional State", "Automation", "History", "Diagnostics" },
            AndraxiaAdminPanels.All.Select(static panel => panel.DisplayName)
        );
        Assert.Equal(7, AndraxiaAdminPanels.All.Select(static panel => panel.Id).Distinct().Count());
    }

    [Fact]
    public void OverviewQueryHandlesNoActiveEvent()
    {
        var context = CreateContext();

        Assert.Empty(context.Queries.ActiveEvents());
        Assert.Equal(WorldCondition.Normal, context.Queries.BritainCondition);
        Assert.Equal(25, context.Queries.Pressure);
    }

    [Fact]
    public void ActiveKillAllEventQueryContainsHumanReadableState()
    {
        var context = CreateContext(KnownEvents.BritainDisturbance);
        var result = context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);

        var view = Assert.Single(context.Queries.ActiveEvents());

        Assert.Equal(result.EventResult.Instance.Id, view.Id);
        Assert.Equal("Britain Brigand Disturbance", view.DisplayName);
        Assert.Equal("Eliminate the threat", view.Objective);
        Assert.Equal(3, view.TotalHostiles);
        Assert.Equal(0, view.ProtectedCount);
    }

    [Fact]
    public void CaravanQueryReportsProtectedObjective()
    {
        var context = CreateContext(KnownEvents.BritainCaravanAmbush, protectedCount: 2, alliedCount: 2);
        context.Service.Trigger(KnownEvents.BritainCaravanAmbush, EventInstanceId.New(), StartUtc);

        var view = Assert.Single(context.Queries.ActiveEvents());

        Assert.Equal(EventObjectiveKind.ProtectTargetAndClearHostiles.ToString(),
            KnownEvents.Definitions.Single(definition => definition.Id == KnownEvents.BritainCaravanAmbush).ObjectiveKind.ToString());
        Assert.Contains("Protect", view.Objective);
        Assert.Equal(2, view.ProtectedCount);
        Assert.Equal(2, view.AlliedCount);
        Assert.Equal(2, view.Entities.Count(entity => entity.Role == "Protected"));
        Assert.Equal(2, view.Entities.Count(entity => entity.Role == "Ally"));
    }

    [Fact]
    public void EventDetailAndDeletedEntityAreSafe()
    {
        var context = CreateContext(KnownEvents.BritainDisturbance);
        var result = context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);

        Assert.True(context.Queries.TryEvent(result.EventResult.Instance.Id, out var detail));
        Assert.Equal(3, detail.Entities.Count);
        Assert.All(detail.Entities, entity =>
        {
            Assert.Equal("Missing", entity.RuntimeType);
            Assert.Null(entity.Deleted);
        });
        Assert.False(context.Queries.TryEvent(EventInstanceId.New(), out _));
    }

    [Fact]
    public void DefinitionLocationsAreFilteredAndInvalidForcedLocationDoesNotMutate()
    {
        using var clock = new SimulationClock(StartUtc);
        var context = CreateContext(KnownEvents.BritainDisturbance);
        var brigandLocations = context.Queries.Locations(KnownEvents.BritainDisturbance);
        var undeadLocations = context.Queries.Locations(KnownEvents.BritainUndeadDisturbance);

        Assert.NotEmpty(brigandLocations);
        Assert.DoesNotContain(brigandLocations, location => undeadLocations.Any(other => other.Id == location.Id));

        var result = context.Actions.Trigger(
            null,
            KnownEvents.BritainDisturbance,
            KnownEncounterLocations.BritainUndeadGraveyardEast
        );

        Assert.False(result.Succeeded);
        Assert.Empty(context.Events.EnumerateInstances());
        Assert.Equal(WorldCondition.Normal, context.Queries.BritainCondition);
    }

    [Fact]
    public void GoToQueryResolvesSelectedMapAndAnchor()
    {
        var context = CreateContext(KnownEvents.BritainDisturbance);
        var result = context.Service.Trigger(
            KnownEvents.BritainDisturbance,
            EventInstanceId.New(),
            StartUtc,
            KnownEncounterLocations.BritainRoadNorth
        );

        Assert.True(context.Queries.TryGoTo(result.EventResult.Instance.Id, out var map, out var anchor));
        Assert.Same(Map.Trammel, map);
        Assert.Equal(new Point3D(1664, 1490, 0), anchor);
        Assert.False(context.Queries.TryGoTo(EventInstanceId.New(), out _, out _));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("abc")]
    public void PressureValidationRejectsInvalidValues(string token)
    {
        var context = CreateContext();
        var result = context.Actions.SetPressure(null, token);

        Assert.False(result.Succeeded);
        Assert.Equal(25, context.Queries.Pressure);
    }

    [Fact]
    public void ConcernValidationUsesStableKnownTokens()
    {
        var context = CreateContext();
        Assert.False(context.Actions.SetConcern(null, "orcish").Succeeded);
        Assert.Equal(RegionalConcern.None, context.Queries.Concern);
    }

    [Fact]
    public void RegionalViewAndAdministrativeMutationAreIsolatedBySelectedId()
    {
        var second = new AndraxiaRegionDefinition(new AndraxiaRegionId("region.test"), "Test Region");
        var context = CreateContext(regionalDefinitions: [KnownAndraxiaRegions.Definitions[0], second]);

        Assert.Equal([KnownAndraxiaRegions.Britain, second.Id],
            context.Queries.Regions.Select(static definition => definition.Id));
        Assert.True(context.Actions.SetPressure(null, second.Id, "70").Succeeded);
        Assert.True(context.Actions.SetConcern(null, second.Id, "raiders").Succeeded);
        Assert.True(context.Queries.TryRegion(second.Id, out var changed));
        Assert.True(context.Queries.TryRegion(KnownAndraxiaRegions.Britain, out var britain));
        Assert.Equal(70, changed.Pressure);
        Assert.Equal(RegionalConcern.Raiders, changed.Concern);
        Assert.Equal(25, britain.Pressure);
        Assert.Equal(RegionalConcern.None, britain.Concern);
    }

    [Fact]
    public void HistoryPaginatesNewestFirstAndHandlesEmptyStore()
    {
        var empty = CreateContext();
        Assert.Empty(empty.Queries.History(0).Entries);
        empty.Dispose();
        _context = null;

        var context = CreateContext();
        for (var i = 0; i < 12; i++)
        {
            var id = EventInstanceId.New();
            context.Events.Trigger(KnownEvents.BritainDisturbance, id, StartUtc.AddMinutes(i * 2));
            context.Events.Complete(id, StartUtc.AddMinutes(i * 2 + 1));
        }

        var first = context.Queries.History(0);
        var second = context.Queries.History(1);

        Assert.Equal(10, first.Entries.Count);
        Assert.Equal(2, second.Entries.Count);
        Assert.True(first.Entries[0].CompletedUtc > first.Entries[1].CompletedUtc);
        Assert.False(context.Queries.TryEvent(EventInstanceId.New(), out _));
    }

    [Fact]
    public void AutomationStatusReflectsNormalGeneratorState()
    {
        var context = CreateContext();

        Assert.False(context.Queries.AutomationEnabled);
        Assert.Equal(AutoEventEligibility.Disabled, context.Queries.AutomationEligibility);
        Assert.Equal(1, context.Queries.OrdinaryPlayers);
    }

    [Fact]
    public void ConfirmationActionRevalidatesTerminalEvent()
    {
        using var clock = new SimulationClock(StartUtc);
        var context = CreateContext(KnownEvents.BritainDisturbance);
        var trigger = context.Service.Trigger(KnownEvents.BritainDisturbance, EventInstanceId.New(), StartUtc);
        context.Service.Complete(trigger.EventResult.Instance.Id, StartUtc.AddMinutes(1));

        var result = context.Actions.TransitionEvent(null, trigger.EventResult.Instance.Id, EventLifecycleState.Failed);

        Assert.False(result.Succeeded);
        Assert.Equal("Event no longer Active.", result.Message);
    }

    private Context CreateContext(
        EventDefinitionId? handlerDefinition = null,
        int protectedCount = 0,
        int alliedCount = 0,
        AndraxiaRegionDefinition[] regionalDefinitions = null
    )
    {
        _context?.Dispose();
        var events = new EventStore(KnownEvents.Definitions);
        var states = new WorldStateStore(KnownWorldStates.Definitions);
        var regionalStates = new RegionalStateStore(regionalDefinitions);
        var pressure = new RegionalPressureStore(regionalStates);
        var concern = new RegionalConcernStore(regionalStates);
        var spawner = new TestEventEncounterSpawner
        {
            DefinitionId = handlerDefinition ?? KnownEvents.BritainDisturbance,
            ProtectedSpawnCount = protectedCount,
            AlliedSpawnCount = alliedCount
        };
        var service = new AndraxiaEventService(events, states, [spawner], new DeterministicEncounterLocationSelector(),
            static () => 1, NullEventAwareness.Instance, pressure, concern);
        var generator = new AndraxiaAutoEventGenerator(events, states, service, pressure: pressure, concern: concern);
        var stabilizer = new RegionalPressureStabilizer(pressure, concern);
        stabilizer.Initialize(StartUtc);
        var queries = new AndraxiaAdminQueries(states, events, service, generator, pressure, stabilizer, concern,
            static _ => null);
        var actions = new AndraxiaAdminActions(states, service, generator, pressure, concern, queries);
        return _context = new Context(events, service, generator, stabilizer, queries, actions);
    }

    public void Dispose()
    {
        _context?.Dispose();
        _context = null;
    }

    private sealed record Context(
        EventStore Events,
        AndraxiaEventService Service,
        AndraxiaAutoEventGenerator Generator,
        RegionalPressureStabilizer Stabilizer,
        AndraxiaAdminQueries Queries,
        AndraxiaAdminActions Actions
    ) : IDisposable
    {
        public void Dispose()
        {
            Generator.StopTimer();
            Stabilizer.StopTimer();
            Service.StopExpirationTimer();
        }
    }
}
