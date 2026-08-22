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
        CommandSystem.Register("AndraxiaStateTransition", AccessLevel.Owner, TransitionState_OnCommand);
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

    [Usage("AndraxiaStateTransition <stable-id> <condition-token>")]
    [Description("Attempts a validated Andraxia world-state transition.")]
    private static void TransitionState_OnCommand(CommandEventArgs e)
    {
        if (e.Length != 2 || string.IsNullOrWhiteSpace(e.GetString(0)))
        {
            e.Mobile.SendMessage("Usage: AndraxiaStateTransition <stable-id> <condition-token>");
            return;
        }

        var conditionToken = e.GetString(1);
        if (!WorldConditionTokens.TryParse(conditionToken, out var requested))
        {
            e.Mobile.SendMessage($"Unknown Andraxia world-state condition token '{conditionToken}'.");
            return;
        }

        var id = new WorldStateId(e.GetString(0));
        var result = _store.Transition(id, requested);

        if (result.Succeeded)
        {
            var previousToken = WorldConditionTokens.GetToken(result.PreviousCondition!.Value);
            var requestedToken = WorldConditionTokens.GetToken(result.RequestedCondition);

            CommandLogging.WriteLine(
                e.Mobile,
                $"transitioned Andraxia world state '{id}' from '{previousToken}' to '{requestedToken}'"
            );
            e.Mobile.SendMessage($"Transitioned {id} from {previousToken} to {requestedToken}.");
            return;
        }

        if (result.Failure == WorldStateTransitionFailure.UnknownState)
        {
            e.Mobile.SendMessage($"Unknown Andraxia world-state identifier '{id}'.");
            return;
        }

        var currentToken = WorldConditionTokens.GetToken(result.PreviousCondition!.Value);
        e.Mobile.SendMessage(
            $"Rejected transition for {id} from {currentToken} to " +
            $"{WorldConditionTokens.GetToken(result.RequestedCondition)}: {result.Failure}."
        );
    }
}
