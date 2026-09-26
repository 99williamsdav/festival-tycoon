using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private string? _organicQueueCaptureDirectory;
    private int _organicQueueStep;
    private int _organicQueueFrame;
    private int _organicQueueRoute;
    private long _organicQueueRouteStarted;
    private GameSession? _organicQueueBaseline;
    private GameSession? _organicQueueBest;
    private int _organicQueueMaximum;
    private bool _organicQueueShortCaptured;
    private Label? _organicQueueLabel;
    private string _organicQueuePausedHash = "";
    private string _organicQueuePausedCells = "";
    private readonly int[] _organicQueueMaxima = new int[5];
    private Vector3 _organicQueueOverviewFocus;
    private float _organicQueueOverviewSize;
    private int _organicQueueBestSettled;
    private string _organicQueueWaterPointId = "water.main";
    private GameSession? _organicQueueBestFormed;
    private int _organicQueueBestFormedCount;
    private int _organicQueueShownLongCount;
    private int _organicQueueShownLongSettled;
    private ulong[] _organicQueueShortIds = [];
    private bool _organicQueueLongDemand;
    private int _organicQueueShownShortCount;
    private string OrganicQueueKind => _organicQueueRoute == 0 ? "water" : _organicQueueRoute <= 2 ? "food" : "drinks";
    private bool OrganicQueueBefore => _organicQueueRoute is 1 or 3;
    private string OrganicQueuePrefix => OrganicQueueKind + (OrganicQueueBefore ? "-before-legacy-reference" : "-after-varied");

    private void OrganicQueueSetNeeds(bool warming = false)
    {
        var medical = _session.CaptureMedical()!;
        var ids = warming ? _session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).Select(p => p.AgentId).ToHashSet()
            : _organicQueueShortIds.ToHashSet();
        var thirst = warming ? 0 : OrganicQueueKind == "water" ? 10000 : OrganicQueueKind == "drinks" ? 6000 : 1000;
        typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
            medical with { DevelopmentInterventionFixturesEnabled = true, Needs = medical.Needs.Select(n => n with
                { Thirst = ids.Contains(n.AgentId) ? thirst : 0, HeatExposure = 0, LastDecisionTick = _session.CurrentTick }).ToArray() });
        var immersion = _session.CaptureImmersion()!;
        typeof(GameSession).GetField("_immersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
            immersion with { People = immersion.People.Select(p => p with
                { Hunger = !warming && OrganicQueueKind == "food" && ids.Contains(p.AgentId) ? 10000 : 0,
                    LastDecisionTick = !warming && ids.Contains(p.AgentId) ? _session.CurrentTick - 800 : _session.CurrentTick }).ToArray() });
    }

    private void OrganicQueueAdvance(int ticks)
    {
        if (!_organicQueueLongDemand)
        {
            // Until the genuinely small line is photographed, the remaining
            // adults continue normal movement with only cosmetic-fixture demand
            // decisions deferred. No existing queue owner/member is changed.
            var state = _session.CaptureImmersion()!;
            typeof(GameSession).GetField("_immersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                state with { People = state.People.Select(p => _organicQueueShortIds.Contains(p.AgentId) ? p : p with { LastDecisionTick = _session.CurrentTick }).ToArray() });
            var needs = _session.CaptureMedical()!;
            typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                needs with { Needs = needs.Needs.Select(n => _organicQueueShortIds.Contains(n.AgentId) ? n : n with { LastDecisionTick = _session.CurrentTick }).ToArray() });
        }
        if (OrganicQueueKind != "water")
        {
            // Labelled paid-demand fixture keeps nonurgent medical decision
            // cooldown current. Urgent water/heat thresholds still bypass it.
            var medical = _session.CaptureMedical()!;
            typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                medical with { Needs = medical.Needs.Select(n => n with { LastDecisionTick = _session.CurrentTick }).ToArray() });
        }
        StaffCaptureAdvance(ticks);
        if (_session.PreparedStatus == PreparationStatus.Failed || _session.CaptureLifecycleSnapshot()!.Casualties.Count != 0)
            throw new InvalidOperationException("Unrelated fatal chain invalidated the bounded queue fixture.");
    }

    private void OrganicQueueExtendDemand()
    {
        var remaining = _session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed && !_organicQueueShortIds.Contains(p.AgentId)).Select(p => p.AgentId).ToHashSet();
        var medical = _session.CaptureMedical()!;
        var immersion = _session.CaptureImmersion()!;
        var incumbentNeeds = medical.Needs.Where(n => !remaining.Contains(n.AgentId)).ToArray();
        var incumbentPeople = immersion.People.Where(p => !remaining.Contains(p.AgentId)).ToArray();
        var physicalPositions = _session.CaptureObservation().NavigationAgents.Select(n => (n.Id, n.XMillimetres, n.ZMillimetres)).ToArray();
        var thirst = OrganicQueueKind == "water" ? 10000 : OrganicQueueKind == "drinks" ? 6000 : 1000;
        typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
            medical with { Needs = medical.Needs.Select(n => remaining.Contains(n.AgentId) ? n with { Thirst = thirst, HeatExposure = 0, LastDecisionTick = _session.CurrentTick } : n).ToArray() });
        typeof(GameSession).GetField("_immersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
            immersion with { People = immersion.People.Select(p => remaining.Contains(p.AgentId) ? p with { Hunger = OrganicQueueKind == "food" ? 10000 : 0, LastDecisionTick = _session.CurrentTick - 800 } : p).ToArray() });
        if (OrganicQueueKind == "water")
            foreach (var id in remaining.OrderBy(id => id)) StaffCaptureSend(new DevelopmentMedicalFixtureCommand(id, MedicalAction.GuideToWater));
        var afterMedical = _session.CaptureMedical()!; var afterImmersion = _session.CaptureImmersion()!;
        if (!incumbentNeeds.SequenceEqual(afterMedical.Needs.Where(n => !remaining.Contains(n.AgentId))) ||
            !incumbentPeople.SequenceEqual(afterImmersion.People.Where(p => !remaining.Contains(p.AgentId))) ||
            medical.WaterOwnerId != afterMedical.WaterOwnerId || medical.WaterDrinkTicks != afterMedical.WaterDrinkTicks ||
            !medical.WaterQueue.SequenceEqual(afterMedical.WaterQueue) || !medical.WaterOverflow.SequenceEqual(afterMedical.WaterOverflow) ||
            !physicalPositions.SequenceEqual(_session.CaptureObservation().NavigationAgents.Select(n => (n.Id, n.XMillimetres, n.ZMillimetres))) ||
            immersion.Vendors.Any(v => afterImmersion.Vendors.Single(a => a.Id == v.Id) is var a &&
                (a.OwnerId != v.OwnerId || a.ServiceTicks != v.ServiceTicks || !a.Queue.SequenceEqual(v.Queue))))
            throw new InvalidOperationException("Extending labelled demand changed an existing queue incumbent or service timer.");
        _organicQueueLongDemand = true; _organicQueueRouteStarted = _session.CurrentTick;
        GD.Print($"ORGANIC_QUEUE_DEMAND_EXTENDED route={OrganicQueuePrefix} short_cohort={string.Join(',', _organicQueueShortIds)} additional={remaining.Count} incumbents_unchanged=True service_timers_unchanged=True positions_injected=False");
    }

    private void OrganicQueueRestore(GameSession state)
    {
        var saved = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-preparation", new(state, _saveCompatibility, "labelled-queue-reference", DateTimeOffset.UtcNow));
        if (!saved.IsSuccess) throw new InvalidOperationException(saved.Error);
        PreparationLoad();
        if (_session.CaptureSnapshot().AuthoritativeHash != state.CaptureSnapshot().AuthoritativeHash)
            throw new InvalidOperationException("Queue fixture real-file restore changed state.");
    }

    private int OrganicQueueCount() => OrganicQueueKind == "water"
        ? _session.CaptureWaterPoints().Single(p => p.Id == _organicQueueWaterPointId) is { } point ? point.Queue.Length + point.Overflow.Length : 0
        : _session.CaptureImmersion()!.Vendors.Single(v => v.Id == OrganicQueueKind).Queue.Length;

    private ulong[] OrganicQueueMembers() => OrganicQueueKind == "water"
        ? _session.CaptureWaterPoints().Single(p => p.Id == _organicQueueWaterPointId).Queue.Concat(_session.CaptureWaterPoints().Single(p => p.Id == _organicQueueWaterPointId).Overflow).ToArray()
        : _session.CaptureImmersion()!.Vendors.Single(v => v.Id == OrganicQueueKind).Queue;

    private (int Settled, string Distances) OrganicQueueSettled()
    {
        var ids = OrganicQueueMembers();
        var cells = OrganicQueueKind == "water" ? _session.CaptureWaterQueueCells(_organicQueueWaterPointId) : _session.CaptureImmersionQueueCells(OrganicQueueKind);
        var navigation = _session.CaptureObservation().NavigationAgents;
        var settled = 0;
        var distances = ids.Select((id, index) =>
        {
            var nav = navigation.Single(n => n.Id.Value == id); var centre = TraversalGrid.CellCentre(cells[index]);
            var distance = Math.Sqrt(Math.Pow(nav.XMillimetres - centre.XMillimetres, 2) + Math.Pow(nav.ZMillimetres - centre.ZMillimetres, 2));
            if (nav.Action == AgentNavigationAction.Arrived && distance <= 125) settled++;
            return $"{id}:{distance:0}mm:{nav.Action}";
        }).ToArray();
        return (settled, string.Join(';', distances));
    }

    private string OrganicQueueCells() => OrganicQueueKind == "water"
        ? string.Join(';', _session.CaptureWaterQueueCells(_organicQueueWaterPointId).Select(c => $"{c.X},{c.Z}"))
        : string.Join(';', _session.CaptureImmersionQueueCells(OrganicQueueKind).Select(c => $"{c.X},{c.Z}"));

    private void OrganicQueueFocus(bool overview)
    {
        if (overview) { _focus = _organicQueueOverviewFocus; _camera.Size = _organicQueueOverviewSize; }
        else
        {
            var cell = OrganicQueueKind == "water" ? _session.CaptureWaterPoints().Single(p => p.Id == _organicQueueWaterPointId).Cell : _session.CaptureImmersion()!.Vendors.Single(v => v.Id == OrganicQueueKind).Cell;
            var turns = OrganicQueueKind == "water" ? _session.CaptureWaterPoints().Single(p => p.Id == _organicQueueWaterPointId).QuarterTurns : _session.CaptureImmersion()!.Vendors.Single(v => v.Id == OrganicQueueKind).QuarterTurns;
            var offset = GameSession.RotateWaterOffset(new GridCell(0, 14), turns);
            _focus = ImmersionPosition(cell) + new Vector3(offset.X * .5f, 0, offset.Z * .5f); _camera.Size = 26;
        }
        ApplyCamera();
        var settled = OrganicQueueSettled().Settled; var count = OrganicQueueCount();
        _organicQueueLabel!.Text = $"LABELLED NEED/COOLDOWN DEMO • {OrganicQueuePrefix}\nClaimed {count} • settled {settled} • walking {count - settled} • no body teleport • camera {_camera.Size:0} m";
        ClearSelection();
    }

    private void OrganicQueueImage(string suffix)
    {
        var physical = OrganicQueueSettled();
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_organicQueueCaptureDirectory!, OrganicQueuePrefix + "-" + suffix + ".png"));
        var navigation = _session.CaptureObservation().NavigationAgents;
        var ids = OrganicQueueMembers();
        GD.Print($"ORGANIC_QUEUE_IMAGE route={OrganicQueuePrefix} kind={suffix} tick={_session.CurrentTick} actual_members={ids.Length} settled={physical.Settled} in_transit={ids.Length - physical.Settled} distances={physical.Distances} fifo={string.Join(',', ids)} cells={OrganicQueueCells()} physical={string.Join(';', ids.Select(id => navigation.Single(n => n.Id.Value == id)).Select(n => $"{n.Id.Value}:{n.XMillimetres},{n.ZMillimetres}:{n.Action}"))}");
    }

    private void ProcessOrganicQueueCapture()
    {
        if (_organicQueueCaptureDirectory is null || ++_organicQueueFrame < 4) return;
        try
        {
            switch (_organicQueueStep)
            {
                case 0:
                    _organicQueueOverviewFocus = _focus; _organicQueueOverviewSize = _camera.Size;
                    var layer = new CanvasLayer { Layer = 19 }; AddChild(layer);
                    _organicQueueLabel = LabelText("LABELLED QUEUE DEMONSTRATION • hunger/thirst setup", 17, new Color("fff0bd"));
                    _organicQueueLabel.Position = new Vector2(450, 125); layer.AddChild(_organicQueueLabel);
                    _programmeDraft = ["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"];
                    RefreshProgrammeControls(); _programmeBook!.EmitSignal(Button.SignalName.Pressed);
                    foreach (var offer in new[] { "staff.steward", "equipment.buy" }) _offerButtons[offer].EmitSignal(Button.SignalName.Pressed);
                    _immersionStockButton!.EmitSignal(Button.SignalName.Pressed);
                    OrganicQueueSetNeeds(true); PreparationStart();
                    for (var tick = 0; tick < 1500; tick += 40)
                    {
                        var state = _session.CaptureImmersion()!;
                        typeof(GameSession).GetField("_immersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                            state with { People = state.People.Select(p => p with { LastDecisionTick = _session.CurrentTick }).ToArray() });
                        StaffCaptureAdvance(Math.Min(40, 1500 - tick));
                    }
                    _organicQueueBaseline = GameSession.Restore(_session.CapturePersistenceSnapshot()).Session ?? throw new InvalidOperationException("Queue cohort baseline did not restore.");
                    _organicQueueStep = 1; return;
                case 1:
                    OrganicQueueRestore(_organicQueueBaseline!);
                    _organicQueueLongDemand = false;
                    _organicQueueShortIds = _session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).OrderBy(p => p.AgentId).Take(5).Select(p => p.AgentId).ToArray();
                    OrganicQueueSetNeeds();
                    if (OrganicQueueBefore)
                    {
                        var state = _session.CaptureImmersion()!;
                        typeof(GameSession).GetField("_immersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                            state with { Vendors = state.Vendors.Select(v => v.Id == OrganicQueueKind ? v with { QueueCells = null } : v).ToArray() });
                    }
                    if (OrganicQueueKind == "water")
                        foreach (var person in _session.CapturePreparation()!.People.Where(p => _organicQueueShortIds.Contains(p.AgentId)))
                            StaffCaptureSend(new DevelopmentMedicalFixtureCommand(person.AgentId, MedicalAction.GuideToWater));
                    _organicQueueRouteStarted = _session.CurrentTick; _organicQueueMaximum = 0; _organicQueueBestSettled = 0; _organicQueueShortCaptured = false; _organicQueueBest = null; _organicQueueBestFormed = null; _organicQueueBestFormedCount = 0;
                    OrganicQueueFocus(true); _organicQueueStep = 2; return;
                case 2:
                    var count = OrganicQueueCount();
                    var settled = OrganicQueueSettled().Settled;
                    _organicQueueLabel!.Text = $"LABELLED NEED/COOLDOWN DEMO • {OrganicQueuePrefix}\nClaimed {count} • settled {settled} • walking {count - settled} • real physical routes • camera {_camera.Size:0} m";
                    if (count > _organicQueueMaximum || count == _organicQueueMaximum && settled > _organicQueueBestSettled)
                    {
                        _organicQueueMaximum = count;
                        _organicQueueBestSettled = settled;
                        _organicQueueBest = GameSession.Restore(_session.CapturePersistenceSnapshot()).Session ?? throw new InvalidOperationException("Physical queue peak did not restore.");
                    }
                    if (settled >= 3 && settled * 2 >= count && count > _organicQueueBestFormedCount)
                    {
                        _organicQueueBestFormedCount = count;
                        _organicQueueBestFormed = GameSession.Restore(_session.CapturePersistenceSnapshot()).Session ?? throw new InvalidOperationException("Formed queue snapshot did not restore.");
                    }
                    if (!_organicQueueShortCaptured && count >= 3 && settled >= 3)
                    { _organicQueueShortCaptured = true; _organicQueueStep = 3; return; }
                    if (count >= (OrganicQueueKind == "water" ? 11 : 8) && settled * 2 >= count || _session.CurrentTick - _organicQueueRouteStarted >= 6000)
                    {
                        if (_organicQueueMaximum < 3 || _organicQueueBest is null) throw new InvalidOperationException("Physical queue never reached three actual members: " + OrganicQueuePrefix);
                        if (!_organicQueueShortCaptured)
                        {
                            OrganicQueueRestore(_organicQueueBest); _organicQueueShortCaptured = true;
                            GD.Print($"ORGANIC_QUEUE_SETTLED_LIMIT route={OrganicQueuePrefix} claimed={_organicQueueMaximum} settled={_organicQueueBestSettled} short_image_contains_in_transit=True");
                            OrganicQueueFocus(true); _organicQueueStep = 3; return;
                        }
                        _organicQueueMaxima[_organicQueueRoute] = _organicQueueMaximum;
                        OrganicQueueRestore(_organicQueueBestFormed ?? _organicQueueBest); OrganicQueueFocus(true); _organicQueueStep = 5; return;
                    }
                    OrganicQueueAdvance(40); return;
                case 3:
                    OrganicQueueImage("short-overview"); OrganicQueueFocus(false); _organicQueueStep = 4; return;
                case 4:
                    _organicQueueShownShortCount = OrganicQueueCount();
                    if (_organicQueueShownShortCount is < 3 or > 5) throw new InvalidOperationException("The short queue must contain three to five actual members.");
                    OrganicQueueImage("short-detail"); OrganicQueueExtendDemand(); OrganicQueueFocus(true); _organicQueueStep = 2; return;
                case 5:
                    OrganicQueueImage("longest-observed-overview"); OrganicQueueFocus(false); _organicQueueStep = 6; return;
                case 6:
                    _organicQueueShownLongCount = OrganicQueueCount(); _organicQueueShownLongSettled = OrganicQueueSettled().Settled;
                    OrganicQueueImage("longest-observed-detail");
                    _organicQueuePausedHash = _session.CaptureSnapshot().AuthoritativeHash; _organicQueuePausedCells = OrganicQueueCells();
                    PreparationSave(); PreparationLoad();
                    if (_session.CaptureSnapshot().AuthoritativeHash != _organicQueuePausedHash || OrganicQueueCells() != _organicQueuePausedCells)
                        throw new InvalidOperationException("Occupied physical queue changed across exact file reload.");
                    _organicQueueStep = 7; return;
                case 7:
                    if (_session.CaptureSnapshot().AuthoritativeHash != _organicQueuePausedHash || OrganicQueueCells() != _organicQueuePausedCells)
                        throw new InvalidOperationException("Paused queue geometry shuffled.");
                    OrganicQueueImage("reloaded-stable"); OrganicQueueAdvance(400);
                    OrganicQueueFocus(false); _organicQueueStep = 81; return;
                case 81:
                    _organicQueueStep = 8; return; // Complete one draw of advanced physical positions.
                case 8:
                    OrganicQueueImage("advanced-fifo");
                    GD.Print($"ORGANIC_QUEUE_ROUTE_COMPLETE route={OrganicQueuePrefix} short_image_members={_organicQueueShownShortCount} maximum_claimed={_organicQueueMaximum} long_image_members={_organicQueueShownLongCount} long_image_settled={_organicQueueShownLongSettled} distinct_short_long={_organicQueueShownLongCount > _organicQueueShownShortCount} target_depth_reached={_organicQueueShownLongCount >= (OrganicQueueKind == "water" ? 11 : 8) && _organicQueueShownLongSettled * 2 >= _organicQueueShownLongCount} reload=True pause_stable=True advanced=True positions_injected=False");
                    if (++_organicQueueRoute < 5) { _organicQueueStep = 1; return; }
                    _organicQueueStep = 9; return;
                case 9:
                    // Honest BEFORE water evidence is a real preserved saved queue,
                    // rerendered in this build; no claim that this is an old binary.
                    var archived = Path.Combine("C:/Projects/festival-tycoon/reports/evidence/R0.05c/legacy-water-rerun/saves");
                    var restored = SaveFileAdapter.LoadSlot(archived, "manual-water-playtest", _saveCompatibility);
                    if (!restored.IsSuccess) throw new InvalidOperationException("Archived physical water baseline unavailable: " + restored.Error);
                    OrganicQueueRestore(restored.Session!); _organicQueueRoute = 0;
                    _organicQueueWaterPointId = _session.CaptureWaterPoints().OrderByDescending(point => point.Queue.Length + point.Overflow.Length).First().Id;
                    OrganicQueueFocus(false);
                    _organicQueueLabel!.Text = "BEFORE WATER • ARCHIVED REAL R0.05c SAVE RERENDER\nPreserved occupied geometry/positions • not a fresh old-build run";
                    _organicQueueStep = 10; return;
                case 10:
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_organicQueueCaptureDirectory!, "water-before-archived-saved-physical-line.png"));
                    GD.Print($"ORGANIC_QUEUE_CAPTURE_COMPLETE water_after_max={_organicQueueMaxima[0]} food_before_max={_organicQueueMaxima[1]} food_after_max={_organicQueueMaxima[2]} drinks_before_max={_organicQueueMaxima[3]} drinks_after_max={_organicQueueMaxima[4]} water_before_archived_point={_organicQueueWaterPointId} water_before_archived_members={OrganicQueueCount()} water_before_long_unverified=True actual_routes=True no_position_injection=True saved_reload=True paused_stable=True before_water_archived_save=True fixture_need_cooldown=True");
                    GetTree().Quit(); return;
            }
        }
        catch (Exception error) { GD.PushError("ORGANIC_QUEUE_CAPTURE_FAILED " + error); GetTree().Quit(1); }
    }
}
