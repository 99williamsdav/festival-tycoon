namespace Festival.Simulation;

public sealed record LineupEditResult(bool IsValid, bool IsNoOp, string Message, string[] ActIds);

public sealed partial class GameSession
{
    public static GameSession CreateBookingCampaign(ulong seed)
    {
        var session = CreateResultsCampaign(seed);
        session._preparation = session._preparation! with { LineupReactionsVersion = 1 };
        return session;
    }
    public static int GuestLineupAdjustment(int affinitySum) => Math.Clamp((affinitySum - 150) * 10, -1500, 1500);
    public static int PerformerLineupPenalty(FestivalAct act, int slot) => slot == 2 || act.Ego < 70 ? 0 :
        1500 * act.Ego * (200 - act.Professionalism) / 20000;
    private int AdmissionLineupAdjustment(EditionPerson person)
    {
        if (_preparation?.LineupReactionsVersion != 1 || _programme is not { ActIds.Length: 3 } programme) return 0;
        if (person.Role == ProtectedPersonRole.Guest)
            return GuestLineupAdjustment(programme.ActIds.Sum(id => FestivalAffinity(person.AgentId, FestivalActs.Single(a => a.Id == id))));
        if (person.Role != ProtectedPersonRole.Performer) return 0;
        var mapping = programme.Performers.Single(p => p.AgentId == person.AgentId);
        return -PerformerLineupPenalty(FestivalActs.Single(a => a.Id == programme.ActIds[mapping.SlotIndex]), mapping.SlotIndex);
    }
    // Shared presentation resolver: stable IDs and source slots, never a mutable node or paid preview.
    public LineupEditResult PreviewLineupEdit(string actId, int sourceSlot, int targetSlot, bool remove = false)
    {
        var p = _preparation; var q = _programme;
        var ids = (p?.Plan?.ActIds ?? q?.ActIds ?? []).Length == 3 ? (p?.Plan?.ActIds ?? q!.ActIds).ToArray() : new[] { "", "", "" };
        LineupEditResult Invalid(string reason) => new(false, false, reason, ids);
        if (p?.Status != PreparationStatus.Preparing || q is null) return Invalid("Programme locked after Start.");
        if (targetSlot is < 0 or > 2 || sourceSlot is < -1 or > 2 || !FestivalActs.Any(a => a.Id == actId)) return Invalid("Invalid band or non-set drop target.");
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
