namespace Festival.Simulation;

public enum BuildServiceKind { WaterTap, Toilet, FoodVan, Bar, FirstAid, StewardPost }
public sealed record BuildPlacement(string Id, BuildServiceKind Kind, GridCell Cell, int QuarterTurns);
public sealed record PlaceBuildServiceCommand(BuildServiceKind Kind, GridCell Cell, int QuarterTurns = 0) : SessionCommand;
public sealed record MoveBuildServiceCommand(string Id, GridCell Cell, int QuarterTurns = 0) : SessionCommand;
public sealed record RemoveBuildServiceCommand(string Id) : SessionCommand;
public sealed record UseDefaultBuildLayoutCommand : SessionCommand;

public sealed partial class GameSession
{
    private static readonly (BuildServiceKind Kind, int FeePennies, int Limit)[] BuildCatalogue =
    [
        (BuildServiceKind.WaterTap, 2_000, 2),
        (BuildServiceKind.Toilet, 4_000, 2),
        (BuildServiceKind.FoodVan, 8_000, 1),
        (BuildServiceKind.Bar, 7_000, 1),
        (BuildServiceKind.FirstAid, 5_000, 1),
        (BuildServiceKind.StewardPost, 4_000, 1)
    ];

    public static int BuildServiceFeePennies(BuildServiceKind kind) =>
        BuildCatalogue.Single(item => item.Kind == kind).FeePennies;
    public static int BuildServiceLimit(BuildServiceKind kind) =>
        BuildCatalogue.Single(item => item.Kind == kind).Limit;
    public IReadOnlyList<BuildPlacement> CaptureBuildPlacements() => _preparation?.BuildPlacements.ToArray() ?? [];
    public long BuildDraftCost => _preparation?.BuildPlacements?.Sum(item => (long)BuildServiceFeePennies(item.Kind)) ?? 0;
    public static BuildPlacement[] StandardBuildLayout() =>
    [
        new("water.main", BuildServiceKind.WaterTap, MedicalWaterCell, 0),
        new("toilet.main", BuildServiceKind.Toilet, new(172, 140), 0),
        new("food", BuildServiceKind.FoodVan, new(144, 119), 0),
        new("drinks", BuildServiceKind.Bar, new(160, 120), 0),
        new("first-aid", BuildServiceKind.FirstAid, MedicalTentCell, 0),
        new("steward-post", BuildServiceKind.StewardPost, DisorderSecurityPostCell, 1)
    ];

    private static string NextBuildId(BuildServiceKind kind, IReadOnlyList<BuildPlacement> placed)
    {
        var prefix = kind switch
        {
            BuildServiceKind.WaterTap => "water",
            BuildServiceKind.Toilet => "toilet",
            BuildServiceKind.FoodVan => "food",
            BuildServiceKind.Bar => "drinks",
            BuildServiceKind.FirstAid => "first-aid",
            BuildServiceKind.StewardPost => "steward-post",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (kind is not (BuildServiceKind.WaterTap or BuildServiceKind.Toilet)) return prefix;
        var first = prefix + ".main";
        if (!placed.Any(item => item.Id == first)) return first;
        for (var index = 1; index <= BuildServiceLimit(kind); index++)
        {
            var id = prefix + ".extra-" + index;
            if (!placed.Any(item => item.Id == id)) return id;
        }
        throw new InvalidOperationException("No free build identity remains.");
    }

    private CommandResult? ValidateBuildCommand(EntityId? target, SessionCommand command)
    {
        if (target is not null || _preparation is not { Status: PreparationStatus.Preparing, Plan: { Committed: false } } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Build layout can be edited only before opening.");
        if (command is RemoveBuildServiceCommand remove)
            return p.BuildPlacements.Any(item => item.Id == remove.Id) ? null :
                CommandResult.Rejected(CommandReasonCode.UnknownTarget, "This service is not in the draft.");
        if (command is UseDefaultBuildLayoutCommand)
            return ValidateBuildLayout(StandardBuildLayout(), _equipment, p.WaterTowerOwned) is { } defaultsError ?
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, defaultsError) : null;
        BuildPlacement proposed;
        if (command is PlaceBuildServiceCommand place)
        {
            if (!Enum.IsDefined(place.Kind)) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown service.");
            if (p.BuildPlacements.Count(item => item.Kind == place.Kind) >= BuildServiceLimit(place.Kind))
                return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "All slots for this service are placed; select one to move it.");
            proposed = new(NextBuildId(place.Kind, p.BuildPlacements), place.Kind, place.Cell, place.QuarterTurns);
        }
        else if (command is MoveBuildServiceCommand move)
        {
            var existing = p.BuildPlacements.SingleOrDefault(item => item.Id == move.Id);
            if (existing is null) return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Select a placed service to move.");
            proposed = existing with { Cell = move.Cell, QuarterTurns = move.QuarterTurns };
        }
        else return CommandResult.Rejected(CommandReasonCode.UnknownCommand, "Unknown build edit.");
        var next = p.BuildPlacements.Where(item => item.Id != proposed.Id).Append(proposed).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        return ValidateBuildLayout(next, _equipment, p.WaterTowerOwned) is { } issue ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, issue) : null;
    }

    private void ApplyBuildCommand(SessionCommand command)
    {
        var p = _preparation!;
        _preparation = p with { BuildPlacements = command switch
        {
            PlaceBuildServiceCommand place => p.BuildPlacements.Append(new BuildPlacement(NextBuildId(place.Kind, p.BuildPlacements),
                place.Kind, place.Cell, place.QuarterTurns)).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            MoveBuildServiceCommand move => p.BuildPlacements.Select(item => item.Id == move.Id ? item with
                { Cell = move.Cell, QuarterTurns = move.QuarterTurns } : item).ToArray(),
            RemoveBuildServiceCommand remove => p.BuildPlacements.Where(item => item.Id != remove.Id).ToArray(),
            UseDefaultBuildLayoutCommand => StandardBuildLayout().OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            _ => throw new InvalidOperationException("Unknown build edit.")
        } };
        SyncBuildPhysicalLayout();
    }

    private void SyncBuildPhysicalLayout()
    {
        if (_preparation is not { } p || _medical is null || _immersion is null) return;
        var water = p.BuildPlacements.Where(item => item.Kind == BuildServiceKind.WaterTap).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var primary = water.FirstOrDefault(item => item.Id == "water.main");
        // A placed main tap keeps its live queue; every other tap stands fresh at its placement.
        var existingMain = Taps.FirstOrDefault(item => item.Id == "water.main") ?? OpeningMainTap();
        SetTaps((primary is null ? [] : new[] { existingMain with { Cell = primary.Cell, QuarterTurns = primary.QuarterTurns, GeometryVersion = 1, QueueCells = [] } })
            .Concat(water.Where(item => item.Id != "water.main").Select(item =>
                new WaterPointState(item.Id, item.Cell, [], [], null, 0) { QuarterTurns = item.QuarterTurns, GeometryVersion = 1 })).ToArray());
        _preparation = p with
        {
            PrimaryWaterCell = primary?.Cell ?? MedicalWaterCell,
            PrimaryWaterQuarterTurns = primary?.QuarterTurns ?? 0,
            PrimaryWaterGeometryVersion = primary is null ? 0 : 1,
            ExtraWaterSiteIds = water.Where(item => item.Id != "water.main").Select(item => item.Id).ToArray(),
            WaterPlacements = water.Where(item => item.Id != "water.main").Select(item =>
                new WaterPlacement(item.Id, item.Cell) { QuarterTurns = item.QuarterTurns, GeometryVersion = 1 }).ToArray(),
            FirstAidPlacement = p.BuildPlacements.FirstOrDefault(item => item.Kind == BuildServiceKind.FirstAid) is { } aid
                ? new(aid.Cell, aid.QuarterTurns) : null,
            StewardPostPlacement = p.BuildPlacements.FirstOrDefault(item => item.Kind == BuildServiceKind.StewardPost) is { } post
                ? new(post.Cell, post.QuarterTurns) : null
        };
        _immersion = _immersion with
        {
            Vendors = p.BuildPlacements.Where(item => item.Kind is BuildServiceKind.FoodVan or BuildServiceKind.Bar)
                .Select(item => NewLooseVendor(new ImmersionVendor(item.Id, item.Cell, item.QuarterTurns, [])))
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Toilet = p.BuildPlacements.FirstOrDefault(item => item.Id == "toilet.main") is { } toilet
                ? new ToiletFacility(toilet.Id, toilet.Cell, toilet.QuarterTurns, [], null, false, 0, 0, 0,
                    ToiletRules.CapacityMillilitres, ToiletRules.ContainmentPermille) : null,
            ExtraToilets = p.BuildPlacements.Where(item => item.Kind == BuildServiceKind.Toilet && item.Id != "toilet.main")
                .Select(item => new ToiletFacility(item.Id, item.Cell, item.QuarterTurns, [], null, false, 0, 0, 0,
                    ToiletRules.CapacityMillilitres, ToiletRules.ContainmentPermille)).ToArray()
        };
    }

    private static string? ValidateBuildLayout(IReadOnlyList<BuildPlacement> placements,
        EquipmentSnapshot? equipment = null, bool waterTowerOwned = false)
    {
        static bool IdentityMatchesKind(BuildPlacement item) => item.Kind switch
        {
            BuildServiceKind.WaterTap => item.Id is "water.main" or "water.extra-1",
            BuildServiceKind.Toilet => item.Id is "toilet.main" or "toilet.extra-1",
            BuildServiceKind.FoodVan => item.Id == "food",
            BuildServiceKind.Bar => item.Id == "drinks",
            BuildServiceKind.FirstAid => item.Id == "first-aid",
            BuildServiceKind.StewardPost => item.Id == "steward-post",
            _ => false
        };
        if (placements.Any(item => item is null || !Enum.IsDefined(item.Kind) || item.QuarterTurns is < 0 or > 3 ||
            string.IsNullOrWhiteSpace(item.Id) || !IdentityMatchesKind(item)) ||
            placements.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != placements.Count ||
            BuildCatalogue.Any(entry => placements.Count(item => item.Kind == entry.Kind) > entry.Limit))
            return "Build identities or service limits are invalid.";
        var terrain = new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        var reserved = new HashSet<GridCell>();
        for (var x = 90; x <= 101; x++)
            for (var z = 139; z <= 160; z++) reserved.Add(new(x, z));
        for (var x = MedicalRestCell.X - 1; x <= MedicalRestCell.X + 1; x++)
            for (var z = MedicalRestCell.Z - 1; z <= MedicalRestCell.Z + 1; z++) reserved.Add(new(x, z));
        if (equipment is { } unit)
        {
            var centre = TraversalGrid.WorldToCell(unit.XMillimetres, unit.ZMillimetres);
            for (var x = centre.X - 5; x <= centre.X + 5; x++)
                for (var z = centre.Z - 5; z <= centre.Z + 5; z++) reserved.Add(new(x, z));
        }
        if (waterTowerOwned)
            for (var x = WaterTowerCell.X - 4; x <= WaterTowerCell.X + 4; x++)
                for (var z = WaterTowerCell.Z - 4; z <= WaterTowerCell.Z + 4; z++) reserved.Add(new(x, z));
        foreach (var item in placements)
        {
            var cells = BuildReservedCells(item).Distinct().ToArray();
            if (cells.Any(cell => cell.X is < 68 or > 190 || cell.Z is < 108 or > 190 ||
                !terrain.Contains(cell) || terrain.Get(cell) is not { IsWalkable: true, Surface: GroundSurface.Grass }))
                return "Service footprint or entrance needs clear grass inside the festival site.";
            if (cells.Any(cell => !reserved.Add(cell))) return "Service footprint or access overlaps another placement.";
        }
        if (!BuildAccessClear(placements, equipment, waterTowerOwned))
            return "Service blocks the route from the gate to a required entrance or exit.";
        return null;
    }

    private static string? ValidateBuildMirrors(PreparationSnapshot p, SessionPersistenceSnapshot saved)
    {
        if (saved.Medical is null || saved.Immersion is not { } immersion || saved.Facilities?.Taps is not { } taps ||
            p.ExtraWaterSiteIds is null || p.WaterPlacements is null || immersion.Vendors is null)
            return "Saved build services have no physical state.";
        var water = p.BuildPlacements.Where(item => item.Kind == BuildServiceKind.WaterTap).ToArray();
        var main = water.SingleOrDefault(item => item.Id == "water.main");
        if (p.PrimaryWaterCell != (main?.Cell ?? MedicalWaterCell) || p.PrimaryWaterQuarterTurns != (main?.QuarterTurns ?? 0) ||
            p.PrimaryWaterGeometryVersion != (main is null ? 0 : 1) ||
            taps.FirstOrDefault(item => item.Id == "water.main") is var mainTap && (mainTap is null) != (main is null) ||
            mainTap is not null && (mainTap.Cell != p.PrimaryWaterCell || mainTap.QuarterTurns != p.PrimaryWaterQuarterTurns ||
                mainTap.GeometryVersion != p.PrimaryWaterGeometryVersion))
            return "Saved primary tap differs from the build layout.";
        var extras = water.Where(item => item.Id != "water.main").OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (!extras.Select(item => item.Id).SequenceEqual(p.ExtraWaterSiteIds) ||
            !extras.Select(item => (item.Id, item.Cell, item.QuarterTurns)).SequenceEqual(
                p.WaterPlacements.Select(item => (item.Id, item.Cell, item.QuarterTurns))) ||
            !extras.Select(item => (item.Id, item.Cell, item.QuarterTurns)).SequenceEqual(
                taps.Where(item => item.Id != "water.main").Select(item => (item.Id, item.Cell, item.QuarterTurns))))
            return "Saved extra taps differ from the build layout.";
        foreach (var kind in new[] { BuildServiceKind.FirstAid, BuildServiceKind.StewardPost })
        {
            var item = p.BuildPlacements.SingleOrDefault(placement => placement.Kind == kind);
            var post = kind == BuildServiceKind.FirstAid ? p.FirstAidPlacement : p.StewardPostPlacement;
            if ((item is null) != (post is null) || item is not null && (item.Cell != post!.Cell || item.QuarterTurns != post.QuarterTurns))
                return "Saved response post differs from the build layout.";
        }
        var vendors = p.BuildPlacements.Where(item => item.Kind is BuildServiceKind.FoodVan or BuildServiceKind.Bar)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (!vendors.Select(item => (item.Id, item.Cell, item.QuarterTurns)).SequenceEqual(
            immersion.Vendors.Select(item => (item.Id, item.Cell, item.QuarterTurns))))
            return "Saved vendor differs from the build layout.";
        var toilets = p.BuildPlacements.Where(item => item.Kind == BuildServiceKind.Toilet)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (!toilets.Select(item => (item.Id, item.Cell, item.QuarterTurns)).SequenceEqual(
            EffectiveToilets(immersion).OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => (item.Id, item.Cell, item.QuarterTurns))))
            return "Saved toilet differs from the build layout.";
        return null;
    }

    private static bool BuildAccessClear(IReadOnlyList<BuildPlacement> placements,
        EquipmentSnapshot? equipment, bool waterTowerOwned)
    {
        var blocked = new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain())
            .Overrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        void Block(GridCell cell) => blocked[cell] = new(cell, GroundSurface.Grass, false);
        foreach (var item in placements)
        {
            switch (item.Kind)
            {
                case BuildServiceKind.WaterTap:
                    for (var x = item.Cell.X - 1; x <= item.Cell.X + 1; x++)
                        for (var z = item.Cell.Z - 1; z <= item.Cell.Z + 1; z++) Block(new(x, z));
                    break;
                case BuildServiceKind.Toilet:
                    foreach (var cell in ToiletSolidCells(new(item.Id, item.Cell, item.QuarterTurns, [], null,
                        false, 0, 0, 0, ToiletRules.CapacityMillilitres, ToiletRules.ContainmentPermille))) Block(cell);
                    break;
                case BuildServiceKind.FoodVan or BuildServiceKind.Bar:
                    foreach (var cell in ImmersionFootprint(new(item.Kind == BuildServiceKind.FoodVan ? "food" : "drinks",
                        item.Cell, item.QuarterTurns, []))) Block(cell);
                    break;
                case BuildServiceKind.FirstAid or BuildServiceKind.StewardPost:
                    var radius = item.Kind == BuildServiceKind.FirstAid ? 3 : 2;
                    for (var x = item.Cell.X - radius; x <= item.Cell.X + radius; x++)
                        for (var z = item.Cell.Z - radius; z <= item.Cell.Z + radius; z++) Block(new(x, z));
                    break;
            }
        }
        if (waterTowerOwned)
            for (var x = WaterTowerCell.X - 3; x <= WaterTowerCell.X + 3; x++)
                for (var z = WaterTowerCell.Z - 3; z <= WaterTowerCell.Z + 3; z++) Block(new(x, z));
        if (equipment is { } unit)
        {
            var lower = TraversalGrid.WorldToCell(unit.XMillimetres - 1_500, unit.ZMillimetres - 1_000);
            var upper = TraversalGrid.WorldToCell(unit.XMillimetres + 1_500, unit.ZMillimetres + 1_000);
            for (var x = lower.X; x <= upper.X; x++)
                for (var z = lower.Z; z <= upper.Z; z++) Block(new(x, z));
        }
        for (var x = 91; x <= 100; x++)
            for (var z = 140; z <= 159; z++)
            {
                var cell = new GridCell(x, z);
                blocked[cell] = new(cell, GroundSurface.Grass,
                    x is >= 92 and <= 98 && z is >= 143 and <= 157 || x is >= 98 and <= 100 && z is >= 156 and <= 158);
            }
        var grid = new TraversalGrid(blocked.Values);
        var destinations = new List<GridCell> { MedicalRestCell };
        foreach (var item in placements)
        {
            switch (item.Kind)
            {
                case BuildServiceKind.WaterTap:
                    destinations.Add(WaterServiceCell(item.Cell, item.QuarterTurns)); break;
                case BuildServiceKind.Toilet:
                    var toilet = new ToiletFacility(item.Id, item.Cell, item.QuarterTurns, [], null, false, 0, 0, 0,
                        ToiletRules.CapacityMillilitres, ToiletRules.ContainmentPermille);
                    destinations.Add(ToiletQueueCell(toilet, 0));
                    destinations.Add(ToiletInsideCell(toilet));
                    destinations.Add(ToiletExitCell(toilet));
                    break;
                case BuildServiceKind.FoodVan or BuildServiceKind.Bar:
                    destinations.Add(ImmersionServiceCell(new(item.Kind == BuildServiceKind.FoodVan ? "food" : "drinks",
                        item.Cell, item.QuarterTurns, []))); break;
                case BuildServiceKind.FirstAid or BuildServiceKind.StewardPost:
                    var offset = item.Kind == BuildServiceKind.FirstAid ? 6 : 5;
                    foreach (var x in new[] { 0, 2 })
                    {
                        var rotated = RotateWaterOffset(new(x, offset), item.QuarterTurns);
                        destinations.Add(new(item.Cell.X + rotated.X, item.Cell.Z + rotated.Z));
                    }
                    break;
            }
        }
        return destinations.All(cell => DeterministicPathfinder.FindPath(grid, MedicalExitCell, cell).Found);
    }

    private static GridCell[] BuildReservedCells(BuildPlacement item)
    {
        static IEnumerable<GridCell> Square(GridCell centre, int radius) =>
            from x in Enumerable.Range(centre.X - radius, radius * 2 + 1)
            from z in Enumerable.Range(centre.Z - radius, radius * 2 + 1)
            select new GridCell(x, z);
        return item.Kind switch
        {
            BuildServiceKind.WaterTap => Square(item.Cell, 2)
                .Append(WaterServiceCell(item.Cell, item.QuarterTurns)).ToArray(),
            BuildServiceKind.Toilet => ToiletReservedCells(new(item.Id, item.Cell, item.QuarterTurns, [], null,
                false, 0, 0, 0, ToiletRules.CapacityMillilitres, ToiletRules.ContainmentPermille))
                .Append(ToiletExitCell(new(item.Id, item.Cell, item.QuarterTurns, [], null,
                    false, 0, 0, 0, ToiletRules.CapacityMillilitres, ToiletRules.ContainmentPermille))).ToArray(),
            BuildServiceKind.FoodVan or BuildServiceKind.Bar => ImmersionFootprint(new(item.Kind == BuildServiceKind.FoodVan ? "food" : "drinks",
                item.Cell, item.QuarterTurns, []))
                .Append(ImmersionServiceCell(new(item.Kind == BuildServiceKind.FoodVan ? "food" : "drinks",
                    item.Cell, item.QuarterTurns, []))).ToArray(),
            BuildServiceKind.FirstAid => Square(item.Cell, 3)
                .Concat(new[] { RotateWaterOffset(new(0, 6), item.QuarterTurns), RotateWaterOffset(new(2, 6), item.QuarterTurns) }
                    .Select(offset => new GridCell(item.Cell.X + offset.X, item.Cell.Z + offset.Z))).ToArray(),
            BuildServiceKind.StewardPost => Square(item.Cell, 2)
                .Concat(new[] { RotateWaterOffset(new(0, 5), item.QuarterTurns), RotateWaterOffset(new(2, 5), item.QuarterTurns) }
                    .Select(offset => new GridCell(item.Cell.X + offset.X, item.Cell.Z + offset.Z))).ToArray(),
            _ => []
        };
    }
}
