namespace Festival.Simulation;

public enum SoundRig { Basic, Standard, Pro }

/// <summary>What each thing on the generator draws, against what the generator can supply.</summary>
public sealed record PowerDraw(int Stage, int Bar, int Food, int Lights, int Capacity)
{
    public int Total => Stage + Bar + Food + Lights;
    public bool Over => Total > Capacity;
}

/// <summary>
/// The festival's power budget. The farm's little diesel supplies 100; a hired generator 140. The stage's sound rig,
/// the bar, the food van and (from dusk) the festoon lights draw from it. The included basic PA with both stalls and
/// the lights never exceeds the diesel, so an unchanged setup carries no risk; a bigger rig can. Over capacity, strain
/// builds with how far over it is, and enough strain starts the overload warning, then a dangerous fault. Bringing the
/// load back under capacity lets the strain ease and the generator settle.
/// </summary>
public static class PowerRules
{
    public const int FarmDieselCapacity = 100, HiredGeneratorCapacity = 140;
    public const int BasicRigDraw = 50, StandardRigDraw = 65, ProRigDraw = 80, StallDraw = 15, LightsDraw = 10;
    /// <summary>Any rig idling between sets: amps warm, nothing playing.</summary>
    public const int RigStandbyDraw = 20;
    /// <summary>Strain at which the warning starts: ten over capacity gets there in 40 seconds, twenty in 20.</summary>
    public const int StrainWarning = 32_000, StrainMaximum = 64_000;
    /// <summary>How fast strain eases once the load is back within capacity: about ten seconds from the warning.</summary>
    public const int StrainRecoveryPerTick = 40;
    /// <summary>The festoon lights come on at seven-tenths of the day, as the presentation's dusk does.</summary>
    public const int LightsOnTick = GameSession.PreparedDayTicks * 70 / 100;
    public const string StandardRigOffer = "equipment.rent", ProRigOffer = "equipment.pro", GeneratorOffer = "generator.hire";

    public static int RigDraw(SoundRig rig) => rig switch { SoundRig.Pro => ProRigDraw, SoundRig.Standard => StandardRigDraw, _ => BasicRigDraw };
    public static string RigName(SoundRig rig) => rig switch { SoundRig.Pro => "Pro sound rig", SoundRig.Standard => "Standard sound rig", _ => "Basic PA" };
}

public sealed partial class GameSession
{
    private string[] PowerHires => _preparation is not { } p ? [] : p.Plan?.OfferIds ?? p.AcceptedOffers;

    /// <summary>The stage's rig: the included basic PA unless a bigger one is hired.</summary>
    public SoundRig Rig => PowerHires.Contains(PowerRules.ProRigOffer) ? SoundRig.Pro :
        PowerHires.Contains(PowerRules.StandardRigOffer) || _preparation?.OwnedEquipment.Length > 0 ? SoundRig.Standard : SoundRig.Basic;

    public int GeneratorCapacity => PowerHires.Contains(PowerRules.GeneratorOffer) ? PowerRules.HiredGeneratorCapacity : PowerRules.FarmDieselCapacity;

    /// <summary>Whether this edition runs on the power budget rather than the older scripted overload.</summary>
    public bool PowerBudgetActive => _equipment?.Version == 3;

    /// <summary>What's drawing power now; before opening, the evening's planned peak with every stall and the lights on.</summary>
    public PowerDraw CapturePower()
    {
        var e = _equipment;
        var live = _preparation?.Status is PreparationStatus.Running or PreparationStatus.Departing or PreparationStatus.Failed or PreparationStatus.Finished;
        // A rig draws its full power only while a set is playing; before opening, the plan shows that peak.
        var stage = !StagePowered ? 0 :
            !live || _livePerformances.Any(set => set?.Stage == LiveSetStage.Live) ? PowerRules.RigDraw(Rig) : PowerRules.RigStandbyDraw;
        int Stall(string id, bool powered) => _immersion is not null && Vendors.Any(v => v.Id == id) && (!live || powered) ? PowerRules.StallDraw : 0;
        var lightsOn = !live || _preparation!.StartedTick >= 0 && CurrentTick >= _preparation.StartedTick + PowerRules.LightsOnTick;
        return new(stage, Stall("drinks", e?.BarPowered ?? true), Stall("food", e?.FoodPowered ?? true),
            lightsOn && (!live || (e?.LightsPowered ?? true)) ? PowerRules.LightsDraw : 0, GeneratorCapacity);
    }

    /// <summary>Whether a stall has power to serve: the bar or food van may be switched off to spare the generator.</summary>
    public bool StallPowered(string vendorId) => (!PowerBudgetActive || (vendorId == "drinks" ? _equipment!.BarPowered : _equipment!.FoodPowered)) &&
        !CableCut(vendorId) && !CableCut("generator");

    /// <summary>One tick of the power budget: strain builds over capacity and eases under it, and drives the warning.</summary>
    private void AdvancePowerBudget(EquipmentSnapshot e)
    {
        var draw = CapturePower();
        var over = draw.Total - draw.Capacity;
        var strain = over > 0 ? Math.Min(PowerRules.StrainMaximum, e.Strain + over) : Math.Max(0, e.Strain - PowerRules.StrainRecoveryPerTick);
        _equipment = e = e with { Strain = strain, Capacity = draw.Capacity, LoadPercent = draw.Total * 100 / draw.Capacity };
        switch (e.Stage)
        {
            case EquipmentStage.Normal or EquipmentStage.Resolved when strain >= PowerRules.StrainWarning:
                _equipment = e with { Stage = EquipmentStage.Warning, WarningTick = CurrentTick, WarningAcknowledged = false, Condition = Math.Min(e.Condition, 7_000),
                    Response = "No response" };
                EquipmentEvent($"equipment:warning:{CurrentTick}", $"GENERATOR OVERLOAD: drawing {draw.Total} of {draw.Capacity}. Switch off the bar, food van or lights, cut the stage, or bring the load back under capacity before it faults.");
                break;
            case EquipmentStage.Warning or EquipmentStage.DangerousFault when strain == 0:
                _equipment = e with { Stage = EquipmentStage.Resolved, Response = "Load back within capacity; the generator settled" };
                EquipmentEvent($"equipment:settled:{CurrentTick}", _equipment.Response);
                break;
            case EquipmentStage.Warning when CurrentTick >= e.WarningTick + EquipmentDangerDelayTicks:
                _equipment = e with { Stage = EquipmentStage.DangerousFault, Condition = 3_000 };
                EquipmentEvent($"equipment:fault:{CurrentTick}", $"Dangerous generator fault: still drawing {draw.Total} of {draw.Capacity}. Shed load or cut the stage.");
                break;
            case EquipmentStage.DangerousFault when CurrentTick >= e.WarningTick + EquipmentDeathDelayTicks && NearbyEquipmentPerson() is { } victim:
                EquipmentDeath(e, victim);
                break;
        }
    }

    /// <summary>Whether next tick's strain crosses into the warning or eases to nothing, as AdvancePowerBudget will find.</summary>
    private bool PowerTransitionOnNextTick(EquipmentSnapshot e)
    {
        var draw = CapturePower();
        var over = draw.Total - draw.Capacity;
        var next = over > 0 ? Math.Min(PowerRules.StrainMaximum, e.Strain + over) : Math.Max(0, e.Strain - PowerRules.StrainRecoveryPerTick);
        return e.Stage is EquipmentStage.Normal or EquipmentStage.Resolved && next >= PowerRules.StrainWarning ||
            e.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault && next == 0;
    }

    /// <summary>A power switch for the bar, the food van or the festoon lights. A stall switched off stops serving.</summary>
    private void TogglePowerSwitch(EquipmentAction action)
    {
        var e = _equipment!;
        _equipment = action switch
        {
            EquipmentAction.ToggleBarPower => e with { BarPowered = !e.BarPowered, Response = e.BarPowered ? "Bar switched off to spare the generator" : "Bar power restored" },
            EquipmentAction.ToggleFoodPower => e with { FoodPowered = !e.FoodPowered, Response = e.FoodPowered ? "Food van switched off to spare the generator" : "Food van power restored" },
            _ => e with { LightsPowered = !e.LightsPowered, Response = e.LightsPowered ? "Festoon lights switched off" : "Festoon lights back on" },
        };
        // A stall without power can't serve its queue; those waiting for something it sells go back to their day.
        if (_immersion is null || action == EquipmentAction.ToggleLights) return;
        var id = action == EquipmentAction.ToggleBarPower ? "drinks" : "food";
        if (StallPowered(id)) return;
        foreach (var vendor in Vendors.Where(v => v.Id == id).ToArray())
            foreach (var waiting in vendor.Queue.Where(q => q != vendor.OwnerId).ToArray())
                if (_persons[waiting].Order != ImmersionProduct.Water) LeaveImmersionQueue(waiting, true);
    }
}
