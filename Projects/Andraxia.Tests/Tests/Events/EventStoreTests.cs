using System;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

public class EventStoreTests
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly EventInstanceId FirstId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly EventInstanceId SecondId = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public void KnownDefinitionTriggersActiveInstanceWithExplicitId()
    {
        var store = CreateStore();

        var result = store.Trigger(KnownEvents.BritainDisturbance, FirstId, StartUtc);

        Assert.True(result.Succeeded);
        Assert.Equal(EventTransitionFailure.None, result.Failure);
        Assert.Equal(FirstId, result.Instance.Id);
        Assert.Equal(KnownEvents.BritainDisturbance, result.Instance.DefinitionId);
        Assert.Equal(KnownEvents.Britain, result.Instance.TargetId);
        Assert.Equal(EventLifecycleState.Active, result.Instance.State);
        Assert.Equal(StartUtc, result.Instance.StartedUtc);
        Assert.Equal(StartUtc + TimeSpan.FromMinutes(5), result.Instance.ExpiresUtc);
        Assert.Null(result.Instance.CompletedUtc);
        Assert.Equal("11111111111111111111111111111111", result.Instance.Id.ToString());
    }

    [Fact]
    public void UnknownDefinitionIsRejectedWithoutMutation()
    {
        var store = CreateStore();

        var result = store.Trigger(new EventDefinitionId("event.unknown"), FirstId, StartUtc);

        Assert.False(result.Succeeded);
        Assert.Equal(EventTransitionFailure.UnknownDefinition, result.Failure);
        Assert.Empty(store.EnumerateInstances());
    }

    [Fact]
    public void DuplicateActiveDefinitionOrTargetIsRejected()
    {
        var store = CreateStore();
        Assert.True(store.Trigger(KnownEvents.BritainDisturbance, FirstId, StartUtc).Succeeded);

        var result = store.Trigger(KnownEvents.BritainDisturbance, SecondId, StartUtc);

        Assert.False(result.Succeeded);
        Assert.Equal(EventTransitionFailure.DuplicateActiveDefinitionOrTarget, result.Failure);
        Assert.Single(store.EnumerateInstances());
        Assert.False(store.TryGetInstance(SecondId, out _));
    }

    [Theory]
    [InlineData(EventLifecycleState.Succeeded)]
    [InlineData(EventLifecycleState.Failed)]
    public void ActiveInstanceCanEnterTerminalState(EventLifecycleState terminalState)
    {
        var store = CreateStore();
        Assert.True(store.Trigger(KnownEvents.BritainDisturbance, FirstId, StartUtc).Succeeded);

        var completedUtc = StartUtc.AddMinutes(1);
        var result = terminalState == EventLifecycleState.Succeeded
            ? store.Complete(FirstId, completedUtc)
            : store.Fail(FirstId, completedUtc);

        Assert.True(result.Succeeded);
        Assert.Equal(EventLifecycleState.Active, result.PreviousState);
        Assert.Equal(terminalState, result.RequestedState);
        Assert.Equal(terminalState, result.Instance.State);
        Assert.Equal(completedUtc, result.Instance.CompletedUtc);
    }

    [Fact]
    public void SameStateAndTerminalTransitionsAreRejected()
    {
        var store = CreateStore();
        Assert.True(store.Trigger(KnownEvents.BritainDisturbance, FirstId, StartUtc).Succeeded);
        Assert.True(store.Complete(FirstId, StartUtc.AddMinutes(1)).Succeeded);

        var same = store.Complete(FirstId, StartUtc.AddMinutes(2));
        var terminal = store.Fail(FirstId, StartUtc.AddMinutes(2));

        Assert.Equal(EventTransitionFailure.SameState, same.Failure);
        Assert.Equal(EventTransitionFailure.TerminalInstance, terminal.Failure);
        Assert.True(store.TryGetInstance(FirstId, out var instance));
        Assert.Equal(EventLifecycleState.Succeeded, instance.State);
    }

    [Fact]
    public void UnknownInstanceIsRejected()
    {
        var store = CreateStore();

        var result = store.Complete(FirstId, StartUtc);

        Assert.False(result.Succeeded);
        Assert.Equal(EventTransitionFailure.UnknownInstance, result.Failure);
        Assert.Empty(store.EnumerateInstances());
    }

    [Fact]
    public void NonUtcLifecycleTimeIsRejectedAsProgrammerError()
    {
        var store = CreateStore();

        Assert.Throws<ArgumentException>(
            () => store.Trigger(KnownEvents.BritainDisturbance, FirstId, DateTime.SpecifyKind(StartUtc, DateTimeKind.Local))
        );
    }

    [Theory]
    [InlineData("active", EventLifecycleState.Active)]
    [InlineData("succeeded", EventLifecycleState.Succeeded)]
    [InlineData("failed", EventLifecycleState.Failed)]
    public void StableLifecycleTokensRoundTrip(string token, EventLifecycleState expected)
    {
        Assert.True(EventLifecycleTokens.TryParse(token, out var state));
        Assert.Equal(expected, state);
        Assert.Equal(token, EventLifecycleTokens.GetToken(state));
    }

    [Fact]
    public void UnknownLifecycleTokenIsRejected() =>
        Assert.False(EventLifecycleTokens.TryParse("unknown", out _));

    [Fact]
    public void ExplicitInstanceIdParsesOnlyStableFormat()
    {
        Assert.True(EventInstanceId.TryParse(FirstId.ToString(), out var parsed));
        Assert.Equal(FirstId, parsed);
        Assert.False(EventInstanceId.TryParse(FirstId.Value.ToString("D"), out _));
        Assert.False(EventInstanceId.TryParse(Guid.Empty.ToString("N"), out _));
    }

    private static EventStore CreateStore() => new(KnownEvents.Definitions);
}
