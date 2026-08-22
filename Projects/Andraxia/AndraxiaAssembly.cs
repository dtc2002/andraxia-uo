namespace Server.Andraxia;

/// <summary>
/// Identifies the separately loaded Andraxia extension assembly.
/// </summary>
public static class AndraxiaAssembly
{
    private static AndraxiaWorldStatePersistence _worldStatePersistence;
    private static AndraxiaEventPersistence _eventPersistence;

    internal static WorldStateStore WorldStates { get; private set; }
    internal static EventStore Events { get; private set; }
    internal static AndraxiaEventService EventService { get; private set; }

    public static void Configure()
    {
        if (_worldStatePersistence != null)
        {
            return;
        }

        WorldStates = new WorldStateStore(KnownWorldStates.Definitions);
        _worldStatePersistence = new AndraxiaWorldStatePersistence(WorldStates);
        Events = new EventStore(KnownEvents.Definitions);
        EventService = new AndraxiaEventService(Events, WorldStates);
        _eventPersistence = new AndraxiaEventPersistence(Events, WorldStates, EventService);
        WorldStateCommands.Configure(WorldStates);
        EventCommands.Configure(EventService, Events);
    }
}
