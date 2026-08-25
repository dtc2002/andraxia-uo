using System.Collections.Generic;
using Server;
using Server.Andraxia;

namespace Andraxia.Tests;

internal sealed class TestEventEncounterSpawner(uint firstSerial = 1) : IEventEncounterSpawner
{
    private uint _nextSerial = firstSerial;

    public bool SpawnSucceeds { get; set; } = true;
    public int SpawnBeforeFailure { get; set; }
    public int ProtectedSpawnCount { get; set; }
    public int AlliedSpawnCount { get; set; }
    public HashSet<Serial> Existing { get; } = [];
    public List<Serial> Deleted { get; } = [];
    public EncounterLocation SelectedLocation { get; private set; }
    public int RequestedEncounterSize { get; private set; }
    public EncounterSeverity RequestedSeverity { get; private set; }
    public List<Point3D> SpawnedPositions { get; } = [];
    public EventDefinitionId DefinitionId { get; init; } = KnownEvents.BritainDisturbance;
    public IReadOnlyList<EncounterLocation> Locations => KnownEncounterLocations.GetForDefinition(DefinitionId);
    public bool TrySpawn(
        EncounterLocation location,
        int encounterSize,
        EncounterSeverity severity,
        ICollection<Serial> spawned,
        ICollection<Serial> protectedMobiles,
        ICollection<Serial> alliedMobiles,
        out string failure
    )
    {
        SelectedLocation = location;
        RequestedEncounterSize = encounterSize;
        RequestedSeverity = severity;
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
        for (var i = 0; i < ProtectedSpawnCount && SpawnSucceeds; i++)
        {
            var serial = (Serial)_nextSerial++;
            spawned.Add(serial);
            protectedMobiles.Add(serial);
            Existing.Add(serial);
        }
        for (var i = 0; i < AlliedSpawnCount && SpawnSucceeds; i++)
        {
            var serial = (Serial)_nextSerial++;
            spawned.Add(serial);
            alliedMobiles.Add(serial);
            Existing.Add(serial);
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
