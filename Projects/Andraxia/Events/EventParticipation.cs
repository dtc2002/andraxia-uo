using System;
using System.Collections.Generic;
using System.Linq;
using Server.Items;
using Server.Logging;

namespace Server.Andraxia;

internal readonly record struct EventParticipant(Serial MobileSerial, int Damage, bool RewardDelivered);
internal readonly record struct ParticipationSnapshot(
    int TotalDamage, IReadOnlyList<EventParticipant> Participants, bool CombatCompletionEligible, bool RewardsProcessed
);

internal sealed class EventParticipationTracker
{
    internal const int RewardGold = 500;
    internal const double QualificationThreshold = 0.10;
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(EventParticipationTracker));
    private readonly Dictionary<EventInstanceId, ParticipationState> _states = [];
    private readonly EventStore _events;
    private readonly Func<Serial, int, bool> _deliver;

    internal EventParticipationTracker(EventStore events, Func<Serial, int, bool> deliver = null)
    {
        _events = events;
        _deliver = deliver ?? DeliverGold;
    }

    internal void Capture(Mobile creature)
    {
        var instance = _events.EnumerateInstances().FirstOrDefault(candidate =>
            candidate.State == EventLifecycleState.Active && candidate.OwnedMobiles.Contains(creature.Serial));
        if (instance == null)
        {
            return;
        }
        foreach (var entry in creature.DamageEntries)
        {
            if (entry.HasExpired || entry.DamageGiven <= 0)
            {
                continue;
            }
            var damager = entry.Damager?.GetDamageMaster(creature) ?? entry.Damager;
            RecordContribution(
                instance.Id,
                damager?.Serial ?? Serial.MinusOne,
                entry.DamageGiven,
                damager is { Player: true, AccessLevel: AccessLevel.Player }
            );
        }
    }

    internal IReadOnlyList<EventParticipant> Participants(EventInstanceId id) =>
        GetOrCreate(id).Participants.Values.OrderBy(static p => p.MobileSerial.Value).ToArray();

    internal bool Qualifies(EventInstanceId id, EventParticipant participant)
    {
        var total = GetOrCreate(id).TotalDamage;
        return total > 0 && participant.Damage >= total * QualificationThreshold;
    }

    internal IReadOnlyList<EventParticipant> Qualifying(EventInstanceId id) =>
        Participants(id).Where(participant => Qualifies(id, participant)).ToArray();

    internal void FinalizeCombatAndProcess(EventInstanceId id)
    {
        GetOrCreate(id).CombatCompletionEligible = true;
        ProcessPending(id);
    }

    internal void ProcessPending(EventInstanceId id)
    {
        var state = GetOrCreate(id);
        if (!state.CombatCompletionEligible)
        {
            return;
        }
        foreach (var participant in Qualifying(id).Where(static p => !p.RewardDelivered))
        {
            try
            {
                if (_deliver(participant.MobileSerial, RewardGold))
                {
                    state.Participants[participant.MobileSerial] = participant with { RewardDelivered = true };
                }
                else
                {
                    logger.Warning("Andraxia reward remains pending for Mobile {Serial}", participant.MobileSerial);
                }
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Andraxia reward delivery failed for Mobile {Serial}", participant.MobileSerial);
            }
        }
    }

    internal void CloseWithoutRewards(EventInstanceId id) => GetOrCreate(id).CombatCompletionEligible = false;

    internal void RecordContribution(EventInstanceId id, Serial serial, int damage, bool ordinaryPlayer = true)
    {
        if (damage <= 0)
        {
            return;
        }

        var state = GetOrCreate(id);
        state.TotalDamage += damage;
        if (!ordinaryPlayer)
        {
            return;
        }

        state.Participants.TryGetValue(serial, out var participant);
        state.Participants[serial] = new EventParticipant(
            serial,
            participant.Damage + damage,
            participant.RewardDelivered
        );
    }

    internal ParticipationSnapshot Get(EventInstanceId id)
    {
        var state = GetOrCreate(id);
        var qualifying = Qualifying(id);
        return new ParticipationSnapshot(
            state.TotalDamage, Participants(id), state.CombatCompletionEligible,
            !state.CombatCompletionEligible || qualifying.All(static p => p.RewardDelivered)
        );
    }

    internal void Restore(EventInstanceId id, int totalDamage, IEnumerable<EventParticipant> participants, bool eligible) =>
        _states[id] = new ParticipationState
        {
            TotalDamage = totalDamage,
            Participants = participants.ToDictionary(static p => p.MobileSerial),
            CombatCompletionEligible = eligible
        };

    internal void Clear() => _states.Clear();

    private ParticipationState GetOrCreate(EventInstanceId id)
    {
        if (!_states.TryGetValue(id, out var state))
        {
            _states[id] = state = new ParticipationState();
        }
        return state;
    }

    private static bool DeliverGold(Serial serial, int amount)
    {
        if (World.FindMobile(serial) is not { Deleted: false, Player: true, AccessLevel: AccessLevel.Player } player)
        {
            return false;
        }
        var gold = new Gold(amount);
        if (!player.PlaceInBackpack(gold))
        {
            try
            {
                var bank = player.BankBox;
                bank.DropItem(gold);
                if (gold.Deleted || gold.Parent != bank)
                {
                    gold.Delete();
                    return false;
                }
            }
            catch
            {
                gold.Delete();
                throw;
            }
        }
        player.SendMessage("Your aid in defending Britain has been recognized.");
        return true;
    }

    private sealed class ParticipationState
    {
        internal int TotalDamage;
        internal Dictionary<Serial, EventParticipant> Participants = [];
        internal bool CombatCompletionEligible;
    }
}
