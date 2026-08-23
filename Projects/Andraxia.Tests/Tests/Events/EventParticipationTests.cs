using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

public sealed class EventParticipationTests
{
    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(50, true)]
    public void TenPercentEncounterDamageThresholdIsDeterministic(int damage, bool qualifies)
    {
        var store = new EventStore(KnownEvents.Definitions);
        var tracker = new EventParticipationTracker(store);
        var id = EventInstanceId.New();
        tracker.Restore(id, 100, [new EventParticipant((Serial)1u, damage, false)], true);

        Assert.Equal(qualifies ? 1 : 0, tracker.Qualifying(id).Count);
    }

    [Fact]
    public void MultipleQualifiersAreRewardedExactlyOnce()
    {
        var delivered = new List<Serial>();
        var tracker = new EventParticipationTracker(
            new EventStore(KnownEvents.Definitions),
            (serial, _) =>
            {
                delivered.Add(serial);
                return true;
            }
        );
        var id = EventInstanceId.New();
        tracker.Restore(
            id,
            100,
            [
                new EventParticipant((Serial)1u, 40, false),
                new EventParticipant((Serial)2u, 30, false),
                new EventParticipant((Serial)3u, 5, false)
            ],
            true
        );

        tracker.ProcessPending(id);
        tracker.ProcessPending(id);

        Assert.Equal(new[] { (Serial)1u, (Serial)2u }, delivered);
        Assert.True(tracker.Get(id).RewardsProcessed);
    }

    [Fact]
    public void RestoredProcessedEventNeverRewardsAgain()
    {
        var deliveries = 0;
        var tracker = new EventParticipationTracker(
            new EventStore(KnownEvents.Definitions),
            (_, _) =>
            {
                deliveries++;
                return true;
            }
        );
        var id = EventInstanceId.New();
        tracker.Restore(id, 100, [new EventParticipant((Serial)1u, 100, true)], true);

        tracker.ProcessPending(id);

        Assert.Equal(0, deliveries);
    }

    [Fact]
    public void FailedParticipantRemainsPendingWhileOthersDeliverAndRetry()
    {
        var secondSucceeds = false;
        var deliveries = new List<Serial>();
        var tracker = new EventParticipationTracker(
            new EventStore(KnownEvents.Definitions),
            (serial, _) =>
            {
                if (serial == (Serial)2u && !secondSucceeds) return false;
                deliveries.Add(serial);
                return true;
            }
        );
        var id = EventInstanceId.New();
        tracker.Restore(
            id,
            100,
            [new EventParticipant((Serial)1u, 50, false), new EventParticipant((Serial)2u, 50, false)],
            true
        );

        tracker.ProcessPending(id);
        Assert.True(tracker.Participants(id)[0].RewardDelivered);
        Assert.False(tracker.Participants(id)[1].RewardDelivered);
        secondSucceeds = true;
        tracker.ProcessPending(id);

        Assert.Equal(new[] { (Serial)1u, (Serial)2u }, deliveries);
        Assert.All(tracker.Participants(id), static participant => Assert.True(participant.RewardDelivered));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void OnePlayerAcrossEveryOwnedCreatureReceivesOneFiveHundredGoldReward(int creatureCount)
    {
        var deliveries = new List<(Serial Serial, int Gold)>();
        var tracker = new EventParticipationTracker(
            new EventStore(KnownEvents.Definitions),
            (serial, gold) =>
            {
                deliveries.Add((serial, gold));
                return true;
            }
        );
        var id = EventInstanceId.New();

        for (var creature = 0; creature < creatureCount; creature++)
        {
            tracker.RecordContribution(id, (Serial)1u, 100);
        }
        Assert.Empty(deliveries);

        tracker.FinalizeCombatAndProcess(id);

        var delivery = Assert.Single(deliveries);
        Assert.Equal((Serial)1u, delivery.Serial);
        Assert.Equal(500, delivery.Gold);
        Assert.True(Assert.Single(tracker.Participants(id)).RewardDelivered);
    }

    [Fact]
    public void TwoPlayersAcrossThreeCreaturesReceiveFiveHundredEach()
    {
        var deliveries = new List<(Serial Serial, int Gold)>();
        var tracker = new EventParticipationTracker(
            new EventStore(KnownEvents.Definitions),
            (serial, gold) =>
            {
                deliveries.Add((serial, gold));
                return true;
            }
        );
        var id = EventInstanceId.New();
        for (var creature = 0; creature < 3; creature++)
        {
            tracker.RecordContribution(id, (Serial)1u, 60);
            tracker.RecordContribution(id, (Serial)2u, 40);
        }

        tracker.FinalizeCombatAndProcess(id);

        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, static delivery => Assert.Equal(500, delivery.Gold));
        Assert.Equal(1000, deliveries.Sum(static delivery => delivery.Gold));
    }
}
