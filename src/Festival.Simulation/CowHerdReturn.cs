namespace Festival.Simulation;

public sealed partial class GameSession
{
    // All pacing is derived from the existing cow, owner and route. No new saved timer or speed override.
    private LooseCow? HerdReturnCow(NavigationAgentState agent)
    {
        if (_preparation?.Status != PreparationStatus.Running || _cows is null) return null;
        if (IsPlayerSteward(agent.Id.Value) || agent.IntentId != "cow.drive") return null;
        return _cows.Loose.FirstOrDefault(c => c.Driving && c.HerderId == agent.Id.Value);
    }

    private GridCell HerdLeadGoal(LooseCow cow)
    {
        var gate = TraversalGrid.CellCentre(CowRules.GateInside);
        long gx = gate.XMillimetres - cow.XMillimetres, gz = gate.ZMillimetres - cow.ZMillimetres;
        // Near the opening, stand aside rather than occupy the cow's final gate cell.
        if (gx * gx + gz * gz <= 2_500L * 2_500)
            return WalkableNear(new(CowRules.GateInside.X - 2, CowRules.GateInside.Z + 3));
        var x = cow.XMillimetres; var z = cow.ZMillimetres; long distance = 0;
        for (var i = cow.RouteIndex; i < cow.Route.Length; i++)
        {
            var cell = cow.Route[i]; var point = TraversalGrid.CellCentre(cell);
            long dx = point.XMillimetres - x, dz = point.ZMillimetres - z;
            distance += IntegerSquareRoot(dx * dx + dz * dz);
            if (cell == CowRules.GateInside) break;
            if (distance >= CowRules.HerdLeadMillimetres) return WalkableNear(cell);
            x = point.XMillimetres; z = point.ZMillimetres;
        }
        return WalkableNear(new(CowRules.GateInside.X - 2, CowRules.GateInside.Z + 3));
    }

    private void SetHerdLeadGoal(LooseCow cow, NavigationAgentState agent) =>
        ApplyAgentDestination(agent.Id, new(HerdLeadGoal(cow), "cow.drive"), avoidanceReplan: true);

    private void RefreshHerdReturnRoutes()
    {
        if (CurrentTick % CowRules.HerdReplanTicks != 0 || _cows is null) return;
        foreach (var cow in _cows.Loose)
        {
            if (!cow.Driving || cow.HerderId is not { } id || IsPlayerSteward(id) ||
                !_navigationAgents.TryGetValue(new(id), out var agent) || agent.IntentId != "cow.drive" ||
                agent.Action == AgentNavigationAction.NoRoute) continue;
            var goal = HerdLeadGoal(cow);
            if (agent.Destination != goal || agent.Action == AgentNavigationAction.Travelling && CurrentTick % 160 == 0)
                SetHerdLeadGoal(cow, agent);
        }
    }

    private int HerdReturnMovementBudget(NavigationAgentState agent, int requested, long stepX, long stepZ)
    {
        if (HerdReturnCow(agent) is not { } cow) return requested;
        var goal = TraversalGrid.CellCentre(HerdLeadGoal(cow));
        long x = agent.XMillimetres - cow.XMillimetres, z = agent.ZMillimetres - cow.ZMillimetres;
        var ahead = x * (goal.XMillimetres - cow.XMillimetres) + z * (goal.ZMillimetres - cow.ZMillimetres) > 0;
        if (!ahead) return requested; // Joining the lead from behind is still an ordinary walk.
        var squared = x * x + z * z;
        if (squared >= (long)CowRules.HerdMaxLeadMillimetres * CowRules.HerdMaxLeadMillimetres && x * stepX + z * stepZ > 0) return 0;
        return squared >= 1_600L * 1_600 && x * stepX + z * stepZ > 0 ? Math.Min(requested, CowRules.DrivenMillimetresPerTick * 1000) : requested;
    }

}

