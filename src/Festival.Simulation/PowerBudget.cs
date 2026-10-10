namespace Festival.Simulation;

public enum SoundRig { Basic, Standard, Pro }

/// <summary>What each thing on the generator draws, against what the generator can supply.</summary>
/// <param name="Stage">The trailer stage's rig.</param>
/// <param name="Capacity">The farm generator's, or with the generators pooled, every generator still in the pool.</param>
public sealed record PowerDraw(int Stage, int Bar, int Food, int Lights, int Capacity)
{
    /// <summary>Every later stage's rig, when the generators are pooled.</summary>
    public int OtherStages { get; init; }
    public int Total => Stage + Bar + Food + Lights + OtherStages;
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
    /// <summary>The festoon lights come on at seven-tenths of the day, as the presentation's dusk does (single-stage day).</summary>
    public const int LightsOnTick = GameSession.PreparedDayTicks * 70 / 100;
    /// <summary>Seven-tenths of a day of this length.</summary>
    public static int LightsOnTickFor(int dayTicks) => dayTicks * 70 / 100;
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
        // The trailer's rig draws its full power only while its set is playing; before opening, the plan shows that peak.
        var stage = !StagePowered ? 0 :
            !live || _livePerformances[0]?.Stage == LiveSetStage.Live ? PowerRules.RigDraw(Rig) : PowerRules.RigStandbyDraw;
        // Every bar and every van draws its own share; each switch covers all of its kind.
        int Stalls(Func<string, bool> kind, bool powered) => _immersion is null || live && !powered ? 0 : Vendors.Count(v => kind(v.Id)) * PowerRules.StallDraw;
        var lightsOn = !live || _preparation!.StartedTick >= 0 && CurrentTick >= _preparation.StartedTick + PowerRules.LightsOnTickFor(PreparedEditionDurationTicks);
        var draw = new PowerDraw(stage, Stalls(Festival.Simulation.Stalls.IsBar, e?.BarPowered ?? true), Stalls(Festival.Simulation.Stalls.IsVan, e?.FoodPowered ?? true),
            lightsOn && (!live || (e?.LightsPowered ?? true)) ? PowerRules.LightsDraw : 0, GeneratorCapacity);
        if (!PowerPooled) return draw;
        // One supply: every stage's rig draws from it, and it holds every generator not cut off.
        return draw with { OtherStages = Enumerable.Range(1, Stages.Count - 1).Sum(StageRigDraw),
            Capacity = (FarmGeneratorInPool ? GeneratorCapacity : 0) + _stageGenerators!.Where(StageGeneratorInPool).Sum(g => g.Capacity) };
    }

    /// <summary>
    /// With the Pond Stage open, its generator and the farm diesel share one supply: everything draws from the pool, and
    /// strain, the warning and the fault follow the pool's overage. A single stage keeps the farm diesel alone.
    /// </summary>
    public bool PowerPooled => PowerBudgetActive && _stageGenerators is { Length: > 0 };
    /// <summary>A generator cut off (or the farm's failed) leaves the pool; what's left shares the rest.</summary>
    private bool FarmGeneratorInPool => _equipment!.Stage is not (EquipmentStage.Isolated or EquipmentStage.Terminal);
    private static bool StageGeneratorInPool(StageGeneratorSnapshot generator) => generator.Stage != EquipmentStage.Isolated;
    /// <summary>The overage that strains a generator: the pool's while it's in it, none once it's out (it eases).</summary>
    private static int Overage(PowerDraw draw, bool inPool) => inPool ? draw.Total - draw.Capacity : -1;

    /// <summary>Whether a stall has power to serve: the bars or the food vans may be switched off to spare the generator.</summary>
    public bool StallPowered(string vendorId) => (!PowerBudgetActive || (Festival.Simulation.Stalls.IsBar(vendorId) ? _equipment!.BarPowered : _equipment!.FoodPowered)) &&
        !CableCut(vendorId) && !CableCut("generator");

    /// <summary>One tick of the power budget: strain builds over capacity and eases under it, and drives the warning.</summary>
    private void AdvancePowerBudget(EquipmentSnapshot e)
    {
        var draw = CapturePower();
        var over = Overage(draw, !PowerPooled || FarmGeneratorInPool);
        var strain = over > 0 ? Math.Min(PowerRules.StrainMaximum, e.Strain + over) : Math.Max(0, e.Strain - PowerRules.StrainRecoveryPerTick);
        // The farm generator keeps its own capacity; its load is the pool's when pooled.
        _equipment = e = e with { Strain = strain, Capacity = GeneratorCapacity,
            LoadPercent = !PowerPooled ? draw.Total * 100 / draw.Capacity : draw.Capacity == 0 ? 300 : Math.Min(300, draw.Total * 100 / draw.Capacity) };
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
        var over = Overage(draw, !PowerPooled || FarmGeneratorInPool);
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
        Func<string, bool> kind = action == EquipmentAction.ToggleBarPower ? Festival.Simulation.Stalls.IsBar : Festival.Simulation.Stalls.IsVan;
        foreach (var vendor in Vendors.Where(v => kind(v.Id) && !StallPowered(v.Id)).ToArray())
            foreach (var waiting in vendor.Queue.Where(q => q != vendor.OwnerId).ToArray())
                if (_persons[waiting].Order != ImmersionProduct.Water) LeaveImmersionQueue(waiting, true);
    }
}
