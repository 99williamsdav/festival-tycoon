namespace Festival.Simulation;

// A goody two-shoes with nothing else to do picks up other people's litter nearby and bins it.
public sealed partial class GameSession
{
    /// <summary>
    /// Arrived somewhere in the piece's cell. A piece in their own cell needs no walk, and arriving there leaves
    /// them wherever in the cell they stood, so the exact centre is not required.
    /// </summary>
    private bool InLitterCell(ulong id, GridCell cell) => _navigationAgents[new(id)] is { Action: AgentNavigationAction.Arrived } nav &&
        nav.Destination == cell && PersonCell(id) == cell;

    private bool IsGoodyTwoShoes(ulong id) => IsGuest(id) && LitterRules.Dickishness(CampaignSeed, id) <= LitterRules.GoodyTwoShoesMaximum;

    /// <summary>Gives up a piece they were walking to fetch; it stays where it lies.</summary>
    private void ReleaseGoodyPickup(ulong id)
    {
        EnsureWasteIndices();
        if (_pickupByPerson.TryGetValue(id, out var waste))
            SetWaste(_wasteById[waste] with { CarrierId = null, Approach = null, ActionTick = -1 });
    }

    private void AdvanceGoodyPickups()
    {
        EnsureWasteIndices();
        // Fetching: walk to the piece, stoop for a second, and it becomes theirs to carry to a bin.
        foreach (var (picker, wasteId) in _pickupByPerson.OrderBy(p => p.Key).ToArray())
        {
            var piece = _wasteById[wasteId];
            var route = _navigationAgents[new(picker)];
            if (_persons[picker].Departed || HigherPriorityOwns(picker) || LitterUrgent(picker) || ImmersionDepartureActive ||
                route.Action == AgentNavigationAction.NoRoute || route.Action == AgentNavigationAction.Arrived && !InLitterCell(picker, piece.Approach!.Value))
            { ReleaseGoodyPickup(picker); continue; }
            if (!InLitterCell(picker, piece.Approach!.Value)) continue;
            if (piece.ActionTick < 0) { SetWaste(piece with { ActionTick = CurrentTick }); continue; }
            if (CurrentTick - piece.ActionTick < LitterRules.DisposalTicks) continue;
            var nav = _navigationAgents[new(picker)];
            SetWaste(piece with { Location = WasteLocation.Carried, Approach = null, ActionTick = -1, PickedUpTick = CurrentTick,
                XMillimetres = nav.XMillimetres, ZMillimetres = nav.ZMillimetres });
        }
        if (_preparation?.Status != PreparationStatus.Running || CaptureBins().Count == 0) return;
        bool Swept(string id) => _litter!.Sweeps.Any(j => j.Remaining > 0 && !j.TargetIsBin && j.TargetId == id);
        var binCells = CaptureBins().Select(b => b.Cell).ToArray();
        // Only litter they could then carry to a bin: the same walk limit everyone applies to their own rubbish.
        // ...and with a side of that bin they can actually reach, as the carry step will need.
        bool BinWithinWalk(ulong id, GridCell from) => binCells.Any(b =>
            EstimateWalkTicks(id, from, b) + EstimateWalkTicks(id, b, new GridCell(b.X + 2, b.Z)) <= LitterRules.BinWalkLimitTicks &&
            ReachableBinSide(id, b) is not null);
        foreach (var person in PeopleIn(PersonView.Roster).Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed && IsGoodyTwoShoes(p.Id)).ToArray())
        {
            var id = person.Id;
            // Nothing else to do: settled and watching, hands free, no needs pressing and no other claim on them.
            var nav = _navigationAgents[new(id)];
            if (WasteOwnsNavigation(id) || CleanupOwnsNavigation(id) || HigherPriorityOwns(id) || LitterUrgent(id) || FaultWorkOwns(id) ||
                _persons[id].Intent != MedicalIntent.WatchShow || _persons[id].Held is not null || nav.Action != AgentNavigationAction.Arrived ||
                !ImmersionHandsAvailable(id)) continue;
            var here = PersonCell(id);
            var target = GroundNear(here, LitterRules.GoodyReachCells)
                .Where(w => w.CarrierId is null && !Swept(w.Id))
                .Select(w => (Piece: w, Cell: TraversalGrid.WorldToCell(w.XMillimetres, w.ZMillimetres)))
                .Where(t => BinWithinWalk(id, t.Cell))
                .OrderBy(t => CellDistanceSquared(t.Cell, here)).ThenBy(t => t.Piece.Id, StringComparer.Ordinal).Take(4)
                .FirstOrDefault(t => _traversalGrid!.Get(t.Cell).IsWalkable && DeterministicPathfinder.FindPath(_traversalGrid, here, t.Cell).Found);
            if (target.Piece is null) continue;
            SetWaste(target.Piece with { CarrierId = id, Approach = target.Cell, ActionTick = -1 });
            ApplyAgentDestination(new(id), new(target.Cell, "litter.goody-pickup"));
        }
    }
}
