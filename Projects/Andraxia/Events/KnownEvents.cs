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
            new EventDefinition(BritainDisturbance, Britain, TimeSpan.FromMinutes(5)),
            new EventDefinition(BritainUndeadDisturbance, Britain, TimeSpan.FromMinutes(5))
        ]
    );

    private static readonly ReadOnlyCollection<EventDefinitionId> _automaticDefinitions = new(
        [BritainUndeadDisturbance, BritainDisturbance]
    );

    public static IReadOnlyList<EventDefinition> Definitions => _definitions;
    public static IReadOnlyList<EventDefinitionId> AutomaticDefinitions => _automaticDefinitions;
}
