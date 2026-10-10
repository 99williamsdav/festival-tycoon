using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private const string BinAsset = "res://assets/environment/bin-slatted-asset-v1/lwf_bin_slatted_shell_v1.glb";
    private const string LitterAssetRoot = "res://assets/environment/litter-assets-v1/";
    private sealed record BinView(StaticBody3D Body, Node3D Part, Node3D Full, MultiMeshInstance3D Wasps);
    private readonly Dictionary<string, BinView> _binViews = [];
    private readonly Dictionary<ulong, string> _binPickOwners = [];
    private readonly Dictionary<ImmersionProduct, MultiMeshInstance3D> _litterBatches = [];
    private int _renderedLitterVersion = -1;
    private string _renderedBinLayout = "";
    private string _renderedLiftedWaste = "";
    private string? _selectedBinId;
    private Button? _binEmptyButton;
    private (string?, long) _binEmptierKey;
    private Button? _binMoveButton, _cleanupButton;
    private Material? _sharedLitterMaterial;
    private Mesh? _waspMesh;
    private double _waspRenderTick = -1;
    public int VisibleLitterInstanceCount => _litterBatches.Values.Sum(b => b.Multimesh.InstanceCount);
    private static string LitterAsset(ImmersionProduct product) => LitterAssetRoot + (product switch
    { ImmersionProduct.Beer => "lwf_litter_beer_cup_v1.glb", ImmersionProduct.SoftDrink or ImmersionProduct.Water => "lwf_litter_soft_cup_v1.glb",
        ImmersionProduct.Chips => "lwf_litter_chips_tray_v1.glb", ImmersionProduct.Pizza => "lwf_litter_pizza_plate_v1.glb", ImmersionProduct.Curry => "lwf_litter_curry_tray_v1.glb",
        _ => throw new ArgumentOutOfRangeException(nameof(product), product, "Unknown immersion product") });
    private Mesh LoadLitterMesh(string path)
    {
        var root = InstantiateAsset(path);
        var mesh = root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Single().Mesh!;
        _sharedLitterMaterial ??= mesh.SurfaceGetMaterial(0);
        root.QueueFree(); return mesh;
    }
    private MultiMeshInstance3D LitterBatch(Mesh mesh)
    {
        var node = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh },
            MaterialOverride = _sharedLitterMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(node); return node;
    }
    private static uint WasteVisualSeed(string id)
    { var hash = 2166136261u; foreach (var ch in id) hash = unchecked((hash ^ ch) * 16777619); return hash; }
    private const float FoodLitterLift = .02f;
    private static Transform3D GroundWasteTransform(WastePiece piece, Vector3 position)
    {
        var seed = WasteVisualSeed(piece.Id); var yaw = seed % 6283 / 1000f;
        var side = !piece.Product.IsFood() && seed % 3 != 0;
        var pose = side ? new Quaternion(Vector3.Right, Mathf.Pi / 2) : Quaternion.Identity;
        var basis = new Basis(new Quaternion(Vector3.Up, yaw) * pose);
        // A food tray or plate sits up on the grass blades, so its floor (the smear, the crumbs) shows rather than its rim alone.
        var support = side ? piece.Product == ImmersionProduct.Beer ? .046f : .047f : piece.Product.IsFood() ? FoodLitterLift : 0;
        return new(basis, position + new Vector3(0, support + .003f, 0));
    }
    private void SyncLitterWorld()
    {
        var bins = _session.CaptureBins();
        foreach (var stale in _binViews.Keys.Except(bins.Select(b => b.Id)).ToArray())
        {
            var view = _binViews[stale]; _binPickOwners.Remove(view.Body.GetInstanceId()); view.Body.QueueFree(); view.Wasps.QueueFree(); _binViews.Remove(stale);
        }
        foreach (var bin in bins)
        {
            if (!_binViews.TryGetValue(bin.Id, out var view))
            {
                var body = new StaticBody3D { Name = "OwnedBin-" + bin.Id, CollisionLayer = 1, CollisionMask = 0 };
                body.AddChild(InstantiateAsset(BinAsset));
                var part = InstantiateAsset("res://assets/environment/bin-slatted-asset-v1/lwf_bin_rubbish_partfull_v1.glb");
                var full = InstantiateAsset("res://assets/environment/bin-slatted-asset-v1/lwf_bin_rubbish_full_v1.glb");
                body.AddChild(part); body.AddChild(full);
                body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = .325f, Height = .95f }, Position = new(0, .475f, 0) });
                AddChild(body); _binPickOwners[body.GetInstanceId()] = bin.Id;
                _waspMesh ??= LoadLitterMesh(LitterAssetRoot + "lwf_wasp_v1.glb");
                var wasps = LitterBatch(_waspMesh); wasps.Multimesh.InstanceCount = 5;
                view = new(body, part, full, wasps); _binViews[bin.Id] = view;
            }
            view.Body.Position = ImmersionPosition(bin.Cell); view.Body.RotationDegrees = new(0, bin.QuarterTurns * 90, 0);
            view.Part.Visible = bin.Pieces is > 0 and < LitterRules.BinCapacity;
            // The part-full layer (modelled at 0.55-0.75 m) rises with the fill: just in sight below the liner rim when a few
            // pieces are in, just under the full heap's level at the brim.
            view.Part.Position = new Vector3(0, -.2f + .4f * Mathf.Clamp(bin.Pieces / (float)LitterRules.BinCapacity, 0, 1), 0);
            view.Full.Visible = bin.Pieces >= LitterRules.BinCapacity;
            view.Wasps.Visible = bin.Wasps;
            view.Wasps.Position = view.Body.Position;
        }
        var pieces = _session.CaptureLitter()?.Pieces ?? [];
        var layout = string.Join('|', bins.Select(b => $"{b.Id}:{b.Cell}"));
        var lifted = string.Join('|', _cleanupLiftedWaste.Order(StringComparer.Ordinal));
        // Carrying and cleanup claims change the piece list often; only ground and bin changes are drawn.
        if (_session.LitterVisualVersion != _renderedLitterVersion || layout != _renderedBinLayout || lifted != _renderedLiftedWaste)
        {
            _renderedLitterVersion = _session.LitterVisualVersion; _renderedBinLayout = layout;
            _renderedLiftedWaste = lifted;
            var visible = pieces.Where(p => p.Location == WasteLocation.Ground && !_cleanupLiftedWaste.Contains(p.Id)).Select(p => (Piece: p,
                Position: new Vector3(p.XMillimetres / 1000f, 0, p.ZMillimetres / 1000f))).ToList();
            foreach (var bin in bins)
            {
                var extras = pieces.Where(p => p.Location == WasteLocation.Bin && p.BinId == bin.Id).Skip(LitterRules.BinCapacity).ToArray();
                for (var i = 0; i < extras.Length; i++)
                {
                    var angle = i * 2.399963f; var radius = .48f + .05f * Mathf.Sqrt(i);
                    visible.Add((extras[i], ImmersionPosition(bin.Cell) + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius)));
                }
            }
            foreach (var product in Enum.GetValues<ImmersionProduct>())
            {
                if (!_litterBatches.TryGetValue(product, out var batch)) _litterBatches[product] = batch = LitterBatch(LoadLitterMesh(LitterAsset(product)));
                var group = visible.Where(v => v.Piece.Product == product).ToArray(); batch.Multimesh.InstanceCount = group.Length;
                // One buffer upload per batch rather than a call per instance: 12 floats, row-major basis then origin.
                var buffer = new float[group.Length * 12];
                for (var i = 0; i < group.Length; i++)
                {
                    var t = GroundWasteTransform(group[i].Piece, group[i].Position); var b = t.Basis; var o = i * 12;
                    buffer[o] = b.X.X; buffer[o + 1] = b.Y.X; buffer[o + 2] = b.Z.X; buffer[o + 3] = t.Origin.X;
                    buffer[o + 4] = b.X.Y; buffer[o + 5] = b.Y.Y; buffer[o + 6] = b.Z.Y; buffer[o + 7] = t.Origin.Y;
                    buffer[o + 8] = b.X.Z; buffer[o + 9] = b.Y.Z; buffer[o + 10] = b.Z.Z; buffer[o + 11] = t.Origin.Z;
                }
                if (group.Length > 0) batch.Multimesh.Buffer = buffer;
            }
        }
        // Fixed mesh dimensions. Hover is a function of authoritative simulation time, including pause.
        if (_waspRenderTick != _session.CurrentTick)
        {
            _waspRenderTick = _session.CurrentTick; var time = _session.CurrentTick / 80f;
            foreach (var (id, view) in _binViews.Where(p => p.Value.Wasps.Visible))
                for (var i = 0; i < 5; i++)
                {
                    var phase = time * 2.2f + i * 1.256637f + WasteVisualSeed(id) % 10;
                    var r = .17f + i * .025f;
                    view.Wasps.Multimesh.SetInstanceTransform(i, new Transform3D(new Basis(Vector3.Up, -phase),
                        new Vector3(Mathf.Cos(phase) * r, 1.05f + .08f * Mathf.Sin(time * 3 + i), Mathf.Sin(phase) * r)));
                }
        }
        RefreshBinInspector(); RefreshCleanupAction();
    }
    private void BuildLitterInspector(VBoxContainer parent)
    {
        _binMoveButton = ButtonText("Move bin", () => { if (_selectedBinId is { } id) BeginBuildPlacement(BuildServiceKind.Bin, id); });
        _binEmptyButton = ButtonText("Empty now", () =>
        {
            if (_selectedBinId is not { } id) return;
            _preparationMessage = _host.Execute(new EmptyBinCommand(id), out var error) ? "A steward is on their way to empty the bin." : error!;
            _binEmptierKey = default; RefreshPreparationHud(); RefreshBinInspector();
        });
        _binEmptyButton.Visible = false;
        parent.AddChild(_binMoveButton); _binMoveButton.Visible = false; parent.AddChild(_binEmptyButton);
        _cleanupButton = ButtonText("Clean up", () =>
        {
            if (_selectedAttendeeId is { } id)
            {
                _preparationMessage = _host.Execute(new CleanUpCommand(id.Value), out var error) ? "Steward assigned a bounded cleanup sweep." : error!;
                RefreshPreparationHud();
            }
        });
        parent.AddChild(_cleanupButton); _cleanupButton.Visible = false;
        _cleanupButton.TooltipText = "Sweep near this steward's current post, then return. Arguments and fights take priority. Bins empty only at 90% or more.";
    }
    private void SelectBin(string id) { ClearSelection(); _selectedBinId = id; RefreshBinInspector(); }
    private void RefreshBinInspector()
    {
        if (_binMoveButton is not null) _binMoveButton.Visible = _selectedBinId is not null && _session.PreparedStatus == PreparationStatus.Preparing;
        if (_binEmptyButton is not null)
        {
            // Bring the emptying forward: the nearest free steward goes now, whatever the bin's level.
            _binEmptyButton.Visible = _selectedBinId is not null && _session.PreparedStatus == PreparationStatus.Running;
            // Finding the steward pathfinds, so it's worked out once a tick at most, not every frame.
            if (_binEmptyButton.Visible && (_selectedBinId, _session.CurrentTick) != _binEmptierKey)
            {
                _binEmptierKey = (_selectedBinId, _session.CurrentTick);
                var ready = _session.BinEmptier(_selectedBinId!, out var why) is not null;
                _binEmptyButton.Disabled = !ready;
                _binEmptyButton.TooltipText = ready ? "Send the nearest free steward to empty this bin now." : why;
            }
        }
        if (_selectedBinId is not { } id || _session.CaptureBins().SingleOrDefault(b => b.Id == id) is not { } bin || !_binViews.TryGetValue(id, out var view)) return;
        RefreshContextPanelVisibility();
        _inspectorTitle.Text = "Litter bin";
        _inspectorBody.Text = $"{bin.FullPercent}% full • {bin.Pieces}/{bin.Capacity} pieces\nExtra items: {bin.ExtraPieces}\n" +
            (bin.Wasps ? "Full bin attracting wasps.\n" : "Open top general waste.\n") +
            "Stewards empty during cleanup at 90% or more.";
        _highlight.Position = view.Body.Position + new Vector3(0, .08f, 0); _highlight.Scale = new(.55f, 1, .55f); _highlight.Visible = true;
    }
    private void RefreshCleanupAction()
    {
        if (_cleanupButton is null) return;
        var id = _selectedAttendeeId?.Value;
        _cleanupButton.Visible = id is not null && _session.GetStewardResponses().Any(s => s.WorkerId == id) && _session.PreparedStatus == PreparationStatus.Running;
        if (id is { } worker) _cleanupButton.Disabled = _session.ValidateCommand(new(new(_session.NextSubmissionSequence + 1), _session.CampaignId,
            _session.Phase, _session.CurrentTick, _session.NextSubmissionSequence, null, new CleanUpCommand(worker))) is not null;
    }
}
