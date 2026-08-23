using System.Linq;
using Server.Commands;

namespace Server.Andraxia;

internal static class EventCommands
{
    private static AndraxiaEventService _service;
    private static EventStore _store;
    private static AndraxiaAutoEventGenerator _autoEvents;

    public static void Configure(
        AndraxiaEventService service,
        EventStore store,
        AndraxiaAutoEventGenerator autoEvents
    )
    {
        if (_service != null)
        {
            return;
        }

        _service = service;
        _store = store;
        _autoEvents = autoEvents;
        CommandSystem.Register("AndraxiaEvents", AccessLevel.Owner, ListEvents_OnCommand);
        CommandSystem.Register("AndraxiaEventTrigger", AccessLevel.Owner, TriggerEvent_OnCommand);
        CommandSystem.Register("AndraxiaEventComplete", AccessLevel.Owner, CompleteEvent_OnCommand);
        CommandSystem.Register("AndraxiaEventFail", AccessLevel.Owner, FailEvent_OnCommand);
        CommandSystem.Register("AndraxiaAutoEvents", AccessLevel.Owner, AutoEvents_OnCommand);
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
            var completed = instance.CompletedUtc is { } completedUtc
                ? $", completed {completedUtc:O}"
                : null;
            e.Mobile.SendMessage(
                $"{instance.Id}: {instance.DefinitionId}, target {instance.TargetId}, " +
                $"state {EventLifecycleTokens.GetToken(instance.State)}, started {instance.StartedUtc:O}, " +
                $"expires {instance.ExpiresUtc:O}{completed}, owned {instance.OwnedMobiles.Count}, " +
                $"remaining {instance.OwnedMobiles.Count(serial => World.FindMobile(serial) is { Deleted: false })}"
            );

            if (instance.SelectedLocationId is not { } locationId)
            {
                e.Mobile.SendMessage("  Location=- Map=- Anchor=-");
            }
            else if (KnownEncounterLocations.TryGet(locationId, out var location))
            {
                e.Mobile.SendMessage(
                    $"  Location={locationId} Map={location.Map.Name} Anchor={location.X},{location.Y},{location.Z}"
                );
            }
            else
            {
                e.Mobile.SendMessage($"  Location={locationId} Map=? Anchor=?");
            }

            if (instance.State != EventLifecycleState.Active)
            {
                continue;
            }

            foreach (var serial in instance.OwnedMobiles)
            {
                var mobile = World.FindMobile(serial, true);
                if (mobile == null)
                {
                    e.Mobile.SendMessage($"  {serial} Type=missing Map=- Pos=- Alive=? Deleted=?");
                    continue;
                }

                e.Mobile.SendMessage(
                    $"  {serial} Type={mobile.GetType().Name} Map={mobile.Map?.Name ?? "-"} " +
                    $"Pos={mobile.X},{mobile.Y},{mobile.Z} Alive={mobile.Alive} Deleted={mobile.Deleted}"
                );
            }
        }
    }

    [Usage("AndraxiaEventTrigger event.test.britain-disturbance [location-id]")]
    [Description("Triggers the approved Andraxia test event.")]
    private static void TriggerEvent_OnCommand(CommandEventArgs e)
    {
        if (e.Length is < 1 or > 2 ||
            string.IsNullOrWhiteSpace(e.GetString(0)) ||
            e.Length == 2 && string.IsNullOrWhiteSpace(e.GetString(1)))
        {
            e.Mobile.SendMessage("Usage: AndraxiaEventTrigger event.test.britain-disturbance [location-id]");
            return;
        }

        var definitionId = new EventDefinitionId(e.GetString(0));
        var result = e.Length == 2
            ? _service.Trigger(definitionId, new EncounterLocationId(e.GetString(1)))
            : _service.Trigger(definitionId);
        if (!result.Succeeded)
        {
            SendFailure(e, result);
            return;
        }

        var instance = result.EventResult.Instance;
        CommandLogging.WriteLine(
            e.Mobile,
            $"triggered Andraxia event '{instance.DefinitionId}' as '{instance.Id}' at '{instance.SelectedLocationId}'"
        );
        e.Mobile.SendMessage(
            $"Triggered {instance.DefinitionId} as {instance.Id}; state " +
            $"{EventLifecycleTokens.GetToken(instance.State)}, location {instance.SelectedLocationId}."
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

    [Usage("AndraxiaAutoEvents [on|off|evaluate]")]
    [Description("Displays, controls, or immediately evaluates automatic Andraxia event generation.")]
    private static void AutoEvents_OnCommand(CommandEventArgs e)
    {
        if (e.Length == 0)
        {
            SendAutoEventStatus(e);
            return;
        }

        if (e.Length != 1)
        {
            e.Mobile.SendMessage("Usage: AndraxiaAutoEvents [on|off|evaluate]");
            return;
        }

        var option = e.GetString(0);
        if (option.InsensitiveEquals("evaluate"))
        {
            var result = _autoEvents.Evaluate(Core.Now);
            CommandLogging.WriteLine(e.Mobile, "evaluated automatic Andraxia event generation");

            if (!result.Evaluated)
            {
                e.Mobile.SendMessage("AutoEvents evaluation: disabled.");
            }
            else if (!result.Eligible)
            {
                e.Mobile.SendMessage("AutoEvents evaluation: ineligible.");
            }
            else if (result.TriggerResult?.Succeeded == true)
            {
                e.Mobile.SendMessage("AutoEvents evaluation: triggered.");
            }
            else
            {
                e.Mobile.SendMessage("AutoEvents evaluation: eligible/no trigger.");
            }

            return;
        }

        if (option.InsensitiveEquals("on"))
        {
            if (_autoEvents.Enable(Core.Now))
            {
                CommandLogging.WriteLine(e.Mobile, "enabled automatic Andraxia event generation");
                e.Mobile.SendMessage("Automatic Andraxia events enabled.");
            }
            else
            {
                e.Mobile.SendMessage("Automatic Andraxia events are already enabled.");
            }
        }
        else if (option.InsensitiveEquals("off"))
        {
            if (_autoEvents.Disable())
            {
                CommandLogging.WriteLine(e.Mobile, "disabled automatic Andraxia event generation");
                e.Mobile.SendMessage("Automatic Andraxia events disabled.");
            }
            else
            {
                e.Mobile.SendMessage("Automatic Andraxia events are already disabled.");
            }
        }
        else
        {
            e.Mobile.SendMessage("Usage: AndraxiaAutoEvents [on|off|evaluate]");
            return;
        }

        SendAutoEventStatus(e);
    }

    private static void SendAutoEventStatus(CommandEventArgs e)
    {
        var next = _autoEvents.NextEvaluationUtc is { } nextUtc ? nextUtc.ToString("O") : "-";
        e.Mobile.SendMessage(
            $"AutoEvents: {(_autoEvents.Enabled ? "enabled" : "disabled")}, next {next}"
        );
        e.Mobile.SendMessage(
            $"Delay {AndraxiaAutoEventGenerator.MinimumDelay.TotalMinutes:0}-" +
            $"{AndraxiaAutoEventGenerator.MaximumDelay.TotalMinutes:0}m, " +
            $"chance {AndraxiaAutoEventGenerator.TriggerProbability:P0}"
        );
    }
}
