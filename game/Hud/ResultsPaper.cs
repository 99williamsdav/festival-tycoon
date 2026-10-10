using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The morning after: the Lower Wittering Gazette's review, the festival accounts and how every set went, on a sheet
/// over a blurred view of the field, with Newspaper, Accounts and Performances tabs and Return to menu.
/// </summary>
internal sealed class ResultsPaper(IHudHost _hud)
{
    private static readonly Color Newsprint = new("f4eede");
    private static readonly Color Ledger = new("f7f2e4");
    private static readonly Color Rule = new("e8dfc8");
    private static readonly Color Body = new("2e3833");
    private static readonly Color Programme = new("f6f0e2");

    /// <summary>The sheet's pages, one per tab.</summary>
    public enum Page { Newspaper, Accounts, Performances }

    private CanvasLayer? _layer;
    private PanelContainer? _sheet;
    private ScrollContainer? _scroll;
    private VBoxContainer? _body;
    private Button? _newspaperTab;
    private Button? _accountsTab;
    private Button? _performancesTab;
    private Page _page;
    private readonly int[] _scrolls = new int[3];
    private ImageTexture? _field;

    public bool IsOpen => _layer is not null;

    public void Close() { _layer?.QueueFree(); _layer = null; _field = null; }

    /// <summary>
    /// Opens on the newspaper. <paramref name="returnToMenu"/> reports whether leaving succeeded; <paramref name="nextFestival"/>,
    /// when the festival unlocked the next tier, opens it the same way.
    /// </summary>
    public void Open(Node parent, Func<bool> returnToMenu, Func<bool>? nextFestival = null)
    {
        _page = Page.Newspaper; Array.Clear(_scrolls);
        // The field as it looks now becomes the backdrop and the front-page photograph.
        var image = parent.GetViewport().GetTexture().GetImage();
        _field = image is null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
        _layer = new CanvasLayer { Layer = 19 }; parent.AddChild(_layer);
        var size = _hud.Viewport.GetVisibleRect().Size;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop }; root.SetAnchorsPreset(Control.LayoutPreset.FullRect); _layer.AddChild(root);
        root.AddChild(new ColorRect { Color = Ui.BarDeep, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore });
        if (_field is not null)
            root.AddChild(new TextureRect { Texture = _field, Size = size, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale, Material = Shaded(BlurShader), MouseFilter = Control.MouseFilterEnum.Ignore });
        root.AddChild(new ColorRect { Color = new Color(Ui.BarDeep, 0.8f), Size = size, MouseFilter = Control.MouseFilterEnum.Ignore });

        var title = new VBoxContainer { Position = Ui.S(36, 30) }; title.AddThemeConstantOverride("separation", Ui.Px(2)); root.AddChild(title);
        title.AddChild(Ui.Caps("The morning after", Ui.Gold));
        title.AddChild(Ui.Heading("Festival results", 26, Ui.BarText));

        var sheetTop = Ui.S(30);
        var sheetHeight = Math.Min(Ui.S(660), size.Y - 2 * sheetTop);
        var sheetLeft = Math.Max(Ui.S(230), (size.X - Ui.S(830)) / 2 - Ui.S(20));
        _sheet = new PanelContainer { Position = new Vector2(sheetLeft, sheetTop), Size = new Vector2(Ui.S(830), sheetHeight) };
        root.AddChild(_sheet);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FocusMode = Control.FocusModeEnum.All };
        Ui.SlimScrollbar(_scroll); _sheet.AddChild(_scroll);
        _body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 0);
        var gutter = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; gutter.AddThemeConstantOverride("margin_right", Ui.Px(14));
        gutter.AddChild(_body); _scroll.AddChild(gutter);

        var tabs = new VBoxContainer { Position = new Vector2(sheetLeft + Ui.S(830), sheetTop + Ui.S(40)) };
        tabs.AddThemeConstantOverride("separation", Ui.Px(6)); root.AddChild(tabs);
        _newspaperTab = Tab("Newspaper", "file-text", () => ShowPage(Page.Newspaper)); _newspaperTab.Name = "NewspaperTab"; _newspaperTab.TooltipText = "Public festival review";
        _accountsTab = Tab("Accounts", "chart-column", () => ShowPage(Page.Accounts)); _accountsTab.Name = "AccountsTab"; _accountsTab.TooltipText = "Income, expenditure and cash";
        _performancesTab = Tab("Performances", "music", () => ShowPage(Page.Performances)); _performancesTab.Name = "PerformancesTab";
        _performancesTab.TooltipText = "How every set went, and how the bands feel about you now";
        tabs.AddChild(_newspaperTab); tabs.AddChild(_accountsTab); tabs.AddChild(_performancesTab);

        Button? menu = null;
        menu = Ui.Style(new Button { Text = "Return to menu", Name = "ReturnToMenu", MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Primary, 15, 8);
        menu.Pressed += () =>
        {
            if (returnToMenu()) return;
            menu!.Text = "Save failed · retry menu";
            menu.TooltipText = _hud.Message;
        };
        menu.Size = Ui.S(188, 46);
        menu.Position = new Vector2(Math.Min(sheetLeft + Ui.S(846), size.X - Ui.S(204)), sheetTop + sheetHeight - Ui.S(66));
        root.AddChild(menu);
        if (nextFestival is not null && _hud.Session.NextFestivalCarryOver is { } carry)
        {
            // Completing a festival unlocks the next tier: cash, debt, reputation and owned kit come forward.
            var tier = carry.FromTier + 1;
            Button? next = null;
            next = Ui.Style(new Button { Text = "Next festival", Name = "NextFestival", MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                TooltipText = $"Tier {tier}: {FestivalTickets.Sold(tier)} guests at {FestivalCurrency.Format(FestivalTickets.PricePennies(tier))}. " +
                    $"Brings {FestivalCurrency.Format(carry.CashPennies)} cash, {FestivalCurrency.Format(carry.DebtPennies)} of loan still owed, " +
                    "your reputation and owned kit. Perks, staff, bookings, stock and the build start fresh." +
                    (tier >= GameSession.PondStageFromTier ? "\nWe've upgraded with a second stage, the Pond Stage, which comes with its own generator." : "") }, Ui.ButtonKind.Primary, 15, 8);
            next.Pressed += () =>
            {
                if (nextFestival()) return;
                next!.Text = "Save failed · retry";
                next.TooltipText = _hud.Message;
            };
            next.Size = menu.Size;
            next.Position = menu.Position - new Vector2(0, Ui.S(58));
            root.AddChild(next);
        }
        ShowPage(Page.Newspaper, first: true);
    }

    private static Button Tab(string text, string icon, Action action)
    {
        var tab = new Button { Text = text, Icon = Ui.Icon(icon), Alignment = HorizontalAlignment.Left, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            CustomMinimumSize = Ui.S(170, 46) };
        tab.Pressed += action;
        tab.AddThemeFontOverride("font", Ui.BodyBold); tab.AddThemeFontSizeOverride("font_size", Ui.Px(15));
        tab.AddThemeConstantOverride("icon_max_width", Ui.Px(18)); tab.AddThemeConstantOverride("h_separation", Ui.Px(8));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_disabled_color",
                     "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color", "icon_disabled_color" })
            tab.AddThemeColorOverride(state, Ui.Ink);
        return tab;
    }

    private static void StyleTab(Button tab, bool active, Color paper)
    {
        var box = Ui.Box(active ? paper : new Color("cfc4a8"), 0, padX: 14);
        box.CornerRadiusTopRight = box.CornerRadiusBottomRight = Ui.Px(8);
        if (active) { box.ShadowColor = new Color(0, 0, 0, 0.35f); box.ShadowSize = Ui.Px(8); box.ShadowOffset = Ui.S(4, 6); }
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" }) tab.AddThemeStyleboxOverride(state, box);
        tab.CustomMinimumSize = Ui.S(active ? 170 : 160, 46);
        tab.Disabled = active;
    }

    private const string BlurShader = @"shader_type canvas_item;
void fragment() {
    vec2 step = TEXTURE_PIXEL_SIZE * 4.0;
    vec4 sum = vec4(0.0);
    for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++) sum += texture(TEXTURE, UV + vec2(float(x), float(y)) * step);
    vec3 c = sum.rgb / 25.0;
    float g = dot(c, vec3(0.299, 0.587, 0.114));
    COLOR = vec4(mix(vec3(g), c, 0.6), 1.0);
}";
    private const string PhotoShader = @"shader_type canvas_item;
void fragment() {
    vec4 c = texture(TEXTURE, UV);
    float g = clamp((dot(c.rgb, vec3(0.299, 0.587, 0.114)) - 0.5) * 1.15 + 0.5, 0.0, 1.0);
    COLOR = vec4(mix(vec3(g), vec3(g) * vec3(1.07, 0.99, 0.86), 0.15), 1.0);
}";
    private static ShaderMaterial Shaded(string code) => new() { Shader = new Shader { Code = code } };

    /// <summary>The newspaper, or the accounts.</summary>
    public void Show(bool accounts, bool first = false) => ShowPage(accounts ? Page.Accounts : Page.Newspaper, first);

    public void ShowPage(Page page, bool first = false)
    {
        if (_body is null || _scroll is null) return;
        if (!first) _scrolls[(int)_page] = _scroll.ScrollVertical;
        _page = page;
        foreach (var child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        var paper = page switch { Page.Accounts => Ledger, Page.Performances => Programme, _ => Newsprint };
        _sheet!.AddThemeStyleboxOverride("panel", Ui.Box(paper, 0, padX: page == Page.Newspaper ? 34 : 30, padY: 22, shadow: 22, shadowAlpha: 0.55f));
        switch (page)
        {
            case Page.Accounts: RenderFestivalAccounts(_body); break;
            case Page.Performances: RenderPerformances(_body); break;
            default: RenderFestivalNewspaper(_body); break;
        }
        StyleTab(_newspaperTab!, page == Page.Newspaper, Newsprint);
        StyleTab(_accountsTab!, page == Page.Accounts, Ledger);
        StyleTab(_performancesTab!, page == Page.Performances, Programme);
        var position = _scrolls[(int)page];
        _scroll.ScrollVertical = position;
        _scroll.SetDeferred("scroll_vertical", position);
    }

    /// <summary>Scrolls the sheet, for a look further down the page.</summary>
    public void ScrollTo(int position) { if (_scroll is not null) { _scroll.ScrollVertical = position; _scroll.SetDeferred("scroll_vertical", position); } }

    private static Control Gap(float mockup) => new() { CustomMinimumSize = new Vector2(0, Ui.S(mockup)), MouseFilter = Control.MouseFilterEnum.Ignore };
    private static ColorRect Line(Color colour, float thickness) => new() { Color = colour, CustomMinimumSize = new Vector2(0, Math.Max(1, Ui.S(thickness))), MouseFilter = Control.MouseFilterEnum.Ignore };
    private static Label Paragraph(string text, float size, Color colour, Font? font = null)
    {
        var label = Ui.Text(text, size, colour, font); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; return label;
    }

    private void RenderFestivalNewspaper(VBoxContainer body)
    {
        var result = _hud.Session.CompletedFestivalResult!;
        body.AddChild(Line(Ui.Ink, 3)); body.AddChild(Gap(8));
        var masthead = Ui.Heading("The Lower Wittering Gazette", 46); masthead.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(masthead);
        body.AddChild(Gap(6)); body.AddChild(Line(Ui.Ink, 1)); body.AddChild(Gap(6));
        var edition = new HBoxContainer(); body.AddChild(edition);
        foreach (var (text, align) in new[] { ("Local edition", HorizontalAlignment.Left), ("Festival review", HorizontalAlignment.Center), ("Day after the festival", HorizontalAlignment.Right) })
        {
            var label = Ui.Caps(text, new Color("3a4640")); label.HorizontalAlignment = align; label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; edition.AddChild(label);
        }
        body.AddChild(Gap(6)); body.AddChild(Line(Ui.Ink, 1)); body.AddChild(Gap(2)); body.AddChild(Line(Ui.Ink, 1));
        body.AddChild(Gap(14));
        var headline = result.Stars switch { 5 => "A day to remember", 4 => "Festival hits the right note",
            3 => "A mixed reception in the field", 2 => "Festival leaves room for improvement",
            1 => "A difficult day in the field", _ => "The field falls quiet" };
        var head = Ui.Heading(headline, 44); head.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(head);
        // A completed festival never had a death (that ends the day), so a poor review can always offer this much.
        if (result.Stars is <= 2) { body.AddChild(Gap(4)); body.AddChild(Paragraph("At least no one died.", 20, new Color("3a4640"), Ui.Slab)); }
        body.AddChild(Gap(16));

        var lead = new HBoxContainer(); lead.AddThemeConstantOverride("separation", Ui.Px(22)); body.AddChild(lead);
        var verdict = new PanelContainer { CustomMinimumSize = new Vector2(Ui.S(250), 0) };
        verdict.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Ink, 0, padX: 16, padY: 14)); lead.AddChild(verdict);
        var verdictBox = new VBoxContainer(); verdictBox.AddThemeConstantOverride("separation", Ui.Px(8)); verdict.AddChild(verdictBox);
        verdictBox.AddChild(Ui.Caps("Our verdict", Ui.Gold));
        var stars = new HBoxContainer(); stars.AddThemeConstantOverride("separation", Ui.Px(4)); verdictBox.AddChild(stars);
        for (var i = 0; i < 5; i++)
            stars.AddChild(Ui.IconRect("star", 22, i < (result.Stars ?? 0) ? Ui.Gold : new Color(Ui.Gold, 0.35f)));
        var score = new HBoxContainer(); score.AddThemeConstantOverride("separation", Ui.Px(8)); verdictBox.AddChild(score);
        score.AddChild(Ui.Heading(result.Stars?.ToString() ?? "–", 40, Newsprint));
        var outOf = Ui.Text(result.Stars is null ? "unrated · no admitted guests" : "out of 5", 16, new Color("c9d1c7")); outOf.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; score.AddChild(outOf);
        verdictBox.AddChild(Ui.Text("Guest satisfaction", 14, new Color("c9d1c7")));
        var meter = new HBoxContainer(); meter.AddThemeConstantOverride("separation", Ui.Px(8)); verdictBox.AddChild(meter);
        var bar = new ProgressBar { ShowPercentage = false, MaxValue = 100, Value = (double)(result.SatisfactionPercent ?? 0), CustomMinimumSize = new Vector2(0, Ui.S(8)),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        bar.AddThemeStyleboxOverride("background", Ui.Box(new Color("3a4640"), 4)); bar.AddThemeStyleboxOverride("fill", Ui.Box(Ui.Gold, 4));
        meter.AddChild(bar); meter.AddChild(Ui.Heading(result.SatisfactionPercent is { } percent ? $"{percent:0.0}%" : "–", 18, Newsprint));
        var preparation = _hud.Session.CapturePreparation();
        if (preparation?.StandingBefore is { } before)
        {
            var reputation = new HBoxContainer(); reputation.AddThemeConstantOverride("separation", Ui.Px(8)); verdictBox.AddChild(reputation);
            var caption = Ui.Text("Festival reputation", 14, new Color("c9d1c7")); caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; reputation.AddChild(caption);
            reputation.AddChild(Ui.Heading($"{before.Reputation} → {preparation.Reputation}", 18, Ui.Gold));
        }

        var story = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; story.AddThemeConstantOverride("separation", Ui.Px(4)); lead.AddChild(story);
        if (_field is not null)
        {
            var region = new Rect2(_field.GetSize() * new Vector2(0.18f, 0.28f), _field.GetSize() * new Vector2(0.5f, 0.3f));
            story.AddChild(new TextureRect { Texture = new AtlasTexture { Atlas = _field, Region = region }, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(0, Ui.S(150)), ClipContents = true, Material = Shaded(PhotoShader) });
            var caption = Ui.Text("The festival field, as the last guests headed home.", 12, new Color("3a4640")); story.AddChild(caption);
        }
        story.AddChild(Paragraph($"The rating reflects the final satisfaction of {result.GuestCount} admitted guests, including early leavers.", 17, Ui.Ink, Ui.Slab));
        if (preparation?.StandingBefore is { } previous)
        {
            var scenes = Enumerable.Range(0, FestivalGenre.Count).Where(genre => preparation.SceneCredibility[genre] != previous.SceneCredibility[genre])
                .Select(genre => $"{FestivalGenre.Name(genre).ToLowerInvariant()} {previous.SceneCredibility[genre]} → {preparation.SceneCredibility[genre]}").ToArray();
            if (scenes.Length > 0)
                story.AddChild(Paragraph("Word gets around the scenes: " + string.Join(", ", scenes) + ".", 13.5f, Body));
        }
        body.AddChild(Gap(16)); body.AddChild(Line(Ui.Ink, 1)); body.AddChild(Gap(12));

        var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", Ui.Px(24)); body.AddChild(columns);
        var crowd = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; crowd.AddThemeConstantOverride("separation", Ui.Px(6)); columns.AddChild(crowd);
        crowd.AddChild(Ui.Heading("The crowd's verdict", 22));
        crowd.AddChild(Paragraph("Every admitted guest has physically left the farm. Their final satisfaction shapes the public review.", 14.5f, Body));
        var around = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; around.AddThemeConstantOverride("separation", Ui.Px(8)); columns.AddChild(around);
        around.AddChild(Ui.Heading("Around the field", 22));
        var facts = new HBoxContainer(); facts.AddThemeConstantOverride("separation", Ui.Px(8)); around.AddChild(facts);
        foreach (var (value, singular, plural) in new[] { (result.BeersFinished, "beer finished", "beers finished"), (result.Fights, "fight", "fights"), (result.MedicalCollapses, "medical collapse", "medical collapses") })
        {
            var box = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            box.AddThemeStyleboxOverride("panel", Ui.Box(new Color(0, 0, 0, 0), 0, Ui.Ink, 1, 10, 8));
            var words = new VBoxContainer(); words.AddThemeConstantOverride("separation", Ui.Px(2)); box.AddChild(words);
            words.AddChild(Ui.Heading(value?.ToString() ?? "–", 28));
            words.AddChild(Ui.Text(value is null ? "not recorded" : value == 1 ? singular : plural, 12.5f, new Color("3a4640")));
            facts.AddChild(box);
        }
        around.AddChild(Paragraph("Fights include encounters involving a guest; collapses count guest rescue deadlines. Fight knockouts are excluded.", 11.5f, Ui.InkMuted));
        body.AddChild(Gap(18)); body.AddChild(Line(new Color("c9bfa6"), 1)); body.AddChild(Gap(6));
        body.AddChild(Paragraph("Rating guide: below 20 / 40 / 60 / 80% earns 1 / 2 / 3 / 4 stars; 80% or above earns 5. Provisional thresholds.", 11.5f, Ui.InkMuted));
    }

    /// <summary>
    /// How every set went, in timetable order across the stages: when it was due and ran, the crowd, how it went and
    /// what went wrong, a few words from people who were there, and what it did for the band's relationship with you.
    /// </summary>
    private void RenderPerformances(VBoxContainer body)
    {
        var session = _hud.Session;
        var records = session.PerformanceRecords
            .OrderBy(record => record.ScheduledStartTick).ThenBy(record => FestivalStages.IndexOf(FestivalStages.All, record.StageId)).ToArray();
        var names = session.CapturePreparation()?.People.ToDictionary(person => person.AgentId, person => person.Name) ?? new();
        var opened = session.CapturePreparation()?.StartedTick ?? 0;

        var top = new PanelContainer();
        var underline = Ui.Box(new Color(0, 0, 0, 0), 0); underline.BorderColor = Ui.Ink; underline.BorderWidthBottom = Ui.Px(2); underline.ContentMarginBottom = Ui.S(8);
        top.AddThemeStyleboxOverride("panel", underline); body.AddChild(top);
        var heading = new HBoxContainer(); top.AddChild(heading);
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; words.AddThemeConstantOverride("separation", Ui.Px(4)); heading.AddChild(words);
        words.AddChild(Ui.Heading("Performances", 32));
        words.AddChild(Ui.Text("Every set as it went · the crowd, the hiccups, the word in the field, and how the bands feel about you now", 13.5f, Ui.InkMuted));
        var up = records.Count(record => record.Delta > 0);
        var down = records.Count(record => record.Delta < 0);
        var badge = Ui.Caps($"{up} warmer · {down} cooler", up >= down ? Ui.TealDeep : new Color("7a3312"));
        badge.AddThemeStyleboxOverride("normal", Ui.Box(up >= down ? Ui.TealWash : Ui.AlertWash, 4, padX: 8, padY: 4));
        badge.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; heading.AddChild(badge);
        body.AddChild(Gap(12));
        if (records.Length == 0) body.AddChild(Paragraph("No sets were played.", 15, Ui.InkMuted));
        foreach (var record in records)
        {
            body.AddChild(PerformanceCard(record, names, opened));
            body.AddChild(Gap(10));
        }
        body.AddChild(Paragraph("A band's relationship runs from −100 to +100 and carries from festival to festival. A friendly band's fee falls by half " +
            "a percent a point, to half at +100; a sour one's rises a percent a point, to double at −100.", 11.5f, Ui.InkMuted));
        body.AddChild(Gap(70));
    }

    private static Control PerformanceCard(PerformanceRecord record, IReadOnlyDictionary<ulong, string> names, long opened)
    {
        var act = ActCatalogue.Find(record.ActId);
        var stage = FestivalStages.Find(record.StageId);
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", Ui.Box(Colors.White, 6, new Color("e2d8bf"), 1, 16, 12));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Ui.Px(6)); card.AddChild(box);

        // The act and its set on the left, the relationship on the right.
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", Ui.Px(12)); box.AddChild(head);
        var title = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; title.AddThemeConstantOverride("separation", Ui.Px(2)); head.AddChild(title);
        var due = $"{FestivalClockText(record.ScheduledStartTick - opened)}–{FestivalClockText(record.ScheduledEndTick - opened)}";
        title.AddChild(Ui.Caps($"{(stage is null ? record.StageId : Main.StageTitle(stage))} · Set {record.Slot + 1} · {due}" +
            (act is null ? "" : $" · {FestivalGenre.Name(act.Genre)}"), Ui.InkMuted, 9.5f));
        var name = Ui.Heading(act?.Name ?? record.ActId, 22); name.AutowrapMode = TextServer.AutowrapMode.WordSmart; title.AddChild(name);
        var (wash, ink) = RelationColours(record.Delta);
        var change = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        change.AddThemeStyleboxOverride("panel", Ui.Box(record.Delta == 0 ? new Color("efe8d6") : wash, 6, padX: 12, padY: 6)); head.AddChild(change);
        var changeWords = new VBoxContainer(); changeWords.AddThemeConstantOverride("separation", 0); change.AddChild(changeWords);
        var delta = Ui.Heading(record.Delta == 0 ? "No change" : Signed(record.Delta), 20, record.Delta == 0 ? Ui.Ink : ink);
        delta.HorizontalAlignment = HorizontalAlignment.Right; changeWords.AddChild(delta);
        var fee = Ui.Text($"Relationship {Signed(record.RelationshipBefore)} → {Signed(record.RelationshipAfter)} · fee next time {FeeChange(record.RelationshipAfter)}",
            11.5f, record.Delta == 0 ? Ui.InkMuted : ink);
        fee.HorizontalAlignment = HorizontalAlignment.Right; changeWords.AddChild(fee);

        // How it went: the reaction, then whatever went wrong.
        var went = new HFlowContainer(); went.AddThemeConstantOverride("h_separation", Ui.Px(6)); went.AddThemeConstantOverride("v_separation", Ui.Px(4)); box.AddChild(went);
        var good = record.Reaction is GigRules.Enthusiastic or GigRules.Warm;
        var bad = record.Reaction is GigRules.CutShort or GigRules.NoShow or GigRules.EmptyField;
        went.AddChild(Chip(GigRules.ReactionWord(record.Reaction), good ? Ui.TealWash : bad ? Ui.AlertWash : new Color("efe8d6"),
            good ? Ui.TealDeep : bad ? new Color("7a3312") : Ui.Ink));
        foreach (var hiccup in record.Hiccups) went.AddChild(Chip(GigRules.HiccupWord(hiccup), new Color("f3e2cf"), new Color("7a3312")));
        if (record.Hiccups.Length == 0 && record.Reaction != GigRules.NoShow) went.AddChild(Chip("No hiccups", new Color("efe8d6"), Ui.InkMuted));

        var crowd = record.StartedTick < 0 ? $"Never started · {record.PeakCrowd} waited for them" :
            $"Peak crowd {record.PeakCrowd} · {record.SetEndCrowd} still there at the end · about {record.ExpectedCrowd} expected";
        var played = $"{FestivalClockText(record.StartedTick - opened)}–{FestivalClockText(record.EndedTick - opened)}";
        if (record.StartedTick >= 0 && played != due) crowd += $" · played {played}";
        box.AddChild(Paragraph(crowd, 13.5f, Body));

        // A few words from people who were there.
        foreach (var quote in record.Quotes)
        {
            var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", Ui.Px(8)); box.AddChild(line);
            line.AddChild(new ColorRect { Color = Ui.Gold, CustomMinimumSize = new Vector2(Ui.Px(3), 0), MouseFilter = Control.MouseFilterEnum.Ignore });
            var said = Paragraph($"“{GigQuotes.Line(record.Reaction, quote)}” — {(names.TryGetValue(quote.GuestId, out var who) ? who : "a guest")}" +
                (quote.Fan ? ", a fan" : ""), 14.5f, Ui.Ink, Ui.Slab);
            line.AddChild(said);
        }
        if (record.Reasons.Length > 0)
            box.AddChild(Paragraph("Why it moved: " + string.Join(", ", record.Reasons) + ".", 12, Ui.InkMuted));
        return card;
    }

    private static Label Chip(string text, Color wash, Color ink)
    {
        var chip = Ui.Caps(text, ink, 9.5f);
        chip.AddThemeStyleboxOverride("normal", Ui.Box(wash, 4, padX: 7, padY: 3));
        return chip;
    }

    private static HBoxContainer MoneyRow(Container parent, string label, string quantity, string amount, bool total = false, bool showQuantity = true)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Ui.Px(8)); parent.AddChild(row);
        var name = Ui.Text(label, total ? 15 : 13.5f, Ui.Ink, total ? Ui.SlabBold : null); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart; row.AddChild(name);
        if (showQuantity)
        {
            var count = Ui.Text(quantity, 13.5f, Ui.Ink, total ? Ui.BodyBold : null); count.HorizontalAlignment = HorizontalAlignment.Right;
            count.CustomMinimumSize = new Vector2(Ui.S(36), 0); row.AddChild(count);
        }
        var money = Ui.Text(amount, 13.5f, Ui.Ink, Ui.BodyBold); money.HorizontalAlignment = HorizontalAlignment.Right;
        money.CustomMinimumSize = new Vector2(Ui.S(56), 0); row.AddChild(money);
        return row;
    }

    private static void RuledRows(VBoxContainer parent, Action<VBoxContainer> rows, bool lastHeavy = true)
    {
        var list = new VBoxContainer(); list.AddThemeConstantOverride("separation", 0); parent.AddChild(list);
        rows(list);
        var children = list.GetChildren().OfType<Control>().ToArray();
        list.GetChildren().ToList().ForEach(child => list.RemoveChild(child));
        for (var i = 0; i < children.Length; i++)
        {
            var wrap = new PanelContainer();
            var box = Ui.Box(new Color(0, 0, 0, 0), 0, padY: 4); box.BorderColor = lastHeavy && i == children.Length - 1 ? Ui.Ink : Rule; box.BorderWidthBottom = 1;
            wrap.AddThemeStyleboxOverride("panel", box); wrap.AddChild(children[i]); list.AddChild(wrap);
        }
    }

    private static Control ColumnHeading(string title, string caption)
    {
        var head = new PanelContainer();
        var box = Ui.Box(new Color(0, 0, 0, 0), 0); box.BorderColor = Ui.Ink; box.BorderWidthBottom = Ui.Px(1.5f); box.ContentMarginBottom = Ui.S(4);
        head.AddThemeStyleboxOverride("panel", box);
        var line = new HBoxContainer(); head.AddChild(line);
        var name = Ui.Heading(title, 19); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; line.AddChild(name);
        var note = Ui.Caps(caption, Ui.InkMuted, 9.5f); note.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; line.AddChild(note);
        return head;
    }

    private void RenderFestivalAccounts(VBoxContainer body)
    {
        var report = _hud.Session.CompletedFestivalAccounts!;
        var top = new PanelContainer();
        var underline = Ui.Box(new Color(0, 0, 0, 0), 0); underline.BorderColor = Ui.Ink; underline.BorderWidthBottom = Ui.Px(2); underline.ContentMarginBottom = Ui.S(8);
        top.AddThemeStyleboxOverride("panel", underline); body.AddChild(top);
        var heading = new HBoxContainer(); top.AddChild(heading);
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; words.AddThemeConstantOverride("separation", Ui.Px(4)); heading.AddChild(words);
        words.AddChild(Ui.Heading("Festival accounts", 32));
        words.AddChild(Ui.Text("Lower Wittering · actual recorded transactions for this attempt", 13.5f, Ui.InkMuted));
        var badge = Ui.Caps(report.Reconciles ? "✓ Cash reconciles" : "Partial record", report.Reconciles ? Ui.TealDeep : new Color("7a3312"));
        badge.AddThemeStyleboxOverride("normal", Ui.Box(report.Reconciles ? Ui.TealWash : Ui.AlertWash, 4, padX: 8, padY: 4));
        badge.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; heading.AddChild(badge);
        body.AddChild(Gap(14));

        var items = report.Sales.Sum(sale => sale.Quantity);
        var tiles = new HBoxContainer(); tiles.AddThemeConstantOverride("separation", Ui.Px(10)); body.AddChild(tiles);
        void Tile(string caption, string value, string note, Color ink, bool dark = false)
        {
            var tile = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            tile.AddThemeStyleboxOverride("panel", dark ? Ui.Box(Ui.Ink, 6, padX: 12, padY: 10) : Ui.Box(Colors.White, 6, new Color("e2d8bf"), 1, 12, 10));
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 0); tile.AddChild(box);
            box.AddChild(Ui.Caps(caption, dark ? Ui.Gold : Ui.InkMuted, 9.5f));
            box.AddChild(Ui.Heading(value, 26, dark ? Ui.BarText : ink));
            box.AddChild(Ui.Text(note, 12, dark ? new Color("c9d1c7") : Ui.InkMuted));
            tiles.AddChild(tile);
        }
        Tile("Income", FestivalCurrency.Format(report.IncomePennies), $"{report.TicketsSold} tickets · {items} item{(items == 1 ? "" : "s")} sold", Ui.TealDeep);
        Tile("Operating spend", FestivalCurrency.Format(report.OperatingExpensesPennies), "Staff, acts and services", Ui.Ink);
        Tile(report.OperatingResultPennies < 0 ? "Operating loss" : "Operating result", FestivalCurrency.Format(report.OperatingResultPennies), "After cost of items sold",
            report.OperatingResultPennies < 0 ? Ui.Link : Ui.TealDeep);
        Tile("Closing cash", FestivalCurrency.Format(report.ClosingCashPennies),
            $"{(report.NetCashChangePennies < 0 ? "−" : "+")}{FestivalCurrency.Format(Math.Abs(report.NetCashChangePennies))} on the {FestivalCurrency.Format(report.OpeningCashPennies)} " +
            (report.CarriedInPennies is null ? "loan" : "carried in"), Ui.Ink, dark: true);
        body.AddChild(Gap(8));
        // What came in from the last festival, and the loan still owed (settlement is parked, so nothing is repaid yet).
        body.AddChild(Paragraph((report.CarriedInPennies is { } carried
                ? $"Carried in from the last festival: {FestivalCurrency.Format(carried)}. "
                : $"Starter loan: {FestivalCurrency.Format(report.OpeningCashPennies)}. ") +
            $"Loan still owed: {FestivalCurrency.Format(report.DebtOwedPennies)}; no repayments are due yet.", 13, Ui.Ink, Ui.BodyBold));
        body.AddChild(Gap(14));

        var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", Ui.Px(26)); body.AddChild(columns);
        var income = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; income.AddThemeConstantOverride("separation", 0); columns.AddChild(income);
        income.AddChild(ColumnHeading("Income", "Qty · sales"));
        RuledRows(income, list =>
        {
            MoneyRow(list, $"Tickets · advance sales {FestivalCurrency.Format(report.TicketPricePennies)}", report.TicketsSold.ToString(), FestivalCurrency.Format(report.TicketSalesPennies));
            // A row for each van's trader.
            if (report.PitchFees.Length > 0 && report.PitchFees.Sum(pitch => pitch.FeePennies) == report.PitchFeePennies)
                foreach (var pitch in report.PitchFees) MoneyRow(list, $"Pitch fee · {pitch.Trader}", "1", FestivalCurrency.Format(pitch.FeePennies));
            else if (report.PitchFeePennies > 0)
                MoneyRow(list, $"Pitch fee · {report.PitchFeeTrader}", "1", FestivalCurrency.Format(report.PitchFeePennies));
            var bars = report.Sales.Select(sale => sale.Stall).Distinct().Count();
            foreach (var sale in report.Sales)
            {
                var item = (bars > 1 ? $"{Stalls.Label(sale.Stall)} · " : "") + GameSession.ProductName(sale.Product);
                var rate = sale.UnitPricePennies >= GameSession.ImmersionPrice(sale.Product) ? "full price" : "50% rate";
                MoneyRow(list, $"{item} · {rate} {FestivalCurrency.Format(sale.UnitPricePennies)}", sale.Quantity.ToString(), FestivalCurrency.Format(sale.AmountPennies));
            }
            if (report.Sales.Length == 0) list.AddChild(Ui.Text("Nothing sold at the bar", 13.5f, Ui.InkMuted));
        });
        MoneyRow(income, "Total income", (items + report.TicketsSold).ToString(), FestivalCurrency.Format(report.IncomePennies), true);
        income.AddChild(Paragraph("50% rate applies to staff and performers; discounted beer is performer-only.", 11.5f, Ui.InkMuted));

        var spending = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; spending.AddThemeConstantOverride("separation", 0); columns.AddChild(spending);
        spending.AddChild(ColumnHeading("Operating expenditure", "Paid"));
        foreach (var group in report.OperatingExpenses.GroupBy(line => line.Category)
            .OrderBy(group => group.Key == "Staff and rental" ? 0 : group.Key == "Act bookings" ? 1 : 2))
        {
            var caption = Ui.Caps(group.Key, Ui.InkMuted, 9.5f); spending.AddChild(Gap(6)); spending.AddChild(caption);
            RuledRows(spending, list => { foreach (var expense in group) MoneyRow(list, expense.Label, "", FestivalCurrency.Format(expense.AmountPennies), showQuantity: false); }, lastHeavy: false);
        }
        if (report.OperatingExpenses.Length == 0) spending.AddChild(Ui.Text("No operating payments recorded", 13.5f, Ui.InkMuted));
        spending.AddChild(Line(Ui.Ink, 1));
        MoneyRow(spending, "Total operating", "", FestivalCurrency.Format(report.OperatingExpensesPennies), true, showQuantity: false);
        if (!report.FacilityDetailRecorded)
            spending.AddChild(Paragraph("Facility item detail is not recorded for this save; the paid total is shown.", 11.5f, Ui.InkMuted));

        body.AddChild(Gap(12)); body.AddChild(Line(Ui.Ink, 1.5f)); body.AddChild(Gap(8));
        var chartHead = new HBoxContainer(); body.AddChild(chartHead);
        var chartTitle = Ui.Heading("Where the cash went", 19); chartTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; chartHead.AddChild(chartTitle);
        var chartNote = Ui.Text("Stock is a cash outflow; its cost counts as spend only when sold", 12, Ui.InkMuted); chartNote.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; chartHead.AddChild(chartNote);
        body.AddChild(Gap(6));
        var steps = new List<(string Label, long Change, bool Total)>
        {
            (report.CarriedInPennies is null ? "Loan" : "Carried in", report.OpeningCashPennies, true), ("Tickets", report.TicketSalesPennies, false), ("Sales & pitch", report.IncomePennies - report.TicketSalesPennies, false), ("Operating", -report.OperatingExpensesPennies, false),
        };
        if (report.StockPurchaseRecorded) steps.Add(("Stock", -report.StockPurchasesPennies, false));
        steps.Add(("Equipment", -report.CapitalPurchasesPennies, false));
        steps.Add(("Closing", report.ClosingCashPennies, true));
        body.AddChild(new CashWaterfall { Steps = steps, CustomMinimumSize = new Vector2(0, Ui.S(128)) });

        body.AddChild(Gap(14)); body.AddChild(ColumnHeading("Cost of items sold", "Qty · cost"));
        RuledRows(body, list =>
        {
            foreach (var cost in report.SoldItemCosts)
                MoneyRow(list, $"{cost.Label} · {FestivalCurrency.Format(cost.UnitCostPennies)} each", cost.Quantity.ToString(), FestivalCurrency.Format(cost.AmountPennies));
            if (report.SoldItemCosts.Length == 0) list.AddChild(Ui.Text("No consumed stock recorded", 13.5f, Ui.InkMuted));
        });
        MoneyRow(body, "Total cost of items sold", "", FestivalCurrency.Format(report.SoldItemCostPennies), true);
        body.AddChild(Gap(14)); body.AddChild(ColumnHeading("Stock purchased · cash only", "Qty · paid"));
        RuledRows(body, list =>
        {
            if (!report.StockPurchaseRecorded) list.AddChild(Ui.Text("Not recorded for this save", 13.5f, Ui.InkMuted));
            else if (report.StockDetailRecorded)
                foreach (var stock in report.PurchasedStock)
                    MoneyRow(list, $"{stock.Label} · {FestivalCurrency.Format(stock.UnitCostPennies)} each", stock.Quantity.ToString(), FestivalCurrency.Format(stock.AmountPennies));
            else MoneyRow(list, "Recorded stock purchase", "", FestivalCurrency.Format(report.StockPurchasesPennies));
        }, lastHeavy: false);
        body.AddChild(Gap(8));
        body.AddChild(Paragraph(report.Reconciles ? "Recorded income, expenditure and cash reconcile."
            : "Full cash reconciliation is unavailable for this save; no missing movement has been assumed.", 12.5f, Ui.InkMuted));
        body.AddChild(Gap(70));
    }
}
