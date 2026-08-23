using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Server.Andraxia;

public static class KnownEvents
{
    public static readonly EventDefinitionId BritainDisturbance = new("event.test.britain-disturbance");
    public static readonly EventDefinitionId BritainUndeadDisturbance = new("event.britain.undead-disturbance");
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
                "The disturbance near Britain appears to have subsided."
            ),
            new EventDefinition(
                BritainUndeadDisturbance,
                Britain,
                TimeSpan.FromMinutes(5),
                "Britain Undead Disturbance",
                "Restless dead have been reported around Britain.",
                "Uneasy reports speak of strange activity near Britain.",
                "Reports suggest the danger near Britain has passed.",
                "The disturbance near Britain appears to have subsided."
            )
        ]
    );

    private static readonly ReadOnlyCollection<EventDefinitionId> _automaticDefinitions = new(
        [BritainUndeadDisturbance, BritainDisturbance]
    );

    public static IReadOnlyList<EventDefinition> Definitions => _definitions;
    public static IReadOnlyList<EventDefinitionId> AutomaticDefinitions => _automaticDefinitions;
}
