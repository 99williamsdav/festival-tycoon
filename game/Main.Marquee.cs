using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The "Under My Umbrella" stretch tent on the field: the model with its faded roof, pick colliders on its poles and
/// pegs (never the sail, so the guests under it stay clickable), a name sign and the inspector.
/// </summary>
public partial class Main
{
    private const string MarqueeAsset = "res://assets/environment/lwf_stretch_tent_v1.glb";
    private sealed record MarqueeView(StaticBody3D Body, RoofFade Roof);
    private readonly Dictionary<string, MarqueeView> _marqueeViews = [];
    private readonly Dictionary<ulong, string> _marqueePickOwners = [];
    private string? _selectedMarqueeId;
    private Button? _marqueeMoveButton;
    internal const string MarqueeTitle = "Under My Umbrella";

    // Poles and pegs in the model's own metres (out/lwf_stretch_tent_layout_v1.json): x, z, height. Picked a little
    // wider than they're drawn, so a thin pole can still be clicked.
    private static readonly (float X, float Z, float Height)[] MarqueeUprights =
    [
        (-1.7f, -.3f, 3.96f), (1.9f, .3f, 3.76f),
        (-3.7f, -2.7f, 1.91f), (3.7f, -2.7f, 2.01f), (-3.7f, 2.7f, 2.41f), (3.7f, 2.7f, 2.31f),
        (-4.6f, -3.35f, .3f), (4.6f, -3.35f, .3f), (-4.6f, 3.35f, .3f), (4.6f, 3.35f, .3f),
    ];

    private static Node3D InstantiateMarquee() => InstantiateAsset(MarqueeAsset);

    private void SyncMarqueeWorld()
    {
        var tents = _session.CaptureMarquees();
        foreach (var stale in _marqueeViews.Keys.Except(tents.Select(tent => tent.Id)).ToArray())
        {
            var view = _marqueeViews[stale];
            _marqueePickOwners.Remove(view.Body.GetInstanceId()); view.Body.QueueFree(); _marqueeViews.Remove(stale);
            if (_selectedMarqueeId == stale) ClearSelection();
        }
        var now = Time.GetTicksMsec() / 1000.0;
        foreach (var tent in tents)
        {
            if (!_marqueeViews.TryGetValue(tent.Id, out var view))
            {
                var body = new StaticBody3D { Name = "Marquee-" + tent.Id, CollisionLayer = 1, CollisionMask = 0 };
                var model = InstantiateMarquee();
                body.AddChild(model);
                foreach (var (x, z, height) in MarqueeUprights)
                    body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = .22f, Height = height }, Position = new(x, height / 2, z) });
                body.AddChild(BuildingName("MARQUEE", new Vector3(0, 4.4f, 0), 28));
                AddChild(body);
                _marqueePickOwners[body.GetInstanceId()] = tent.Id;
                view = new(body, RoofFade.Attach(model)); _marqueeViews[tent.Id] = view;
            }
            view.Body.Position = ImmersionPosition(tent.Cell);
            view.Body.RotationDegrees = new(0, tent.QuarterTurns * 90, 0);
            // Faded while anyone is under it, or while it's selected so the player can see in.
            view.Roof.Update(tent.Sheltering > 0 || _selectedMarqueeId == tent.Id, now);
        }
        RefreshMarqueeInspector();
    }

    private void BuildMarqueeInspector(VBoxContainer parent)
    {
        _marqueeMoveButton = ButtonText("Move marquee", () => { if (_selectedMarqueeId is { } id) BeginBuildPlacement(BuildServiceKind.Marquee, id); });
        _marqueeMoveButton.Visible = false;
        parent.AddChild(_marqueeMoveButton);
    }

    private void SelectMarquee(string id) { ClearSelection(); _selectedMarqueeId = id; RefreshMarqueeInspector(); }

    private void RefreshMarqueeInspector()
    {
        if (_marqueeMoveButton is not null) _marqueeMoveButton.Visible = _selectedMarqueeId is not null && _session.PreparedStatus == PreparationStatus.Preparing;
        if (_selectedMarqueeId is not { } id || _session.CaptureMarquees().SingleOrDefault(tent => tent.Id == id) is not { } tent ||
            !_marqueeViews.TryGetValue(id, out var view)) return;
        RefreshContextPanelVisibility();
        _inspectorTitle.Text = MarqueeTitle;
        _inspectorBody.Text = _session.PreparedStatus == PreparationStatus.Preparing
            ? $"Stretch tent · {FestivalCurrency.Format(MarqueeRules.FeePennies)} hire for the day\n" +
              $"Shade for up to {tent.RestCapacity} overheated guests to rest in.\nNobody heats up under the sail."
            : $"{tent.Sheltering} under the sail · {tent.Resting}/{tent.RestCapacity} resting in the shade\n" +
              "Nobody heats up under the sail. Overheated guests rest here when it's nearer than first aid; once it's full they go to first aid.";
        _highlight.Position = view.Body.Position + new Vector3(0, .08f, 0); _highlight.Scale = new(4.4f, 1, 4.4f); _highlight.Visible = true;
    }
}
