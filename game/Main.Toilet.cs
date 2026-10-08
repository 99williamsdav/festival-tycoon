using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private const string ToiletAsset = "res://assets/environment/portaloo/lwf_portaloo_v1.glb";
    private const string OccupiedIndicatorAsset = "res://assets/environment/portaloo/lwf_portaloo_indicator_occupied_v1.glb";
    private readonly HashSet<ulong> _toiletPickIds = [];
    private readonly Dictionary<ulong, string> _toiletPickOwners = [];
    private sealed record ToiletView(StaticBody3D Body, Node3D Door, Node3D Free, Node3D Occupied);
    private readonly Dictionary<string, ToiletView> _toiletViews = [];
    private bool _selectedToilet;
    private string? _selectedToiletId;
    private Button? _toiletMoveButton;

    private static Node3D RequireToiletNode(Node3D root, string name) =>
        root.FindChild(name, true, false) as Node3D ?? throw new InvalidOperationException("Approved portaloo node missing: " + name);

    private void AddToiletCollision(StaticBody3D body, Node3D doorPivot, string id)
    {
        // Disjoint shell boxes keep the measured doorway/interior open. The moving
        // door has its own body under DoorPivot, never part of a static hull.
        foreach (var (centre, size) in new (Vector3, Vector3)[]
        {
            (new(0,.04f,0),new(1.46f,.08f,1.56f)),
            (new(-.67f,1.11f,0),new(.06f,2.06f,1.5f)),
            (new(.67f,1.11f,0),new(.06f,2.06f,1.5f)),
            (new(-.565f,1.09f,-.73f),new(.27f,2.02f,.08f)),
            (new(.565f,1.09f,-.73f),new(.27f,2.02f,.08f)),
            (new(0,1.11f,.72f),new(1.28f,2.06f,.06f)),
            (new(0,2.135f,-.73f),new(1.4f,.07f,.08f)),
            (new(0,2.24f,0),new(1.5f,.22f,1.58f)),
            (new(0,.275f,.465f),new(1.12f,.39f,.45f)),
        }) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = centre });
        var door = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0, Name = "PortalooDoorPick" };
        doorPivot.AddChild(door);
        door.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.9f,2.032f,.035f) },
            Position = new(-.45f,1.024f,0) });
        _toiletPickIds.Add(body.GetInstanceId());
        _toiletPickIds.Add(door.GetInstanceId());
        _toiletPickOwners[body.GetInstanceId()] = id;
        _toiletPickOwners[door.GetInstanceId()] = id;
    }

    private void SyncToiletWorld()
    {
        var toilets = _session.CaptureToilets();
        foreach (var stale in _toiletViews.Keys.Except(toilets.Select(item => item.Id)).ToArray())
        {
            var view = _toiletViews[stale];
            foreach (var key in _toiletPickOwners.Where(pair => pair.Value == stale).Select(pair => pair.Key).ToArray())
            { _toiletPickOwners.Remove(key); _toiletPickIds.Remove(key); }
            view.Body.QueueFree(); _toiletViews.Remove(stale);
        }
        foreach (var toilet in toilets)
        {
            if (!_toiletViews.TryGetValue(toilet.Id, out var view))
            {
                var body = new StaticBody3D { Name = "OwnedPortaloo-" + toilet.Id, CollisionLayer = 1, CollisionMask = 0 };
                var visual = InstantiateAsset(ToiletAsset);
                body.AddChild(visual);
                var door = RequireToiletNode(visual, "DoorPivot");
                var socket = RequireToiletNode(visual, "OccupancySocket");
                var free = RequireToiletNode(visual, "IndicatorFree");
                var occupied = InstantiateAsset(OccupiedIndicatorAsset);
                socket.AddChild(occupied);
                AddChild(body);
                AddToiletCollision(body, door, toilet.Id);
                var name = BuildingName("TOILET", new Vector3(0, 2.8f, 0), 28);
                body.AddChild(name);
                view = new(body, door, free, occupied); _toiletViews.Add(toilet.Id, view);
            }
            view.Body.Position = ImmersionPosition(toilet.Cell);
            view.Body.RotationDegrees = new Vector3(0, toilet.QuarterTurns * 90, 0);
            view.Door.RotationDegrees = new Vector3(0, toilet.DoorOpen ? -110 : 0, 0);
            view.Occupied.Visible = toilet.OccupiedIndicator;
            view.Free.Visible = !toilet.OccupiedIndicator;
        }
        RefreshToiletInspector();
    }

    private void BuildToiletInspector(VBoxContainer parent)
    {
        _toiletMoveButton = ButtonText("Move", () =>
        {
            if (_selectedToilet) BeginToiletPlacement();
            RefreshPreparationHud();
        });
        _toiletMoveButton.Visible = false;
        _toiletMoveButton.TooltipText = "Move before opening. Comma/period rotate; right-click or Esc cancels.";
        parent.AddChild(_toiletMoveButton);
        _toiletOccupantButton = ButtonText("", () =>
        {
            if (_selectedToiletId is { } id && ToiletOccupant(id) is { } occupant) SelectAttendee(new EntityId(occupant));
        });
        _toiletOccupantButton.Visible = false;
        _toiletOccupantButton.TooltipText = "Select whoever is inside (or double-click the toilet).";
        parent.AddChild(_toiletOccupantButton);
        _lavSuckerButton = ButtonText($"Call Dav's Lav-Sucker ({FestivalCurrency.Format(LavSuckerRules.FeePennies)})", () =>
        {
            if (_selectedToiletId is not { } id) return;
            _preparationMessage = _host.Execute(new CallLavSuckerCommand(id), out var error) ? "Dav's Lav-Sucker is on the way." : error!;
            RefreshPreparationHud(); RefreshToiletInspector();
        });
        _lavSuckerButton.Visible = false;
        parent.AddChild(_lavSuckerButton);
    }

    private Button? _toiletOccupantButton;
    private Button? _lavSuckerButton;

    /// <summary>Whoever is inside a toilet: its user, or someone it is waiting to let out.</summary>
    private ulong? ToiletOccupant(string toiletId) =>
        _session.CaptureToilets().SingleOrDefault(t => t.Id == toiletId) is { } toilet ? toilet.OwnerId ?? toilet.InterruptedOccupantId : null;

    private void SelectToilet(string? id = null)
    {
        ClearSelection(); _selectedToilet = true; _selectedToiletId = id ?? _session.CaptureToilet()?.Id; RefreshToiletInspector();
    }

    private void RefreshToiletInspector()
    {
        RefreshContextPanelVisibility();
        if (_toiletMoveButton is null) return;
        _toiletMoveButton.Visible = _selectedToilet && _session.PreparedStatus == PreparationStatus.Preparing;
        _toiletMoveButton.Text = false ? "Cancel move" : "Move";
        if (_lavSuckerButton is not null)
        {
            // The emptying company: only on the day, and only for a loo with something in it.
            _lavSuckerButton.Visible = _selectedToilet && _session.PreparedStatus == PreparationStatus.Running;
            var why = _selectedToiletId is { } lavId ? _session.LavSuckerUnavailable(lavId) : "";
            _lavSuckerButton.Disabled = why is not null;
            _lavSuckerButton.TooltipText = why ?? "Pay Dav to drive in and suck it empty. It takes about a quarter of an hour.";
        }
        var inside = _selectedToilet && _selectedToiletId is { } shownId ? ToiletOccupant(shownId) : null;
        _toiletOccupantButton!.Visible = inside is not null;
        if (inside is { } who) _toiletOccupantButton.Text = $"Select {_session.CapturePreparation()!.People.Single(p => p.AgentId == who).Name} (inside)";
        if (!_selectedToilet || _selectedToiletId is null || !_toiletViews.TryGetValue(_selectedToiletId, out var selectedView) ||
            _session.CaptureToilets().SingleOrDefault(item => item.Id == _selectedToiletId) is not { } toilet) return;
        _inspectorTitle.Text = "Portaloo • owned";
        _inspectorBody.Text = $"{(toilet.InterruptedOccupantId is { } interrupted ?
            $"Unavailable • {_session.CapturePreparation()!.People.Single(p => p.AgentId == interrupted).Name} needs a clear exit" :
            _session.ToiletBeingEmptied(toilet.Id) ? "Dav's Lav-Sucker is emptying it" :
            toilet.IsFull ? _session.LavSuckerBooked(toilet.Id) ? "OUT OF ORDER • Dav's Lav-Sucker is on the way" : "OUT OF ORDER • full, no new visits" :
            _session.LavSuckerBooked(toilet.Id) && toilet.OwnerId is null ? "Free • Dav's Lav-Sucker is on the way" : toilet.OwnerId is { } occupant ?
            $"Occupied by {_session.CapturePreparation()!.People.Single(p => p.AgentId == occupant).Name}" : "Free")}" +
            $"\nTank {toilet.UsedMillilitres / 1000m:0.0}/{toilet.CapacityMillilitres / 1000m:0.0} L • {toilet.FullPercent}% full" +
            $"\nWees {toilet.WeeCount} • poos {toilet.PooCount} • queue {toilet.Queue.Length}" +
            (_session.FaultStatus(toilet.Id) is { } fault ? "\n" + fault : "") +
            "\nSmell rises as the tank fills.";
        _highlight.Position = selectedView.Body.Position + new Vector3(0, .08f, 0);
        _highlight.Scale = new Vector3(1.3f, 1, 1.5f); _highlight.Visible = true;
    }

    private void BeginToiletPlacement()
    {
        BeginBuildPlacement(BuildServiceKind.Toilet, _selectedToiletId);
    }

}
