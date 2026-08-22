using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Server.Andraxia;

public static class KnownEvents
{
    public static readonly EventDefinitionId BritainDisturbance = new("event.test.britain-disturbance");
    public static readonly EventTargetId Britain = new("region.britain");

    private static readonly ReadOnlyCollection<EventDefinition> _definitions = new(
        [new EventDefinition(BritainDisturbance, Britain, TimeSpan.FromMinutes(5))]
    );

    public static IReadOnlyList<EventDefinition> Definitions => _definitions;
}
