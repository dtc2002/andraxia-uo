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
    private readonly Dictionary<AndraxiaRegionId, List<RegisteredRumor>> _concernRumors = [];
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

    internal static string ConcernRumorKeyFor(AndraxiaRegionId regionId) => $"{regionId.Value}.concern";
    internal void SyncConcern(RegionalConcern concern) => SyncConcern(KnownAndraxiaRegions.Britain, concern);

    internal void SyncConcern(AndraxiaRegionId regionId, RegionalConcern concern)
    {
        RemoveConcernRumor(regionId);
        if (concern == RegionalConcern.None)
        {
            return;
        }

        var text = ConcernRumorText(regionId, concern);
        List<RegisteredRumor> registrations = [];
        foreach (var entryList in _entryLists())
        {
            registrations.Add(new RegisteredRumor(entryList, entryList.AddEntry([text], MaximumRumorDuration)));
        }
        if (registrations.Count != 0)
        {
            _concernRumors[regionId] = registrations;
        }
    }

    internal bool IsConcernRumorRegistered(AndraxiaRegionId regionId)
    {
        if (!_concernRumors.TryGetValue(regionId, out var registrations)) return false;
        var entryLists = _entryLists().ToArray();
        return entryLists.Length != 0 && entryLists.All(entryList => registrations.Any(registration =>
            registration.EntryList == entryList &&
            entryList.Entries?.Contains(registration.Entry) == true &&
            !registration.Entry.Expired));
    }

    internal bool IsConcernRumorRegistered() => IsConcernRumorRegistered(KnownAndraxiaRegions.Britain);

    private void RemoveConcernRumor(AndraxiaRegionId regionId)
    {
        if (!_concernRumors.Remove(regionId, out var registrations)) return;
        foreach (var registration in registrations)
        {
            registration.EntryList.RemoveEntry(registration.Entry);
        }
    }

    private static string ConcernRumorText(AndraxiaRegionId regionId, RegionalConcern concern)
    {
        var regionName = KnownAndraxiaRegions.Definitions.FirstOrDefault(definition => definition.Id == regionId)
            ?.DisplayName ?? regionId.Value;
        if (regionId != KnownAndraxiaRegions.Britain)
        {
            return concern switch
            {
                RegionalConcern.Banditry => $"Travelers say organized brigands remain a problem around {regionName}.",
                RegionalConcern.Undead => $"Rumors of restless dead continue to trouble {regionName}.",
                RegionalConcern.Raiders => $"Travelers say raiding parties remain active near {regionName}.",
                RegionalConcern.Beasts => $"Hunters warn that dangerous beasts remain a concern near {regionName}.",
                RegionalConcern.TradeRoutes => $"Merchants remain uneasy about the roads around {regionName}.",
                _ => throw new ArgumentOutOfRangeException(nameof(concern))
            };
        }
        return concern switch
        {
            RegionalConcern.Banditry => "Travelers say organized brigands remain a problem around Britain.",
            RegionalConcern.Undead => "Rumors of restless dead continue to trouble the lands around Britain.",
            RegionalConcern.Raiders => "Travelers say raiding parties remain active beyond Britain's walls.",
            RegionalConcern.Beasts => "Hunters warn that dangerous beasts remain a concern in the countryside.",
            RegionalConcern.TradeRoutes => "Merchants remain uneasy about the roads surrounding Britain.",
            _ => throw new ArgumentOutOfRangeException(nameof(concern))
        };
    }

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
