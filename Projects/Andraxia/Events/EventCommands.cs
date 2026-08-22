using System.Linq;
using Server.Commands;

namespace Server.Andraxia;

internal static class EventCommands
{
    private static AndraxiaEventService _service;
    private static EventStore _store;

    public static void Configure(AndraxiaEventService service, EventStore store)
    {
        if (_service != null)
        {
            return;
        }

        _service = service;
        _store = store;
        CommandSystem.Register("AndraxiaEvents", AccessLevel.Owner, ListEvents_OnCommand);
        CommandSystem.Register("AndraxiaEventTrigger", AccessLevel.Owner, TriggerEvent_OnCommand);
        CommandSystem.Register("AndraxiaEventComplete", AccessLevel.Owner, CompleteEvent_OnCommand);
        CommandSystem.Register("AndraxiaEventFail", AccessLevel.Owner, FailEvent_OnCommand);
    }

    [Usage("AndraxiaEvents")]
    [Description("Displays Andraxia event instances.")]
    private static void ListEvents_OnCommand(CommandEventArgs e)
    {
        if (e.Length != 0)
        {
            e.Mobile.SendMessage("Usage: AndraxiaEvents");
            return;
        }

        var instances = _store.EnumerateInstances().OrderBy(static instance => instance.Id.Value).ToArray();
        e.Mobile.SendMessage("--- Andraxia Events ---");

        if (instances.Length == 0)
        {
            e.Mobile.SendMessage("No event instances.");
            return;
        }

        foreach (var instance in instances)
        {
            e.Mobile.SendMessage(
                $"{instance.Id}: {instance.DefinitionId}, target {instance.TargetId}, " +
                $"state {EventLifecycleTokens.GetToken(instance.State)}"
            );
        }
    }

    [Usage("AndraxiaEventTrigger event.test.britain-disturbance")]
    [Description("Triggers the approved Andraxia test event.")]
    private static void TriggerEvent_OnCommand(CommandEventArgs e)
    {
        if (e.Length != 1 || string.IsNullOrWhiteSpace(e.GetString(0)))
        {
            e.Mobile.SendMessage("Usage: AndraxiaEventTrigger event.test.britain-disturbance");
            return;
        }

        var definitionId = new EventDefinitionId(e.GetString(0));
        var result = _service.Trigger(definitionId);
        if (!result.Succeeded)
        {
            SendFailure(e, result);
            return;
        }

        var instance = result.EventResult.Instance;
        CommandLogging.WriteLine(e.Mobile, $"triggered Andraxia event '{instance.DefinitionId}' as '{instance.Id}'");
        e.Mobile.SendMessage(
            $"Triggered {instance.DefinitionId} as {instance.Id}; state " +
            $"{EventLifecycleTokens.GetToken(instance.State)}."
        );
    }

    [Usage("AndraxiaEventComplete <instance-id> confirm")]
    [Description("Completes an active Andraxia event.")]
    private static void CompleteEvent_OnCommand(CommandEventArgs e) =>
        TransitionEvent(e, EventLifecycleState.Succeeded);

    [Usage("AndraxiaEventFail <instance-id> confirm")]
    [Description("Fails an active Andraxia event.")]
    private static void FailEvent_OnCommand(CommandEventArgs e) =>
        TransitionEvent(e, EventLifecycleState.Failed);

    private static void TransitionEvent(CommandEventArgs e, EventLifecycleState requested)
    {
        var command = requested == EventLifecycleState.Succeeded ? "AndraxiaEventComplete" : "AndraxiaEventFail";
        if (e.Length != 2 || !e.GetString(1).InsensitiveEquals("confirm"))
        {
            e.Mobile.SendMessage($"Usage: {command} <instance-id> confirm");
            return;
        }

        var idToken = e.GetString(0);
        if (!EventInstanceId.TryParse(idToken, out var instanceId))
        {
            e.Mobile.SendMessage($"Invalid Andraxia event-instance identifier '{idToken}'.");
            return;
        }

        var result = requested == EventLifecycleState.Succeeded
            ? _service.Complete(instanceId)
            : _service.Fail(instanceId);

        if (!result.Succeeded)
        {
            SendFailure(e, result);
            return;
        }

        var instance = result.EventResult.Instance;
        var stateToken = EventLifecycleTokens.GetToken(instance.State);
        CommandLogging.WriteLine(e.Mobile, $"transitioned Andraxia event '{instance.Id}' to '{stateToken}'");
        e.Mobile.SendMessage($"Transitioned {instance.Id} to {stateToken}.");
    }

    private static void SendFailure(CommandEventArgs e, AndraxiaEventResult result)
    {
        if (result.WorldStateResult is { } worldStateResult && !worldStateResult.Succeeded)
        {
            e.Mobile.SendMessage($"Rejected Andraxia event operation: world state {worldStateResult.Failure}.");
            return;
        }

        e.Mobile.SendMessage($"Rejected Andraxia event operation: {result.EventResult.Failure}.");
    }
}
