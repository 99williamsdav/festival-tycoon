using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The farmer's cows: a herd grazing in the pasture beyond the east hedge, the oak field gate between it and the
/// festival (shut, broken or mended with twine), the cows that get loose when it breaks, and the power leads they chew.
/// Placements from assets/source/environment/cow-pasture-v1/out/cow_pasture_layout_v1.json.
/// </summary>
public partial class Main
{
    private const string CowModel = "res://assets/characters/animals/lwf_cow_v4.glb";
    private const float CowWalkSpeedAt1x = 0.5f;
    private Node3D? _pastureGate;
    private readonly List<Node3D> _pastureCows = [];
    private readonly Dictionary<string, (Node3D Body, AnimationPlayer? Player, Vector3 Last)> _looseCows = [];
    private readonly Dictionary<ulong, string> _cowPicks = [];
    private readonly Dictionary<string, (Node3D Intact, Node3D Chewed, Node3D? Spark, Sprite3D Pin)> _cableViews = [];
    private string? _selectedCowId;
    private Button? _herdCowButton;
    private double _sparkClock;

    private void BuildPasture()
    {
        var root = new Node3D { Name = "Pasture" };
        AddChild(root);
        string Env(string name) => $"res://assets/environment/{name}.glb";
        foreach (var (name, x, z, yaw) in new (string, float, float, float)[]
                 { ("lwf_hedge_gate_end_v1", 32, 0, 90), ("lwf_hedge_gate_end_v1", 32, -5.3f, 270), ("lwf_hedge_straight_4m_a_v1", 32, -4.6f, 90) })
            RegisterBreezeHedge(PlaceBackstagePiece(root, Env(name), x, z, yaw));
        ApplyPastureGrass(PlaceBackstagePiece(root, Env("lwf_cow_pasture_ground_v1"), 45.2f, -2, 0));
        PlaceBackstagePiece(root, Env("lwf_cow_pasture_fence_v1"), 45.2f, -2, 0);
        PlaceBackstagePiece(root, Env("lwf_water_trough_v1"), 35.5f, -6.6f, 0);
        PlaceBackstagePiece(root, Env("lwf_hay_ring_feeder_v1"), 44, 6, 0);
        _pastureGate = PlaceBackstagePiece(root, Env("lwf_field_gate_oak_v1"), 32, -2.65f, 0);
        SetGateState(PastureGateState.Closed);
        // The herd, scattered over the field: grazing or chewing the cud, each a little out of step with the rest.
        for (var i = 0; i < CowRules.HerdSize; i++)
        {
            var x = 37 + (i * 7.3f) % 19; var z = -22 + (i * 11.7f) % 40;
            var cow = PlaceBackstagePiece(root, CowModel, x, z, (i * 67) % 360);
            PlayCowClip(cow, i % 3 == 0 ? "idle" : "graze", i * 0.37f);
            _pastureCows.Add(cow);
        }
    }

    private void SetGateState(PastureGateState state)
    {
        if (_pastureGate is null) return;
        void Show(string node, bool visible) { if (_pastureGate.FindChild(node, true, false) is Node3D n) n.Visible = visible; }
        Show("LWF_FieldGate_LeafIntact", state == PastureGateState.Closed);
        Show("LWF_FieldGate_LeafBroken", state == PastureGateState.Broken);
        Show("LWF_FieldGate_LeafRepaired", state == PastureGateState.Repaired);
        Show("LWF_FieldGate_Repair", state == PastureGateState.Repaired);
    }

    private static AnimationPlayer? PlayCowClip(Node3D cow, string clip, float startAt = 0)
    {
        if (cow.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().FirstOrDefault() is not { } player || !player.HasAnimation(clip)) return null;
        player.GetAnimation(clip).LoopMode = Animation.LoopModeEnum.Linear;
        if (player.CurrentAnimation != clip) { player.Play(clip, customBlend: .3); if (startAt > 0) player.Seek(startAt % (float)player.CurrentAnimationLength, true); }
        return player;
    }

    private void ProcessCows(double delta)
    {
        var cows = _session.CaptureCows();
        SetGateState(cows?.Gate ?? PastureGateState.Closed);
        var loose = cows?.Loose ?? [];
        // The herd thins as cows get out, and fills again as they're driven home.
        for (var i = 0; i < _pastureCows.Count; i++) _pastureCows[i].Visible = i < CowRules.HerdSize - loose.Length;
        foreach (var gone in _looseCows.Keys.Where(id => loose.All(c => c.Id != id)).ToArray())
        {
            var view = _looseCows[gone];
            foreach (var pick in _cowPicks.Where(p => p.Value == gone).Select(p => p.Key).ToArray()) _cowPicks.Remove(pick);
            view.Body.QueueFree(); _looseCows.Remove(gone);
            if (_selectedCowId == gone) ClearSelection();
        }
        var paused = CharacterPresentationPaused;
        foreach (var cow in loose)
        {
            var at = new Vector3(cow.XMillimetres / 1000f, 0.04f, cow.ZMillimetres / 1000f);
            if (!_looseCows.TryGetValue(cow.Id, out var view))
            {
                var body = InstantiateAsset(CowModel); body.Name = $"Cow_{cow.Id}"; body.Position = at; AddChild(body);
                var pick = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
                pick.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.2f, 1.5f, 0.9f) }, Position = new Vector3(0, 0.75f, 0) });
                body.AddChild(pick); _cowPicks[pick.GetInstanceId()] = cow.Id;
                view = (body, null, at);
            }
            // Facing the way it's going; the walk plays at the pace it's really moving, so its hooves stay planted.
            var moved = new Vector3(at.X - view.Last.X, 0, at.Z - view.Last.Z);
            if (moved.LengthSquared() > 1e-6f) view.Body.Rotation = new Vector3(0, Mathf.LerpAngle(view.Body.Rotation.Y, Mathf.Atan2(-moved.Z, moved.X), .2f), 0);
            view.Body.Position = at;
            var walking = cow.Activity == CowActivity.Ambling || cow.Driving;
            var player = PlayCowClip(view.Body, walking ? "walk" : cow.Activity == CowActivity.Herded ? "idle" : "graze");
            if (player is not null)
                player.SpeedScale = paused ? 0 : walking && delta > 0 ? Mathf.Clamp(moved.Length() / (float)delta / CowWalkSpeedAt1x, .2f, 4f) : 1;
            _looseCows[cow.Id] = (view.Body, player, at);
        }
        ProcessCables(delta);
        if (_selectedCowId is not null) RefreshCowInspector();
    }

    /// <summary>A lead from each utility to where its cable runs, swapped for a chewed one (sparking, with a pin) when cut.</summary>
    private void ProcessCables(double delta)
    {
        if (_session.CaptureEquipment() is not { } equipment) return;
        _sparkClock += _session.IsPaused ? 0 : delta;
        var flash = _sparkClock % 1.0 < 0.1;
        foreach (var (utility, spot) in _session.CableSpots())
        {
            if (!_cableViews.TryGetValue(utility, out var view))
            {
                var from = utility == "generator" ? new Vector3(equipment.XMillimetres / 1000f, 0, equipment.ZMillimetres / 1000f)
                    : ImmersionPosition(_session.CaptureVendors().Single(v => v.Id == utility).Cell);
                var to = ImmersionPosition(spot);
                var yaw = Mathf.Atan2(-(to.Z - from.Z), to.X - from.X);
                var intact = InstantiateAsset("res://assets/environment/lwf_power_lead_v1.glb"); intact.Position = from; intact.Rotation = new Vector3(0, yaw, 0); AddChild(intact);
                var chewed = InstantiateAsset("res://assets/environment/lwf_power_lead_chewed_v1.glb"); chewed.Position = from; chewed.Rotation = new Vector3(0, yaw, 0); AddChild(chewed);
                var pin = WorldSprite(GD.Load<Texture2D>("res://assets/ui/field-notes/pin_chewed_cable.png"), 4, new Vector2(0, 32));
                pin.Position = from + new Vector3(0, 3.2f, 0); pin.Layers = EyeHiddenLayer; AddChild(pin);
                view = (intact, chewed, chewed.FindChild("LWF_Spark", true, false) as Node3D, pin);
                _cableViews[utility] = view;
            }
            var cut = _session.CableCut(utility);
            view.Intact.Visible = !cut; view.Chewed.Visible = cut; view.Pin.Visible = cut;
            if (view.Spark is not null) view.Spark.Visible = cut && flash;
        }
    }

    private bool TrySelectCow(GodotObject collider)
    {
        if (!_cowPicks.TryGetValue(collider.GetInstanceId(), out var cowId)) return false;
        ClearSelection();
        _selectedCowId = cowId;
        RefreshCowInspector();
        return true;
    }

    private void BuildCowInspectorAction(VBoxContainer parent)
    {
        _herdCowButton = ButtonText("Herd it back to the field", () =>
        {
            if (_selectedCowId is not { } id) return;
            _preparationMessage = _host.Execute(new HerdCowCommand(id), out var error) ? "A steward is on their way to herd the cow home." : error!;
            RefreshPreparationHud(); RefreshCowInspector();
        });
        _herdCowButton.Visible = false;
        parent.AddChild(_herdCowButton);
    }

    private void RefreshCowInspector()
    {
        if (_herdCowButton is not null) _herdCowButton.Visible = _selectedCowId is not null;
        if (_selectedCowId is not { } id || _session.CaptureCows()?.Loose.FirstOrDefault(c => c.Id == id) is not { } cow || !_looseCows.TryGetValue(id, out var view)) return;
        _inspectorTitle.Text = "A loose cow";
        var herder = cow.HerderId is { } h ? _session.CapturePreparation()!.People.Single(p => p.AgentId == h).Name : null;
        _inspectorBody.Text = herder is not null ? cow.Driving ? $"{herder} is walking it back to the gate." : $"{herder} is on the way to round it up." :
            cow.Activity == CowActivity.Ambling ? "Ambling about the festival." : "Grazing. It might chew a power cable if it wanders behind the generator, the bar or the food van.";
        var (_, reason) = _session.CowHerder(id);
        _herdCowButton!.Disabled = reason is not null;
        _herdCowButton.TooltipText = reason ?? "Send the nearest free steward to drive it back through the gate.";
        _highlight.Position = view.Body.Position + new Vector3(0, .08f, 0);
        _highlight.Scale = new Vector3(1.6f, 1, 1.6f); _highlight.Visible = true;
    }
}
