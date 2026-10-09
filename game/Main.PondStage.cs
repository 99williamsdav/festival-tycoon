using Festival.Simulation;
using Godot;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The Pond Stage as the player sees it, while a festival runs it: the modular riser and its speakers by the pond, its
/// own hired generator off the deck's west end, and the bands' garden gate cut in the east hedge with the lane beyond.
/// The riser and generator can be picked; the generator has the farm generator's strain, warning and cutoff. With
/// the Pond Stage closed none of it shows and the east hedge runs unbroken. Presentation only.
/// </summary>
public partial class Main
{
    private Node3D? _pondStage, _pondGate, _pondHedgeRun, _pondGateLeaf, _pondDrumKit;
    private float _pondGateOpen;
    private OmniLight3D[] _pondStageLights = [];
    private ulong _pondStagePickId, _pondGeneratorPickId;
    private Node3D? _pondGeneratorVisual;
    private string? _pondGeneratorShown, _pondDrumHardware;
    private Node3D? _pondPowerChip;
    private Label3D? _pondPowerChipText;
    private Sprite3D? _pondPowerChipIcon;
    private bool _pondGeneratorShaking;
    private double _pondStageSync;
    /// <summary>What of the Pond Stage is selected: "stage", "generator", or nothing.</summary>
    private string? _selectedPond;
    private Button? _pondCutButton;

    /// <summary>A stage's name as a heading says it: "Trailer Stage", "Pond Stage".</summary>
    internal static string StageTitle(FestivalStage stage) => stage.Id == FestivalStages.MainId ? "Trailer Stage" :
        stage.Name.StartsWith("The ", System.StringComparison.Ordinal) ? stage.Name[4..] : stage.Name;

    private static Vector3 PondStageOrigin => new(PondRiser.OriginXMillimetres / 1000f, 0, PondRiser.OriginZMillimetres / 1000f);
    private static Vector3 PondGeneratorHome => new(PondRiser.GeneratorXMillimetres / 1000f, 0, PondRiser.GeneratorZMillimetres / 1000f);

    private void BuildPondStageWorld()
    {
        // The riser and its speakers share the stage's own frame: ground origin, turned half round so it faces -Z.
        // Built hidden, so the first show below also cuts the hedge for the gate.
        _pondStage = new Node3D { Name = "PondStage", Position = PondStageOrigin, Visible = false,
            RotationDegrees = new Vector3(0, FestivalStages.Pond.Placement.YawDegrees, 0) };
        AddChild(_pondStage);
        _pondStage.AddChild(InstantiateAsset("res://assets/environment/modular-riser-v1/lwf_modular_riser_stage_v1.glb"));
        foreach (var side in new[] { -1f, 1f })
        {
            var speaker = InstantiateAsset("res://assets/environment/modular-riser-v1/lwf_modular_riser_speaker_v1.glb");
            speaker.Position = new Vector3(side * 2.42f, .9f, 1.37f);
            _pondStage.AddChild(speaker);
        }
        // Picked like the trailer: one box over the deck, stair and feet (local X -4.88..3.14, Z ±2.14, 1.6 m high).
        var pick = new StaticBody3D { CollisionLayer = 1, CollisionMask = 1, Position = new Vector3(-.87f, .8f, 0) };
        pick.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8.02f, 1.6f, 4.28f) } });
        _pondStage.AddChild(pick);
        _pondStagePickId = pick.GetInstanceId();
        // Two lights over the front of the deck, and the stage's name.
        _pondStageLights = [new OmniLight3D { Position = new Vector3(-1.6f, 2.4f, 1.6f), OmniRange = 7, LightColor = new Color("ffd18a"), LightEnergy = 0 },
            new OmniLight3D { Position = new Vector3(1.6f, 2.4f, 1.6f), OmniRange = 7, LightColor = new Color("eaa8ff"), LightEnergy = 0 }];
        foreach (var light in _pondStageLights) _pondStage.AddChild(light);
        _pondStage.AddChild(BuildingName("POND STAGE", new Vector3(0, 5.2f, 0)));

        // The bands' gate: the east hedge's run here (z 24..16) is cut for a garden gate at z 17, cells 161..162, with
        // a rounded hedge end either side as at the farmhouse gate, and the lane beyond. Gate and lane turn half round
        // so the lane lies outside the east hedge.
        _pondGate = new Node3D { Name = "PondGate", Visible = false };
        AddChild(_pondGate);
        string Env(string name) => $"res://assets/environment/{name}.glb";
        foreach (var (name, z, yaw) in new (string, float, float)[]
                 { ("lwf_hedge_straight_4m_a_v1", 24, 90), ("lwf_hedge_straight_4m_a_v1", 23, 90), ("lwf_hedge_gate_end_v1", 19, 90), ("lwf_hedge_gate_end_v1", 15, 270) })
            RegisterBreezeHedge(PlaceBackstagePiece(_pondGate, Env(name), 32, z, yaw));
        var gate = PlaceBackstagePiece(_pondGate, Env("lwf_garden_gate_iron_v1"), 32, 17, 180);
        _pondGateLeaf = gate.FindChild("LWF_GardenGate_Leaf", true, false) as Node3D;
        PlaceBackstagePiece(_pondGate, Env("lwf_hedge_gate_lane_v1"), 32, 17, 180);
    }

    /// <summary>Shows the Pond Stage while this festival runs it, keeps its generator, drums and gate in step, and its chip.</summary>
    private void ProcessPondStage(double delta)
    {
        var open = _session.PondStageOpen;
        if (open && _pondStage is null) BuildPondStageWorld();
        if (_pondStage is null) return;
        if (_pondStage.Visible != open)
        {
            _pondStage.Visible = _pondGate!.Visible = open;
            if (_pondHedgeRun is not null) _pondHedgeRun.Visible = !open;
            foreach (var body in _pondStage.FindChildren("*", "StaticBody3D", true, false).OfType<StaticBody3D>()) body.CollisionLayer = open ? 1u : 0;
        }
        if (!open)
        {
            if (_selectedPond is not null) ClearSelection();
            _pondGeneratorVisual?.QueueFree(); _pondGeneratorVisual = null; _pondGeneratorShown = null;
            _pondDrumKit?.QueueFree(); _pondDrumKit = null; _pondDrumHardware = null;
            if (_pondPowerChip is not null) _pondPowerChip.Visible = false;
            return;
        }
        SyncPondGenerator(delta);
        // The garden gate swings open as a band member comes through, as the farmhouse gate does.
        if (_pondGateLeaf is not null)
        {
            var near = _attendeeVisuals.Values.Any(body => body.Visible && new Vector2(body.Position.X - 32, body.Position.Z - 17).LengthSquared() < 2.2f * 2.2f);
            _pondGateOpen = Mathf.MoveToward(_pondGateOpen, near ? 1 : 0, _session.IsPaused ? 0 : (float)delta * 2.5f);
            _pondGateLeaf.RotationDegrees = new Vector3(0, -90 + 90 * Mathf.SmoothStep(0, 1, _pondGateOpen), 0);
        }
        _pondStageSync -= delta;
        if (_pondStageSync > 0) return;
        _pondStageSync = .5;
        if (_session.PreparedStatus == PreparationStatus.Failed && _pondDrumKit is not null && IsInstanceValid(_pondDrumKit)) return;
        // The drums belong to whoever is on the riser, or next up, until the outgoing drummer puts the sticks down.
        var live = _session.CaptureLivePerformance(FestivalStages.PondId);
        var onStage = live?.Stage is LiveSetStage.Live or LiveSetStage.Interrupted;
        var act = onStage ? _session.CurrentStageAct(FestivalStages.PondId) : _session.UpcomingStageAct(FestivalStages.PondId) ?? _session.CurrentStageAct(FestivalStages.PondId);
        if (act is null && _session.PreparedStatus is PreparationStatus.Departing or PreparationStatus.Finished && _pondDrumKit is not null) return;
        var drummerStillPlaying = live?.Performers.Any(p => p.InstrumentAttached) == true;
        _pondDrumKit = DrumHardware(FestivalStages.Pond, drummerStillPlaying ? _session.CurrentStageAct(FestivalStages.PondId) ?? act : act, ref _pondDrumHardware, _pondDrumKit);
        if (_selectedPond is not null) RefreshPondInspector();
    }

    /// <summary>The pond generator's model for its state, its smoke and shake, and its power chip, as the farm generator's.</summary>
    private void SyncPondGenerator(double delta)
    {
        if (_session.CaptureStageGenerator(FestivalStages.PondId) is not { } generator) return;
        var variant = generator.Stage switch { EquipmentStage.Warning => "overloaded", EquipmentStage.DangerousFault or EquipmentStage.Terminal => "fault", EquipmentStage.Isolated => "isolated", _ => "normal" };
        if (_pondGeneratorShown != variant || _pondGeneratorVisual is null || !IsInstanceValid(_pondGeneratorVisual))
        {
            _pondGeneratorVisual?.QueueFree();
            _pondGeneratorVisual = AddAsset($"res://assets/equipment/lwf_hire_generator_{variant}_v1.glb", PondGeneratorHome);
            _pondGeneratorShown = variant;
            _pondGeneratorShaking = AddGeneratorSmoke(_pondGeneratorVisual, "hire_generator", generator.Stage);
            var pick = new StaticBody3D { CollisionLayer = 1, CollisionMask = 1, Position = new Vector3(.17f, 1, 0) };
            pick.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3.8f, 2, 1.5f) } });
            _pondGeneratorVisual.AddChild(pick); _pondGeneratorPickId = pick.GetInstanceId();
            // Smaller and lower than the stage it powers, so the two names don't collide.
            _pondGeneratorVisual.AddChild(BuildingName("GENERATOR", new Vector3(0, 1.4f, 0), 28));
        }
        _pondGeneratorVisual.Position = _pondGeneratorShaking && !_session.IsPaused
            ? PondGeneratorHome + new Vector3(Mathf.Sin((float)_generatorClock * 125f) * .015f, 0, Mathf.Cos((float)_generatorClock * 97f) * .015f) : PondGeneratorHome;
        if (_pondPowerChip is null)
        {
            _pondPowerChip = new Node3D();
            AddChild(_pondPowerChip);
            _pondPowerChipIcon = new Sprite3D { PixelSize = .0018f, FixedSize = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
                RenderPriority = 3, Offset = new Vector2(-16, 0), Layers = EyeHiddenLayer };
            _pondPowerChip.AddChild(_pondPowerChipIcon);
            _pondPowerChipText = WorldText.Speech(new Label3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, HorizontalAlignment = HorizontalAlignment.Left,
                Offset = new Vector2(4, 0), OutlineModulate = new Color("17302a") }, 28);
            _pondPowerChipText.Layers = EyeHiddenLayer;
            _pondPowerChip.AddChild(_pondPowerChipText);
        }
        var power = _session.CaptureStagePower(FestivalStages.PondId);
        var trouble = generator.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault || power?.Over == true && _session.PreparedStatus == PreparationStatus.Running;
        var planning = _session.PreparedStatus == PreparationStatus.Preparing && _hudWorkspaceOpen && HudPageSelected("Supplies");
        _pondPowerChip.Visible = power is not null && (trouble || _selectedPond == "generator" || planning) && !EyeViewActive;
        _pondPowerChip.Position = PondGeneratorHome + new Vector3(0, 2.6f, 0);
        _pondPowerChipText!.Text = power is null ? "" : $"{power.Total} / {power.Capacity}";
        _pondPowerChipText.Modulate = trouble ? new Color("ffb08a") : new Color("fff7e1");
        _pondPowerChipIcon!.Texture = GD.Load<Texture2D>(trouble ? "res://assets/ui/lwf_power_chip_zap_alert_v1.svg" : "res://assets/ui/lwf_power_chip_zap_v1.svg");
    }

    /// <summary>Picks a Pond Stage collider; false when it isn't one.</summary>
    private bool TrySelectPond(Node collider)
    {
        if (_pondStage is not { Visible: true }) return false;
        var id = collider.GetInstanceId();
        if (id == _pondStagePickId) SelectPond("stage");
        else if (id == _pondGeneratorPickId && _pondGeneratorVisual is not null) SelectPond("generator");
        else return false;
        return true;
    }

    private void SelectPond(string part)
    {
        ClearSelection();
        _selectedPond = part;
        var generator = part == "generator";
        _highlight.Position = (generator ? _pondGeneratorVisual!.Position : PondStageOrigin) + new Vector3(0, .08f, 0);
        _highlight.Scale = generator ? new Vector3(3, 1, 3) : new Vector3(5, 1, 5); _highlight.Visible = true;
        RefreshStagePowerAction();
        RefreshPondInspector();
        GD.Print($"POND_SELECTED part={part}");
    }

    private static string PondGeneratorState(StageGeneratorSnapshot generator, PowerDraw? power) => generator.Stage switch
    {
        EquipmentStage.Warning => "OVERLOADED · cut the stage or lighten the rig before it faults",
        EquipmentStage.DangerousFault => "DANGEROUS FAULT · cut the stage now; keep people away",
        EquipmentStage.Isolated => "Stage cut off",
        EquipmentStage.Terminal => "Failed",
        _ => power?.Over == true ? "Over capacity · strain building" : "Running within capacity",
    };

    private void RefreshPondInspector()
    {
        if (_selectedPond is null) return;
        var generator = _session.CaptureStageGenerator(FestivalStages.PondId);
        var power = _session.CaptureStagePower(FestivalStages.PondId);
        if (_selectedPond == "generator")
        {
            _inspectorTitle.Text = "Pond Stage generator";
            _inspectorBody.Text = generator is null ? "Runs the Pond Stage only." :
                $"POWER {power?.Total ?? 0} / {generator.Capacity} · {PondGeneratorState(generator, power)}\n" +
                $"Stage {power?.Stage ?? 0} ({PowerRules.RigName(_session.Rig)})" + (_session.PreparedStatus == PreparationStatus.Preparing ? " (evening peak)" : "") +
                "\nHired with the stage; it runs the Pond Stage's rig and nothing else.";
        }
        else
        {
            var id = FestivalStages.PondId;
            var live = _session.CaptureLivePerformance(id);
            var onStage = live?.Stage is LiveSetStage.Live or LiveSetStage.Interrupted;
            var act = onStage ? _session.CurrentStageAct(id) : _session.UpcomingStageAct(id) ?? _session.CurrentStageAct(id);
            var status = _session.PreparedStatus == PreparationStatus.Preparing ? "Before opening" : _session.CaptureProgramme(id)?.Status ?? "";
            _inspectorTitle.Text = FestivalStages.Pond.Name;
            _inspectorBody.Text = $"{(onStage ? "On stage" : "Next up")} · {act?.Name ?? "Awaiting booking"}\n{status}\n" +
                $"Sound {_session.SoundScoreAt(id)}/100 · {PerformanceRules.SoundWord(_session.SoundScoreAt(id))}" +
                (generator is null ? "" : $"\nGenerator · {PondGeneratorState(generator, power)}");
        }
        RefreshStagePowerAction();
        RefreshContextPanelVisibility();
    }

    private void BuildPondStageAction(VBoxContainer parent)
    {
        _pondCutButton = ButtonText("Cut Pond Stage power", () =>
            CommitEquipmentAction(new StageGeneratorCommand(FestivalStages.PondId, StageGeneratorAction.Isolate)));
        _pondCutButton.Visible = false;
        parent.AddChild(_pondCutButton);
    }

    private void RefreshPondCutButton()
    {
        if (_pondCutButton is null) return;
        _pondCutButton.Visible = _selectedPond is not null && _session.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing;
        if (!_pondCutButton.Visible) return;
        var error = _session.ValidateCommand(CampaignEnvelope(new StageGeneratorCommand(FestivalStages.PondId, StageGeneratorAction.Isolate)));
        _pondCutButton.Disabled = error is not null;
        _pondCutButton.TooltipText = "Cuts the Pond Stage off its generator: its music stops, but the rig's draw comes off at once.\n" + (error?.Message ?? "Cut the Pond Stage's power.");
    }
}
