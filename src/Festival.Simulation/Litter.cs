using System.Text.Json;

namespace Festival.Simulation;

public enum WasteLocation { Carried, Ground, Bin, Removed }
/// <param name="CarrierId">Someone other than the producer handling it: a goody two-shoes walking to pick it up (still on
/// the ground, with an Approach) or carrying it to a bin. Null when the producer carries it or nobody does.</param>
/// <param name="PickedUpTick">When a goody two-shoes picked it up: their carrying clock starts there, not at the meal.</param>
public sealed record WastePiece(string Id, ulong ProducerId, ImmersionProduct Product, long CompletedTick,
    WasteLocation Location, int XMillimetres, int ZMillimetres, string? BinId = null,
    GridCell? Approach = null, long ActionTick = -1, ulong? CarrierId = null, long PickedUpTick = -1)
{
    [System.Text.Json.Serialization.JsonIgnore] public ulong Carrier => CarrierId ?? ProducerId;
    [System.Text.Json.Serialization.JsonIgnore] public long CarryStartTick => PickedUpTick >= 0 ? PickedUpTick : CompletedTick;
}
public sealed record CleanupSweep(ulong WorkerId, GridCell Centre, int RadiusCells, int Remaining,
    long UntilTick, bool Manual, string? TargetId = null, bool TargetIsBin = false,
    GridCell? Approach = null, long ActionTick = -1, long CooldownUntil = 0);
public sealed record LitterSnapshot(int Version, WastePiece[] Pieces, CleanupSweep[] Sweeps);
internal readonly record struct PersistedImmersionPurchaseLookup(ulong AgentId, ImmersionProduct Product, long Tick);
public sealed record CleanUpCommand(ulong WorkerId) : SessionCommand;
public sealed record BinReadModel(string Id, GridCell Cell, int QuarterTurns, int Pieces)
{
    public int Capacity => LitterRules.BinCapacity;
    public int FullPercent => Pieces * 100 / Capacity;
    public int ExtraPieces => Math.Max(0, Pieces - Capacity);
    public bool Wasps => Pieces >= Capacity;
    public bool CanEmpty => Pieces * 100 >= Capacity * 90;
}

public static class LitterRules
{
    public const int BinCapacity = 20;
    public const int CarryTicks = 160, DisposalTicks = 80, EmptyTicks = 240;
    public const int MaximumCarryTicks = 960;
    public const int LocalRadiusCells = 16, ManualRadiusCells = 32, TriggerPieces = 4;
    public const int LocalTargets = 6, ManualTargets = 12;
    public const int LocalDurationTicks = 2400, ManualDurationTicks = 4800, CooldownTicks = 800;
    public const int GroundEffectRadiusCells = 8, WaspRadiusCells = 6;
    public const int GroundLossCap = 5, WaspLoss = 4;
    /// <summary>A guest this careful (litter carelessness at most this) is a goody two-shoes and picks up others' litter.</summary>
    public const int GoodyTwoShoesMaximum = 7;
    /// <summary>How near a piece must be for an idle goody two-shoes to fetch it.</summary>
    public const int GoodyReachCells = 12;
    /// <summary>The longest walk anyone will make to a bin (see <see cref="WillUseBin"/>).</summary>
    public const int BinWalkLimitTicks = 640;
    /// <summary>Litter, cleanup and nuisance advance once per festival second.</summary>
    public const int SecondTicks = 80;

    public static int Dickishness(ulong seed, ulong id)
    {
        var value = unchecked(seed + id * 0x9E3779B97F4A7C15UL);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return (int)((value ^ (value >> 31)) % 101);
    }
    // A short detour is acceptable to most people; the personality is deliberately hidden.
    public static bool WillUseBin(int dickishness, int walkTicks, int detourTicks, bool urgent, ProtectedPersonRole role) =>
        !urgent && walkTicks <= BinWalkLimitTicks && (role != ProtectedPersonRole.Guest ||
            dickishness + walkTicks / 16 + Math.Max(0, detourTicks) / 16 < 105);
}

public sealed partial class GameSession
{
    private LitterSnapshot? _litter;
    private static readonly LitterSnapshot EmptyLitter = new(1, [], []);
    public LitterSnapshot? CaptureLitter() => _litter;
    private WastePiece? CaptureWaste(string id) { EnsureWasteIndices(); return _wasteById.GetValueOrDefault(id); }
    public WastePiece? CaptureCarriedWaste(ulong id) { EnsureWasteIndices(); return _carriedByPerson.TryGetValue(id, out var waste) ? _wasteById[waste] : null; }
    public bool WasteCarryEligible(ulong id) => CaptureCarriedWaste(id) is not null && !HigherPriorityOwns(id) && !CleanupOwnsNavigation(id);
    internal string? LitterCanonicalJson => _litter is null ? null : JsonSerializer.Serialize(_litter);
    public IReadOnlyList<BinReadModel> CaptureBins()
    {
        EnsureWasteIndices();
        return (_preparation?.BuildPlacements ?? []).Where(p => p.Kind == BuildServiceKind.Bin)
            .Select(p => new BinReadModel(p.Id, p.Cell, p.QuarterTurns, _binCounts.GetValueOrDefault(p.Id))).ToArray();
    }
    private void SetWaste(WastePiece piece)
    {
        EnsureWasteIndices();
        var previous = _wasteById[piece.Id];
        UnindexWaste(previous); IndexWaste(piece);
        // A copy keeps captured snapshots immutable; the index finds the slot without a scan.
        var pieces = (WastePiece[])_litter!.Pieces.Clone();
        pieces[_wasteSlot[piece.Id]] = piece;
        _litter = _litter with { Pieces = pieces };
        _indexedPieces = pieces;
        if (previous.Location != piece.Location && (previous.Location is WasteLocation.Ground or WasteLocation.Bin || piece.Location is WasteLocation.Ground or WasteLocation.Bin))
            LitterVisualVersion++;
    }
    private void SetSweep(CleanupSweep sweep) => _litter = _litter! with
        { Sweeps = _litter.Sweeps.Where(s => s.WorkerId != sweep.WorkerId).Append(sweep).OrderBy(s => s.WorkerId).ToArray() };
    private bool WasteOwnsNavigation(ulong id) { EnsureWasteIndices(); return _carriedByPerson.ContainsKey(id) || _pickupByPerson.ContainsKey(id); }
    private bool CleanupOwnsNavigation(ulong id) => _litter?.Sweeps.Any(s => s.WorkerId == id && s.Remaining > 0) == true;
    private void RecordCompletedWaste(ulong id, ImmersionHeldItem held)
    {
        _litter ??= EmptyLitter;
        EnsureWasteIndices();
        if (_wasteById.ContainsKey(held.TransactionId)) return;
        var nav = _navigationAgents[new(id)];
        _litter = _litter with { Pieces = _litter.Pieces.Append(new WastePiece(held.TransactionId, id, held.Product,
            CurrentTick, WasteLocation.Carried, nav.XMillimetres, nav.ZMillimetres)).ToArray() };
        _wasteSlot[held.TransactionId] = _litter.Pieces.Length - 1;
        IndexWaste(_litter.Pieces[^1]); _indexedPieces = _litter.Pieces;
    }
    private static long CellDistanceSquared(GridCell a, GridCell b) => (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Z - b.Z) * (a.Z - b.Z);
    private GridCell PersonCell(ulong id) => TraversalGrid.WorldToCell(_navigationAgents[new(id)].XMillimetres, _navigationAgents[new(id)].ZMillimetres);
    private bool LitterUrgent(ulong id) => _persons[id] is { } p &&
        (p.Thirst >= 7000 || p.HeatExposure >= 7000 || p.ToiletNeed >= 7500 || p.Hunger >= 8000 ||
         p.HealthStage is MedicalStage.Collapsed or MedicalStage.Critical || IsCurrentProgrammePerformer(id));
    private bool HigherPriorityOwns(ulong id) => HasClaim(id, PersonClaims.MedicalNavigation | PersonClaims.ResponseAssigned |
        PersonClaim.Fighting | PersonClaim.Performing | PersonClaim.ToiletVisit | PersonClaim.Shopping | PersonClaim.Maintaining);
    private bool AtLitterCell(ulong id, GridCell cell) => _navigationAgents[new(id)] is { Action: AgentNavigationAction.Arrived } nav &&
        nav.Destination == cell && (nav.XMillimetres, nav.ZMillimetres) == TraversalGrid.CellCentre(cell);
    private GridCell? ReachableBinSide(ulong id, GridCell bin)
    {
        var here = PersonCell(id);
        return new[] { new GridCell(bin.X, bin.Z + 2), new(bin.X + 2, bin.Z), new(bin.X, bin.Z - 2), new(bin.X - 2, bin.Z) }
            .OrderBy(c => CellDistanceSquared(c, here)).ThenBy(c => c.X).ThenBy(c => c.Z)
            .Where(c => _traversalGrid!.Contains(c) && _traversalGrid.Get(c).IsWalkable &&
                DeterministicPathfinder.FindPath(_traversalGrid, here, c).Found).Select(c => (GridCell?)c).FirstOrDefault();
    }
    private void DropWaste(WastePiece piece, bool reroute)
    {
        var carrier = piece.Carrier;
        var nav = _navigationAgents[new(carrier)];
        SetWaste(piece with { Location = WasteLocation.Ground, XMillimetres = nav.XMillimetres, ZMillimetres = nav.ZMillimetres,
            BinId = null, Approach = null, ActionTick = -1, CarrierId = null, PickedUpTick = -1 });
        if (reroute) ReturnToListening(carrier);
    }
    private void ReleaseWasteAtExit(ulong id)
    {
        if (CaptureCarriedWaste(id) is { } waste) DropWaste(waste, false);
        ReleaseGoodyPickup(id);
        InterruptCleanup(id);
    }
    private void AdvanceCarriedWaste()
    {
        EnsureWasteIndices();
        foreach (var original in _carriedByPerson.OrderBy(p => p.Key).Select(p => _wasteById[p.Value]).ToArray())
        {
            var id = original.Carrier; var p = _persons[id]; var piece = original;
            // Release the rubbish at the actual position; a safety/service/departure route is never replaced.
            if (p.Departed || HigherPriorityOwns(id) || LitterUrgent(id) || ImmersionDepartureActive)
            { DropWaste(piece, false); continue; }
            if (CurrentTick - piece.CarryStartTick < LitterRules.CarryTicks) continue;
            if (CurrentTick - piece.CarryStartTick >= LitterRules.MaximumCarryTicks)
            { DropWaste(piece, true); continue; }
            if (piece.Approach is { } approach && piece.BinId is not null)
            {
                if (_navigationAgents[new(id)].Action == AgentNavigationAction.NoRoute) { DropWaste(piece, true); continue; }
                if (!AtLitterCell(id, approach)) continue;
                if (piece.ActionTick < 0) SetWaste(piece with { ActionTick = CurrentTick });
                else if (CurrentTick - piece.ActionTick >= LitterRules.DisposalTicks)
                {
                    SetWaste(piece with { Location = WasteLocation.Bin, Approach = null, ActionTick = -1, CarrierId = null, PickedUpTick = -1 });
                    ReturnToListening(id);
                }
                continue;
            }
            if (piece.ActionTick >= 0)
            {
                if (CurrentTick - piece.ActionTick >= LitterRules.DisposalTicks) DropWaste(piece, false);
                continue;
            }
            var here = PersonCell(id); var destination = _navigationAgents[new(id)].Destination ?? here;
            var choice = CaptureBins().OrderBy(b => CellDistanceSquared(b.Cell, here)).ThenBy(b => b.Id, StringComparer.Ordinal)
                .Take(4).Select(b => (Bin: b, Side: ReachableBinSide(id, b.Cell))).FirstOrDefault(c => c.Side is not null &&
                    LitterRules.WillUseBin(LitterRules.Dickishness(CampaignSeed, id), EstimateWalkTicks(id, here, c.Side.Value),
                        EstimateWalkTicks(id, here, c.Side.Value) + EstimateWalkTicks(id, c.Side.Value, destination) - EstimateWalkTicks(id, here, destination), false, p.Role));
            if (choice.Side is { } side)
            {
                SetWaste(piece with { BinId = choice.Bin.Id, Approach = side });
                ApplyAgentDestination(new(id), new(side, "litter.dispose"));
            }
            else SetWaste(piece with { ActionTick = CurrentTick });
        }
    }

    private CommandResult? ValidateCleanUp(EntityId? target, CleanUpCommand command) => target is not null ||
        _preparation?.Status != PreparationStatus.Running || !GetStewardResponses().Any(s => s.WorkerId == command.WorkerId) ||
        StaffUnavailableReason(command.WorkerId) is { } || WasteOwnsNavigation(command.WorkerId)
        ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Choose an available on-duty steward for cleanup.") : null;
    private void ApplyCleanUp(CleanUpCommand command) => BeginSweep(command.WorkerId, true);
    private void BeginSweep(ulong id, bool manual)
    {
        RecallWorker(id, "Steward starting a bounded cleanup sweep");
        var centre = StaffDutyCell(id, ResponseRole.Steward);
        // Litter advances once a second, so the deadline lands on a litter second: a manual sweep
        // started mid-second would otherwise outlive its deadline until the next one.
        var until = CurrentTick + (manual ? LitterRules.ManualDurationTicks : LitterRules.LocalDurationTicks);
        until += (LitterRules.SecondTicks - until % LitterRules.SecondTicks) % LitterRules.SecondTicks;
        SetSweep(new(id, centre, manual ? LitterRules.ManualRadiusCells : LitterRules.LocalRadiusCells,
            manual ? LitterRules.ManualTargets : LitterRules.LocalTargets, until, manual));
    }
    private void EndSweep(CleanupSweep job, bool returnToPost)
    {
        SetSweep(job with { Remaining = 0, TargetId = null, Approach = null, ActionTick = -1, CooldownUntil = CurrentTick + LitterRules.CooldownTicks });
        if (returnToPost && !HigherPriorityOwns(job.WorkerId)) ReturnToListening(job.WorkerId);
    }
    private void InterruptCleanup(ulong id)
    {
        if (_litter?.Sweeps.SingleOrDefault(s => s.WorkerId == id && s.Remaining > 0) is { } sweep) EndSweep(sweep, false);
    }

    // Ground pieces are spatially indexed only when the authoritative piece array changes.
    // Effect and sweep queries visit nearby buckets, never every piece every frame.
    private WastePiece[]? _indexedPieces;
    private readonly Dictionary<(int X, int Z), List<WastePiece>> _groundBuckets = [];
    private readonly Dictionary<string, WastePiece> _wasteById = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, string> _carriedByPerson = [];
    private readonly Dictionary<string, int> _binCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _wasteSlot = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, string> _pickupByPerson = [];
    /// <summary>
    /// Presentation-only change counter: moves whenever ground litter or bin contents change, so the
    /// renderer redraws only then. Not saved or hashed; a restored session starts it afresh.
    /// </summary>
    public int LitterVisualVersion { get; private set; }
    private static (int X, int Z) GroundBucket(WastePiece p)
    { var c = TraversalGrid.WorldToCell(p.XMillimetres, p.ZMillimetres); return (c.X / 8, c.Z / 8); }
    private void IndexWaste(WastePiece p)
    {
        _wasteById[p.Id] = p;
        if (p.Location == WasteLocation.Carried) _carriedByPerson[p.Carrier] = p.Id;
        if (p.Location == WasteLocation.Ground && p.CarrierId is { } picker) _pickupByPerson[picker] = p.Id;
        if (p.Location == WasteLocation.Bin) _binCounts[p.BinId!] = _binCounts.GetValueOrDefault(p.BinId!) + 1;
        if (p.Location == WasteLocation.Ground)
        {
            var key = GroundBucket(p);
            if (!_groundBuckets.TryGetValue(key, out var bucket)) _groundBuckets[key] = bucket = [];
            bucket.Add(p);
        }
    }
    private void UnindexWaste(WastePiece p)
    {
        _wasteById.Remove(p.Id);
        if (p.Location == WasteLocation.Carried) _carriedByPerson.Remove(p.Carrier);
        if (p.Location == WasteLocation.Ground && p.CarrierId is { } picker) _pickupByPerson.Remove(picker);
        if (p.Location == WasteLocation.Bin) _binCounts[p.BinId!]--;
        if (p.Location == WasteLocation.Ground) _groundBuckets[GroundBucket(p)].Remove(p);
    }
    private void EnsureWasteIndices()
    {
        var pieces = _litter?.Pieces;
        if (ReferenceEquals(_indexedPieces, pieces)) return;
        _groundBuckets.Clear(); _wasteById.Clear(); _carriedByPerson.Clear(); _binCounts.Clear(); _wasteSlot.Clear(); _pickupByPerson.Clear();
        for (var i = 0; i < (pieces?.Length ?? 0); i++) { IndexWaste(pieces![i]); _wasteSlot[pieces[i].Id] = i; }
        _indexedPieces = pieces;
        LitterVisualVersion++;
    }
    private IEnumerable<WastePiece> GroundNear(GridCell centre, int radius)
    {
        EnsureWasteIndices();
        for (var x = Math.Max(0, centre.X - radius) / 8; x <= (centre.X + radius) / 8; x++)
        for (var z = Math.Max(0, centre.Z - radius) / 8; z <= (centre.Z + radius) / 8; z++)
            if (_groundBuckets.TryGetValue((x, z), out var bucket)) foreach (var piece in bucket)
                if (CellDistanceSquared(centre, TraversalGrid.WorldToCell(piece.XMillimetres, piece.ZMillimetres)) <= (long)radius * radius) yield return piece;
    }
    private void AdvanceCleanup()
    {
        foreach (var original in _litter!.Sweeps.Where(s => s.Remaining > 0).ToArray())
        {
            var job = original; var id = job.WorkerId;
            if (_preparation?.Status != PreparationStatus.Running || CurrentTick >= job.UntilTick || HigherPriorityOwns(id) || LitterUrgent(id) ||
                job.Centre != StaffDutyCell(id, ResponseRole.Steward)) { EndSweep(job, !HigherPriorityOwns(id)); continue; }
            if (job.TargetId is not null && job.Approach is { } approach)
            {
                if (_navigationAgents[new(id)].Action == AgentNavigationAction.NoRoute) { EndSweep(job, true); continue; }
                if (!AtLitterCell(id, approach)) continue;
                if (job.ActionTick < 0) { SetSweep(job with { ActionTick = CurrentTick }); continue; }
                if (CurrentTick - job.ActionTick < (job.TargetIsBin ? LitterRules.EmptyTicks : LitterRules.DisposalTicks)) continue;
                if (job.TargetIsBin)
                {
                    // Recheck the user's 90% threshold at the actual bin, even for manual cleanup.
                    if (CaptureBins().Single(b => b.Id == job.TargetId).CanEmpty)
                    {
                        _litter = _litter with { Pieces = _litter.Pieces.Select(w => w.Location == WasteLocation.Bin && w.BinId == job.TargetId ?
                            w with { Location = WasteLocation.Removed, BinId = null } : w).ToArray() };
                        LitterVisualVersion++; // Also bumped by the index rebuild; explicit so no reader depends on that order.
                    }
                }
                else if (CaptureWaste(job.TargetId!) is { Location: WasteLocation.Ground } waste)
                    SetWaste(waste with { Location = WasteLocation.Removed });
                job = job with { Remaining = job.Remaining - 1, TargetId = null, Approach = null, ActionTick = -1 };
                SetSweep(job);
                if (job.Remaining == 0) { EndSweep(job, true); continue; }
            }
            if (job.TargetId is not null) continue;
            bool Claimed(string target, bool bin) => _litter.Sweeps.Any(s => s.Remaining > 0 && s.TargetId == target && s.TargetIsBin == bin);
            var targets = CaptureBins().Where(b => b.CanEmpty && CellDistanceSquared(b.Cell, job.Centre) <= (long)job.RadiusCells * job.RadiusCells && !Claimed(b.Id, true))
                .Select(b => (Id: b.Id, Bin: true, Cell: b.Cell))
                .Concat(GroundNear(job.Centre, job.RadiusCells).Where(w => w.CarrierId is null && !Claimed(w.Id, false))
                    .Select(w => (Id: w.Id, Bin: false, Cell: TraversalGrid.WorldToCell(w.XMillimetres, w.ZMillimetres))))
                // Sort the whole candidate set before capping it: the spatial index's bucket order
                // depends on history (a restored game rebuilds it in array order), so it must never pick.
                .OrderBy(t => CellDistanceSquared(t.Cell, PersonCell(id))).ThenBy(t => t.Id, StringComparer.Ordinal).Take(8);
            var assigned = false;
            foreach (var t in targets)
            {
                var side = t.Bin ? ReachableBinSide(id, t.Cell) : _traversalGrid!.Get(t.Cell).IsWalkable &&
                    DeterministicPathfinder.FindPath(_traversalGrid, PersonCell(id), t.Cell).Found ? (GridCell?)t.Cell : null;
                if (side is null) continue;
                SetSweep(job with { TargetId = t.Id, TargetIsBin = t.Bin, Approach = side });
                ApplyAgentDestination(new(id), new(side.Value, t.Bin ? "litter.empty-bin" : "litter.pick-up"));
                assigned = true; break;
            }
            if (!assigned) EndSweep(job, true);
        }
        if (_preparation?.Status != PreparationStatus.Running) return;
        foreach (var worker in GetStewardResponses().OrderBy(s => s.WorkerId))
        {
            var id = worker.WorkerId;
            if (CleanupOwnsNavigation(id) || WasteOwnsNavigation(id) || StaffUnavailableReason(id) is not null || LitterUrgent(id) ||
                _litter.Sweeps.Any(s => s.WorkerId == id && s.CooldownUntil > CurrentTick)) continue;
            var centre = StaffDutyCell(id, ResponseRole.Steward);
            if (GroundNear(centre, LitterRules.LocalRadiusCells).Take(LitterRules.TriggerPieces).Count() >= LitterRules.TriggerPieces ||
                CaptureBins().Any(b => b.CanEmpty && CellDistanceSquared(b.Cell, centre) <= LitterRules.LocalRadiusCells * LitterRules.LocalRadiusCells)) BeginSweep(id, false);
        }
    }
    private void AdvanceLitter()
    {
        if (_litter is null || !MedicalOperationsActive || CurrentTick % LitterRules.SecondTicks != 0) return;
        AdvanceGoodyPickups(); AdvanceCarriedWaste(); AdvanceCleanup();
        var wasps = CaptureBins().Where(b => b.Wasps).ToArray();
        foreach (var p in PeopleIn(PersonView.Roster).Where(p => p.Admitted && !p.Departed))
        {
            var here = PersonCell(p.Id);
            var nuisance = GroundNear(here, LitterRules.GroundEffectRadiusCells).Take(LitterRules.GroundLossCap).Count();
            var nearby = wasps.FirstOrDefault(b => CellDistanceSquared(b.Cell, here) <= LitterRules.WaspRadiusCells * LitterRules.WaspRadiusCells);
            if (nearby is not null) nuisance += LitterRules.WaspLoss;
            nuisance = UnpleasantFor(p.Id, nuisance);
            if (nuisance > 0) MutatePerson(p.Id, person => person.Satisfaction = Math.Max(0, person.Satisfaction - nuisance));
            if (nearby is not null) MaybeWaspSting(p.Id);
            if (nearby is not null && !HigherPriorityOwns(p.Id) && !WasteOwnsNavigation(p.Id) && !CleanupOwnsNavigation(p.Id) &&
                !LitterUrgent(p.Id) && p.Role == ProtectedPersonRole.Guest && _navigationAgents[new(p.Id)].Action == AgentNavigationAction.Arrived)
            {
                var away = new GridCell(here.X + Math.Sign(here.X - nearby.Cell.X) * 3, here.Z + (here.Z == nearby.Cell.Z ? 3 : Math.Sign(here.Z - nearby.Cell.Z) * 3));
                if (_traversalGrid!.Contains(away) && _traversalGrid.Get(away).IsWalkable && DeterministicPathfinder.FindPath(_traversalGrid, here, away).Found)
                    ApplyAgentDestination(new(p.Id), new(away, "litter.avoid-wasps"));
            }
        }
    }
    private void BlockLitterBins()
    {
        if (_traversalGrid is null) return;
        var cells = _traversalGrid.Overrides.ToDictionary(p => p.Key, p => p.Value);
        foreach (var bin in CaptureBins()) foreach (var cell in BinSolidCells(bin.Cell)) cells[cell] = new(cell, GroundSurface.Grass, false);
        _traversalGrid = new(cells.Values);
    }
    private static IEnumerable<GridCell> BinSolidCells(GridCell centre) =>
        from x in Enumerable.Range(centre.X - 1, 3) from z in Enumerable.Range(centre.Z - 1, 3) select new GridCell(x, z);

    private static string? ValidatePersistedLitter(SessionPersistenceSnapshot s)
    {
        if (s.Litter is null) return s.Preparation?.Plan is null ? null : "Current Build save requires litter state.";
        var litter = s.Litter;
        if (litter.Version != 1 || litter.Pieces is null || litter.Sweeps is null || s.Preparation is not { } prep || s.Immersion is not { } immersion ||
            litter.Pieces.Any(w => w is null) || litter.Sweeps.Any(j => j is null)) return "Litter state shape invalid.";
        var bins = prep.BuildPlacements.Where(p => p.Kind == BuildServiceKind.Bin).ToDictionary(p => p.Id);
        var grid = s.TraversalGrid is { } savedGrid ? new TraversalGrid(savedGrid.Cells.Select(c => new TerrainCellOverride(new(c.X, c.Z), (GroundSurface)c.Surface, c.IsWalkable))) : null;
        bool ValidCell(GridCell c) => c.X is >= 0 and < TraversalGrid.Width && c.Z is >= 0 and < TraversalGrid.Depth && (grid?.Get(c).IsWalkable ?? true);
        bool BinSide(GridCell side, string bin) => bins.TryGetValue(bin, out var b) && ValidCell(side) && CellDistanceSquared(side, b.Cell) == 4 && (side.X == b.Cell.X || side.Z == b.Cell.Z);
        static bool Fetching(WastePiece w) => w.Location == WasteLocation.Ground && w.CarrierId is not null;
        bool RouteAt(ulong id, GridCell c) => s.NavigationAgents?.SingleOrDefault(n => n.Id == id) is { } n &&
            n.DestinationX == c.X && n.DestinationZ == c.Z;
        bool ArrivedAt(ulong id, GridCell c) => RouteAt(id, c) && s.NavigationAgents!.Single(n => n.Id == id) is { Action: (int)AgentNavigationAction.Arrived } n &&
            (n.XMillimetres, n.ZMillimetres) == TraversalGrid.CellCentre(c);
        // Lookups are indexed: a long festival can hold thousands of pieces and purchases.
        var purchases = new Dictionary<string, PersistedImmersionPurchaseLookup>(StringComparer.Ordinal);
        foreach (var p in immersion.Purchases) purchases.TryAdd(p.Id, new(p.AgentId, p.Product, p.Tick));
        var held = immersion.People.Select(p => p.Held?.TransactionId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (litter.Pieces.Select(w => w.Id).Distinct().Count() != litter.Pieces.Length) return "Litter identity, completion or location invalid.";
        var byId = litter.Pieces.ToDictionary(w => w.Id, StringComparer.Ordinal);
        if (litter.Pieces.Any(w =>
            !Enum.IsDefined(w.Location) || !purchases.TryGetValue(w.Id, out var bought) || bought.AgentId != w.ProducerId || bought.Product != w.Product ||
                w.CompletedTick < bought.Tick + ImmersionConsumeTicks(bought.Product) ||
            w.CompletedTick > s.CurrentTick || w.ActionTick < -1 || w.ActionTick > s.CurrentTick ||
            w.XMillimetres < TraversalGrid.OriginMillimetres || w.XMillimetres >= TraversalGrid.OriginMillimetres + TraversalGrid.Width * TraversalGrid.CellSizeMillimetres ||
            w.ZMillimetres < TraversalGrid.OriginMillimetres || w.ZMillimetres >= TraversalGrid.OriginMillimetres + TraversalGrid.Depth * TraversalGrid.CellSizeMillimetres ||
            w.ActionTick >= 0 && (w.Location != WasteLocation.Carried && !Fetching(w) || w.ActionTick < w.CompletedTick) ||
            w.BinId is not null && !bins.ContainsKey(w.BinId) || w.Location == WasteLocation.Bin && w.BinId is null ||
            w.Location is WasteLocation.Ground or WasteLocation.Removed && (w.BinId is not null || w.Approach is not null && !Fetching(w)) ||
            w.Approach is not null && !(w.Location == WasteLocation.Carried && w.BinId is not null || Fetching(w)) ||
            // Someone else handles a piece only while fetching it from the ground or carrying it; their clock starts at the pick-up.
            w.CarrierId is { } other && (w.Location is not (WasteLocation.Carried or WasteLocation.Ground) || !prep.People.Any(p => p.AgentId == other && p.Role == ProtectedPersonRole.Guest)) ||
            (w.PickedUpTick >= 0) != (w.Location == WasteLocation.Carried && w.CarrierId is not null) || w.PickedUpTick > s.CurrentTick ||
            w.PickedUpTick >= 0 && w.PickedUpTick < w.CompletedTick ||
            held.Contains(w.Id))) return "Litter identity, completion or location invalid.";
        if (litter.Pieces.Where(w => w.Location == WasteLocation.Carried || Fetching(w)).GroupBy(w => w.Carrier).Any(g => g.Count() > 1) ||
            litter.Sweeps.Select(j => j.WorkerId).Distinct().Count() != litter.Sweeps.Length ||
            litter.Sweeps.Any(j => s.Disorder?.Stewards.Any(w => w.WorkerId == j.WorkerId) != true || j.Remaining < 0 || j.Remaining > (j.Manual ? LitterRules.ManualTargets : LitterRules.LocalTargets) ||
                j.RadiusCells != (j.Manual ? LitterRules.ManualRadiusCells : LitterRules.LocalRadiusCells) || j.ActionTick < -1 || j.ActionTick > s.CurrentTick ||
                (j.TargetId is null) != (j.Approach is null) || j.TargetId is not null && (j.Remaining == 0 || (j.TargetIsBin ? !bins.ContainsKey(j.TargetId) : !(byId.GetValueOrDefault(j.TargetId)?.Location == WasteLocation.Ground)))) ||
            litter.Sweeps.Where(j => j.TargetId is not null).GroupBy(j => (j.TargetId, j.TargetIsBin)).Any(g => g.Count() > 1)) return "Litter cleanup ownership invalid.";
        if (prep.Status == PreparationStatus.Preparing && (litter.Pieces.Length != 0 || litter.Sweeps.Length != 0)) return "Draft/retry must clear waste.";
        if (litter.Pieces.Any(w => w.Location == WasteLocation.Carried &&
            (prep.People.Single(p => p.AgentId == w.Carrier).Departed ||
             w.Approach is { } approach && (!BinSide(approach, w.BinId!) || !RouteAt(w.Carrier, approach) ||
                w.ActionTick >= 0 && !ArrivedAt(w.Carrier, approach)))) ||
            litter.Pieces.Any(w => Fetching(w) && (w.Approach != TraversalGrid.WorldToCell(w.XMillimetres, w.ZMillimetres) || !ValidCell(w.Approach!.Value) ||
                prep.People.Single(p => p.AgentId == w.Carrier).Departed || !RouteAt(w.Carrier, w.Approach.Value) ||
                w.ActionTick >= 0 && !ArrivedAt(w.Carrier, w.Approach.Value) ||
                litter.Sweeps.Any(j => j.Remaining > 0 && !j.TargetIsBin && j.TargetId == w.Id))) ||
            litter.Sweeps.Any(j => !ValidCell(j.Centre) || j.UntilTick < 0 || j.UntilTick % LitterRules.SecondTicks != 0 ||
                j.UntilTick > s.CurrentTick + LitterRules.ManualDurationTicks + LitterRules.SecondTicks ||
                j.CooldownUntil < 0 || j.CooldownUntil > s.CurrentTick + LitterRules.CooldownTicks ||
                j.TargetId is null && j.ActionTick != -1 || j.Remaining > 0 && j.UntilTick < s.CurrentTick ||
                j.Approach is { } c && (!ValidCell(c) || !RouteAt(j.WorkerId, c) || j.ActionTick >= 0 && !ArrivedAt(j.WorkerId, c) ||
                    CellDistanceSquared(j.Centre, j.TargetIsBin ? bins[j.TargetId!].Cell : c) > (long)j.RadiusCells * j.RadiusCells ||
                    j.TargetIsBin && !BinSide(c, j.TargetId!) || !j.TargetIsBin && c != TraversalGrid.WorldToCell(byId[j.TargetId!].XMillimetres, byId[j.TargetId!].ZMillimetres))))
            return "Litter physical route or action timing invalid.";
        return null;
    }
}
