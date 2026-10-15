namespace Festival.Simulation;

/// <summary>
/// A 2 × 1 m track-mat panel: 4 × 2 cells from its low corner. A run along Z lays panels across the path (4 cells wide in
/// X, 2 along Z); a run along X turns them a quarter (2 cells in X, 4 in Z).
/// </summary>
public sealed record MatPanel(GridCell Corner, bool AlongX)
{
    public IEnumerable<GridCell> Cells()
    {
        var (width, depth) = AlongX ? (2, 4) : (4, 2);
        for (var z = 0; z < depth; z++)
        for (var x = 0; x < width; x++)
            yield return new(Corner.X + x, Corner.Z + z);
    }
}

public enum GroundCoverAction { Straw, Matting, Lift }

/// <summary>
/// Lays a straight run of straw or matting from one cell to another along whichever axis the drag runs most, or lifts
/// whatever lies along it. Cells it can't cover (a service standing there, the stage, off the grass) are left out.
/// </summary>
public sealed record GroundCoverCommand(GroundCoverAction Action, GridCell From, GridCell To) : SessionCommand;

public static class GroundCoverRules
{
    /// <summary>Straw comes from Tier 2, with the rain; so does matting.</summary>
    public const int FromTier = 2;
    /// <summary>A bale: £2, spread over 24 cells (6 m²), so a 1.5 m path 4 m long.</summary>
    public const int BalePennies = 200, CellsPerBale = 24;
    /// <summary>A straw path is three cells (1.5 m) wide; a mat run is a panel's 2 m.</summary>
    public const int StrawWidthCells = 3;
    /// <summary>Hire of one 2 × 1 m panel for the day.</summary>
    public const int MatPanelPennies = 250;
    /// <summary>The longest run one drag lays: 40 m.</summary>
    public const int MaximumRunCells = 80;
    /// <summary>Footfall that tramples straw flat: past this it stops protecting the ground.</summary>
    public const int TrampleMaximum = 600;
    /// <summary>Straw tramples twice as fast when it's wet.</summary>
    public const int TrampleWetFrom = GroundRules.PuddleWet;

    public static int BalesFor(int cells) => (cells + CellsPerBale - 1) / CellsPerBale;
}

public sealed partial class GameSession
{
    /// <summary>What the layout's straw and matting cost: the bales the straw takes and each panel's hire.</summary>
    public static long GroundCoverCost(PreparationSnapshot? p) =>
        GroundCoverRules.BalesFor(p?.StrawCells?.Length ?? 0) * (long)GroundCoverRules.BalePennies +
        (p?.MatPanels?.Length ?? 0) * (long)GroundCoverRules.MatPanelPennies;

    /// <summary>Every Build cost: the services' fees and the ground cover.</summary>
    public static long BuildCostOf(PreparationSnapshot p) =>
        (p.BuildPlacements?.Sum(item => (long)BuildServiceFeePennies(item.Kind)) ?? 0) + GroundCoverCost(p);

    public IReadOnlyList<int> CapturePlannedStraw() => _preparation?.StrawCells ?? [];
    public IReadOnlyList<MatPanel> CaptureMatPanels() => _preparation?.MatPanels ?? [];

    private static int CellIndex(GridCell cell) => cell.Z * TraversalGrid.Width + cell.X;
    private static GridCell CellAt(int index) => new(index % TraversalGrid.Width, index / TraversalGrid.Width);

    /// <summary>The ground every placed service stands on solidly, as the route check blocks it.</summary>
    private static IEnumerable<GridCell> BuildSolidCells(BuildPlacement item)
    {
        static IEnumerable<GridCell> Square(GridCell centre, int radius) =>
            from x in Enumerable.Range(centre.X - radius, radius * 2 + 1) from z in Enumerable.Range(centre.Z - radius, radius * 2 + 1) select new GridCell(x, z);
        return item.Kind switch
        {
            BuildServiceKind.Bin => BinSolidCells(item.Cell),
            BuildServiceKind.Marquee => MarqueeSolidCells(item.Cell, item.QuarterTurns),
            BuildServiceKind.WaterTap => Square(item.Cell, 1),
            BuildServiceKind.Toilet => ToiletSolidCells(ToiletOf(item)),
            BuildServiceKind.FoodVan or BuildServiceKind.Bar => ImmersionFootprint(new(Stalls.Id(item.Kind, 1), item.Cell, item.QuarterTurns, [])),
            BuildServiceKind.FirstAid => Square(item.Cell, 3),
            BuildServiceKind.StewardPost => Square(item.Cell, 2),
            _ => [],
        };
    }

    /// <summary>Ground that can take straw or matting: open grass inside the site, clear of the stages and of anything built.</summary>
    private static HashSet<GridCell> CoverBlocked(PreparationSnapshot p, IReadOnlyList<FestivalStage> stages, EquipmentSnapshot? equipment,
        GroundCoverAction? laying = null)
    {
        var blocked = new HashSet<GridCell>();
        // Straw and matting never stack: each is laid only where the other isn't, so nothing is paid for twice.
        if (laying == GroundCoverAction.Straw) blocked.UnionWith((p.MatPanels ?? []).SelectMany(panel => panel.Cells()));
        if (laying == GroundCoverAction.Matting) blocked.UnionWith((p.StrawCells ?? []).Select(CellAt));
        foreach (var item in p.BuildPlacements ?? []) blocked.UnionWith(BuildSolidCells(item));
        if (equipment is { } unit)
        {
            var centre = TraversalGrid.WorldToCell(unit.XMillimetres, unit.ZMillimetres);
            for (var x = centre.X - 3; x <= centre.X + 3; x++) for (var z = centre.Z - 3; z <= centre.Z + 3; z++) blocked.Add(new(x, z));
        }
        if (p.WaterTowerOwned)
            for (var x = WaterTowerCell.X - 3; x <= WaterTowerCell.X + 3; x++) for (var z = WaterTowerCell.Z - 3; z <= WaterTowerCell.Z + 3; z++) blocked.Add(new(x, z));
        foreach (var stage in stages)
            for (var x = stage.ReserveBounds.MinX; x <= stage.ReserveBounds.MaxX; x++)
            for (var z = stage.ReserveBounds.MinZ; z <= stage.ReserveBounds.MaxZ; z++)
                if (stage.Reserve(new(x, z))) blocked.Add(new(x, z));
        return blocked;
    }

    private static readonly Lazy<TraversalGrid> FarmTerrain = new(() => new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain()));

    private static bool CoverGround(GridCell cell, HashSet<GridCell> blocked) =>
        cell.X is >= 68 and <= 190 && cell.Z is >= 108 and <= 190 && !Backstage.Area(cell) && !FestivalStages.Main.Reserve(cell) &&
        FarmTerrain.Value.Contains(cell) && FarmTerrain.Value.Get(cell) is { IsWalkable: true, Surface: GroundSurface.Grass } && !blocked.Contains(cell);

    /// <summary>The cells a drag runs along: the centre line along its longer axis, clipped to the longest run.</summary>
    private static (bool AlongX, int Fixed, int From, int To) CoverRun(GridCell from, GridCell to)
    {
        var alongX = Math.Abs(to.X - from.X) > Math.Abs(to.Z - from.Z);
        var (a, b) = alongX ? (from.X, to.X) : (from.Z, to.Z);
        if (Math.Abs(b - a) >= GroundCoverRules.MaximumRunCells) b = a + Math.Sign(b - a) * (GroundCoverRules.MaximumRunCells - 1);
        return (alongX, alongX ? from.Z : from.X, Math.Min(a, b), Math.Max(a, b));
    }

    private static GridCell RunCell(bool alongX, int along, int across) => alongX ? new(along, across) : new(across, along);

    /// <summary>The straw a drag would lay: a path three cells wide down the run, on ground that can take it.</summary>
    private static int[] StrawRun(GroundCoverCommand command, HashSet<GridCell> blocked)
    {
        var (alongX, line, from, to) = CoverRun(command.From, command.To);
        var cells = new List<int>();
        for (var along = from; along <= to; along++)
        for (var across = line - 1; across <= line + 1; across++)
        {
            var cell = RunCell(alongX, along, across);
            if (CoverGround(cell, blocked)) cells.Add(CellIndex(cell));
        }
        return cells.ToArray();
    }

    /// <summary>
    /// The panels a drag would lay: one per metre down the run, each across the path. A panel that would overlap a mat
    /// already down is left out, so a run laid across another stops at its edge, and the first run keeps the junction.
    /// </summary>
    private static MatPanel[] MatRun(GroundCoverCommand command, HashSet<GridCell> blocked, IReadOnlyList<MatPanel> existing)
    {
        var (alongX, line, from, to) = CoverRun(command.From, command.To);
        var taken = existing.SelectMany(panel => panel.Cells()).ToHashSet();
        var panels = new List<MatPanel>();
        for (var along = from; along + 1 <= Math.Max(to, from + 1); along += 2)
        {
            var corner = RunCell(alongX, along, line - 2);
            var panel = new MatPanel(corner, alongX);
            if (panel.Cells().All(cell => CoverGround(cell, blocked) && !taken.Contains(cell))) panels.Add(panel);
        }
        return panels.ToArray();
    }

    private static IEnumerable<GridCell> LiftRun(GroundCoverCommand command)
    {
        var (alongX, line, from, to) = CoverRun(command.From, command.To);
        for (var along = from; along <= to; along++)
        for (var across = line - 2; across <= line + 1; across++)
            yield return RunCell(alongX, along, across);
    }

    public bool GroundCoverAvailable => (_preparation?.Tier??1)>=GroundCoverRules.FromTier || IsClassicCampaign&&IsRainyFestival;
    private CommandResult? ValidateGroundCover(EntityId? target, GroundCoverCommand command)
    {
        if (target is not null || _preparation is not { Status: PreparationStatus.Preparing, Plan: { Committed: false } } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Straw and matting are laid before opening.");
        if (!Enum.IsDefined(command.Action) || !new TraversalGrid().Contains(command.From) || !new TraversalGrid().Contains(command.To))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown ground cover.");
        if (!GroundCoverAvailable) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Straw and matting come with Tier 2's wet weather.");
        var blocked = CoverBlocked(p, Stages, _equipment, command.Action);
        var straw = (p.StrawCells ?? []).ToHashSet();
        var matted = (p.MatPanels ?? []).SelectMany(panel => panel.Cells()).ToHashSet();
        return command.Action switch
        {
            GroundCoverAction.Straw when StrawRun(command, blocked).All(straw.Contains) =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No open grass there to lay straw on."),
            GroundCoverAction.Matting when MatRun(command, blocked, p.MatPanels ?? []).Length == 0 =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No room there for track matting."),
            GroundCoverAction.Lift when !LiftRun(command).Any(cell => straw.Contains(CellIndex(cell)) || matted.Contains(cell)) =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "There's nothing laid there to lift."),
            _ => null,
        };
    }

    private void ApplyGroundCover(GroundCoverCommand command)
    {
        var p = _preparation!;
        var blocked = CoverBlocked(p, Stages, _equipment, command.Action);
        var straw = (p.StrawCells ?? []).ToList();
        var mats = (p.MatPanels ?? []).ToList();
        switch (command.Action)
        {
            case GroundCoverAction.Straw: straw.AddRange(StrawRun(command, blocked)); break;
            case GroundCoverAction.Matting: mats.AddRange(MatRun(command, blocked, mats)); break;
            default:
                var lifted = LiftRun(command).ToHashSet();
                straw.RemoveAll(cell => lifted.Contains(CellAt(cell)));
                mats.RemoveAll(panel => panel.Cells().Any(lifted.Contains));
                break;
        }
        _preparation = WithGroundCover(p, straw, mats);
    }

    /// <summary>The layout with this straw and matting, sorted and without repeats; empty lists are none.</summary>
    private static PreparationSnapshot WithGroundCover(PreparationSnapshot p, IEnumerable<int> straw, IEnumerable<MatPanel> mats)
    {
        var cells = straw.Distinct().Order().ToArray();
        var panels = mats.OrderBy(panel => panel.Corner.Z).ThenBy(panel => panel.Corner.X).ThenBy(panel => panel.AlongX).ToArray();
        return p with { StrawCells = cells.Length == 0 ? null : cells, MatPanels = panels.Length == 0 ? null : panels };
    }

    /// <summary>Lifts any straw and matting a service now stands on, as the default layout drops onto the field.</summary>
    private static PreparationSnapshot ClearCoverUnderServices(PreparationSnapshot p)
    {
        if (p.StrawCells is null && p.MatPanels is null) return p;
        var solid = (p.BuildPlacements ?? []).SelectMany(BuildSolidCells).ToHashSet();
        return WithGroundCover(p, (p.StrawCells ?? []).Where(cell => !solid.Contains(CellAt(cell))),
            (p.MatPanels ?? []).Where(panel => !panel.Cells().Any(solid.Contains)));
    }

    /// <summary>Whether a service placed here would stand on straw or matting.</summary>
    private static bool StandsOnCover(PreparationSnapshot p, BuildPlacement item)
    {
        if (p.StrawCells is null && p.MatPanels is null) return false;
        var covered = (p.StrawCells ?? []).Select(CellAt).Concat((p.MatPanels ?? []).SelectMany(panel => panel.Cells())).ToHashSet();
        return BuildSolidCells(item).Any(covered.Contains);
    }

    /// <summary>The saved layout's cover is what the commands could have laid: sorted, on open ground, panels apart.</summary>
    private static string? ValidatePersistedGroundCover(PreparationSnapshot p, SessionPersistenceSnapshot s)
    {
        if (p.StrawCells is null && p.MatPanels is null) return null;
        if (p.Tier < GroundCoverRules.FromTier && !(SavedClassic(s)&&SavedWeatherKind(s)==WeatherKind.Rain) || p.StrawCells is { Length: 0 } || p.MatPanels is { Length: 0 } || (p.MatPanels ?? []).Any(panel => panel is null))
            return "Ground cover invalid for this festival.";
        var straw = p.StrawCells ?? [];
        if (straw.Where((cell, index) => index > 0 && cell <= straw[index - 1]).Any()) return "Straw cells must be in ascending order without repeats.";
        var panels = p.MatPanels ?? [];
        if (!panels.SequenceEqual(panels.OrderBy(panel => panel.Corner.Z).ThenBy(panel => panel.Corner.X).ThenBy(panel => panel.AlongX)))
            return "Mat panels must be in order.";
        var blocked = CoverBlocked(p, SavedStages(s), s.Equipment);
        if (straw.Any(cell => cell < 0 || cell >= TraversalGrid.Width * TraversalGrid.Depth || !CoverGround(CellAt(cell), blocked)))
            return "Straw lies where it can't be laid.";
        var matCells = panels.SelectMany(panel => panel.Cells()).ToArray();
        if (matCells.Distinct().Count() != matCells.Length || matCells.Any(cell => !CoverGround(cell, blocked))) return "Track matting lies where it can't be laid.";
        var strawSet = straw.ToHashSet();
        if (matCells.Any(cell => strawSet.Contains(CellIndex(cell)))) return "Straw and matting lie on the same ground.";
        return null;
    }
}
