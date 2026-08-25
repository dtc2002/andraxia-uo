using System;
using System.Collections.Generic;
using System.Linq;
using Server.Commands;
using Server.Gumps;
using Server.Network;

namespace Server.Andraxia;

internal enum AndraxiaAdminConfirmation
{
    None,
    ResetWorld,
    CompleteEvent,
    FailEvent,
    SetPressure,
    SetConcern,
    ClearConcern,
    EnableAutomation,
    DisableAutomation
}

internal sealed class AndraxiaAdminGump : DynamicGump
{
    private const int LabelHue = 0x481;
    private const int MutedHue = 0x3B2;
    private readonly AndraxiaAdminQueries _queries;
    private readonly AndraxiaAdminActions _actions;
    private AndraxiaAdminPanelId _panel;
    private EventInstanceId? _detailId;
    private int _historyPage;
    private int _definitionIndex;
    private int _locationIndex = -1;
    private int _concernIndex;
    private string _status;
    private AndraxiaAdminConfirmation _confirmation;
    private EventInstanceId? _confirmationEventId;
    private string _confirmationValue;
    private EventInstanceId[] _displayedEvents = [];
    private EventInstanceId[] _displayedHistory = [];

    internal AndraxiaAdminGump(
        AndraxiaAdminQueries queries,
        AndraxiaAdminActions actions,
        AndraxiaAdminPanelId panel = AndraxiaAdminPanelId.Overview,
        string status = null
    ) : base(30, 30)
    {
        _queries = queries;
        _actions = actions;
        _panel = panel;
        _status = status;
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();
        builder.AddBackground(0, 0, 820, 600, 9270);
        builder.AddHtml(0, 14, 820, 24, "Andraxia Administration".Center("#E6C86E"));
        RenderNavigation(ref builder);
        builder.AddAlphaRegion(145, 48, 660, 500);

        if (_confirmation != AndraxiaAdminConfirmation.None)
        {
            RenderConfirmation(ref builder);
        }
        else if (_detailId is { } detailId)
        {
            RenderEventDetail(ref builder, detailId);
        }
        else
        {
            switch (_panel)
            {
                case AndraxiaAdminPanelId.Overview:
                    RenderOverview(ref builder);
                    break;
                case AndraxiaAdminPanelId.WorldState:
                    RenderWorldState(ref builder);
                    break;
                case AndraxiaAdminPanelId.Events:
                    RenderEvents(ref builder);
                    break;
                case AndraxiaAdminPanelId.RegionalState:
                    RenderRegionalState(ref builder);
                    break;
                case AndraxiaAdminPanelId.Automation:
                    RenderAutomation(ref builder);
                    break;
                case AndraxiaAdminPanelId.History:
                    RenderHistory(ref builder);
                    break;
                case AndraxiaAdminPanelId.Diagnostics:
                    RenderDiagnostics(ref builder);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(_status))
        {
            builder.AddLabel(155, 555, 0x35, Trim(_status, 92));
        }
        builder.AddButton(620, 570, 4014, 4016, 8);
        builder.AddLabel(655, 572, LabelHue, "Refresh");
        builder.AddButton(750, 570, 4017, 4019, 0);
        builder.AddLabel(785, 572, LabelHue, "Close");
    }

    private void RenderNavigation(ref DynamicGumpBuilder builder)
    {
        var y = 58;
        foreach (var panel in AndraxiaAdminPanels.All)
        {
            builder.AddButton(15, y, 4005, 4007, (int)panel.Id + 1);
            builder.AddLabel(50, y + 2, panel.Id == _panel ? 0x35 : LabelHue, panel.DisplayName);
            y += 42;
        }
    }

    private void RenderOverview(ref DynamicGumpBuilder builder)
    {
        Header(ref builder, "Britain Overview");
        var classification = _queries.PressureClassification;
        Line(ref builder, 95, $"World: {_queries.BritainCondition}    Pressure: {_queries.Pressure}/100 ({classification})");
        Line(ref builder, 117, $"Concern: {_queries.Concern}  quiet {_queries.ConcernQuietIntervals}/4");
        Line(ref builder, 139, RegionalConcernStore.Description(_queries.Concern), MutedHue);
        Line(ref builder, 170, $"Players: {_queries.OrdinaryPlayers}    Auto events: {OnOff(_queries.AutomationEnabled)}");
        Line(ref builder, 192, $"Eligibility: {_queries.AutomationEligibility}    Chance: {RegionalPressureStore.TriggerProbability(_queries.Pressure):P0}");
        Line(ref builder, 214, $"Next evaluation: {Utc(_queries.NextEvaluationUtc)}");
        Line(ref builder, 236, $"Next stabilization: {Utc(_queries.NextStabilizationUtc)}");

        var active = _queries.ActiveEvents().FirstOrDefault();
        if (active == null)
        {
            Line(ref builder, 280, "No active world event.", 0x35);
            return;
        }

        Line(ref builder, 275, active.DisplayName, 0x35);
        Line(ref builder, 297, $"{active.Category} | {active.Objective} | {active.Severity}");
        Line(ref builder, 319, $"{active.LocationName} | hostiles {active.RemainingHostiles}/{active.TotalHostiles}");
        if (active.ProtectedCount != 0)
        {
            Line(ref builder, 341, $"Protected target: {active.RemainingProtected}/{active.ProtectedCount} present");
        }
        Line(ref builder, 363, $"Expires: {Utc(active.ExpiresUtc)} | Rumor: {YesNo(active.RumorRegistered)}");
    }

    private void RenderWorldState(ref DynamicGumpBuilder builder)
    {
        Header(ref builder, "World State");
        Line(ref builder, 100, $"Stable ID: {KnownWorldStates.Britain.Value}");
        Line(ref builder, 125, $"Current condition: {_queries.BritainCondition}", 0x35);
        Button(ref builder, 175, 180, 100, "Normal -> Threatened");
        Button(ref builder, 175, 220, 101, "Threatened -> Normal");
        Button(ref builder, 175, 280, 102, "Reset to default...");
    }

    private void RenderEvents(ref DynamicGumpBuilder builder)
    {
        Header(ref builder, "Events");
        var active = _queries.ActiveEvents().Take(3).ToArray();
        _displayedEvents = active.Select(static item => item.Id).ToArray();
        var y = 88;
        if (active.Length == 0)
        {
            Line(ref builder, y, "No active world event.");
            y += 34;
        }
        foreach (var (item, index) in active.Select((item, index) => (item, index)))
        {
            Line(ref builder, y, $"{item.DisplayName} | {item.Severity} | {item.LocationName}", 0x35);
            Line(ref builder, y + 20, $"{item.Id} | hostiles {item.RemainingHostiles}/{item.TotalHostiles} | " +
                $"protected {item.RemainingProtected}/{item.ProtectedCount} | allies {item.RemainingAllies}/{item.AlliedCount}", MutedHue);
            SmallButton(ref builder, 160, y + 42, 220 + index, "Details");
            SmallButton(ref builder, 270, y + 42, 240 + index, "Go To");
            SmallButton(ref builder, 380, y + 42, 260 + index, "Complete");
            SmallButton(ref builder, 500, y + 42, 280 + index, "Fail");
            y += 78;
        }

        var definitions = _queries.Definitions;
        _definitionIndex = Math.Clamp(_definitionIndex, 0, definitions.Count - 1);
        var definition = definitions[_definitionIndex];
        var locations = _queries.Locations(definition.Id);
        _locationIndex = Math.Clamp(_locationIndex, -1, locations.Count - 1);
        Line(ref builder, 370, "Development trigger", 0x35);
        SmallButton(ref builder, 160, 396, 200, "<");
        AtLine(ref builder, 210, 398, Trim(definition.DisplayName, 55));
        SmallButton(ref builder, 665, 396, 201, ">");
        SmallButton(ref builder, 160, 426, 202, "<");
        AtLine(ref builder, 210, 428, _locationIndex < 0 ? "Automatic Location" : locations[_locationIndex].DisplayName);
        SmallButton(ref builder, 665, 426, 203, ">");
        Button(ref builder, 160, 466, 204, "Trigger");
    }

    private void RenderEventDetail(ref DynamicGumpBuilder builder, EventInstanceId id)
    {
        if (!_queries.TryEvent(id, out var item))
        {
            Header(ref builder, "Event Detail");
            Line(ref builder, 100, "Event is no longer retained.", 0x35);
            Button(ref builder, 160, 135, 301, "Back");
            return;
        }

        Header(ref builder, item.DisplayName);
        var lines = new List<string>
        {
            $"{item.DefinitionId} | {item.Id}",
            $"{item.State} | {item.Category} | {item.Objective}",
            $"Severity: {item.Severity} ({item.SeverityDescription})",
            $"Location: {item.LocationName} ({item.LocationId?.Value ?? "-"})",
            $"Map/anchor: {item.Map?.Name ?? "-"} {item.Anchor.X},{item.Anchor.Y},{item.Anchor.Z}",
            $"Started {Utc(item.StartedUtc)} | Expires {Utc(item.ExpiresUtc)}",
            $"Completed {Utc(item.CompletedUtc)} | hostiles {item.RemainingHostiles}/{item.TotalHostiles}",
            $"Protected merchants {item.RemainingProtected}/{item.ProtectedCount} | caravan guards {item.RemainingAllies}/{item.AlliedCount}",
            $"Composition: {item.Composition}",
            $"Consequence: {item.Consequence} | Town Crier: {YesNo(item.RumorRegistered)}",
            $"Rumor: {item.Rumor}"
        };
        var y = 78;
        foreach (var line in lines)
        {
            Line(ref builder, y, Trim(line, 100), y == 78 ? 0x35 : LabelHue);
            y += 19;
        }
        Line(ref builder, y + 4, "Owned entities", 0x35);
        y += 24;
        foreach (var entity in item.Entities.Take(7))
        {
            Line(ref builder, y,
                $"{entity.Role} {entity.RuntimeType} {entity.Serial} {entity.MapName} " +
                $"{entity.Location.X},{entity.Location.Y},{entity.Location.Z} A={Bool(entity.Alive)} D={Bool(entity.Deleted)}",
                MutedHue);
            y += 18;
        }
        Line(ref builder, y + 3, "Participation", 0x35);
        y += 23;
        if (item.Participants.Count == 0)
        {
            Line(ref builder, y, "None", MutedHue);
        }
        foreach (var participant in item.Participants.Take(5))
        {
            Line(ref builder, y, $"{participant.Name}: {participant.Damage} ({participant.Percentage:0.#}%) " +
                $"Q={YesNo(participant.Qualified)} Reward={participant.RewardState}", MutedHue);
            y += 18;
        }
        SmallButton(ref builder, 160, 515, 300, "Go To Event");
        SmallButton(ref builder, 315, 515, 301, "Back");
    }

    private void RenderRegionalState(ref DynamicGumpBuilder builder)
    {
        Header(ref builder, "Regional State");
        Line(ref builder, 90, $"Pressure: {_queries.Pressure}/100 ({_queries.PressureClassification})", 0x35);
        Line(ref builder, 112, RegionalPressureStore.Description(_queries.PressureClassification));
        Line(ref builder, 134, $"Trigger probability: {RegionalPressureStore.TriggerProbability(_queries.Pressure):P0}");
        Line(ref builder, 156, $"Next stabilization: {Utc(_queries.NextStabilizationUtc)}");
        Line(ref builder, 178, $"Last pressure change: {_queries.LastPressureChange}", MutedHue);
        builder.AddTextEntry(160, 210, 80, 22, LabelHue, 1, _queries.Pressure.ToString());
        Button(ref builder, 255, 208, 400, "Set Pressure...");

        var concerns = Enum.GetValues<RegionalConcern>();
        _concernIndex = Math.Clamp(_concernIndex, 0, concerns.Length - 1);
        var selected = concerns[_concernIndex];
        Line(ref builder, 270, $"Concern: {_queries.Concern}  quiet {_queries.ConcernQuietIntervals}/4", 0x35);
        Line(ref builder, 292, RegionalConcernStore.Description(_queries.Concern));
        Line(ref builder, 314, $"Bias: {RegionalConcernMapping.Definition(_queries.Concern)?.Value ?? "None"}");
        Line(ref builder, 336, $"Town Crier: {YesNo(AndraxiaAssembly.EventService.IsConcernRumorRegistered())}");
        Line(ref builder, 358, $"Last concern change: {_queries.LastConcernChange}", MutedHue);
        SmallButton(ref builder, 160, 395, 401, "<");
        AtLine(ref builder, 210, 397, $"{selected} ({RegionalConcernStore.Token(selected)})");
        SmallButton(ref builder, 480, 395, 402, ">");
        Button(ref builder, 160, 430, 403, "Set Concern...");
        Button(ref builder, 350, 430, 404, "Clear Concern...");
    }

    private void RenderAutomation(ref DynamicGumpBuilder builder)
    {
        Header(ref builder, "Automation");
        Line(ref builder, 90, $"Status: {OnOff(_queries.AutomationEnabled)} | Eligibility: {_queries.AutomationEligibility}", 0x35);
        Line(ref builder, 112, $"Ordinary players: {_queries.OrdinaryPlayers} | Pressure: {_queries.Pressure}/100");
        Line(ref builder, 134, $"Probability: {RegionalPressureStore.TriggerProbability(_queries.Pressure):P0}");
        Line(ref builder, 156, $"Concern: {_queries.Concern} | Bias: {RegionalConcernMapping.Definition(_queries.Concern)?.Value ?? "None"}");
        Line(ref builder, 178, $"Evaluation range: 5-10 minutes | Next: {Utc(_queries.NextEvaluationUtc)}");
        Line(ref builder, 210, $"Recent event: {AndraxiaAssembly.AutoEvents.LastAutomaticDefinitionId?.Value ?? "None"}", 0x35);
        var y = 235;
        foreach (var definition in _queries.Definitions)
        {
            var locationId = AndraxiaAssembly.AutoEvents.GetLastAutomaticLocation(definition.Id);
            var location = locationId is { } id && KnownEncounterLocations.TryGet(id, out var known) ? known.DisplayName : "None";
            Line(ref builder, y, $"{definition.DisplayName}: {location}", MutedHue);
            y += 20;
        }
        Button(ref builder, 160, 370, 500, "Enable...");
        Button(ref builder, 315, 370, 501, "Disable...");
        Button(ref builder, 470, 370, 502, "Evaluate Now");
    }

    private void RenderHistory(ref DynamicGumpBuilder builder)
    {
        Header(ref builder, "Event History");
        var page = _queries.History(_historyPage);
        _historyPage = page.Page;
        _displayedHistory = page.Entries.Select(static entry => entry.Id).ToArray();
        if (page.Entries.Count == 0)
        {
            Line(ref builder, 95, "No terminal event history.");
        }
        var y = 82;
        foreach (var (item, index) in page.Entries.Select((item, index) => (item, index)))
        {
            SmallButton(ref builder, 160, y, 620 + index, "Details");
            AtLine(ref builder, 260, y + 2, $"{item.CompletedUtc:yyyy-MM-dd HH:mm} {item.DisplayName} | {item.State} | {item.Severity} | P:{item.Participants.Count}");
            y += 38;
        }
        if (page.Page > 0) SmallButton(ref builder, 160, 500, 600, "Previous");
        AtLine(ref builder, 350, 503, $"Page {page.Page + 1}/{page.PageCount}");
        if (page.Page + 1 < page.PageCount) SmallButton(ref builder, 500, 500, 601, "Next");
    }

    private void RenderDiagnostics(ref DynamicGumpBuilder builder)
    {
        Header(ref builder, "Diagnostics");
        var active = _queries.ActiveEvents();
        var history = _queries.History(0);
        var lines = new[]
        {
            $"Known definitions: {_queries.Definitions.Count}",
            $"Known curated locations: {_queries.Definitions.Sum(definition => _queries.Locations(definition.Id).Count)}",
            $"Active events: {active.Count}",
            $"Terminal history: {Enumerable.Range(0, history.PageCount).Sum(page => _queries.History(page).Entries.Count)}/{EventStore.MaximumTerminalHistory}",
            $"World-state persistence: registered",
            $"Event persistence version: {AndraxiaEventPersistence.CurrentVersion}",
            $"Regional-state persistence version: {RegionalPressurePersistence.CurrentVersion}",
            $"Expiration scheduler: {OnOff(AndraxiaAssembly.EventService.ExpirationTimerRunning)}",
            $"Auto-event scheduler: {OnOff(AndraxiaAssembly.AutoEvents.TimerRunning)}",
            $"Regional stabilizer: {OnOff(AndraxiaAssembly.PressureStabilizer.TimerRunning)}",
            $"Town guard implementation: {nameof(AndraxiaTownGuard)}",
            "Instant-kill behavior: Disabled",
            $"Next expiration: {Utc(AndraxiaAssembly.EventService.NextExpirationUtc)}",
            $"Next automatic evaluation: {Utc(_queries.NextEvaluationUtc)}",
            $"Next stabilization: {Utc(_queries.NextStabilizationUtc)}",
            $"Active event rumors: {_queries.EventRumorCount()}",
            $"Regional concern rumor: {YesNo(AndraxiaAssembly.EventService.IsConcernRumorRegistered())}"
        };
        var y = 85;
        foreach (var line in lines)
        {
            Line(ref builder, y, line);
            y += 27;
        }
    }

    private void RenderConfirmation(ref DynamicGumpBuilder builder)
    {
        Header(ref builder, "Confirm Administrative Action");
        Line(ref builder, 130, ConfirmationText(), 0x35);
        Line(ref builder, 165, "The target and current state will be revalidated when confirmed.", MutedHue);
        Button(ref builder, 250, 225, 900, "Confirm");
        Button(ref builder, 450, 225, 901, "Cancel");
    }

    public override void OnResponse(NetState sender, in RelayInfo info)
    {
        var owner = sender.Mobile;
        if (owner?.AccessLevel < AccessLevel.Owner)
        {
            owner?.SendMessage("Only an Owner may use the Andraxia Administration Console.");
            return;
        }
        var button = info.ButtonID;
        if (button == 0) return;
        if (button is >= 1 and <= 7)
        {
            _panel = (AndraxiaAdminPanelId)(button - 1);
            _detailId = null;
            _confirmation = AndraxiaAdminConfirmation.None;
            Resend(owner);
            return;
        }
        if (button == 8) { Resend(owner); return; }
        if (_confirmation != AndraxiaAdminConfirmation.None)
        {
            if (button == 900) ExecuteConfirmation(owner);
            else _confirmation = AndraxiaAdminConfirmation.None;
            Resend(owner);
            return;
        }
        HandlePanelResponse(owner, info);
        Resend(owner);
    }

    private void HandlePanelResponse(Mobile owner, in RelayInfo info)
    {
        var button = info.ButtonID;
        if (button == 100) Result(_actions.TransitionWorld(owner, WorldCondition.Threatened));
        else if (button == 101) Result(_actions.TransitionWorld(owner, WorldCondition.Normal));
        else if (button == 102) Confirm(AndraxiaAdminConfirmation.ResetWorld);
        else if (button == 200) { _definitionIndex--; NormalizeDefinition(); }
        else if (button == 201) { _definitionIndex++; NormalizeDefinition(); }
        else if (button == 202) CycleLocation(-1);
        else if (button == 203) CycleLocation(1);
        else if (button == 204) Trigger(owner);
        else if (button is >= 220 and < 223) SelectDisplayed(button - 220, true);
        else if (button is >= 240 and < 243) GoToDisplayed(owner, button - 240);
        else if (button is >= 260 and < 263) ConfirmDisplayed(button - 260, AndraxiaAdminConfirmation.CompleteEvent);
        else if (button is >= 280 and < 283) ConfirmDisplayed(button - 280, AndraxiaAdminConfirmation.FailEvent);
        else if (button == 300 && _detailId is { } id) Result(_actions.GoTo(owner, id));
        else if (button == 301) _detailId = null;
        else if (button == 400) { _confirmationValue = info.GetTextEntry(1); Confirm(AndraxiaAdminConfirmation.SetPressure); }
        else if (button == 401) CycleConcern(-1);
        else if (button == 402) CycleConcern(1);
        else if (button == 403) { _confirmationValue = RegionalConcernStore.Token(Enum.GetValues<RegionalConcern>()[_concernIndex]); Confirm(AndraxiaAdminConfirmation.SetConcern); }
        else if (button == 404) Confirm(AndraxiaAdminConfirmation.ClearConcern);
        else if (button == 500) Confirm(AndraxiaAdminConfirmation.EnableAutomation);
        else if (button == 501) Confirm(AndraxiaAdminConfirmation.DisableAutomation);
        else if (button == 502) Result(_actions.Evaluate(owner));
        else if (button == 600) _historyPage--;
        else if (button == 601) _historyPage++;
        else if (button is >= 620 and < 630) SelectHistory(button - 620);
    }

    private void ExecuteConfirmation(Mobile owner)
    {
        var result = _confirmation switch
        {
            AndraxiaAdminConfirmation.ResetWorld => _actions.ResetWorld(owner),
            AndraxiaAdminConfirmation.CompleteEvent when _confirmationEventId is { } id =>
                _actions.TransitionEvent(owner, id, EventLifecycleState.Succeeded),
            AndraxiaAdminConfirmation.FailEvent when _confirmationEventId is { } id =>
                _actions.TransitionEvent(owner, id, EventLifecycleState.Failed),
            AndraxiaAdminConfirmation.SetPressure => _actions.SetPressure(owner, _confirmationValue),
            AndraxiaAdminConfirmation.SetConcern => _actions.SetConcern(owner, _confirmationValue),
            AndraxiaAdminConfirmation.ClearConcern => _actions.ClearConcern(owner),
            AndraxiaAdminConfirmation.EnableAutomation => _actions.SetAutomation(owner, true),
            AndraxiaAdminConfirmation.DisableAutomation => _actions.SetAutomation(owner, false),
            _ => new AdminActionResult(false, "Administrative target is no longer available.")
        };
        Result(result);
        _confirmation = AndraxiaAdminConfirmation.None;
        _confirmationEventId = null;
    }

    private void Trigger(Mobile owner)
    {
        NormalizeDefinition();
        var definition = _queries.Definitions[_definitionIndex];
        var locations = _queries.Locations(definition.Id);
        EncounterLocationId? location = _locationIndex >= 0 && _locationIndex < locations.Count ? locations[_locationIndex].Id : null;
        Result(_actions.Trigger(owner, definition.Id, location));
    }

    private void NormalizeDefinition()
    {
        var count = _queries.Definitions.Count;
        _definitionIndex = (_definitionIndex % count + count) % count;
        _locationIndex = -1;
    }

    private void CycleLocation(int delta)
    {
        NormalizeDefinitionIndexOnly();
        var count = _queries.Locations(_queries.Definitions[_definitionIndex].Id).Count + 1;
        _locationIndex = ((_locationIndex + 1 + delta) % count + count) % count - 1;
    }

    private void NormalizeDefinitionIndexOnly()
    {
        var count = _queries.Definitions.Count;
        _definitionIndex = (_definitionIndex % count + count) % count;
    }

    private void CycleConcern(int delta)
    {
        var count = Enum.GetValues<RegionalConcern>().Length;
        _concernIndex = (_concernIndex + delta + count) % count;
    }

    private void SelectDisplayed(int index, bool detail)
    {
        if (index >= 0 && index < _displayedEvents.Length && detail) _detailId = _displayedEvents[index];
        else _status = "Event no longer Active.";
    }

    private void GoToDisplayed(Mobile owner, int index)
    {
        if (index >= 0 && index < _displayedEvents.Length) Result(_actions.GoTo(owner, _displayedEvents[index]));
        else _status = "Event no longer Active.";
    }

    private void ConfirmDisplayed(int index, AndraxiaAdminConfirmation confirmation)
    {
        if (index >= 0 && index < _displayedEvents.Length)
        {
            _confirmationEventId = _displayedEvents[index];
            Confirm(confirmation);
        }
        else _status = "Event no longer Active.";
    }

    private void SelectHistory(int index)
    {
        if (index >= 0 && index < _displayedHistory.Length) _detailId = _displayedHistory[index];
        else _status = "History entry is no longer retained.";
    }

    private void Confirm(AndraxiaAdminConfirmation confirmation) => _confirmation = confirmation;
    private void Result(AdminActionResult result) => _status = result.Message;
    private void Resend(Mobile owner) => owner.SendGump(this);

    private string ConfirmationText() => _confirmation switch
    {
        AndraxiaAdminConfirmation.ResetWorld => "Reset Britain world state to its default?",
        AndraxiaAdminConfirmation.CompleteEvent => $"Complete event {_confirmationEventId}?",
        AndraxiaAdminConfirmation.FailEvent => $"Fail event {_confirmationEventId}?",
        AndraxiaAdminConfirmation.SetPressure => $"Set Britain pressure to '{_confirmationValue}'?",
        AndraxiaAdminConfirmation.SetConcern => $"Set Britain concern to '{_confirmationValue}'?",
        AndraxiaAdminConfirmation.ClearConcern => "Clear Britain regional concern?",
        AndraxiaAdminConfirmation.EnableAutomation => "Enable automatic event generation?",
        AndraxiaAdminConfirmation.DisableAutomation => "Disable automatic event generation?",
        _ => "Confirm action?"
    };

    private static void Header(ref DynamicGumpBuilder builder, string text) => builder.AddLabel(160, 60, 0x35, Trim(text, 70));
    private static void Line(ref DynamicGumpBuilder builder, int y, string text, int hue = LabelHue) => builder.AddLabel(160, y, hue, Trim(text, 105));
    private static void AtLine(ref DynamicGumpBuilder builder, int x, int y, string text, int hue = LabelHue) =>
        builder.AddLabel(x, y, hue, Trim(text, 90));
    private static void Button(ref DynamicGumpBuilder builder, int x, int y, int id, string text)
    {
        builder.AddButton(x, y, 4005, 4007, id);
        builder.AddLabel(x + 35, y + 2, LabelHue, text);
    }
    private static void SmallButton(ref DynamicGumpBuilder builder, int x, int y, int id, string text) => Button(ref builder, x, y, id, text);
    private static string Utc(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "-";
    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static string OnOff(bool value) => value ? "Enabled" : "Disabled";
    private static string Bool(bool? value) => value.HasValue ? YesNo(value.Value) : "?";
    private static string Trim(string value, int length) => string.IsNullOrEmpty(value) || value.Length <= length ? value ?? "" : value[..(length - 3)] + "...";
}

internal static class AndraxiaAdminConsole
{
    internal const string CommandName = "Andraxia";
    internal const AccessLevel RequiredAccess = AccessLevel.Owner;
    private static AndraxiaAdminQueries _queries;
    private static AndraxiaAdminActions _actions;
    internal static bool CanOpen(AccessLevel accessLevel) => accessLevel >= RequiredAccess;

    internal static void Configure(
        WorldStateStore worldStates,
        EventStore events,
        AndraxiaEventService eventService,
        AndraxiaAutoEventGenerator autoEvents,
        RegionalPressureStore pressure,
        RegionalPressureStabilizer stabilizer,
        RegionalConcernStore concern
    )
    {
        if (_queries != null) return;
        _queries = new AndraxiaAdminQueries(worldStates, events, eventService, autoEvents, pressure, stabilizer, concern);
        _actions = new AndraxiaAdminActions(worldStates, eventService, autoEvents, pressure, concern, _queries);
        RegisterCommand();
    }

    internal static void RegisterCommand()
    {
        CommandSystem.Register(CommandName, RequiredAccess, OnCommand);
    }

    [Usage("Andraxia")]
    [Description("Opens the Andraxia Administration Console.")]
    private static void OnCommand(CommandEventArgs e)
    {
        if (!CanOpen(e.Mobile.AccessLevel))
        {
            e.Mobile.SendMessage("Only an Owner may use the Andraxia Administration Console.");
            return;
        }
        e.Mobile.SendGump(new AndraxiaAdminGump(_queries, _actions));
    }
}
