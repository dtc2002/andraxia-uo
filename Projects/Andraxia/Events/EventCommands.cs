using System.Collections.Generic;
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
        CommandSystem.Register("AndraxiaEvent", AccessLevel.Owner, EventDetail_OnCommand);
        CommandSystem.Register("AndraxiaEventTrigger", AccessLevel.Owner, TriggerEvent_OnCommand);
        CommandSystem.Register("AndraxiaEventComplete", AccessLevel.Owner, CompleteEvent_OnCommand);
        CommandSystem.Register("AndraxiaEventFail", AccessLevel.Owner, FailEvent_OnCommand);
        CommandSystem.Register("AndraxiaAutoEvents", AccessLevel.Owner, AutoEvents_OnCommand);
    }

    [Usage("AndraxiaEvents [history]")]
    [Description("Displays active Andraxia events or recent terminal history.")]
    private static void ListEvents_OnCommand(CommandEventArgs e)
    {
        string[] lines;
        if (e.Length == 0)
        {
            lines = BuildSummaryLines(_store, _service, _autoEvents);
        }
        else if (e.Length == 1 && e.GetString(0).InsensitiveEquals("history"))
        {
            lines = BuildHistoryLines(_store);
        }
        else
        {
            e.Mobile.SendMessage("Usage: AndraxiaEvents [history]");
            return;
        }

        foreach (var line in lines)
        {
            e.Mobile.SendMessage(line);
        }
    }

    [Usage("AndraxiaEvent <instance-id>")]
    [Description("Displays deep diagnostics for one Andraxia event instance.")]
    private static void EventDetail_OnCommand(CommandEventArgs e)
    {
        if (e.Length != 1 || !EventInstanceId.TryParse(e.GetString(0), out var instanceId))
        {
            e.Mobile.SendMessage("Usage: AndraxiaEvent <instance-id>");
            return;
        }

        foreach (var line in BuildDetailLines(_store, _service, instanceId))
        {
            e.Mobile.SendMessage(line);
        }
    }

    internal static string[] BuildSummaryLines(
        EventStore store,
        AndraxiaEventService service,
        AndraxiaAutoEventGenerator autoEvents
    )
    {
        List<string> lines = ["--- Active Andraxia Events ---"];
        var active = store.EnumerateInstances()
            .Where(static instance => instance.State == EventLifecycleState.Active)
            .OrderBy(static instance => instance.ExpiresUtc)
            .ThenBy(static instance => instance.Id.Value)
            .ToArray();
        if (active.Length == 0)
        {
            lines.Add("No active events.");
        }

        foreach (var instance in active)
        {
            store.TryGetDefinition(instance.DefinitionId, out var definition);
            KnownEncounterLocations.TryGet(instance.SelectedLocationId ?? default, out var location);
            lines.Add($"{definition?.DisplayName ?? "Unknown event"} [{instance.Id}]");
            lines.Add(
                $"  {EventLifecycleTokens.GetToken(instance.State)} | {location?.DisplayName ?? "Unknown location"} | " +
                $"remaining {Remaining(instance)}/{instance.OwnedMobiles.Count} | expires {instance.ExpiresUtc:O}"
            );
            lines.Add($"  Rumor: {location?.RumorText ?? "-"}");
            lines.Add($"  Town Crier registered: {(service.IsRumorRegistered(instance.Id) ? "Yes" : "No")}");
        }

        lines.Add(
            $"AutoEvents: {(autoEvents.Enabled ? "enabled" : "disabled")}, " +
            $"next {(autoEvents.NextEvaluationUtc?.ToString("O") ?? "-")}"
        );
        return lines.ToArray();
    }

    internal static string[] BuildHistoryLines(EventStore store)
    {
        List<string> lines = ["--- Recent Andraxia Event History ---"];
        var terminal = store.EnumerateInstances()
            .Where(static instance => instance.State != EventLifecycleState.Active)
            .OrderByDescending(static instance => instance.CompletedUtc)
            .ThenByDescending(static instance => instance.Id.Value)
            .ToArray();
        if (terminal.Length == 0)
        {
            lines.Add("No terminal event history.");
        }

        foreach (var instance in terminal)
        {
            store.TryGetDefinition(instance.DefinitionId, out var definition);
            lines.Add(
                $"{instance.CompletedUtc:O} | {definition?.DisplayName ?? instance.DefinitionId.Value} | " +
                $"{EventLifecycleTokens.GetToken(instance.State)} | {instance.Id}"
            );
        }

        return lines.ToArray();
    }

    internal static string[] BuildDetailLines(
        EventStore store,
        AndraxiaEventService service,
        EventInstanceId instanceId
    )
    {
        if (!store.TryGetInstance(instanceId, out var instance))
        {
            return [$"Andraxia event '{instanceId}' was not found."];
        }

        store.TryGetDefinition(instance.DefinitionId, out var definition);
        EncounterLocation location = null;
        var hasLocation = instance.SelectedLocationId is { } locationId &&
                          KnownEncounterLocations.TryGet(locationId, out location);
        List<string> lines =
        [
            $"--- {definition?.DisplayName ?? "Unknown event"} ---",
            $"Instance={instance.Id} Definition={instance.DefinitionId}",
            $"State={EventLifecycleTokens.GetToken(instance.State)} Target={instance.TargetId}",
            hasLocation
                ? $"Location={location.DisplayName} ({location.Id}) Map={location.Map?.Name ?? "-"} " +
                  $"Anchor={location.X},{location.Y},{location.Z}"
                : $"Location={instance.SelectedLocationId?.Value ?? "-"} Map=? Anchor=?",
            $"Started={instance.StartedUtc:O} Expires={instance.ExpiresUtc:O} " +
            $"Completed={instance.CompletedUtc?.ToString("O") ?? "-"}",
            $"Owned={instance.OwnedMobiles.Count} Remaining={Remaining(instance)}",
            $"Rumor: {(hasLocation ? location.RumorText : "-")}",
            $"Town Crier registered: {(service.IsRumorRegistered(instance.Id) ? "Yes" : "No")}"
        ];

        foreach (var serial in instance.OwnedMobiles)
        {
            var mobile = World.FindMobile(serial, true);
            lines.Add(
                mobile == null
                    ? $"  {serial} Type=missing Map=- Pos=- Alive=? Deleted=?"
                    : $"  {serial} Type={mobile.GetType().Name} Map={mobile.Map?.Name ?? "-"} " +
                      $"Pos={mobile.X},{mobile.Y},{mobile.Z} Alive={mobile.Alive} Deleted={mobile.Deleted}"
            );
        }

        return lines.ToArray();
    }

    private static int Remaining(EventInstance instance) =>
        instance.OwnedMobiles.Count(serial => World.FindMobile(serial) is { Deleted: false });

    [Usage("AndraxiaEventTrigger <event-definition-id> [location-id]")]
    [Description("Triggers a registered Andraxia encounter event.")]
    private static void TriggerEvent_OnCommand(CommandEventArgs e)
    {
        if (e.Length is < 1 or > 2 ||
            string.IsNullOrWhiteSpace(e.GetString(0)) ||
            e.Length == 2 && string.IsNullOrWhiteSpace(e.GetString(1)))
        {
            e.Mobile.SendMessage("Usage: AndraxiaEventTrigger <event-definition-id> [location-id]");
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
