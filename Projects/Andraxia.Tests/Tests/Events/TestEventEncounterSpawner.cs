using System.Collections.Generic;
using Server;
using Server.Andraxia;

namespace Andraxia.Tests;

internal sealed class TestEventEncounterSpawner : IEventEncounterSpawner
{
    private uint _nextSerial = 1;

    public bool SpawnSucceeds { get; set; } = true;
    public int SpawnBeforeFailure { get; set; }
    public HashSet<Serial> Existing { get; } = [];
    public List<Serial> Deleted { get; } = [];
    public EncounterLocation SelectedLocation { get; private set; }
    public List<Point3D> SpawnedPositions { get; } = [];

    public bool TrySpawn(EncounterLocation location, ICollection<Serial> spawned, out string failure)
    {
        SelectedLocation = location;
        var count = SpawnSucceeds ? BritainBrigandEncounter.EncounterSize : SpawnBeforeFailure;
        for (var i = 0; i < count; i++)
        {
            var serial = (Serial)_nextSerial++;
            spawned.Add(serial);
            Existing.Add(serial);
            SpawnedPositions.Add(
                i switch
                {
                    0 => location.Anchor,
                    1 => new Point3D(location.X + 3, location.Y + 2, location.Z),
                    _ => new Point3D(location.X + 6, location.Y, location.Z)
                }
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
