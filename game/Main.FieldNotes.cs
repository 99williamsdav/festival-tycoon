using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

/// <summary>
/// Field notes: the first time something becomes relevant, the game pauses once and a short note says what's going on,
/// with pins on whatever it's about. Never instructions, just a nudge. Seen notes are remembered for the player, not the
/// save, so a second campaign doesn't repeat them. Allergic guests near a wasp-ridden bin keep a small marker after
/// the wasp note, so a hidden trait can be planned around. Presentation only.
/// </summary>
public partial class Main
{
    private sealed record FieldNote(string Id, string Title, string Line, Func<Vector3[]?> Find);

    private const string FieldNotesFile = "user://field_notes.json";
    private HashSet<string>? _fieldNotesSeen;
    private PanelContainer? _fieldNoteCard;
    private Label? _fieldNoteTitle, _fieldNoteLine;
    private FieldNote? _fieldNoteShown;
    private bool _fieldNotePaused;
    private double _fieldNotePoll;
    private readonly List<Label3D> _fieldNotePins = [];
    private readonly Dictionary<ulong, Label3D> _allergyMarkers = [];

    private FieldNote[] FieldNotes() =>
    [
        new("wasps", "Wasps", "A full bin is a wasp magnet, which can be more than just a nuisance for some...", () =>
        {
            var bins = _session.CaptureBins().Where(b => b.Wasps).ToArray();
            return bins.Length == 0 ? null : bins.Select(b => ImmersionPosition(b.Cell) + Vector3.Up * 1.4f).Concat(AllergicNear(bins)).ToArray();
        }),
        new("heat", "Heat", "It's a scorcher, and not everyone remembers to drink their water...", () =>
            People(_session.CaptureMedical()?.Needs.Where(n => n.Profile == MedicalNeedProfile.Guest && n.Stage == MedicalStage.Distress).Select(n => n.AgentId))),
        new("drunk", "Drink", "Beer makes everyone friendlier, up to a point...", () =>
            People(_session.CaptureImmersion()?.People.Where(p => p.Intoxication >= 5_000).Select(p => p.AgentId))),
        new("stuck", "Stuck", "Portaloo doors have a habit of sticking, and whoever's inside isn't getting out on their own...", () =>
            Places(_session.CaptureFaults()?.Faults.Where(f => f.Kind == FacilityFaultKind.StuckInToilet && f.Stage == FacilityFaultStage.Active)
                .Select(f => _session.CaptureToilets().FirstOrDefault(t => t.Id == f.FacilityId)?.Cell), 3.4f)),
        new("toxic", "Fumes", "A nearly full portaloo is grim at the best of times, and worse if you're stuck in it...", () =>
            Places(_session.CaptureToilets().Where(t => _session.ToiletToxic(t.Id)).Select(t => (GridCell?)t.Cell), 3.4f)),
        new("generator", "Power", "Everything plugged in leans on the generator, and it has its limits...", () =>
            _session.PowerBudgetActive && _session.CapturePower().Over ? Generator() : null),
        new("argument", "Tempers", "Long waits and short tempers don't mix, and words can turn into something worse...", () =>
            People(_session.CaptureDisorder()?.People.Where(p => p.Stage == DisorderStage.Argument).Select(p => p.AgentId))),
        new("band-late", "Late", "The crowd came for the music, and their patience won't last forever...", () =>
            !_session.FestivalBandLate || _session.CaptureProgramme() is not { } q ? null :
                People(q.Performers.Where(r => r.SlotIndex == q.CurrentSlot).Select(r => r.AgentId))),
        new("litter", "Litter", "Not everyone makes it to a bin, and nobody likes standing in rubbish...", () =>
        {
            var ground = _session.CaptureLitter()?.Pieces.Where(w => w.Location == WasteLocation.Ground).Take(6).ToArray();
            return ground is not { Length: > 0 } ? null : ground.Select(w => new Vector3(w.XMillimetres / 1000f, 1.1f, w.ZMillimetres / 1000f)).ToArray();
        }),
        new("dusk", "Lights", "The lights are coming on, and they want their share of the power too...", () =>
            _session.PowerBudgetActive && _session.CapturePower().Lights > 0 ? Generator() : null),
    ];

    private Vector3[]? People(IEnumerable<ulong>? ids)
    {
        var at = ids?.Where(id => _attendeeVisuals.ContainsKey(new EntityId(id))).Take(6)
            .Select(id => _attendeeVisuals[new EntityId(id)].Position + Vector3.Up * 2.6f).ToArray();
        return at is { Length: > 0 } ? at : null;
    }

    private static Vector3[]? Places(IEnumerable<GridCell?>? cells, float height)
    {
        var at = cells?.Where(c => c is not null).Select(c => ImmersionPosition(c!.Value) + Vector3.Up * height).ToArray();
        return at is { Length: > 0 } ? at : null;
    }

    private Vector3[]? Generator() => _session.CaptureEquipment() is { } e ? [new Vector3(e.XMillimetres / 1000f, 2.6f, e.ZMillimetres / 1000f)] : null;

    /// <summary>Allergic guests within reach of a bin's wasps.</summary>
    private IEnumerable<Vector3> AllergicNear(BinReadModel[] bins) => AllergicGuestsNear(bins)
        .Where(id => _attendeeVisuals.ContainsKey(new EntityId(id))).Select(id => _attendeeVisuals[new EntityId(id)].Position + Vector3.Up * 2.6f);

    private IEnumerable<ulong> AllergicGuestsNear(BinReadModel[] bins)
    {
        foreach (var person in _session.CapturePreparation()?.People ?? [])
        {
            if (person.Role != ProtectedPersonRole.Guest || !person.Admitted || person.Departed || !_session.GuestCharacterOf(person.AgentId).WaspAllergy) continue;
            var nav = _session.CaptureObservation().NavigationAgents.FirstOrDefault(a => a.Id.Value == person.AgentId);
            if (nav is null) continue;
            var cell = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
            if (bins.Any(b => Math.Abs(b.Cell.X - cell.X) <= LitterRules.WaspRadiusCells && Math.Abs(b.Cell.Z - cell.Z) <= LitterRules.WaspRadiusCells))
                yield return person.AgentId;
        }
    }

    private void BuildFieldNotes(CanvasLayer layer, Vector2 size)
    {
        _fieldNotesSeen = LoadFieldNotesSeen();
        // Top centre, clear of the incident cards on the left and the stage card on the right.
        _fieldNoteCard = new PanelContainer { Visible = false, AnchorLeft = .5f, AnchorRight = .5f,
            OffsetLeft = -Ui.S(230), OffsetRight = Ui.S(230), OffsetTop = Ui.ContentTop + Ui.S(8), MouseFilter = Control.MouseFilterEnum.Stop };
        _fieldNoteCard.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, 8, Ui.Gold, 2, 20, 16, shadow: 14, shadowAlpha: .38f));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Ui.Px(8)); _fieldNoteCard.AddChild(box);
        _fieldNoteTitle = Ui.Caps("", Ui.GoldInk, 11); box.AddChild(_fieldNoteTitle);
        _fieldNoteLine = Ui.Text("", 15, Ui.Ink, Ui.BodySemi); _fieldNoteLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _fieldNoteLine.CustomMinimumSize = new Vector2(Ui.S(400), 0); box.AddChild(_fieldNoteLine);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; box.AddChild(row);
        var ok = Ui.Style(new Button { Text = "Got it", MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Primary, 13);
        ok.Pressed += DismissFieldNote; row.AddChild(ok);
        layer.AddChild(_fieldNoteCard);
    }

    private void ProcessFieldNotes(double delta)
    {
        if (_fieldNoteCard is null || _fieldNotesSeen is null) return;
        var running = _session.PreparedStatus == PreparationStatus.Running && !EyeViewActive;
        SyncAllergyMarkers(running);
        if (_fieldNoteShown is { } shown)
        {
            // Pins follow whoever they're on; a note left open when the day ends just closes.
            if (!running && _session.PreparedStatus != PreparationStatus.Running) { DismissFieldNote(); return; }
            var at = shown.Find();
            if (at is not null) PlacePins(at);
            BobPins();
            return;
        }
        if (!running) return;
        _fieldNotePoll -= delta;
        if (_fieldNotePoll > 0) return;
        _fieldNotePoll = .5;
        foreach (var note in FieldNotes())
        {
            if (_fieldNotesSeen.Contains(note.Id) || note.Find() is not { } at) continue;
            ShowFieldNote(note, at);
            return;
        }
    }

    private void ShowFieldNote(FieldNote note, Vector3[] at)
    {
        _fieldNoteShown = note;
        _fieldNoteTitle!.Text = note.Title.ToUpperInvariant();
        _fieldNoteLine!.Text = note.Line;
        _fieldNoteCard!.Visible = true;
        // The first time only, the game waits while it's read.
        _fieldNotePaused = !_session.IsPaused;
        if (_fieldNotePaused) { _host.Submit(new SetPausedCommand(true)); RefreshPreparationHud(); }
        PlacePins(at);
        _rig.Frame(new Vector3(at[0].X, 0, at[0].Z), Math.Min(_rig.Camera.Size, 30));
    }

    private void DismissFieldNote()
    {
        if (_fieldNoteShown is { } note && _fieldNotesSeen!.Add(note.Id)) SaveFieldNotesSeen();
        _fieldNoteShown = null;
        _fieldNoteCard!.Visible = false;
        foreach (var pin in _fieldNotePins) pin.Visible = false;
        if (_fieldNotePaused && _session.IsPaused) { _host.Submit(new SetPausedCommand(false)); RefreshPreparationHud(); }
        _fieldNotePaused = false;
    }

    private void PlacePins(Vector3[] at)
    {
        while (_fieldNotePins.Count < at.Length)
        {
            var pin = new Label3D { Text = "▼", FontSize = 64, PixelSize = .0012f, FixedSize = true, OutlineSize = 18,
                OutlineModulate = new Color("1d2a25"), Modulate = Ui.Gold, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                RenderPriority = 3, NoDepthTest = true };
            AddChild(pin); _fieldNotePins.Add(pin);
        }
        for (var i = 0; i < _fieldNotePins.Count; i++)
        {
            _fieldNotePins[i].Visible = i < at.Length;
            if (i < at.Length) _fieldNotePins[i].SetMeta("Anchor", at[i]);
        }
    }

    private void BobPins()
    {
        var lift = .15f * MathF.Sin((float)(Time.GetTicksMsec() / 1000.0 * 4));
        foreach (var pin in _fieldNotePins.Where(p => p.Visible && p.HasMeta("Anchor")))
            pin.Position = pin.GetMeta("Anchor").AsVector3() + Vector3.Up * lift;
    }

    /// <summary>Once the wasp note's been read, allergic guests near wasps wear a small marker.</summary>
    private void SyncAllergyMarkers(bool running)
    {
        var near = running && _fieldNotesSeen!.Contains("wasps") || _fieldNoteShown?.Id == "wasps"
            ? AllergicGuestsNear(_session.CaptureBins().Where(b => b.Wasps).ToArray()).ToHashSet() : [];
        foreach (var id in _allergyMarkers.Keys.Where(id => !near.Contains(id)).ToArray())
        { _allergyMarkers[id].QueueFree(); _allergyMarkers.Remove(id); }
        foreach (var id in near)
        {
            if (!_attendeeVisuals.TryGetValue(new EntityId(id), out var body)) continue;
            if (!_allergyMarkers.TryGetValue(id, out var marker))
            {
                marker = new Label3D { Text = "!", Font = SignFont, FontSize = 44, PixelSize = .0011f, FixedSize = true, OutlineSize = 14,
                    OutlineModulate = new Color("1d2a25"), Modulate = new Color("f2b233"), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    RenderPriority = 2, NoDepthTest = true };
                AddChild(marker); _allergyMarkers.Add(id, marker);
            }
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
}
