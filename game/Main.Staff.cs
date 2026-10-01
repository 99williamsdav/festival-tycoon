using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{

    private static string StaffAbilityText(StaffProfile p) => $"{p.Role.ToString().ToUpperInvariant()} ABILITIES\nWALKING  {p.WalkingSpeedPermille / 10m:0}% of standard speed\n" +
        (p.Role == ResponseRole.Medic ? $"TREATMENT  {p.TreatmentTicks / 80m:0.0}s at 1× • starts after arrival" :
            $"CALMING  {p.CalmingSkill}/10000\nFIGHTING  {p.ConfrontationSkill}/10000 • abilities, not success chances");

    private static string[] StaffStatLabels(StaffRole role) => role switch
    {
        StaffRole.Sound => ["Mixing"],
        StaffRole.Medic => ["Treatment", "Pace"],
        _ => ["Calming", "Firmness", "Pace"],
    };

    private static int[] StaffRatings(StaffCandidate c) => c.Role switch
    {
        StaffRole.Sound => [StaffCatalogue.MixingRating(c)],
        StaffRole.Medic => [StaffCatalogue.TreatmentRating(c), StaffCatalogue.PaceRating(c)],
        _ => [StaffCatalogue.SkillRating(c.CalmingSkill), StaffCatalogue.SkillRating(c.ConfrontationSkill), StaffCatalogue.PaceRating(c)],
    };

    /// <summary>A candidate's abilities as the tooltip spells them out, perk training included.</summary>
    private string StaffCandidateAbilities(StaffCandidate c) => c.Role == StaffRole.Sound
        ? $"SOUND ABILITIES\nMIXING  {(c.MixingBonus > 0 ? "+" : "")}{c.MixingBonus} enjoyment each time the crowd warms to a set"
        : StaffAbilityText(_session.GetCandidateProfile(c));

    /// <summary>The inspector's trait line. The alcoholic stays a hint until they are caught buying beer.</summary>
    private string StaffTraitText(ulong id)
    {
        if (_session.HiredCandidateFor(id) is not { Traits.Length: > 0 } hired) return "";
        var caught = _session.CaptureImmersion()?.Purchases.Any(item => item.AgentId == id && item.Product == ImmersionProduct.Beer) == true;
        return "TRAITS  " + string.Join(", ", hired.Traits.Select(trait => trait == StaffTrait.SneakyAlcoholic && caught
            ? "Sneaky alcoholic (caught at the bar)" : StaffCatalogue.TraitLabel(trait))) + "\n";
    }

    private string ResponseStaffInspectorText(ulong id)
    {
        var profile = _session.GetResponseStaff().SingleOrDefault(item => item.AgentId == id);
        if (profile is null) return "";
        var medic = _session.GetMedicResponses().SingleOrDefault(item => item.WorkerId == id);
        var steward = _session.GetStewardResponses().SingleOrDefault(item => item.WorkerId == id);
        var intervention = _session.CaptureStaffInterventions().SingleOrDefault(item => item.WorkerId == id &&
            item.Stage is StaffInterventionStage.Travelling or StaffInterventionStage.Guiding or StaffInterventionStage.Escorting);
        var target = intervention is not null ? (ulong?)intervention.GuestId :
            medic?.Stage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing ? medic.PatientId :
            steward?.Stage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming or SecurityResponseStage.Confronting ? steward.TargetId : null;
        var targetName = target is { } personId ? _session.CapturePreparation()!.People.Single(item => item.AgentId == personId).Name : "None";
        var nav = _session.CaptureObservation().NavigationAgents.SingleOrDefault(item => item.Id.Value == id);
        var eta = _session.EstimateStaffTravelTicks(id);
        var treatmentRemaining = medic?.Stage == MedicalResponseStage.Treating ? $" • treatment {System.Math.Max(0, medic.StartedTick + medic.TreatmentTicks - _session.CurrentTick) / 80m:0.0}s left" : "";
        return $"{StaffAbilityText(profile)}\n{_session.ResponseStaffStatus(id)} • TARGET {targetName}{treatmentRemaining}\n" +
            $"{intervention?.Description ?? medic?.Description ?? steward?.Description}\nROUTE {nav?.Action} • ETA {(eta is { } ticks ? $"~{ticks / 80m:0.0}s plus crowd delays" : "unavailable")}\nAssign from the target person's inspector\n";
    }
}
