namespace Festival.Simulation;

public sealed record LineupEditResult(bool IsValid, bool IsNoOp, string Message, string[] ActIds);

public sealed partial class GameSession
{
    public static int GuestLineupAdjustment(int affinitySum) => Math.Clamp((affinitySum - 150) * 10, -1500, 1500);
    /// <summary>A lineup above what the ticket promised pleases arriving guests; one below it disappoints.</summary>
    public static int ExpectationAdjustment(int averagePopularity, int expectedPopularity) =>
        Math.Clamp((averagePopularity - expectedPopularity) * 10, -800, 800);
    /// <summary>Per mille applied to what a set gives its listeners: better than expected plays bigger.</summary>
    public static int MusicExpectationPermille(int popularity, int expectedPopularity) =>
        Math.Clamp(1000 + (popularity - expectedPopularity) * 5, 700, 1300);
    public static int PerformerLineupPenalty(FestivalAct act, int slot) => slot == 2 || act.Ego < 70 ? 0 :
        1500 * act.Ego * (200 - act.Professionalism) / 20000;
    private int AdmissionLineupAdjustment(Person person)
    {
        // Guests weigh the whole line-up, across every stage: the three acts they most want to see, and how famous the
        // acts are on average. A band member weighs only their own set.
        if (BookedActIds(_programme) is not { } lineup) return 0;
        if (person.Role == ProtectedPersonRole.Guest)
            return GuestLineupAdjustment(lineup.Select(id => FestivalAffinity(person.Id, FestivalActs.Single(a => a.Id == id))).OrderDescending().Take(3).Sum()) +
                ExpectationAdjustment((int)Math.Round(lineup.Average(id => FestivalActs.Single(a => a.Id == id).Popularity)), ExpectedPopularity);
        if (person.Role != ProtectedPersonRole.Performer) return 0;
        var programme = StageProgramme(PerformerStage(person.Id))!;
        var mapping = programme.Performers.Single(p => p.AgentId == person.Id);
        return -PerformerLineupPenalty(FestivalActs.Single(a => a.Id == programme.ActIds[mapping.SlotIndex]), mapping.SlotIndex);
    }
    // Shared presentation resolver: stable IDs and source slots, never a mutable node or paid preview.
    public LineupEditResult PreviewLineupEdit(string actId, int sourceSlot, int targetSlot, bool remove = false, string stageId = FestivalStages.MainId)
    {
        var stage = Math.Max(0, FestivalStages.IndexOf(Stages, stageId));
        var p = _preparation; var q = StageProgramme(stage);
        var ids = p?.Plan is { } plan ? PlanStageActs(plan, stage) : q?.ActIds.Length == 3 ? q.ActIds.ToArray() : new[] { "", "", "" };
        LineupEditResult Invalid(string reason) => new(false, false, reason, ids);
        if (p?.Status != PreparationStatus.Preparing || q is null) return Invalid("Programme locked after Start.");
        if (targetSlot is < 0 or > 2 || sourceSlot is < -1 or > 2 || !FestivalActs.Any(a => a.Id == actId)) return Invalid("Invalid band or non-set drop target.");
        if (!remove && !ActWillPlay(actId)) return Invalid($"{ActCatalogue.Find(actId)!.Name} won't play for the festival yet.");
        var actualSource = Array.IndexOf(ids, actId);
        if (actualSource != sourceSlot) return Invalid("Lineup changed; select the band again.");
        if (p.Plan is null && (remove || sourceSlot < 0)) return Invalid("Already-paid programme: reorder booked bands only; no refunds or rebooking.");
        if (remove)
        {
            if (sourceSlot != targetSlot) return Invalid("Remove requires the assigned set.");
            ids[targetSlot] = "";
            return new(true, false, $"Removed {FestivalActs.Single(a => a.Id == actId).Name} from Set {targetSlot + 1}.", ids);
        }
        if (sourceSlot == targetSlot) return new(true, true, $"Already in Set {targetSlot + 1}.", ids);
        var old = ids[targetSlot];
        var message = sourceSlot >= 0 ? old == "" ? $"Move from Set {sourceSlot + 1} to Set {targetSlot + 1}" : $"Swap Set {sourceSlot + 1} and Set {targetSlot + 1}" :
            old == "" ? $"Place in Set {targetSlot + 1}" : $"Replace {FestivalActs.Single(a => a.Id == old).Name} with {FestivalActs.Single(a => a.Id == actId).Name}";
        if (sourceSlot >= 0) ids[sourceSlot] = old;
        ids[targetSlot] = actId;
        return new(true, false, message, ids);
    }
}
