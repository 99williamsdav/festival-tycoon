namespace Festival.Simulation;

public sealed partial class GameSession
{
    private const long StewardAttendanceDistanceSquared = 1_562_500; // 1.25 m maximum; approach aims for 1 m.
    private bool StewardNearPoint(int x, int z, ulong targetId)
    {
        var target = _navigationAgents[new(targetId)];
        var dx = (long)x - target.XMillimetres; var dz = (long)z - target.ZMillimetres;
        return dx * dx + dz * dz is >= 250_000 and <= StewardAttendanceDistanceSquared &&
            TraversalSweep.IsWalkable(_traversalGrid!, x, z, target.XMillimetres, target.ZMillimetres);
    }
    private bool StewardAttending(ulong workerId, ulong targetId)
    {
        var worker = _navigationAgents[new(workerId)];
        return worker.Action == AgentNavigationAction.Arrived && StewardNearPoint(worker.XMillimetres, worker.ZMillimetres, targetId);
    }
    private GridCell? StewardResponseCell(ulong workerId, ulong targetId)
    {
        var worker = _navigationAgents[new(workerId)]; var target = _navigationAgents[new(targetId)];
        var counterpart = GuestFightOrigin(targetId) is { } fight
            ? (ulong?)(fight.InitiatorId == targetId ? fight.OpponentId : fight.InitiatorId) : null;
        bool Suitable(GridCell candidate)
        {
            if (!_traversalGrid!.Contains(candidate) || !_traversalGrid.Get(candidate).IsWalkable ||
                WaterPoints().Any(point => CaptureWaterQueueCells(point.Id).Contains(candidate))) return false;
            var centre = TraversalGrid.CellCentre(candidate);
            return StewardNearPoint(centre.XMillimetres, centre.ZMillimetres, targetId) &&
                (counterpart is null || StewardNearPoint(centre.XMillimetres, centre.ZMillimetres, counterpart.Value));
        }
        // Keep a still-valid approach point while the target moves within the tolerance.
        if (worker.Action != AgentNavigationAction.NoRoute && worker.Destination is { } current && Suitable(current)) return current;
        var cell = TraversalGrid.WorldToCell(target.XMillimetres, target.ZMillimetres);
        var candidates = Enumerable.Range(-3, 7).SelectMany(x => Enumerable.Range(-3, 7).Select(z => new GridCell(cell.X+x,cell.Z+z)))
            .Where(Suitable).OrderBy(candidate => { var c=TraversalGrid.CellCentre(candidate); var x=(long)c.XMillimetres-target.XMillimetres; var z=(long)c.ZMillimetres-target.ZMillimetres; return Math.Abs(x*x+z*z-1_000_000); })
            .ThenBy(candidate => candidate.X).ThenBy(candidate => candidate.Z);
        return candidates.Cast<GridCell?>().FirstOrDefault(candidate => MedicalRouteExists(workerId, candidate!.Value));
    }
    private DisorderIncidentOrigin? GuestFightOrigin(ulong id) => _disorder?.Incidents.LastOrDefault(origin =>
        (origin.InitiatorId == id || origin.OpponentId == id) && !IsSteward(origin.OpponentId) &&
        (PersonIn(PersonView.Disorder, id) is { ConductStage: DisorderStage.Fight } person
            ? person.ConductStageTick == origin.FightTick : origin.HandlingAttempt is { Outcome: FightHandlingOutcome.Handling }));
    private void SetFightAttempt(DisorderIncidentOrigin origin, FightHandlingAttempt attempt) => _disorder = _disorder! with
    { Incidents = _disorder.Incidents.Select(item => item == origin ? item with { HandlingAttempt = attempt } : item).ToArray() };

    private bool FightHandlingPositionValid(StewardResponse response, DisorderIncidentOrigin origin)
    {
        var worker = _navigationAgents[new(response.WorkerId)];
        bool Near(ulong id)
        {
            var participant = _navigationAgents[new(id)];
            var dx = (long)worker.XMillimetres - participant.XMillimetres; var dz = (long)worker.ZMillimetres - participant.ZMillimetres;
            return StewardNearPoint(worker.XMillimetres, worker.ZMillimetres, id);
        }
        // Arrival at a legal bedside is required. Both fighters remain physical participants.
        return worker.Action == AgentNavigationAction.Arrived && Near(origin.InitiatorId) && Near(origin.OpponentId);
    }
    private void EndGuestFightResponse(StewardResponse response, DisorderIncidentOrigin origin, FightHandlingOutcome outcome, string reason)
    {
        if (origin.HandlingAttempt is { Outcome: FightHandlingOutcome.Handling } attempt && attempt.WorkerId == response.WorkerId)
            SetFightAttempt(origin, attempt with { EndedTick = CurrentTick, Outcome = outcome });
        SetStewardResponse(response with { Stage = SecurityResponseStage.Completed, TargetId = null, Description = reason });
        DisorderEvent("security:fight-handling-ended", origin.InitiatorId, response.WorkerId, origin.Pressure, reason);
    }
    private bool GuestFightParticipantsAvailable(DisorderIncidentOrigin origin) =>
        origin.InjuryTick == -1 && !PersonCollapsed(origin.InitiatorId) && !PersonCollapsed(origin.OpponentId) &&
        PersonIn(PersonView.Roster, origin.InitiatorId) is { Admitted: true, Departed: false } &&
        PersonIn(PersonView.Roster, origin.OpponentId) is { Admitted: true, Departed: false } &&
        PersonIn(PersonView.Disorder, origin.InitiatorId) is { ConductStage: DisorderStage.Fight } initiator && initiator.OpponentId == origin.OpponentId &&
        PersonIn(PersonView.Disorder, origin.OpponentId) is { ConductStage: DisorderStage.Fight } opponent && opponent.OpponentId == origin.InitiatorId;
    private void AdvanceGuestFightResponse(StewardResponse response, DisorderIncidentOrigin origin)
    {
        var active = GuestFightParticipantsAvailable(origin);
        if (!active || PersonCollapsed(response.WorkerId) || _navigationAgents[new(response.WorkerId)].Action == AgentNavigationAction.NoRoute)
        { EndGuestFightResponse(response, origin, FightHandlingOutcome.Interrupted, "Fight response ended because participants, worker or route became unavailable; original deadlines remain"); return; }
        if (origin.HandlingAttempt is { } attempted && (attempted.WorkerId != response.WorkerId || attempted.Outcome != FightHandlingOutcome.Handling))
        { EndGuestFightResponse(response, origin, FightHandlingOutcome.Interrupted, "This fight's single physical attempt is already owned or spent"); return; }
        if (origin.HandlingAttempt is null && response.Stage is SecurityResponseStage.Calming or SecurityResponseStage.Confronting)
        {
            response = response with { Stage = SecurityResponseStage.Travelling, Description = "Argument became a reciprocal fight; physically re-approaching before the one handling attempt" };
            SetStewardResponse(response);
        }
        if (response.Stage == SecurityResponseStage.Travelling)
        {
            if (FightHandlingPositionValid(response, origin))
            {
                // This is the exact once-per-incident attempt boundary: actual arrival,
                // not reservation/dispatch. Interruptions after this point consume it.
                SetFightAttempt(origin, new(response.WorkerId, CurrentTick, -1, FightHandlingOutcome.Handling));
                SetStewardResponse(response with { Stage = SecurityResponseStage.Confronting, StartedTick = CurrentTick, Description = "Physically arrived; handling the reciprocal fight" });
                DisorderEvent("security:fight-handling-start", origin.InitiatorId, response.WorkerId, origin.Pressure, "Physical arrival starts the incident's one handling attempt; original fight deadline unchanged");
            }
            else if (CurrentTick % 80 == 0 && StewardResponseCell(response.WorkerId, response.TargetId!.Value) is { } cell && _navigationAgents[new(response.WorkerId)].Destination != cell)
                ApplyAgentDestination(new(response.WorkerId), new(cell, "disorder.security-retarget"));
            return;
        }
        if (response.Stage != SecurityResponseStage.Confronting) return;
        if (!FightHandlingPositionValid(response, origin))
        { EndGuestFightResponse(response, origin, FightHandlingOutcome.Interrupted, "Physical handling interrupted; the one attempt is spent and original fight continues"); return; }
        if (CurrentTick < response.StartedTick + DisorderConfrontationTicks) return;
        var initiator = _persons[origin.InitiatorId];
        var success = GetResponseStaff().Single(worker => worker.AgentId == response.WorkerId).ConfrontationSkill + NextRandom(RandomStreamId.Incidents) % 2_001 >=
            initiator.Pressure + initiator.Temperament / 4;
        if (success)
        {
            // Keep the existing fight origin/timestamps. -2 is the existing no-injury terminal marker.
            _disorder = _disorder! with { Incidents = _disorder.Incidents.Select(item => item == origin ? item with { InjuryTick = -2 } : item).ToArray() };
            foreach (var id in new[] { origin.InitiatorId, origin.OpponentId })
            {
                MutatePerson(id, person => { person.ConductStage = DisorderStage.Resolved; person.Pressure = 0; person.CooldownUntilTick = CurrentTick + 800; });
                ReturnToListening(id);
            }
            origin = _disorder.Incidents.Single(item => item.InitiatorId == origin.InitiatorId && item.FightTick == origin.FightTick);
        }
        EndGuestFightResponse(response, origin, success ? FightHandlingOutcome.Succeeded : FightHandlingOutcome.Failed,
            success ? "Steward physically stopped the reciprocal fight" : "The one physical handling attempt failed; original fight and injury deadline continue");
    }
    private void FinishInterruptedFightAttempts()
    {
        if (_disorder is null) return;
        foreach (var origin in _disorder.Incidents.Where(origin => origin.InjuryTick != -1 && origin.HandlingAttempt is { Outcome: FightHandlingOutcome.Handling }).ToArray())
        {
            var job = GetStewardResponses().Single(response => response.WorkerId == origin.HandlingAttempt!.WorkerId);
            EndGuestFightResponse(job, origin, FightHandlingOutcome.Interrupted, "Original fight resolved or reached injury before physical handling completed; no extra deadline or retry");
        }
    }
    private void ReconcileMergedFightClaims()
    {
        if (_disorder is null) return;
        foreach (var origin in _disorder.Incidents.Where(origin => origin.InjuryTick == -1 && !IsSteward(origin.OpponentId)).ToArray())
        {
            var jobs = GetStewardResponses().Where(job => StewardBusy(job) && (job.TargetId == origin.InitiatorId || job.TargetId == origin.OpponentId))
                .OrderBy(job => origin.HandlingAttempt?.WorkerId == job.WorkerId ? 0 : 1).ThenBy(job => job.DispatchedTick).ThenBy(job => job.WorkerId).ToArray();
            foreach (var duplicate in jobs.Skip(1))
                EndGuestFightResponse(duplicate, origin, FightHandlingOutcome.Interrupted, "Two targets merged into one reciprocal fight; duplicate claim released before physical handling");
        }
    }
    private void ReleaseFightHandlingForBoundary(string reason)
    {
        if (_disorder is null) return;
        foreach (var origin in _disorder.Incidents.Where(origin => origin.HandlingAttempt is { Outcome: FightHandlingOutcome.Handling }).ToArray())
            EndGuestFightResponse(GetStewardResponses().Single(job => job.WorkerId == origin.HandlingAttempt!.WorkerId), origin, FightHandlingOutcome.Interrupted, reason);
    }
    private void ReleaseFrozenStaffClaims()
    {
        if (!IsLifecycleEditionFrozen()) return;
        foreach (var sweep in (_litter?.Sweeps ?? []).Where(s => s.Remaining > 0).ToArray()) EndSweep(sweep, false);
        ReleaseFightHandlingForBoundary("First death froze the edition; handling attempt interrupted without a new roll");
        FinishStaffResponsesForDeparture();
    }

    private static string? ValidatePersistedFightHandling(DisorderSnapshot disorder, SessionPersistenceSnapshot snapshot)
    {
        var jobs = disorder.Stewards ?? [];
        if (jobs.Any(job => job.Stage == SecurityResponseStage.Confronting &&
            disorder.People.SingleOrDefault(person => person.AgentId == job.TargetId) is { Stage: DisorderStage.Fight, OpponentId: { } opponent } target &&
            !jobs.Any(other => other.WorkerId == opponent) && !disorder.Incidents.Any(origin => origin.FightTick == target.StageTick &&
                (origin.InitiatorId == target.AgentId || origin.OpponentId == target.AgentId) && origin.HandlingAttempt is { Outcome: FightHandlingOutcome.Handling } attempt &&
                attempt.WorkerId == job.WorkerId && attempt.StartedTick == job.StartedTick)))
            return "Active guest-fight handling requires its persisted single attempt.";
        foreach (var origin in disorder.Incidents.Where(origin => origin.HandlingAttempt is not null))
        {
            var attempt = origin.HandlingAttempt!;
            if (!Enum.IsDefined(attempt.Outcome) ||
                !jobs.Any(job => job.WorkerId == attempt.WorkerId) || jobs.Any(job => job.WorkerId == origin.OpponentId) ||
                attempt.StartedTick < origin.FightTick || attempt.StartedTick > snapshot.CurrentTick ||
                attempt.EndedTick > snapshot.CurrentTick || attempt.Outcome == FightHandlingOutcome.Handling &&
                    (attempt.EndedTick != -1 || origin.InjuryTick != -1 ||
                     !jobs.Any(job => job.WorkerId == attempt.WorkerId && job.Stage == SecurityResponseStage.Confronting && job.StartedTick == attempt.StartedTick &&
                         (job.TargetId == origin.InitiatorId || job.TargetId == origin.OpponentId))) ||
                attempt.Outcome != FightHandlingOutcome.Handling && attempt.EndedTick < attempt.StartedTick ||
                attempt.Outcome is FightHandlingOutcome.Succeeded or FightHandlingOutcome.Failed && attempt.EndedTick < attempt.StartedTick + DisorderConfrontationTicks ||
                attempt.Outcome == FightHandlingOutcome.Succeeded && origin.InjuryTick != -2)
                return "Guest-fight handling attempt, outcome or physical job ownership invalid.";
        }
        return null;
    }
}
