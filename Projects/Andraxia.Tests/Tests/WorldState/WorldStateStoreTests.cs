using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

public class WorldStateStoreTests
{
    [Fact]
    public void BritainDefaultsToNormal()
    {
        var store = CreateStore();

        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
        Assert.Equal(WorldCondition.Normal, condition);
    }

    [Fact]
    public void NormalToThreatenedSucceeds()
    {
        var store = CreateStore();

        var result = store.Transition(KnownWorldStates.Britain, WorldCondition.Threatened);

        Assert.True(result.Succeeded);
        Assert.Equal(WorldStateTransitionFailure.None, result.Failure);
        Assert.Equal(WorldCondition.Normal, result.PreviousCondition);
        Assert.Equal(WorldCondition.Threatened, result.RequestedCondition);
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
        Assert.Equal(WorldCondition.Threatened, condition);
    }

    [Fact]
    public void ThreatenedToNormalSucceeds()
    {
        var store = CreateStore();
        Assert.True(store.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);

        var result = store.Transition(KnownWorldStates.Britain, WorldCondition.Normal);

        Assert.True(result.Succeeded);
        Assert.Equal(WorldStateTransitionFailure.None, result.Failure);
        Assert.Equal(WorldCondition.Threatened, result.PreviousCondition);
        Assert.Equal(WorldCondition.Normal, result.RequestedCondition);
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
        Assert.Equal(WorldCondition.Normal, condition);
    }

    [Fact]
    public void ThreatenedToOccupiedFailsWithoutMutation()
    {
        var store = CreateStore();
        Assert.True(store.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);

        var result = store.Transition(KnownWorldStates.Britain, WorldCondition.Occupied);

        Assert.False(result.Succeeded);
        Assert.Equal(WorldStateTransitionFailure.TransitionNotAllowed, result.Failure);
        Assert.Equal(WorldCondition.Threatened, result.PreviousCondition);
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
        Assert.Equal(WorldCondition.Threatened, condition);
    }

    [Fact]
    public void NormalToOccupiedFailsWithoutMutation()
    {
        var store = CreateStore();

        var result = store.Transition(KnownWorldStates.Britain, WorldCondition.Occupied);

        Assert.False(result.Succeeded);
        Assert.Equal(WorldStateTransitionFailure.TransitionNotAllowed, result.Failure);
        Assert.Equal(WorldCondition.Normal, result.PreviousCondition);
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var condition));
        Assert.Equal(WorldCondition.Normal, condition);
    }

    [Fact]
    public void SameConditionTransitionIsRejected()
    {
        var store = CreateStore();

        var result = store.Transition(KnownWorldStates.Britain, WorldCondition.Normal);

        Assert.False(result.Succeeded);
        Assert.Equal(WorldStateTransitionFailure.SameCondition, result.Failure);
        Assert.Equal(WorldCondition.Normal, result.PreviousCondition);
    }

    [Fact]
    public void UnknownIdentifierIsRejectedWithoutMutation()
    {
        var store = CreateStore();
        var unknown = new WorldStateId("region.unknown");

        var result = store.Transition(unknown, WorldCondition.Threatened);

        Assert.False(result.Succeeded);
        Assert.Equal(WorldStateTransitionFailure.UnknownState, result.Failure);
        Assert.Null(result.PreviousCondition);
        Assert.False(store.TryGetState(unknown, out _));
        Assert.False(store.Reset(unknown));
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var britain));
        Assert.Equal(WorldCondition.Normal, britain);
    }

    [Fact]
    public void ResetRestoresDefaultAndIsIdempotent()
    {
        var store = CreateStore();
        Assert.True(store.Transition(KnownWorldStates.Britain, WorldCondition.Threatened).Succeeded);

        Assert.True(store.Reset(KnownWorldStates.Britain));
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var firstReset));
        Assert.Equal(WorldCondition.Normal, firstReset);

        Assert.True(store.Reset(KnownWorldStates.Britain));
        Assert.True(store.TryGetState(KnownWorldStates.Britain, out var secondReset));
        Assert.Equal(WorldCondition.Normal, secondReset);
    }

    [Theory]
    [InlineData("threatened", WorldCondition.Threatened)]
    [InlineData("occupied", WorldCondition.Occupied)]
    public void StableConditionTokensParseForAdministrativeTransitions(string token, WorldCondition expected)
    {
        Assert.True(WorldConditionTokens.TryParse(token, out var condition));
        Assert.Equal(expected, condition);
    }

    [Fact]
    public void UnknownAdministrativeConditionTokenIsRejected()
    {
        Assert.False(WorldConditionTokens.TryParse("unknown-condition", out _));
    }

    private static WorldStateStore CreateStore() => new(KnownWorldStates.Definitions);
}
