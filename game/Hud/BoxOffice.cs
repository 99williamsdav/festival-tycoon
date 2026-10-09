using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The briefing between the perk draft and planning: advance ticket sales and the budget they make,
/// who bought tickets (by favourite genre), and what the ticket price has led them to expect, told as
/// a calibre scale and a few of the ticket holders' own words. Start planning dismisses it.
/// </summary>
internal sealed class BoxOffice(IHudHost _hud, Action _startPlanning)
{
    private static readonly string[] Calibres = ["Pub back room", "Village fete", "Regional favourites", "Festival circuit", "Headliners"];
    private static readonly string[] FanNames = ["Folk fans", "Indie kids", "Pop fans", "Ravers", "Punks", "Metalheads"];
    private static readonly string[] FanName = ["folk fan", "indie kid", "pop fan", "raver", "punk", "metalhead"];
    /// <summary>Lines per genre, one picked per speaker; "{price}" becomes "A tenner" or the ticket price.</summary>
    private static readonly string[][] Quotes =
    [
        [
            "As long as there's a band and a bar, I'm happy.",
            "Bring a fiddle and I'll bring my own tankard.",
            "I've ironed my good waistcoat for this.",
            "If there's no sea shanty, I'm starting one.",
            "I just want a hay bale, a cider and something with an accordion.",
            "{price} for a singalong? I'll be singing the whole thing, then.",
        ],
        [
            "{price}? I want a proper band. Not Gary from the pub again.",
            "If they've been on local radio, I'm in.",
            "I'll be at the front, arms folded, deciding if they're any good.",
            "I liked them before anyone else did. I'll be telling everyone.",
            "Bringing my tote bag. Not explaining why.",
            "If the guitarist hasn't got a fringe, I'm asking questions.",
        ],
        [
            "I'll know every word or I'm going home.",
            "Something I can dance to with my nan.",
            "Glitter is not optional. I've brought spares.",
            "If there's no key change, it doesn't count.",
            "I've been rehearsing the dance in the kitchen since March.",
            "I've made a sign. It has a pun on it. You're welcome.",
        ],
        [
            "One decent drop and I'll stay till they switch the lights off.",
            "As long as the speakers are louder than the cows.",
            "I'll be the one waving a glow stick at a sheep.",
            "I don't need a band. I need a bassline and somewhere to stand.",
            "If my fillings aren't rattling, they're not trying.",
            "{price}? I've paid more for a taxi home from a rave.",
        ],
        [
            "Three chords and an attitude. That's all I've paid for.",
            "If nobody gets told off by the vicar, what's the point?",
            "I'm here to be disappointed, loudly.",
            "I've safety-pinned my jacket specially. Don't touch it.",
            "If they sell out by the second song, I'm leaving. After the second song.",
            "I've already written the angry review. Just need the band.",
        ],
        [
            "If there's no mosh pit I'm asking for my money back.",
            "Heavier than the tractor or I'm not interested.",
            "I've got earplugs. I won't be using them.",
            "My mum knitted me a black jumper with a skull on. I'm wearing it.",
            "If the cows aren't scared, it wasn't loud enough.",
            "I'll headbang to anything. Even the safety announcements.",
        ],
    ];

    private PanelContainer? _root;
    private Label? _ticketPrice;
    private Label? _sold;
    private Label? _soldOf;
    private Label? _ticketLine;
    private Label? _loanLine;
    private Label? _budget;
    private VBoxContainer? _fans;
    private Label? _expectLead;
    private CalibreScale? _scale;
    private readonly Label[] _calibreNames = new Label[5];
    private Label? _expectSummary;
    private VBoxContainer? _quotes;
    private string _key = "";

    public bool Visible => _root?.Visible == true;

    public void Build(CanvasLayer layer, Vector2 size)
    {
        _root = new PanelContainer { Position = new Vector2(0, Ui.TopBar), Size = new Vector2(size.X, size.Y - Ui.TopBar), Visible = false, Theme = HudTheme(),
            MouseFilter = Control.MouseFilterEnum.Stop };
        _root.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Ui.BarDeep, 0.74f), 0));
        layer.AddChild(_root);
        var canvas = new Control { MouseFilter = Control.MouseFilterEnum.Pass }; _root.AddChild(canvas);
        Control Place(Control control, float x, float y, float width)
        {
            control.Position = Ui.S(x, y - 60); control.CustomMinimumSize = new Vector2(Ui.S(width), 0); control.Size = control.CustomMinimumSize;
            canvas.AddChild(control); return control;
        }

        var heading = new VBoxContainer(); heading.AddThemeConstantOverride("separation", Ui.Px(4));
        heading.AddChild(Ui.Caps("Before you plan", Ui.Gold));
        heading.AddChild(Ui.Heading("The box office", 36, Ui.BarText));
        heading.AddChild(Ui.Text("Tickets went on sale in the spring. Here's who's coming, and what they think they've paid for.", 14.5f, Ui.BarMuted));
        Place(heading, 72, 82, 1136);

        Place(TicketColumn(), 72, 182, 340);
        Place(FansCard(), 436, 182, 320);
        Place(ExpectationCard(), 780, 182, 428);

        // Under the budget, so the expectations card can grow with longer quotes.
        var footer = new VBoxContainer(); footer.AddThemeConstantOverride("separation", Ui.Px(8));
        var start = Ui.Style(new Button { Text = "Start planning", Icon = Ui.Icon("chevron-right"), IconAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(0, Ui.S(48)), MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Primary, 16, 8);
        start.Pressed += _startPlanning; footer.AddChild(start);
        var next = Ui.Text("Next: build your site, book three acts and hire a sound engineer.", 13, Ui.BarMuted);
        next.AutowrapMode = TextServer.AutowrapMode.WordSmart; footer.AddChild(next);
        Place(footer, 72, 594, 340);
    }

    private static PanelContainer Card(Color? colour = null, float padX = 18, float padY = 18)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Ui.Box(colour ?? Ui.Paper, 10, padX: padX, padY: padY, shadow: 16, shadowAlpha: 0.45f));
        return card;
    }

    private VBoxContainer TicketColumn()
    {
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", Ui.Px(14));
        var stub = Card(padX: 0, padY: 0); stub.ClipContents = true; column.AddChild(stub);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 0); stub.AddChild(row);
        var face = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var (side, value) in new[] { ("margin_left", 18f), ("margin_right", 18f), ("margin_top", 18f), ("margin_bottom", 16f) })
            face.AddThemeConstantOverride(side, Ui.Px(value));
        row.AddChild(face);
        var words = new VBoxContainer(); words.AddThemeConstantOverride("separation", Ui.Px(4)); face.AddChild(words);
        words.AddChild(Ui.Caps("Admit one · Tier 1", Ui.Link));
        var name = Ui.Heading("Lower Wittering Festival", 25); name.AutowrapMode = TextServer.AutowrapMode.WordSmart; words.AddChild(name);
        words.AddChild(Ui.Text("One day on the farm · three sets", 13, Ui.InkMuted));
        words.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(8)) });
        var price = new HBoxContainer(); price.AddThemeConstantOverride("separation", Ui.Px(8)); words.AddChild(price);
        _ticketPrice = Ui.Heading("", 44); price.AddChild(_ticketPrice);
        var each = Ui.Text("a ticket", 13.5f, Ui.InkMuted); each.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; price.AddChild(each);

        var tear = new PanelContainer { CustomMinimumSize = new Vector2(Ui.S(92), 0) };
        tear.AddThemeStyleboxOverride("panel", Ui.Box(new Color("efe3ca"), 0));
        tear.Draw += () => tear.DrawDashedLine(new Vector2(1, 0), new Vector2(1, tear.Size.Y), new Color("c9b994"), Ui.S(2), Ui.S(6));
        row.AddChild(tear);
        var counts = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; counts.AddThemeConstantOverride("separation", Ui.Px(2)); tear.AddChild(counts);
        _sold = Ui.Heading("", 34); _sold.HorizontalAlignment = HorizontalAlignment.Center; counts.AddChild(_sold);
        _soldOf = Ui.Text("", 12, Ui.InkMuted); _soldOf.HorizontalAlignment = HorizontalAlignment.Center; counts.AddChild(_soldOf);

        // The SOLD OUT stamp sits across the stub face.
        var stampHolder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        stub.AddChild(stampHolder);
        var stamp = Ui.Heading("SOLD OUT", 20, Ui.Alert);
        stamp.AddThemeStyleboxOverride("normal", Ui.Box(new Color(0, 0, 0, 0), 6, Ui.Alert, 3, 10, 2));
        stamp.Modulate = new Color(1, 1, 1, 0.85f); stamp.Rotation = Mathf.DegToRad(-12);
        stamp.Position = Ui.S(150, 132);
        stampHolder.AddChild(stamp);

        var budget = Card(new Color(Ui.Paper, 0.96f), 18, 14); column.AddChild(budget);
        var lines = new VBoxContainer(); lines.AddThemeConstantOverride("separation", Ui.Px(6)); budget.AddChild(lines);
        lines.AddChild(Ui.Caps("Your budget", Ui.InkMuted));
        HBoxContainer Line(string caption, out Label value, Color ink)
        {
            var line = new HBoxContainer();
            var text = Ui.Text(caption, 14.5f, Ui.Ink); text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; line.AddChild(text);
            value = Ui.Text("", 14.5f, ink, Ui.BodyBold); line.AddChild(value);
            return line;
        }
        var tickets = Line("", out _ticketLine, Ui.TealDeep); lines.AddChild(tickets);
        tickets.GetChild<Label>(0).Name = "Caption";
        lines.AddChild(Line("Starter loan", out _loanLine, Ui.Ink));
        lines.AddChild(new ColorRect { Color = Ui.Ink, CustomMinimumSize = new Vector2(0, Math.Max(1, Ui.S(1.5f))) });
        var total = new HBoxContainer(); lines.AddChild(total);
        var spend = Ui.Heading("To spend", 18); spend.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; spend.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; total.AddChild(spend);
        _budget = Ui.Heading("", 24); total.AddChild(_budget);
        lines.AddChild(Ui.Text("Nothing is spent until you open the gates.", 12.5f, Ui.InkMuted));
        return column;
    }

    private PanelContainer FansCard()
    {
        var card = Card();
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Ui.Px(4)); card.AddChild(box);
        var title = new HBoxContainer(); title.AddThemeConstantOverride("separation", Ui.Px(8)); box.AddChild(title);
        var icon = Ui.IconRect("users", 18, Ui.Teal); icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; title.AddChild(icon);
        title.AddChild(Ui.Heading("Who's coming", 22));
        box.AddChild(Ui.Text("Ticket holders, by the music they love most", 13, Ui.InkMuted));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        _fans = new VBoxContainer(); _fans.AddThemeConstantOverride("separation", Ui.Px(9)); box.AddChild(_fans);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(12)) });
        var tip = Ui.Text("Fans enjoy their own genre most. Book for the crowd you've got.", 13, Ui.TealDeep);
        tip.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        tip.AddThemeStyleboxOverride("normal", Ui.Box(Ui.TealWash, 8, padX: 12, padY: 10)); box.AddChild(tip);
        return card;
    }

    private PanelContainer ExpectationCard()
    {
        var card = Card();
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Ui.Px(4)); card.AddChild(box);
        var title = new HBoxContainer(); title.AddThemeConstantOverride("separation", Ui.Px(8)); box.AddChild(title);
        var icon = Ui.IconRect("message-square", 18, Ui.Link); icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; title.AddChild(icon);
        title.AddChild(Ui.Heading("What they're expecting", 22));
        _expectLead = Ui.Text("", 13, Ui.InkMuted); box.AddChild(_expectLead);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(28)) });
        _scale = new CalibreScale { CustomMinimumSize = new Vector2(0, Ui.S(10)) }; box.AddChild(_scale);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(4)) });
        var names = new HBoxContainer(); names.AddThemeConstantOverride("separation", Ui.Px(3)); box.AddChild(names);
        for (var i = 0; i < Calibres.Length; i++)
        {
            _calibreNames[i] = Ui.Text(Calibres[i], 11.5f, Ui.InkMuted);
            _calibreNames[i].HorizontalAlignment = HorizontalAlignment.Center; _calibreNames[i].AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _calibreNames[i].SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; _calibreNames[i].SizeFlagsStretchRatio = 1; _calibreNames[i].CustomMinimumSize = new Vector2(1, 0);
            names.AddChild(_calibreNames[i]);
        }
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(8)) });
        _expectSummary = Ui.Text("", 13.5f, Ui.Ink); _expectSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_expectSummary);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        _quotes = new VBoxContainer(); _quotes.AddThemeConstantOverride("separation", Ui.Px(9)); box.AddChild(_quotes);
        return card;
    }

    private static int CalibreStep(int popularity) => Math.Clamp(popularity / 20, 0, Calibres.Length - 1);

    public void Refresh(bool show)
    {
        if (_root is null) return;
        _root.Visible = show;
        if (!show) return;
        var session = _hud.Session;
        var p = session.CapturePreparation()!;
        var price = FestivalTickets.PricePennies(p.Tier);
        var sold = FestivalTickets.Sold(p.Tier);
        var key = $"{session.CampaignSeed}:{p.Attempt}:{price}:{sold}";
        if (key == _key) return;
        _key = key;
        var tickets = FestivalTickets.RevenuePennies(p.Tier);
        _ticketPrice!.Text = FestivalCurrency.Format(price);
        _sold!.Text = sold.ToString(); _soldOf!.Text = $"of {sold} sold";
        _ticketLine!.GetParent().GetNode<Label>("Caption").Text = $"Ticket sales · {sold} × {FestivalCurrency.Format(price)}";
        _ticketLine.Text = FestivalCurrency.Format(tickets);
        _loanLine!.Text = FestivalCurrency.Format(p.OpeningCashPennies - tickets);
        // From Tier 2 there's no new loan: the rest is what the last festival closed on.
        _loanLine.GetParent().GetNode<Label>("Caption").Text = p.CarriedIn is { } carry ? $"Carried from Tier {carry.FromTier}" : "Starter loan";
        _budget!.Text = FestivalCurrency.Format(p.OpeningCashPennies);

        var guests = p.People.Where(person => person.Role == ProtectedPersonRole.Guest).ToArray();
        foreach (var child in _fans!.GetChildren()) { _fans.RemoveChild(child); child.QueueFree(); }
        foreach (var group in guests.GroupBy(guest => guest.ExpectedGenre).OrderByDescending(group => group.Count()).ThenBy(group => group.Key))
            _fans.AddChild(FanRow(group.Key, group.Count()));

        var expected = session.ExpectedPopularity;
        var step = CalibreStep(expected);
        var priceWord = price == 1_000 ? "a tenner" : FestivalCurrency.Format(price);
        _expectLead!.Text = $"For {priceWord}, they're expecting…";
        _scale!.Step = step; _scale.Marker = expected / 100f;
        for (var i = 0; i < Calibres.Length; i++)
        {
            _calibreNames[i].AddThemeColorOverride("font_color", i == step ? Ui.GoldInk : Ui.InkMuted);
            _calibreNames[i].AddThemeFontOverride("font", i == step ? Ui.BodyBold : Ui.Body);
        }
        var bigger = step < Calibres.Length - 1 ? "Bigger names will thrill them" : "Anything less will feel like a let-down";
        var smaller = step > 0 ? $"{Calibres[step - 1].ToLowerInvariant()} acts will leave a few muttering" : "no act is too small for them";
        _expectSummary!.Text = $"{Calibres[step]} acts will keep them happy. {bigger}; {smaller}.";
        _expectSummary.TooltipText = $"Guests expect act popularity around {expected}. Acts above that lift their enjoyment; acts below lower it.";
        _expectSummary.MouseFilter = Control.MouseFilterEnum.Pass;

        foreach (var child in _quotes!.GetChildren()) { _quotes.RemoveChild(child); child.QueueFree(); }
        foreach (var guest in Speakers(guests, session.CampaignSeed))
        {
            var lines = Quotes[guest.ExpectedGenre];
            var line = lines[(int)((guest.AgentId + session.CampaignSeed) % (ulong)lines.Length)]
                .Replace("{price}", price == 1_000 ? "A tenner" : FestivalCurrency.Format(price));
            _quotes.AddChild(QuoteCard(line, $"{guest.Name} · {FanName[guest.ExpectedGenre]}"));
        }
    }

    /// <summary>Three ticket holders with different favourite genres, chosen by the campaign seed.</summary>
    private static IEnumerable<EditionPerson> Speakers(EditionPerson[] guests, ulong seed)
    {
        var picked = new List<EditionPerson>();
        foreach (var guest in guests.OrderBy(guest => (guest.AgentId * 0x9E3779B97F4A7C15UL) ^ seed))
        {
            if (picked.Any(item => item.ExpectedGenre == guest.ExpectedGenre)) continue;
            picked.Add(guest);
            if (picked.Count == 3) break;
        }
        return picked;
    }

    private static readonly Color[] FanColours = [new("6f9a5b"), new("c0643a"), new("c99a1e"), new("4a7fa6"), new("c94a7a"), new("4b4a52")];

    private static HBoxContainer FanRow(int genre, int count)
    {
        var colour = genre is >= 0 and < 6 ? FanColours[genre] : Ui.InkMuted;
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Ui.Px(8));
        var label = new HBoxContainer { CustomMinimumSize = new Vector2(Ui.S(96), 0) }; label.AddThemeConstantOverride("separation", Ui.Px(6)); row.AddChild(label);
        var dot = new Panel { CustomMinimumSize = Ui.S(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        dot.AddThemeStyleboxOverride("panel", Ui.Box(colour, 5)); label.AddChild(dot);
        label.AddChild(Ui.Text(genre is >= 0 and < 6 ? FanNames[genre] : "Others", 14, Ui.Ink));
        var blocks = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; blocks.AddThemeConstantOverride("separation", Ui.Px(3)); row.AddChild(blocks);
        for (var i = 0; i < count; i++)
        {
            var block = new Panel { CustomMinimumSize = Ui.S(14, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            block.AddThemeStyleboxOverride("panel", Ui.Box(colour, 3)); blocks.AddChild(block);
        }
        var number = Ui.Text(count.ToString(), 14, Ui.Ink, Ui.BodyBold); number.HorizontalAlignment = HorizontalAlignment.Right;
        number.CustomMinimumSize = new Vector2(Ui.S(22), 0); row.AddChild(number);
        return row;
    }

    private static PanelContainer QuoteCard(string quote, string who)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Ui.Box(Ui.PaperBright, 8, Ui.PaperRule, 1, 12, 9));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Ui.Px(3)); card.AddChild(box);
        var text = Ui.Text($"“{quote}”", 15, Ui.Ink, Ui.Slab); text.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(text);
        box.AddChild(Ui.Text(who, 12, Ui.InkMuted));
        return card;
    }
}
