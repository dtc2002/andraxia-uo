using Server.Commands;

namespace Server.Andraxia;

internal static class ConcernCommands
{
    private static RegionalConcernStore _store;
    internal static void Configure(RegionalConcernStore store)
    {
        if (_store != null) return; _store = store;
        CommandSystem.Register("AndraxiaConcern", AccessLevel.Owner, OnCommand);
    }
    [Usage("AndraxiaConcern [clear confirm|set <token> confirm]")]
    private static void OnCommand(CommandEventArgs e)
    {
        if (e.Length == 0)
        {
            e.Mobile.SendMessage($"Britain concern: {_store.Britain}");
            e.Mobile.SendMessage($"Description: {RegionalConcernStore.Description(_store.Britain)}");
            e.Mobile.SendMessage($"Quiet intervals: {_store.QuietIntervals}/4");
            e.Mobile.SendMessage($"Concern-biased event: {RegionalConcernMapping.Definition(_store.Britain)?.Value ?? "None"}");
            return;
        }
        if (e.Length == 2 && e.GetString(0).InsensitiveEquals("clear") && e.GetString(1).InsensitiveEquals("confirm"))
        { _store.Clear("Administrative clear"); CommandLogging.WriteLine(e.Mobile, "cleared Britain regional concern"); e.Mobile.SendMessage("Britain concern cleared."); return; }
        if (e.Length == 3 && e.GetString(0).InsensitiveEquals("set") && RegionalConcernStore.TryParse(e.GetString(1), out var concern) && e.GetString(2).InsensitiveEquals("confirm"))
        { _store.Establish(concern, "Administrative set"); CommandLogging.WriteLine(e.Mobile, $"set Britain regional concern to {concern}"); e.Mobile.SendMessage($"Britain concern set to {concern}."); return; }
        e.Mobile.SendMessage("Usage: AndraxiaConcern [clear confirm|set <token> confirm]");
    }
}
