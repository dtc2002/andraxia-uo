using System.Collections.Generic;
using Server;
using Server.Andraxia;

namespace Andraxia.Tests;

internal sealed class TestEventEncounterSpawner(uint firstSerial = 1) : IEventEncounterSpawner
{
    private uint _nextSerial = firstSerial;

    public bool SpawnSucceeds { get; set; } = true;
    public int SpawnBeforeFailure { get; set; }
    public HashSet<Serial> Existing { get; } = [];
    public List<Serial> Deleted { get; } = [];
    public EncounterLocation SelectedLocation { get; private set; }
    public int RequestedEncounterSize { get; private set; }
    public List<Point3D> SpawnedPositions { get; } = [];
    public EventDefinitionId DefinitionId { get; init; } = KnownEvents.BritainDisturbance;
    public IReadOnlyList<EncounterLocation> Locations => KnownEncounterLocations.GetForDefinition(DefinitionId);
    public bool TrySpawn(
        EncounterLocation location,
        int encounterSize,
        ICollection<Serial> spawned,
        out string failure
    )
    {
        SelectedLocation = location;
        RequestedEncounterSize = encounterSize;
        var count = SpawnSucceeds ? encounterSize : SpawnBeforeFailure;
        for (var i = 0; i < count; i++)
        {
            var serial = (Serial)_nextSerial++;
            spawned.Add(serial);
            Existing.Add(serial);
            SpawnedPositions.Add(
                new Point3D(
                    location.X + EncounterFormation.Offsets[i].X,
                    location.Y + EncounterFormation.Offsets[i].Y,
                    location.Z + EncounterFormation.Offsets[i].Z
                )
            );
        }

        failure = SpawnSucceeds ? null : "Test spawn failure";
        return SpawnSucceeds;
    }

    public void Delete(Serial serial)
    {
        Deleted.Add(serial);
        Existing.Remove(serial);
    }

    public bool Exists(Serial serial) => Existing.Contains(serial);
}
