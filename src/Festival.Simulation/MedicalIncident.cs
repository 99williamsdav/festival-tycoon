using System.Text.Json;

namespace Festival.Simulation;

public enum MedicalStage { Clear, Distress, Collapsed, Critical, Treated, Removed, Terminal }
public enum MedicalIntent { WatchShow, SeekWater, Rest, AwaitMedic, Leaving, Collapsed, Refilling }
public enum MedicalResponseStage { None, Travelling, Treating, Removing, Completed }
public enum MedicalAction { GuideToWater, GuideToRest, DispatchMedic, SafeRemove, ReturnToShow }
public sealed record MedicalCommand(ulong GuestId, MedicalAction Action) : SessionCommand;
public sealed record MedicalNeed(ulong AgentId, int Thirst, int HeatExposure, MedicalIntent Intent,
    string Reason, long LastDecisionTick, int? QueueSlot, long LastWaterTick);
public sealed record MedicalEvidence(string Id, long Tick, string Description);
public sealed record MedicalSnapshot(int Version, bool IsHot, ulong MedicId, ulong AtRiskGuestId,
    MedicalNeed[] Needs, ulong[] WaterQueue, ulong? WaterOwnerId, int WaterRemainingTicks,
    MedicalStage Stage, MedicalResponseStage ResponseStage, long WarningTick, long CollapseTick,
    long CriticalTick, long ResponseStartedTick, string Response, MedicalEvidence[] Evidence);

public sealed partial class GameSession
{
    // Prototype Hot scenario, not clinical thresholds or a general weather model.
    public const int MedicalWaterServiceTicks = 560;       // 7 real seconds at 1×.
    public const int MedicalDecisionCooldownTicks = 240;   // 3 real seconds at 1×.
    public const int MedicalCollapseDelayTicks = 1_600;   // 20 real seconds after distress.
    public const int MedicalCriticalDelayTicks = 800;     // 10 real seconds after collapse.
    public const int MedicalDeathDelayTicks = 2_400;      // 30 real seconds after collapse.
    public const int MedicalTreatmentTicks = 480;        // 6 real seconds after physical arrival.
    public const int MedicalDistressThirst = 9_000;
    public const int MedicalDistressHeat = 8_000;
    public static readonly GridCell MedicalWaterCell = new(120, 150);   // (-3.75, 11.25) m; open field east of the stage.
    public static readonly GridCell MedicalTentCell = new(119, 172);    // (-4.25, 22.25) m; north of the audience.
    public static readonly GridCell MedicalMedicCell = new(122, 164);   // (-2.75, 18.25) m.
    public static readonly GridCell MedicalRestCell = new(119, 178);    // (-4.25, 25.25) m.
    public static readonly GridCell MedicalExitCell = new(128, 186);    // (0.25, 29.25) m.
    // A compact, staggered waiting patch with at least 1.5 m between standing centres.
    // Slot zero alone owns the tap. The approved visual is presentation, not a navmesh.
    private static readonly GridCell[] WaterSlots =
    [
        new(120, 155), new(117, 158), new(123, 158), new(120, 161), new(125, 161),
        new(115, 161), new(118, 164), new(124, 164), new(113, 164), new(127, 164)
    ];
    public static GridCell MedicalQueueSlot(int index) => WaterSlots[index];
    private bool MedicalQueueExcludesListening(GridCell cell) => _medical is not null &&
        WaterSlots.Any(slot => Math.Abs(cell.X - slot.X) <= 2 && Math.Abs(cell.Z - slot.Z) <= 2);

    private MedicalSnapshot? _medical;
    private bool MedicalOwnsNavigation(ulong id) => _medical is { } m &&
        (m.WaterQueue.Contains(id) || m.Needs.Any(item => item.AgentId == id &&
            item.Intent is MedicalIntent.Rest or MedicalIntent.AwaitMedic or MedicalIntent.Leaving or MedicalIntent.Collapsed));
    public MedicalSnapshot? CaptureMedical() => _medical is null ? null : JsonSerializer.Deserialize<MedicalSnapshot>(JsonSerializer.Serialize(_medical));
    internal string? MedicalCanonicalJson => _medical is null ? null : JsonSerializer.Serialize(_medical);
    public bool MedicalBoundaryOnNextTick => !IsPaused && _medical is { } m && _preparation is { Status: PreparationStatus.Running } &&
        (m.Stage == MedicalStage.Clear && m.Needs.Single(item => item.AgentId == m.AtRiskGuestId).Thirst >= MedicalDistressThirst - 1 ||
         m.Stage == MedicalStage.Distress && CurrentTick + 1 >= m.WarningTick + MedicalCollapseDelayTicks ||
         m.Stage == MedicalStage.Collapsed && CurrentTick + 1 >= m.CollapseTick + MedicalCriticalDelayTicks ||
         m.Stage == MedicalStage.Critical && CurrentTick + 1 >= m.CollapseTick + MedicalDeathDelayTicks ||
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
            -MedicalDecisionCooldownTicks, null, -1)).ToArray();
        session._medical = new(2, true, medicId, atRisk, needs, [], null, 0,
            MedicalStage.Clear, MedicalResponseStage.None, -1, -1, -1, -1, "No response",
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
        var patient = _navigationAgents[new(m.AtRiskGuestId)];
        var dx = (long)medic.XMillimetres - patient.XMillimetres;
        var dz = (long)medic.ZMillimetres - patient.ZMillimetres;
        return medic.Action == AgentNavigationAction.Arrived && patient.Action == AgentNavigationAction.Arrived &&
            dx * dx + dz * dz <= 6_250_000;
    }

    private CommandResult? ValidateMedicalCommand(EntityId? target, MedicalCommand command)
    {
        if (target is not null || _medical is not { } m || _preparation?.Status != PreparationStatus.Running || !Enum.IsDefined(command.Action))
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Medical actions require a live Hot edition.");
        if (!m.Needs.Any(item => item.AgentId == command.GuestId))
            return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "The selected person is not a guest in this edition.");
        if (m.Stage is MedicalStage.Terminal or MedicalStage.Treated or MedicalStage.Removed)
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "This medical incident has settled.");
        if (command.GuestId == m.AtRiskGuestId && command.Action != MedicalAction.DispatchMedic &&
            m.ResponseStage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter,
                "An active medical response owns this guest; allow physical travel and treatment or safe removal to finish.");
        if (command.Action == MedicalAction.GuideToRest && command.GuestId != m.AtRiskGuestId)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter,
                "This prototype's first-aid rest route is reserved for the at-risk guest; guide other guests to free water.");
        if (command.Action == MedicalAction.DispatchMedic)
        {
            if (command.GuestId != m.AtRiskGuestId)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Select the distressed guest before dispatching Riley.");
            if (m.Stage == MedicalStage.Clear)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No medical warning yet; guide the guest to free water or rest.");
            if (m.ResponseStage != MedicalResponseStage.None)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Riley already owns this response; allow travel and treatment to finish.");
            if (!_preparation.People.Single(item => item.AgentId == m.MedicId).Admitted)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Riley has not physically arrived; wait for the medic or use a reachable safe-removal route.");
        }
        if (command.Action == MedicalAction.SafeRemove && (command.GuestId != m.AtRiskGuestId ||
            m.Stage != MedicalStage.Distress || m.ResponseStage != MedicalResponseStage.None))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Safe removal requires an ambulatory distressed guest and an unowned response.");
        if (command.Action is MedicalAction.GuideToWater or MedicalAction.GuideToRest or MedicalAction.ReturnToShow &&
            m.Stage is MedicalStage.Collapsed or MedicalStage.Critical)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "A collapsed guest needs physical first aid, not an ordinary destination.");
        if (command.Action == MedicalAction.ReturnToShow && !m.WaterQueue.Contains(command.GuestId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Only a waiting water visitor can leave this queue.");
        if (command.Action == MedicalAction.GuideToWater && m.WaterQueue.Length >= WaterSlots.Length && !m.WaterQueue.Contains(command.GuestId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "The free-water queue is full; send the guest to rest or dispatch aid.");
        if (command.Action == MedicalAction.GuideToWater && !m.WaterQueue.Contains(command.GuestId) &&
            !MedicalRouteExists(command.GuestId, WaterSlots[m.WaterQueue.Length]))
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
        if (command.Action == MedicalAction.GuideToWater) { JoinWater(command.GuestId, "Player guided to free water despite the show/wait tradeoff"); return; }
        if (command.Action == MedicalAction.ReturnToShow) { LeaveWater(command.GuestId, "Left the water queue to watch the band"); return; }
        if (command.Action == MedicalAction.GuideToRest)
        {
            LeaveWater(command.GuestId, "Rest chosen", reroute: false);
            SetNeed(command.GuestId, item => item with { Intent = MedicalIntent.Rest, Reason = "Rest chosen to reduce Hot exposure", QueueSlot = null });
            ApplyAgentDestination(id, new(MedicalRestCell, "medical.rest"));
            MedicalEvent("medical:rest", "Guest routed physically to the shaded first-aid rest point.");
            return;
        }
        if (command.Action == MedicalAction.SafeRemove)
        {
            LeaveWater(command.GuestId, "Safe removal chosen", reroute: false);
            SetNeed(command.GuestId, item => item with { Intent = MedicalIntent.Leaving, Reason = "Safe removal via the main gate", QueueSlot = null });
            ApplyAgentDestination(id, new(MedicalExitCell, "medical.safe-removal"));
            _medical = _medical! with { ResponseStage = MedicalResponseStage.Removing, Response = "Safe removal en route; not protection until the guest reaches the gate" };
            MedicalEvent("medical:remove-dispatch", _medical.Response);
            return;
        }
        LeaveWater(command.GuestId, "Medic now owns response", reroute: false);
        var patient = _navigationAgents[id];
        var patientCell = TraversalGrid.WorldToCell(patient.XMillimetres, patient.ZMillimetres);
        ApplyAgentDestination(id, new(patientCell, "medical.await-medic"));
        SetNeed(command.GuestId, item => item with { Intent = MedicalIntent.AwaitMedic,
            Reason = "Awaiting physically dispatched medic", QueueSlot = null });
        // A medic cannot occupy the patient's cell. Reserve a walkable response
        // position beside it rather than waiting forever on collision avoidance.
        var responseCell = MedicalResponseCell(m.MedicId, command.GuestId)!.Value;
        ApplyAgentDestination(new(m.MedicId), new(responseCell, "medical.dispatch"));
        _medical = _medical! with { ResponseStage = MedicalResponseStage.Travelling,
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

    private void JoinWater(ulong id, string reason)
    {
        var m = _medical!;
        if (m.WaterQueue.Contains(id)) return;
        if (m.WaterQueue.Length >= WaterSlots.Length) return;
        var slot = m.WaterQueue.Length;
        _medical = m with { WaterQueue = m.WaterQueue.Append(id).ToArray() };
        SetNeed(id, item => item with { Intent = MedicalIntent.SeekWater, Reason = reason,
            QueueSlot = slot, LastDecisionTick = CurrentTick });
        ApplyAgentDestination(new(id), new(WaterSlots[slot], "medical.free-water-queue"));
        MedicalEvent("medical:queue-join", $"Guest {id} reserved free-water slot {slot}; no payment or stock transfer.");
    }

    private void LeaveWater(ulong id, string reason, bool reroute = true)
    {
        var m = _medical!;
        if (!m.WaterQueue.Contains(id)) return;
        var ordered = m.WaterQueue.Where(item => item != id).ToArray();
        _medical = m with { WaterQueue = ordered, WaterOwnerId = m.WaterOwnerId == id ? null : m.WaterOwnerId,
            WaterRemainingTicks = m.WaterOwnerId == id ? 0 : m.WaterRemainingTicks };
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
        if (reroute) ReturnToListening(id);
        MedicalEvent("medical:queue-leave", $"Guest {id} released their free-water reservation: {reason}.");
    }

    private void ReturnToListening(ulong id)
    {
        var place = _livePerformance?.Listeners.SingleOrDefault(item => item.AgentId == id)?.Place;
        if (place is { } cell) ApplyAgentDestination(new(id), new(cell, "performance.listen"));
        else
        {
            var index = Array.FindIndex(_preparation!.People, item => item.AgentId == id);
            ApplyAgentDestination(new(id), new(PreparedPlace(index), "medical.return"));
        }
    }

    private void AdvanceMedical()
    {
        if (_medical is not { } m || _preparation is not { Status: PreparationStatus.Running } p) return;
        if (CurrentTick % 4 == 0)
        {
            var needs = m.Needs.Select(item => item with
            {
                Thirst = Math.Min(10_000, item.Thirst + 1),
                HeatExposure = Math.Min(10_000, item.HeatExposure + (item.AgentId == m.AtRiskGuestId ? 1 : CurrentTick % 32 == 0 ? 1 : 0))
            }).ToArray();
            _medical = m = m with { Needs = needs };
        }
        if (CurrentTick % 80 == 0)
        {
            foreach (var need in m.Needs)
            {
                var person = p.People.Single(item => item.AgentId == need.AgentId);
                if (!person.Admitted || need.Intent is MedicalIntent.Rest or MedicalIntent.AwaitMedic or MedicalIntent.Leaving or MedicalIntent.Collapsed or MedicalIntent.Refilling ||
                    m.Stage is MedicalStage.Collapsed or MedicalStage.Critical && need.AgentId == m.AtRiskGuestId ||
                    CurrentTick - need.LastDecisionTick < MedicalDecisionCooldownTicks) continue;
                var nav = _navigationAgents[new(need.AgentId)];
                var from = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
                var travel = Math.Abs(from.X - MedicalWaterCell.X) + Math.Abs(from.Z - MedicalWaterCell.Z);
                var enthusiasm = _livePerformance?.Listeners.SingleOrDefault(item => item.AgentId == need.AgentId)?.Enthusiasm ?? 35;
                var waterScore = need.Thirst + need.HeatExposure / 3 + (need.Thirst >= MedicalDistressThirst ? 3_000 : 0) -
                    travel * 15 - m.WaterQueue.Length * 120;
                var showScore = 5_000 + enthusiasm * 40 + (_livePerformance?.Stage == LiveSetStage.Live ? 300 : 0);
                if (need.Intent == MedicalIntent.SeekWater)
                {
                    if (m.WaterOwnerId != need.AgentId && need.Thirst < 8_500 && showScore > waterScore + 1_200)
                        LeaveWater(need.AgentId, $"Band appeal {showScore} exceeded water utility {waterScore}; queue place released");
                }
                else if (waterScore > showScore && m.WaterQueue.Length < WaterSlots.Length)
                    JoinWater(need.AgentId, $"Hot thirst {need.Thirst}/10000 outweighed band {showScore}, travel {travel} cells and wait {m.WaterQueue.Length * MedicalWaterServiceTicks} ticks");
                else SetNeed(need.AgentId, item => item with { Reason = $"Watching band: music {showScore} vs water {waterScore} incl. travel/wait",
                    LastDecisionTick = CurrentTick });
                m = _medical!;
            }
        }
        m = _medical!;
        if (m.WaterQueue.Length > 0)
        {
            var first = m.WaterQueue[0];
            var atTap = _navigationAgents[new(first)] is { Action: AgentNavigationAction.Arrived, Destination: { } destination } && destination == WaterSlots[0];
            if (m.WaterOwnerId is null && atTap)
            {
                _medical = m = m with { WaterOwnerId = first, WaterRemainingTicks = MedicalWaterServiceTicks };
                SetNeed(first, item => item with { Intent = MedicalIntent.Refilling,
                    Reason = "Refilling at the free tap after physical arrival; relief follows completed service" });
                MedicalEvent("medical:refill-start", $"Guest {first} started a {MedicalWaterServiceTicks}-tick free refill at the tap.");
                m = _medical!;
            }
            if (m.WaterOwnerId == first && atTap)
            {
                _medical = m = m with { WaterRemainingTicks = m.WaterRemainingTicks - 1 };
                if (m.WaterRemainingTicks == 0)
                {
                    LeaveWater(first, "Completed free refill", reroute: true);
                    SetNeed(first, item => item with { Thirst = 1_500, HeatExposure = Math.Max(0, item.HeatExposure - 2_000),
                        LastWaterTick = CurrentTick, Reason = "Free water refilled after physical queue service" });
                    MedicalEvent("medical:water", $"Guest {first} completed {MedicalWaterServiceTicks} ticks at the free tap.");
                }
            }
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
            _medical = m with { Stage = MedicalStage.Treated, ResponseStage = MedicalResponseStage.Completed,
                Response = "Need relieved through free water or rest before collapse" };
            if (target.Intent == MedicalIntent.Rest)
            {
                SetNeed(target.AgentId, item => item with { Intent = MedicalIntent.WatchShow,
                    Reason = "Rest relieved Hot exposure; free to return to the show" });
                ReturnToListening(target.AgentId);
            }
            MedicalEvent("medical:prevented", _medical.Response); return;
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
            _medical = m = m with { ResponseStage = MedicalResponseStage.None, ResponseStartedTick = -1,
                Response = "Treatment interrupted: guest or medic moved out of reach; dispatch Riley again before the deadline" };
            MedicalEvent("medical:treatment-interrupted", m.Response);
        }
        if (m.ResponseStage == MedicalResponseStage.Treating && CurrentTick >= m.ResponseStartedTick + MedicalTreatmentTicks)
        {
            SetNeed(target.AgentId, item => item with { Thirst = 2_000, HeatExposure = 3_000,
                Intent = MedicalIntent.WatchShow, Reason = "Basic first aid completed after physical medic arrival" });
            ReturnToListening(target.AgentId);
            _medical = _medical! with { Stage = MedicalStage.Treated, ResponseStage = MedicalResponseStage.Completed,
                Response = "Basic first aid completed" };
            MedicalEvent("medical:treatment-complete", _medical.Response); return;
        }
        m = _medical!;
        if (m.Stage == MedicalStage.Distress && CurrentTick >= m.WarningTick + MedicalCollapseDelayTicks)
        {
            LeaveWater(m.AtRiskGuestId, "Collapsed before refill", reroute: false);
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
            ApplyMedicalDeath();
    }

    private void ApplyMedicalDeath()
    {
        var m = _medical!; var p = _preparation!;
        var victim = p.People.Single(item => item.AgentId == m.AtRiskGuestId);
        var cause = $"In fixed Hot conditions {victim.Name} dried up after thirst {m.Needs.Single(item => item.AgentId == victim.AgentId).Thirst}/10000 and heat exposure; distress tick {m.WarningTick}, collapse tick {m.CollapseTick}, critical tick {m.CriticalTick}; response: {m.Response}, job {m.ResponseStage}.";
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
        if (s.Preparation is not { } p || m.Version != 2 || !m.IsHot || m.Needs is null || m.WaterQueue is null ||
            m.Evidence is null || m.Needs.Length != p.Tier * 20 ||
            !m.Needs.Select(item => item.AgentId).SequenceEqual(p.People.Where(item => item.Role == ProtectedPersonRole.Guest).Select(item => item.AgentId)) ||
            !p.People.Any(item => item.AgentId == m.MedicId && item.Name == "Riley Hart" && item.Role == ProtectedPersonRole.Staff) ||
            m.AtRiskGuestId != m.Needs[19].AgentId || m.Needs.Any(item => item.Thirst is < 0 or > 10_000 || item.HeatExposure is < 0 or > 10_000 ||
                !Enum.IsDefined(item.Intent) || item.QueueSlot is < 0 or >= 10 || item.LastDecisionTick > s.CurrentTick) ||
            m.WaterQueue.Length > WaterSlots.Length || m.WaterQueue.Distinct().Count() != m.WaterQueue.Length ||
            m.WaterQueue.Where((id, index) => !m.Needs.Any(item => item.AgentId == id && item.QueueSlot == index)).Any() ||
            m.Needs.Any(item => item.QueueSlot is not null && !m.WaterQueue.Contains(item.AgentId)) ||
            m.WaterOwnerId is { } owner && (m.WaterQueue.Length == 0 || m.WaterQueue[0] != owner) ||
            m.WaterRemainingTicks is < 0 or > MedicalWaterServiceTicks ||
            m.WaterOwnerId is null && (m.WaterRemainingTicks != 0 || m.Needs.Any(item => item.Intent == MedicalIntent.Refilling)) ||
            m.WaterOwnerId is { } activeOwner && (m.WaterRemainingTicks == 0 ||
                m.Needs.Single(item => item.AgentId == activeOwner).Intent != MedicalIntent.Refilling) ||
            !Enum.IsDefined(m.Stage) || !Enum.IsDefined(m.ResponseStage) ||
            m.WarningTick > s.CurrentTick || m.CollapseTick > s.CurrentTick || m.CriticalTick > s.CurrentTick ||
            m.Stage == MedicalStage.Terminal && (p.Status != PreparationStatus.Failed || s.Lifecycle?.Casualties.Length != 1) ||
            p.Status == PreparationStatus.Failed && m.Stage != MedicalStage.Terminal)
            return "Medical Hot state, queue ownership or causal stage invalid.";
        return null;
    }
}
