namespace Festival.Simulation;

public enum BuildServiceKind { WaterTap, Toilet, FoodVan, Bar, FirstAid, StewardPost, Bin, Marquee }
public sealed record BuildPlacement(string Id, BuildServiceKind Kind, GridCell Cell, int QuarterTurns);
public sealed record PlaceBuildServiceCommand(BuildServiceKind Kind, GridCell Cell, int QuarterTurns = 0) : SessionCommand;
public sealed record MoveBuildServiceCommand(string Id, GridCell Cell, int QuarterTurns = 0) : SessionCommand;
public sealed record RemoveBuildServiceCommand(string Id) : SessionCommand;
public sealed record UseDefaultBuildLayoutCommand : SessionCommand;
/// <summary>Lay out the services the previous festival placed, as far as this festival's ground and limits allow.</summary>
public sealed record UseLastFestivalLayoutCommand : SessionCommand;
/// <summary>How many of each service one festival at a tier may place. The tap count is before the Another Round perk's +1.</summary>
public sealed record TierBuildLimits(int Taps, int Toilets, int FoodVans, int Bars, int FirstAid, int StewardPosts, int Marquees);

public sealed partial class GameSession
{
    private static readonly (BuildServiceKind Kind, int FeePennies)[] BuildCatalogue =
    [
        (BuildServiceKind.WaterTap, 1_500),
        (BuildServiceKind.Toilet, 3_000),
        (BuildServiceKind.FoodVan, 0), // the food trader pays the festival to pitch, instead (FoodTraders)
        (BuildServiceKind.Bar, 5_000),
        (BuildServiceKind.FirstAid, 3_500),
        (BuildServiceKind.StewardPost, 2_500),
        (BuildServiceKind.Bin, 1_000),
        (BuildServiceKind.Marquee, MarqueeRules.FeePennies)
    ];

    /// <summary>
    /// Build limits by tier: one more tap, toilet, marquee, bar and food van at Tier 2. Bins are unlimited.
    /// </summary>
    private static readonly TierBuildLimits[] BuildLimitsByTier =
    [
        new(Taps: 1, Toilets: 2, FoodVans: 1, Bars: 1, FirstAid: 1, StewardPosts: 1, Marquees: 1),
        new(Taps: 2, Toilets: 3, FoodVans: 2, Bars: 2, FirstAid: 1, StewardPosts: 1, Marquees: 2),
    ];

    public static TierBuildLimits BuildLimits(int tier) => BuildLimitsByTier[Math.Clamp(tier, 1, BuildLimitsByTier.Length) - 1];

    public static int BuildServiceFeePennies(BuildServiceKind kind) =>
        BuildCatalogue.Single(item => item.Kind == kind).FeePennies;
    /// <summary>How many of a service this festival may place: the tier's limit, and one more tap with the Another Round perk.</summary>
    public int ServiceLimit(BuildServiceKind kind) =>
        BuildServiceLimit(kind, _preparation?.Tier ?? 1) - (kind == BuildServiceKind.WaterTap && !HasPerk("another-round") ? 1 : 0);

    /// <summary>The most of a service any festival at this tier may place (the tap's assumes the perk).</summary>
    public static int BuildServiceLimit(BuildServiceKind kind, int tier)
    {
        var limits = BuildLimits(tier);
        return kind switch
        {
            BuildServiceKind.WaterTap => limits.Taps + 1,
            BuildServiceKind.Toilet => limits.Toilets,
            BuildServiceKind.FoodVan => limits.FoodVans,
            BuildServiceKind.Bar => limits.Bars,
            BuildServiceKind.FirstAid => limits.FirstAid,
            BuildServiceKind.StewardPost => limits.StewardPosts,
            BuildServiceKind.Bin => int.MaxValue,
            BuildServiceKind.Marquee => limits.Marquees,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>"One water tap is", "Two water taps are".</summary>
    private static string TapCount(int taps) => taps == 1 ? "One water tap is" : $"{(taps == 2 ? "Two" : taps.ToString())} water taps are";
    public IReadOnlyList<BuildPlacement> CaptureBuildPlacements() => _preparation?.BuildPlacements.ToArray() ?? [];
    public long BuildDraftCost => _preparation is { } p ? BuildCostOf(p) : 0;
    /// <summary>
    /// The suggested layout, taken from how the user actually lays out a Tier 1 field: two loos by the stage, a bin
    /// between stage and lane, chips and bar along the lane, first aid across it and the steward post by the gate.
    /// </summary>
    /// <remarks>
    /// From Tier 2, the tier's second tap and third toilet join it: the tap beside the Pond Stage's crowd, east of the
    /// lane, so its listeners and band don't walk the width of the field for water; the toilet on
    /// the lane end of the row. Both keep clear of either stage, the rest area and the lane, with or without the pond.
    /// A second bar stands back to back with the first, serving the Pond Stage's crowd, and a second van (curry, see
    /// <see cref="DefaultSecondVanTrader"/>) faces that crowd from the south, each with a bin for its litter. With the
    /// generators pooled the basic PA on both stages, four stalls and the lights draw exactly the 170 the pool holds.
    /// </remarks>
    public static BuildPlacement[] StandardBuildLayout(int tier = 1) => tier < 2 ? TierOneBuildLayout() :
    [
        .. TierOneBuildLayout(),
        new("water.extra-1", BuildServiceKind.WaterTap, new(148, 137), 0),
        new("toilet.extra-2", BuildServiceKind.Toilet, new(122, 121), 2),
        new(Stalls.Id(BuildServiceKind.Bar, 2), BuildServiceKind.Bar, new(147, 146), 1),
        new(Stalls.Id(BuildServiceKind.FoodVan, 2), BuildServiceKind.FoodVan, new(170, 124), 0),
        new("bin.2", BuildServiceKind.Bin, new(147, 152), 0),
        new("bin.3", BuildServiceKind.Bin, new(178, 128), 0),
    ];

    /// <summary>The trader the default layout pitches at its second van, unless the player picked one for it already.</summary>
    public const string DefaultSecondVanTrader = "trader.korma-chameleon";

    private static BuildPlacement[] TierOneBuildLayout() =>
    [
        new("water.main", BuildServiceKind.WaterTap, new(103, 128), 1),
        new("toilet.main", BuildServiceKind.Toilet, new(117, 121), 2),
        new("toilet.extra-1", BuildServiceKind.Toilet, new(112, 121), 2),
        new(Stalls.FirstVan, BuildServiceKind.FoodVan, new(140, 162), 3),
        new(Stalls.FirstBar, BuildServiceKind.Bar, new(140, 148), 3),
        new("first-aid", BuildServiceKind.FirstAid, new(139, 120), 0),
        new("steward-post", BuildServiceKind.StewardPost, new(119, 171), 2),
        new("bin.1", BuildServiceKind.Bin, new(121, 148), 0),
    ];

    /// <summary>
    /// What "Copy last festival's layout" would lay out now: each service the previous festival placed, where it stood,
    /// unless this festival's limit for its kind is already met or it no longer fits (the new tier's stage and crowd, a
    /// reserved or blocked cell, or a route it would block), when it's skipped. The first of each kind is tried before
    /// the second, and so on (water.main, the first bar and bin.1 before water.extra-1, the second bar and bin.2), so
    /// a lower limit or a clash leaves off extras rather than mains. Services the new tier allows more of stay unplaced.
    /// Null when there's no previous festival to copy.
    /// </summary>
    /// <remarks>
    /// It depends only on the carried layout, the tier, the tap limit, the stages, the generator and the water tower,
    /// never on what's placed now, so it's worked out once for those and remembered: the Build drawer's price, the
    /// confirmation, the command's check and its apply (and the save coordinator's copy of the session) share it.
    /// </remarks>
    public (BuildPlacement[] Placed, int Skipped)? LastFestivalLayoutFit()
    {
        if (_preparation is not { PreviousLayout: { } previous } p) return null;
        var key = new LayoutFitKey(previous.ToArray(), p.Tier, ServiceLimit(BuildServiceKind.WaterTap), Stages,
            _equipment is { } unit ? (unit.XMillimetres, unit.ZMillimetres) : null, p.WaterTowerOwned);
        lock (LayoutFitLock)
            if (_lastLayoutFit is { } cached && cached.Key.SameAs(key)) return (cached.Placed.ToArray(), cached.Skipped);
        var fit = FitLayout(key, _equipment);
        lock (LayoutFitLock) _lastLayoutFit = (key, fit.Placed, fit.Skipped);
        return (fit.Placed.ToArray(), fit.Skipped);
    }

    private static (BuildPlacement[] Placed, int Skipped) FitLayout(LayoutFitKey key, EquipmentSnapshot? equipment)
    {
        int Limit(BuildServiceKind kind) => kind == BuildServiceKind.WaterTap ? key.TapLimit : BuildServiceLimit(kind, key.Tier);
        string? Check(IEnumerable<BuildPlacement> layout) =>
            ValidateBuildLayout(layout.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(), key.Stages, equipment, key.WaterTowerOwned, key.Tier);
        var ordered = key.Layout.OrderBy(OrdinalOfKind).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        // The rule is one at a time, in that order: each is placed if its kind's limit has room and it fits beside those
        // placed before it, else left off. A layout that fits whole fits in every part, so the same answer comes in far
        // fewer checks: if everything left that the limits allow fits whole beside what's placed, it's all placed (most
        // layouts settle in one check); if not, the longest run of it that fits, found by halving, is placed, and the
        // service after that run is the one left off.
        var placed = new List<BuildPlacement>();
        var skipped = 0;
        for (var index = 0; index < ordered.Length;)
        {
            // From here on, in order, those the limits leave room for beside what's placed and those before them.
            var rest = new List<int>();
            for (var at = index; at < ordered.Length; at++)
                if (placed.Count(other => other.Kind == ordered[at].Kind) + rest.Count(other => ordered[other].Kind == ordered[at].Kind) < Limit(ordered[at].Kind))
                    rest.Add(at);
            IEnumerable<BuildPlacement> With(int count) => placed.Concat(rest.Take(count).Select(at => ordered[at]));
            if (rest.Count == 0 || Check(With(rest.Count)) is null)
            {
                placed.AddRange(With(rest.Count).Skip(placed.Count).ToArray());
                skipped += ordered.Length - index - rest.Count;
                break;
            }
            // With(fits) fits (what's placed already does) and With(fails) doesn't.
            var (fits, fails) = (0, rest.Count);
            while (fails - fits > 1)
            {
                var middle = (fits + fails) / 2;
                if (Check(With(middle)) is null) fits = middle; else fails = middle;
            }
            var leftOff = rest[fits];
            placed.AddRange(With(fits).Skip(placed.Count).ToArray());
            skipped += leftOff - index - fits + 1; // over its limit before it, and it
            index = leftOff + 1;
        }
        return (placed.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(), skipped);
    }

    /// <summary>A service's place among its kind: 1 for a main tap, toilet or marquee, the first bar or van, bin.1 and the one-off posts.</summary>
    private static int OrdinalOfKind(BuildPlacement item) =>
        Stalls.IsStall(item.Kind) ? Stalls.Number(item.Id) :
        item.Id.LastIndexOf(".extra-", StringComparison.Ordinal) is var extra and >= 0 && int.TryParse(item.Id[(extra + 7)..], out var index) ? index + 1 :
        item.Kind == BuildServiceKind.Bin && int.TryParse(item.Id[(item.Id.IndexOf('.') + 1)..], out var bin) ? bin : 1;

    private sealed record LayoutFitKey(BuildPlacement[] Layout, int Tier, int TapLimit, IReadOnlyList<FestivalStage> Stages,
        (long X, long Z)? Generator, bool WaterTowerOwned)
    {
        public bool SameAs(LayoutFitKey other) => Tier == other.Tier && TapLimit == other.TapLimit && WaterTowerOwned == other.WaterTowerOwned &&
            Generator == other.Generator && Stages.SequenceEqual(other.Stages, ReferenceEqualityComparer.Instance) &&
            Layout.SequenceEqual(other.Layout);
    }
    private static readonly object LayoutFitLock = new();
    private static (LayoutFitKey Key, BuildPlacement[] Placed, int Skipped)? _lastLayoutFit;

    /// <summary>A carried previous layout must be one the festival before could have placed: sorted, and valid there.</summary>
    private static string? ValidatePreviousLayout(PreparationSnapshot p, bool pondStageTrial)
    {
        if (p.PreviousLayout is not { } previous) return null;
        var before = p.Tier - 1;
        return p.CarriedIn is null || before < 1 || previous.Length == 0 ||
            !previous.Select(item => item?.Id).SequenceEqual(previous.Select(item => item?.Id).Order(StringComparer.Ordinal)) ||
            ValidateBuildLayout(previous, FestivalStages.For(before, pondStageTrial), null, false, before) is not null
            ? "Last festival's layout is invalid." : null;
    }

    private static string NextBuildId(BuildServiceKind kind, IReadOnlyList<BuildPlacement> placed)
    {
        if (Stalls.IsStall(kind)) return Stalls.Next(kind, placed.Select(item => item.Id));
        var prefix = kind switch
        {
            BuildServiceKind.WaterTap => "water",
            BuildServiceKind.Toilet => "toilet",
            BuildServiceKind.FirstAid => "first-aid",
            BuildServiceKind.StewardPost => "steward-post",
            BuildServiceKind.Bin => "bin",
            BuildServiceKind.Marquee => "marquee",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (kind == BuildServiceKind.Bin)
        {
            for (var index = 1; ; index++)
                if (!placed.Any(p => p.Id == "bin." + index)) return "bin." + index;
        }
        if (kind is not (BuildServiceKind.WaterTap or BuildServiceKind.Toilet or BuildServiceKind.Marquee)) return prefix;
        var first = prefix + ".main";
        if (!placed.Any(item => item.Id == first)) return first;
        for (var index = 1; ; index++)
        {
            var id = prefix + ".extra-" + index;
            if (!placed.Any(item => item.Id == id)) return id;
        }
    }

    private CommandResult? ValidateBuildCommand(EntityId? target, SessionCommand command)
    {
        if (target is not null || _preparation is not { Status: PreparationStatus.Preparing, Plan: { Committed: false } } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Build layout can be edited only before opening.");
        if (command is RemoveBuildServiceCommand remove)
            return p.BuildPlacements.Any(item => item.Id == remove.Id) ? null :
                CommandResult.Rejected(CommandReasonCode.UnknownTarget, "This service is not in the draft.");
        if (command is UseLastFestivalLayoutCommand)
            return p.PreviousLayout is null ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, "There's no last festival's layout to copy.") :
                LastFestivalLayoutFit()!.Value.Placed.Length == 0 ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, "None of last festival's layout fits this field.") : null;
        if (command is UseDefaultBuildLayoutCommand)
            return ValidateBuildLayout(StandardBuildLayout(p.Tier), Stages, _equipment, p.WaterTowerOwned, p.Tier) is { } defaultsError ?
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, defaultsError) : null;
        BuildPlacement proposed;
        if (command is PlaceBuildServiceCommand place)
        {
            if (!Enum.IsDefined(place.Kind)) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown service.");
            if (p.BuildPlacements.Count(item => item.Kind == place.Kind) >= ServiceLimit(place.Kind))
                return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, place.Kind == BuildServiceKind.WaterTap && !HasPerk("another-round")
                    ? $"{TapCount(ServiceLimit(place.Kind))} placed; the Another Round perk allows {(ServiceLimit(place.Kind) == 1 ? "a second" : "one more")}."
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
        if (ValidateBuildLayout(next, Stages, _equipment, p.WaterTowerOwned, p.Tier) is { } issue) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, issue);
        // Straw and matting are walked on, not built on.
        return StandsOnCover(p, proposed) ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Lift the straw or matting there first.") : null;
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
            UseDefaultBuildLayoutCommand => StandardBuildLayout(p.Tier).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            UseLastFestivalLayoutCommand => LastFestivalLayoutFit()!.Value.Placed,
            _ => throw new InvalidOperationException("Unknown build edit.")
        } };
        // A whole layout lands wherever it lands: any straw or matting under it is lifted.
        if (command is UseDefaultBuildLayoutCommand or UseLastFestivalLayoutCommand) _preparation = ClearCoverUnderServices(_preparation);
        // A whole layout's second van is the curry van, unless the player already picked its trader or van 1 has curry.
        if (command is UseDefaultBuildLayoutCommand or UseLastFestivalLayoutCommand && _preparation.Plan is { } plan && _preparation.BuildPlacements.Any(item => item.Id == Stalls.Id(BuildServiceKind.FoodVan, 2)) &&
            ChosenTraderId(plan, Stalls.Id(BuildServiceKind.FoodVan, 2)) is null && plan.TraderId != DefaultSecondVanTrader)
            _preparation = _preparation with { Plan = plan with { VanTraders = (plan.VanTraders ?? []).Append(new(Stalls.Id(BuildServiceKind.FoodVan, 2), DefaultSecondVanTrader))
                .OrderBy(item => Stalls.Number(item.VanId)).ToArray() } };
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

    /// <remarks>
    /// Every check here must stay monotone in the placements: if a layout passes, any subset of it passes too. The
    /// last festival's copy relies on that to bisect for the first clash. So no "must have" or "at least" rules here
    /// (e.g. "toilets need a bin within N cells"); those belong in the start requirements.
    /// </remarks>
    private static string? ValidateBuildLayout(IReadOnlyList<BuildPlacement> placements, IReadOnlyList<FestivalStage> stages,
        EquipmentSnapshot? equipment = null, bool waterTowerOwned = false, int tier = 1)
    {
        // A main one, then numbered extras up to the tier's limit.
        bool Numbered(BuildPlacement item, string prefix) => item.Id == prefix + ".main" ||
            Enumerable.Range(1, BuildServiceLimit(item.Kind, tier) - 1).Any(index => item.Id == $"{prefix}.extra-{index}");
        bool IdentityMatchesKind(BuildPlacement item) => item.Kind switch
        {
            BuildServiceKind.WaterTap => Numbered(item, "water"),
            BuildServiceKind.Toilet => Numbered(item, "toilet"),
            BuildServiceKind.Marquee => Numbered(item, "marquee"),
            BuildServiceKind.FoodVan or BuildServiceKind.Bar => Stalls.Number(item.Id) is var number and > 0 && Stalls.KindOf(item.Id) == item.Kind && number <= BuildServiceLimit(item.Kind, tier),
            BuildServiceKind.FirstAid => item.Id == "first-aid",
            BuildServiceKind.StewardPost => item.Id == "steward-post",
            BuildServiceKind.Bin => item.Id.StartsWith("bin.", StringComparison.Ordinal) && int.TryParse(item.Id[4..], out var number) && number > 0 && item.Id == "bin." + number,
            _ => false
        };
        if (placements.Any(item => item is null || !Enum.IsDefined(item.Kind) || item.QuarterTurns is < 0 or > 3 ||
            string.IsNullOrWhiteSpace(item.Id) || !IdentityMatchesKind(item)) ||
            placements.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != placements.Count ||
            BuildCatalogue.Any(entry => placements.Count(item => item.Kind == entry.Kind) > BuildServiceLimit(entry.Kind, tier)))
            return "Build identities or service limits are invalid.";
        var terrain = new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain(stages.Any(stage => stage.Id == FestivalStages.BarnId)));
        var reserved = new HashSet<GridCell>();
        // The stage, its stairs and drawbar, and backstage behind the barriers.
        for (var x = 66; x <= 101; x++)
            for (var z = 112; z <= 164; z++)
                if (FestivalStages.Main.Reserve(new(x, z)) || Backstage.Area(new(x, z))) reserved.Add(new(x, z));
        // Any other stage's riser, stair foot, generator and band ground.
        foreach (var stage in stages.Skip(1))
            for (var x = stage.ReserveBounds.MinX; x <= stage.ReserveBounds.MaxX; x++)
                for (var z = stage.ReserveBounds.MinZ; z <= stage.ReserveBounds.MaxZ; z++)
                    if (stage.Reserve(new(x, z))) reserved.Add(new(x, z));
        // The rest area out in front of first aid stays clear; it moves with first aid, and only exists once it's placed.
        var restCentre = RestCentre(placements);
        var restArea = new HashSet<GridCell>();
        if (placements.Any(item => item.Kind == BuildServiceKind.FirstAid))
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
        var occupants = new Dictionary<GridCell, List<BuildPlacement>>();
        var tentSolids = placements.Where(item => item.Kind == BuildServiceKind.Marquee)
            .ToDictionary(item => item.Id, item => MarqueeSolidCells(item.Cell, item.QuarterTurns).ToHashSet());
        bool CanShare(BuildPlacement a, BuildPlacement b, GridCell cell) =>
            a.Kind == BuildServiceKind.Bin && b.Kind == BuildServiceKind.Marquee && !tentSolids[b.Id].Contains(cell) ||
            b.Kind == BuildServiceKind.Bin && a.Kind == BuildServiceKind.Marquee && !tentSolids[a.Id].Contains(cell);
        foreach (var item in placements)
        {
            var cells = BuildReservedCells(item).Distinct().ToArray();
            if (cells.Any(cell => cell.X is < 68 or > 190 || cell.Z is < 108 or > 190 ||
                !terrain.Contains(cell) || terrain.Get(cell) is not { IsWalkable: true, Surface: GroundSurface.Grass }))
                return "Service footprint or entrance needs clear grass inside the festival site.";
            // Bins are the exception: a crowd makes litter, and a bin is small enough to stand among it.
            if (item.Kind != BuildServiceKind.Bin && cells.Any(cell => InAudienceArea(stages, cell))) return "Keep the audience area in front of the stage clear.";
            // A toilet's walkway in front of its door may be shared with the toilet beside it, so a row of loos can
            // stand side by side; nothing else may stand on it, and nothing may overlap a cubicle.
            var access = item.Kind == BuildServiceKind.Toilet ? ToiletAccessCells(ToiletOf(item)).ToHashSet() : [];
            foreach (var cell in cells)
            {
                if (access.Contains(cell) && sharedAccess.Contains(cell)) continue;
                // The rest area moves with first aid, so say which one is in the way.
                if (restArea.Contains(cell)) return "Keep clear of the ground in front of first aid, where overheated guests rest.";
                if (reserved.Contains(cell)) return "Service footprint or access overlaps another placement.";
                if (!occupants.TryGetValue(cell, out var prior)) occupants[cell] = prior = [];
                if (prior.Any(other => !CanShare(item, other, cell))) return "Service footprint or access overlaps another placement.";
                prior.Add(item);
                if (access.Contains(cell)) sharedAccess.Add(cell);
            }
        }
        if (!BuildAccessClear(placements, stages, equipment, waterTowerOwned))
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

    private static bool BuildAccessClear(IReadOnlyList<BuildPlacement> placements, IReadOnlyList<FestivalStage> stages,
        EquipmentSnapshot? equipment, bool waterTowerOwned)
    {
        var blocked = new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain(stages.Any(stage => stage.Id == FestivalStages.BarnId)))
            .Overrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        void Block(GridCell cell) => blocked[cell] = new(cell, GroundSurface.Grass, false);
        foreach (var item in placements)
        {
            switch (item.Kind)
            {
                case BuildServiceKind.Bin:
                    foreach (var cell in BinSolidCells(item.Cell)) Block(cell);
                    break;
                case BuildServiceKind.Marquee:
                    foreach (var cell in MarqueeSolidCells(item.Cell, item.QuarterTurns)) Block(cell);
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
                    foreach (var cell in ImmersionFootprint(new(Stalls.Id(item.Kind, 1),
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
        foreach (var (cell, walkable) in stages.SelectMany(stage => stage.Cells())) blocked[cell] = new(cell, GroundSurface.Grass, walkable);
        var grid = new TraversalGrid(blocked.Values);
        var destinations = placements.Any(item => item.Kind == BuildServiceKind.FirstAid) ? new List<GridCell> { RestCentre(placements) } : [];
        foreach (var item in placements)
        {
            switch (item.Kind)
            {
                case BuildServiceKind.Bin:
                    destinations.AddRange(new[] { new GridCell(item.Cell.X, item.Cell.Z + 2), new(item.Cell.X + 2, item.Cell.Z), new(item.Cell.X, item.Cell.Z - 2), new(item.Cell.X - 2, item.Cell.Z) });
                    break;
                case BuildServiceKind.WaterTap:
                    destinations.Add(WaterServiceCell(item.Cell, item.QuarterTurns)); break;
                case BuildServiceKind.Marquee:
                    destinations.Add(item.Cell); break;
                case BuildServiceKind.Toilet:
                    var toilet = new ToiletFacility(item.Id, item.Cell, item.QuarterTurns, [], null, false, 0, 0, 0,
                        ToiletRules.CapacityMillilitres, ToiletRules.ContainmentPermille);
                    destinations.Add(ToiletQueueCell(toilet, 0));
                    destinations.Add(ToiletInsideCell(toilet));
                    destinations.Add(ToiletExitCell(toilet));
                    break;
                case BuildServiceKind.FoodVan or BuildServiceKind.Bar:
                    destinations.Add(ImmersionServiceCell(new(Stalls.Id(item.Kind, 1),
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
            // Keep the full berth for other services; bins may share its non-solid canopy cells.
            BuildServiceKind.Marquee => MarqueeFootprintCells(item.Cell, item.QuarterTurns),
            BuildServiceKind.WaterTap => Round(item.Cell)
                .Append(WaterServiceCell(item.Cell, item.QuarterTurns)).ToArray(),
            BuildServiceKind.Toilet => ToiletReservedCells(ToiletOf(item)),
            BuildServiceKind.FoodVan or BuildServiceKind.Bar => ImmersionFootprint(new(Stalls.Id(item.Kind, 1),
                item.Cell, item.QuarterTurns, []))
                .Append(ImmersionServiceCell(new(Stalls.Id(item.Kind, 1),
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
