using System.Text.Json;

namespace Festival.Simulation;

public sealed record FestivalAct(string Id, string Name, int Genre, int PricePennies, int Popularity, int Ego = 0, int Professionalism = 0);
public sealed record ProgrammePerformer(ulong AgentId, int SlotIndex, int RoleIndex);
/// <summary>One stage's running order: its booked acts, its bands' members by slot, and how far through the day it is.</summary>
public sealed record StageProgrammeSnapshot(string StageId, string[] ActIds, ProgrammePerformer[] Performers, int CurrentSlot, long SlotEndTick, string Status);
/// <summary>Every stage's running order, in the stage catalogue's order.</summary>
public sealed record ProgrammeSnapshot(int Version, StageProgrammeSnapshot[] Stages);
/// <summary>Books the main stage's three sets.</summary>
public sealed record SetProgrammeCommand(string[] ActIds) : SessionCommand;

public sealed partial class GameSession
{
    // Version 5 keeps a running order for each stage.
    public const int ProgrammeVersion = 5;
    private ProgrammeSnapshot? _programme;
    /// <summary>The main stage's running order, as the booking sheet and stage panel show it.</summary>
    public StageProgrammeSnapshot? CaptureProgramme() => CaptureProgramme(FestivalStages.MainId);
    public StageProgrammeSnapshot? CaptureProgramme(string stageId) =>
        FestivalStages.IndexOf(stageId) is var stage and >= 0 && StageProgramme(stage) is { } q ? CopyOf(q) : null;
    /// <summary>Every stage's running order.</summary>
    public ProgrammeSnapshot? CaptureProgrammes() => _programme is null ? null : _programme with { Stages = _programme.Stages.Select(CopyOf).ToArray() };
    private static StageProgrammeSnapshot CopyOf(StageProgrammeSnapshot q) => q with { ActIds = q.ActIds.ToArray(), Performers = q.Performers.ToArray() };
    internal string? ProgrammeCanonicalJson => _programme is null ? null : JsonSerializer.Serialize(_programme);
    private StageProgrammeSnapshot? StageProgramme(int stage) => _programme?.Stages[stage];
    // Booking, the act offer and the preparation plan only know the main stage so far.
    private StageProgrammeSnapshot? MainProgramme => StageProgramme(0);
    private void SetStageProgramme(int stage, StageProgrammeSnapshot q)
    {
        var stages = _programme!.Stages.ToArray();
        stages[stage] = q;
        _programme = _programme with { Stages = stages };
    }
    /// <summary>The stage a band member plays on, or -1 for anyone else.</summary>
    private int PerformerStage(ulong id)
    {
        for (var stage = 0; stage < (_programme?.Stages.Length ?? 0); stage++)
            if (_programme!.Stages[stage].Performers.Any(role => role.AgentId == id)) return stage;
        return -1;
    }
    /// <summary>Every stage's booked acts in stage order, or null until every stage is fully booked.</summary>
    private static string[]? BookedActIds(ProgrammeSnapshot? programme) =>
        programme is { } q && q.Stages.Select((stage, index) => stage.ActIds.Length == FestivalStages.All[index].SlotCount).All(booked => booked)
            ? q.Stages.SelectMany(stage => stage.ActIds).ToArray() : null;
    /// <summary>The stage a band member comes in for and waits behind; the main stage for anyone not booked to one.</summary>
    private FestivalStage BandStage(ulong id) => FestivalStages.All[Math.Max(0, PerformerStage(id))];
    /// <summary>The main stage's set times, for callers that only know the one stage.</summary>
    public static readonly int[] FestivalSlotStarts = [.. FestivalStages.Main.SlotStarts];
    public static readonly int[] FestivalSlotEnds = [.. FestivalStages.Main.SlotEnds];
    // The headliner is 120 seconds; the first two sets are 105 seconds.
    public const int FestivalSlotDurationTicks = 9_600;
    /// <summary>This run's offer: acts who will play for the festival now, then a few just out of reach.</summary>
    public IReadOnlyList<FestivalAct> GetFestivalActs() => MainProgramme is not { } main ? [] :
        ActCatalogue.Offer(Standing, CampaignSeed, _preparation?.Tier ?? 1, _preparation?.Plan?.ActIds ?? main.ActIds);
    private static FestivalAct[] FestivalActs => ActCatalogue.All;
    /// <summary>The festival's reputation and scene credibility.</summary>
    public FestivalStanding Standing => _preparation is { } p ? new(p.Reputation, p.SceneCredibility.ToArray()) : FestivalStanding.New;
    public ActStanding ActStandingOf(FestivalAct act) => ActCatalogue.StandingOf(Standing, act);
    /// <summary>What this festival pays the act, including the stretch-booking premium.</summary>
    public int ActFee(FestivalAct act) => ActCatalogue.Fee(Standing, act);
    public int ActReputationNeeded(FestivalAct act) => ActCatalogue.ReputationNeeded(Standing, act);
    public int TicketPricePennies => FestivalTickets.PricePennies(_preparation?.Tier ?? 1);
    /// <summary>The act popularity this ticket price leads guests to expect.</summary>
    public int ExpectedPopularity => ActCatalogue.ExpectedPopularity(TicketPricePennies);
    /// <summary>
    /// A rebuilt baseline priced as this preparation paid: its standing before any completed festival
    /// changed it, so act fees (and stretch premiums) match the recorded payments.
    /// </summary>
    private GameSession WithPaymentStanding(PreparationSnapshot p)
    {
        var standing = p.StandingBefore ?? new(p.Reputation, p.SceneCredibility);
        PreparationView = PreparationView! with { Reputation = standing.Reputation, SceneCredibility = standing.SceneCredibility.ToArray() };
        return this;
    }
    private bool ActWillPlay(string id) => ActCatalogue.Find(id) is { } act && ActStandingOf(act) != ActStanding.Locked;
    /// <summary>The act in a stage's current slot, once its sets are booked.</summary>
    private FestivalAct? StageAct(int stage) => StageProgramme(stage) is { CurrentSlot: >= 0 } q && q.CurrentSlot < FestivalStages.All[stage].SlotCount &&
        q.ActIds.Length == FestivalStages.All[stage].SlotCount ? FestivalActs.Single(a => a.Id == q.ActIds[q.CurrentSlot]) : null;
    /// <summary>The main stage's act now.</summary>
    public FestivalAct? CurrentFestivalAct => StageAct(0);
    private int UpcomingProgrammeSlot(int stage) => StageProgramme(stage) is not { } q ? -1 : q.CurrentSlot < 0 ? 0 :
        _livePerformances[stage]?.Stage == LiveSetStage.BeforeSet ? q.CurrentSlot : q.CurrentSlot < FestivalStages.All[stage].SlotCount - 1 ? q.CurrentSlot + 1 : -1;
    /// <summary>The main stage's next act, and when it's due.</summary>
    public FestivalAct? UpcomingFestivalAct => MainProgramme is { } q && q.ActIds.Length == FestivalStages.Main.SlotCount &&
        UpcomingProgrammeSlot(0) is var slot and >= 0 && slot < FestivalStages.Main.SlotCount ? FestivalActs.Single(a => a.Id == q.ActIds[slot]) : null;
    public long UpcomingFestivalTick => UpcomingProgrammeSlot(0) is var slot and >= 0 && slot < FestivalStages.Main.SlotCount && _preparation is { } p
        ? p.StartedTick + FestivalStages.Main.SlotStarts[slot] : -1;
    /// <summary>No stage is playing, or paused mid-set.</summary>
    public bool ScheduledSilence
    {
        get
        {
            foreach (var live in _livePerformances)
                if (live?.Stage is LiveSetStage.Live or LiveSetStage.Interrupted) return false;
            return true;
        }
    }
    /// <summary>When a stage's late set was due to start, or -1 while that stage is on time.</summary>
    private long LateReadyScheduledTickOf(int stage)
    {
        if (StageProgramme(stage) is not { } q || _preparation is not { Status: PreparationStatus.Running } p || _livePerformances[stage] is not { } live) return -1;
        var def = FestivalStages.All[stage];
        if (q.ActIds.Length != def.SlotCount) return -1;
        if (live.Stage == LiveSetStage.BeforeSet && !live.Performers.All(person => person.OnStage) && CurrentTick >= live.PlannedTick && CurrentTick < q.SlotEndTick) return live.PlannedTick;
        if (live.Stage == LiveSetStage.Finished)
            for (var slot = q.CurrentSlot + 1; slot < def.SlotCount; slot++)
                if (CurrentTick >= p.StartedTick + def.SlotStarts[slot] && CurrentTick < p.StartedTick + def.SlotEnds[slot]) return p.StartedTick + def.SlotStarts[slot];
        return -1;
    }
    /// <summary>The first stage, in catalogue order, whose band is late; -1 while every stage is on time.</summary>
    private int LateStage
    {
        get
        {
            for (var stage = 0; stage < FestivalStages.All.Count; stage++)
                if (LateReadyScheduledTickOf(stage) >= 0) return stage;
            return -1;
        }
    }
    public long LateReadyScheduledTick => LateStage is var stage and >= 0 ? LateReadyScheduledTickOf(stage) : -1;
    public bool FestivalBandLate => LateStage >= 0;
    public const int BandDelayRemarkGraceTicks = 400; // 5 seconds at 80 deterministic ticks/s, not wall-clock time.
    public const int BandStageProgressWindowTicks = 160; // a stalled approach becomes remark-eligible after 2 seconds.
    public bool BandDelayRemarkEligible
    {
        get
        {
            var stage = LateStage;
            if (stage < 0 || CurrentTick - LateReadyScheduledTickOf(stage) < BandDelayRemarkGraceTicks) return false;
            if (_livePerformances[stage] is not { Stage: LiveSetStage.BeforeSet } live) return true;
            var offstage = live.Performers.Where(person => !person.OnStage).ToArray();
            return offstage.Any(person =>
                MedicalOwnsNavigation(person.AgentId) || InterventionOwnsTarget(person.AgentId) || InterventionOwnsWorker(person.AgentId) ||
                !_navigationAgents.TryGetValue(new(person.AgentId), out var nav) ||
                nav.Action != AgentNavigationAction.Travelling || !IsStageApproachRoute(person, nav) ||
                person.LastStageProgressTick is not { } progress || CurrentTick - progress > BandStageProgressWindowTicks);
        }
    }
    public FestivalAct? LateReadyFestivalAct
    {
        get
        {
            var stage = LateStage;
            if (stage < 0 || StageProgramme(stage) is not { } q || _preparation is not { } p) return null;
            var due = (int)(LateReadyScheduledTickOf(stage) - p.StartedTick);
            var def = FestivalStages.All[stage];
            for (var slot = 0; slot < def.SlotCount; slot++)
                if (def.SlotStarts[slot] == due) return FestivalActs.Single(act => act.Id == q.ActIds[slot]);
            return null;
        }
    }
    /// <summary>Whether this person is in a band due on, or playing, any stage now.</summary>
    public bool IsCurrentProgrammePerformer(ulong id)
    {
        for (var stage = 0; stage < (_programme?.Stages.Length ?? 0); stage++)
            if (_programme!.Stages[stage] is { CurrentSlot: >= 0 } q && q.Performers.Any(p => p.AgentId == id && p.SlotIndex == q.CurrentSlot) &&
                _livePerformances[stage]?.Stage != LiveSetStage.Finished) return true;
        return false;
    }
    public int FestivalAffinity(ulong id, FestivalAct act)
    {
        var main = _persons[id].ExpectedGenre;
        return main == act.Genre ? 80 + (int)(id % 21) : 15 + (int)((id * 37 + (ulong)act.Genre * 17 + CampaignSeed) % 56);
    }
    private CommandResult? ValidateProgramme(EntityId? target, SetProgrammeCommand command)
    {
        if (target is not null || MainProgramme is not { } main || _preparation is not { Status: PreparationStatus.Preparing } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Programme is editable before opening only.");
        if (p.Plan is not null)
        {
            if (command.ActIds is not null && command.ActIds.FirstOrDefault(id => id != "" && ActCatalogue.Find(id) is not null && !ActWillPlay(id)) is { } locked)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, $"{ActCatalogue.Find(locked)!.Name} won't play for the festival yet.");
            return command.ActIds is not null && command.ActIds.Length is 0 or 3 && command.ActIds.Where(id => id != "").Distinct().Count() == command.ActIds.Count(id => id != "") && command.ActIds.All(id => id == "" || FestivalActs.Any(a => a.Id == id))
                ? null : CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Choose distinct acts for the three slots.");
        }
        if (command.ActIds is null || command.ActIds.Length != 3 || command.ActIds.Distinct().Count() != 3 || command.ActIds.Any(id => !FestivalActs.Any(a => a.Id == id) || !ActWillPlay(id)))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Choose three distinct festival acts.");
        if (main.ActIds.Length > 0 && !main.ActIds.Order().SequenceEqual(command.ActIds.Order()))
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "Paid acts can be reordered but not replaced.");
        if (main.ActIds.Length == 0 && _festivalFinances[new(p.FinanceOwnerId)].CashPennies < command.ActIds.Sum(id => ActFee(FestivalActs.Single(a => a.Id == id))))
            return CommandResult.Rejected(CommandReasonCode.InsufficientFunds, "Insufficient cash for the three-act programme.");
        return null;
    }
    private void ApplyProgramme(SetProgrammeCommand command)
    {
        if (_preparation?.Plan is { } plan)
        {
            _preparation = _preparation with { Plan = plan with { ActIds = command.ActIds.ToArray() } };
            return;
        }
        if (MainProgramme!.ActIds.Length == 0)
            foreach (var id in command.ActIds) ApplyPreparationOffer(new(id));
        SetStageProgramme(0, MainProgramme! with { ActIds = command.ActIds.ToArray(), Status = "Programme booked" });
    }
    private void AdvanceProgramme(int stage)
    {
        if (StageProgramme(stage) is not { } q || _preparation is not { Status: PreparationStatus.Running } p || _livePerformances[stage] is not { } live) return;
        var def = FestivalStages.All[stage];
        if (live.Stage != LiveSetStage.Finished)
        {
            var ready = live.Performers.All(person => person.OnStage);
            SetStageProgramme(stage, q with { Status = live.Stage == LiveSetStage.Live ? "Playing" : live.Stage == LiveSetStage.Interrupted ? (!StagePowered ? "Power interrupted" : "Paused: performer away from stage marks") : CurrentTick >= live.PlannedTick && !ready ? "Late: performers not on marks" : "Performers approaching stage" });
            return;
        }
        if (q.CurrentSlot >= def.SlotCount - 1) { SetStageProgramme(stage, q with { Status = "Final set finished; performers remain on farm" }); return; }
        var due = p.StartedTick + def.SlotStarts[q.CurrentSlot + 1] - LiveSetStageEntryLeadTicks;
        var clear = live.Performers.All(person => _navigationAgents[new(person.AgentId)] is { } nav && !ProgrammeStageAccessOccupied(def, nav));
        SetStageProgramme(stage, q with { Status = clear ? "Scheduled changeover" : "Changeover: outgoing performers clearing stairs" });
        if (CurrentTick < due || !clear) return;
        SetStageProgramme(stage, StageProgramme(stage)! with { CurrentSlot = q.CurrentSlot + 1 });
        StartStagePerformance(stage);
    }
    private static bool ProgrammeStageAccessOccupied(FestivalStage stage, NavigationAgentState nav)
    {
        return stage.Access(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres));
    }
    private static string? ValidatePersistedProgramme(SessionPersistenceSnapshot snapshot)
    {
        if (snapshot.Programme is not { } programme) return null;
        if (programme.Version != ProgrammeVersion) return programme.Version switch
        {
            1 => "Unsupported festival programme version: the earlier 480-second timetable requires its matching build; no silent timing migration is available.",
            2 => "Unsupported festival programme version: the earlier 160-second timetable requires its matching build; no silent timing migration is available.",
            3 => "Unsupported festival programme version: the earlier 300-second timetable requires its matching build; no silent timing migration is available.",
            4 => "Unsupported festival programme version: the single-stage programme requires its matching build; no silent migration is available.",
            _ => "Unsupported festival programme version; load it with its matching build."
        };
        const string invalid = "Festival programme identity, booking or fixed window invalid.";
        if (snapshot.Preparation is not { Tier: >= 1 and <= HighestTier } p || p.People is null || p.People.Any(person => person is null) || p.AcceptedOffers is null || snapshot.Disorder is null ||
            programme.Stages is null || programme.Stages.Length != FestivalStages.All.Count || programme.Stages.Any(stage => stage is null) ||
            p.Status == PreparationStatus.Preparing && snapshot.LivePerformances is not null ||
            p.Status is PreparationStatus.Departing or PreparationStatus.Finished && snapshot.CurrentTick < p.StartedTick + PreparedDayTicks)
            return invalid;
        for (var stage = 0; stage < programme.Stages.Length; stage++)
        {
            var q = programme.Stages[stage];
            var def = FestivalStages.All[stage];
            var band = def.PerformerMarks.Count;
            if (q.StageId != def.Id || q.ActIds is null || q.Performers is null || q.Performers.Any(role => role is null) ||
                q.ActIds.Length != 0 && q.ActIds.Length != def.SlotCount || q.ActIds.Distinct().Count() != q.ActIds.Length || q.ActIds.Any(id => !FestivalActs.Any(a => a.Id == id)) ||
                q.Performers.Length != def.SlotCount * band || q.CurrentSlot < -1 || q.CurrentSlot >= def.SlotCount || string.IsNullOrWhiteSpace(q.Status) ||
                q.Performers.Where((role, index) => role.SlotIndex != index / band || role.RoleIndex != index % band).Any() ||
                p.Status == PreparationStatus.Preparing && (q.CurrentSlot != -1 || q.SlotEndTick != -1) ||
                p.Status != PreparationStatus.Preparing && (q.ActIds.Length != def.SlotCount || q.CurrentSlot < 0 || q.SlotEndTick != p.StartedTick + def.SlotEnds[q.CurrentSlot]) ||
                q.CurrentSlot > 0 && snapshot.CurrentTick < p.StartedTick + def.SlotStarts[q.CurrentSlot] - LiveSetStageEntryLeadTicks)
                return invalid;
        }
        // Every band member plays on exactly one stage, and no act is booked twice across stages.
        var booked = programme.Stages.SelectMany(q => q.ActIds).ToArray();
        if (!programme.Stages.SelectMany(q => q.Performers).Select(role => role.AgentId)
                .SequenceEqual(p.People.Where(person => person.Role == ProtectedPersonRole.Performer).Select(person => person.AgentId)) ||
            booked.Distinct().Count() != booked.Length ||
            booked.Length > 0 && !booked.Order().SequenceEqual(p.AcceptedOffers.Where(id => id.StartsWith("act.")).Order()))
            return invalid;
        return null;
    }
}
