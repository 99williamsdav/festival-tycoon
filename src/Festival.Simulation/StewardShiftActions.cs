namespace Festival.Simulation;

/// <summary>
/// The player steward's hands: hold the action key on something in reach and, if they stay in reach and it still
/// needs doing when the hold runs out, it's done. Each action reuses what the crew already does: the calm and the
/// fight's one handling attempt (keyed rolls against the player's own skills), the medic's dispatch, freeing a loo,
/// bodging a tap, emptying a bin, a sweep's pick-up, a bale of straw and a herded cow. Walking a guest out is the
/// safe egress, with the guest following the player to the gate.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Everything in reach and in front of the player steward: people, cows, and the toilets, taps, bars, bins, litter
    /// and mud with something to do. Those with an action come first, best action first, then nearest, then by id.
    /// <paramref name="facingDegrees"/> previews another facing (the camera's); the saved one otherwise. Data only.
    /// </summary>
    public IReadOnlyList<StewardReachTarget> StewardReach(int? facingDegrees = null, int reachMillimetres = StewardShiftRules.ReachMillimetres)
    {
        if (_stewardShift is not { PlayerId: { } id } shift || !_navigationAgents.TryGetValue(new(id), out var me)) return [];
        var (fx, fz) = StewardShiftRules.Facing(facingDegrees ?? shift.FacingDegrees);
        var found = new List<StewardReachTarget>();
        bool InFront(int x, int z, int reach, out int distance)
        {
            long dx = x - me.XMillimetres, dz = z - me.ZMillimetres;
            var length = IntegerSquareRoot(dx * dx + dz * dz);
            distance = (int)Math.Min(int.MaxValue, length);
            return length <= reach && fx * dx + fz * dz >= StewardShiftRules.ReachConeCosPermille * length;
        }
        void Add(string target, StewardTargetKind kind, string name, int x, int z, int reach, bool onlyWithAction, ulong? person = null, ProtectedPersonRole role = default)
        {
            if (!InFront(x, z, reach, out var distance)) return;
            var action = BestStewardAction(target);
            if (action is null && onlyWithAction) return;
            found.Add(new(target, kind, name, action, distance, person, role));
        }
        foreach (var person in PeopleIn(PersonView.Roster))
        {
            if (person.Id == id || !person.Admitted || person.Departed || GuestWaitingForRelease(person.Id) ||
                !_navigationAgents.TryGetValue(new(person.Id), out var nav)) continue;
            Add($"person:{person.Id}", StewardTargetKind.Person, person.Name, nav.XMillimetres, nav.ZMillimetres, reachMillimetres, false, person.Id, person.Role);
        }
        foreach (var cow in _cows?.Loose ?? [])
            Add($"cow:{cow.Id}", StewardTargetKind.Cow, "Loose cow", cow.XMillimetres, cow.ZMillimetres, reachMillimetres, false);
        foreach (var target in StewardFacilityTargets())
            if (StewardTargetPoint(target) is { } point) Add(target, point.Kind, point.Name, point.X, point.Z, reachMillimetres, true);
        var here = TraversalGrid.WorldToCell(me.XMillimetres, me.ZMillimetres);
        foreach (var piece in GroundNear(here, StewardShiftRules.LitterReachMillimetres / TraversalGrid.CellSizeMillimetres + 1))
            Add($"litter:{piece.Id}", StewardTargetKind.Litter, "Litter", piece.XMillimetres, piece.ZMillimetres,
                Math.Min(reachMillimetres, StewardShiftRules.LitterReachMillimetres), true);
        if (IsRainyFestival)
        {
            var ahead = TraversalGrid.WorldToCell(me.XMillimetres + fx * StewardShiftRules.MudAheadMillimetres / 1000,
                me.ZMillimetres + fz * StewardShiftRules.MudAheadMillimetres / 1000);
            var target = $"mud:{ahead.X}:{ahead.Z}";
            // Straight ahead is in front by definition; only its reach is checked.
            if (BestStewardAction(target) is { } spread)
            {
                var (cx, cz) = TraversalGrid.CellCentre(ahead);
                long dx = cx - me.XMillimetres, dz = cz - me.ZMillimetres;
                found.Add(new(target, StewardTargetKind.Mud, "Mud", spread, (int)IntegerSquareRoot(dx * dx + dz * dz)));
            }
        }
        return found.OrderBy(item => item.Action is { } action ? (int)action : int.MaxValue).ThenBy(item => item.DistanceMillimetres)
            .ThenBy(item => item.TargetId, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Facilities the player could have business with: every toilet, tap, bar and bin.</summary>
    private IEnumerable<string> StewardFacilityTargets()
    {
        foreach (var toilet in EffectiveToilets(_facilities)) yield return $"toilet:{toilet.Id}";
        foreach (var tap in WaterPoints()) yield return $"tap:{tap.Id}";
        foreach (var vendor in Vendors) if (Stalls.IsBar(vendor.Id)) yield return $"bar:{vendor.Id}";
        foreach (var bin in CaptureBins()) yield return $"bin:{bin.Id}";
    }

    private static (string Prefix, string Key) SplitTarget(string target)
    {
        var colon = target.IndexOf(':');
        return colon <= 0 ? ("", "") : (target[..colon], target[(colon + 1)..]);
    }

    /// <summary>Where a target is and what it's called; null once it's gone (left, removed, home).</summary>
    public (StewardTargetKind Kind, string Name, int X, int Z)? StewardTargetPoint(string target)
    {
        var (prefix, rest) = SplitTarget(target);
        switch (prefix)
        {
            case "person" when ulong.TryParse(rest, out var id) && $"person:{id}" == target && PersonIn(PersonView.Roster, id) is { Admitted: true, Departed: false } person &&
                                 _navigationAgents.TryGetValue(new(id), out var nav):
                return (StewardTargetKind.Person, person.Name, nav.XMillimetres, nav.ZMillimetres);
            case "toilet" when EffectiveToilets(_facilities).FirstOrDefault(t => t.Id == rest) is { } toilet:
            {
                var (x, z) = TraversalGrid.CellCentre(ToiletDoorFrontCell(toilet));
                return (StewardTargetKind.Toilet, "Portaloo", x, z);
            }
            case "tap" when WaterPoints().FirstOrDefault(t => t.Id == rest) is { } tap:
            {
                var (x, z) = TraversalGrid.CellCentre(tap.Cell);
                return (StewardTargetKind.Tap, "Water tap", x, z);
            }
            case "bar" when Vendors.FirstOrDefault(v => v.Id == rest && Stalls.IsBar(v.Id)) is { } bar:
            {
                // The counter's front edge.
                var front = RotateWaterOffset(new(0, 3), bar.QuarterTurns);
                var (x, z) = TraversalGrid.CellCentre(new(bar.Cell.X + front.X, bar.Cell.Z + front.Z));
                return (StewardTargetKind.Bar, Stalls.Label(bar.Id), x, z);
            }
            case "bin" when CaptureBins().FirstOrDefault(b => b.Id == rest) is { } bin:
            {
                var (x, z) = TraversalGrid.CellCentre(bin.Cell);
                return (StewardTargetKind.Bin, "Bin", x, z);
            }
            case "litter" when CaptureWaste(rest) is { Location: WasteLocation.Ground } piece:
                return (StewardTargetKind.Litter, "Litter", piece.XMillimetres, piece.ZMillimetres);
            case "cow" when _cows?.Loose.FirstOrDefault(c => c.Id == rest) is { } cow:
                return (StewardTargetKind.Cow, "Loose cow", cow.XMillimetres, cow.ZMillimetres);
            case "mud" when rest.Split(':') is [var sx, var sz] && int.TryParse(sx, out var cx) && int.TryParse(sz, out var cz) &&
                            GroundRules.InField(new(cx, cz)) && $"mud:{cx}:{cz}" == target:
            {
                var (x, z) = TraversalGrid.CellCentre(new(cx, cz));
                return (StewardTargetKind.Mud, "Mud", x, z);
            }
            default:
                return null;
        }
    }

    /// <summary>The best thing the player could do to a target now, in the order of <see cref="StewardActionKind"/>.</summary>
    public StewardActionKind? BestStewardAction(string target)
    {
        foreach (var kind in StewardActionsFor(SplitTarget(target).Prefix))
            if (StewardActionAvailable(kind, target)) return kind;
        return null;
    }

    private static StewardActionKind[] StewardActionsFor(string prefix) => prefix switch
    {
        "person" => [StewardActionKind.BreakUp, StewardActionKind.RadioMedic, StewardActionKind.StandBy, StewardActionKind.Calm, StewardActionKind.Escort, StewardActionKind.Directions],
        "toilet" => [StewardActionKind.FreeLoo],
        "tap" => [StewardActionKind.Drink, StewardActionKind.BodgeTap],
        "bar" => [StewardActionKind.Drink],
        "bin" => [StewardActionKind.EmptyBin],
        "litter" => [StewardActionKind.PickLitter],
        "cow" => [StewardActionKind.HerdCow],
        "mud" => [StewardActionKind.SpreadStraw],
        _ => [],
    };

    /// <summary>Whether the action can be done to the target right now (reach aside).</summary>
    private bool StewardActionAvailable(StewardActionKind kind, string target)
    {
        if (_stewardShift is not { PlayerId: { } me } shift || _preparation?.Status != PreparationStatus.Running ||
            !StewardActionsFor(SplitTarget(target).Prefix).Contains(kind) || StewardTargetPoint(target) is null) return false;
        var rest = SplitTarget(target).Key;
        ulong.TryParse(rest, out var id);
        switch (kind)
        {
            case StewardActionKind.BreakUp:
                return PersonIn(PersonView.Disorder, id) is { ConductStage: DisorderStage.Fight } && GuestFightOrigin(id) is { HandlingAttempt: null } fight &&
                    GuestFightParticipantsAvailable(fight);
            case StewardActionKind.RadioMedic:
                return CollapsedHere(id) && !HasClaim(id, PersonClaim.MedicPatient) && SelectRoleResponse(ResponseRole.Medic, id, out _) is not null;
            case StewardActionKind.StandBy:
                return CollapsedHere(id) && !(shift.StoodByFor ?? []).Contains(id);
            case StewardActionKind.Calm:
                return PersonIn(PersonView.Disorder, id) is { ConductStage: DisorderStage.Complaint or DisorderStage.Agitated or DisorderStage.Argument } person &&
                    person.Intent != MedicalIntent.Leaving && !PersonCollapsed(id) && !InterventionOwnsTarget(id) && !StuckInToilet(id);
            case StewardActionKind.Escort:
                return shift.EscortOffer is { } offer && offer.GuestId == id && offer.UntilTick > CurrentTick && shift.EscortId is null && CanBeWalkedOut(id);
            case StewardActionKind.Directions:
                return IsGuest(id) && !(shift.Directed ?? []).Contains(id) && SeekingAService(id);
            case StewardActionKind.FreeLoo:
                return ActiveFault(rest) is { Kind: FacilityFaultKind.StuckInToilet };
            case StewardActionKind.BodgeTap:
                return ActiveFault(rest) is { Kind: FacilityFaultKind.BrokenTap };
            case StewardActionKind.Drink:
                return PlayerWantsADrink(me) && (SplitTarget(target).Prefix == "bar" || ActiveFault(rest) is null && _disorder?.WaterClosed != true);
            case StewardActionKind.EmptyBin:
                return CaptureBins().FirstOrDefault(b => b.Id == rest) is { Pieces: > 0 };
            case StewardActionKind.PickLitter:
                return CaptureWaste(rest) is { Location: WasteLocation.Ground, CarrierId: null } &&
                    _litter?.Sweeps.Any(s => s.Remaining > 0 && !s.TargetIsBin && s.TargetId == rest) != true;
            case StewardActionKind.HerdCow:
                return _cows?.Loose.FirstOrDefault(c => c.Id == rest) is { HerderId: null };
            case StewardActionKind.SpreadStraw:
            {
                if (!IsRainyFestival || _weather!.StrawBales <= 0 || StewardTargetPoint(target) is not { } point) return false;
                var cell = TraversalGrid.WorldToCell(point.X, point.Z);
                return _traversalGrid?.Get(cell).IsWalkable == true && (GroundMudAt(cell) >= GroundRules.MudLevel || GroundWetAt(cell) >= GroundRules.PuddleWet) &&
                    StrawTargets(cell).Contains(cell);
            }
            default:
                return false;
        }
    }

    private bool CollapsedHere(ulong id) => !IsPlayerSteward(id) && PersonIn(PersonView.Medical, id) is { HealthStage: MedicalStage.Collapsed or MedicalStage.Critical } &&
        !StuckInToilet(id);

    /// <summary>Calm now, on their feet, doing nothing that can't be dropped, and with a way out.</summary>
    private bool CanBeWalkedOut(ulong id) => IsGuest(id) && _persons[id] is { Admitted: true, Departed: false } person &&
        person.Intent is MedicalIntent.WatchShow or MedicalIntent.SeekWater && person.ConductStage is DisorderStage.Resolved or DisorderStage.Calm &&
        !PersonCollapsed(id) && !PersonalActivityInService(id) && !StuckInToilet(id) &&
        !HasClaim(id, PersonClaims.ResponseAssigned | PersonClaim.Fighting | PersonClaim.Performing) && MedicalRouteExists(id, MedicalExitCell);

    /// <summary>On their way to a stall, the loos or the water, and not yet in the queue.</summary>
    private bool SeekingAService(ulong id)
    {
        var person = _persons[id];
        if (_navigationAgents[new(id)].Action != AgentNavigationAction.Travelling || person.ConductStage == DisorderStage.Fight || PersonCollapsed(id)) return false;
        return person.VendorId is { } at && !Vendors.Any(v => v.Id == at && (v.Queue.Contains(id) || v.OwnerId == id)) ||
            person.ToiletStage == ToiletVisitStage.Approaching ||
            person.Intent == MedicalIntent.SeekWater && !WaterPoints().Any(w => w.Queue.Contains(id) || w.Overflow.Contains(id));
    }

    private bool PlayerWantsADrink(ulong me) => _persons[me] is var p && (p.Thirst >= 1_000 || p.HeatExposure >= 1_000);

    private CommandResult? ValidateStewardAction(EntityId? target, StewardActionCommand command, bool autopilot = false)
    {
        if (target is not null || _stewardShift?.PlayerId is null || _preparation?.Status != PreparationStatus.Running)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Nobody is on shift.");
        if (!autopilot && _stewardShift.Autopilot is not null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Your steward is working by themselves: take them over first.");
        if (!Enum.IsDefined(command.Action) || command.FacingDegrees is < 0 or > 359 || command.TargetId is null)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown action.");
        if (command.Action == StewardActionKind.Release)
            return _stewardShift.Action is null ? CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "Nothing is held.") : null;
        if (_stewardShift.Action is not null) return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "Already holding an action.");
        if (_stewardShift.PlanUp) return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Put the site plan away first.");
        if (!StewardActionAvailable(command.Action, command.TargetId) ||
            !StewardReach(command.FacingDegrees).Any(item => item.TargetId == command.TargetId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Nothing like that is in reach.");
        return null;
    }

    private void ApplyStewardAction(StewardActionCommand command, bool autopilot = false)
    {
        var shift = _stewardShift!;
        _stewardShift = command.Action == StewardActionKind.Release
            ? shift with { Action = null }
            : shift with { Action = new(command.Action, command.TargetId, CurrentTick, autopilot ? StewardAutopilotRules.HoldTicks(command.Action) : StewardShiftRules.DurationTicks(command.Action))
                { Auto = autopilot }, FacingDegrees = command.FacingDegrees };
        SettleStewardShift();
    }

    /// <summary>Why a held action can't carry on: its target gone, out of reach, or no longer needing it. Null while it can.</summary>
    private string? HeldActionProblem(StewardActionProgress held)
    {
        if (_stewardShift?.PlayerId is not { } me || !_navigationAgents.TryGetValue(new(me), out var nav)) return "Off shift.";
        if (StewardTargetPoint(held.TargetId) is not { } point) return "They've gone.";
        long dx = point.X - nav.XMillimetres, dz = point.Z - nav.ZMillimetres;
        if (dx * dx + dz * dz > (long)StewardShiftRules.HoldReachMillimetres * StewardShiftRules.HoldReachMillimetres) return "Out of reach.";
        if (StewardActionAvailable(held.Kind, held.TargetId)) return null;
        return held.Kind switch
        {
            StewardActionKind.BreakUp => "The fight's over, or another steward has it.",
            StewardActionKind.RadioMedic => "The medic's already been called.",
            StewardActionKind.StandBy => "They're in the medic's hands now.",
            StewardActionKind.Calm => "They're past calming now.",
            StewardActionKind.FreeLoo or StewardActionKind.BodgeTap => "Someone else sorted it.",
            StewardActionKind.PickLitter => "Someone else got it.",
            StewardActionKind.HerdCow => "Another steward has that cow.",
            _ => "That's changed.",
        };
    }

    private void Score(Func<StewardShiftScore, StewardShiftScore> change) =>
        _stewardShift = _stewardShift! with { Score = change(_stewardShift.Score ?? new()) };

    /// <summary>
    /// The shift's tick, after the crew's responses: a held action that has run its time is done; the guest being
    /// walked out follows, and is handed out of the gate once there; an escort offer lapses.
    /// </summary>
    private void AdvanceStewardShift()
    {
        if (_stewardShift is not { PlayerId: { } me } shift) return;
        if (shift.Action is { } held && CurrentTick >= held.StartedTick + held.DurationTicks)
        {
            if (HeldActionProblem(held) is { } problem) DropStewardAction(held, problem);
            else CompleteStewardAction(held, me);
        }
        AdvanceEscort(me);
        if (_stewardShift.EscortOffer is { } offer && offer.UntilTick <= CurrentTick) _stewardShift = _stewardShift with { EscortOffer = null };
        AdvanceStageInvasion(me);
    }

    /// <summary>
    /// At the end of every tick and after every command: a held action whose target has gone or moved off is dropped,
    /// an escort that can't go on ends, and the radio's calls follow the incidents. So a save always holds a valid shift.
    /// </summary>
    private void SettleStewardShift()
    {
        if (_stewardShift?.PlayerId is not { } me) return;
        if (_stewardShift.Action is { } held && HeldActionProblem(held) is { } problem) DropStewardAction(held, problem);
        if (_stewardShift.EscortId is { } guest && EscortProblem(guest) is { } lost) EndEscort(guest, lost, back: true);
        RefreshStewardCalls();
    }

    private void DropStewardAction(StewardActionProgress held, string why) =>
        _stewardShift = _stewardShift! with { Action = null, LastOutcome = new(held.Kind, held.TargetId, false, false, CurrentTick, why) };

    private void CompleteStewardAction(StewardActionProgress held, ulong me)
    {
        _stewardShift = _stewardShift! with { Action = null };
        var rest = SplitTarget(held.TargetId).Key;
        ulong.TryParse(rest, out var id);
        var name = StewardTargetPoint(held.TargetId)?.Name ?? "";
        var profile = PlayerStewardProfile(me);
        // Working by themselves, the player's steward is no match for the player's own judgement.
        if (held.Auto) profile = profile with { CalmingSkill = Math.Max(0, profile.CalmingSkill - StewardAutopilotRules.SkillPenalty),
            ConfrontationSkill = Math.Max(0, profile.ConfrontationSkill - StewardAutopilotRules.SkillPenalty) };
        AnswerStewardCalls(held, id, rest);
        var (success, text) = held.Kind switch
        {
            StewardActionKind.Calm => PlayerCalm(id, profile),
            StewardActionKind.BreakUp => PlayerBreakUp(id, profile, held),
            StewardActionKind.Escort => StartEscort(id),
            StewardActionKind.RadioMedic => RadioTheMedic(id),
            StewardActionKind.StandBy => StandBy(id, name),
            StewardActionKind.FreeLoo => FreeTheLoo(rest),
            StewardActionKind.BodgeTap => BodgeTheTap(rest),
            StewardActionKind.EmptyBin => EmptyTheBin(rest),
            StewardActionKind.PickLitter => PickUpLitter(rest),
            StewardActionKind.SpreadStraw => SpreadABale(held.TargetId),
            StewardActionKind.HerdCow => DriveTheCow(rest, me),
            StewardActionKind.Directions => GiveDirections(id, name),
            StewardActionKind.Drink => HaveADrink(me),
            _ => (false, ""),
        };
        _stewardShift = _stewardShift! with { LastOutcome = new(held.Kind, held.TargetId, true, success, CurrentTick, text) };
    }

    /// <summary>The steward's calm against how wound up they are, as the crew's: a keyed roll, so nothing else's dice move.</summary>
    private (bool, string) PlayerCalm(ulong id, StaffProfile profile)
    {
        var person = _persons[id] with { };
        var roll = CowRules.Draw(CampaignSeed, "steward-shift.calm", CurrentTick, id, 2_001);
        if (profile.CalmingSkill + roll >= person.Pressure + person.Temperament / 4)
        {
            MutatePerson(id, item => { item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.ConductStageTick = CurrentTick; item.CooldownUntilTick = CurrentTick + 800; });
            DisorderEvent("steward-shift:calmed", id, profile.AgentId, person.Pressure, "The player steward calmed them down");
            Score(s => s with { Calmed = s.Calmed + 1, MoodAdded = s.MoodAdded + StewardShiftRules.MoodCredit(StewardActionKind.Calm) });
            _stewardShift = _stewardShift! with { EscortOffer = new(id, CurrentTick + StewardShiftRules.EscortOfferTicks) };
            return (true, $"{person.Name} has calmed down.");
        }
        Score(s => s with { CalmsFailed = s.CalmsFailed + 1 });
        var pressure = Math.Min(10_000, person.Pressure + StewardShiftRules.FailedCalmPressure);
        // Already arguing, with a grievance and someone to hand: it tips into a fight. Otherwise it's an argument now.
        if (person.ConductStage == DisorderStage.Argument && person.Grievance != DisorderGrievance.None && FindDisorderOpponent(id) is { } opponent)
        {
            MutatePerson(id, item => item.Pressure = pressure);
            BeginDisorderFight(id, opponent, "steward-shift:calm-failed-fight");
            return (false, $"It's kicked off: {person.Name} has gone for {_persons[opponent].Name}!");
        }
        MutatePerson(id, item =>
        {
            item.Pressure = Math.Max(pressure, DisorderArgumentPressure);
            if (item.ConductStage != DisorderStage.Argument) { item.ConductStage = DisorderStage.Argument; item.ConductStageTick = CurrentTick; }
        });
        DisorderEvent("steward-shift:calm-failed", id, profile.AgentId, pressure, "The player steward's calming wound them up");
        return (false, $"{person.Name} won't be calmed: it's getting heated.");
    }

    /// <summary>The fight's one physical handling attempt, made by the player: the crew's confrontation roll, keyed.</summary>
    private (bool, string) PlayerBreakUp(ulong id, StaffProfile profile, StewardActionProgress held)
    {
        var origin = GuestFightOrigin(id)!;
        var initiator = _persons[origin.InitiatorId];
        var roll = CowRules.Draw(CampaignSeed, "steward-shift.break-up", CurrentTick, origin.InitiatorId, 2_001);
        var success = profile.ConfrontationSkill + roll >= initiator.Pressure + initiator.Temperament / 4;
        if (success)
        {
            _disorder = _disorder! with { Incidents = _disorder.Incidents.Select(item => item == origin ? item with { InjuryTick = -2 } : item).ToArray() };
            foreach (var fighter in new[] { origin.InitiatorId, origin.OpponentId })
            {
                MutatePerson(fighter, person => { person.ConductStage = DisorderStage.Resolved; person.Pressure = 0; person.CooldownUntilTick = CurrentTick + 800; });
                ReturnToListening(fighter);
            }
            origin = _disorder.Incidents.Single(item => item.InitiatorId == origin.InitiatorId && item.FightTick == origin.FightTick);
        }
        SetFightAttempt(origin, new(profile.AgentId, held.StartedTick, CurrentTick, success ? FightHandlingOutcome.Succeeded : FightHandlingOutcome.Failed));
        DisorderEvent("steward-shift:break-up", origin.InitiatorId, profile.AgentId, origin.Pressure,
            success ? "The player steward stopped the fight" : "The player steward's one attempt failed; the fight goes on");
        if (!success)
        {
            Score(s => s with { BreakUpsFailed = s.BreakUpsFailed + 1 });
            return (false, "They shook you off: the fight goes on.");
        }
        Score(s => s with { FightsBrokenUp = s.FightsBrokenUp + 1, MoodAdded = s.MoodAdded + StewardShiftRules.MoodCredit(StewardActionKind.BreakUp) });
        _stewardShift = _stewardShift! with { EscortOffer = new(origin.InitiatorId, CurrentTick + StewardShiftRules.EscortOfferTicks) };
        return (true, $"Broken up. {initiator.Name} started it.");
    }

    /// <summary>The instigator drops whatever they were doing and follows the player to the gate.</summary>
    private (bool, string) StartEscort(ulong id)
    {
        AbandonActivity(id, CurrentActivity(_persons[id]), false, "Walked out by a steward");
        MutatePerson(id, item =>
        {
            item.Intent = MedicalIntent.Leaving; item.Reason = "Being walked out of the gate by a steward";
            item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.Grievance = DisorderGrievance.None;
            item.ConductStageTick = CurrentTick; item.CooldownUntilTick = long.MaxValue; item.WaterQueueSlot = null;
        });
        _stewardShift = _stewardShift! with { EscortId = id, EscortOffer = null };
        FollowThePlayer(id);
        DisorderEvent("steward-shift:escort", id, _stewardShift.PlayerId, 0, "Following the player steward to the gate");
        return (true, $"{_persons[id].Name} is following you to the gate.");
    }

    private (bool, string) RadioTheMedic(ulong id)
    {
        if (SelectRoleResponse(ResponseRole.Medic, id, out _) is not MedicalCommand command) return (false, "No medic's free: stay with them.");
        ApplyMedicalCommand(command);
        Score(s => s with { MedicRadioed = s.MedicRadioed + 1 });
        return (true, $"Medic's on the way to {_persons[id].Name}.");
    }

    private (bool, string) StandBy(ulong id, string name)
    {
        _stewardShift = _stewardShift! with { StoodByFor = [.. _stewardShift.StoodByFor ?? [], id] };
        Score(s => s with { StoodBy = s.StoodBy + 1 });
        return (true, $"You kept the crowd back from {name}.");
    }

    private (bool, string) FreeTheLoo(string toiletId)
    {
        var fault = ActiveFault(toiletId)!;
        ResolveFault(fault, FacilityFaultStage.Fixed);
        Score(s => s with { LoosFreed = s.LoosFreed + 1, MoodAdded = s.MoodAdded + StewardShiftRules.MoodCredit(StewardActionKind.FreeLoo) });
        return (true, $"Door's open: {_persons[fault.VictimId].Name} is free.");
    }

    private (bool, string) BodgeTheTap(string tapId)
    {
        ResolveFault(ActiveFault(tapId)!, FacilityFaultStage.Bodged);
        Score(s => s with { TapsBodged = s.TapsBodged + 1, MoodAdded = s.MoodAdded + StewardShiftRules.MoodCredit(StewardActionKind.BodgeTap) });
        return (true, "Bodged: it's running at half flow.");
    }

    private (bool, string) EmptyTheBin(string binId)
    {
        _litter = _litter! with { Pieces = _litter.Pieces.Select(w => w.Location == WasteLocation.Bin && w.BinId == binId ?
            w with { Location = WasteLocation.Removed, BinId = null } : w).ToArray() };
        LitterVisualVersion++;
        Score(s => s with { BinsEmptied = s.BinsEmptied + 1, MoodAdded = s.MoodAdded + StewardShiftRules.MoodCredit(StewardActionKind.EmptyBin) });
        return (true, "Bin emptied.");
    }

    private (bool, string) PickUpLitter(string pieceId)
    {
        SetWaste(CaptureWaste(pieceId)! with { Location = WasteLocation.Removed });
        Score(s => s with { LitterPicked = s.LitterPicked + 1, MoodAdded = s.MoodAdded + StewardShiftRules.MoodCredit(StewardActionKind.PickLitter) });
        return (true, "Bagged it.");
    }

    /// <summary>A bale from the store, spread as the crew's straw job spreads it: the muddiest ground near the spot first.</summary>
    private (bool, string) SpreadABale(string target)
    {
        var point = StewardTargetPoint(target)!.Value;
        var spot = TraversalGrid.WorldToCell(point.X, point.Z);
        var cells = StrawTargets(spot).Take(GroundCoverRules.CellsPerBale).ToArray();
        foreach (var cell in cells) SpreadStrawOn(cell);
        SetWeather(_weather! with { StrawBales = _weather.StrawBales - 1 });
        Score(s => s with { StrawSpread = s.StrawSpread + 1, MoodAdded = s.MoodAdded + StewardShiftRules.MoodCredit(StewardActionKind.SpreadStraw) });
        return (true, $"Straw down: {cells.Length * TraversalGrid.CellSizeMillimetres * TraversalGrid.CellSizeMillimetres / 1_000_000} m² firmed up.");
    }

    /// <summary>The cow sets off for its gate, driven by the player: it walks on while they keep up behind it.</summary>
    private (bool, string) DriveTheCow(string cowId, ulong me)
    {
        var cow = _cows!.Loose.Single(c => c.Id == cowId);
        var cell = TraversalGrid.WorldToCell(cow.XMillimetres, cow.ZMillimetres);
        var home = DeterministicPathfinder.FindPath(_traversalGrid!, cell, CowRules.GateInside);
        var route = home.Found && home.Path.All(CowOnSite) ? home.Path.ToArray() : [CowRules.GateInside];
        _cows = _cows with { Loose = _cows.Loose.Select(c => c.Id == cowId ? c with { HerderId = me, HerdStartedTick = CurrentTick, Activity = CowActivity.Herded,
            Driving = true, Route = route, RouteIndex = 0 } : c).ToArray() };
        return (true, "Walk on behind it to drive it home.");
    }

    private (bool, string) GiveDirections(ulong id, string name)
    {
        var before = _persons[id].Satisfaction;
        ChangeSatisfaction(id, StewardShiftRules.DirectionsSatisfaction, MoodCause.StaffNearby);
        var added = _persons[id].Satisfaction - before;
        _stewardShift = _stewardShift! with { Directed = [.. _stewardShift.Directed ?? [], id] };
        Score(s => s with { Directions = s.Directions + 1, MoodAdded = s.MoodAdded + added });
        return (true, $"{name}: “Cheers!”");
    }

    private (bool, string) HaveADrink(ulong me)
    {
        MutatePerson(me, item =>
        {
            item.Thirst = Math.Max(0, item.Thirst - StewardShiftRules.DrinkThirstRelief);
            item.HeatExposure = Math.Max(0, item.HeatExposure - StewardShiftRules.DrinkHeatRelief);
        });
        Score(s => s with { Drinks = s.Drinks + 1 });
        return (true, "That's better.");
    }

    /// <summary>
    /// The guest being walked out keeps a little behind the player; with both near the gate they're handed out of it
    /// as a safe egress. Further than they'd follow, they slip back into the crowd.
    /// </summary>
    private void AdvanceEscort(ulong me)
    {
        if (_stewardShift!.EscortId is not { } guest) return;
        // Medical and disorder ran earlier this tick: a guest who's just collapsed or been hit isn't steered on.
        if (EscortProblem(guest) is { } lost) { EndEscort(guest, lost, back: true); return; }
        var player = _navigationAgents[new(me)]; var them = _navigationAgents[new(guest)];
        long dx = them.XMillimetres - player.XMillimetres, dz = them.ZMillimetres - player.ZMillimetres;
        var apart = dx * dx + dz * dz;
        if (apart > (long)StewardShiftRules.EscortLostMillimetres * StewardShiftRules.EscortLostMillimetres)
        { EndEscort(guest, "Lost them in the crowd.", back: true); return; }
        var (gx, gz) = TraversalGrid.CellCentre(MedicalExitCell);
        long ex = gx - player.XMillimetres, ez = gz - player.ZMillimetres;
        if (ex * ex + ez * ez <= (long)StewardShiftRules.EscortGateMillimetres * StewardShiftRules.EscortGateMillimetres &&
            apart <= (long)StewardShiftRules.EscortCloseMillimetres * StewardShiftRules.EscortCloseMillimetres)
        {
            ApplyAgentDestination(new(guest), new(MedicalExitCell, "disorder.safe-egress"));
            DisorderEvent("disorder:egress", guest, me, 0, "Walked to the gate by the player steward; egress completes on arrival.");
            Score(s => s with { Escorted = s.Escorted + 1, MoodAdded = s.MoodAdded + StewardShiftRules.MoodCredit(StewardActionKind.Escort) });
            _stewardShift = _stewardShift with { EscortId = null, LastOutcome = new(StewardActionKind.Escort, $"person:{guest}", true, true, CurrentTick,
                $"Seen out: {_persons[guest].Name} is leaving.") };
            return;
        }
        if (CurrentTick % StewardShiftRules.EscortRefreshTicks == 0) FollowThePlayer(guest);
    }

    /// <summary>Routes the escorted guest to open ground a step behind the player, on their side; unchanged when already near.</summary>
    private void FollowThePlayer(ulong guest)
    {
        var me = _navigationAgents[new(_stewardShift!.PlayerId!.Value)]; var them = _navigationAgents[new(guest)];
        // Nobody follows the player up a stage's steps: they wait at the bottom.
        if (OnStageCells(Stages, TraversalGrid.WorldToCell(me.XMillimetres, me.ZMillimetres))) return;
        long dx = them.XMillimetres - me.XMillimetres, dz = them.ZMillimetres - me.ZMillimetres;
        var length = Math.Max(1, IntegerSquareRoot(dx * dx + dz * dz));
        if (length <= StewardShiftRules.EscortFollowMillimetres * 2 && them.Action is AgentNavigationAction.Arrived && them.IntentId == StewardShiftRules.EscortIntent) return;
        var spot = WalkableNear(TraversalGrid.WorldToCell(me.XMillimetres + (int)(dx * StewardShiftRules.EscortFollowMillimetres / length),
            me.ZMillimetres + (int)(dz * StewardShiftRules.EscortFollowMillimetres / length)));
        if (them.Destination == spot && them.IntentId == StewardShiftRules.EscortIntent && them.Action != AgentNavigationAction.NoRoute) return;
        ApplyAgentDestination(new(guest), new(spot, StewardShiftRules.EscortIntent));
    }

    /// <summary>Why the escorted guest can no longer follow; null while they can.</summary>
    private string? EscortProblem(ulong guest) =>
        _persons[guest] is not { Admitted: true, Departed: false } person ? "They've gone." :
        PersonCollapsed(guest) ? "They've collapsed." :
        person.Intent != MedicalIntent.Leaving || _navigationAgents[new(guest)].IntentId != StewardShiftRules.EscortIntent ? "Something else took them." : null;

    /// <summary>The escort's over without the gate: they go back to the show, wound down but no longer barred.</summary>
    private void EndEscort(ulong guest, string why, bool back)
    {
        _stewardShift = _stewardShift! with { EscortId = null, LastOutcome = new(StewardActionKind.Escort, $"person:{guest}", false, false, CurrentTick, why) };
        if (!back || _persons[guest] is not { Admitted: true, Departed: false, Intent: MedicalIntent.Leaving } || PersonCollapsed(guest)) return;
        MutatePerson(guest, item => { item.Intent = MedicalIntent.WatchShow; item.Reason = "Slipped away from the steward"; item.CooldownUntilTick = CurrentTick + 800; });
        ReturnToListening(guest);
    }

    /// <summary>A cow the player is driving: it walks on home while they keep up, waits when they fall behind, and wanders off past that.</summary>
    private LooseCow? AdvancePlayerHerdedCow(LooseCow?[] herd, int index, ulong me)
    {
        var cow = herd[index]!;
        var nav = _navigationAgents[new(me)];
        long dx = cow.XMillimetres - nav.XMillimetres, dz = cow.ZMillimetres - nav.ZMillimetres;
        var apart = dx * dx + dz * dz;
        if (apart > (long)StewardShiftRules.HerdLostMillimetres * StewardShiftRules.HerdLostMillimetres || CurrentTick - cow.HerdStartedTick > CowRules.HerdDeadlineTicks)
        {
            _stewardShift = _stewardShift! with { LastOutcome = new(StewardActionKind.HerdCow, $"cow:{cow.Id}", false, false, CurrentTick, "The cow's wandered off again.") };
            return LetTheCowGo(cow);
        }
        if (apart > (long)StewardShiftRules.HerdKeepUpMillimetres * StewardShiftRules.HerdKeepUpMillimetres) return cow;
        var (moved, blocker) = StepCowApart(herd, index, CowRules.DrivenMillimetresPerTick);
        if (blocker >= 0)
        {
            if (CurrentTick % 80 == 0 && herd[blocker] is { HerderId: null, Activity: CowActivity.Grazing } grazer) herd[blocker] = MoveAside(herd, grazer, cow);
            return cow;
        }
        if (moved.RouteIndex < moved.Route.Length) return moved;
        MedicalEvent("cows:home", $"{cow.Id} was driven back into its field by the player steward.");
        Score(s => s with { CowsHerded = s.CowsHerded + 1, MoodAdded = s.MoodAdded + StewardShiftRules.MoodCredit(StewardActionKind.HerdCow) });
        _stewardShift = _stewardShift! with { LastOutcome = new(StewardActionKind.HerdCow, $"cow:{cow.Id}", true, true, CurrentTick, "Back in its field.") };
        return null;
    }

    private LooseCow LetTheCowGo(LooseCow cow) => cow with { HerderId = null, HerdStartedTick = -1, Driving = false, Activity = CowActivity.Grazing, Route = [], RouteIndex = 0,
        UntilTick = CurrentTick + 400 };

    /// <summary>
    /// The shift ends (closing, a death, a retry): the held action and any escort offer go, a driven cow goes back to
    /// grazing, the guest being walked out carries on out of the gate alone, every open call is closed and scored, and
    /// the crowd's mood is taken. The crew's ordinary rules then walk the steward home with everyone else.
    /// </summary>
    private void ReleaseStewardControl()
    {
        if (_stewardShift?.PlayerId is not { } me) return;
        if (_cows is { } cows && cows.Loose.Any(c => c.HerderId == me))
            _cows = cows with { Loose = cows.Loose.Select(c => c.HerderId == me ? LetTheCowGo(c) : c).ToArray() };
        if (_stewardShift.EscortId is { } guest && _persons[guest] is { Admitted: true, Departed: false } && !PersonCollapsed(guest))
            ApplyAgentDestination(new(guest), new(MedicalExitCell, "disorder.safe-egress"));
        CloseStewardCalls();
        var guests = PeopleIn(PersonView.Roster).Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted).ToArray();
        var mood = guests.Length == 0 ? 0 : (int)(guests.Sum(p => (long)p.Satisfaction) / guests.Length);
        _stewardShift = _stewardShift with { PlayerId = null, MoveX = 0, MoveZ = 0, Run = false, RemainderX = 0, RemainderZ = 0, Action = null,
            EscortOffer = null, EscortId = null, ClosingMood = mood, EndedTick = CurrentTick, OnStage = null, Autopilot = null, PlanUp = false };
    }

    /// <summary>
    /// The player's own thirst and heat, every four ticks while on shift (see <see cref="StewardShiftRules.ThirstPerStep"/>).
    /// Neither ever collapses them; high, they slow down.
    /// </summary>
    private void AdvancePlayerNeeds(ulong me, bool hot, bool rainy)
    {
        var shift = _stewardShift!;
        var running = shift.Run && !shift.PlanUp && (shift.MoveX != 0 || shift.MoveZ != 0);
        var extra = running && CurrentTick / 4 % 2 == 0 ? 1 : 0;
        var thirst = rainy ? (CurrentTick / 4 % 2 == 0 ? StewardShiftRules.ThirstPerStep : 0) : StewardShiftRules.ThirstPerStep + extra;
        var heat = InShade(me) ? -StewardShiftRules.ShadeCoolPerStep : hot && !rainy ? StewardShiftRules.HeatPerStep + extra : -StewardShiftRules.RainCoolPerStep;
        MutatePerson(me, item =>
        {
            item.Thirst = Math.Clamp(item.Thirst + thirst, 0, 10_000);
            item.HeatExposure = Math.Clamp(item.HeatExposure + heat, 0, 10_000);
        });
    }

    /// <summary>How the player's thirst and heat hold their pace back, per mille of a normal walk.</summary>
    private int PlayerNeedPacePermille(ulong me)
    {
        var worst = Math.Max(_persons[me].Thirst, _persons[me].HeatExposure);
        return worst >= StewardShiftRules.NeedSlowerLevel ? StewardShiftRules.NeedSlowerPermille :
            worst >= StewardShiftRules.NeedSlowLevel ? StewardShiftRules.NeedSlowPermille : 1_000;
    }

    /// <summary>
    /// Checked on the restored session, where the live rules can say it: a held action's target is still there, in
    /// reach and needing it; the escorted guest is still following; and the radio's open calls are exactly the live incidents.
    /// </summary>
    private string? StewardShiftWorkIssue()
    {
        if (_stewardShift is not { PlayerId: { } me } shift) return null;
        if (shift.Action is { } held && HeldActionProblem(held) is { } problem) return $"The player steward's held action can't be going on ({problem}).";
        if (shift.EscortId is { } guest && EscortProblem(guest) is { } lost) return $"The player steward's escort isn't following ({lost}).";
        if (shift.EscortOffer is { } offer && (!IsGuest(offer.GuestId) || offer.GuestId == shift.EscortId)) return "The player steward's escort offer is for nobody.";
        if (_cows?.Loose.Any(c => c.HerderId == me && !c.Driving) == true) return "The player steward drives a cow or doesn't.";
        if (PlayerStageIssue(me) is { } stage) return stage;
        return StewardCallsIssue();
    }

    /// <summary>The shift's own fields, checked against the saved snapshot alone: shapes, clocks and bounds.</summary>
    private static string? ValidatePersistedStewardWork(StewardShiftSnapshot shift, SessionPersistenceSnapshot s, PreparationSnapshot p)
    {
        if (shift.Score is not { } score || shift.Calls is not { } calls) return "Steward shift score or calls missing.";
        var counts = new long[] { score.Calls, score.Answered, score.Calmed, score.CalmsFailed, score.FightsBrokenUp, score.BreakUpsFailed, score.Escorted,
            score.MedicRadioed, score.StoodBy, score.LoosFreed, score.TapsBodged, score.BinsEmptied, score.LitterPicked, score.StrawSpread, score.CowsHerded,
            score.Directions, score.Drinks, score.UrgentUnhandled, score.MoodAdded, score.StageInvasions, score.AutoAnswered, score.Covered, score.RadioJobs };
        if (counts.Any(count => count is < 0 or > 100_000) || score.Answered > score.Calls || score.AutoAnswered > score.Answered || score.UrgentUnhandled > score.Calls - score.Answered || score.ResponseTicks < 0 ||
            score.MoodAdded < StewardShiftRules.CreditedMood(score) || score.MoodAdded > StewardShiftRules.CreditedMood(score) + (long)score.Directions * StewardShiftRules.DirectionsSatisfaction ||
            score.ResponseTicks > (long)score.Answered * Math.Max(1, s.CurrentTick) ||
            score.StageTicks < score.StageInvasions || score.StageTicks > s.CurrentTick - p.StartedTick + 1 || score.StageInvasions == 0 && score.StageTicks != 0)
            return "Steward shift score out of bounds.";
        var guests = p.People.Where(person => person.Role == ProtectedPersonRole.Guest).ToDictionary(person => person.AgentId);
        bool OnSite(ulong id) => guests.TryGetValue(id, out var guest) && guest.Admitted && !guest.Departed;
        var on = shift.PlayerId is not null;
        var handsOn = s.Mode == CampaignMode.HandsOn;
        bool BadCall(StewardCall call) => call is null || string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Subject) ||
            !Enum.IsDefined(call.Kind) || call.OpenedTick < p.StartedTick || call.OpenedTick > s.CurrentTick ||
            call.ClosedTick != -1 && (call.ClosedTick < call.OpenedTick || call.ClosedTick > s.CurrentTick) ||
            call.AnsweredTick != -1 && (call.AnsweredTick < call.OpenedTick || call.AnsweredTick > (call.ClosedTick >= 0 ? call.ClosedTick : s.CurrentTick)) ||
            call.Auto && call.AnsweredTick < 0 || call.CoveredBy is { } crew && (!handsOn || crew == shift.PlayerId ||
                !p.People.Any(person => person.AgentId == crew && person.Role == ProtectedPersonRole.Staff && person.Name != HandsOnStewardName)) ||
            call.Where is { } where && (!handsOn || where.Length is 0 or > 64);
        if (calls.Length > 512 || calls.Any(BadCall) ||
            calls.Where(call => call.ClosedTick < 0).Select(call => call.Id).Distinct().Count() != calls.Count(call => call.ClosedTick < 0) ||
            calls.Select(call => (call.Id, call.OpenedTick)).Distinct().Count() != calls.Length ||
            calls.Count(call => call.ClosedTick >= 0) > StewardShiftRules.ClosedCallsKept || !on && calls.Any(call => call.ClosedTick < 0))
            return "Steward shift radio calls invalid.";
        // A hands-on day's log of the night: closed calls that went out or were answered, in the order they closed,
        // exactly the ones the score counted (those the crew covered and the player left to them aside).
        var log = shift.Log ?? [];
        if (shift.Log is not null && (!handsOn || log.Length is 0 or > 4_096 || log.Any(call => BadCall(call) || call.ClosedTick < 0 || !InStewardLog(call)) ||
                log.Select(call => (call.Id, call.OpenedTick)).Distinct().Count() != log.Length || log.Zip(log.Skip(1)).Any(pair => pair.Second.ClosedTick < pair.First.ClosedTick)) ||
            handsOn && (log.Count(call => call.AnsweredTick >= 0 || call.CoveredBy is null) != score.Calls || log.Count(call => call.AnsweredTick >= 0) != score.Answered ||
                log.Count(call => call.Auto) != score.AutoAnswered || log.Count(call => call.AnsweredTick < 0 && call.CoveredBy is not null) != score.Covered))
            return "Steward shift call log invalid.";
        if (shift.Directed is { } directed && (directed.Distinct().Count() != directed.Length || directed.Any(id => !guests.ContainsKey(id))) ||
            (shift.Directed?.Length ?? 0) != score.Directions)
            return "Steward shift directions invalid.";
        if (shift.StoodByFor is { } stood && (stood.Distinct().Count() != stood.Length || stood.Any(id => !guests.ContainsKey(id))) ||
            (shift.StoodByFor?.Length ?? 0) != score.StoodBy)
            return "Steward shift stand-bys invalid.";
        if (shift.Action is { } held && (!on || held.Kind == StewardActionKind.Release || !Enum.IsDefined(held.Kind) || string.IsNullOrWhiteSpace(held.TargetId) ||
                held.DurationTicks != (held.Auto ? StewardAutopilotRules.HoldTicks(held.Kind) : StewardShiftRules.DurationTicks(held.Kind)) ||
                held.Auto != (shift.Autopilot is not null) || held.StartedTick > s.CurrentTick || held.StartedTick + held.DurationTicks <= s.CurrentTick))
            return "Steward shift held action invalid.";
        if (shift.EscortId is { } escorted && (!on || !OnSite(escorted)) ||
            shift.EscortOffer is { } offer && (!on || !OnSite(offer.GuestId) || offer.UntilTick <= s.CurrentTick || offer.UntilTick > s.CurrentTick + StewardShiftRules.EscortOfferTicks))
            return "Steward shift escort invalid.";
        if (shift.LastOutcome is { } last && (!Enum.IsDefined(last.Kind) || last.Tick < p.StartedTick || last.Tick > s.CurrentTick ||
                string.IsNullOrWhiteSpace(last.TargetId) || last.TargetId.Length > 64 || last.Text is null || last.Text.Length > 200))
            return "Steward shift outcome invalid.";
        if (!on && shift.OnStage is not null || shift.OnStage is { } stage && (FestivalStages.IndexOf(SavedStages(s), stage.StageId) < 0 ||
                stage.SinceTick < p.StartedTick || stage.SinceTick > s.CurrentTick || stage.InvadedTick is { } invaded && (invaded < stage.SinceTick || invaded > s.CurrentTick) ||
                stage.InvadedTick is not null && score.StageInvasions == 0))
            return "Steward shift stage record invalid.";
        // The plan is in the player's own hand: only while they have their steward, with nothing else held.
        if (shift.PlanUp && (!on || shift.Autopilot is not null || shift.Action is not null))
            return "Steward shift site plan invalid.";
        if (on != (shift.ClosingMood is null) || on != (shift.EndedTick is null) || shift.ClosingMood is < 0 or > 10_000 ||
            shift.EndedTick is { } ended && (ended < p.StartedTick || ended > s.CurrentTick))
            return "Steward shift close invalid.";
        return null;
    }
}
