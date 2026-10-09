namespace Festival.Simulation;

public sealed partial class GameSession
{
    // Derived descriptive title only: personal names/IDs and rule roles remain
    // authoritative and unchanged. Booking already enters persistence/hash.
    public string? FestivalPerformerTitle(ulong id)
    {
        if (PerformerStage(id) is not (var stage and >= 0) || StageProgramme(stage) is not { } q ||
            q.Performers.SingleOrDefault(person => person.AgentId == id) is not { } role) return null;
        var band = q.ActIds.Length == FestivalStages.All[stage].SlotCount
            ? FestivalActs.Single(act => act.Id == q.ActIds[role.SlotIndex]).Name
            : $"Unbooked Set {role.SlotIndex + 1}";
        var instrument = role.RoleIndex switch { 0 => "Lead", 1 => "Bassist", 2 => "Drummer", _ => "Performer" };
        return $"{band} {instrument}";
    }
}
