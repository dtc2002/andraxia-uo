using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Server.Andraxia;

public static class KnownEvents
{
    public static readonly EventDefinitionId BritainDisturbance = new("event.test.britain-disturbance");
    public static readonly EventDefinitionId BritainUndeadDisturbance = new("event.britain.undead-disturbance");
    public static readonly EventDefinitionId BritainOrcRaidingParty = new("event.britain.orc-raiding-party");
    public static readonly EventDefinitionId BritainBeastOutbreak = new("event.britain.beast-outbreak");
    public static readonly EventDefinitionId BritainCaravanAmbush = new("event.britain.caravan-ambush");
    public static readonly EventTargetId Britain = new("region.britain");

    private static readonly ReadOnlyCollection<EventDefinition> _definitions = new(
        [
            new EventDefinition(
                BritainDisturbance,
                Britain,
                TimeSpan.FromMinutes(5),
                "Britain Brigand Disturbance",
                "Brigands are disrupting travel around Britain.",
                "Word spreads of lawlessness near Britain.",
                "Reports suggest the danger near Britain has passed.",
                "The disturbance near Britain appears to have subsided.",
                "500 gold for meaningful participation",
                category: EventCategory.Banditry
            ),
            new EventDefinition(
                BritainUndeadDisturbance,
                Britain,
                TimeSpan.FromMinutes(5),
                "Britain Undead Disturbance",
                "Restless dead have been reported around Britain.",
                "Uneasy reports speak of strange activity near Britain.",
                "Reports suggest the danger near Britain has passed.",
                "The disturbance near Britain appears to have subsided.",
                "500 gold for meaningful participation",
                category: EventCategory.Undead
            ),
            new EventDefinition(
                BritainOrcRaidingParty, Britain, TimeSpan.FromMinutes(5), "Britain Orc Raiding Party",
                "Orc raiders threaten travel around Britain.", "Reports of raiders are spreading near Britain.",
                "Reports suggest the raiders near Britain have been defeated.",
                "The raiders near Britain appear to have dispersed.", "500 gold for meaningful participation",
                category: EventCategory.Raiders
            ),
            new EventDefinition(
                BritainBeastOutbreak, Britain, TimeSpan.FromMinutes(5), "Britain Beast Outbreak",
                "Dangerous beasts roam the countryside around Britain.",
                "Travelers warn of danger in the countryside near Britain.",
                "Reports suggest the countryside near Britain is safer.",
                "The danger in the countryside appears to have subsided.", "500 gold for meaningful participation",
                category: EventCategory.Beasts
            ),
            new EventDefinition(
                BritainCaravanAmbush, Britain, TimeSpan.FromMinutes(5), "Britain Caravan Ambush",
                "A caravan is under attack near Britain.", "Word spreads of trouble along the roads near Britain.",
                "Reports say the threatened caravan survived.", "The caravan near Britain has been lost.",
                "500 gold for meaningful participation", EventObjectiveKind.ProtectTargetAndClearHostiles,
                EventCategory.Distress
            )
        ]
    );

    private static readonly ReadOnlyCollection<EventDefinitionId> _automaticDefinitions = new(
        [BritainBeastOutbreak, BritainCaravanAmbush, BritainOrcRaidingParty, BritainUndeadDisturbance, BritainDisturbance]
    );

    public static IReadOnlyList<EventDefinition> Definitions => _definitions;
    public static IReadOnlyList<EventDefinitionId> AutomaticDefinitions => _automaticDefinitions;
}
