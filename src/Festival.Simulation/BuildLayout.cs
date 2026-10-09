namespace Festival.Simulation;

public enum BuildServiceKind { WaterTap, Toilet, FoodVan, Bar, FirstAid, StewardPost, Bin }
public sealed record BuildPlacement(string Id, BuildServiceKind Kind, GridCell Cell, int QuarterTurns);
public sealed record PlaceBuildServiceCommand(BuildServiceKind Kind, GridCell Cell, int QuarterTurns = 0) : SessionCommand;
public sealed record MoveBuildServiceCommand(string Id, GridCell Cell, int QuarterTurns = 0) : SessionCommand;
public sealed record RemoveBuildServiceCommand(string Id) : SessionCommand;
public sealed record UseDefaultBuildLayoutCommand : SessionCommand;

public sealed partial class GameSession
{
    private static readonly (BuildServiceKind Kind, int FeePennies, int Limit)[] BuildCatalogue =
    [
        (BuildServiceKind.WaterTap, 1_500, 2),
        (BuildServiceKind.Toilet, 3_000, 2),
        (BuildServiceKind.FoodVan, 0, 1), // the food trader pays the festival to pitch, instead (FoodTraders)
        (BuildServiceKind.Bar, 5_000, 1),
        (BuildServiceKind.FirstAid, 3_500, 1),
        (BuildServiceKind.StewardPost, 2_500, 1),
        (BuildServiceKind.Bin, 1_000, int.MaxValue)
    ];

    public static int BuildServiceFeePennies(BuildServiceKind kind) =>
        BuildCatalogue.Single(item => item.Kind == kind).FeePennies;
    /// <summary>
    /// How many of a service this festival may place: one water tap, or two with the Another Round perk; the
    /// catalogue limit for everything else.
    /// </summary>
    public int ServiceLimit(BuildServiceKind kind) =>
        kind == BuildServiceKind.WaterTap ? (HasPerk("another-round") ? 2 : 1) : BuildServiceLimit(kind);

    /// <summary>The most of a service any festival may place (the tap's assumes the perk).</summary>
    public static int BuildServiceLimit(BuildServiceKind kind) =>
        BuildCatalogue.Single(item => item.Kind == kind).Limit;
    public IReadOnlyList<BuildPlacement> CaptureBuildPlacements() => _preparation?.BuildPlacements.ToArray() ?? [];
    public long BuildDraftCost => _preparation?.BuildPlacements?.Sum(item => (long)BuildServiceFeePennies(item.Kind)) ?? 0;
    /// <summary>
    /// The suggested layout, taken from how the user actually lays out a Tier 1 field: two loos by the stage, a bin
    /// between stage and lane, chips and bar along the lane, first aid across it and the steward post by the gate.
    /// </summary>
    public static BuildPlacement[] StandardBuildLayout() =>
    [
        new("water.main", BuildServiceKind.WaterTap, new(103, 128), 1),
        new("toilet.main", BuildServiceKind.Toilet, new(117, 121), 2),
        new("toilet.extra-1", BuildServiceKind.Toilet, new(112, 121), 2),
        new("food", BuildServiceKind.FoodVan, new(140, 162), 3),
        new("drinks", BuildServiceKind.Bar, new(140, 148), 3),
        new("first-aid", BuildServiceKind.FirstAid, new(139, 120), 0),
        new("steward-post", BuildServiceKind.StewardPost, new(119, 171), 2),
        new("bin.1", BuildServiceKind.Bin, new(121, 148), 0),
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
            BuildServiceKind.Bin => "bin",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (kind == BuildServiceKind.Bin)
        {
            for (var index = 1; ; index++)
                if (!placed.Any(p => p.Id == "bin." + index)) return "bin." + index;
        }
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
            if (p.BuildPlacements.Count(item => item.Kind == place.Kind) >= ServiceLimit(place.Kind))
                return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, place.Kind == BuildServiceKind.WaterTap && !HasPerk("another-round")
                    ? "One water tap is placed; the Another Round perk allows a second."
                    : "All slots for this service are placed; select one to move it.");
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
        SetVendors(p.BuildPlacements.Where(item => item.Kind is BuildServiceKind.FoodVan or BuildServiceKind.Bar)
            .Select(item => NewLooseVendor(new ImmersionVendor(item.Id, item.Cell, item.QuarterTurns, [])))
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray());
        SetToilets(PlacedToilets(p));
        _litter ??= EmptyLitter;
        _faults ??= EmptyFaults;
        _cows ??= EmptyCows;
        _lavSucker ??= EmptyLavSucker;
        if (_groundWear is null) ResetGround();
    }

    /// <summary>Every placed toilet, standing fresh at its placement: the main toilet first, then any others in placement order.</summary>
    private static ToiletFacility[] PlacedToilets(PreparationSnapshot p) =>
        p.BuildPlacements.Where(item => item.Id == "toilet.main")
            .Concat(p.BuildPlacements.Where(item => item.Kind == BuildServiceKind.Toilet && item.Id != "toilet.main"))
            .Select(item => new ToiletFacility(item.Id, item.Cell, item.QuarterTurns, [], null, false, 0, 0, 0,
                ToiletRules.CapacityMillilitres, ToiletRules.ContainmentPermille))
            .Select(toilet => toilet with { QueueCells = [ToiletDoorstepCell(toilet)] }).ToArray();

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
            BuildServiceKind.Bin => item.Id.StartsWith("bin.", StringComparison.Ordinal) && int.TryParse(item.Id[4..], out var number) && number > 0 && item.Id == "bin." + number,
            _ => false
        };
        if (placements.Any(item => item is null || !Enum.IsDefined(item.Kind) || item.QuarterTurns is < 0 or > 3 ||
            string.IsNullOrWhiteSpace(item.Id) || !IdentityMatchesKind(item)) ||
            placements.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != placements.Count ||
            BuildCatalogue.Any(entry => placements.Count(item => item.Kind == entry.Kind) > entry.Limit))
            return "Build identities or service limits are invalid.";
        var terrain = new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        var reserved = new HashSet<GridCell>();
        // The stage, its stairs and drawbar, and backstage behind the barriers.
        for (var x = 66; x <= 101; x++)
            for (var z = 112; z <= 164; z++)
                if (Backstage.StageReserve(new(x, z)) || Backstage.Area(new(x, z))) reserved.Add(new(x, z));
        // The rest area out in front of first aid stays clear.
        var restCentre = RestCentre(placements);
        var restArea = new HashSet<GridCell>();
        for (var x = restCentre.X - 1; x <= restCentre.X + 1; x++)
            for (var z = restCentre.Z - 1; z <= restCentre.Z + 1; z++) { reserved.Add(new(x, z)); restArea.Add(new(x, z)); }
        if (equipment is { } unit)
        {
            var centre = TraversalGrid.WorldToCell(unit.XMillimetres, unit.ZMillimetres);
            for (var x = centre.X - 5; x <= centre.X + 5; x++)
                for (var z = centre.Z - 5; z <= centre.Z + 5; z++) reserved.Add(new(x, z));
        }
        if (waterTowerOwned)
            for (var x = WaterTowerCell.X - 4; x <= WaterTowerCell.X + 4; x++)
                for (var z = WaterTowerCell.Z - 4; z <= WaterTowerCell.Z + 4; z++) reserved.Add(new(x, z));
        var sharedAccess = new HashSet<GridCell>();
        foreach (var item in placements)
        {
            var cells = BuildReservedCells(item).Distinct().ToArray();
            if (cells.Any(cell => cell.X is < 68 or > 190 || cell.Z is < 108 or > 190 ||
                !terrain.Contains(cell) || terrain.Get(cell) is not { IsWalkable: true, Surface: GroundSurface.Grass }))
                return "Service footprint or entrance needs clear grass inside the festival site.";
            // Bins are the exception: a crowd makes litter, and a bin is small enough to stand among it.
            if (item.Kind != BuildServiceKind.Bin && cells.Any(InAudienceArea)) return "Keep the audience area in front of the stage clear.";
            // A toilet's walkway in front of its door may be shared with the toilet beside it, so a row of loos can
            // stand side by side; nothing else may stand on it, and nothing may overlap a cubicle.
            var access = item.Kind == BuildServiceKind.Toilet ? ToiletAccessCells(ToiletOf(item)).ToHashSet() : [];
            foreach (var cell in cells)
            {
                if (access.Contains(cell) && sharedAccess.Contains(cell)) continue;
                // The rest area moves with first aid, so say which one is in the way.
                if (restArea.Contains(cell)) return "Keep clear of the ground in front of first aid, where overheated guests rest.";
                if (!reserved.Add(cell)) return "Service footprint or access overlaps another placement.";
                if (access.Contains(cell)) sharedAccess.Add(cell);
            }
        }
        if (!BuildAccessClear(placements, equipment, waterTowerOwned))
            return "Service blocks the route from the gate to a required entrance or exit.";
        return null;
    }

    /// <summary>The standing facilities are exactly what the saved Build layout places.</summary>
    private static string? ValidateBuildFacilities(PreparationSnapshot p, SessionPersistenceSnapshot saved)
    {
        if (saved.Medical is null || saved.Immersion is null || saved.Facilities is not { Taps: { } taps, Vendors: { } savedVendors } facilities)
            return "Saved build services have no physical state.";
        var water = p.BuildPlacements.Where(item => item.Kind == BuildServiceKind.WaterTap).ToArray();
        var main = water.SingleOrDefault(item => item.Id == "water.main");
        if (taps.FirstOrDefault(item => item.Id == "water.main") is var mainTap && (mainTap is null) != (main is null) ||
            mainTap is not null && (mainTap.Cell != main!.Cell || mainTap.QuarterTurns != main.QuarterTurns || mainTap.GeometryVersion != 1))
            return "Saved primary tap differs from the build layout.";
        var extras = water.Where(item => item.Id != "water.main").OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (!extras.Select(item => (item.Id, item.Cell, item.QuarterTurns)).SequenceEqual(
                taps.Where(item => item.Id != "water.main").Select(item => (item.Id, item.Cell, item.QuarterTurns))))
            return "Saved extra taps differ from the build layout.";
        var vendors = p.BuildPlacements.Where(item => item.Kind is BuildServiceKind.FoodVan or BuildServiceKind.Bar)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (!vendors.Select(item => (item.Id, item.Cell, item.QuarterTurns)).SequenceEqual(
            savedVendors.Select(item => (item.Id, item.Cell, item.QuarterTurns))))
            return "Saved vendor differs from the build layout.";
        var toilets = p.BuildPlacements.Where(item => item.Kind == BuildServiceKind.Toilet)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (!toilets.Select(item => (item.Id, item.Cell, item.QuarterTurns)).SequenceEqual(
            EffectiveToilets(facilities).OrderBy(item => item.Id, StringComparer.Ordinal)
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
                case BuildServiceKind.Bin:
                    foreach (var cell in BinSolidCells(item.Cell)) Block(cell);
                    break;
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
        foreach (var (cell, walkable) in Backstage.TrailerCells()) blocked[cell] = new(cell, GroundSurface.Grass, walkable);
        var grid = new TraversalGrid(blocked.Values);
        var destinations = new List<GridCell> { RestCentre(placements) };
        foreach (var item in placements)
        {
            switch (item.Kind)
            {
                case BuildServiceKind.Bin:
                    destinations.AddRange(new[] { new GridCell(item.Cell.X, item.Cell.Z + 2), new(item.Cell.X + 2, item.Cell.Z), new(item.Cell.X, item.Cell.Z - 2), new(item.Cell.X - 2, item.Cell.Z) });
                    break;
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

    /// <summary>The ground a placed service stands on (and its doorstep or service cell), as build validation sees it.</summary>
    public static GridCell[] BuildFootprint(BuildPlacement item) => BuildReservedCells(item);
    private static ToiletFacility ToiletOf(BuildPlacement item) => new(item.Id, item.Cell, item.QuarterTurns, [], null,
        false, 0, 0, 0, ToiletRules.CapacityMillilitres, ToiletRules.ContainmentPermille);

    private static GridCell[] BuildReservedCells(BuildPlacement item)
    {
        static IEnumerable<GridCell> Square(GridCell centre, int radius) =>
            from x in Enumerable.Range(centre.X - radius, radius * 2 + 1)
            from z in Enumerable.Range(centre.Z - radius, radius * 2 + 1)
            select new GridCell(x, z);
        // Small things keep a round berth rather than a square one: a grid square looks like a diamond from the
        // isometric camera, so it asked for half as much room again along the screen's up-down and left-right.
        static IEnumerable<GridCell> Round(GridCell centre) =>
            Square(centre, 2).Where(cell => (cell.X - centre.X) * (cell.X - centre.X) + (cell.Z - centre.Z) * (cell.Z - centre.Z) <= 5);
        return item.Kind switch
        {
            BuildServiceKind.Bin => Round(item.Cell).ToArray(),
            BuildServiceKind.WaterTap => Round(item.Cell)
                .Append(WaterServiceCell(item.Cell, item.QuarterTurns)).ToArray(),
            BuildServiceKind.Toilet => ToiletReservedCells(ToiletOf(item)),
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
