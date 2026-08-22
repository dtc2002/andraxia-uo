using System;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public class AndraxiaEventServiceTests
{
    private static readonly EventInstanceId FirstId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly EventInstanceId SecondId = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public void TriggerChangesBritainToThreatenedAndCreatesActiveEvent()
    {
        var (service, events, worldStates) = CreateService();

        var result = service.Trigger(KnownEvents.BritainDisturbance, FirstId);

        Assert.True(result.Succeeded);
        Assert.True(events.TryGetInstance(FirstId, out var instance));
        Assert.Equal(EventLifecycleState.Active, instance.State);
        AssertWorldState(worldStates, WorldCondition.Threatened);
    }

    [Fact]
    public void FailedEventValidationDoesNotMutateBritain()
    {
        var (service, events, worldStates) = CreateService();
        Assert.True(service.Trigger(KnownEvents.BritainDisturbance, FirstId).Succeeded);

        var result = service.Trigger(KnownEvents.BritainDisturbance, SecondId);

        Assert.False(result.Succeeded);
        Assert.Null(result.WorldStateResult);
        Assert.Equal(EventTransitionFailure.DuplicateActiveDefinitionOrTarget, result.EventResult.Failure);
        Assert.Single(events.EnumerateInstances());
        AssertWorldState(worldStates, WorldCondition.Threatened);
    }

    [Fact]
    public void RejectedWorldStateActivationDoesNotCreateEvent()
    {
        var (service, events, worldStates) = CreateService();
        Assert.True(worldStates.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);

        var result = service.Trigger(KnownEvents.BritainDisturbance, FirstId);

        Assert.False(result.Succeeded);
        Assert.Equal(WorldStateTransitionFailure.SameCondition, result.WorldStateResult?.Failure);
        Assert.Empty(events.EnumerateInstances());
        AssertWorldState(worldStates, WorldCondition.Threatened);
    }

    [Theory]
    [InlineData(EventLifecycleState.Succeeded)]
    [InlineData(EventLifecycleState.Failed)]
    public void TerminalTransitionRestoresBritainToNormal(EventLifecycleState terminalState)
    {
        var (service, events, worldStates) = CreateService();
        Assert.True(service.Trigger(KnownEvents.BritainDisturbance, FirstId).Succeeded);

        var result = terminalState == EventLifecycleState.Succeeded
            ? service.Complete(FirstId)
            : service.Fail(FirstId);

        Assert.True(result.Succeeded);
        Assert.True(events.TryGetInstance(FirstId, out var instance));
        Assert.Equal(terminalState, instance.State);
        AssertWorldState(worldStates, WorldCondition.Normal);
    }

    [Fact]
    public void RejectedCompletionWorldStateTransitionLeavesEventActive()
    {
        var (service, events, worldStates) = CreateService();
        Assert.True(service.Trigger(KnownEvents.BritainDisturbance, FirstId).Succeeded);
        Assert.True(worldStates.Transition(KnownWorldStates.Britain, WorldCondition.Normal).Succeeded);

        var result = service.Complete(FirstId);

        Assert.False(result.Succeeded);
        Assert.Equal(WorldStateTransitionFailure.SameCondition, result.WorldStateResult?.Failure);
        Assert.True(events.TryGetInstance(FirstId, out var instance));
        Assert.Equal(EventLifecycleState.Active, instance.State);
        AssertWorldState(worldStates, WorldCondition.Normal);
    }

    private static (AndraxiaEventService Service, EventStore Events, WorldStateStore WorldStates) CreateService()
    {
        var events = new EventStore(KnownEvents.Definitions);
        var worldStates = new WorldStateStore(KnownWorldStates.Definitions);
        return (new AndraxiaEventService(events, worldStates), events, worldStates);
    }

    private static void AssertWorldState(WorldStateStore store, WorldCondition expected)
    {
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
        Assert.Equal(expected, condition);
    }
}
