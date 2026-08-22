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

    public bool TrySpawn(ICollection<Serial> spawned, out string failure)
    {
        var count = SpawnSucceeds ? BritainBrigandEncounter.EncounterSize : SpawnBeforeFailure;
        for (var i = 0; i < count; i++)
        {
            var serial = (Serial)_nextSerial++;
            spawned.Add(serial);
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
