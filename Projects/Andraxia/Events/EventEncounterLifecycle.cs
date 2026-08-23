namespace Server.Andraxia;

internal static class EventEncounterLifecycle
{
    private static AndraxiaEventService _service;

    public static void Configure(AndraxiaEventService service) => _service ??= service;

    public static void OnCreatureRemoved(Mobile creature) =>
        _service?.HandleOwnedMobileRemoved(creature.Serial, Core.Now);

    public static void OnCreatureDefeated(Mobile creature) => _service?.CaptureParticipation(creature);
}
