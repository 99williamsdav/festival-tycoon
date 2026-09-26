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
        if (_session.CaptureProgramme() is null) return;
        _programmeControls = new VBoxContainer(); parent.AddChild(_programmeControls);
        _programmeControls.AddChild(LabelText("ONE DAY • THREE FIXED SETS", 16, new Color("29352c")));
        _programmeSummary = LabelText("", 13, new Color("29352c"));
        _programmeSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _programmeControls.AddChild(_programmeSummary);
        var acts = _session.GetFestivalActs().ToArray();
        _programmeDraft = acts.Take(3).Select(act => act.Id).ToArray();
        var windows = new[] { "1 • 0:15–1:30", "2 • 1:55–3:10", "3 • 3:35–4:50" };
        for (var slot = 0; slot < 3; slot++)
        {
            _programmeControls.AddChild(LabelText(windows[slot], 13, new Color("29352c")));
            var index = slot;
            var choice = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            choice.AddThemeFontSizeOverride("font_size", 13);
            choice.ItemSelected += selected =>
            {
                if (_programmeRefreshing) return;
                _programmeDraft[index] = choice.GetItemMetadata((int)selected).AsString();
                RefreshProgrammeControls();
            };
            _programmeChoices[slot] = choice; _programmeControls.AddChild(choice);
        }
        _programmeBook = ButtonText("BOOK THREE ACTS", () =>
            CommitEquipmentAction(new SetProgrammeCommand(_programmeDraft.ToArray())));
        _programmeBook.TooltipText = "Book three distinct acts once. After payment, only their order can change before opening; no refund or repeat fee.";
        _programmeControls.AddChild(_programmeBook);
    }

    private void RefreshProgrammeControls()
    {
        if (_programmeControls is null) return;
        _programmeControls.Visible = _session.CaptureProgramme() is not null;
        if (_session.CaptureProgramme() is not { } programme) return;
        var p = _session.CapturePreparation()!;
        var acts = _session.GetFestivalActs().ToArray();
        var booked = programme.ActIds.Length == 3;
        var bookingKey = string.Join("|", programme.ActIds);
        if (_programmeRenderedBooking != bookingKey)
        {
            if (booked) _programmeDraft = programme.ActIds.ToArray();
            _programmeRenderedBooking = bookingKey;
        }
        var available = booked ? acts.Where(act => programme.ActIds.Contains(act.Id)).ToArray() : acts;
        _programmeRefreshing = true;
        for (var slot = 0; slot < 3; slot++)
        {
            var choice = _programmeChoices[slot]; choice.Clear();
            foreach (var act in available)
            {
                choice.AddItem($"{act.Name} • {FestivalGenreName(act.Genre)} • £{act.PricePennies / 100}");
                choice.SetItemMetadata(choice.ItemCount - 1, act.Id);
                choice.SetItemTooltip(choice.ItemCount - 1, $"{act.Name} · {FestivalGenreName(act.Genre)}\nPopularity {act.Popularity}/100 · £{act.PricePennies / 100}\nDifferent people enjoy other genres differently.");
            }
            var selected = Array.FindIndex(available, act => act.Id == _programmeDraft[slot]);
            choice.Select(Math.Max(0, selected));
            choice.Disabled = p.Status != PreparationStatus.Preparing;
        }
        _programmeRefreshing = false;
        var total = acts.Where(act => _programmeDraft.Contains(act.Id)).Sum(act => act.PricePennies);
        var rejected = _session.ValidateCommand(CampaignEnvelope(new SetProgrammeCommand(_programmeDraft.ToArray())));
        _programmeBook!.Text = booked ? "SAVE ACT ORDER • NO EXTRA FEE" : $"BOOK THREE ACTS • £{total / 100}";
        _programmeBook.Disabled = rejected is not null;
        _programmeBook.TooltipText = rejected?.Message ?? "Atomic booking/order; autosave must succeed before any payment or change applies.";
        _programmeBook.Visible = p.Status == PreparationStatus.Preparing;
        _programmeSummary!.Text = booked
            ? string.Join("\n", programme.ActIds.Select((id, slot) => $"{slot + 1}. {acts.Single(act => act.Id == id).Name} • {FestivalGenreName(acts.Single(act => act.Id == id).Genre)}")) +
              "\n25s changeovers • no music during scheduled silence."
            : "Choose three different acts in order. All nine performers are protected people. Popularity affects appeal, not guest count.\nPay once; reorder before opening only.";
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
