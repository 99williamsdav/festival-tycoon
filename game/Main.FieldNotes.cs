using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

/// <summary>
/// Field notes: the first time something becomes relevant, the game pauses once and a page torn from a field notebook
/// says what's going on, with a pin on whatever it's about. Never instructions, just a nudge. Seen notes are remembered
/// for the player, not the save, so a second campaign doesn't repeat them, and they're kept in the field guide.
/// Allergic guests near a wasp-ridden bin wear a small striped badge once the wasp note has been seen, so a hidden trait
/// can be planned around. Presentation only. Art: assets/source/ui/field-notes-v1 (sprites are @2x).
/// </summary>
public partial class Main
{
    /// <param name="Art">The doodle and pin artwork's name for this note.</param>
    private sealed record FieldNoteText(string Id, string Art, string Title, string Line);
    private sealed record FieldNote(FieldNoteText Text, Func<Vector3[]?> Find);

    private const string FieldNotesFile = "user://field_notes.json";
    private const string FieldArt = "res://assets/ui/field-notes/";
    // Screen pixels per texel for the @2x world sprites, as a share of the viewport's height (1280×720 reference).
    private const float FieldSpritePixel = .0021f;

    private static readonly FieldNoteText[] FieldNoteTexts =
    [
        new("wasps", "wasps", "Wasps", "A full bin is a wasp magnet, which can be more than just a nuisance for some..."),
        new("heat", "heat", "Heat", "It's a scorcher, and not everyone remembers to drink their water..."),
        new("drunk", "drink", "Drink", "Beer makes everyone friendlier, up to a point..."),
        new("stuck", "stuck", "Stuck", "Portaloo doors have a habit of sticking, and whoever's inside isn't getting out on their own..."),
        new("toxic", "toxic", "Fumes", "A nearly full portaloo is grim at the best of times, and worse if you're stuck in it..."),
        new("generator", "power", "Power", "Everything plugged in leans on the generator, and it has its limits..."),
        new("argument", "tempers", "Tempers", "Long waits and short tempers don't mix, and words can turn into something worse..."),
        new("band-late", "late", "Late", "The crowd came for the music, and their patience won't last forever..."),
        new("litter", "litter", "Litter", "Not everyone makes it to a bin, and nobody likes standing in rubbish..."),
        new("dusk", "dusk", "Lights", "The lights are coming on, and they want their share of the power too..."),
    ];

    private HashSet<string>? _fieldNotesSeen;
    private Control? _fieldNoteCard;
    private Label? _fieldNoteTitle, _fieldNoteCount, _fieldNoteLine;
    private TextureRect? _fieldNoteDoodle;
    private FieldNote? _fieldNoteShown;
    private bool _fieldNotePaused;
    private double _fieldNotePoll;
    private readonly List<(Sprite3D Pin, Sprite3D Glow)> _fieldNotePins = [];
    private readonly Dictionary<ulong, Sprite3D> _allergyMarkers = [];
    private readonly List<Sprite3D> _allergyMarkersLeaving = [];

    private FieldNote[] FieldNotes()
    {
        Func<Vector3[]?> Find(string id) => id switch
        {
            // The bin only: allergic guests wear their badge as soon as the note shows.
            "wasps" => () => Places(_session.CaptureBins().Where(b => b.Wasps).Select(b => (GridCell?)b.Cell), 1.4f),
            "heat" => () => Person(_session.CaptureMedical()?.Needs.Where(n => n.Profile == MedicalNeedProfile.Guest && n.Stage == MedicalStage.Distress).Select(n => n.AgentId)),
            "drunk" => () => Person(_session.CaptureImmersion()?.People.Where(p => p.Intoxication >= 5_000).Select(p => p.AgentId)),
            "stuck" => () => Places(_session.CaptureFaults()?.Faults.Where(f => f.Kind == FacilityFaultKind.StuckInToilet && f.Stage == FacilityFaultStage.Active)
                .Select(f => _session.CaptureToilets().FirstOrDefault(t => t.Id == f.FacilityId)?.Cell), 3.4f),
            "toxic" => () => Places(_session.CaptureToilets().Where(t => _session.ToiletToxic(t.Id)).Select(t => (GridCell?)t.Cell), 3.4f),
            "generator" => () => _session.PowerBudgetActive && _session.CapturePower().Over ? Generator() : null,
            "argument" => () => Person(_session.CaptureDisorder()?.People.Where(p => p.Stage == DisorderStage.Argument).Select(p => p.AgentId)),
            "band-late" => () => !_session.FestivalBandLate || _session.CaptureProgramme() is not { } q ? null :
                Person(q.Performers.Where(r => r.SlotIndex == q.CurrentSlot).Select(r => r.AgentId)),
            "litter" => () => _session.CaptureLitter()?.Pieces.FirstOrDefault(w => w.Location == WasteLocation.Ground) is { } piece
                ? [new Vector3(piece.XMillimetres / 1000f, 1.1f, piece.ZMillimetres / 1000f)] : null,
            _ => () => _session.PowerBudgetActive && _session.CapturePower().Lights > 0 ? Generator() : null,
        };
        return FieldNoteTexts.Select(text => new FieldNote(text, Find(text.Id))).ToArray();
    }

    /// <summary>One pin for a person or a group: over the highest head among them.</summary>
    private Vector3[]? Person(IEnumerable<ulong>? ids)
    {
        var heads = ids?.Where(id => _attendeeVisuals.ContainsKey(new EntityId(id)))
            .Select(id => _attendeeVisuals[new EntityId(id)].Position + Vector3.Up * 3.4f).ToArray();
        return heads is { Length: > 0 } ? [heads.MaxBy(head => head.Y)] : null;
    }

    private static Vector3[]? Places(IEnumerable<GridCell?>? cells, float height)
    {
        var at = cells?.Where(c => c is not null).Select(c => ImmersionPosition(c!.Value) + Vector3.Up * height).ToArray();
        return at is { Length: > 0 } ? at : null;
    }

    private Vector3[]? Generator() => _session.CaptureEquipment() is { } e ? [new Vector3(e.XMillimetres / 1000f, 2.6f, e.ZMillimetres / 1000f)] : null;

    private IEnumerable<ulong> AllergicGuestsNear(BinReadModel[] bins)
    {
        if (bins.Length == 0) yield break;
        var agents = _session.CaptureObservation().NavigationAgents.ToDictionary(a => a.Id.Value);
        foreach (var person in _session.CapturePreparation()?.People ?? [])
        {
            if (person.Role != ProtectedPersonRole.Guest || !person.Admitted || person.Departed || !_session.GuestCharacterOf(person.AgentId).WaspAllergy ||
                !agents.TryGetValue(person.AgentId, out var nav)) continue;
            var cell = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
            // The same round reach the wasps sting within.
            if (bins.Any(b => (b.Cell.X - cell.X) * (b.Cell.X - cell.X) + (b.Cell.Z - cell.Z) * (b.Cell.Z - cell.Z) <= LitterRules.WaspRadiusCells * LitterRules.WaspRadiusCells))
                yield return person.AgentId;
        }
    }

    private static Texture2D FieldTexture(string name) => GD.Load<Texture2D>(FieldArt + name + ".png");

    /// <summary>A ring with a doodle in it, at @2x art drawn half size.</summary>
    private static Control DoodleRing(string ring, string doodle, float ringSize, float doodleSize, out TextureRect inner)
    {
        var holder = new Control { CustomMinimumSize = Ui.S(ringSize, ringSize), MouseFilter = Control.MouseFilterEnum.Ignore };
        holder.AddChild(new TextureRect { Texture = FieldTexture(ring), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, Size = Ui.S(ringSize, ringSize), MouseFilter = Control.MouseFilterEnum.Ignore });
        inner = new TextureRect { Texture = FieldTexture(doodle), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Size = Ui.S(doodleSize, doodleSize),
            Position = Ui.S((ringSize - doodleSize) / 2, (ringSize - doodleSize) / 2), MouseFilter = Control.MouseFilterEnum.Ignore };
        holder.AddChild(inner);
        return holder;
    }

    private void BuildFieldNotes(CanvasLayer layer, Vector2 size)
    {
        _fieldNotesSeen = LoadFieldNotesSeen();
        // A torn notebook page, top centre, clear of the incident cards on the left and the stage card on the right.
        var card = new VBoxContainer { Visible = false, AnchorLeft = .5f, AnchorRight = .5f, OffsetLeft = -Ui.S(230), OffsetRight = Ui.S(230),
            OffsetTop = Ui.ContentTop + Ui.S(8), MouseFilter = Control.MouseFilterEnum.Stop };
        card.AddThemeConstantOverride("separation", 0);
        _fieldNoteCard = card;
        var page = new PanelContainer();
        var paper = Ui.Box(Ui.Paper, 0, Ui.Gold, 0, 22, 20, shadow: 9, shadowAlpha: .4f);
        paper.BorderWidthTop = Ui.Px(3); paper.ContentMarginBottom = Ui.S(14); paper.ShadowOffset = new Vector2(0, Ui.S(5));
        paper.ShadowColor = new Color("0a1410", .4f);
        page.AddThemeStyleboxOverride("panel", paper); card.AddChild(page);
        page.AddChild(new RuledPaper { MarginX = 52, FirstRule = 41 });
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Ui.Px(16)); page.AddChild(row);
        var ringColumn = new VBoxContainer(); ringColumn.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(6)) });
        ringColumn.AddChild(DoodleRing("ring", "doodle_wasps", 46, 37, out var doodle)); _fieldNoteDoodle = doodle;
        row.AddChild(ringColumn);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", Ui.Px(6));
        row.AddChild(text);
        var header = new HBoxContainer(); text.AddChild(header);
        header.AddChild(Ui.Caps("Field note ·", Ui.GoldInk, 10.5f));
        _fieldNoteTitle = Ui.Caps("", Ui.Ink, 10.5f); _fieldNoteTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; header.AddChild(_fieldNoteTitle);
        _fieldNoteCount = Ui.Text("", 11, Ui.InkMuted, Ui.BodySemi); header.AddChild(_fieldNoteCount);
        _fieldNoteLine = Ui.Text("", 18, Ui.Ink, Ui.Slab); _fieldNoteLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _fieldNoteLine.CustomMinimumSize = new Vector2(Ui.S(354), 0); text.AddChild(_fieldNoteLine);
        var footer = new HBoxContainer(); text.AddChild(footer);
        var kept = Ui.Text("Kept in your field guide", 12, Ui.InkMuted); kept.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        kept.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; footer.AddChild(kept);
        var ok = Ui.Style(new Button { Text = "Got it", CustomMinimumSize = Ui.S(84, 30), MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Primary, 13);
        ok.Pressed += DismissFieldNote; footer.AddChild(ok);
        card.AddChild(new TextureRect { Texture = FieldTexture("torn_edge"), StretchMode = TextureRect.StretchModeEnum.Tile,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, CustomMinimumSize = new Vector2(0, Ui.S(7)), MouseFilter = Control.MouseFilterEnum.Ignore });
        // A strip of washi tape holding it to the screen, overhanging the top edge (placed over the page's content area).
        var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; page.AddChild(overlay);
        overlay.AddChild(new TextureRect { Texture = FieldTexture("tape"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, Size = Ui.S(78, 22), Position = Ui.S(320, -32), RotationDegrees = -7,
            MouseFilter = Control.MouseFilterEnum.Ignore });
        layer.AddChild(card);
    }

    private void ProcessFieldNotes(double delta)
    {
        if (_fieldNoteCard is null || _fieldNotesSeen is null) return;
        var running = _session.PreparedStatus == PreparationStatus.Running && !EyeViewActive;
        SyncAllergyMarkers(running, delta);
        if (_fieldNoteShown is { } shown)
        {
            // Pins follow whoever they're on; a note left open when the day ends just closes.
            if (_session.PreparedStatus != PreparationStatus.Running) { DismissFieldNote(); return; }
            var at = shown.Find();
            if (at is not null) PlacePins(shown.Text.Art, at);
            BobPins();
            return;
        }
        if (!running) return;
        _fieldNotePoll -= delta;
        if (_fieldNotePoll > 0) return;
        _fieldNotePoll = .5;
        foreach (var note in FieldNotes())
        {
            if (_fieldNotesSeen.Contains(note.Text.Id) || note.Find() is not { } at) continue;
            ShowFieldNote(note, at);
            return;
        }
    }

    private void ShowFieldNote(FieldNote note, Vector3[] at)
    {
        _fieldNoteShown = note;
        _fieldNoteTitle!.Text = " " + note.Text.Title.ToUpperInvariant();
        _fieldNoteCount!.Text = $"{_fieldNotesSeen!.Count + 1} of {FieldNoteTexts.Length}";
        _fieldNoteLine!.Text = note.Text.Line;
        _fieldNoteDoodle!.Texture = FieldTexture("doodle_" + note.Text.Art);
        _fieldNoteCard!.Visible = true;
        // The first time only, the game waits while it's read.
        _fieldNotePaused = !_session.IsPaused;
        if (_fieldNotePaused) { _host.Submit(new SetPausedCommand(true)); RefreshPreparationHud(); }
        PlacePins(note.Text.Art, at);
        // Framed a little below centre, so the pin sits clear of the card at the top of the screen.
        var size = Math.Min(_rig.Camera.Size, 30);
        var up = _rig.Camera.GlobalBasis.Y with { Y = 0 };
        _rig.Frame(new Vector3(at[0].X, 0, at[0].Z) + (up.LengthSquared() > 0 ? up.Normalized() * size * .18f : Vector3.Zero), size);
    }

    private void DismissFieldNote()
    {
        if (_fieldNoteShown is { } note && _fieldNotesSeen!.Add(note.Text.Id)) SaveFieldNotesSeen();
        _fieldNoteShown = null;
        _fieldNoteCard!.Visible = false;
        foreach (var (pin, glow) in _fieldNotePins) { pin.Visible = false; glow.Visible = false; }
        if (_fieldNotePaused && _session.IsPaused) { _host.Submit(new SetPausedCommand(false)); RefreshPreparationHud(); }
        _fieldNotePaused = false;
    }

    private static Sprite3D WorldSprite(Texture2D texture, int priority, Vector2 offset) => new()
    {
        Texture = texture, PixelSize = FieldSpritePixel, FixedSize = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true, RenderPriority = priority, Offset = offset, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        AlphaCut = SpriteBase3D.AlphaCutMode.Disabled, Shaded = false,
    };

    private void PlacePins(string art, Vector3[] at)
    {
        while (_fieldNotePins.Count < at.Length)
        {
            // The pin's tip sits on what it's about; at dusk a warm glow sits behind its disc.
            var glow = WorldSprite(FieldTexture("pin_glow"), 3, new Vector2(0, 40)); glow.Modulate = new Color(1, 1, 1, .45f);
            var pin = WorldSprite(FieldTexture("pin_" + art), 4, new Vector2(0, 28));
            AddChild(glow); AddChild(pin); _fieldNotePins.Add((pin, glow));
        }
        var dusk = _session.PowerBudgetActive && _session.CapturePower().Lights > 0;
        for (var i = 0; i < _fieldNotePins.Count; i++)
        {
            var (pin, glow) = _fieldNotePins[i];
            pin.Visible = i < at.Length; glow.Visible = pin.Visible && dusk;
            if (!pin.Visible) continue;
            pin.Texture = FieldTexture("pin_" + art);
            pin.SetMeta("Anchor", at[i]);
        }
    }

    private void BobPins()
    {
        var lift = .15f * MathF.Sin((float)(Time.GetTicksMsec() / 1000.0 * 4));
        foreach (var (pin, glow) in _fieldNotePins.Where(p => p.Pin.Visible && p.Pin.HasMeta("Anchor")))
            pin.Position = glow.Position = pin.GetMeta("Anchor").AsVector3() + Vector3.Up * lift;
    }

    /// <summary>Allergic guests near wasps wear a small striped badge: from the wasp note on, fading in and out with the wasps.</summary>
    private void SyncAllergyMarkers(bool running, double delta)
    {
        const float fade = .3f;
        var step = (float)delta / fade;
        var near = running && _fieldNotesSeen!.Contains("wasps") || _fieldNoteShown?.Text.Id == "wasps"
            ? AllergicGuestsNear(_session.CaptureBins().Where(b => b.Wasps).ToArray()).ToHashSet() : [];
        foreach (var id in _allergyMarkers.Keys.Where(id => !near.Contains(id)).ToArray())
        { _allergyMarkersLeaving.Add(_allergyMarkers[id]); _allergyMarkers.Remove(id); }
        for (var i = _allergyMarkersLeaving.Count - 1; i >= 0; i--)
        {
            var leaving = _allergyMarkersLeaving[i];
            leaving.Modulate = new Color(1, 1, 1, Mathf.Max(0, leaving.Modulate.A - step));
            if (leaving.Modulate.A <= 0) { leaving.QueueFree(); _allergyMarkersLeaving.RemoveAt(i); }
        }
        foreach (var id in near)
        {
            if (!_attendeeVisuals.TryGetValue(new EntityId(id), out var body)) continue;
            if (!_allergyMarkers.TryGetValue(id, out var marker))
            {
                marker = WorldSprite(FieldTexture("wasp_marker"), 2, Vector2.Zero); marker.Modulate = new Color(1, 1, 1, 0);
                AddChild(marker); _allergyMarkers.Add(id, marker);
            }
            marker.Modulate = new Color(1, 1, 1, Mathf.Min(1, marker.Modulate.A + step));
            marker.Position = body.Position + Vector3.Up * 2.3f;
        }
    }

    private static HashSet<string> LoadFieldNotesSeen()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(FieldNotesFile)) return [];
            using var file = Godot.FileAccess.Open(FieldNotesFile, Godot.FileAccess.ModeFlags.Read);
            return JsonSerializer.Deserialize<string[]>(file.GetAsText())?.ToHashSet() ?? [];
        }
        catch (Exception) { return []; }
    }

    private void SaveFieldNotesSeen()
    {
        try
        {
            using var file = Godot.FileAccess.Open(FieldNotesFile, Godot.FileAccess.ModeFlags.Write);
            file?.StoreString(JsonSerializer.Serialize(_fieldNotesSeen!.Order().ToArray()));
        }
        catch (Exception e) { GD.PushWarning($"Field notes not saved: {e.Message}"); }
    }

    // ---- The field guide: every note seen so far, in a cloth-bound notebook. ----

    private CanvasLayer? _fieldGuide;

    /// <summary>Opens the field guide over whatever's on screen: from the festival menu or the title screen.</summary>
    private void OpenFieldGuide()
    {
        if (_fieldGuide is not null) return;
        var seen = _fieldNotesSeen ?? LoadFieldNotesSeen();
        _fieldGuide = new CanvasLayer { Layer = 30 }; AddChild(_fieldGuide);
        var dim = new ColorRect { Color = new Color("0e1f1a", .59f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect); _fieldGuide.AddChild(dim);
        var centre = new CenterContainer(); centre.SetAnchorsPreset(Control.LayoutPreset.FullRect); dim.AddChild(centre);
        var cover = new PanelContainer { Theme = HudTheme() };
        var cloth = Ui.Box(new Color("2a4a3e"), 12, padX: 10, padY: 10); cloth.ContentMarginTop = Ui.S(6);
        cover.AddThemeStyleboxOverride("panel", cloth); centre.AddChild(cover);
        var spread = new HBoxContainer(); spread.AddThemeConstantOverride("separation", 0); cover.AddChild(spread);
        var noted = FieldNoteTexts.Count(n => seen.Contains(n.Id));

        VBoxContainer Page(bool left)
        {
            var panel = new PanelContainer { CustomMinimumSize = Ui.S(486, 610) };
            var paper = Ui.Box(Ui.Paper, 6, padX: 36, padY: 30);
            if (left) { paper.CornerRadiusTopRight = paper.CornerRadiusBottomRight = 0; } else { paper.CornerRadiusTopLeft = paper.CornerRadiusBottomLeft = 0; }
            panel.AddThemeStyleboxOverride("panel", paper);
            panel.AddChild(new RuledPaper { Pitch = 24, FirstRule = 120, Rule = new Color(Ui.PaperRule, .47f) });
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Ui.Px(14)); panel.AddChild(box);
            spread.AddChild(panel);
            return box;
        }
        var leftPage = Page(true);
        spread.AddChild(new TextureRect { Texture = FieldTexture("guide_gutter"), StretchMode = TextureRect.StretchModeEnum.Tile,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, CustomMinimumSize = new Vector2(Ui.S(28), 0) });
        var rightPage = Page(false);

        var heading = new HBoxContainer(); leftPage.AddChild(heading);
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; heading.AddChild(titles);
        titles.AddChild(Ui.Text("Field guide", 30, Ui.Ink, Ui.SlabBold));
        titles.AddChild(Ui.Text("Notes from the field, kept as you find them.", 14, Ui.InkMuted));
        var count = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; rightPage.AddChild(count);
        var countLabel = Ui.Text($"{noted} of {FieldNoteTexts.Length} noted", 15, Ui.GoldInk, Ui.BodyBold); count.AddChild(countLabel);
        var close = Ui.IconButton("Close", "x", Ui.ButtonKind.Bar, CloseFieldGuide, 13); count.AddChild(close);
        rightPage.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(36)) });

        for (var i = 0; i < FieldNoteTexts.Length; i++)
        {
            var note = FieldNoteTexts[i];
            var known = seen.Contains(note.Id);
            var entry = new HBoxContainer { CustomMinimumSize = new Vector2(0, Ui.S(84)) }; entry.AddThemeConstantOverride("separation", Ui.Px(17));
            (i < 5 ? leftPage : rightPage).AddChild(entry);
            entry.AddChild(DoodleRing(known ? "ring" : "ring_blank", known ? "doodle_" + note.Art : "doodle_unknown", 50, 28, out _));
            var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; words.AddThemeConstantOverride("separation", Ui.Px(4));
            entry.AddChild(words);
            words.AddChild(Ui.Caps($"{i + 1:00} · {(known ? note.Title : "Not yet spotted")}", known ? Ui.GoldInk : new Color("a89a7a"), 10.5f));
            if (known)
            {
                var line = Ui.Text(note.Line, 15, Ui.Ink, Ui.Slab); line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                line.CustomMinimumSize = new Vector2(Ui.S(340), 0); words.AddChild(line);
            }
            else
                foreach (var share in new[] { .92f, .6f })
                {
                    var bar = new Panel { CustomMinimumSize = new Vector2(Ui.S(340 * share), Ui.S(8)), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
                    bar.AddThemeStyleboxOverride("panel", Ui.Box(new Color("e6d8b8"), 4)); words.AddChild(bar);
                }
        }
        var footer = Ui.Text("Notes are remembered for you, not the save, so a new campaign won't repeat them.", 12, Ui.InkMuted);
        footer.HorizontalAlignment = HorizontalAlignment.Center; leftPage.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        rightPage.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill }); rightPage.AddChild(footer);
    }

    private void CloseFieldGuide() { _fieldGuide?.QueueFree(); _fieldGuide = null; }
}
