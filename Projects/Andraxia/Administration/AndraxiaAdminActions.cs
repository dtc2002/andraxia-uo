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
        => SetPressure(owner, KnownAndraxiaRegions.Britain, token);

    internal AdminActionResult SetPressure(Mobile owner, AndraxiaRegionId regionId, string token)
    {
        if (!int.TryParse(token, out var value) || value is < 0 or > RegionalPressureStore.MaximumPressure)
        {
            return new(false, "Pressure must be an integer from 0 through 100.");
        }

        if (!pressure.Set(regionId, value, "Administrative set")) return new(false, "Unknown regional identifier.");
        var name = queries.TryRegion(regionId, out var region) ? region.DisplayName : regionId.Value;
        if (owner != null) CommandLogging.WriteLine(owner, $"set {regionId} regional pressure to {value}");
        return new(true, $"{name} pressure set to {value}.");
    }

    internal AdminActionResult SetConcern(Mobile owner, string token)
        => SetConcern(owner, KnownAndraxiaRegions.Britain, token);

    internal AdminActionResult SetConcern(Mobile owner, AndraxiaRegionId regionId, string token)
    {
        if (!RegionalConcernStore.TryParse(token, out var value))
        {
            return new(false, "Unknown regional concern token.");
        }

        if (!concern.Establish(regionId, value, "Administrative set")) return new(false, "Unknown regional identifier.");
        var name = queries.TryRegion(regionId, out var region) ? region.DisplayName : regionId.Value;
        if (owner != null) CommandLogging.WriteLine(owner, $"set {regionId} regional concern to {token}");
        return new(true, $"{name} concern set to {value}.");
    }

    internal AdminActionResult ClearConcern(Mobile owner)
        => ClearConcern(owner, KnownAndraxiaRegions.Britain);

    internal AdminActionResult ClearConcern(Mobile owner, AndraxiaRegionId regionId)
    {
        if (!concern.Clear(regionId, "Administrative clear")) return new(false, "Unknown regional identifier.");
        var name = queries.TryRegion(regionId, out var region) ? region.DisplayName : regionId.Value;
        if (owner != null) CommandLogging.WriteLine(owner, $"cleared {regionId} regional concern");
        return new(true, $"{name} concern cleared.");
    }

    internal AdminActionResult SetSecurity(Mobile owner, AndraxiaRegionId regionId, string token) =>
        SetRegionalValue(owner, regionId, token, true);

    internal AdminActionResult SetProsperity(Mobile owner, AndraxiaRegionId regionId, string token) =>
        SetRegionalValue(owner, regionId, token, false);

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

    private AdminActionResult SetRegionalValue(
        Mobile owner,
        AndraxiaRegionId regionId,
        string token,
        bool security
    )
    {
        var label = security ? "Security" : "Prosperity";
        if (!int.TryParse(token, out var value) || value is < 0 or > 100)
        {
            return new(false, $"{label} must be an integer from 0 through 100.");
        }
        var changed = security
            ? pressure.States.SetSecurity(regionId, value, "Administrative set")
            : pressure.States.SetProsperity(regionId, value, "Administrative set");
        if (!changed) return new(false, "Unknown regional identifier.");
        var name = queries.TryRegion(regionId, out var region) ? region.DisplayName : regionId.Value;
        if (owner != null) CommandLogging.WriteLine(owner, $"set {regionId} regional {label.ToLowerInvariant()} to {value}");
        return new(true, $"{name} {label.ToLowerInvariant()} set to {value}.");
    }
}

internal static class AdminEventExtensions
{
    internal static string DisplayId(this EventInstance instance) => instance.DefinitionId.Value;
}
