using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The Staff sheet, laid out like the Programme's: your crew's slots on the left, filled or waiting, and the
/// market's candidates in a table on the right with a role filter. Hire puts a candidate straight into their
/// role's slot (the perk's extra slot when the main one is taken, else replacing whoever held it); Remove empties
/// a slot. Everything is an unpaid plan until Start.
/// </summary>
public partial class Main
{
    private sealed record StaffSlotView(PanelContainer Card, Label Caption, Label Title, Label Detail, Button Action);
    private sealed record StaffRowView(PanelContainer Row, HBoxContainer Tags, Label Status, Button Hire);

    private readonly Dictionary<string, StaffSlotView> _staffSlots = [];
    private readonly Dictionary<string, StaffRowView> _staffRows = [];
    private VBoxContainer? _staffTableBody;
    private OptionButton? _staffRoleFilter;
    private StaffRole? _staffRoleShown;
    private Label? _staffTableCount;
    private string _staffMarketKey = "";
    private bool _staffPanelBuilt;

    private static readonly (string Key, StaffRole? Role, bool Extra, string Name)[] StaffSlots =
    [
        ("sound", StaffRole.Sound, false, "Sound engineer"), ("medic", StaffRole.Medic, false, "Medic"), ("steward", StaffRole.Steward, false, "Steward"),
        ("extra-medic", StaffRole.Medic, true, "Extra medic"), ("extra-steward", StaffRole.Steward, true, "Extra steward"), ("maintenance", null, false, "Maintenance"),
    ];

    private static Color StaffRoleColour(StaffRole? role) => role switch
    {
        StaffRole.Sound => new Color("3e5a8c"), StaffRole.Medic => new Color("2f8a5f"), StaffRole.Steward => Ui.Teal, _ => new Color("8a6a2e"),
    };

    private string[] StaffHires(PreparationSnapshot p) => p.Plan?.OfferIds ?? p.AcceptedOffers;

    private static string SlotPrefix(string key) => key == "maintenance" ? "maintenance.worker" : key.StartsWith("extra-", StringComparison.Ordinal) ? $"staff.{key}." : $"staff.{key}.";

    private void BuildStaffPanel(VBoxContainer page)
    {
        _staffPanelBuilt = true;
        var root = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", Ui.Px(22)); page.AddChild(root);

        var crew = new VBoxContainer { CustomMinimumSize = new Vector2(Ui.S(300), 0) };
        crew.AddThemeConstantOverride("separation", Ui.Px(6)); root.AddChild(crew);
        crew.AddChild(Ui.Heading("Your crew", 27));
        crew.AddChild(WithWrap(Ui.Text("A sound engineer, a medic and a steward are required. Paid at Start.", 13.5f, Ui.InkMuted)));
        crew.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(4)) });
        foreach (var (key, role, _, name) in StaffSlots)
        {
            var slotKey = key;
            var card = new PanelContainer { CustomMinimumSize = new Vector2(0, Ui.S(64)), MouseFilter = Control.MouseFilterEnum.Pass };
            crew.AddChild(card);
            var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", Ui.Px(8)); card.AddChild(line);
            var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
            words.AddThemeConstantOverride("separation", Ui.Px(1)); line.AddChild(words);
            var caption = Ui.Caps("", Ui.Ink, 9.5f); words.AddChild(caption);
            var title = Ui.Heading("", 17); title.ClipText = true; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; words.AddChild(title);
            var detail = Ui.Text("", 12.5f, Ui.Ink); detail.ClipText = true; detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; words.AddChild(detail);
            var action = new Button { Flat = true, MouseDefaultCursorShape = Control.CursorShape.PointingHand, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            action.AddThemeFontSizeOverride("font_size", Ui.Px(13)); action.AddThemeFontOverride("font", Ui.BodySemi);
            action.Pressed += () => StaffSlotAction(slotKey);
            line.AddChild(action);
            _staffSlots[key] = new StaffSlotView(card, caption, title, detail, action);
        }

        root.AddChild(new ColorRect { Color = Ui.PaperRule, CustomMinimumSize = new Vector2(1, 0), MouseFilter = Control.MouseFilterEnum.Ignore });

        var table = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        table.AddThemeConstantOverride("separation", 0); root.AddChild(table);
        var heading = new HBoxContainer(); table.AddChild(heading);
        var tableTitle = Ui.Heading("Candidates", 22); tableTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; heading.AddChild(tableTitle);
        _staffTableCount = Ui.Text("", 13, Ui.InkMuted); _staffTableCount.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; heading.AddChild(_staffTableCount);
        heading.AddChild(new Control { CustomMinimumSize = new Vector2(Ui.S(40), 0) }); // clear of the sheet's close button
        table.AddChild(Ui.Text("Hire puts someone straight into their role's slot on the left.", 13, Ui.InkMuted));
        table.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        _staffRoleFilter = new OptionButton { CustomMinimumSize = new Vector2(Ui.S(170), Ui.S(36)), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        _staffRoleFilter.AddThemeFontSizeOverride("font_size", Ui.Px(13.5f));
        _staffRoleFilter.AddItem("All roles");
        foreach (var role in new[] { StaffRole.Sound, StaffRole.Medic, StaffRole.Steward })
            _staffRoleFilter.AddItem(char.ToUpperInvariant(StaffCatalogue.RoleName(role)[0]) + StaffCatalogue.RoleName(role)[1..] + "s");
        _staffRoleFilter.ItemSelected += index => { _staffRoleShown = index == 0 ? null : (StaffRole)(index - 1); RefreshStaffPanel(); };
        table.AddChild(_staffRoleFilter);
        table.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        var header = new PanelContainer();
        var underline = Ui.Box(new Color(0, 0, 0, 0), 0, padX: 10); underline.BorderColor = Ui.Ink; underline.BorderWidthBottom = Ui.Px(1.5f); underline.ContentMarginBottom = Ui.S(6);
        header.AddThemeStyleboxOverride("panel", underline); table.AddChild(header);
        var headerLine = StaffTableLine(); header.AddChild(headerLine);
        foreach (var (text, column) in new[] { ("Candidate", 0), ("Role", 1), ("Wage", 2), ("Skills", 3), ("", 4) })
            StaffCell(headerLine, column, Ui.Caps(text, Ui.InkMuted, 10.5f));
        _staffTableBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _staffTableBody.AddThemeConstantOverride("separation", 0);
        // About seven rows show at once; the rest of the market scrolls.
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, Ui.S(330)) };
        Ui.SlimScrollbar(scroll); scroll.AddChild(_staffTableBody); table.AddChild(scroll);
        RefreshStaffPanel();
    }

    private static Label WithWrap(Label label) { label.AutowrapMode = TextServer.AutowrapMode.WordSmart; return label; }

    private static float StaffColumnWidth(int column) => column switch { 1 => Ui.S(116), 2 => Ui.S(48), 3 => Ui.S(170), 4 => Ui.S(84), _ => 0 };
    private static HBoxContainer StaffTableLine()
    {
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", Ui.Px(10));
        return line;
    }
    private static Control StaffCell(HBoxContainer line, int column, Control content)
    {
        content.CustomMinimumSize = new Vector2(StaffColumnWidth(column), content.CustomMinimumSize.Y);
        content.SizeFlagsHorizontal = column == 0 ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill;
        content.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(content);
        return content;
    }

    /// <summary>One row per candidate in this edition's market; a new campaign or retry brings a new market.</summary>
    private void SyncStaffRows(IReadOnlyList<StaffCandidate> candidates)
    {
        var key = string.Join("|", candidates.Select(c => c.Id + c.Name));
        if (key == _staffMarketKey || _staffTableBody is null) return;
        _staffMarketKey = key;
        foreach (var row in _staffRows.Values) { _staffTableBody.RemoveChild(row.Row); row.Row.QueueFree(); }
        _staffRows.Clear();
        foreach (var candidate in candidates) _staffTableBody.AddChild(StaffRow(candidate));
    }

    private PanelContainer StaffRow(StaffCandidate c)
    {
        var row = new PanelContainer { CustomMinimumSize = new Vector2(0, Ui.S(46)), MouseFilter = Control.MouseFilterEnum.Pass };
        var rule = Ui.Box(new Color(0, 0, 0, 0), 0, padX: 10, padY: 4); rule.BorderColor = Ui.PaperRule; rule.BorderWidthBottom = 1;
        row.AddThemeStyleboxOverride("panel", rule);
        var line = StaffTableLine(); row.AddChild(line);
        var who = new VBoxContainer(); who.AddThemeConstantOverride("separation", Ui.Px(2));
        var nameLine = new HBoxContainer(); nameLine.AddThemeConstantOverride("separation", Ui.Px(8)); who.AddChild(nameLine);
        var name = Ui.Text(c.Name, 14, Ui.Ink, Ui.BodyBold); name.ClipText = true; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.CustomMinimumSize = new Vector2(1, 0); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; nameLine.AddChild(name);
        var status = Ui.Text("", 11.5f, Ui.Teal, Ui.BodyBold); nameLine.AddChild(status);
        var tags = new HBoxContainer(); tags.AddThemeConstantOverride("separation", Ui.Px(4)); who.AddChild(tags);
        foreach (var trait in c.Traits)
        {
            var good = StaffCatalogue.IsPositive(trait);
            var tag = Ui.Caps(StaffCatalogue.TraitLabel(trait), good ? Ui.TealDeep : new Color("7a5a1c"), 9);
            tag.AddThemeStyleboxOverride("normal", Ui.Box(good ? Ui.TealWash : new Color("f1dfb4"), 4, padX: 5, padY: 1));
            tag.TooltipText = StaffCatalogue.TraitDescription(trait); tag.MouseFilter = Control.MouseFilterEnum.Pass;
            tags.AddChild(tag);
        }
        if (c.Traits.Length == 0) tags.AddChild(Clip(Ui.Text(c.Blurb, 11.5f, Ui.InkMuted)));
        StaffCell(line, 0, who);
        var role = new HBoxContainer(); role.AddThemeConstantOverride("separation", Ui.Px(6));
        var dot = new Panel { CustomMinimumSize = Ui.S(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        dot.AddThemeStyleboxOverride("panel", Ui.Box(StaffRoleColour(c.Role), 5)); role.AddChild(dot);
        var roleName = StaffCatalogue.RoleName(c.Role);
        role.AddChild(Ui.Text(char.ToUpperInvariant(roleName[0]) + roleName[1..], 13, Ui.Ink));
        StaffCell(line, 1, role);
        StaffCell(line, 2, Ui.Text(FestivalCurrency.Format(c.WagePennies), 14, Ui.Ink, Ui.BodySemi));
        var skills = new GridContainer { Columns = 2 }; skills.AddThemeConstantOverride("h_separation", Ui.Px(6)); skills.AddThemeConstantOverride("v_separation", 0);
        var labels = StaffStatLabels(c.Role); var ratings = StaffRatings(c);
        for (var i = 0; i < labels.Length; i++)
        {
            skills.AddChild(Ui.Caps(labels[i], Ui.InkMuted, 9));
            skills.AddChild(new RatingBars { Score = ratings[i] * 20, Ink = Ui.TealDeep, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        }
        StaffCell(line, 3, skills);
        var hire = Ui.Style(new Button { Text = "Hire", CustomMinimumSize = new Vector2(Ui.S(76), Ui.S(32)), MouseDefaultCursorShape = Control.CursorShape.PointingHand },
            Ui.ButtonKind.Secondary, 13.5f);
        hire.Pressed += () => HireStaff(c.Id);
        StaffCell(line, 4, hire);
        row.TooltipText = FestivalCopy($"{c.Name} · {roleName}\n{c.Blurb}\n{StaffCandidateAbilities(c)}\n" +
            string.Concat(c.Traits.Select(trait => $"{StaffCatalogue.TraitLabel(trait).ToUpperInvariant()}  {StaffCatalogue.TraitDescription(trait)}\n")));
        _staffRows[c.Id] = new StaffRowView(row, tags, status, hire);
        return row;
    }

    private static Label Clip(Label label)
    {
        label.ClipText = true; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.CustomMinimumSize = new Vector2(1, 0); label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    /// <summary>The offer a Hire click takes: the role's slot, or its perk-granted second slot when the first is filled.</summary>
    private string HireTarget(StaffCandidate c, PreparationSnapshot p)
    {
        var hires = StaffHires(p);
        var key = StaffCatalogue.Key(c.Role);
        if (!hires.Any(id => id.StartsWith($"staff.{key}.", StringComparison.Ordinal))) return c.Id;
        var extraOwned = c.Role == StaffRole.Medic ? p.ExtraMedicSlotOwned : c.Role == StaffRole.Steward && p.ExtraStewardSlotOwned;
        return extraOwned && !hires.Any(id => id.StartsWith($"staff.extra-{key}.", StringComparison.Ordinal)) ? c.ExtraOfferId : c.Id;
    }

    private void HireStaff(string candidateId)
    {
        if (_session.CapturePreparation() is not { } p || _session.GetStaffCandidates().SingleOrDefault(c => c.Id == candidateId) is not { } c) return;
        var offer = HireTarget(c, p);
        var replaced = StaffHires(p).FirstOrDefault(id => _session.GetPreparationOffers().SingleOrDefault(o => o.Id == id)?.Category ==
            _session.GetPreparationOffers().Single(o => o.Id == offer).Category);
        var accepted = _host.Execute(new AcceptPreparationOfferCommand(offer), out var error);
        var was = replaced is null ? null : StaffCatalogue.ForOffer(_session.GetStaffCandidates(), replaced)?.Name;
        _preparationMessage = !accepted ? error ?? "Staff plan was not changed."
            : was is not null ? $"Hired {c.Name} in place of {was}. Paid at Start." : $"Hired {c.Name}. Paid at Start.";
        RefreshPreparationHud();
    }

    private void StaffSlotAction(string key)
    {
        if (_session.CapturePreparation() is not { } p) return;
        var held = StaffHires(p).FirstOrDefault(id => key == "maintenance" ? id == "maintenance.worker" : id.StartsWith(SlotPrefix(key), StringComparison.Ordinal));
        SessionCommand command = held is not null ? new RemovePreparationOfferCommand(held) : new AcceptPreparationOfferCommand("maintenance.worker");
        if (held is null && key != "maintenance") return;
        var accepted = _host.Execute(command, out var error);
        _preparationMessage = accepted ? held is not null ? "Removed from the crew. Hire someone else from the list." : "Maintenance worker hired. Paid at Start." : error ?? "Staff plan was not changed.";
        RefreshPreparationHud();
    }

    private void RefreshStaffPanel()
    {
        if (!_staffPanelBuilt || _session.CapturePreparation() is not { } p) return;
        var candidates = _session.GetStaffCandidates();
        SyncStaffRows(candidates);
        var hires = StaffHires(p);
        var preparing = p.Status == PreparationStatus.Preparing;
        var offers = _session.GetPreparationOffers().ToDictionary(o => o.Id);
        foreach (var (key, role, extra, name) in StaffSlots)
        {
            var view = _staffSlots[key];
            var owned = key switch { "extra-medic" => p.ExtraMedicSlotOwned, "extra-steward" => p.ExtraStewardSlotOwned, "maintenance" => offers.ContainsKey("maintenance.worker"), _ => true };
            view.Card.Visible = owned;
            if (!owned) continue;
            var held = hires.FirstOrDefault(id => key == "maintenance" ? id == "maintenance.worker" : id.StartsWith(SlotPrefix(key), StringComparison.Ordinal));
            var who = held is null ? null : StaffCatalogue.ForOffer(candidates, held);
            var required = !extra && key != "maintenance";
            view.Caption.Text = $"{name} · {(required ? "required" : extra ? "perk slot" : "optional")}".ToUpperInvariant();
            var colour = StaffRoleColour(role);
            if (held is not null)
            {
                var style = Ui.Box(colour.Lerp(Ui.Paper, 0.82f), 6, padX: 12, padY: 8);
                view.Card.AddThemeStyleboxOverride("panel", style);
                view.Caption.AddThemeColorOverride("font_color", colour.Darkened(0.2f));
                view.Title.Text = who?.Name ?? "Morgan Finch";
                view.Title.AddThemeColorOverride("font_color", Ui.Ink);
                view.Detail.Text = FestivalCurrency.Format(offers[held].PricePennies) +
                    (who is { Traits.Length: > 0 } ? " · " + string.Join(", ", who.Traits.Select(StaffCatalogue.TraitLabel)) : key == "maintenance" ? " · fixes physical breakdowns" : "");
                view.Action.Text = "Remove"; view.Action.Visible = preparing;
                view.Action.TooltipText = $"Take {view.Title.Text} off the crew";
            }
            else
            {
                view.Card.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Ui.Gold, required ? 0.2f : 0.1f), 6, Ui.GoldShadow, required ? 2 : 1, 12, 8));
                view.Caption.AddThemeColorOverride("font_color", Ui.GoldInk);
                view.Title.AddThemeColorOverride("font_color", Ui.GoldInk);
                if (key == "maintenance")
                {
                    view.Title.Text = "Morgan Finch";
                    view.Detail.Text = $"Fixes physical breakdowns · {FestivalCurrency.Format(offers["maintenance.worker"].PricePennies)}";
                    view.Action.Text = "Hire"; view.Action.Visible = preparing;
                    view.Action.TooltipText = "Add a maintenance worker to the crew";
                }
                else
                {
                    view.Title.Text = $"Hire a{(role == StaffRole.Sound ? "" : extra ? "nother" : "")} {StaffCatalogue.RoleName(role!.Value)}";
                    view.Detail.Text = required ? "Required before Start · pick from the list" : "Your perk adds this slot";
                    view.Action.Visible = false;
                }
            }
        }
        foreach (var c in candidates)
        {
            var row = _staffRows[c.Id];
            row.Row.Visible = _staffRoleShown is null || c.Role == _staffRoleShown;
            var main = hires.Contains(c.Id); var second = hires.Contains(c.ExtraOfferId);
            row.Status.Text = main || second ? "On your crew" : "";
            var target = HireTarget(c, p);
            var issue = _session.ValidateCommand(CampaignEnvelope(new AcceptPreparationOfferCommand(target)))?.Message;
            var replacing = hires.FirstOrDefault(id => offers.TryGetValue(id, out var o) && o.Category == offers[target].Category);
            var replacedName = replacing is null ? null : StaffCatalogue.ForOffer(candidates, replacing)?.Name;
            row.Hire.Text = main || second ? "Hired" : replacing is null ? "Hire" : "Swap in";
            row.Hire.Disabled = !preparing || main || second || issue is not null;
            row.Hire.TooltipText = main || second ? $"{c.Name} is on your crew; remove them from the slot on the left." :
                issue ?? (replacedName is not null ? $"Hire {c.Name} in place of {replacedName}." : $"Hire {c.Name} as your {(target == c.ExtraOfferId ? "extra " : "")}{StaffCatalogue.RoleName(c.Role)}.");
            row.Row.Modulate = main || second ? new Color(1, 1, 1, 0.6f) : Colors.White;
        }
        var shown = candidates.Count(c => _staffRoleShown is null || c.Role == _staffRoleShown);
        _staffTableCount!.Text = $"{shown} {(shown == 1 ? "candidate" : "candidates")} · cheapest first";
        // Cheapest first within each role, roles in slot order, as the old cards were.
        var ordered = candidates.OrderBy(c => c.Role).ThenBy(c => c.WagePennies).ThenBy(c => c.Id, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < ordered.Length; i++) _staffTableBody!.MoveChild(_staffRows[ordered[i].Id].Row, i);
    }
}
