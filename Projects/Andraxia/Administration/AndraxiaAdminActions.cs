using System;
using Server.Commands;

namespace Server.Andraxia;

internal readonly record struct AdminActionResult(bool Succeeded, string Message);

internal sealed class AndraxiaAdminActions(
    WorldStateStore worldStates,
    AndraxiaEventService eventService,
    AndraxiaAutoEventGenerator autoEvents,
    RegionalPressureStore pressure,
    RegionalConcernStore concern,
    AndraxiaAdminQueries queries
)
{
    internal AdminActionResult TransitionWorld(Mobile owner, WorldCondition condition)
    {
        var result = worldStates.Transition(KnownWorldStates.Britain, condition);
        if (!result.Succeeded)
        {
            return new(false, $"Invalid world-state transition: {result.Failure}.");
        }

        CommandLogging.WriteLine(owner, $"transitioned Andraxia state '{KnownWorldStates.Britain}' to '{condition}'");
        return new(true, $"Britain transitioned from {result.PreviousCondition} to {condition}.");
    }

    internal AdminActionResult ResetWorld(Mobile owner)
    {
        if (!worldStates.Reset(KnownWorldStates.Britain))
        {
            return new(false, "Britain world state is unavailable.");
        }

        CommandLogging.WriteLine(owner, $"reset Andraxia state '{KnownWorldStates.Britain}'");
        return new(true, "Britain world state reset to Normal.");
    }

    internal AdminActionResult Trigger(Mobile owner, EventDefinitionId definitionId, EncounterLocationId? locationId)
    {
        var result = locationId is { } location ? eventService.Trigger(definitionId, location) : eventService.Trigger(definitionId);
        if (!result.Succeeded)
        {
            return new(false, $"Event trigger rejected: {result.EventResult.Failure}.");
        }

        var instance = result.EventResult.Instance;
        CommandLogging.WriteLine(owner,
            $"triggered Andraxia event '{instance.DefinitionId}' as '{instance.Id}' at '{instance.SelectedLocationId}'");
        return new(true, $"Event triggered successfully: {instance.DisplayId()}.");
    }

    internal AdminActionResult TransitionEvent(Mobile owner, EventInstanceId id, EventLifecycleState requested)
    {
        var result = requested == EventLifecycleState.Succeeded ? eventService.Complete(id) : eventService.Fail(id);
        if (!result.Succeeded)
        {
            return new(false, result.EventResult.Failure is EventTransitionFailure.UnknownInstance or
                EventTransitionFailure.TerminalInstance ? "Event no longer Active." :
                $"Event transition rejected: {result.EventResult.Failure}.");
        }

        CommandLogging.WriteLine(owner,
            $"transitioned Andraxia event '{id}' to '{EventLifecycleTokens.GetToken(requested)}'");
        return new(true, $"Event transitioned to {requested}.");
    }

    internal AdminActionResult SetPressure(Mobile owner, string token)
    {
        if (!int.TryParse(token, out var value) || value is < 0 or > RegionalPressureStore.MaximumPressure)
        {
            return new(false, "Pressure must be an integer from 0 through 100.");
        }

        pressure.SetBritain(value, "Administrative set");
        CommandLogging.WriteLine(owner, $"set Britain regional pressure to {value}");
        return new(true, $"Britain pressure set to {value}.");
    }

    internal AdminActionResult SetConcern(Mobile owner, string token)
    {
        if (!RegionalConcernStore.TryParse(token, out var value))
        {
            return new(false, "Unknown regional concern token.");
        }

        concern.Establish(value, "Administrative set");
        CommandLogging.WriteLine(owner, $"set Britain regional concern to {token}");
        return new(true, $"Britain concern set to {value}.");
    }

    internal AdminActionResult ClearConcern(Mobile owner)
    {
        concern.Clear("Administrative clear");
        CommandLogging.WriteLine(owner, "cleared Britain regional concern");
        return new(true, "Britain concern cleared.");
    }

    internal AdminActionResult SetAutomation(Mobile owner, bool enabled)
    {
        var changed = enabled ? autoEvents.Enable(Core.Now) : autoEvents.Disable();
        if (!changed)
        {
            return new(false, $"Automatic events are already {(enabled ? "enabled" : "disabled")}.");
        }

        CommandLogging.WriteLine(owner, $"{(enabled ? "enabled" : "disabled")} automatic Andraxia event generation");
        return new(true, $"Automatic events {(enabled ? "enabled" : "disabled")}.");
    }

    internal AdminActionResult Evaluate(Mobile owner)
    {
        var result = autoEvents.Evaluate(Core.Now);
        CommandLogging.WriteLine(owner, "evaluated automatic Andraxia event generation");
        return new(true, !result.Evaluated ? "Evaluation: disabled." : !result.Eligible ? "Evaluation: ineligible." :
            result.TriggerResult?.Succeeded == true ? "Evaluation: triggered." : "Evaluation: eligible/no trigger.");
    }

    internal AdminActionResult GoTo(Mobile owner, EventInstanceId id)
    {
        if (!queries.TryGoTo(id, out var map, out var anchor) || map == null)
        {
            return new(false, "Event location no longer available.");
        }

        owner.MoveToWorld(anchor, map);
        return new(true, $"Moved to event on {map.Name} at {anchor.X},{anchor.Y},{anchor.Z}.");
    }
}

internal static class AdminEventExtensions
{
    internal static string DisplayId(this EventInstance instance) => instance.DefinitionId.Value;
}
