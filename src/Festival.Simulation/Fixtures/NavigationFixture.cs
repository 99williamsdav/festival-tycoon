namespace Festival.Simulation.Fixtures;

public sealed record NavigationFixtureState(GameSession Session, EntityId AgentId, GridCell GateCell, GridCell ServiceCell);

public static class NavigationFixture
{
    public const string ServiceIntentId = "ai.seek-service-point";

    public static NavigationFixtureState CreateGateToServiceSession()
    {
        var session = new GameSession(20260914, new CampaignId(20260914));
        var gate = TraversalGrid.WorldToCell(0, 30_000);
        var servicePoint = LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.service-point");
        var service = TraversalGrid.WorldToCell(
            checked((int)(servicePoint.XMetres * 1000)), checked((int)(servicePoint.ZMetres * 1000)));
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
        BlockRectangle(cells, 11_000, 27_000, -22_500, -11_500);  // small barn +Z front; same padded extents rotated to yaw0
        BlockRectangle(cells, -11_000, 11_000, -30_000, -16_000); // large barn in the rear field
        // Tree trunks, a quarter-metre clear around each; their crowns overhang freely.
        BlockRectangle(cells, -32_700, -31_300, 8_300, 9_700);     // oak in the west hedge
        BlockRectangle(cells, 28_950, 30_050, -30_050, -28_950);   // field maple behind the small barn
        BlockRectangle(cells, -29_630, -28_770, -7_030, -6_170);   // old apple at the farmhouse corner
        // The hedgerow round the field, a metre thick, with the gate gap in the north side: everyone comes and goes
        // through the gate. It cuts the vehicle track where the track meets the south hedge.
        BlockRectangle(cells, -32_500, 32_499, -32_500, -31_500); // south
        BlockRectangle(cells, -32_500, -31_500, -32_500, 32_499); // west
        BlockRectangle(cells, 31_500, 32_499, -32_500, 32_499);   // east
        BlockRectangle(cells, -32_500, -3_001, 31_500, 32_499);   // north, west of the gate
        BlockRectangle(cells, 3_000, 32_499, 31_500, 32_499);     // north, east of the gate
        // Backstage's crowd barriers (a thin line, but one nobody walks through) and its flight cases and crates.
        foreach (var cell in Backstage.BarrierCells().Concat(Backstage.PropCells()))
            cells[cell] = new TerrainCellOverride(cell, GroundSurface.Grass, false);
        // The farm pond in the south-east corner: its water and muddy margin, one run of cells per column.
        foreach (var (x, fromZ, toZ) in PondColumns)
            for (var z = fromZ; z <= toZ; z++)
            {
                var cell = new GridCell(x, z);
                cells[cell] = new TerrainCellOverride(cell, GroundSurface.Grass, false);
            }
        return cells.Values.ToArray();
    }

    private static readonly (int X, int FromZ, int ToZ)[] PondColumns =
    [
        (165, 173, 180), (166, 173, 180), (167, 172, 181), (168, 172, 182), (169, 171, 182), (170, 170, 182),
        (171, 170, 183), (172, 169, 184), (173, 169, 185), (174, 169, 185), (175, 169, 185), (176, 169, 185),
        (177, 169, 185), (178, 169, 185), (179, 169, 184), (180, 169, 184), (181, 170, 184), (182, 171, 184),
        (183, 172, 183), (184, 173, 182), (185, 174, 181), (186, 176, 179),
    ];

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
