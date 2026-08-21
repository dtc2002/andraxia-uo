namespace Server.Andraxia;

/// <summary>
/// Identifies the separately loaded Andraxia extension assembly.
/// </summary>
public static class AndraxiaAssembly
{
    private static AndraxiaWorldStatePersistence _worldStatePersistence;

    internal static WorldStateStore WorldStates { get; private set; }

    public static void Configure()
    {
        if (_worldStatePersistence != null)
        {
            return;
        }

        WorldStates = new WorldStateStore(KnownWorldStates.Definitions);
        _worldStatePersistence = new AndraxiaWorldStatePersistence(WorldStates);
        WorldStateCommands.Configure(WorldStates);
    }
}
