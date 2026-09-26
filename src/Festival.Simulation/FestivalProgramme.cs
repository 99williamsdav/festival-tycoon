using System.Text.Json;

namespace Festival.Simulation;

public sealed record FestivalAct(string Id, string Name, int Genre, int PricePennies, int Popularity);
public sealed record ProgrammePerformer(ulong AgentId, int SlotIndex, int RoleIndex);
public sealed record ProgrammeSnapshot(int Version, string[] ActIds, ProgrammePerformer[] Performers, int CurrentSlot, long SlotEndTick, string Status);
public sealed record SetProgrammeCommand(string[] ActIds) : SessionCommand;

public sealed partial class GameSession
{
    private ProgrammeSnapshot? _programme;
    public ProgrammeSnapshot? CaptureProgramme() => _programme is null ? null : _programme with { ActIds = _programme.ActIds.ToArray(), Performers = _programme.Performers.ToArray() };
    internal string? ProgrammeCanonicalJson => _programme is null ? null : JsonSerializer.Serialize(_programme);
    public static readonly int[] FestivalSlotStarts = [1200, 9200, 17200];
    public static readonly int[] FestivalSlotEnds = [7200, 15200, 23200];
    public const int FestivalSlotDurationTicks = 6000;
    public IReadOnlyList<FestivalAct> GetFestivalActs() => _programme is null ? [] : FestivalActs;
    private static readonly FestivalAct[] FestivalActs = [
        new("act.meadow-lanterns", "Meadow Lanterns", 0, 4000, 40),
        new("act.orchard-chorus", "Orchard Chorus", 0, 7500, 70),
        new("act.barnstorm-circuit", "Barnstorm Circuit", 1, 5500, 55),
        new("act.copper-static", "Copper Static", 1, 9000, 80),
        new("act.neon-postcards", "Neon Postcards", 2, 11000, 90),
        new("act.field-frequency", "Field Frequency", 3, 8500, 75)];
    public FestivalAct? CurrentFestivalAct => _programme is { CurrentSlot: >= 0 and < 3 } q && q.ActIds.Length == 3 ? FestivalActs.Single(a => a.Id == q.ActIds[q.CurrentSlot]) : null;
    private int UpcomingProgrammeSlot => _programme is not { } q ? -1 : q.CurrentSlot < 0 ? 0 : _livePerformance?.Stage == LiveSetStage.BeforeSet ? q.CurrentSlot : q.CurrentSlot < 2 ? q.CurrentSlot + 1 : -1;
    public FestivalAct? UpcomingFestivalAct => _programme is { ActIds.Length: 3 } q && UpcomingProgrammeSlot is >= 0 and < 3 ? FestivalActs.Single(a => a.Id == q.ActIds[UpcomingProgrammeSlot]) : null;
    public long UpcomingFestivalTick => UpcomingProgrammeSlot is >= 0 and < 3 && _preparation is { } p ? p.StartedTick + FestivalSlotStarts[UpcomingProgrammeSlot] : -1;
    public bool ScheduledSilence => _programme is not null && _livePerformance?.Stage is not (LiveSetStage.Live or LiveSetStage.Interrupted);
    public long LateReadyScheduledTick
    {
        get
        {
            if (_programme is not { ActIds.Length: 3 } q || _preparation is not { Status: PreparationStatus.Running } p || _livePerformance is not { } live) return -1;
            if (live.Stage == LiveSetStage.BeforeSet && !live.Performers.All(person => person.OnStage) && CurrentTick >= live.PlannedTick && CurrentTick < q.SlotEndTick) return live.PlannedTick;
            if (live.Stage == LiveSetStage.Finished)
                for (var slot = q.CurrentSlot + 1; slot < 3; slot++)
                    if (CurrentTick >= p.StartedTick + FestivalSlotStarts[slot] && CurrentTick < p.StartedTick + FestivalSlotEnds[slot]) return p.StartedTick + FestivalSlotStarts[slot];
            return -1;
        }
    }
    public bool FestivalBandLate => LateReadyScheduledTick >= 0;
    public FestivalAct? LateReadyFestivalAct
    {
        get
        {
            var tick = LateReadyScheduledTick;
            if (tick < 0 || _programme is not { } q || _preparation is not { } p) return null;
            var slot = Array.IndexOf(FestivalSlotStarts, (int)(tick - p.StartedTick));
            return slot < 0 ? null : FestivalActs.Single(act => act.Id == q.ActIds[slot]);
        }
    }
    public bool IsCurrentProgrammePerformer(ulong id) => _programme is { CurrentSlot: >= 0 } q && q.Performers.Any(p => p.AgentId == id && p.SlotIndex == q.CurrentSlot) && _livePerformance?.Stage != LiveSetStage.Finished;
    public int FestivalAffinity(ulong id, FestivalAct act)
    {
        var main = _preparation!.People.Single(p => p.AgentId == id).ExpectedGenre;
        return main == act.Genre ? 80 + (int)(id % 21) : 15 + (int)((id * 37 + (ulong)act.Genre * 17 + CampaignSeed) % 56);
    }
    public static GameSession CreateTimetableCampaign(ulong seed)
    {
        var session = CreateDisorderCampaign(seed);
        var p = session._preparation!;
        var people = p.People.Select(person => person.Role == ProtectedPersonRole.Guest ? person with { ExpectedGenre = (int)((person.AgentId * 17 + seed) % 4) } : person).ToList();
        string[] names = ["Robin Shaw", "Ellis Brook", "Taylor Finch", "Ash Dale", "Rowan Lake", "Sky Morgan"];
        foreach (var name in names)
        {
            var id = session.NextEntityId++;
            session._wallets.Add(new(id), new WalletState { OwnerId = new(id), CashPennies = 500 });
            people.Add(new(id, name, ProtectedPersonRole.Performer, 0));
            session._medical = session._medical! with { Needs = session._medical.Needs.Append(new MedicalNeed(id, 2500, 2500, MedicalIntent.WatchShow, "Awaiting set; water and rest available", -MedicalDecisionCooldownTicks, null, -1, MedicalNeedProfile.Performer)).ToArray() };
        }
        session._preparation = p with { People = people.OrderBy(person => person.AgentId).ToArray() };
        session._medical = session._medical! with { Needs = session._medical.Needs.Select(need => need.Profile == MedicalNeedProfile.Performer ? need with { Thirst = 2500, HeatExposure = 2500 } : need).OrderBy(need => need.AgentId).ToArray() };
        session._programme = new(3, [], people.Where(person => person.Role == ProtectedPersonRole.Performer).Select((person, i) => new ProgrammePerformer(person.AgentId, i / 3, i % 3)).ToArray(), -1, -1, "Choose three acts");
        return session;
    }
    private CommandResult? ValidateProgramme(EntityId? target, SetProgrammeCommand command)
    {
        if (target is not null || _programme is null || _preparation is not { Status: PreparationStatus.Preparing } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Programme is editable before opening only.");
        if (command.ActIds is null || command.ActIds.Length != 3 || command.ActIds.Distinct().Count() != 3 || command.ActIds.Any(id => !FestivalActs.Any(a => a.Id == id)))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Choose three distinct festival acts.");
        if (_programme.ActIds.Length > 0 && !_programme.ActIds.Order().SequenceEqual(command.ActIds.Order()))
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "Paid acts can be reordered but not replaced.");
        if (_programme.ActIds.Length == 0 && _festivalFinances[new(p.FinanceOwnerId)].CashPennies < command.ActIds.Sum(id => FestivalActs.Single(a => a.Id == id).PricePennies))
            return CommandResult.Rejected(CommandReasonCode.InsufficientFunds, "Insufficient cash for the three-act programme.");
        return null;
    }
    private void ApplyProgramme(SetProgrammeCommand command)
    {
        if (_programme!.ActIds.Length == 0)
            foreach (var id in command.ActIds) ApplyPreparationOffer(new(id));
        _programme = _programme with { ActIds = command.ActIds.ToArray(), Status = "Programme booked" };
    }
    private void AdvanceProgramme()
    {
        if (_programme is not { } q || _preparation is not { Status: PreparationStatus.Running } p || _livePerformance is not { } live) return;
        if (live.Stage != LiveSetStage.Finished)
        {
            var ready = live.Performers.All(person => person.OnStage);
            _programme = q with { Status = live.Stage == LiveSetStage.Live ? "Playing" : live.Stage == LiveSetStage.Interrupted ? (_equipment?.Stage is EquipmentStage.Isolated or EquipmentStage.Terminal ? "Power interrupted" : "Paused: performer away from stage marks") : CurrentTick >= live.PlannedTick && !ready ? "Late: performers not on marks" : "Performers approaching stage" };
            return;
        }
        if (q.CurrentSlot >= 2) { _programme = q with { Status = "Final set finished; performers remain on farm" }; return; }
        var due = p.StartedTick + FestivalSlotStarts[q.CurrentSlot + 1] - LiveSetStageEntryLeadTicks;
        var clear = live.Performers.All(person => _navigationAgents[new(person.AgentId)] is { } nav && !ProgrammeStageAccessOccupied(nav));
        _programme = q with { Status = clear ? "Scheduled changeover" : "Changeover: outgoing performers clearing stairs" };
        if (CurrentTick < due || !clear) return;
        _programme = _programme with { CurrentSlot = q.CurrentSlot + 1 };
        StartLivePerformance();
    }
    private static bool ProgrammeStageAccessOccupied(NavigationAgentState nav)
    {
        var cell = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        return cell.X is >= 91 and <= 101 && cell.Z is >= 140 and <= 159;
    }
    private static string? ValidatePersistedProgramme(SessionPersistenceSnapshot snapshot)
    {
        if (snapshot.Programme is not { } q) return null;
        if (q.Version != 3) return q.Version switch
        {
            1 => "Unsupported festival programme version: the earlier 480-second timetable requires its matching build; no silent timing migration is available.",
            2 => "Unsupported festival programme version: the earlier 160-second timetable requires its matching build; no silent timing migration is available.",
            _ => "Unsupported festival programme version; load it with its matching build."
        };
        if (snapshot.Preparation is not { Tier: 1 } p || p.People is null || p.People.Any(person => person is null) || p.AcceptedOffers is null || snapshot.Disorder is null || q.ActIds is null || q.Performers is null || q.Performers.Any(role => role is null) ||
            q.ActIds.Length is not (0 or 3) || q.ActIds.Distinct().Count() != q.ActIds.Length || q.ActIds.Any(id => !FestivalActs.Any(a => a.Id == id)) ||
            q.Performers.Length != 9 || q.CurrentSlot is < -1 or > 2 || string.IsNullOrWhiteSpace(q.Status) ||
            q.Performers.Where((role, index) => role.SlotIndex != index / 3 || role.RoleIndex != index % 3).Any() ||
            !q.Performers.Select(role => role.AgentId).SequenceEqual(p.People.Where(person => person.Role == ProtectedPersonRole.Performer).Select(person => person.AgentId)) ||
            p.Status == PreparationStatus.Preparing && (q.CurrentSlot != -1 || q.SlotEndTick != -1 || snapshot.LivePerformance is not null) ||
            p.Status != PreparationStatus.Preparing && (q.ActIds.Length != 3 || q.CurrentSlot < 0 || q.SlotEndTick != p.StartedTick + FestivalSlotEnds[q.CurrentSlot]) ||
            p.Status is PreparationStatus.Departing or PreparationStatus.Finished && snapshot.CurrentTick < p.StartedTick + PreparedDayTicks ||
            q.ActIds.Length == 3 && !q.ActIds.Order().SequenceEqual(p.AcceptedOffers.Where(id => id.StartsWith("act.")).Order()) ||
            q.CurrentSlot > 0 && snapshot.CurrentTick < p.StartedTick + FestivalSlotStarts[q.CurrentSlot] - LiveSetStageEntryLeadTicks)
            return "Festival programme identity, booking or fixed window invalid.";
        return null;
    }
}
