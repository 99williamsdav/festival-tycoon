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

    /// <summary>The evening peak a plan would draw with this rig: the rig, both stalls and the festoon lights.</summary>
    private PowerDraw PlannedPeakWith(SoundRig rig)
    {
        var stalls = _session.CaptureVendors().Count(v => v.Id is "drinks" or "food") * PowerRules.StallDraw;
        return new PowerDraw(PowerRules.RigDraw(rig), stalls, 0, PowerRules.LightsDraw, _session.GeneratorCapacity);
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
        Set(_barPowerButton, EquipmentAction.ToggleBarPower, equipment.BarPowered, "bar", PowerRules.StallDraw);
        Set(_foodPowerButton!, EquipmentAction.ToggleFoodPower, equipment.FoodPowered, "food van", PowerRules.StallDraw);
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
        return $"POWER {power.Total} / {power.Capacity} · {state}\n" +
            $"Stage {power.Stage} ({PowerRules.RigName(_session.Rig)}) · bar {power.Bar} · food van {power.Food} · lights {power.Lights}" +
            (_session.PreparedStatus == PreparationStatus.Preparing ? " (evening peak)" : "") +
            $"\nCondition {e.Condition / 100}%";
    }
}
