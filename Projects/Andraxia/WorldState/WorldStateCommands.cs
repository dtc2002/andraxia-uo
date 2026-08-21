using Server.Commands;

namespace Server.Andraxia;

internal static class WorldStateCommands
{
    private static WorldStateStore _store;

    public static void Configure(WorldStateStore store)
    {
        if (_store != null)
        {
            return;
        }

        _store = store;
        CommandSystem.Register("AndraxiaState", AccessLevel.Administrator, InspectState_OnCommand);
        CommandSystem.Register("AndraxiaStateReset", AccessLevel.Administrator, ResetState_OnCommand);
    }

    [Usage("AndraxiaState [stable-id]")]
    [Description("Displays registered Andraxia persistent world state.")]
    private static void InspectState_OnCommand(CommandEventArgs e)
    {
        if (e.Length > 1)
        {
            e.Mobile.SendMessage("Usage: AndraxiaState [stable-id]");
            return;
        }

        if (e.Length == 1)
        {
            var id = new WorldStateId(e.GetString(0));
            if (!_store.TryGetState(id, out var condition))
            {
                e.Mobile.SendMessage($"Unknown Andraxia world-state identifier '{id}'.");
                return;
            }

            e.Mobile.SendMessage($"{id}: {WorldConditionTokens.GetToken(condition)}");
            return;
        }

        e.Mobile.SendMessage("--- Andraxia World State ---");
        foreach (var (id, condition) in _store.EnumerateStates())
        {
            e.Mobile.SendMessage($"{id}: {WorldConditionTokens.GetToken(condition)}");
        }
    }

    [Usage("AndraxiaStateReset <stable-id> confirm")]
    [Description("Resets one Andraxia world state to its registered default.")]
    private static void ResetState_OnCommand(CommandEventArgs e)
    {
        if (e.Length != 2 || !e.GetString(1).InsensitiveEquals("confirm"))
        {
            e.Mobile.SendMessage("Usage: AndraxiaStateReset <stable-id> confirm");
            return;
        }

        var id = new WorldStateId(e.GetString(0));
        if (!_store.TryGetState(id, out var previous))
        {
            e.Mobile.SendMessage($"Unknown Andraxia world-state identifier '{id}'.");
            return;
        }

        _store.Reset(id);
        _store.TryGetState(id, out var current);

        CommandLogging.WriteLine(
            e.Mobile,
            $"reset Andraxia world state '{id}' from '{WorldConditionTokens.GetToken(previous)}' " +
            $"to '{WorldConditionTokens.GetToken(current)}'"
        );
        e.Mobile.SendMessage(
            $"Reset {id} from {WorldConditionTokens.GetToken(previous)} to " +
            $"{WorldConditionTokens.GetToken(current)}. The next world save will persist this change."
        );
    }
}
