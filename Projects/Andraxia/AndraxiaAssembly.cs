namespace Server.Andraxia;

/// <summary>
/// Identifies the separately loaded Andraxia extension assembly.
/// </summary>
public static class AndraxiaAssembly
{
    private static AndraxiaWorldStatePersistence _worldStatePersistence;
    private static AndraxiaEventPersistence _eventPersistence;
    private static RegionalPressurePersistence _pressurePersistence;

    internal static WorldStateStore WorldStates { get; private set; }
    internal static EventStore Events { get; private set; }
    internal static AndraxiaEventService EventService { get; private set; }
    internal static AndraxiaAutoEventGenerator AutoEvents { get; private set; }
    internal static RegionalStateStore Regions { get; private set; }
    internal static RegionalPressureStore Pressure { get; private set; }
    internal static RegionalPressureStabilizer PressureStabilizer { get; private set; }
    internal static RegionalConcernStore Concern { get; private set; }

    public static void Configure()
    {
        if (_worldStatePersistence != null)
        {
            return;
        }

        WorldStates = new WorldStateStore(KnownWorldStates.Definitions);
        _worldStatePersistence = new AndraxiaWorldStatePersistence(WorldStates);
        Events = new EventStore(KnownEvents.Definitions);
        Regions = new RegionalStateStore(KnownAndraxiaRegions.Definitions);
        Pressure = new RegionalPressureStore(Regions);
        Concern = new RegionalConcernStore(Regions);
        PressureStabilizer = new RegionalPressureStabilizer(Pressure, Concern);
        _pressurePersistence = new RegionalPressurePersistence(Pressure, PressureStabilizer, Concern);
        EventService = new AndraxiaEventService(Events, WorldStates, pressure: Pressure);
        AutoEvents = new AndraxiaAutoEventGenerator(Events, WorldStates, EventService, pressure: Pressure, concern: Concern);
        EventEncounterLifecycle.Configure(EventService);
        _eventPersistence = new AndraxiaEventPersistence(Events, WorldStates, EventService, AutoEvents);
        WorldStateCommands.Configure(WorldStates);
        EventCommands.Configure(EventService, Events, AutoEvents);
        PressureCommands.Configure(Pressure, PressureStabilizer);
        ConcernCommands.Configure(Concern);
        AndraxiaAdminConsole.Configure(
            WorldStates, Events, EventService, AutoEvents, Pressure, PressureStabilizer, Concern
        );
    }

    public static void Initialize() => AndraxiaGuardSystem.Install();
}
