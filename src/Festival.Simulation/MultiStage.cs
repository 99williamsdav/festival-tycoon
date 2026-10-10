using System.Text.Json;

namespace Festival.Simulation;

/// <summary>
/// A second stage's own generator: a fixed hire that runs that stage's rig and nothing else. It strains, warns and
/// faults by the farm diesel's power-budget rules (see <see cref="PowerRules"/>), and can be cut off the same way.
/// </summary>
public sealed record StageGeneratorSnapshot(int Version, string StageId, int XMillimetres, int ZMillimetres, int Capacity, int Strain,
    EquipmentStage Stage, long WarningTick, string Response);
public enum StageGeneratorAction { Isolate }
/// <summary>Cuts a second stage's power at its own generator, as the emergency cutoff does for the trailer stage.</summary>
public sealed record StageGeneratorCommand(string StageId, StageGeneratorAction Action) : SessionCommand;

public static class StageGeneratorRules
{
    public const int Version = 1;
    /// <summary>
    /// The Pond Stage's hired generator. The rig hire covers both stages, so the basic PA (50) and the standard rig (65)
    /// sit inside it; the pro rig (80) strains it while a pond set plays.
    /// </summary>
    public const int PondCapacity = 70;
}

public sealed partial class GameSession
{
    // How far ahead a guest weighs each stage's music, how much better another stage must be before they walk over,
    // and how long they give a stage once they've arrived for it.
    public const int StageChoiceHorizonTicks = 9_600;
    public const int StageSwitchMarginPercent = 25;
    public const long StageSwitchMarginValue = 2_500L * 800;
    public const int StageSwitchDwellTicks = 1_600;

    private bool _pondStageTrial;
    /// <summary>Set when the campaign was created to try the Pond Stage before Tier 2.</summary>
    public bool PondStageTrial => _pondStageTrial;
    /// <summary>The Pond Stage comes with Tier 2 (and its own generator); a trial campaign has it from the start.</summary>
    public const int PondStageFromTier = 2;
    public bool PondStageOpen => _pondStageTrial || (_preparation?.Tier ?? 1) >= PondStageFromTier;

    // ---- Bands off the lane ----

    // The trailer's bands set off from the lane as the gates open. Every other stage's bands follow through the same
    // garden gate once those have gone, a band at a time and each member a few seconds after the last.
    public const int BandReleaseStartTicks = 1_600, BandReleaseBandGapTicks = 960, BandReleaseMemberGapTicks = 240;
    // Spare time a later stage's band keeps in hand: it reaches the foot of its stair this long before it's called up.
    public const int BandArrivalSlackTicks = 1_600;

    /// <summary>
    /// How long after opening a band member leaves the lane: at once for the trailer's bands; for any other stage's, by
    /// the stagger above, but never so late that the walk (straight-line distance, half again for the way round, at a
    /// slow walker's pace) and the slack wouldn't get them to the foot of the stair before their set's call.
    /// </summary>
    public int BandReleaseTicks(ulong id)
    {
        if (_programme is null || PerformerStage(id) is not (var stage and > 0)) return 0;
        var def = Stages[stage];
        var q = StageProgramme(stage)!;
        var ordinal = Array.FindIndex(q.Performers, role => role.AgentId == id);
        var role = q.Performers[ordinal];
        var planned = BandReleaseStartTicks + ((stage - 1) * def.SlotCount + role.SlotIndex) * BandReleaseBandGapTicks + role.RoleIndex * BandReleaseMemberGapTicks;
        var latest = def.SlotStarts[role.SlotIndex] - LiveSetStageEntryLeadTicks - BandArrivalSlackTicks - BandWalkTicks(def.ArrivalStart(ordinal), def.AccessCells[role.RoleIndex]);
        return Math.Max(1, Math.Min(planned, latest));
    }

    /// <summary>A generous walk estimate: octile distance, half again for the way round, at 24 mm a tick.</summary>
    public static int BandWalkTicks(GridCell from, GridCell to)
    {
        var dx = Math.Abs(from.X - to.X); var dz = Math.Abs(from.Z - to.Z);
        var millimetres = ((long)Math.Max(dx, dz) * 1_000 + (long)Math.Min(dx, dz) * 414) * TraversalGrid.CellSizeMillimetres / 1_000;
        return (int)(millimetres * 3 / 2 / 24);
    }

    /// <summary>A band member still standing on the lane, waiting for their time to come in.</summary>
    private bool WaitingOnTheLane(ulong id) =>
        _preparation is { Status: PreparationStatus.Running } && PersonIn(PersonView.Roster, id) is { Role: ProtectedPersonRole.Performer, Admitted: false } &&
        _navigationAgents.TryGetValue(new(id), out var nav) && nav.Destination is null && BandReleaseTicks(id) > 0;

    /// <summary>A band member leaves the lane: to the foot of the stair if they're on now, otherwise to the waiting ground.</summary>
    private void LeaveTheLane(ulong id, int index)
    {
        if (LivePerformerStage(id) is var stage and >= 0 && _livePerformances[stage] is { Stage: not LiveSetStage.Finished } live && IsCurrentProgrammePerformer(id))
            ApplyAgentDestination(new(id), new(live.Performers.Single(item => item.AgentId == id).AccessCell, "performance.side-entry"));
        else ApplyAgentDestination(new(id), new(IdlePlace(index), "edition.arrival"));
    }
    /// <summary>The stages this festival runs, in stage order.</summary>
    public IReadOnlyList<FestivalStage> Stages => FestivalStages.For(PondStageOpen);
    private static IReadOnlyList<FestivalStage> SavedStages(SessionPersistenceSnapshot s) =>
        FestivalStages.For(s.PondStageTrial || (s.Preparation?.Tier ?? 1) >= PondStageFromTier);

    // ---- Each stage's power and sound ----

    // One generator for each stage after the trailer's, in stage order; null without the power budget.
    private StageGeneratorSnapshot[]? _stageGenerators;
    public IReadOnlyList<StageGeneratorSnapshot> CaptureStageGenerators() => _stageGenerators?.ToArray() ?? [];
    public StageGeneratorSnapshot? CaptureStageGenerator(string stageId) => _stageGenerators?.FirstOrDefault(item => item.StageId == stageId);
    internal IEnumerable<string> StageGeneratorCanonicalJson() => (_stageGenerators ?? []).Select(item => JsonSerializer.Serialize(item));
    private StageGeneratorSnapshot? StageGenerator(int stage) => stage > 0 && _stageGenerators is { } all && stage - 1 < all.Length ? all[stage - 1] : null;
    private void SetStageGenerator(int stage, StageGeneratorSnapshot generator)
    {
        var all = _stageGenerators!.ToArray();
        all[stage - 1] = generator;
        _stageGenerators = all;
    }
    private static StageGeneratorSnapshot NewPondGenerator() => new(StageGeneratorRules.Version, FestivalStages.PondId,
        PondRiser.GeneratorXMillimetres, PondRiser.GeneratorZMillimetres, StageGeneratorRules.PondCapacity, 0, EquipmentStage.Resolved, -1,
        "Pond generator running within capacity");

    /// <summary>Whether a stage's rig has power: the trailer's from the farm generator, any other from its own.</summary>
    public bool StagePoweredAt(int stage) => stage == 0 ? StagePowered :
        StageGenerator(stage)?.Stage is not (EquipmentStage.Isolated or EquipmentStage.Terminal);
    public bool StagePoweredAt(string stageId) => FestivalStages.IndexOf(Stages, stageId) is var stage and >= 0 && StagePoweredAt(stage);

    /// <summary>The sound reaching a stage's crowd: the rig, that stage's engineer, less a straining generator.</summary>
    public int SoundScoreAt(int stage) => stage == 0 ? SoundScore :
        Math.Clamp(PerformanceRules.RigSound(Rig) + SoundMixingBonusAt(stage) * PerformanceRules.MixingStep -
            (StageGenerator(stage)?.Stage switch { EquipmentStage.Warning => PerformanceRules.StrainedSoundPenalty, EquipmentStage.DangerousFault => PerformanceRules.FaultSoundPenalty, _ => 0 }), 0, 100);
    public int SoundScoreAt(string stageId) => FestivalStages.IndexOf(Stages, stageId) is var stage and >= 0 ? SoundScoreAt(stage) : 0;

    /// <summary>How a stage's set is going, or nothing between sets.</summary>
    public SetPerformance? CurrentStagePerformance(string stageId) => FestivalStages.IndexOf(Stages, stageId) is var stage and >= 0 ? StagePerformance(stage) : null;

    /// <summary>The slot a stage's own sound engineer fills: the trailer's is the first staff hire, the rest are named by the programme.</summary>
    private ulong? StageEngineerId(int stage) => stage == 0 ? StaffSlotId(StaffRole.Sound) : StageProgramme(stage)?.EngineerId;
    public ulong? StageEngineerId(string stageId) => FestivalStages.IndexOf(Stages, stageId) is var stage and >= 0 ? StageEngineerId(stage) : null;
    /// <summary>The stage whose own sound engineer this person is, after the trailer's; -1 for anyone else.</summary>
    private int EngineerStage(ulong id)
    {
        for (var stage = 1; stage < (_programme?.Stages.Length ?? 0); stage++)
            if (_programme!.Stages[stage].EngineerId == id) return stage;
        return -1;
    }

    /// <summary>A stage's engineer's mix, dulled by drink.</summary>
    private int SoundMixingBonusAt(int stage)
    {
        if (stage == 0) return SoundMixingBonus();
        if (StageEngineerId(stage) is not { } id || HiredCandidateFor(id) is not { } engineer) return 0;
        return engineer.MixingBonus - (PersonIn(PersonView.Consumption, id)?.Intoxication ?? 0) / 2_500;
    }

    /// <summary>
    /// The supply a stage after the trailer's draws on now (before opening, the planned peak): with the generators pooled,
    /// the whole pool and everything on it.
    /// </summary>
    public PowerDraw? CaptureStagePower(string stageId) => CaptureStagePower(stageId, CurrentTick);
    private PowerDraw? CaptureStagePower(string stageId, long tick)
    {
        var stage = FestivalStages.IndexOf(Stages, stageId);
        if (stage <= 0 || StageGenerator(stage) is not { } generator) return null;
        return PowerPooled ? CapturePower(tick) : new(StageRigDraw(stage), 0, 0, 0, generator.Capacity);
    }

    /// <summary>A later stage's rig: its full draw while its set plays (or planned, before opening), standby between sets.</summary>
    private int StageRigDraw(int stage)
    {
        var live = _preparation?.Status is PreparationStatus.Running or PreparationStatus.Departing or PreparationStatus.Failed or PreparationStatus.Finished;
        return !StagePoweredAt(stage) ? 0 : !live || _livePerformances[stage]?.Stage == LiveSetStage.Live ? PowerRules.RigDraw(Rig) : PowerRules.RigStandbyDraw;
    }

    /// <summary>One tick of each second stage's generator: strain builds over capacity and eases under it, as the farm diesel's does.</summary>
    private void AdvanceStageGenerators()
    {
        if (_stageGenerators is null || _preparation is not { Status: PreparationStatus.Running }) return;
        for (var stage = 1; stage < Stages.Count; stage++)
        {
            var g = StageGenerator(stage)!;
            var draw = CaptureStagePower(g.StageId)!;
            var over = Overage(draw, !PowerPooled || StageGeneratorInPool(g));
            // Pooled, the farm generator (advanced first) holds the one strain, and every generator in the pool takes it.
            var strain = PowerPooled && FarmGeneratorInPool && StageGeneratorInPool(g) ? _equipment!.Strain
                : over > 0 ? Math.Min(PowerRules.StrainMaximum, g.Strain + over) : Math.Max(0, g.Strain - PowerRules.StrainRecoveryPerTick);
            g = g with { Strain = strain };
            var name = Stages[stage].Name;
            switch (g.Stage)
            {
                case EquipmentStage.Normal or EquipmentStage.Resolved when strain >= PowerRules.StrainWarning:
                    g = g with { Stage = EquipmentStage.Warning, WarningTick = CurrentTick, Response = PowerPooled
                        ? $"{name} generator overload: the pooled supply is drawing {draw.Total} of {draw.Capacity}. Switch off stalls or lights, or cut a stage's rig, before it faults."
                        : $"{name} generator overload: drawing {draw.Total} of {draw.Capacity}. Cut the stage or bring the load back under capacity before it faults." };
                    break;
                case EquipmentStage.Warning or EquipmentStage.DangerousFault when strain == 0:
                    g = g with { Stage = EquipmentStage.Resolved, Response = "Load back within capacity; the generator settled" };
                    break;
                case EquipmentStage.Warning when CurrentTick >= g.WarningTick + EquipmentDangerDelayTicks:
                    g = g with { Stage = EquipmentStage.DangerousFault, Response = $"Dangerous {name} generator fault: still drawing {draw.Total} of {draw.Capacity}." };
                    break;
            }
            SetStageGenerator(stage, g);
        }
    }

    /// <summary>Whether next tick moves any second stage's generator into the warning, a fault or back to settled.</summary>
    private bool StageGeneratorBoundaryOnNextTick
    {
        get
        {
            if (_stageGenerators is null || _preparation is not { Status: PreparationStatus.Running }) return false;
            for (var stage = 1; stage < Stages.Count; stage++)
            {
                var g = StageGenerator(stage)!;
                var draw = CaptureStagePower(g.StageId, CurrentTick + 1)!;
                var over = Overage(draw, !PowerPooled || StageGeneratorInPool(g));
                var next = over > 0 ? Math.Min(PowerRules.StrainMaximum, g.Strain + over) : Math.Max(0, g.Strain - PowerRules.StrainRecoveryPerTick);
                if (g.Stage is EquipmentStage.Normal or EquipmentStage.Resolved && next >= PowerRules.StrainWarning ||
                    g.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault && next == 0 ||
                    g.Stage == EquipmentStage.Warning && CurrentTick + 1 >= g.WarningTick + EquipmentDangerDelayTicks) return true;
            }
            return false;
        }
    }

    private CommandResult? ValidateStageGeneratorCommand(EntityId? target, StageGeneratorCommand command)
    {
        var stage = FestivalStages.IndexOf(Stages, command.StageId ?? "");
        if (target is not null || _preparation?.Status != PreparationStatus.Running || !Enum.IsDefined(command.Action))
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "A stage's generator can be cut only while the festival runs.");
        if (stage <= 0 || StageGenerator(stage) is not { } generator)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "That stage has no generator of its own.");
        if (generator.Stage == EquipmentStage.Isolated)
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "That stage is already cut off.");
        return null;
    }

    private void ApplyStageGeneratorCommand(StageGeneratorCommand command)
    {
        var stage = FestivalStages.IndexOf(Stages, command.StageId);
        SetStageGenerator(stage, StageGenerator(stage)! with { Stage = EquipmentStage.Isolated, Response = "Emergency cutoff: stage power isolated" });
    }

    private static string? ValidatePersistedStageGenerators(SessionPersistenceSnapshot s)
    {
        var stages = SavedStages(s);
        const string invalid = "Stage generator identity, power or stage invalid.";
        if (s.PondStageTrial && (s.Programme is null || s.Preparation is null)) return "The Pond Stage trial needs a festival programme.";
        var expected = stages.Count > 1 && s.Equipment?.Version == 3;
        if (s.StageGenerators is not { } generators) return expected ? invalid : null;
        if (!expected || generators.Length != stages.Count - 1 || s.Preparation is not { } p) return invalid;
        for (var index = 0; index < generators.Length; index++)
        {
            var g = generators[index];
            if (g is null || g.Version != StageGeneratorRules.Version || g.StageId != stages[index + 1].Id ||
                g.XMillimetres != PondRiser.GeneratorXMillimetres || g.ZMillimetres != PondRiser.GeneratorZMillimetres ||
                g.Capacity != StageGeneratorRules.PondCapacity || g.Strain is < 0 or > PowerRules.StrainMaximum || string.IsNullOrWhiteSpace(g.Response) ||
                g.Stage is not (EquipmentStage.Resolved or EquipmentStage.Warning or EquipmentStage.DangerousFault or EquipmentStage.Isolated) ||
                g.WarningTick < -1 || g.WarningTick > s.CurrentTick ||
                g.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault && g.WarningTick < p.StartedTick ||
                g.Stage == EquipmentStage.DangerousFault && s.CurrentTick < g.WarningTick + EquipmentDangerDelayTicks ||
                p.Status == PreparationStatus.Preparing && (g.Stage != EquipmentStage.Resolved || g.Strain != 0 || g.WarningTick != -1))
                return invalid;
        }
        return null;
    }

    // ---- Which stage each guest listens at ----

    /// <summary>
    /// What a stage's act now is worth to a guest each second: the music appeal the activity chooser gives it (their
    /// taste for its genre and its popularity), scaled by how well the set is playing, as it scales what they enjoy.
    /// </summary>
    private long WantToSee(ulong id, int stage, FestivalAct act)
    {
        var appeal = 2_500L + FestivalAffinity(id, act) * 50 + act.Popularity * 10;
        var overall = _equipment?.Version == 3 && StagePerformance(stage) is { } set ? set.Overall : 50;
        return appeal * PerformanceRules.MusicPermille(overall) / 1_000;
    }

    /// <summary>
    /// A stage's music over the next two minutes, as a guest standing here would get it: nothing while they walk over or
    /// before the set starts, nothing from a finished or powerless stage.
    /// </summary>
    private long StageListeningValue(ulong id, int stage, GridCell here, GridCell? place)
    {
        if (_livePerformances[stage] is not { Stage: not LiveSetStage.Finished } live || StageAct(stage) is not { } act || !StagePoweredAt(stage)) return 0;
        var walk = EstimateWalkTicks(id, here, place ?? Stages[stage].AudienceCentre);
        if (walk == int.MaxValue) return 0;
        var from = Math.Max(CurrentTick + walk, live.PlannedTick);
        var to = CurrentTick + StageChoiceHorizonTicks;
        return to <= from ? 0 : WantToSee(id, stage, act) * (to - from);
    }

    /// <summary>
    /// Once a second, a quarter of the crowd (by id) weighs the stages. A guest who is watching, not walking to a place
    /// and has given their stage a little while moves to another stage when its music, walk included, beats theirs by a
    /// clear margin. Sound carries between the stages, but at these fixed places neither drowns the other out.
    /// </summary>
    private void AdvanceStageChoices()
    {
        if (Stages.Count < 2 || CurrentTick % 80 != 0 || _preparation is not { Status: PreparationStatus.Running }) return;
        var cohort = (ulong)(CurrentTick / 80 % 4);
        foreach (var person in PeopleIn(PersonView.Roster).ToArray())
        {
            if (person.Role != ProtectedPersonRole.Guest || person.Id % 4 != cohort || !person.Admitted || person.Departed ||
                AudienceNavigationOwned(person.Id) || CurrentActivity(_persons[person.Id]) != ActivityKind.Watch ||
                _persons[person.Id].Intent != MedicalIntent.WatchShow) continue;
            var from = LiveListenerStage(person.Id);
            if (from < 0) continue;
            var record = _livePerformances[from]!.Listeners.First(item => item.AgentId == person.Id);
            if (record.JoinedTick is { } joined && CurrentTick - joined < StageSwitchDwellTicks) continue;
            var nav = _navigationAgents[new(person.Id)];
            if (record.Place is { } held && (nav.Action != AgentNavigationAction.Arrived || nav.Destination != held)) continue;
            var here = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
            var values = new long[Stages.Count];
            // A movable stage would lose some of its pull here to the sound of a nearer one (a bleed penalty on each
            // value); these two fixed stages are far enough apart that neither does.
            for (var stage = 0; stage < Stages.Count; stage++)
                values[stage] = StageListeningValue(person.Id, stage, here, stage == from ? record.Place : null);
            if (ChooseStage(from, values) is var best && best != from) MoveListener(person.Id, from, best);
        }
    }

    /// <summary>
    /// The stage a guest settles on, given what each stage's music is worth to them from where they stand: their own
    /// unless another beats it by the margin, so near-equal stages don't have them walking back and forth.
    /// </summary>
    public static int ChooseStage(int current, IReadOnlyList<long> values)
    {
        var best = current;
        var bar = values[current] * (100 + StageSwitchMarginPercent) / 100 + StageSwitchMarginValue;
        for (var stage = 0; stage < values.Count; stage++)
            if (stage != current && values[stage] > bar) { best = stage; bar = values[stage]; }
        return best;
    }

    /// <summary>What a stage's act now is worth each second to a guest, by the stage choice's measure; 0 between sets.</summary>
    public long StageWantToSee(ulong guestId, string stageId) =>
        FestivalStages.IndexOf(Stages, stageId) is var stage and >= 0 && stage < _livePerformances.Length &&
        _livePerformances[stage] is { Stage: not LiveSetStage.Finished } && StageAct(stage) is { } act && _persons.TryGet(guestId, out _)
            ? WantToSee(guestId, stage, act) : 0;

    /// <summary>Takes a guest out of one stage's crowd and into another's, where they'll pick a place to stand.</summary>
    private void MoveListener(ulong id, int from, int to)
    {
        var left = _livePerformances[from]!;
        var record = left.Listeners.First(item => item.AgentId == id);
        // Someone who clapped a set out takes their share of its applause with them.
        var away = left.SetEndAudienceIds.Contains(id) ? record.EnjoymentEarned : 0;
        _livePerformances[from] = left with { Listeners = left.Listeners.Where(item => item.AgentId != id).ToArray(),
            SetEndEnjoymentAway = left.SetEndEnjoymentAway + away };
        var joined = _livePerformances[to]!;
        var act = StageAct(to);
        _livePerformances[to] = joined with { Listeners = joined.Listeners
            .Append(new LiveListener(id, null, act is null ? 0 : FestivalAffinity(id, act), 0, 0, false) { JoinedTick = CurrentTick })
            .OrderBy(item => item.AgentId).ToArray() };
        var reason = act is null ? $"Heading over to {Stages[to].Name}" : $"Heading over to {Stages[to].Name} for {act.Name}";
        MutatePerson(id, item => item.Reason = reason);
    }

    /// <summary>The stage whose crowd this guest is in, by stage id; null outside every crowd.</summary>
    public string? ListeningStageId(ulong id) => LiveListenerStage(id) is var stage and >= 0 ? Stages[stage].Id : null;
}
