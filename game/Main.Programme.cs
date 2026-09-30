using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private VBoxContainer? _programmeControls;
    private Label? _programmeSummary;
    private readonly OptionButton[] _programmeChoices = new OptionButton[3];
    private Button? _programmeBook;
    private string[] _programmeDraft = [];
    private string _programmeRenderedBooking = "";
    private bool _programmeRefreshing;
    private string? _presentedActId;
    private const bool FestivalRockAudioApproved = true;

    private string PersonPresentationName(EditionPerson person) => _session.FestivalPerformerTitle(person.AgentId) is { } title
        ? $"{title} • {person.Name}" : person.Name;
    private string PersonPresentationRole(EditionPerson person) => _session.FestivalPerformerTitle(person.AgentId) ??
        (person.Name == "Jordan Hale" ? "Steward" : person.Role.ToString());

    private static string FestivalGenreName(int genre) => genre switch
    { 0 => "Folk", 1 => "Rock", 2 => "Pop", 3 => "Electronic", _ => "Unknown" };

    private void BuildProgrammeControls(VBoxContainer parent)
    {
        BuildBookingControls(parent);
    }

    private void RefreshProgrammeControls()
    {
        if (_bookingLane is not null) { RefreshBookingControls(); return; }
        if (_programmeControls is null) return;
        _programmeControls.Visible = _session.CaptureProgramme() is not null;
        if (_session.CaptureProgramme() is not { } programme) return;
        var p = _session.CapturePreparation()!;
        var acts = _session.GetFestivalActs().ToArray();
        var editable = p.Status == PreparationStatus.Preparing && p.Plan is { Committed: false };
        var actIds = p.Plan?.ActIds ?? programme.ActIds;
        if (editable) _programmeDraft = actIds.Length == 3 ? actIds.ToArray() : ["", "", ""];
        var booked = actIds.Length == 3 && actIds.All(id => id != "");
        var bookingKey = string.Join("|", actIds);
        if (_programmeRenderedBooking != bookingKey)
        {
            if (actIds.Length == 3) _programmeDraft = actIds.ToArray();
            else if (editable) _programmeDraft = ["", "", ""];
            _programmeRenderedBooking = bookingKey;
        }
        var available = booked && !editable ? acts.Where(act => programme.ActIds.Contains(act.Id)).ToArray() : acts;
        _programmeRefreshing = true;
        for (var slot = 0; slot < 3; slot++)
        {
            var choice = _programmeChoices[slot]; choice.Clear();
            if (editable) { choice.AddItem("Empty slot · choose an act"); choice.SetItemMetadata(0, ""); }
            foreach (var act in available)
            {
                choice.AddItem($"{act.Name} • {FestivalGenreName(act.Genre)} • £{act.PricePennies / 100}");
                choice.SetItemMetadata(choice.ItemCount - 1, act.Id);
                choice.SetItemTooltip(choice.ItemCount - 1, $"{act.Name} · {FestivalGenreName(act.Genre)}\nPopularity {act.Popularity}/100 · £{act.PricePennies / 100}\nDifferent people enjoy other genres differently.");
            }
            var selected = Array.FindIndex(available, act => act.Id == _programmeDraft[slot]);
            choice.Select(Math.Max(0, selected + (editable ? 1 : 0)));
            choice.Disabled = p.Status != PreparationStatus.Preparing;
            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                choice.AddThemeColorOverride(state, editable && _programmeDraft[slot] == "" ? new Color("a83232") : HudInk);
            choice.TooltipText = editable && _programmeDraft[slot] == "" ? "Empty slot · choose an act before Start" : "Selecting an act immediately saves the unpaid lineup plan.";
        }
        _programmeRefreshing = false;
        var total = acts.Where(act => _programmeDraft.Contains(act.Id)).Sum(act => act.PricePennies);
        var rejected = _session.ValidateCommand(CampaignEnvelope(new SetProgrammeCommand(_programmeDraft.ToArray())));
        _programmeBook!.Text = booked ? "SAVE ACT ORDER • NO EXTRA FEE" : $"BOOK THREE ACTS • £{total / 100}";
        _programmeBook.Disabled = rejected is not null;
        _programmeBook.TooltipText = rejected?.Message ?? "Update the unpaid lineup now; changed state saves every 30 unpaused seconds and at opening.";
        _programmeBook.Visible = p.Status == PreparationStatus.Preparing && !editable;
        _programmeSummary!.Text = booked
            ? string.Join("\n", programme.ActIds.Select((id, slot) => $"{slot + 1}. {acts.Single(act => act.Id == id).Name} • {FestivalGenreName(acts.Single(act => act.Id == id).Genre)}")) +
              "\n25s changeovers • no music during scheduled silence."
            : "Choose three different acts in order. All nine performers are protected people. Popularity affects appeal, not guest count.\nPay once; reorder before opening only.";
        if (_hudTabs is not null)
            _programmeSummary.Text = booked ? "Three acts booked • paid once.\nOnly their order can change before opening." :
                "Choose three different acts. Nine performers are protected.\nPay once; reorder before opening only.";
        if (editable)
        {
            _programmeBook.Text = $"SAVE LINEUP PLAN • £{total / 100}";
            _programmeBook.TooltipText = rejected?.Message ?? "Freely replace or reorder three distinct acts. Unpaid until Start.";
            _programmeSummary.Text = $"Unpaid lineup • {FestivalCurrency.Format(actIds.Where(id => id != "").Sum(id => acts.Single(a => a.Id == id).PricePennies))}\nChoose three different acts; freely revise before opening.";
        }
    }

    private string FestivalCopy(string text) => _session.CaptureProgramme() is null ? text :
        text.Replace("weekend", "festival", StringComparison.Ordinal).Replace("Weekend", "Festival", StringComparison.Ordinal)
            .Replace("WEEKEND", "FESTIVAL", StringComparison.Ordinal);

    private int PerformerPresentationRole(ulong id, string name)
    {
        var performer = _session.CaptureProgramme()?.Performers.FirstOrDefault(item => item.AgentId == id);
        return performer?.RoleIndex ?? (name == "Alex Reed" ? 0 : name == "Blair Moss" ? 1 : 2);
    }
}
