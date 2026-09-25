using System.Text.Json;

namespace Festival.Simulation;

public enum MedicalStage { Clear, Distress, Collapsed, Critical, Treated, Removed, Terminal }
public enum MedicalIntent { WatchShow, SeekWater, Rest, AwaitMedic, Leaving, Collapsed, Drinking }
public enum MedicalResponseStage { None, Travelling, Treating, Removing, Completed }
public enum MedicalAction { GuideToWater, GuideToRest, DispatchMedic, SafeRemove, ReturnToShow }
public enum MedicalNeedProfile { Guest, Performer }
public sealed record MedicalCommand(ulong GuestId, MedicalAction Action) : SessionCommand;
public sealed record MedicalNeed(ulong AgentId, int Thirst, int HeatExposure, MedicalIntent Intent,
    string Reason, long LastDecisionTick, int? QueueSlot, long LastWaterTick,
    MedicalNeedProfile Profile = MedicalNeedProfile.Guest, MedicalStage Stage = MedicalStage.Clear,
    long WarningTick = -1, long CollapseTick = -1, long CriticalTick = -1);
public sealed record MedicalEvidence(string Id, long Tick, string Description);
public sealed record MedicalSnapshot(int Version, bool IsHot, ulong MedicId, ulong AtRiskGuestId,
    MedicalNeed[] Needs, ulong[] WaterQueue, ulong[] WaterOverflow, ulong? WaterOwnerId, int WaterDrinkTicks,
    MedicalStage Stage, MedicalResponseStage ResponseStage, ulong? ResponsePatientId, long WarningTick, long CollapseTick,
    long CriticalTick, long ResponseStartedTick, string Response, MedicalEvidence[] Evidence);

public sealed partial class GameSession
{
    // Prototype Hot scenario, not clinical thresholds or a general weather model.
    public const int MedicalDrinkThirstPerTick = 16;      // Continuous relief, not a fixed service timer.
    public const int MedicalDrinkHeatPerTick = 4;
    public const int MedicalDecisionCooldownTicks = 240;   // 3 real seconds at 1×.
    public const int MedicalCollapseDelayTicks = 1_600;   // 20 real seconds after distress.
    public const int MedicalCriticalDelayTicks = 800;     // 10 real seconds after collapse.
    public const int MedicalDeathDelayTicks = 2_400;      // 30 real seconds after collapse.
    public const int MedicalTreatmentTicks = 480;        // 6 real seconds after physical arrival.
    public const int MedicalDistressThirst = 9_000;
    public const int MedicalDistressHeat = 8_000;
    public static readonly GridCell MedicalWaterCell = new(95, 123);    // (-16.25, -2.25) m; upper-right overview, away from the audience.
    public static readonly GridCell MedicalTentCell = new(119, 172);    // (-4.25, 22.25) m; north of the audience.
    public static readonly GridCell MedicalMedicCell = new(122, 164);   // (-2.75, 18.25) m.
    public static readonly GridCell MedicalRestCell = new(119, 178);    // (-4.25, 25.25) m.
    public static readonly GridCell MedicalExitCell = new(128, 186);    // (0.25, 29.25) m.
    // One compact line behind the single tap, with a slight human offset and no branches.
    // Slot zero alone owns the tap. Approaching the tail does not reserve a slot.
    private static readonly GridCell[] WaterSlots =
    [
        new(95, 128), new(95, 130), new(96, 132), new(97, 134), new(98, 136),
        new(99, 138), new(101, 140), new(102, 142), new(103, 144), new(104, 146)
    ];
    private static readonly GridCell[] WaterOverflowSlots =
    [
        new(105, 148), new(106, 150), new(107, 152), new(108, 154), new(109, 156),
        new(110, 158), new(111, 160), new(112, 162), new(113, 164), new(114, 166)
    ];
    public static GridCell MedicalQueueSlot(int index) => WaterSlots[index];
    public static GridCell MedicalQueueApproach(int queuedCount, int overflowCount = 0) =>
        queuedCount < WaterSlots.Length ? WaterSlots[queuedCount] : WaterOverflowSlots[Math.Min(overflowCount, WaterOverflowSlots.Length - 1)];
    private bool MedicalQueueExcludesListening(GridCell cell) => _medical is not null &&
        WaterSlots.Concat(WaterOverflowSlots).Any(slot => Math.Abs(cell.X - slot.X) <= 2 && Math.Abs(cell.Z - slot.Z) <= 2);

    private MedicalSnapshot? _medical;
    private bool MedicalOwnsNavigation(ulong id) => _medical is { } m &&
        (m.WaterQueue.Contains(id) || m.Needs.Any(item => item.AgentId == id &&
            item.Intent is MedicalIntent.SeekWater or MedicalIntent.Rest or MedicalIntent.AwaitMedic or MedicalIntent.Leaving or MedicalIntent.Collapsed));
    public MedicalSnapshot? CaptureMedical() => _medical is null ? null : JsonSerializer.Deserialize<MedicalSnapshot>(JsonSerializer.Serialize(_medical));
    internal string? MedicalCanonicalJson => _medical is null ? null : JsonSerializer.Serialize(_medical);
    public bool MedicalBoundaryOnNextTick => !IsPaused && _medical is { } m && _preparation is { Status: PreparationStatus.Running } &&
        (m.Stage == MedicalStage.Clear && m.Needs.Single(item => item.AgentId == m.AtRiskGuestId).Thirst >= MedicalDistressThirst - 1 ||
         m.Stage == MedicalStage.Distress && CurrentTick + 1 >= m.WarningTick + MedicalCollapseDelayTicks ||
         m.Stage == MedicalStage.Collapsed && CurrentTick + 1 >= m.CollapseTick + MedicalCriticalDelayTicks ||
         m.Stage == MedicalStage.Critical && CurrentTick + 1 >= m.CollapseTick + MedicalDeathDelayTicks ||
         m.WaterOwnerId is { } waterOwner && m.Needs.Single(item => item.AgentId == waterOwner).Thirst <= MedicalDrinkThirstPerTick ||
         m.Needs.Any(item => item.Profile == MedicalNeedProfile.Performer &&
             (item.Stage == MedicalStage.Clear && item.Thirst >= MedicalDistressThirst - 1 && item.HeatExposure >= MedicalDistressHeat - 1 ||
              item.Stage == MedicalStage.Distress && CurrentTick + 1 >= item.WarningTick + MedicalCollapseDelayTicks ||
              item.Stage == MedicalStage.Collapsed && CurrentTick + 1 >= item.CollapseTick + MedicalCriticalDelayTicks ||
              item.Stage == MedicalStage.Critical && CurrentTick + 1 >= item.CollapseTick + MedicalDeathDelayTicks)) ||
         m.ResponseStage == MedicalResponseStage.Travelling && _navigationAgents[new(m.MedicId)].Action == AgentNavigationAction.Arrived ||
         m.ResponseStage == MedicalResponseStage.Treating && CurrentTick + 1 >= m.ResponseStartedTick + MedicalTreatmentTicks);

    public static GameSession CreateMedicalCampaign(ulong seed, int tier = 1)
    {
        var session = CreateEquipmentCampaign(seed, tier);
        session._equipment = session._equipment! with { Stage = EquipmentStage.Resolved, LoadPercent = 80,
            Response = "Hot scenario baseline: generator load balanced before opening" };
        var medicId = session.NextEntityId++;
        session._wallets.Add(new(medicId), new WalletState { OwnerId = new(medicId), CashPennies = 500 });
        session._preparation = session._preparation! with { People = session._preparation.People.Append(
            new EditionPerson(medicId, "Riley Hart", ProtectedPersonRole.Staff, 0)).ToArray() };
        var guests = session._preparation.People.Where(item => item.Role == ProtectedPersonRole.Guest).ToArray();
        var atRisk = guests[19].AgentId;
        var needs = guests.Select((guest, index) => new MedicalNeed(guest.AgentId,
            index == 19 ? 8_500 : index < 8 ? 7_600 : 2_000 + (index * 43) % 500,
            index == 19 ? 7_500 : 2_500, MedicalIntent.WatchShow,
            index == 19 ? "Strong act interest outweighs early water trip" : "Water need below show preference",
            -MedicalDecisionCooldownTicks, null, -1))
            .Concat(session._preparation.People.Where(item => item.Role == ProtectedPersonRole.Performer)
                .Select((person, index) => new MedicalNeed(person.AgentId, 6_900 + index * 100, 6_000 + index * 100,
                    MedicalIntent.WatchShow, "Performing; free water and first aid remain available",
                    -MedicalDecisionCooldownTicks, null, -1, MedicalNeedProfile.Performer))).ToArray();
        session._medical = new(5, true, medicId, atRisk, needs, [], [], null, 0,
            MedicalStage.Clear, MedicalResponseStage.None, null, -1, -1, -1, -1, "No response",
            [new("medical:hot", 0, "Fixed Hot scenario; free water and a baseline medic are available before opening.")]);
        return session;
    }

    private void MedicalEvent(string id, string description) => _medical = _medical! with
    { Evidence = _medical.Evidence.Append(new MedicalEvidence(id, CurrentTick, description)).ToArray() };

    private bool MedicalRouteExists(ulong agentId, GridCell destination)
    {
        var agent = _navigationAgents[new(agentId)];
        var origin = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
        return DeterministicPathfinder.FindPath(_traversalGrid!, origin, destination).Found;
    }

    private GridCell? MedicalResponseCell(ulong medicId, ulong patientId)
    {
        var patient = _navigationAgents[new(patientId)];
        var cell = TraversalGrid.WorldToCell(patient.XMillimetres, patient.ZMillimetres);
        foreach (var (dx, dz) in new (int X, int Z)[] { (4, 0), (-4, 0), (0, 4), (0, -4),
                     (3, 2), (-3, 2), (3, -2), (-3, -2), (2, 3), (-2, 3), (2, -3), (-2, -3) })
        {
            var candidate = new GridCell(cell.X + dx, cell.Z + dz);
            if (!_traversalGrid!.Contains(candidate) || WaterSlots.Contains(candidate) ||
                !_traversalGrid.Get(candidate).IsWalkable || !MedicalRouteExists(medicId, candidate)) continue;
            var centre = TraversalGrid.CellCentre(candidate);
            var px = (long)centre.XMillimetres - patient.XMillimetres;
            var pz = (long)centre.ZMillimetres - patient.ZMillimetres;
            if (px * px + pz * pz <= 6_250_000) return candidate;
        }
        return null;
    }

    private bool MedicalTreatmentPositionValid(MedicalSnapshot m)
    {
        var medic = _navigationAgents[new(m.MedicId)];
        if (m.ResponsePatientId is not { } patientId) return false;
        var patient = _navigationAgents[new(patientId)];
        var dx = (long)medic.XMillimetres - patient.XMillimetres;
        var dz = (long)medic.ZMillimetres - patient.ZMillimetres;
        return medic.Action == AgentNavigationAction.Arrived && patient.Action == AgentNavigationAction.Arrived &&
            dx * dx + dz * dz <= 6_250_000;
    }

    private CommandResult? ValidateMedicalCommand(EntityId? target, MedicalCommand command)
    {
        if (target is not null || _medical is not { } m || _preparation?.Status != PreparationStatus.Running || !Enum.IsDefined(command.Action))
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Medical actions require a live Hot edition.");
        var need = m.Needs.SingleOrDefault(item => item.AgentId == command.GuestId);
        if (need is null)
            return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "The selected person has no medical needs in this edition.");
        var patientStage = need.Profile == MedicalNeedProfile.Guest && command.GuestId == m.AtRiskGuestId ? m.Stage : need.Stage;
        if (m.Stage == MedicalStage.Terminal || patientStage is MedicalStage.Treated or MedicalStage.Removed or MedicalStage.Terminal)
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "This medical incident has settled.");
        if (command.GuestId == m.ResponsePatientId && command.Action != MedicalAction.DispatchMedic &&
            m.ResponseStage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter,
                "An active medical response owns this guest; allow physical travel and treatment or safe removal to finish.");
        if (command.Action == MedicalAction.GuideToRest && command.GuestId != m.AtRiskGuestId && need.Profile != MedicalNeedProfile.Performer)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter,
                "This prototype's first-aid rest route is reserved for the at-risk guest; guide other guests to free water.");
        if (command.Action == MedicalAction.DispatchMedic)
        {
            if (patientStage == MedicalStage.Clear)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No medical warning yet; guide the guest to free water or rest.");
            if (m.ResponseStage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Riley already owns this response; allow travel and treatment to finish.");
            if (!_preparation.People.Single(item => item.AgentId == m.MedicId).Admitted)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Riley has not physically arrived; wait for the medic or use a reachable safe-removal route.");
        }
        if (command.Action == MedicalAction.SafeRemove && (command.GuestId != m.AtRiskGuestId ||
            m.Stage != MedicalStage.Distress || m.ResponseStage != MedicalResponseStage.None))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Safe removal requires an ambulatory distressed guest and an unowned response.");
        if (command.Action is MedicalAction.GuideToWater or MedicalAction.GuideToRest or MedicalAction.ReturnToShow &&
            patientStage is MedicalStage.Collapsed or MedicalStage.Critical)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "A collapsed guest needs physical first aid, not an ordinary destination.");
        if (command.Action == MedicalAction.ReturnToShow && need.Intent != MedicalIntent.SeekWater && !m.WaterQueue.Contains(command.GuestId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Only a water visitor can leave this queue or approach.");
        if (command.Action == MedicalAction.GuideToWater && need.Intent != MedicalIntent.SeekWater && !m.WaterQueue.Contains(command.GuestId) &&
            m.WaterQueue.Length == WaterSlots.Length && m.WaterOverflow.Length == WaterOverflowSlots.Length)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "The free-water line and its visible overflow tail are full.");
        if (command.Action == MedicalAction.GuideToWater && need.Intent != MedicalIntent.SeekWater && !m.WaterQueue.Contains(command.GuestId) &&
            !MedicalRouteExists(command.GuestId, MedicalQueueApproach(m.WaterQueue.Length, m.WaterOverflow.Length)))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No walkable route to free water; clear the path or choose first aid/rest.");
        if (command.Action == MedicalAction.GuideToRest && !MedicalRouteExists(command.GuestId, MedicalRestCell))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No walkable route to the rest point; clear the path or dispatch aid.");
        if (command.Action == MedicalAction.SafeRemove && !MedicalRouteExists(command.GuestId, MedicalExitCell))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No walkable route to the gate; clear the path or dispatch aid.");
        if (command.Action == MedicalAction.DispatchMedic && MedicalResponseCell(m.MedicId, command.GuestId) is null)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Medic cannot reach a walkable position beside this guest; clear the route before dispatch.");
        return null;
    }

    private void ApplyMedicalCommand(MedicalCommand command)
    {
        var m = _medical!;
        var id = new EntityId(command.GuestId);
        if (command.Action == MedicalAction.GuideToWater) { SeekWater(command.GuestId, "Player guided to free water despite the show/wait tradeoff"); return; }
        if (command.Action == MedicalAction.ReturnToShow) { LeaveWater(command.GuestId, "Left the water queue to watch the band"); return; }
        if (command.Action == MedicalAction.GuideToRest)
        {
            LeaveWater(command.GuestId, "Rest chosen", reroute: false);
            SetNeed(command.GuestId, item => item with { Intent = MedicalIntent.Rest, Reason = "Rest chosen to reduce Hot exposure", QueueSlot = null });
            MedicalRelinquishPerformerStage(command.GuestId);
            ApplyAgentDestination(id, new(MedicalRestCell, "medical.rest"));
            MedicalEvent("medical:rest", "Guest routed physically to the shaded first-aid rest point.");
            return;
        }
        if (command.Action == MedicalAction.SafeRemove)
        {
            LeaveWater(command.GuestId, "Safe removal chosen", reroute: false);
            SetNeed(command.GuestId, item => item with { Intent = MedicalIntent.Leaving, Reason = "Safe removal via the main gate", QueueSlot = null });
            ApplyAgentDestination(id, new(MedicalExitCell, "medical.safe-removal"));
            _medical = _medical! with { ResponseStage = MedicalResponseStage.Removing, ResponsePatientId = command.GuestId,
                Response = "Safe removal en route; not protection until the guest reaches the gate" };
            MedicalEvent("medical:remove-dispatch", _medical.Response);
            return;
        }
        LeaveWater(command.GuestId, "Medic now owns response", reroute: false);
        MedicalRelinquishPerformerStage(command.GuestId);
        var patient = _navigationAgents[id];
        var patientCell = TraversalGrid.WorldToCell(patient.XMillimetres, patient.ZMillimetres);
        ApplyAgentDestination(id, new(patientCell, "medical.await-medic"));
        SetNeed(command.GuestId, item => item with {
            Intent = (item.AgentId == m.AtRiskGuestId ? m.Stage : item.Stage) is MedicalStage.Collapsed or MedicalStage.Critical
                ? MedicalIntent.Collapsed : MedicalIntent.AwaitMedic,
            Reason = "Awaiting physically dispatched medic", QueueSlot = null });
        // A medic cannot occupy the patient's cell. Reserve a walkable response
        // position beside it rather than waiting forever on collision avoidance.
        var responseCell = MedicalResponseCell(m.MedicId, command.GuestId)!.Value;
        ApplyAgentDestination(new(m.MedicId), new(responseCell, "medical.dispatch"));
        _medical = _medical! with { ResponseStage = MedicalResponseStage.Travelling, ResponsePatientId = command.GuestId,
            Response = "Medic dispatched; travel and six-second treatment still required" };
        MedicalEvent("medical:dispatch", _medical.Response);
    }

    private void SetNeed(ulong id, Func<MedicalNeed, MedicalNeed> change)
    {
        var m = _medical!; var needs = m.Needs.ToArray();
        var index = Array.FindIndex(needs, item => item.AgentId == id);
        needs[index] = change(needs[index]);
        _medical = m with { Needs = needs };
    }

    private void SeekWater(ulong id, string reason)
    {
        var m = _medical!;
        if (m.WaterQueue.Contains(id) || m.Needs.Any(item => item.AgentId == id && item.Intent == MedicalIntent.SeekWater)) return;
        if (m.WaterQueue.Length == WaterSlots.Length && m.WaterOverflow.Length == WaterOverflowSlots.Length)
        {
            SetNeed(id, item => item with { Reason = "The physical free-water line is full", LastDecisionTick = CurrentTick });
            return;
        }
        SetNeed(id, item => item with { Intent = MedicalIntent.SeekWater, Reason = reason,
            QueueSlot = null, LastDecisionTick = CurrentTick });
        MedicalRelinquishPerformerStage(id);
        ApplyAgentDestination(new(id), new(MedicalQueueApproach(m.WaterQueue.Length, m.WaterOverflow.Length), "medical.free-water-approach"));
        MedicalEvent("medical:water-seek", $"Person {id} walked toward the free-water line without reserving a place.");
    }

    private void RetargetWaterSeekers()
    {
        var m = _medical!;
        if (m.WaterQueue.Length == WaterSlots.Length && m.WaterOverflow.Length == WaterOverflowSlots.Length)
        {
            foreach (var need in m.Needs.Where(item => item.Intent == MedicalIntent.SeekWater && item.QueueSlot is null &&
                         !m.WaterOverflow.Contains(item.AgentId)).ToArray())
                LeaveWater(need.AgentId, "The physical water line is full");
            return;
        }
        var approach = MedicalQueueApproach(m.WaterQueue.Length, m.WaterOverflow.Length);
        foreach (var need in m.Needs.Where(item => item.Intent == MedicalIntent.SeekWater && item.QueueSlot is null))
        {
            if (m.WaterOverflow.Contains(need.AgentId)) continue;
            var nav = _navigationAgents[new(need.AgentId)];
            if (nav.Destination != approach)
                ApplyAgentDestination(new(need.AgentId), new(approach, "medical.free-water-approach"));
        }
    }

    private void AdmitWaterArrivals()
    {
        var m = _medical!;
        if (m.WaterQueue.Length == WaterSlots.Length && m.WaterOverflow.Length == WaterOverflowSlots.Length) return;
        var approach = MedicalQueueApproach(m.WaterQueue.Length, m.WaterOverflow.Length);
        var centre = TraversalGrid.CellCentre(approach);
        var arrived = m.Needs.Where(item => item.Intent == MedicalIntent.SeekWater && item.QueueSlot is null &&
                         !m.WaterOverflow.Contains(item.AgentId))
            .Select(item => (item.AgentId, Nav: _navigationAgents[new(item.AgentId)]))
            .Where(item => item.Nav.Destination == approach &&
                item.Nav.Action is AgentNavigationAction.Travelling or AgentNavigationAction.Arrived &&
                (long)(item.Nav.XMillimetres - centre.XMillimetres) * (item.Nav.XMillimetres - centre.XMillimetres) +
                (long)(item.Nav.ZMillimetres - centre.ZMillimetres) * (item.Nav.ZMillimetres - centre.ZMillimetres) <= 650L * 650)
            .OrderBy(item => item.AgentId).ToArray();
        foreach (var (id, _) in arrived)
        {
            m = _medical!;
            if (m.WaterQueue.Length < WaterSlots.Length)
            {
                var slot = m.WaterQueue.Length;
                _medical = m with { WaterQueue = m.WaterQueue.Append(id).ToArray() };
                SetNeed(id, item => item with { QueueSlot = slot, Reason = $"Joined the free-water line on physical arrival at tick {CurrentTick}" });
                ApplyAgentDestination(new(id), new(WaterSlots[slot], "medical.free-water-queue"));
                MedicalEvent("medical:queue-join", $"Person {id} arrived physically and took free-water place {slot}; no payment or stock transfer.");
            }
            else if (m.WaterOverflow.Length < WaterOverflowSlots.Length)
            {
                var slot = m.WaterOverflow.Length;
                _medical = m with { WaterOverflow = m.WaterOverflow.Append(id).ToArray() };
                SetNeed(id, item => item with { Reason = $"Reached visible water overflow place {slot} at tick {CurrentTick}" });
                ApplyAgentDestination(new(id), new(WaterOverflowSlots[slot], "medical.free-water-overflow"));
                MedicalEvent("medical:overflow-join", $"Person {id} reached visible overflow place {slot} after physical arrival.");
            }
            else break;
        }
        if (arrived.Length > 0) RetargetWaterSeekers();
    }

    private void LeaveWater(ulong id, string reason, bool reroute = true)
    {
        var m = _medical!;
        if (!m.WaterQueue.Contains(id))
        {
            if (!m.Needs.Any(item => item.AgentId == id && item.Intent == MedicalIntent.SeekWater)) return;
            if (m.WaterOverflow.Contains(id))
            {
                _medical = m with { WaterOverflow = m.WaterOverflow.Where(member => member != id).ToArray() };
                m = _medical!;
                for (var index = 0; index < m.WaterOverflow.Length; index++)
                    ApplyAgentDestination(new(m.WaterOverflow[index]), new(WaterOverflowSlots[index], "medical.free-water-overflow"));
            }
            SetNeed(id, item => item with { Intent = MedicalIntent.WatchShow, Reason = reason, QueueSlot = null,
                LastDecisionTick = CurrentTick });
            if (reroute) ReturnToListening(id);
            RetargetWaterSeekers();
            MedicalEvent("medical:queue-leave", $"Person {id} left the water approach before taking a place: {reason}.");
            return;
        }
        var ordered = m.WaterQueue.Where(item => item != id).ToArray();
        _medical = m with { WaterQueue = ordered, WaterOwnerId = m.WaterOwnerId == id ? null : m.WaterOwnerId,
            WaterDrinkTicks = m.WaterOwnerId == id ? 0 : m.WaterDrinkTicks };
        SetNeed(id, item => item with { Intent = MedicalIntent.WatchShow, Reason = reason, QueueSlot = null,
            LastDecisionTick = CurrentTick });
        for (var index = 0; index < ordered.Length; index++)
        {
            var member = ordered[index];
            var old = _medical!.Needs.Single(item => item.AgentId == member);
            if (old.QueueSlot == index) continue;
            SetNeed(member, item => item with { QueueSlot = index });
            ApplyAgentDestination(new(member), new(WaterSlots[index], "medical.free-water-queue"));
        }
        m = _medical!;
        if (m.WaterOverflow.Length > 0)
        {
            var promoted = m.WaterOverflow[0];
            var slot = m.WaterQueue.Length;
            _medical = m with { WaterQueue = m.WaterQueue.Append(promoted).ToArray(),
                WaterOverflow = m.WaterOverflow.Skip(1).ToArray() };
            SetNeed(promoted, item => item with { QueueSlot = slot, Reason = "Advanced from the visible overflow tail" });
            ApplyAgentDestination(new(promoted), new(WaterSlots[slot], "medical.free-water-queue"));
            m = _medical!;
            for (var index = 0; index < m.WaterOverflow.Length; index++)
                ApplyAgentDestination(new(m.WaterOverflow[index]), new(WaterOverflowSlots[index], "medical.free-water-overflow"));
        }
        if (reroute) ReturnToListening(id);
        RetargetWaterSeekers();
        MedicalEvent("medical:queue-leave", $"Guest {id} released their free-water reservation: {reason}.");
    }

    private void ReturnToListening(ulong id)
    {
        var place = _livePerformance?.Listeners.SingleOrDefault(item => item.AgentId == id)?.Place;
        if (place is { } cell) ApplyAgentDestination(new(id), new(cell, "performance.listen"));
        else if (_livePerformance?.Performers.SingleOrDefault(item => item.AgentId == id) is { } performer &&
                 _livePerformance.Stage is LiveSetStage.BeforeSet or LiveSetStage.Live or LiveSetStage.Interrupted)
            ApplyAgentDestination(new(id), new(performer.AccessCell, "medical.return-to-stage-access"));
        else
        {
            var index = Array.FindIndex(_preparation!.People, item => item.AgentId == id);
            ApplyAgentDestination(new(id), new(PreparedPlace(index), "medical.return"));
        }
    }

    private void MedicalRelinquishPerformerStage(ulong id)
    {
        if (_livePerformance is not { } live) return;
        var index = Array.FindIndex(live.Performers, item => item.AgentId == id);
        if (index < 0) return;
        var performers = live.Performers.ToArray();
        performers[index] = performers[index] with
        { AccessReached = false, StairReached = false, OnStage = false, InstrumentAttached = false };
        _livePerformance = live with { Performers = performers };
    }

    private void AdvanceMedical()
    {
        if (_medical is not { } m || _preparation is not { Status: PreparationStatus.Running } p) return;
        if (CurrentTick % 4 == 0)
        {
            var needs = m.Needs.Select(item => item with
            {
                Thirst = Math.Min(10_000, item.Thirst + 1),
                HeatExposure = Math.Min(10_000, item.HeatExposure + (item.AgentId == m.AtRiskGuestId || item.Profile == MedicalNeedProfile.Performer ? 1 : CurrentTick % 32 == 0 ? 1 : 0))
            }).ToArray();
            _medical = m = m with { Needs = needs };
        }
        if (CurrentTick % 80 == 0)
        {
            foreach (var need in m.Needs)
            {
                var person = p.People.Single(item => item.AgentId == need.AgentId);
                if (!person.Admitted || need.Intent is MedicalIntent.Rest or MedicalIntent.AwaitMedic or MedicalIntent.Leaving or MedicalIntent.Collapsed or MedicalIntent.Drinking ||
                    (m.Stage is MedicalStage.Collapsed or MedicalStage.Critical && need.AgentId == m.AtRiskGuestId) ||
                    need.Stage is MedicalStage.Collapsed or MedicalStage.Critical ||
                    CurrentTick - need.LastDecisionTick < MedicalDecisionCooldownTicks) continue;
                var nav = _navigationAgents[new(need.AgentId)];
                var from = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
                var travel = Math.Abs(from.X - MedicalWaterCell.X) + Math.Abs(from.Z - MedicalWaterCell.Z);
                var enthusiasm = _livePerformance?.Listeners.SingleOrDefault(item => item.AgentId == need.AgentId)?.Enthusiasm ?? 35;
                var waterScore = need.Thirst + need.HeatExposure / 3 + (need.Thirst >= MedicalDistressThirst ? 3_000 : 0) -
                    travel * 15 - m.WaterQueue.Length * 120;
                var showScore = 5_000 + enthusiasm * 40 + (_livePerformance?.Stage == LiveSetStage.Live ? 300 : 0) +
                    (need.Profile == MedicalNeedProfile.Performer ? 5_000 : 0) +
                    (need.AgentId == m.AtRiskGuestId ? 10_000 : 0);
                if (need.Intent == MedicalIntent.SeekWater)
                {
                    if (m.WaterOwnerId != need.AgentId && need.Thirst < 8_500 && showScore > waterScore + 1_200)
                        LeaveWater(need.AgentId, $"Band appeal {showScore} exceeded water utility {waterScore}; queue place released");
                }
                else if (waterScore > showScore)
                    SeekWater(need.AgentId, $"Hot thirst {need.Thirst}/10000 outweighed band {showScore}, travel {travel} cells and estimated wait {m.WaterQueue.Sum(id => m.Needs.Single(item => item.AgentId == id).Thirst / MedicalDrinkThirstPerTick)} ticks");
                else SetNeed(need.AgentId, item => item with { Reason = $"Watching band: music {showScore} vs water {waterScore} incl. travel/wait",
                    LastDecisionTick = CurrentTick });
                m = _medical!;
            }
        }
        m = _medical!;
        AdmitWaterArrivals();
        m = _medical!;
        if (m.WaterQueue.Length > 0)
        {
            var first = m.WaterQueue[0];
            var atTap = _navigationAgents[new(first)] is { Action: AgentNavigationAction.Arrived, Destination: { } destination } && destination == WaterSlots[0];
            if (m.WaterOwnerId is null && atTap)
            {
                _medical = m = m with { WaterOwnerId = first, WaterDrinkTicks = 0 };
                SetNeed(first, item => item with { Intent = MedicalIntent.Drinking,
                    Reason = "Drinking at the free tap after physical arrival; thirst and heat improve continuously" });
                MedicalEvent("medical:drink-start", $"Person {first} started drinking at the free tap after physical arrival.");
                m = _medical!;
            }
            if (m.WaterOwnerId == first && atTap)
            {
                SetNeed(first, item => item with { Thirst = Math.Max(0, item.Thirst - MedicalDrinkThirstPerTick),
                    HeatExposure = Math.Max(0, item.HeatExposure - MedicalDrinkHeatPerTick) });
                _medical = m = _medical! with { WaterDrinkTicks = m.WaterDrinkTicks + 1 };
                if (m.Needs.Single(item => item.AgentId == first).Thirst == 0)
                {
                    var drankTicks = m.WaterDrinkTicks;
                    LeaveWater(first, "Thirst reached zero after drinking", reroute: true);
                    SetNeed(first, item => item with { LastWaterTick = CurrentTick,
                        Reason = "Drank free water until thirst reached zero" });
                    MedicalEvent("medical:water", $"Person {first} finished drinking after {drankTicks} ticks; thirst zero, no payment or stock transfer.");
                }
            }
        }
        m = _medical!;
        foreach (var resting in m.Needs.Where(item => item.Profile == MedicalNeedProfile.Performer && item.Intent == MedicalIntent.Rest).ToArray())
        {
            if (_navigationAgents[new(resting.AgentId)] is { Action: AgentNavigationAction.Arrived, Destination: { } restCell } && restCell == MedicalRestCell)
                SetNeed(resting.AgentId, item => item with { HeatExposure = Math.Max(0, item.HeatExposure - 8) });
        }
        m = _medical!;
        var target = m.Needs.Single(item => item.AgentId == m.AtRiskGuestId);
        if (target.Intent == MedicalIntent.Rest && _navigationAgents[new(target.AgentId)] is { Action: AgentNavigationAction.Arrived, Destination: { } rest } && rest == MedicalRestCell)
        {
            SetNeed(target.AgentId, item => item with { HeatExposure = Math.Max(0, item.HeatExposure - 8) });
            m = _medical!; target = m.Needs.Single(item => item.AgentId == m.AtRiskGuestId);
        }
        if (m.Stage == MedicalStage.Clear && target.Thirst >= MedicalDistressThirst && target.HeatExposure >= MedicalDistressHeat)
        {
            _medical = m = m with { Stage = MedicalStage.Distress, WarningTick = CurrentTick };
            MedicalEvent("medical:distress", $"Guest {target.AgentId} distressed in Hot conditions: thirst {target.Thirst}, heat {target.HeatExposure}; free water, rest, safe removal and medic dispatch are available.");
        }
        m = _medical!; target = m.Needs.Single(item => item.AgentId == m.AtRiskGuestId);
        if (m.Stage == MedicalStage.Distress && (target.Thirst < MedicalDistressThirst || target.HeatExposure < MedicalDistressHeat))
        {
            const string relief = "Need relieved through free water or rest before collapse";
            var otherPatientActive = m.ResponsePatientId != target.AgentId &&
                m.ResponseStage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing;
            _medical = m with { Stage = MedicalStage.Treated,
                ResponseStage = otherPatientActive ? m.ResponseStage : MedicalResponseStage.Completed,
                Response = otherPatientActive ? m.Response : relief };
            if (target.Intent == MedicalIntent.Rest)
            {
                SetNeed(target.AgentId, item => item with { Intent = MedicalIntent.WatchShow,
                    Reason = "Rest relieved Hot exposure; free to return to the show" });
                ReturnToListening(target.AgentId);
            }
            MedicalEvent("medical:prevented", relief); return;
        }
        if (m.ResponseStage == MedicalResponseStage.Removing && _navigationAgents[new(target.AgentId)] is { Action: AgentNavigationAction.Arrived, Destination: { } exit } && exit == MedicalExitCell)
        {
            _medical = m with { Stage = MedicalStage.Removed, ResponseStage = MedicalResponseStage.Completed,
                Response = "Guest safely reached the main gate" };
            var people = p.People.Select(item => item.AgentId == target.AgentId ? item with { Departed = true } : item).ToArray();
            _preparation = p with { People = people };
            MedicalEvent("medical:removed", _medical.Response); return;
        }
        if (m.ResponseStage == MedicalResponseStage.Travelling && MedicalTreatmentPositionValid(m))
        {
            _medical = m = m with { ResponseStage = MedicalResponseStage.Treating, ResponseStartedTick = CurrentTick };
            MedicalEvent("medical:treatment-start", "Medic physically reached the guest; six-second response underway.");
        }
        if (m.ResponseStage == MedicalResponseStage.Treating && !MedicalTreatmentPositionValid(m))
        {
            _medical = m = m with { ResponseStage = MedicalResponseStage.None, ResponsePatientId = null, ResponseStartedTick = -1,
                Response = "Treatment interrupted: guest or medic moved out of reach; dispatch Riley again before the deadline" };
            MedicalEvent("medical:treatment-interrupted", m.Response);
        }
        if (m.ResponseStage == MedicalResponseStage.Treating && CurrentTick >= m.ResponseStartedTick + MedicalTreatmentTicks)
        {
            var patientId = m.ResponsePatientId!.Value;
            SetNeed(patientId, item => item with { Thirst = 2_000, HeatExposure = 3_000,
                Intent = MedicalIntent.WatchShow, Stage = MedicalStage.Treated,
                Reason = "Basic first aid completed after physical medic arrival" });
            ReturnToListening(patientId);
            _medical = _medical! with { Stage = patientId == m.AtRiskGuestId ? MedicalStage.Treated : m.Stage,
                ResponseStage = MedicalResponseStage.Completed, Response = "Basic first aid completed" };
            MedicalEvent("medical:treatment-complete", _medical.Response); return;
        }
        m = _medical!;
        if (m.Stage == MedicalStage.Distress && CurrentTick >= m.WarningTick + MedicalCollapseDelayTicks)
        {
            LeaveWater(m.AtRiskGuestId, "Collapsed before drinking", reroute: false);
            var patient = _navigationAgents[new(m.AtRiskGuestId)];
            ApplyAgentDestination(new(m.AtRiskGuestId), new(TraversalGrid.WorldToCell(patient.XMillimetres, patient.ZMillimetres), "medical.collapsed"));
            SetNeed(m.AtRiskGuestId, item => item with { Intent = MedicalIntent.Collapsed, Reason = "Collapsed; needs physical medic response", QueueSlot = null });
            _medical = _medical! with { Stage = MedicalStage.Collapsed, CollapseTick = CurrentTick,
                ResponseStage = m.ResponseStage == MedicalResponseStage.Removing ? MedicalResponseStage.None : m.ResponseStage };
            MedicalEvent("medical:collapse", "Guest collapsed after visible distress; untreated response window remains.");
        }
        m = _medical!;
        if (m.Stage == MedicalStage.Collapsed && CurrentTick >= m.CollapseTick + MedicalCriticalDelayTicks)
        {
            _medical = m with { Stage = MedicalStage.Critical, CriticalTick = CurrentTick };
            MedicalEvent("medical:critical", "Guest remains untreated after collapse; dispatch can still prevent death.");
        }
        m = _medical!;
        if (m.Stage == MedicalStage.Critical && CurrentTick >= m.CollapseTick + MedicalDeathDelayTicks)
        {
            ApplyMedicalDeath(m.AtRiskGuestId, m.WarningTick, m.CollapseTick, m.CriticalTick);
            return;
        }
        foreach (var performer in _medical!.Needs.Where(item => item.Profile == MedicalNeedProfile.Performer).ToArray())
        {
            m = _medical!;
            var need = m.Needs.Single(item => item.AgentId == performer.AgentId);
            if (need.Stage is MedicalStage.Treated or MedicalStage.Removed) continue;
            if (need.Stage == MedicalStage.Clear && need.Thirst >= MedicalDistressThirst && need.HeatExposure >= MedicalDistressHeat)
            {
                SetNeed(need.AgentId, item => item with { Stage = MedicalStage.Distress, WarningTick = CurrentTick });
                MedicalEvent("medical:distress", $"Performer {need.AgentId} distressed in Hot conditions; free water, rest and first aid are available.");
            }
            need = _medical!.Needs.Single(item => item.AgentId == performer.AgentId);
            if (need.Stage == MedicalStage.Distress && (need.Thirst < MedicalDistressThirst || need.HeatExposure < MedicalDistressHeat))
            {
                SetNeed(need.AgentId, item => item with { Stage = MedicalStage.Treated, Reason = "Need relieved before collapse" });
                if (need.Intent == MedicalIntent.Rest)
                {
                    SetNeed(need.AgentId, item => item with { Intent = MedicalIntent.WatchShow });
                    ReturnToListening(need.AgentId);
                }
                MedicalEvent("medical:prevented", $"Performer {need.AgentId} relieved through water or rest.");
                continue;
            }
            if (need.Stage == MedicalStage.Distress && CurrentTick >= need.WarningTick + MedicalCollapseDelayTicks)
            {
                LeaveWater(need.AgentId, "Collapsed before drinking", reroute: false);
                MedicalRelinquishPerformerStage(need.AgentId);
                var patient = _navigationAgents[new(need.AgentId)];
                ApplyAgentDestination(new(need.AgentId), new(TraversalGrid.WorldToCell(patient.XMillimetres, patient.ZMillimetres), "medical.collapsed"));
                SetNeed(need.AgentId, item => item with { Stage = MedicalStage.Collapsed, CollapseTick = CurrentTick,
                    Intent = MedicalIntent.Collapsed, Reason = "Collapsed; needs physical medic response", QueueSlot = null });
                MedicalEvent("medical:collapse", $"Performer {need.AgentId} collapsed after visible distress.");
            }
            need = _medical!.Needs.Single(item => item.AgentId == performer.AgentId);
            if (need.Stage == MedicalStage.Collapsed && CurrentTick >= need.CollapseTick + MedicalCriticalDelayTicks)
            {
                SetNeed(need.AgentId, item => item with { Stage = MedicalStage.Critical, CriticalTick = CurrentTick });
                MedicalEvent("medical:critical", $"Performer {need.AgentId} remains untreated after collapse.");
            }
            need = _medical!.Needs.Single(item => item.AgentId == performer.AgentId);
            if (need.Stage == MedicalStage.Critical && CurrentTick >= need.CollapseTick + MedicalDeathDelayTicks)
            {
                ApplyMedicalDeath(need.AgentId, need.WarningTick, need.CollapseTick, need.CriticalTick);
                return;
            }
        }
    }

    private void ApplyMedicalDeath(ulong victimId, long warningTick, long collapseTick, long criticalTick)
    {
        var m = _medical!; var p = _preparation!;
        var victim = p.People.Single(item => item.AgentId == victimId);
        var cause = $"In fixed Hot conditions {victim.Name} dried up after thirst {m.Needs.Single(item => item.AgentId == victim.AgentId).Thirst}/10000 and heat exposure; distress tick {warningTick}, collapse tick {collapseTick}, critical tick {criticalTick}; response: {m.Response}, job {m.ResponseStage}.";
        _medical = m with { Stage = MedicalStage.Terminal };
        MedicalEvent("medical:death", cause);
        var lifecycle = _lifecycle!; var attempt = CurrentAttempt();
        var transaction = $"medical-death:{CampaignId.Value}:{attempt.AttemptId}";
        lifecycle.Casualties.Add(new(lifecycle.NextCasualtyId++, attempt.AttemptId, victim.Name, victim.Role, cause, CurrentTick, transaction));
        lifecycle.CompletedOutcomeTransactionIds.Add(transaction);
        ReplaceAttempt(attempt with { Status = EditionAttemptStatus.Failed, OutcomeTransactionId = transaction });
        var hearing = $"medical-hearing:{CampaignId.Value}:{attempt.AttemptId}";
        lifecycle.Hearings.Add(new(lifecycle.NextHearingId++, attempt.AttemptId, HearingStatus.Open, hearing, null));
        lifecycle.CompletedOutcomeTransactionIds.Add(hearing);
        _preparation = p with { Status = PreparationStatus.Failed, Rentals = [], WorkContracts = [] };
        FinishLivePerformance();
    }

    private static string? ValidatePersistedMedical(MedicalSnapshot? m, SessionPersistenceSnapshot s)
    {
        if (m is null) return null;
        if (s.Preparation is not { } p || m.Version != 5 || !m.IsHot || m.Needs is null || m.WaterQueue is null || m.WaterOverflow is null ||
            m.Evidence is null || m.Needs.Length != p.Tier * 20 + 3 ||
            !m.Needs.Select(item => item.AgentId).SequenceEqual(p.People.Where(item => item.Role is ProtectedPersonRole.Guest or ProtectedPersonRole.Performer).Select(item => item.AgentId)) ||
            m.Needs.Any(item => item.Profile != (p.People.Single(person => person.AgentId == item.AgentId).Role == ProtectedPersonRole.Performer ? MedicalNeedProfile.Performer : MedicalNeedProfile.Guest)) ||
            !p.People.Any(item => item.AgentId == m.MedicId && item.Name == "Riley Hart" && item.Role == ProtectedPersonRole.Staff) ||
            m.AtRiskGuestId != m.Needs[19].AgentId || m.Needs.Any(item => item.Thirst is < 0 or > 10_000 || item.HeatExposure is < 0 or > 10_000 ||
                !Enum.IsDefined(item.Intent) || !Enum.IsDefined(item.Profile) || !Enum.IsDefined(item.Stage) ||
                item.QueueSlot is < 0 or >= 10 || item.LastDecisionTick > s.CurrentTick ||
                item.WarningTick > s.CurrentTick || item.CollapseTick > s.CurrentTick || item.CriticalTick > s.CurrentTick) ||
            m.WaterQueue.Length > WaterSlots.Length || m.WaterQueue.Distinct().Count() != m.WaterQueue.Length ||
            m.WaterOverflow.Length > WaterOverflowSlots.Length || m.WaterOverflow.Distinct().Count() != m.WaterOverflow.Length ||
            m.WaterOverflow.Any(id => m.WaterQueue.Contains(id)) ||
            m.WaterOverflow.Length > 0 && m.WaterQueue.Length != WaterSlots.Length ||
            m.WaterQueue.Where((id, index) => !m.Needs.Any(item => item.AgentId == id && item.QueueSlot == index)).Any() ||
            m.Needs.Any(item => item.QueueSlot is not null && !m.WaterQueue.Contains(item.AgentId)) ||
            m.Needs.Any(item => item.QueueSlot is not null && item.Intent is not (MedicalIntent.SeekWater or MedicalIntent.Drinking)) ||
            m.WaterOverflow.Where((id, index) => !m.Needs.Any(item => item.AgentId == id && item.Intent == MedicalIntent.SeekWater && item.QueueSlot is null) ||
                s.NavigationAgents?.SingleOrDefault(agent => agent.Id == id) is not { } nav ||
                nav.DestinationX != WaterOverflowSlots[index].X || nav.DestinationZ != WaterOverflowSlots[index].Z).Any() ||
            m.Needs.Any(item => item.Intent == MedicalIntent.SeekWater && item.QueueSlot is null && !m.WaterOverflow.Contains(item.AgentId) &&
                (s.NavigationAgents?.SingleOrDefault(agent => agent.Id == item.AgentId) is not { } nav ||
                 nav.DestinationX != MedicalQueueApproach(m.WaterQueue.Length, m.WaterOverflow.Length).X ||
                 nav.DestinationZ != MedicalQueueApproach(m.WaterQueue.Length, m.WaterOverflow.Length).Z)) ||
            m.WaterOwnerId is { } owner && (m.WaterQueue.Length == 0 || m.WaterQueue[0] != owner) ||
            m.WaterDrinkTicks < 0 || m.WaterDrinkTicks > s.CurrentTick ||
            m.WaterOwnerId is null && (m.WaterDrinkTicks != 0 || m.Needs.Any(item => item.Intent == MedicalIntent.Drinking)) ||
            m.WaterOwnerId is { } activeOwner && m.Needs.Single(item => item.AgentId == activeOwner).Intent != MedicalIntent.Drinking ||
            m.ResponsePatientId is { } responsePatient && !m.Needs.Any(item => item.AgentId == responsePatient) ||
            m.ResponseStage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing && m.ResponsePatientId is null ||
            !Enum.IsDefined(m.Stage) || !Enum.IsDefined(m.ResponseStage) ||
            m.WarningTick > s.CurrentTick || m.CollapseTick > s.CurrentTick || m.CriticalTick > s.CurrentTick ||
            m.Stage == MedicalStage.Terminal && (p.Status != PreparationStatus.Failed || s.Lifecycle?.Casualties.Length != 1) ||
            p.Status == PreparationStatus.Failed && m.Stage != MedicalStage.Terminal)
            return "Medical Hot state, queue ownership or causal stage invalid.";
        return null;
    }
}
