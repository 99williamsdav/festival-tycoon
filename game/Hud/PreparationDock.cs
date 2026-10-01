using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>What the preparation dock needs from the HUD shell to navigate and to know what is open.</summary>
internal interface IPreparationNavigation
{
    /// <summary>"Build", a workspace tab name, or "" when nothing is open.</summary>
    string OpenDestinationName { get; }
    void OpenDestination(string destination);
    /// <summary>The "Before opening" checklist would sit under an open Programme page or context panel.</summary>
    bool ReadinessCovered { get; }
    /// <summary>A build placement is in progress.</summary>
    bool Placing { get; }
    void ConfirmStart();
}

/// <summary>
/// The bottom dock during preparation: four folders (Build, Programme, Staff, Supplies) with their
/// status, the budget with its breakdown, and Start festival; plus the "Ready to open?" card whose
/// missing items open the folder that fixes them.
/// </summary>
internal sealed class PreparationDock(IHudHost _hud, IPreparationNavigation _nav)
{
    private static readonly (string Name, string Icon)[] Folders =
        [("Build", "hammer"), ("Programme", "music"), ("Staff", "users"), ("Supplies", "package")];

    private PanelContainer? _dock;
    private readonly Dictionary<string, (Button Button, Label Status, Control Notch)> _folders = [];
    private Label? _drafted;
    private Label? _draftedOf;
    private Label? _left;
    private ProgressBar? _budgetBar;
    private Label? _breakdown;
    private Button? _start;
    private Label? _startTitle;
    private TextureRect? _startLock;
    private Label? _startDetail;

    private PanelContainer? _readiness;
    private ProgressRing? _ring;
    private Label? _readinessTitle;
    private Label? _readinessDetail;
    private VBoxContainer? _readinessRows;
    private string _readinessKey = "";

    private PanelContainer? _receipt;
    private VBoxContainer? _receiptLines;
    private Label? _receiptTotal;
    private Label? _receiptAfter;
    private Label? _receiptCheck;
    private string _receiptKey = "";

    public void Build(CanvasLayer layer, Vector2 size)
    {
        _dock = new PanelContainer { Position = new Vector2(0, size.Y - Ui.Dock), Size = new Vector2(size.X, Ui.Dock), Theme = HudTheme() };
        var style = Ui.Box(Ui.Bar, 0, padX: 16, padY: 14);
        style.BorderColor = Ui.Gold; style.BorderWidthTop = Ui.Px(2);
        _dock.AddThemeStyleboxOverride("panel", style); layer.AddChild(_dock);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Ui.Px(10)); _dock.AddChild(row);

        foreach (var (name, icon) in Folders)
        {
            var destination = name;
            var button = new Button { CustomMinimumSize = new Vector2(Ui.S(142), 0), TooltipText = $"Open {name}",
                MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            button.Pressed += () => _nav.OpenDestination(destination);
            row.AddChild(button);
            var face = new VBoxContainer { Name = "Face", Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            face.SetAnchorsPreset(Control.LayoutPreset.FullRect); face.AddThemeConstantOverride("separation", Ui.Px(2));
            button.AddChild(face);
            var image = Ui.IconRect(icon, 22, Ui.BarText); image.Name = "Icon"; image.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; face.AddChild(image);
            var title = Ui.Text(name, 16, Ui.BarText, Ui.BodyBold); title.Name = "Title"; title.HorizontalAlignment = HorizontalAlignment.Center; face.AddChild(title);
            var status = Ui.Caps("", Ui.BarMuted, 9.5f); status.HorizontalAlignment = HorizontalAlignment.Center; face.AddChild(status);
            var notch = new ColorRect { Color = Ui.Gold, Size = Ui.S(14, 14), Rotation = Mathf.Pi / 4, MouseFilter = Control.MouseFilterEnum.Ignore,
                Position = new Vector2(Ui.S(71), -Ui.S(18)), Visible = false };
            button.AddChild(notch);
            _folders.Add(name, (button, status, notch));
        }
        row.AddChild(new ColorRect { Color = Ui.BarLine, CustomMinimumSize = new Vector2(1, 0), SizeFlagsVertical = Control.SizeFlags.Fill });

        var budget = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        budget.AddThemeConstantOverride("separation", Ui.Px(6));
        var budgetMargin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        budgetMargin.AddThemeConstantOverride("margin_left", Ui.Px(6)); budgetMargin.AddThemeConstantOverride("margin_right", Ui.Px(6));
        budgetMargin.AddChild(budget); row.AddChild(budgetMargin);
        var headline = new HBoxContainer(); headline.AddThemeConstantOverride("separation", Ui.Px(8)); budget.AddChild(headline);
        var caption = Ui.Caps("Budget", Ui.BarMuted); caption.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; headline.AddChild(caption);
        _drafted = Ui.Text("", 24, Ui.BarText, Ui.SlabBold); headline.AddChild(_drafted);
        _draftedOf = Ui.Text("", 14, Ui.BarMuted); _draftedOf.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; headline.AddChild(_draftedOf);
        headline.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _left = Ui.Text("", 14, Ui.Good, Ui.BodyBold); _left.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; headline.AddChild(_left);
        _budgetBar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, Ui.S(10)), MaxValue = 1 };
        _budgetBar.AddThemeStyleboxOverride("background", Ui.Box(Ui.BarDeep, 5));
        _budgetBar.AddThemeStyleboxOverride("fill", Ui.Box(Ui.Gold, 5));
        budget.AddChild(_budgetBar);
        _breakdown = Ui.Text("", 12.5f, Ui.BarMuted); budget.AddChild(_breakdown);
        _breakdown.ClipText = true; _breakdown.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _breakdown.CustomMinimumSize = new Vector2(1, 0);

        _start = new Button { CustomMinimumSize = new Vector2(Ui.S(236), 0), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        _start.Pressed += _nav.ConfirmStart; row.AddChild(_start);
        var startFace = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        startFace.SetAnchorsPreset(Control.LayoutPreset.FullRect); startFace.AddThemeConstantOverride("separation", Ui.Px(2));
        _start.AddChild(startFace);
        var startLine = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        startLine.AddThemeConstantOverride("separation", Ui.Px(8)); startFace.AddChild(startLine);
        _startLock = Ui.IconRect("lock", 18, Ui.BarText); _startLock.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; startLine.AddChild(_startLock);
        _startTitle = Ui.Text("Start festival", 21, Ui.BarText, Ui.SlabBold); startLine.AddChild(_startTitle);
        _startDetail = Ui.Text("", 13, Ui.Warn, Ui.BodyBold); _startDetail.HorizontalAlignment = HorizontalAlignment.Center; startFace.AddChild(_startDetail);

        BuildReadiness(layer, size);
        BuildReceipt(layer, size);
    }

    private static readonly Color ReceiptPaper = new("fffdf6");

    /// <summary>The Supplies view's "Paid at Start" receipt, in the readiness card's place.</summary>
    private void BuildReceipt(CanvasLayer layer, Vector2 size)
    {
        _receipt = new PanelContainer { Position = new Vector2(size.X - Ui.Gutter - Ui.S(302), Ui.ContentTop), Size = new Vector2(Ui.S(302), 0), Theme = HudTheme(), Visible = false };
        var paper = Ui.Box(ReceiptPaper, 0, padX: 20, padY: 18, shadow: 14, shadowAlpha: 0.38f);
        paper.CornerRadiusTopLeft = paper.CornerRadiusTopRight = Ui.Px(4); paper.ContentMarginBottom = Ui.S(26);
        _receipt.AddThemeStyleboxOverride("panel", paper);
        _receipt.Draw += () =>
        {
            // A torn edge: small triangles hanging from the bottom.
            var step = Ui.S(12); var depth = Ui.S(6); var bottom = _receipt.Size.Y - 1;
            for (var x = 0f; x < _receipt.Size.X; x += step)
                _receipt.DrawColoredPolygon([new Vector2(x, bottom), new Vector2(Math.Min(x + step, _receipt.Size.X), bottom), new Vector2(Math.Min(x + step / 2, _receipt.Size.X), bottom + depth)], ReceiptPaper);
        };
        layer.AddChild(_receipt);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Ui.Px(2)); _receipt.AddChild(box);
        var caption = Ui.Caps("Paid at Start", Ui.InkMuted); caption.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(caption);
        var title = Ui.Heading("Your festival draft", 21); title.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(title);
        box.AddChild(Dashes());
        _receiptLines = new VBoxContainer(); _receiptLines.AddThemeConstantOverride("separation", Ui.Px(7)); box.AddChild(_receiptLines);
        box.AddChild(Dashes());
        var total = new HBoxContainer(); box.AddChild(total);
        var totalCaption = Ui.Heading("Total", 18); totalCaption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; totalCaption.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; total.AddChild(totalCaption);
        _receiptTotal = Ui.Heading("", 24); total.AddChild(_receiptTotal);
        var after = new HBoxContainer(); box.AddChild(after);
        var afterCaption = Ui.Text("Cash after Start", 14, Ui.InkMuted); afterCaption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; after.AddChild(afterCaption);
        _receiptAfter = Ui.Text("", 14, Ui.TealDeep, Ui.BodyBold); after.AddChild(_receiptAfter);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        _receiptCheck = Ui.Text("", 13, Ui.TealDeep, Ui.BodyBold); box.AddChild(_receiptCheck);
    }

    private static Control Dashes()
    {
        var rule = new Control { CustomMinimumSize = new Vector2(0, Ui.S(20)), MouseFilter = Control.MouseFilterEnum.Ignore };
        rule.Draw += () => rule.DrawDashedLine(new Vector2(0, rule.Size.Y / 2), new Vector2(rule.Size.X, rule.Size.Y / 2), new Color("c9b994"), Ui.S(1.5f), Ui.S(5));
        return rule;
    }

    private void RefreshReceipt(GameSession session, PlanCosts costs, int missing, int checks)
    {
        if (session.CapturePreparationPlan() is not { } plan || session.CapturePreparation() is not { } p) return;
        var offers = session.GetPreparationOffers().ToDictionary(offer => offer.Id);
        var lines = new List<(string Label, long Pennies)>();
        if (p.BuildPlacements.Length > 0) lines.Add(($"Site services × {p.BuildPlacements.Length}", costs.Services));
        var acts = plan.ActIds.Count(id => id != "");
        if (acts > 0) lines.Add(($"Acts × {acts}", costs.Acts));
        foreach (var id in plan.OfferIds)
        {
            var offer = offers[id];
            var who = offer.Name.Split(':')[0];
            var label = offer.Category switch
            {
                "staff" => $"{who} · sound engineer",
                "maintenance" => $"{who} · maintenance",
                "extra-medic" => $"{(session.GetOptionalStaffOfferProfile(ResponseRole.Medic)?.Name ?? who).Split(' ')[0]} · medic",
                "extra-steward" => $"{(session.GetOptionalStaffOfferProfile(ResponseRole.Steward)?.Name ?? who).Split(' ')[0]} · steward",
                "equipment" => id == "equipment.rent" ? "Sound rig rental" : "Sound rig purchase",
                _ => offer.Name,
            };
            if (offer.Category != "equipment") lines.Add((label, offer.PricePennies));
        }
        var items = plan.Chips + plan.SoftDrinks + plan.Beers;
        if (items > 0) lines.Add(($"Stock · {items} items", costs.Stock));
        foreach (var id in plan.OfferIds.Where(id => offers[id].Category == "equipment"))
            lines.Add((id == "equipment.rent" ? "Sound rig rental" : "Sound rig purchase", offers[id].PricePennies));
        var key = string.Join("|", lines.Select(line => line.Label + line.Pennies));
        if (key != _receiptKey)
        {
            _receiptKey = key;
            foreach (var child in _receiptLines!.GetChildren()) { _receiptLines.RemoveChild(child); child.QueueFree(); }
            if (lines.Count == 0) _receiptLines.AddChild(Ui.Text("Nothing drafted yet.", 14, Ui.InkMuted));
            foreach (var (label, pennies) in lines)
            {
                var row = new HBoxContainer(); _receiptLines.AddChild(row);
                var name = Ui.Text(label, 14, Ui.Ink); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(name);
                row.AddChild(Ui.Text(FestivalCurrency.Format(pennies), 14, Ui.Ink, Ui.BodyBold));
            }
        }
        _receiptTotal!.Text = FestivalCurrency.Format(session.PreparationPlanCost);
        var left = session.PreparationRemainingCash;
        _receiptAfter!.Text = FestivalCurrency.Format(left);
        _receiptAfter.AddThemeColorOverride("font_color", left >= 0 ? Ui.TealDeep : Ui.Alert);
        _receiptCheck!.Text = missing == 0 ? $"✓  All {checks} opening checks pass" : $"!  {missing} of {checks} opening checks still open";
        _receiptCheck.AddThemeColorOverride("font_color", missing == 0 ? Ui.TealDeep : new Color("7a3312"));
        _receiptCheck.AddThemeStyleboxOverride("normal", Ui.Box(missing == 0 ? Ui.TealWash : Ui.AlertWash, 6, padX: 10, padY: 8));
    }

    private void BuildReadiness(CanvasLayer layer, Vector2 size)
    {
        _readiness = new PanelContainer { Position = new Vector2(size.X - Ui.Gutter - Ui.S(302), Ui.ContentTop),
            Size = new Vector2(Ui.S(302), 0), Theme = HudTheme() };
        _readiness.AddThemeStyleboxOverride("panel", Ui.Sheet(16, 14));
        layer.AddChild(_readiness);
        var card = new VBoxContainer(); card.AddThemeConstantOverride("separation", Ui.Px(2)); _readiness.AddChild(card);
        var heading = new HBoxContainer(); heading.AddThemeConstantOverride("separation", Ui.Px(12)); card.AddChild(heading);
        _ring = new ProgressRing { CustomMinimumSize = Ui.S(48, 48), Track = Ui.PaperRule, Fill = Ui.Teal, TextColor = Ui.Ink,
            Thickness = Ui.S(5), Font = Ui.SlabBold, FontSize = Ui.Px(15) };
        heading.AddChild(_ring);
        var words = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; words.AddThemeConstantOverride("separation", 0); heading.AddChild(words);
        _readinessTitle = Ui.Heading("", 21); words.AddChild(_readinessTitle);
        _readinessDetail = Ui.Text("", 13, Ui.InkMuted); words.AddChild(_readinessDetail);
        card.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        _readinessRows = new VBoxContainer(); _readinessRows.AddThemeConstantOverride("separation", Ui.Px(2)); card.AddChild(_readinessRows);
        var rule = new ColorRect { Color = Ui.PaperRule, CustomMinimumSize = new Vector2(0, 1) };
        card.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(6)) }); card.AddChild(rule);
        var note = Ui.Text("Equipment and stock are optional.", 12.5f, Ui.InkMuted); card.AddChild(note);
    }

    private static string Destination(PreparationStartOwner owner) => owner switch
    {
        PreparationStartOwner.Programme => "Programme",
        PreparationStartOwner.Staff => "Staff",
        _ => "Build",
    };

    /// <summary>Checklist wording: what is in place, and what to do when it is not.</summary>
    private static (string Done, string Todo) Wording(PreparationStartRequirement requirement) => requirement.Id switch
    {
        "water" => ("Water tap", "Place a water tap"),
        "toilet" => ("Toilet", "Place a toilet"),
        "first-aid" => ("First aid", "Place first aid"),
        "steward-post" => ("Steward post", "Place a steward post"),
        "programme" => ("Three acts booked", "Book three acts"),
        "staff" => ("Sound engineer hired", "Hire a sound engineer"),
        "budget" => ("Within budget", "Bring the draft within budget"),
        _ => (requirement.Label, requirement.Label),
    };

    private static string Count(int value) => value switch
    {
        1 => "One thing", 2 => "Two things", 3 => "Three things", 4 => "Four things", 5 => "Five things",
        6 => "Six things", 7 => "Seven things", _ => $"{value} things",
    };

    public void Refresh()
    {
        if (_dock is null) return;
        var session = _hud.Session;
        var preparing = session.PreparedStatus == PreparationStatus.Preparing;
        var shown = preparing && session.CapturePerks()?.Pending != true;
        _dock.Visible = shown;
        var supplies = _nav.OpenDestinationName == "Supplies";
        _readiness!.Visible = shown && !_nav.ReadinessCovered && !supplies;
        _receipt!.Visible = shown && !_nav.ReadinessCovered && supplies;
        if (!shown) return;
        var requirements = session.GetPreparationStartRequirements();
        var missing = requirements.Where(item => !item.Complete).ToArray();
        var costs = PlanCosts.Of(session);
        RefreshFolders(session, missing, costs);

        var funds = session.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        var drafted = session.PreparationPlanCost;
        var left = session.PreparationRemainingCash;
        _drafted!.Text = FestivalCurrency.Format(drafted);
        _draftedOf!.Text = $"drafted of {FestivalCurrency.Format(funds)}";
        _left!.Text = left >= 0 ? $"{FestivalCurrency.Format(left)} left" : $"{FestivalCurrency.Format(-left)} over";
        _left.AddThemeColorOverride("font_color", left >= 0 ? Ui.Good : Ui.Warn);
        _budgetBar!.Value = funds <= 0 ? 0 : Math.Clamp((double)drafted / funds, 0, 1);
        _breakdown!.Text = _nav.Placing ? _hud.Message :
            $"Build {FestivalCurrency.Format(costs.Services)} · acts {FestivalCurrency.Format(costs.Acts)} · staff {FestivalCurrency.Format(costs.Staff)} · " +
            $"supplies {FestivalCurrency.Format(costs.Supplies)} — paid only at Start";

        var issue = _nav.Placing ? "Finish or cancel placement" :
            missing.FirstOrDefault()?.Detail ?? session.ValidateCommand(_hud.Host.Envelope(new StartPreparedEditionCommand()))?.Message;
        var ready = issue is null;
        _start!.Disabled = !ready;
        _start.TooltipText = issue ?? "Review the full setup charge, then open the festival.";
        var startStyle = ready ? Ui.Box(Ui.Gold, 10, shadow: 2) : Ui.Box(new Color("1c3a31"), 10, new Color(Ui.Gold, 0.55f), 2);
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            _start.AddThemeStyleboxOverride(state, state == "hover" && ready ? Ui.Box(Ui.Gold.Lightened(0.12f), 10) : startStyle);
        _startLock!.Visible = !ready;
        _startTitle!.AddThemeColorOverride("font_color", ready ? Ui.Bar : new Color(Ui.BarText, 0.8f));
        _startDetail!.AddThemeColorOverride("font_color", ready ? Ui.Bar : Ui.Warn);
        _startDetail.Text = ready ? $"Pay {FestivalCurrency.Format(drafted)} and open the gates"
            : _nav.Placing ? "Finish placement" : missing.Length > 0 ? $"{missing.Length} task{(missing.Length == 1 ? "" : "s")} left" : "Not ready";

        RefreshReadiness(requirements, missing);
        if (_receipt.Visible) RefreshReceipt(session, costs, missing.Length, requirements.Count);
    }

    private void RefreshFolders(GameSession session, PreparationStartRequirement[] missing, PlanCosts costs)
    {
        var selected = _nav.OpenDestinationName;
        var plan = session.CapturePreparationPlan();
        var placed = session.CapturePreparation()?.BuildPlacements.Length ?? 0;
        var acts = plan?.ActIds.Count(id => id != "") ?? 0;
        foreach (var (name, (button, status, notch)) in _folders)
        {
            var active = selected == name;
            var owed = missing.Where(item => Destination(item.Owner) == name && (name != "Build" || item.Id != "budget")).ToArray();
            var (text, attention, done) = name switch
            {
                "Build" => ($"{placed} placed", owed.Length > 0, owed.Length == 0),
                "Programme" => ($"{acts} of 3 acts", owed.Length > 0, owed.Length == 0),
                "Staff" => (owed.Length > 0 ? "Sound needed" : "Sound hired", owed.Length > 0, owed.Length == 0),
                _ => (costs.Supplies > 0 ? $"{FestivalCurrency.Format(costs.Supplies)} planned" : "Optional", false, false),
            };
            status.Text = ((done ? "✓ " : "") + text).ToUpperInvariant();
            var ink = active ? Ui.Bar : Ui.BarText;
            status.AddThemeColorOverride("font_color", active ? Ui.Bar : attention ? Ui.Warn : done ? Ui.Good : Ui.BarMuted);
            button.GetNode<TextureRect>("Face/Icon").SelfModulate = ink;
            button.GetNode<Label>("Face/Title").AddThemeColorOverride("font_color", ink);
            var fill = active ? Ui.Box(Ui.Gold, 8) : Ui.Box(Ui.BarRaised, 8, new Color(Ui.BarText, 0.14f), 1);
            foreach (var state in new[] { "normal", "pressed", "focus" }) button.AddThemeStyleboxOverride(state, fill);
            button.AddThemeStyleboxOverride("hover", active ? fill : Ui.Box(Ui.BarRaised.Lightened(0.08f), 8, new Color(Ui.BarText, 0.2f), 1));
            notch.Visible = active;
            button.TooltipText = owed.Length == 0 ? $"Open {name}" : $"{name}: required before Start festival. " + string.Join(" ", owed.Select(item => item.Detail));
        }
    }

    private void RefreshReadiness(IReadOnlyList<PreparationStartRequirement> requirements, PreparationStartRequirement[] missing)
    {
        var done = requirements.Count - missing.Length;
        _ring!.Fraction = requirements.Count == 0 ? 0 : (float)done / requirements.Count;
        _ring.Text = $"{done}/{requirements.Count}";
        _readinessTitle!.Text = missing.Length == 0 ? "Ready to open" : "Ready to open?";
        _readinessDetail!.Text = missing.Length == 0 ? "Everything required is in place" : $"{Count(missing.Length)} left before Start";
        var key = string.Join("|", requirements.Select(item => item.Id + ":" + item.Complete));
        if (key == _readinessKey) return;
        _readinessKey = key;
        foreach (var child in _readinessRows!.GetChildren()) child.QueueFree();
        foreach (var requirement in requirements)
            _readinessRows.AddChild(ReadinessRow(requirement));
    }

    private PanelContainer ReadinessRow(PreparationStartRequirement requirement)
    {
        var (doneText, todoText) = Wording(requirement);
        var destination = Destination(requirement.Owner);
        var complete = requirement.Complete;
        var row = new PanelContainer { CustomMinimumSize = new Vector2(0, Ui.S(complete ? 30 : 34)), MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = complete ? $"{doneText} · open {destination} to review" : requirement.Detail };
        var rest = complete ? Ui.Box(new Color(0, 0, 0, 0), 6, padX: 6) : Ui.Box(Ui.AlertWash, 6, padX: 6);
        var hover = complete ? Ui.Box(new Color(0, 0, 0, 0.04f), 6, padX: 6) : Ui.Box(Ui.AlertWash.Darkened(0.04f), 6, padX: 6);
        row.AddThemeStyleboxOverride("panel", rest);
        row.MouseEntered += () => row.AddThemeStyleboxOverride("panel", hover);
        row.MouseExited += () => row.AddThemeStyleboxOverride("panel", rest);
        row.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { _nav.OpenDestination(destination); row.AcceptEvent(); }
        };
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", Ui.Px(10)); row.AddChild(line);
        var mark = new PanelContainer { CustomMinimumSize = Ui.S(20, 20), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        mark.AddThemeStyleboxOverride("panel", Ui.Box(complete ? Ui.Teal : Ui.Alert, 10));
        line.AddChild(mark);
        if (complete)
        {
            var tick = Ui.IconRect("check", 13, Colors.White); tick.SizeFlagsHorizontal = tick.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; mark.AddChild(tick);
        }
        else
        {
            var bang = Ui.Text("!", 13, Colors.White, Ui.BodyBold); bang.HorizontalAlignment = HorizontalAlignment.Center; bang.VerticalAlignment = VerticalAlignment.Center;
            mark.AddChild(bang);
        }
        var label = Ui.Text(complete ? doneText : todoText, 14, Ui.Ink, complete ? null : Ui.BodyBold);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; label.VerticalAlignment = VerticalAlignment.Center; line.AddChild(label);
        if (!complete)
        {
            var link = Ui.Text(destination, 13, Ui.Link, Ui.BodySemi); link.VerticalAlignment = VerticalAlignment.Center; line.AddChild(link);
            var chevron = Ui.IconRect("chevron-right", 15, Ui.Link); chevron.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; line.AddChild(chevron);
        }
        return row;
    }

    /// <summary>The checklist card, for HUD hit-testing.</summary>
    public Control? Readiness => _readiness!.Visible ? _readiness : _receipt;
    public bool Visible => _dock?.Visible == true;
}
