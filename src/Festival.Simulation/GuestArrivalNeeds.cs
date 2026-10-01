namespace Festival.Simulation;

public sealed partial class GameSession
{
    // Independent labelled streams avoid perturbing gameplay RNG or correlating the meters.
    private static ulong GuestInitialRoll(ulong seed, ulong id, ulong salt)
    {
        var value = seed ^ (id * 0x9E3779B97F4A7C15UL) ^ salt;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
    public const int GuestArrivalLatestSecond = 55;
    public static int GuestReleaseTick(ulong seed, ulong id) =>
        (int)(GuestInitialRoll(seed, id, 0x4152524956414CUL) % (GuestArrivalLatestSecond + 1)) * 80;
    public static int GuestOpeningHunger(ulong seed, ulong id) =>
        1_500 + (int)(GuestInitialRoll(seed, id, 0x48554E474552UL) % 4_001);
    public static int GuestOpeningToiletNeed(ulong seed, ulong id) =>
        1_200 + (int)(GuestInitialRoll(seed, id, 0x544F494C4554UL) % 4_001);
    public static int GuestOpeningThirst(ulong seed, ulong id) =>
        2_500 + (int)(GuestInitialRoll(seed, id, 0x544849525354UL) % 5_301);

    public bool GuestWaitingForRelease(ulong id) =>
        _preparation is { Status: PreparationStatus.Running } p &&
        PersonIn(PersonView.Roster, id) is { Role: ProtectedPersonRole.Guest or ProtectedPersonRole.Staff, Admitted: false, Departed: false } &&
        _navigationAgents.TryGetValue(new(id), out var navigation) && navigation.Destination is null;

    private void ApplyBuildGuestOpeningNeeds()
    {
        if (_preparation is null) return;
        foreach (var person in PeopleIn(PersonView.Consumption))
            if (PersonIn(PersonView.Roster, person.Id) is { Role: ProtectedPersonRole.Guest })
                _persons.Set(person with { Hunger = GuestOpeningHunger(CampaignSeed, person.Id), ToiletNeed = GuestOpeningToiletNeed(CampaignSeed, person.Id) });
        if (_medical is not null)
            foreach (var need in PeopleIn(PersonView.Medical))
                if (need.NeedProfile == MedicalNeedProfile.Guest)
                    _persons.Set(need with { Thirst = GuestOpeningThirst(CampaignSeed, need.Id) });
    }
}
