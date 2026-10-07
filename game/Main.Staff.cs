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

    // One festival minute is 80 ticks; skills are out of 10,000, shown out of 10.
    private static string StaffAbilityText(StaffProfile p) => $"Walks at {p.WalkingSpeedPermille / 10m:0}% of normal pace\n" +
        (p.Role == ResponseRole.Medic ? $"Treats someone in about {Math.Max(1, (int)Math.Round(p.TreatmentTicks / 80m))} min once there" :
            $"Calming {p.CalmingSkill / 1000m:0.#}/10 · standing firm {p.ConfrontationSkill / 1000m:0.#}/10");

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

    /// <summary>What a medic or steward is up to, in a line, for the person panel.</summary>
    private string ResponseStaffInspectorText(ulong id)
    {
        if (_hudDevelopment) return ResponseStaffDiagnosticText(id);
        if (_session.GetResponseStaff().SingleOrDefault(item => item.AgentId == id) is null) return "";
        var medic = _session.GetMedicResponses().SingleOrDefault(item => item.WorkerId == id);
        var steward = _session.GetStewardResponses().SingleOrDefault(item => item.WorkerId == id);
        var intervention = _session.CaptureStaffInterventions().SingleOrDefault(item => item.WorkerId == id &&
            item.Stage is StaffInterventionStage.Travelling or StaffInterventionStage.Guiding or StaffInterventionStage.Escorting);
        string Name(ulong person) => _session.CapturePreparation()!.People.Single(item => item.AgentId == person).Name;
        var status = _session.ResponseStaffStatus(id);
        var line = status.StartsWith("Unavailable · incapacitated", StringComparison.Ordinal) ? "Hurt: needs first aid" :
            intervention is not null ? (intervention.Stage == StaffInterventionStage.Travelling ? $"On the way to {Name(intervention.GuestId)}" : $"Helping {Name(intervention.GuestId)}") :
            medic is { Stage: MedicalResponseStage.Treating, PatientId: { } treating } ? $"Treating {Name(treating)}" :
            medic is { Stage: MedicalResponseStage.Travelling or MedicalResponseStage.Removing, PatientId: { } patient } ? $"On the way to {Name(patient)}" :
            steward?.Stage is SecurityResponseStage.Calming or SecurityResponseStage.Confronting && steward.TargetId is { } calming ? $"Dealing with {Name(calming)}" :
            steward?.Stage == SecurityResponseStage.Travelling && steward.TargetId is { } heading ? $"On the way to {Name(heading)}" :
            status.StartsWith("Cleaning", StringComparison.Ordinal) ? "Picking up litter" :
            status.StartsWith("Idle", StringComparison.Ordinal) ? "Free to help" :
            status.StartsWith("Unavailable · ", StringComparison.Ordinal) ? status["Unavailable · ".Length..] : status;
        return line + "\n";
    }

    private string ResponseStaffDiagnosticText(ulong id)
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
