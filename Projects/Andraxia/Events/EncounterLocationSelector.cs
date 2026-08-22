using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Andraxia;

internal interface IEncounterLocationSelector
{
    EncounterLocation Select(
        EventDefinitionId definitionId,
        EventInstanceId instanceId,
        IReadOnlyList<EncounterLocation> candidates
    );
}

internal sealed class DeterministicEncounterLocationSelector : IEncounterLocationSelector
{
    public EncounterLocation Select(
        EventDefinitionId definitionId,
        EventInstanceId instanceId,
        IReadOnlyList<EncounterLocation> candidates
    )
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one encounter location is required.", nameof(candidates));
        }

        var ordered = candidates.OrderBy(static location => location.Id.Value, StringComparer.Ordinal).ToArray();
        var input = $"{definitionId.Value}:{instanceId.Value:N}";
        var hash = 2166136261u;
        foreach (var character in input)
        {
            hash = unchecked((hash ^ character) * 16777619u);
        }

        return ordered[hash % (uint)ordered.Length];
    }
}
