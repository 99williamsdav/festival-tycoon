using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private VBoxContainer? _immersionControls;
    private Label? _immersionSummary;
    private Button? _immersionStockButton;
    private Button? _immersionMoveButton;
    private readonly Dictionary<string, StaticBody3D> _immersionVendors = [];
    private readonly Dictionary<ulong, string> _immersionVendorPicks = [];
    private string? _selectedImmersionVendor;
    private string? _placingImmersionVendor;
    private int _immersionQuarterTurns;
    private GridCell? _immersionCandidate;
    private string? _immersionPlacementIssue;
    private Node3D? _immersionPreview;
    private Label3D? _immersionPreviewLabel;
    private int _immersionPreviewQuarterTurns = -1;
    private MeshInstance3D? _immersionFootprintPreview;
    private readonly List<MeshInstance3D> _immersionQueuePreview = [];
    private VBoxContainer? _immersionNeedSection;
    private ProgressBar? _immersionHungerBar;
    private ProgressBar? _immersionIntoxBar;
    private Label? _immersionNeedLabel;
    private readonly Dictionary<ulong, Label3D> _immersionWarningLabels = [];
    private readonly Dictionary<ulong, long> _immersionLastRemark = [];
    private Label3D? _immersionRemark;
    private ulong? _immersionRemarkPerson;
    private long _immersionRemarkUntil;
    private long _immersionLastGlobalRemark = -640;

    private void ResetImmersionCuePresentation()
    {
        foreach (var label in _immersionWarningLabels.Values) { label.Visible = false; label.QueueFree(); }
        _immersionWarningLabels.Clear(); _immersionLastRemark.Clear();
        if (_immersionRemark is not null) { _immersionRemark.Visible = false; _immersionRemark.QueueFree(); }
        _immersionRemark = null; _immersionRemarkPerson = null; _immersionRemarkUntil = 0;
        _immersionLastGlobalRemark = _session.CurrentTick;
        // A load does not replay previous routine remarks.
        foreach (var person in _session.CaptureImmersion()?.People ?? []) _immersionLastRemark[person.AgentId] = _session.CurrentTick;
    }
    private void AdvanceImmersionCuePresentation()
    {
        foreach (var label in _immersionWarningLabels.Values) label.Visible = false;
        if (_immersionRemark is not null) _immersionRemark.Visible = false;
        if (_session.CaptureImmersion() is not { } state) return;
        var onSite = (_session.CapturePreparation()?.People ?? []).Where(p => p.Admitted && !p.Departed).Select(p => p.AgentId).ToHashSet();
        foreach (var person in state.People.Where(p => p.Intoxication >= 7500 && onSite.Contains(p.AgentId)))
        {
            if (!_attendeeVisuals.TryGetValue(new EntityId(person.AgentId), out var body)) continue;
            if (_medicalCueLabels.TryGetValue(person.AgentId, out var medical) && medical.Visible ||
                _disorderCueLabels.TryGetValue(person.AgentId, out var disorder) && disorder.Visible) continue;
            if (!_immersionWarningLabels.TryGetValue(person.AgentId, out var label))
            {
                label = new Label3D { Text = "! Too much beer • need care!", FontSize = 38, PixelSize = .009f,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = new Color("ffdb73"), OutlineSize = 14 };
                AddChild(label); _immersionWarningLabels.Add(person.AgentId, label);
            }
            label.Position = body.Position + new Vector3(0, 2.7f, 0); label.Visible = true;
        }
        // Existing medical/disorder routine or urgent speech always takes priority.
        if (_medicalCueLabels.Values.Any(l => l.Visible) || _disorderCueLabels.Values.Any(l => l.Visible) || _immersionWarningLabels.Values.Any(l => l.Visible)) return;
        if (_immersionRemarkPerson is { } active && _session.CurrentTick < _immersionRemarkUntil &&
            _attendeeVisuals.TryGetValue(new EntityId(active), out var activeBody))
        { _immersionRemark!.Position = activeBody.Position + new Vector3(0, 2.35f, 0); _immersionRemark.Visible = true; return; }
        if (_session.CurrentTick - _immersionLastGlobalRemark < 640) return;
        var candidate = state.People.FirstOrDefault(p => p.Intoxication is >= 2500 and < 7500 && _session.ImmersionHandsAvailable(p.AgentId) &&
            (!_immersionLastRemark.TryGetValue(p.AgentId, out var last) || _session.CurrentTick - last >= 3200));
        if (candidate is null || !_attendeeVisuals.TryGetValue(new EntityId(candidate.AgentId), out var visual)) return;
        if (_immersionRemark is null)
        {
            _immersionRemark = new Label3D { FontSize = 38, PixelSize = .009f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 14 };
            AddChild(_immersionRemark);
        }
        _immersionRemark.Text = candidate.Intoxication >= 5000 ? "Feeling wobbly • time for a rest" : "Feeling a bit tipsy";
        _immersionRemark.Position = visual.Position + new Vector3(0, 2.35f, 0); _immersionRemark.Visible = true;
        _immersionRemarkPerson = candidate.AgentId; _immersionRemarkUntil = _session.CurrentTick + 240;
        _immersionLastGlobalRemark = _session.CurrentTick; _immersionLastRemark[candidate.AgentId] = _session.CurrentTick;
    }

    private void BuildImmersionNeedBars(VBoxContainer parent)
    {
        _immersionNeedSection = new VBoxContainer { Visible = false }; parent.AddChild(_immersionNeedSection);
        _immersionNeedLabel = LabelText("", 13, new Color("29352c")); _immersionNeedSection.AddChild(_immersionNeedLabel);
        foreach (var title in new[] { "HUNGER", "INTOXICATION • FICTIONAL EXPOSURE" })
        {
            _immersionNeedSection.AddChild(LabelText(title, 12, new Color("29352c")));
            var bar = new ProgressBar { MinValue = 0, MaxValue = 100, CustomMinimumSize = new Vector2(340, 15), ShowPercentage = true };
            _immersionNeedSection.AddChild(bar);
            if (title == "HUNGER") _immersionHungerBar = bar; else _immersionIntoxBar = bar;
        }
    }
    private void RefreshImmersionNeedBars(ulong? id)
    {
        if (_immersionNeedSection is null) return;
        var person = id is { } selected ? _session.CaptureImmersion()?.People.SingleOrDefault(p => p.AgentId == selected) : null;
        _immersionNeedSection.Visible = person is not null;
        if (person is null) return;
        _immersionHungerBar!.Value = person.Hunger / 100d; _immersionIntoxBar!.Value = person.Intoxication / 100d;
        _immersionIntoxBar.Modulate = new Color(person.Intoxication >= 7500 ? "ff7566" : person.Intoxication >= 5000 ? "e8b45b" : "a6c887");
        _immersionNeedLabel!.Text = person.Intoxication >= 7500 ? "HEAVY INTOXICATION • CARE AVAILABLE" : person.Intoxication >= 5000 ? "IMPAIRED • COORDINATION REDUCED" : person.Intoxication >= 2500 ? "TIPSY" : "ADULT FOOD & DRINK NEEDS";
    }

    private static Vector3 ImmersionPosition(GridCell cell)
    {
        var centre = TraversalGrid.CellCentre(cell);
        return new(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f);
    }
    private static string ImmersionProductKey(ImmersionProduct product) => product switch
    { ImmersionProduct.Chips => "chips", ImmersionProduct.SoftDrink => "soft", _ => "beer" };
    private static string ImmersionProductName(ImmersionProduct product) => product switch
    { ImmersionProduct.Chips => "Chips", ImmersionProduct.SoftDrink => "Soft drink", _ => "Beer" };

    private void BuildImmersionControls(VBoxContainer parent)
    {
        _immersionControls = new VBoxContainer(); parent.AddChild(_immersionControls);
        _immersionControls.AddChild(LabelText("FOOD & DRINK • ADULT FESTIVAL", 14, new Color("29352c")));
        _immersionSummary = LabelText("", 13, new Color("29352c"));
        _immersionSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _immersionControls.AddChild(_immersionSummary);
        _immersionStockButton = ButtonText("BUY STARTER STOCK • £96", () => CommitEquipmentAction(new PurchaseImmersionStarterStockCommand()));
        _immersionStockButton.TooltipText = "40 chips (£1 each), 40 soft drinks (60p each), 32 beers (£1 each). Paid from festival funds once before opening; no in-day refill.";
        _immersionControls.AddChild(_immersionStockButton);
        RefreshImmersionControls();
    }

    private void BuildImmersionVendorInspector(VBoxContainer parent)
    {
        _immersionMoveButton = ButtonText("Move", () =>
        {
            if (_selectedImmersionVendor is { } id) BeginImmersionPlacement(id);
        });
        _immersionMoveButton.Visible = false;
        _immersionMoveButton.TooltipText = "Choose a new grass site before opening. Comma/period rotate; right-click or Esc cancels.";
        parent.AddChild(_immersionMoveButton);
    }

    private int ImmersionHeavyOnSiteCount()
    {
        if (_session.CaptureImmersion() is not { } state) return 0;
        var onSite = (_session.CapturePreparation()?.People ?? []).Where(p => p.Admitted && !p.Departed).Select(p => p.AgentId).ToHashSet();
        return state.People.Count(p => p.Intoxication >= 7500 && onSite.Contains(p.AgentId));
    }

    private void RefreshImmersionControls()
    {
        if (_immersionControls is null) return;
        var state = _session.CaptureImmersion(); _immersionControls.Visible = state is not null;
        if (state is null) return;
        var preparing = _session.PreparedStatus == PreparationStatus.Preparing;
        _immersionStockButton!.Visible = preparing;
        _immersionStockButton.Disabled = _session.ValidateCommand(CampaignEnvelope(new PurchaseImmersionStarterStockCommand())) is not null;
        _immersionStockButton.Text = state.StockPurchased ? "STARTER STOCK PURCHASED • £96" : "BUY STARTER STOCK • £96";
        _immersionSummary!.Text = $"Chips £3 • soft £2 • beer £3\nStock {state.ChipsStock}/{state.SoftStock}/{state.BeerStock} • sales {state.Purchases.Length}\n" +
            "Free water remains available. Personal spending budgets vary; staff do not buy beer.\n" +
            string.Join("\n", state.Vendors.Select(v => $"{(v.Id == "food" ? "Food van" : "Drinks stall")}: queue {v.Queue.Length} • {(v.OwnerId is null ? "ready" : "serving")}"));
        if (_session.PreparedStatus == PreparationStatus.Departing) _immersionSummary.Text += "\nCOUNTERS CLOSED • on-site alcohol risk and medic response continue until physical exit.";
        if (ImmersionHeavyOnSiteCount() > 0) _immersionSummary.Text = "! HEAVY INTOXICATION • select affected people for medic care\n" + _immersionSummary.Text;
        SyncImmersionWorld();
        RefreshImmersionVendorInspector();
    }

    private void SyncImmersionWorld()
    {
        var state = _session.CaptureImmersion();
        if (state is null)
        {
            foreach (var visual in _immersionVendors.Values) { visual.Visible = false; visual.QueueFree(); }
            _immersionVendors.Clear(); _immersionVendorPicks.Clear(); ResetImmersionHeldVisuals();
            return;
        }
        foreach (var vendor in state.Vendors)
        {
            if (!_immersionVendors.TryGetValue(vendor.Id, out var body))
            {
                body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
                body.AddChild(InstantiateImmersionVendor(vendor.Id == "food"));
                var size = vendor.Id == "food" ? new Vector3(6, 2.8f, 3) : new Vector3(3.5f, 3.1f, 2.5f);
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(vendor.Id == "food" ? -.5f : 0, size.Y / 2, 0) });
                body.AddChild(new Label3D { Name = "VendorCategoryLabel", Text = vendor.Id == "food" ? "FOOD" : "DRINK", Position = new Vector3(0, 3.4f, 0),
                    FontSize = 45, PixelSize = .009f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
                AddChild(body); _immersionVendors.Add(vendor.Id, body); _immersionVendorPicks.Add(body.GetInstanceId(), vendor.Id);
            }
            body.Position = ImmersionPosition(vendor.Cell); body.RotationDegrees = new Vector3(0, 90 * vendor.QuarterTurns, 0);
        }
    }

    private void BeginImmersionPlacement(string id)
    {
        if (_session.PreparedStatus != PreparationStatus.Preparing || _session.CaptureImmersion() is not { } state) return;
        CancelWaterPlacement(); CancelImmersionPlacement(); ClearSelection();
        _placingImmersionVendor = id; _immersionQuarterTurns = state.Vendors.Single(v => v.Id == id).QuarterTurns;
        _immersionPreview = InstantiateImmersionVendor(id == "food"); AddChild(_immersionPreview);
        _immersionFootprintPreview = new MeshInstance3D { MaterialOverride = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        AddChild(_immersionFootprintPreview);
        // Only service access is needed now; the physical line grows on demand.
        for (var index = 0; index < 1; index++)
        {
            var marker = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .22f, BottomRadius = .22f, Height = .025f },
                MaterialOverride = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
            AddChild(marker); _immersionQueuePreview.Add(marker);
        }
        _immersionPreviewLabel = new Label3D { FontSize = 34, PixelSize = .009f, Position = new Vector3(0, 3.7f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
        _immersionPreview.AddChild(_immersionPreviewLabel);
        _preparationMessage = "Move vendor: click valid grass; comma/period rotate; right-click or Esc cancels.";
        RefreshPreparationHud(); UpdateImmersionPlacementPreview(GetViewport().GetMousePosition());
    }
    private void CancelImmersionPlacement()
    {
        _placingImmersionVendor = null; _immersionCandidate = null; _immersionPlacementIssue = null;
        _immersionPreviewQuarterTurns = -1;
        if (_immersionPreview is not null) { _immersionPreview.Visible = false; _immersionPreview.QueueFree(); }
        _immersionPreview = null; _immersionPreviewLabel = null;
        if (_immersionFootprintPreview is not null) { _immersionFootprintPreview.Visible = false; _immersionFootprintPreview.QueueFree(); _immersionFootprintPreview = null; }
        foreach (var marker in _immersionQueuePreview) { marker.Visible = false; marker.QueueFree(); } _immersionQueuePreview.Clear();
    }
    private void UpdateImmersionPlacementPreview(Vector2 screen)
    {
        if (_placingImmersionVendor is null || _immersionPreview is null) return;
        if (screen.X < 435 || screen.X > GetViewport().GetVisibleRect().Size.X - 435 || screen.Y < 110)
        { _immersionCandidate = null; _immersionPreview.Visible = false; _immersionFootprintPreview!.Visible = false; foreach (var marker in _immersionQueuePreview) marker.Visible = false; return; }
        var ray = _camera.ProjectRayNormal(screen); var origin = _camera.ProjectRayOrigin(screen);
        if (Mathf.Abs(ray.Y) < .001f || -origin.Y / ray.Y <= 0) { _immersionCandidate = null; _immersionPreview.Visible = false; _immersionFootprintPreview!.Visible = false; foreach (var marker in _immersionQueuePreview) marker.Visible = false; return; }
        var world = origin + ray * (-origin.Y / ray.Y);
        var cell = TraversalGrid.WorldToCell(Mathf.RoundToInt(world.X * 1000), Mathf.RoundToInt(world.Z * 1000));
        if (_immersionCandidate == cell && _immersionPreviewQuarterTurns == _immersionQuarterTurns) return;
        _immersionCandidate = cell;
        _immersionPreviewQuarterTurns = _immersionQuarterTurns;
        _immersionPlacementIssue = _session.ValidateCommand(CampaignEnvelope(new PlaceImmersionVendorCommand(_placingImmersionVendor, cell, _immersionQuarterTurns)))?.Message;
        _immersionPreview.Position = ImmersionPosition(cell); _immersionPreview.RotationDegrees = new Vector3(0, 90 * _immersionQuarterTurns, 0); _immersionPreview.Visible = true;
        _immersionPreviewLabel!.Text = _immersionPlacementIssue is null ? "VALID • CLICK TO MOVE" : "INVALID • " + _immersionPlacementIssue;
        _immersionPreviewLabel.Modulate = new Color(_immersionPlacementIssue is null ? "67db76" : "ff7566");
        var proposed = new ImmersionVendor(_placingImmersionVendor, cell, _immersionQuarterTurns, []);
        var footprint = GameSession.ImmersionFootprint(proposed);
        var min = ImmersionPosition(new GridCell(footprint.Min(c => c.X), footprint.Min(c => c.Z)));
        var max = ImmersionPosition(new GridCell(footprint.Max(c => c.X), footprint.Max(c => c.Z)));
        var color = _immersionPlacementIssue is null ? new Color(.25f, .78f, .38f, .4f) : new Color(.9f, .24f, .18f, .4f);
        _immersionFootprintPreview!.Mesh = new BoxMesh { Size = new Vector3(max.X - min.X + .5f, .035f, max.Z - min.Z + .5f) };
        _immersionFootprintPreview.Position = (min + max) / 2 + new Vector3(0, .06f, 0); _immersionFootprintPreview.Visible = true;
        ((StandardMaterial3D)_immersionFootprintPreview.MaterialOverride!).AlbedoColor = color;
        for (var index = 0; index < _immersionQueuePreview.Count; index++)
        {
            _immersionQueuePreview[index].Position = ImmersionPosition(GameSession.ImmersionQueueCell(proposed, index)) + new Vector3(0, .08f, 0);
            ((StandardMaterial3D)_immersionQueuePreview[index].MaterialOverride!).AlbedoColor = color;
            _immersionQueuePreview[index].Visible = true;
        }
    }
    private void CommitImmersionPlacement(Vector2 screen)
    {
        UpdateImmersionPlacementPreview(screen);
        if (_placingImmersionVendor is not { } id || _immersionCandidate is not { } cell || _immersionPlacementIssue is not null) return;
        CommitEquipmentAction(new PlaceImmersionVendorCommand(id, cell, _immersionQuarterTurns));
        if (_session.CaptureImmersion()?.Vendors.Any(v => v.Id == id && v.Cell == cell && v.QuarterTurns == _immersionQuarterTurns) == true) CancelImmersionPlacement();
    }
    private void SelectImmersionVendor(string id)
    {
        ClearSelection(); _selectedImmersionVendor = id; RefreshImmersionVendorInspector();
    }
    private void RefreshImmersionVendorInspector()
    {
        RefreshContextPanelVisibility();
        if (_immersionMoveButton is not null)
            _immersionMoveButton.Visible = _selectedImmersionVendor is not null &&
                _session.PreparedStatus == PreparationStatus.Preparing && _session.CaptureImmersion() is not null;
        if (_selectedImmersionVendor is not { } id || _session.CaptureImmersion() is not { } state || !_immersionVendors.TryGetValue(id, out var body)) return;
        var vendor = state.Vendors.Single(v => v.Id == id);
        _inspectorTitle.Text = id == "food" ? "Food van • chips" : "Drinks stall • soft drinks & beer";
        _inspectorBody.Text = $"Physical FIFO • {vendor.Queue.Length} queued\n" +
            (vendor.OwnerId is { } owner ? $"Serving {_session.CapturePreparation()!.People.Single(p => p.AgentId == owner).Name} • {vendor.ServiceTicks / 80m:0.0}s remaining\n" : "Counter ready\n") +
            $"Facing {vendor.QuarterTurns * 90}° • preparation placement only\n" +
            (id == "food" ? $"Chips £3 • stock {state.ChipsStock}" : $"Soft £2 • stock {state.SoftStock}\nBeer £3 • stock {state.BeerStock}\nAbstainers and staff choose nonalcoholic options; heavy intoxication means no further beer.");
        _highlight.Position = body.Position + new Vector3(0, .08f, 0); _highlight.Scale = new Vector3(id == "food" ? 3.5f : 2, 1, id == "food" ? 3.5f : 2); _highlight.Visible = true;
    }
    private string ImmersionPersonInspectorText(ulong id)
    {
        RefreshImmersionNeedBars(id);
        if (_session.CaptureImmersion()?.People.SingleOrDefault(p => p.AgentId == id) is not { } person) return "";
        var wallet = _session.CaptureSnapshot().Wallets.Single(w => w.OwnerId.Value == id).CashPennies;
        return $"\nFOOD & DRINK • adult\nBudget {FestivalCurrency.Format(wallet)} remaining / {FestivalCurrency.Format(person.OpeningBudgetPennies)} opening\n" +
            $"Hunger {person.Hunger / 100m:0}% • intoxication {person.Intoxication / 100m:0}%\n" +
            (person.Abstains ? "Abstains from beer\n" : "Individual food/drink preferences\n") +
            (person.Held is { } held ? $"Holding {ImmersionProductName(held.Product)} • {held.ConsumedTicks / 80m:0.0}/{GameSession.ImmersionConsumeTicks(held.Product) / 80}s consumed • {(_session.ImmersionHandsAvailable(id) ? "consuming away from counter" : "retained; consumption paused")}\n" : "Hands empty\n") +
            (person.PendingDose > 0 ? "Previously ingested dose still absorbing\n" : "") +
            (person.Intoxication >= 7500 ? "HEAVY INTOXICATION • needs care; no further beer\n" : person.Intoxication >= 5000 ? "IMPAIRED • coordination reduced\n" : person.Intoxication >= 2500 ? "TIPSY\n" : "") +
            (person.Intoxication >= 8500 ? $"Continuously high exposure {person.SevereTicks / 80m:0.0}/20s • collapse risk\n" : "") +
            "Water addresses thirst, not sobriety; food slows uptake; time permits recovery.\n";
    }
    private void AdvanceImmersionPresentation(double delta)
    {
        if (_session.CaptureImmersion() is not { } state) { ResetImmersionHeldVisuals(); return; }
        foreach (var person in state.People)
            if (_attendeeVisuals.TryGetValue(new EntityId(person.AgentId), out var body))
            {
                var hands = _session.ImmersionHandsAvailable(person.AgentId);
                SetImmersionHeldVisual(new(person.AgentId), body, person.Held is { } held ? ImmersionProductKey(held.Product) : null, hands, person.Intoxication, delta);
                if (hands && person.Intoxication >= 5000)
                    body.Rotation = new Vector3(body.Rotation.X, body.Rotation.Y, Mathf.Sin((float)Time.GetTicksMsec() / 350f + person.AgentId) * .035f);
                else if (Mathf.Abs(body.Rotation.X) < .1f) body.Rotation = new Vector3(body.Rotation.X, body.Rotation.Y, 0);
            }
        RefreshImmersionVendorInspector();
    }
}
