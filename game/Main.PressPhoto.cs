using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Festival.Game;

/// <summary>A photograph for the paper: what it shows, and how many were in front of the stage when it was taken.</summary>
internal sealed record PressPhoto(Image Image, string StageId, int Slot, string ActId, int Crowd, bool Aerial);

/// <summary>
/// The paper's photographer. While a set plays with its band on stage, a camera behind the crowd at a photographer's
/// eye height snaps the stage each time that set's crowd reaches a new high. The paper prints the shot from the best
/// set. It renders into its own viewport only at the moment of a shot, and never touches the main camera or the sim.
/// The chosen shot is kept beside the saves, so the paper reopened after a load still has it.
/// </summary>
public partial class Main
{
    private const uint PressHiddenLayer = 1u << 17;
    private readonly Dictionary<(string Stage, int Slot), PressPhoto> _pressPhotos = [];
    private (GameSession? Session, int Attempt) _pressPhotoDay;
    private SubViewport? _pressViewport;
    private Camera3D? _pressCamera;
    private double _pressPoll;
    private readonly Dictionary<(string Stage, int Slot), double> _pressLastShot = [];
    private (string Stage, int Slot, string ActId, int Crowd, bool Aerial)? _pressPending;
    // A fresh viewport's first frame can come out without its sky, so a shot renders a couple of frames before it's read.
    private int _pressFrames;
    private readonly List<(VisualInstance3D Node, uint Layers)> _pressHidden = [];

    private void ProcessPressPhoto(double delta)
    {
        if (_pressPending is { } pending) { if (--_pressFrames <= 0) FinishPressShot(pending); return; }
        if (_session.CapturePreparation() is not { } p) return;
        if (_pressPhotoDay != (_session, p.Attempt)) { _pressPhotos.Clear(); _pressLastShot.Clear(); _pressPhotoDay = (_session, p.Attempt); }
        // First person moves the labels between layers itself; the photographer waits.
        if (p.Status != PreparationStatus.Running || EyeViewActive) return;
        if ((_pressPoll -= delta) > 0) return;
        _pressPoll = 0.5;
        var now = Time.GetTicksMsec() / 1000.0;
        foreach (var live in _session.CaptureLivePerformances())
        {
            if (live.Stage != LiveSetStage.Live || live.Performers.Length == 0 || !live.Performers.All(member => member.OnStage)) continue;
            if (_session.CaptureProgramme(live.StageId) is not { CurrentSlot: >= 0 } programme || programme.CurrentSlot >= programme.ActIds.Length) continue;
            var key = (live.StageId, programme.CurrentSlot);
            var crowd = live.Listeners.Count(listener => listener.AtPlace);
            var best = _pressPhotos.TryGetValue(key, out var shot) ? shot.Crowd : -1;
            // A new high, by enough to be worth another frame, and not more than every few seconds.
            if (crowd <= best + Math.Max(0, best / 8) || _pressLastShot.TryGetValue(key, out var last) && now - last < 3) continue;
            if (StartPressShot(live, programme.CurrentSlot, programme.ActIds[programme.CurrentSlot], crowd)) { _pressLastShot[key] = now; return; }
        }
    }

    /// <summary>Frames the stage from behind its crowd (or from above when there's no crowd to stand in) and asks for one render.</summary>
    private bool StartPressShot(LivePerformanceSnapshot live, int slot, string actId, int crowd)
    {
        if (FestivalStages.Find(live.StageId) is not { } stage) return false;
        var deck = stage.Placement.DeckHeightMillimetres / 1000f;
        var band = stage.PerformerMarks.Select(mark => TraversalGrid.CellCentre(mark))
            .Aggregate(Vector3.Zero, (sum, c) => sum + new Vector3(c.XMillimetres / 1000f, 0, c.ZMillimetres / 1000f)) / stage.PerformerMarks.Count;
        var forward = new Vector3(stage.Facing.X, 0, stage.Facing.Z);
        var side = new Vector3(-forward.Z, 0, forward.X);
        var people = live.Listeners.Where(listener => listener.AtPlace)
            .Select(listener => _attendeeVisuals.TryGetValue(new EntityId(listener.AgentId), out var visual) && IsInstanceValid(visual) ? visual.GlobalPosition : (Vector3?)null)
            .OfType<Vector3>().ToArray();
        var aerial = people.Length < 3;
        Vector3 eye;
        if (aerial)
            eye = band + forward * 14 + side * 4 + Vector3.Up * 9;
        else
        {
            // Just behind most of the crowd, a little to the side of their middle, camera held up a little.
            var depths = people.Select(point => (point - band).Dot(forward)).Order().ToArray();
            var laterals = people.Select(point => (point - band).Dot(side)).Order().ToArray();
            var back = depths[(int)(depths.Length * 0.85f)] + 2.5f;
            eye = band + forward * Mathf.Max(back, 7) + side * (laterals[laterals.Length / 2] * 0.6f + 0.8f) + Vector3.Up * 1.8f;
            // Not with someone's head filling the lens: step sideways until the near view towards the stage is clear.
            foreach (var step in new[] { 0f, 0.8f, -0.8f, 1.6f, -1.6f, 2.4f })
            {
                var spot = eye + side * step;
                if (!people.Any(point => (spot - point).Dot(forward) is var ahead && ahead > -0.6f && ahead < 2.5f &&
                    Mathf.Abs((point - spot).Dot(side)) < 0.4f + ahead * 0.25f)) { eye = spot; break; }
            }
        }
        eye = new Vector3(Mathf.Clamp(eye.X, -31, 31), eye.Y, Mathf.Clamp(eye.Z, -31, 31));
        var target = band + Vector3.Up * (deck + (aerial ? 0.4f : 1.0f));

        if (_pressViewport is null)
        {
            _pressViewport = new SubViewport { Size = new Vector2I(960, 540), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                Msaa3D = Viewport.Msaa.Msaa4X, World3D = GetViewport().FindWorld3D(), Name = "PressPhotographer" };
            _pressCamera = new Camera3D { Current = true, Near = 0.2f, Far = 600,
                CullMask = 0xFFFFF & ~PressHiddenLayer & ~EyeSelfLayer & ~EyeHiddenLayer };
            _pressViewport.AddChild(_pressCamera);
            AddChild(_pressViewport);
        }
        // Fog softens the far countryside, as it does in first person; the main view keeps its own environment.
        var environment = (Godot.Environment)_environment.Duplicate();
        environment.FogEnabled = true; environment.FogDensity = 0.008f; environment.FogSkyAffect = 0; environment.FogLightColor = _environment.BackgroundColor;
        // Seen low and across, wet ground mirrors the bright sky white; the overview never looks at it that way.
        environment.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled;
        _pressCamera!.Environment = environment;
        _pressCamera.LookAtFromPosition(eye, target, Vector3.Up);
        // Wide enough for the stage and the heads in front of it.
        var distance = eye.DistanceTo(target);
        var across = aerial ? 16f : 12f;
        _pressCamera.Fov = Mathf.Clamp(Mathf.RadToDeg(2 * Mathf.Atan(across / 2 / distance * 9 / 16)), 22, 55);

        // Names, speech and mood faces are for the overview: onto a layer only the main camera sees, for this one frame.
        foreach (var node in FindChildren("*", "VisualInstance3D", true, false).OfType<VisualInstance3D>())
            if (node is Label3D or Sprite3D && node.Layers == 1u && !IsGateLettering(node)) { _pressHidden.Add((node, node.Layers)); node.Layers = PressHiddenLayer; }
        _pressViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _pressFrames = 2;
        _pressPending = (live.StageId, slot, actId, crowd, aerial);
        return true;
    }

    private void FinishPressShot((string Stage, int Slot, string ActId, int Crowd, bool Aerial) pending)
    {
        _pressPending = null;
        if (_pressViewport is not null) _pressViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        foreach (var (node, layers) in _pressHidden)
            if (IsInstanceValid(node) && node.Layers == PressHiddenLayer) node.Layers = layers;
        _pressHidden.Clear();
        var image = _pressViewport?.GetTexture().GetImage();
        if (image is null || image.IsEmpty()) return;
        var key = (pending.Stage, pending.Slot);
        // A crowd shot beats any aerial one of the same set.
        if (_pressPhotos.TryGetValue(key, out var held) && !held.Aerial && pending.Aerial) return;
        _pressPhotos[key] = new PressPhoto(image, pending.Stage, pending.Slot, pending.ActId, pending.Crowd, pending.Aerial);
    }

    /// <summary>
    /// The photo for the finished festival: the best set's shot, or the best of any taken; else the one kept beside the
    /// saves from when the paper first opened. Null when there's none, and the paper prints its poster instead.
    /// </summary>
    private PressPhoto? PressPhotoForPaper()
    {
        if (_session.CapturePreparation() is not { Result: not null } p) return null;
        var path = Path.Combine(_host.SaveDirectory, "press-photos", $"{_session.CampaignId.Value}-t{p.Tier}-a{p.Attempt}");
        if (_pressPhotoDay == (_session, p.Attempt) && _pressPhotos.Count > 0)
        {
            var ranked = FestivalReview.BestFirst(_session.PerformanceRecords)
                .Select(record => _pressPhotos.GetValueOrDefault((record.StageId, record.Slot))).OfType<PressPhoto>();
            var chosen = ranked.FirstOrDefault(photo => !photo.Aerial) ?? ranked.FirstOrDefault() ?? _pressPhotos.Values.OrderByDescending(photo => photo.Crowd).First();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                chosen.Image.SavePng(path + ".png");
                File.WriteAllText(path + ".txt", $"{chosen.StageId}|{chosen.Slot}|{chosen.ActId}|{chosen.Crowd}|{(chosen.Aerial ? 1 : 0)}");
            }
            catch (Exception exception) { GD.PushWarning("Press photo not kept: " + exception.Message); }
            return chosen;
        }
        try
        {
            if (!File.Exists(path + ".png") || !File.Exists(path + ".txt")) return null;
            var parts = File.ReadAllText(path + ".txt").Split('|');
            var image = Image.LoadFromFile(path + ".png");
            if (parts.Length != 5 || image is null || image.IsEmpty()) return null;
            return new PressPhoto(image, parts[0], int.Parse(parts[1]), parts[2], int.Parse(parts[3]), parts[4] == "1");
        }
        catch (Exception) { return null; }
    }
}
