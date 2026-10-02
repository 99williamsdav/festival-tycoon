using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Prototype: see the festival through one person's eyes. A perspective camera rides at their eye height, facing
/// where they face, smoothed so their turns don't jerk the view. World labels are hidden, fog and a distant ground
/// stand in for the countryside beyond the hedge, and the sound follows their head. The day carries on as normal;
/// Esc or the button returns to the usual view. Presentation only.
/// </summary>
public partial class Main
{
    private Camera3D? _eyeCamera;
    private EntityId? _eyeTarget;
    private Button? _eyeViewButton;
    private CanvasLayer? _eyeOverlay;
    private Label? _eyeCaption;
    private MeshInstance3D? _eyeGround;
    private readonly List<Label3D> _eyeHiddenLabels = [];
    private double _eyeLabelSweep;
    private float _eyeYaw;
    private bool _eyeFogWas;

    private bool EyeViewActive => _eyeTarget is not null;
    private const uint EyeSelfLayer = 1u << 19, EyeHiddenLayer = 1u << 18;

    private static void SetEyeSelfLayer(Node3D visual, bool self)
    {
        foreach (var mesh in visual.FindChildren("*", "VisualInstance3D", true, false).OfType<VisualInstance3D>())
            mesh.Layers = self ? EyeSelfLayer : 1u;
    }

    /// <summary>Where the ears are: the person's head in first person, otherwise the camera's focus.</summary>
    private Vector3 ListenerPoint => EyeViewActive && _eyeCamera is not null ? _eyeCamera.GlobalPosition with { Y = 0 } : _rig.Focus;

    private void BuildEyeViewAction(VBoxContainer parent)
    {
        _eyeViewButton = ButtonText("See through their eyes", () =>
        {
            if (_selectedAttendeeId is { } id) EnterEyeView(id);
        });
        _eyeViewButton.TooltipText = "Follow this person in first person. Esc returns to the overview.";
        _eyeViewButton.Visible = false;
        parent.AddChild(_eyeViewButton);
    }

    private void EnterEyeView(EntityId id)
    {
        if (!_attendeeVisuals.TryGetValue(id, out var visual)) return;
        _eyeTarget = id;
        // The eyes can't see the head they sit in: that person's meshes go on a layer this camera skips.
        _eyeCamera ??= new Camera3D { Projection = Camera3D.ProjectionType.Perspective, Fov = 72, Near = 0.05f, Far = 600,
            CullMask = 0xFFFFF & ~EyeSelfLayer & ~EyeHiddenLayer };
        if (_eyeCamera.GetParent() is null) AddChild(_eyeCamera);
        _eyeYaw = visual.Rotation.Y;
        PlaceEyeCamera(visual, 1);
        _eyeCamera.Current = true;
        SetEyeSelfLayer(visual, true);
        // Out past the hedge: a muted field running to a fogged horizon, in place of the void.
        _eyeGround ??= new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(1200, 1200) }, Position = new Vector3(0, -0.03f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("7d9a5a"), Roughness = 1 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        if (_eyeGround.GetParent() is null) AddChild(_eyeGround);
        _eyeGround.Visible = true;
        _eyeFogWas = _environment.FogEnabled;
        _environment.FogEnabled = true;
        _environment.FogDensity = 0.012f;
        _environment.FogSkyAffect = 0;
        HideWorldLabels();
        if (_eyeOverlay is null)
        {
            _eyeOverlay = new CanvasLayer { Layer = 20 };
            _eyeCaption = new Label { Position = new Vector2(24, 80) };
            _eyeCaption.AddThemeFontSizeOverride("font_size", 18);
            _eyeCaption.AddThemeColorOverride("font_color", Colors.White);
            _eyeCaption.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, .8f));
            _eyeCaption.AddThemeConstantOverride("outline_size", 6);
            _eyeOverlay.AddChild(_eyeCaption);
            AddChild(_eyeOverlay);
        }
        var name = _session.CapturePreparation()?.People.FirstOrDefault(p => p.AgentId == id.Value)?.Name ?? "Someone";
        _eyeCaption!.Text = $"Seeing through {name}'s eyes  ·  Esc to return";
        _eyeOverlay.Visible = true;
    }

    private void ExitEyeView()
    {
        if (_eyeTarget is { } id && _attendeeVisuals.TryGetValue(id, out var visual) && IsInstanceValid(visual)) SetEyeSelfLayer(visual, false);
        _eyeTarget = null;
        if (_eyeCamera is not null) _eyeCamera.Current = false;
        _rig.Camera.Current = true;
        if (_eyeGround is not null) _eyeGround.Visible = false;
        _environment.FogEnabled = _eyeFogWas;
        foreach (var label in _eyeHiddenLabels.Where(IsInstanceValid)) label.Layers = 1u;
        _eyeHiddenLabels.Clear();
        if (_eyeOverlay is not null) _eyeOverlay.Visible = false;
    }

    /// <summary>Names, speech and building signs are sized for the overview; at eye level they would fill the screen.</summary>
    private void HideWorldLabels()
    {
        foreach (var label in FindChildren("*", "Label3D", true, false).OfType<Label3D>())
            // Moved to a layer the eye camera skips: other code toggles their visibility as people talk.
            if (label.Layers != EyeHiddenLayer && !IsGateLettering(label)) { label.Layers = EyeHiddenLayer; _eyeHiddenLabels.Add(label); }
    }

    // The gate sign and banner lettering belong to the world, so they stay.
    private static bool IsGateLettering(Node label) => label.GetParent() is Node3D parent && parent.Name == "LetteringArea";

    private void PlaceEyeCamera(Node3D visual, float follow)
    {
        var tall = visual.HasMeta("GuestPoseVariant") ? visual.GetMeta("GuestPoseVariant").AsString() :
            visual.HasMeta("RoleVariant") ? visual.GetMeta("RoleVariant").AsString() : "male";
        var eye = tall == "female" ? 1.53f : 1.60f;
        // A collapsed person lies down: the view drops to the grass.
        if (Mathf.Abs(visual.Rotation.X) > 1f) eye = 0.25f;
        _eyeYaw = Mathf.LerpAngle(_eyeYaw, visual.Rotation.Y, follow);
        var target = visual.GlobalPosition + new Vector3(0, eye, 0);
        _eyeCamera!.GlobalPosition = _eyeCamera.GlobalPosition.Lerp(target, Mathf.Max(follow, .5f));
        _eyeCamera.Rotation = new Vector3(Mathf.DegToRad(-6), _eyeYaw, 0);
    }

    private void ProcessEyeView(double delta)
    {
        if (_eyeViewButton is not null)
            _eyeViewButton.Visible = !EyeViewActive && _selectedAttendeeId is { } selected && _attendeeVisuals.ContainsKey(selected) &&
                _session.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing;
        if (_eyeTarget is not { } id) return;
        if (!_attendeeVisuals.TryGetValue(id, out var visual) || !IsInstanceValid(visual) ||
            _session.CapturePreparation()?.People.FirstOrDefault(p => p.AgentId == id.Value)?.Departed == true)
        { ExitEyeView(); return; }
        SetEyeSelfLayer(visual, true); // Poses, kits and held things come and go.
        PlaceEyeCamera(visual, Mathf.Clamp((float)delta * 4f, 0, 1));
        _environment.FogLightColor = _environment.BackgroundColor;
        // New speech bubbles and names appear as the day goes on.
        _eyeLabelSweep -= delta;
        if (_eyeLabelSweep <= 0) { _eyeLabelSweep = .25; HideWorldLabels(); }
    }
}
