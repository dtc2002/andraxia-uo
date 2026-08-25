using Server.Commands;

namespace Server.Andraxia;

internal static class PressureCommands
{
    private static RegionalPressureStore _store;
    private static RegionalPressureStabilizer _stabilizer;

    internal static void Configure(RegionalPressureStore store, RegionalPressureStabilizer stabilizer)
    {
        if (_store != null) return;
        _store = store;
        _stabilizer = stabilizer;
        CommandSystem.Register("AndraxiaPressure", AccessLevel.Owner, OnCommand);
    }

    [Usage("AndraxiaPressure [set <0-100> confirm]")]
    private static void OnCommand(CommandEventArgs e)
    {
        if (e.Length == 0)
        {
            var classification = RegionalPressureStore.Classify(_store.Britain);
            e.Mobile.SendMessage($"Britain pressure: {_store.Britain}/100");
            e.Mobile.SendMessage($"Stability: {classification}");
            e.Mobile.SendMessage($"Auto-event probability: {RegionalPressureStore.TriggerProbability(_store.Britain):P0}");
            e.Mobile.SendMessage($"Regional concern: {AndraxiaAssembly.Concern?.Britain ?? RegionalConcern.None}");
            e.Mobile.SendMessage($"Next stabilization: {_stabilizer.NextRecoveryUtc:O}");
            var playerCount = OnlinePlayerCounter.CountOrdinaryPlayers();
            e.Mobile.SendMessage($"Ordinary players online: {playerCount}");
            e.Mobile.SendMessage($"Auto events currently eligible by population: {(playerCount > 0 ? "Yes" : "No")}");
            if (_store.LastChange is { } change)
            {
                e.Mobile.SendMessage($"Last pressure change: {change.Delta:+#;-#;0} - {change.Reason}");
            }
            return;
        }

        if (e.Length != 3 || !e.GetString(0).InsensitiveEquals("set") ||
            !int.TryParse(e.GetString(1), out var value) || value is < 0 or > 100 ||
            !e.GetString(2).InsensitiveEquals("confirm"))
        {
            e.Mobile.SendMessage("Usage: AndraxiaPressure set <0-100> confirm");
            return;
        }

        var previous = _store.Britain;
        _store.SetBritain(value, "Administrative correction");
        CommandLogging.WriteLine(e.Mobile, $"set Britain pressure from {previous} to {value}");
        e.Mobile.SendMessage($"Britain pressure set to {value}/100.");
    }
}
