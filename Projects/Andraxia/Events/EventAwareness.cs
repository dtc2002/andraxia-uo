using System;
using System.Collections.Generic;
using System.Linq;
using Server.Mobiles;

namespace Server.Andraxia;

internal interface IEventAwareness
{
    void Broadcast(string text);
    void RegisterRumor(EventInstanceId instanceId, string text);
    void RemoveRumor(EventInstanceId instanceId);
    bool IsRumorRegistered(EventInstanceId instanceId);
}

internal sealed class ModernUOEventAwareness : IEventAwareness
{
    internal const string ConcernRumorKey = "region.britain.concern";
    private static readonly TimeSpan MaximumRumorDuration = TimeSpan.FromDays(365);
    private readonly Dictionary<EventInstanceId, List<RegisteredRumor>> _rumors = [];
    private List<RegisteredRumor> _concernRumors = [];
    private readonly Func<IEnumerable<ITownCrierEntryList>> _entryLists;

    internal ModernUOEventAwareness() : this(static () =>
        TownCrier.Instances.Where(static crier => !crier.Deleted).Cast<ITownCrierEntryList>())
    {
    }

    internal ModernUOEventAwareness(Func<IEnumerable<ITownCrierEntryList>> entryLists) =>
        _entryLists = entryLists;

    public void Broadcast(string text) => World.Broadcast(0x35, true, text);

    public void RegisterRumor(EventInstanceId instanceId, string text)
    {
        if (IsRumorRegistered(instanceId) || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        RemoveRumor(instanceId);
        List<RegisteredRumor> registrations = [];
        foreach (var entryList in _entryLists())
        {
            registrations.Add(new RegisteredRumor(entryList, entryList.AddEntry([text], MaximumRumorDuration)));
        }

        if (registrations.Count != 0)
        {
            _rumors[instanceId] = registrations;
        }
    }

    public void RemoveRumor(EventInstanceId instanceId)
    {
        if (_rumors.Remove(instanceId, out var registrations))
        {
            foreach (var registration in registrations)
            {
                registration.EntryList.RemoveEntry(registration.Entry);
            }
        }
    }

    public bool IsRumorRegistered(EventInstanceId instanceId)
    {
        if (!_rumors.TryGetValue(instanceId, out var registrations))
        {
            return false;
        }

        var entryLists = _entryLists().ToArray();
        return entryLists.Length != 0 && entryLists.All(entryList => registrations.Any(registration =>
            registration.EntryList == entryList &&
            entryList.Entries?.Contains(registration.Entry) == true &&
            !registration.Entry.Expired));
    }

    internal void SyncConcern(RegionalConcern concern)
    {
        RemoveConcernRumor();
        if (concern == RegionalConcern.None)
        {
            return;
        }

        var text = ConcernRumorText(concern);
        foreach (var entryList in _entryLists())
        {
            _concernRumors.Add(new RegisteredRumor(entryList, entryList.AddEntry([text], MaximumRumorDuration)));
        }
    }

    internal bool IsConcernRumorRegistered()
    {
        var entryLists = _entryLists().ToArray();
        return entryLists.Length != 0 && entryLists.All(entryList => _concernRumors.Any(registration =>
            registration.EntryList == entryList &&
            entryList.Entries?.Contains(registration.Entry) == true &&
            !registration.Entry.Expired));
    }

    private void RemoveConcernRumor()
    {
        foreach (var registration in _concernRumors)
        {
            registration.EntryList.RemoveEntry(registration.Entry);
        }

        _concernRumors = [];
    }

    private static string ConcernRumorText(RegionalConcern concern) => concern switch
    {
        RegionalConcern.Banditry => "Travelers say organized brigands remain a problem around Britain.",
        RegionalConcern.Undead => "Rumors of restless dead continue to trouble the lands around Britain.",
        RegionalConcern.Raiders => "Travelers say raiding parties remain active beyond Britain's walls.",
        RegionalConcern.Beasts => "Hunters warn that dangerous beasts remain a concern in the countryside.",
        RegionalConcern.TradeRoutes => "Merchants remain uneasy about the roads surrounding Britain.",
        _ => throw new ArgumentOutOfRangeException(nameof(concern))
    };

    private sealed record RegisteredRumor(ITownCrierEntryList EntryList, TownCrierEntry Entry);
}

internal sealed class NullEventAwareness : IEventAwareness
{
    internal static NullEventAwareness Instance { get; } = new();

    public void Broadcast(string text)
    {
    }

    public void RegisterRumor(EventInstanceId instanceId, string text)
    {
    }

    public void RemoveRumor(EventInstanceId instanceId)
    {
    }

    public bool IsRumorRegistered(EventInstanceId instanceId) => false;
}
