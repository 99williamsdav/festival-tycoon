using Festival.Simulation;
using Godot;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The generator's power budget as the player sees it: the evening peak a plan would draw, and the live switches in
/// the generator's inspector for the bar, the food van and the festoon lights, beside the stage cutoff.
/// </summary>
public partial class Main
{
    private Button? _barPowerButton, _foodPowerButton, _lightsPowerButton;
    private Node3D? _powerChip;
    private Label3D? _powerChipText;
    private Sprite3D? _powerChipIcon;
    private bool _generatorShaking;
    private double _generatorClock;

    /// <summary>Smoke from the exhaust, thicker and darker as the generator strains; and a shake when it's struggling.</summary>
    private void AttachGeneratorEffects(Node3D visual, string model, EquipmentStage stage) => _generatorShaking = AddGeneratorSmoke(visual, model, stage);

    /// <summary>A generator's exhaust smoke for its state; true when it's straining enough to shake.</summary>
    private static bool AddGeneratorSmoke(Node3D visual, string model, EquipmentStage stage)
    {
        var shaking = stage is EquipmentStage.Warning or EquipmentStage.DangerousFault;
        if (model == "towable_generator" || stage is EquipmentStage.Isolated or EquipmentStage.Terminal) return shaking;
        var (rate, colour) = stage switch
        {
            EquipmentStage.Warning => (4f, new Color("3d3a36")),
            EquipmentStage.DangerousFault => (6f, new Color("1b1a19")),
            _ => (1.5f, new Color("b9b6ae")),
        };
        // Each puff doubles in size as it rises.
        var swell = new Curve { MinValue = 0, MaxValue = 2 };
        swell.AddPoint(new Vector2(0, 1)); swell.AddPoint(new Vector2(1, 2));
        var smoke = new CpuParticles3D
        {
            Position = model == "hire_generator" ? new Vector3(-0.45f, 1.72f, -0.25f) : new Vector3(-0.32f, 1.35f, 0.25f),
            Amount = (int)(rate * 2) + 2, Lifetime = 1.5, Mesh = new SphereMesh { Radius = .12f, Height = .24f, RadialSegments = 6, Rings = 3 },
            Direction = Vector3.Up, Spread = 12, InitialVelocityMin = .5f, InitialVelocityMax = .7f, Gravity = Vector3.Zero,
            ScaleAmountMin = 1, ScaleAmountMax = 1.2f,
            ScaleAmountCurve = swell,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        visual.AddChild(smoke);
        return shaking;
    }

    /// <summary>The generator's shake, and its power chip: shown when it's selected, while planning supplies, or whenever it's over capacity.</summary>
    private void ProcessGenerator(double delta)
    {
        if (_equipmentVisual is null || !IsInstanceValid(_equipmentVisual) || _session.CaptureEquipment() is not { } e) return;
        _generatorClock += _session.IsPaused ? 0 : delta;
        var home = new Vector3(e.XMillimetres / 1000f, 0, e.ZMillimetres / 1000f);
        _equipmentVisual.Position = _generatorShaking && !_session.IsPaused
            ? home + new Vector3(Mathf.Sin((float)_generatorClock * 125f) * .015f, 0, Mathf.Cos((float)_generatorClock * 97f) * .015f) : home;
        if (!_session.PowerBudgetActive) return;
        if (_powerChip is null)
        {
            _powerChip = new Node3D();
            AddChild(_powerChip);
            _powerChipIcon = new Sprite3D { PixelSize = .0018f, FixedSize = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
                RenderPriority = 3, Offset = new Vector2(-16, 0), Layers = EyeHiddenLayer };
            _powerChip.AddChild(_powerChipIcon);
            _powerChipText = WorldText.Speech(new Label3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, HorizontalAlignment = HorizontalAlignment.Left,
                Offset = new Vector2(4, 0), OutlineModulate = new Color("17302a") }, 28);
            _powerChipText.Layers = EyeHiddenLayer;
            _powerChip.AddChild(_powerChipText);
        }
        var power = _session.CapturePower();
        var trouble = e.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault || power.Over && _session.PreparedStatus == PreparationStatus.Running;
        var planning = _session.PreparedStatus == PreparationStatus.Preparing && _hudWorkspaceOpen && HudPageSelected("Supplies");
        _powerChip.Visible = (trouble || _selectedGenerator || planning) && !EyeViewActive;
        _powerChip.Position = home + new Vector3(0, 2.6f, 0);
        _powerChipText!.Text = $"{power.Total} / {power.Capacity}";
        _powerChipText.Modulate = trouble ? new Color("ffb08a") : new Color("fff7e1");
        _powerChipIcon!.Texture = GD.Load<Texture2D>(trouble ? "res://assets/ui/lwf_power_chip_zap_alert_v1.svg" : "res://assets/ui/lwf_power_chip_zap_v1.svg");
    }

    /// <summary>
    /// The evening peak a plan would draw with this rig: the rig, every bar and van, and the festoon lights; with the
    /// generators pooled, the Pond Stage's rig too, against the whole pool.
    /// </summary>
    private PowerDraw PlannedPeakWith(SoundRig rig)
    {
        var stalls = _session.CaptureVendors().Count * PowerRules.StallDraw;
        return new PowerDraw(PowerRules.RigDraw(rig), stalls, 0, PowerRules.LightsDraw, _session.CapturePower().Capacity)
            { OtherStages = _session.PowerPooled ? PowerRules.RigDraw(rig) * (_session.Stages.Count - 1) : 0 };
    }

    /// <summary>"Stage 50 · Pond Stage 50 · bars 30 …": what draws what.</summary>
    private string PowerParts(PowerDraw power) =>
        $"Stage {power.Stage} ({PowerRules.RigName(_session.Rig)})" + (_session.PowerPooled ? $" · Pond Stage {power.OtherStages}" : "") +
        $" · {(power.Bar > PowerRules.StallDraw ? "bars" : "bar")} {power.Bar} · {(power.Food > PowerRules.StallDraw ? "food vans" : "food van")} {power.Food} · lights {power.Lights}";

    /// <summary>With the generators pooled, which machines make up the supply, and which are out of it.</summary>
    private string PooledSupplyText()
    {
        if (!_session.PowerPooled || _session.CaptureEquipment() is not { } farm) return "";
        var pond = _session.CaptureStageGenerator(FestivalStages.PondId);
        // A stage cut takes off only its rig; a generator leaves the supply only when it fails.
        string Part(string name, int capacity, bool running) => running ? $"{name} {capacity}" : $"{name} failed";
        return $"\nOne supply: {Part("farm generator", _session.GeneratorCapacity, farm.Stage != EquipmentStage.Terminal)}" +
            (pond is null ? "" : $" + {Part("Pond generator", pond.Capacity, pond.Stage != EquipmentStage.Terminal)}");
    }

    private void BuildPowerSwitches(VBoxContainer parent)
    {
        Button Switch(EquipmentAction action)
        {
            var button = ButtonText("", () => CommitEquipmentAction(new EquipmentCommand(action)));
            button.Visible = false; parent.AddChild(button);
            return button;
        }
        _barPowerButton = Switch(EquipmentAction.ToggleBarPower);
        _foodPowerButton = Switch(EquipmentAction.ToggleFoodPower);
        _lightsPowerButton = Switch(EquipmentAction.ToggleLights);
    }

    private void RefreshPowerSwitches()
    {
        if (_barPowerButton is null) return;
        var shown = _selectedGenerator && _session.PowerBudgetActive && _session.PreparedStatus == PreparationStatus.Running && _session.CaptureEquipment() is { } e;
        foreach (var button in new[] { _barPowerButton, _foodPowerButton!, _lightsPowerButton! }) button.Visible = shown;
        if (!shown) return;
        var equipment = _session.CaptureEquipment()!;
        void Set(Button button, EquipmentAction action, bool on, string what, int draw)
        {
            button.Text = on ? $"Switch off the {what} (saves {draw})" : $"Switch the {what} back on";
            button.Disabled = _session.ValidateCommand(CampaignEnvelope(new EquipmentCommand(action))) is not null;
        }
        // One switch for every bar and one for every van.
        var bars = _session.CaptureVendors().Count(v => Stalls.IsBar(v.Id)); var vans = _session.CaptureVendors().Count(v => Stalls.IsVan(v.Id));
        Set(_barPowerButton, EquipmentAction.ToggleBarPower, equipment.BarPowered, bars > 1 ? "bars" : "bar", PowerRules.StallDraw * Math.Max(1, bars));
        Set(_foodPowerButton!, EquipmentAction.ToggleFoodPower, equipment.FoodPowered, vans > 1 ? "food vans" : "food van", PowerRules.StallDraw * Math.Max(1, vans));
        Set(_lightsPowerButton!, EquipmentAction.ToggleLights, equipment.LightsPowered, "festoon lights", PowerRules.LightsDraw);
    }

    /// <summary>The generator inspector's power line: what's drawing what, against capacity.</summary>
    private string PowerBreakdownText()
    {
        var power = _session.CapturePower();
        var e = _session.CaptureEquipment()!;
        var state = e.Stage switch
        {
            EquipmentStage.Warning => "OVERLOADED · shed load before it faults",
            EquipmentStage.DangerousFault => "DANGEROUS FAULT · cut load now; keep people away",
            EquipmentStage.Isolated => "Stage cut off",
            EquipmentStage.Terminal => "Failed",
            _ => power.Over ? "Over capacity · strain building" : "Running within capacity",
        };
        if (e.StageCut) state = "Trailer stage cut off · " + state;
        return $"POWER {power.Total} / {power.Capacity} · {state}\n" +
            PowerParts(power) + (_session.PreparedStatus == PreparationStatus.Preparing ? " (evening peak)" : "") + PooledSupplyText() +
            $"\nCondition {e.Condition / 100}%";
    }
}
