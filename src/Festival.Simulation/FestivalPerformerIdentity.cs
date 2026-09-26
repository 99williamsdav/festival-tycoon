namespace Festival.Simulation;

public sealed partial class GameSession
{
    // Derived descriptive title only: personal names/IDs and rule roles remain
    // authoritative and unchanged. Booking already enters persistence/hash.
    public string? FestivalPerformerTitle(ulong id)
    {
        if (_programme?.Performers.SingleOrDefault(person => person.AgentId == id) is not { } role) return null;
        var band = _programme.ActIds.Length == 3
            ? FestivalActs.Single(act => act.Id == _programme.ActIds[role.SlotIndex]).Name
            : $"Unbooked Set {role.SlotIndex + 1}";
        var instrument = role.RoleIndex switch { 0 => "Lead", 1 => "Bassist", 2 => "Drummer", _ => "Performer" };
        return $"{band} {instrument}";
    }
}
