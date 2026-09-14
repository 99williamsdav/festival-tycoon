namespace Festival.Simulation.Fixtures;

public sealed record NavigationFixtureState(GameSession Session, EntityId AgentId, GridCell GateCell, GridCell ServiceCell);

public static class NavigationFixture
{
    public const string ServiceIntentId = "ai.seek-service-point";

    public static NavigationFixtureState CreateGateToServiceSession()
    {
        var session = new GameSession(20260914, new CampaignId(20260914));
        var gate = TraversalGrid.WorldToCell(0, 30_000);
        var service = TraversalGrid.WorldToCell(4_000, 18_000);
        var initialized = session.Execute(new CommandEnvelope(
            new CommandId(1), session.CampaignId, session.Phase, session.CurrentTick,
            session.NextSubmissionSequence, null,
            new InitializeNavigationFixtureCommand(gate, CreateLowerWitteringTerrain())));
        if (!initialized.IsAccepted || initialized.TargetId is null)
            throw new InvalidOperationException(initialized.Message);
        return new NavigationFixtureState(session, initialized.TargetId.Value, gate, service);
    }

    public static CommandResult IssueAutonomousServiceIntent(NavigationFixtureState fixture) =>
        fixture.Session.Execute(new CommandEnvelope(
            new CommandId(2), fixture.Session.CampaignId, fixture.Session.Phase,
            fixture.Session.CurrentTick, fixture.Session.NextSubmissionSequence,
            fixture.AgentId, new SetAgentDestinationCommand(fixture.ServiceCell, ServiceIntentId)));

    public static IReadOnlyList<TerrainCellOverride> CreateLowerWitteringTerrain()
    {
        var cells = new SortedDictionary<GridCell, TerrainCellOverride>();
        for (var z = 0; z < TraversalGrid.Depth; z++)
        for (var x = 124; x <= 131; x++)
        {
            var cell = new GridCell(x, z);
            cells[cell] = new TerrainCellOverride(cell, GroundSurface.VehicleTrack, true, 1000);
        }

        BlockRectangle(cells, -28_500, -15_500, -19_500, -8_500); // farmhouse
        BlockRectangle(cells, 13_500, 24_500, -25_000, -9_000);   // small barn
        BlockRectangle(cells, 7_000, 29_000, 5_000, 19_000);      // large barn
        return cells.Values.ToArray();
    }

    private static void BlockRectangle(IDictionary<GridCell, TerrainCellOverride> cells, int minX, int maxX, int minZ, int maxZ)
    {
        var from = TraversalGrid.WorldToCell(minX, minZ);
        var to = TraversalGrid.WorldToCell(maxX, maxZ);
        for (var z = from.Z; z <= to.Z; z++)
        for (var x = from.X; x <= to.X; x++)
        {
            var cell = new GridCell(x, z);
            cells[cell] = new TerrainCellOverride(cell, GroundSurface.Grass, false);
        }
    }
}
