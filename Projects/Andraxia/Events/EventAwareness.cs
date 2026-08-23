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
    private static readonly TimeSpan MaximumRumorDuration = TimeSpan.FromDays(365);
    private readonly Dictionary<EventInstanceId, List<RegisteredRumor>> _rumors = [];
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
