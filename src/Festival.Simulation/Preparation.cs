using System.Text.Json;

namespace Festival.Simulation;

public enum PreparationStatus { Preparing, Running, Departing, Finished, Failed }
public sealed record PreparationOffer(string Id, string Category, string Name, int PricePennies, int MusicQuality, int Genre);
public sealed record EditionPerson(ulong AgentId, string Name, ProtectedPersonRole Role, int ExpectedGenre,
    bool Admitted = false, bool Departed = false, int Satisfaction = 5_000, int MusicRisk = 0);
public sealed record PreparationPayment(int Id, string OfferId, int Attempt, long Tick, int AmountPennies,
    LedgerAccountType DebitAccount);
public sealed record PreparationInventoryBalance(int OpeningUnits, int PurchasedUnits, int ConsumedUnits, int RemainingUnits, int UnitCostPennies);
public sealed record WaterPlacement(string Id, GridCell Cell) { public int QuarterTurns { get; init; } public int GeometryVersion { get; init; } }
public sealed record PreparationSnapshot(int Version, int Tier, ulong OfferSeed, int Attempt, PreparationStatus Status,
    ulong FinanceOwnerId, ulong StockId, long StartedTick, bool FixtureOutcomesEnabled,
    string[] OwnedEquipment, string[] Rentals, string[] Contacts, string[] WorkContracts, string[] AcceptedOffers,
    EditionPerson[] People, PreparationPayment[] Payments, int StockConsumed, long OpeningCashPennies)
{
    public int CommunityShareAttempt { get; init; }
    public bool CommunityFavourClaimed { get; init; }
    public bool RetryEconomyFixtureEnabled { get; init; }
    public ulong? MaintenanceWorkerId { get; init; }
    public string[] ExtraWaterSiteIds { get; init; } = [];
    public WaterPlacement[] WaterPlacements { get; init; } = [];
    public GridCell PrimaryWaterCell { get; init; } = GameSession.MedicalWaterCell;
    public int PrimaryWaterQuarterTurns { get; init; }
    public int PrimaryWaterGeometryVersion { get; init; }
    public bool WaterTowerOwned { get; init; }
    public bool ExtraMedicSlotOwned { get; init; }
    public bool ExtraStewardSlotOwned { get; init; }
    public bool RespondersUpgraded { get; init; }
    public StaffProfile[] StaffProfiles { get; init; } = [];
}
public sealed record AcceptPreparationOfferCommand(string OfferId) : SessionCommand;
public sealed record StartPreparedEditionCommand : SessionCommand;
public sealed record CommitCommunityWaterShareCommand : SessionCommand;
public sealed record ApplyWaterFoundationEffectCommand(string EffectId) : SessionCommand;
public sealed record PlaceWaterPointCommand(GridCell Cell, int QuarterTurns = 0) : SessionCommand;
public sealed record MovePrimaryWaterPointCommand(GridCell Cell, int QuarterTurns = 0) : SessionCommand;
public sealed record MoveWaterPointCommand(string PointId, GridCell Cell, int QuarterTurns = 0) : SessionCommand;

public sealed partial class GameSession
{
    // 160 festival minutes = eight live real minutes at the unchanged 80 ticks/s;
    // preparation and pauses target the remaining two minutes, pending playtesting.
    public const int PreparedWeekendTicks = 38_400;
    public const int PreparedDayTicks = 24_000;
    public int PreparedEditionDurationTicks => _programme is null ? PreparedWeekendTicks : PreparedDayTicks;
    private PreparationSnapshot? _preparation;
    public PreparationStatus? PreparedStatus => _preparation?.Status;
    public PreparationSnapshot? CapturePreparation() => _preparation is null ? null :
        JsonSerializer.Deserialize<PreparationSnapshot>(JsonSerializer.Serialize(_preparation));
    internal string? PreparationCanonicalJson => _preparation is not { } p ? null : StaffCompatibleCanonicalJson(p,
        p.ExtraMedicSlotOwned ? "" : nameof(p.ExtraMedicSlotOwned), p.ExtraStewardSlotOwned ? "" : nameof(p.ExtraStewardSlotOwned),
        p.RespondersUpgraded ? "" : nameof(p.RespondersUpgraded), p.StaffProfiles.Length > 0 ? "" : nameof(p.StaffProfiles),
        p.PrimaryWaterQuarterTurns != 0 ? "" : nameof(p.PrimaryWaterQuarterTurns), p.PrimaryWaterGeometryVersion != 0 ? "" : nameof(p.PrimaryWaterGeometryVersion));
    public bool PreparationBoundaryOnNextTick => !IsPaused && _preparation is { } p &&
        (p.Status == PreparationStatus.Running && CurrentTick - p.StartedTick >= PreparedEditionDurationTicks - 1 && p.People.All(item => item.Admitted) ||
         p.Status == PreparationStatus.Departing && p.People.All(item => item.Departed));

    public PreparationInventoryBalance? GetPreparationInventoryBalance() => _preparation is not { } p ? null :
        new(40, p.Payments.Count(item => (p.FixtureOutcomesEnabled || item.Attempt == p.Attempt) && item.OfferId == "contract.stock") * 50,
            p.StockConsumed, _ownedStocks[new(p.StockId)].Quantity, 60);

    public string? CommunityWaterShareDisclosure => _preparation is null || _medical is null ? null :
        "Share free water with the neighbouring community for this weekend. Personal drinking relief is capped at 12 thirst units/tick before any owned tower's +4 bonus (ordinary rates: 8, 12, 16 or 20); faster drinkers take longer and queues may grow. Honour the full weekend to earn 1 Council Favour, once per campaign.";
    public bool CommunityWaterShareActive => _preparation is { Status: PreparationStatus.Running or PreparationStatus.Departing } p && p.CommunityShareAttempt == p.Attempt;

    private CommandResult? ValidateCommunityWaterShare(EntityId? target)
    {
        if (target is not null || _medical is null || _preparation is not { Status: PreparationStatus.Preparing } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Water sharing must be committed before a Hot weekend.");
        if (p.CommunityShareAttempt != 0)
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "The once-per-campaign water choice was already made.");
        return null;
    }

    private void ApplyCommunityWaterShare() => _preparation = _preparation! with { CommunityShareAttempt = _preparation.Attempt };

    private CommandResult? ValidateWaterFoundationEffect(EntityId? target, ApplyWaterFoundationEffectCommand command)
    {
        if (target is not null || _medical is null || _preparation is not { Status: PreparationStatus.Preparing } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Water foundations can be placed only during Hot-weekend preparation.");
        if (command.EffectId == "water.tower")
            return p.WaterTowerOwned ? CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "Water tower already owned.") : null;
        return CommandResult.Rejected(CommandReasonCode.InvalidParameter,
            "Fixed-site water commands are no longer available; choose a grass site for the extra tap.");
    }

    private void ApplyWaterFoundationEffect(ApplyWaterFoundationEffectCommand command)
    {
        var p = _preparation!;
        if (command.EffectId != "water.tower") throw new InvalidOperationException("Legacy fixed-site water creation is disabled.");
        _preparation = p with { WaterTowerOwned = true };
    }

    private static WaterPlacement[] EffectiveWaterPlacements(PreparationSnapshot p) =>
        p.ExtraWaterSiteIds.Select(id => p.WaterPlacements.SingleOrDefault(item => item.Id == id) ??
            new WaterPlacement(id, ExtraWaterSites.Single(site => site.Id == id).Cell)).ToArray();

    private CommandResult? ValidateWaterPlacement(EntityId? target, GridCell cell, string? movingId, int quarterTurns)
    {
        if (target is not null || _medical is null || _preparation is not { Status: PreparationStatus.Preparing } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Water points can be positioned only during Hot-weekend preparation.");
        if (quarterTurns is < 0 or > 3 || movingId is not null && movingId != "water.main" && !p.ExtraWaterSiteIds.Contains(movingId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown water point or orientation.");
        if (movingId is null && p.ExtraWaterSiteIds.Length >= 2)
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "The two additional standpipes are already placed.");
        if (movingId == "water.main" && cell == p.PrimaryWaterCell && quarterTurns == p.PrimaryWaterQuarterTurns && p.PrimaryWaterGeometryVersion == 1)
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "The original standpipe is already at this site.");
        var issue = ValidateWaterPlacementCell(cell, p, movingId, _equipment, quarterTurns);
        if(issue is null && WaterOverlapsImmersion(_immersion,cell,quarterTurns))issue="Water overlaps a placed food/drink vendor or its queue.";
        return issue is null ? null : CommandResult.Rejected(CommandReasonCode.InvalidParameter, issue);
    }

    private static string? ValidateWaterPlacementCell(GridCell cell, PreparationSnapshot p, string? movingId,
        EquipmentSnapshot? equipment, int quarterTurns = 0, int geometryVersion = 1)
    {
        // This deliberately bounds the first placeable-object interaction to the open festival field.
        // The approved tower stays fixed; there is no general building editor or pipe network.
        if (cell.X is < 68 or > 119 || cell.Z is < 112 or > 140)
            return "Choose a grass site inside the open festival field.";
        var terrain = new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        var placed = EffectiveWaterPlacements(p);
        var others = placed.Where(item => item.Id != movingId).Select(item => new WaterPointState(item.Id, item.Cell, [], [], null, 0) { QuarterTurns = item.QuarterTurns, GeometryVersion = item.GeometryVersion }).ToList();
        if (movingId != "water.main") others.Insert(0, new WaterPointState("water.main", p.PrimaryWaterCell, [], [], null, 0) { QuarterTurns = p.PrimaryWaterQuarterTurns, GeometryVersion = p.PrimaryWaterGeometryVersion });
        var proposed = new WaterPointState(movingId ?? "water.proposed", cell, [], [], null, 0) { QuarterTurns = quarterTurns, GeometryVersion = geometryVersion };
        var occupied = new HashSet<GridCell>();
        static void Footprint(HashSet<GridCell> cells, GridCell centre, int radius)
        {
            for (var z = centre.Z - radius; z <= centre.Z + radius; z++)
            for (var x = centre.X - radius; x <= centre.X + radius; x++) cells.Add(new GridCell(x, z));
        }
        foreach (var point in others) Footprint(occupied, point.Cell, WaterFootprintRadius(point) + 1);
        Footprint(occupied, MedicalTentCell, 4);
        Footprint(occupied, MedicalRestCell, 1);
        Footprint(occupied, MedicalMedicCell, 1);
        if (p.WaterTowerOwned) Footprint(occupied, WaterTowerCell, 4);
        if (equipment is { } unit) Footprint(occupied, TraversalGrid.WorldToCell(unit.XMillimetres, unit.ZMillimetres), 5);
        for (var z = 139; z <= 160; z++)
        for (var x = 90; x <= 101; x++) occupied.Add(new GridCell(x, z));
        var proposedCells = new HashSet<GridCell>();
        Footprint(proposedCells, cell, WaterFootprintRadius(proposed));
        proposedCells.Add(WaterPointServiceCell(proposed));
        foreach (var point in others) Footprint(occupied, WaterPointServiceCell(point), 1);
        if (proposedCells.Any(candidate => !terrain.Contains(candidate) ||
            terrain.Get(candidate) is not { IsWalkable: true, Surface: GroundSurface.Grass }))
            return "The tap footprint and service front need clear grass.";
        if (proposedCells.Any(occupied.Contains))
            return "The tap or queue overlaps a building, another line, the stage or a protected route.";
        // Reserve the proposed solid footprint and prove all service fronts and first-aid rest
        // remain reachable from the gate. Queue slots remain walkable and cannot be occupied by it.
        var overrides = terrain.Overrides.ToDictionary(item => item.Key, item => item.Value);
        foreach (var point in others.Append(proposed))
        for (var z = point.Cell.Z - WaterFootprintRadius(point); z <= point.Cell.Z + WaterFootprintRadius(point); z++)
        for (var x = point.Cell.X - WaterFootprintRadius(point); x <= point.Cell.X + WaterFootprintRadius(point); x++)
        {
            var blocked = new GridCell(x, z);
            overrides[blocked] = new(blocked, GroundSurface.Grass, false);
        }
        var grid = new TraversalGrid(overrides.Values);
        foreach (var destination in others.Append(proposed).Select(WaterPointServiceCell).Append(MedicalRestCell))
            if (!DeterministicPathfinder.FindPath(grid, MedicalExitCell, destination).Found)
                return "This position blocks a walkable route to water or first aid.";
        return null;
    }

    private void ApplyWaterPlacement(GridCell cell, string? movingId, int quarterTurns)
    {
        var p = _preparation!;
        if (movingId == "water.main")
        {
            _preparation = p with { PrimaryWaterCell = cell, PrimaryWaterQuarterTurns = quarterTurns, PrimaryWaterGeometryVersion = 1 };
            _medical = _medical! with { MainWaterCell = cell, MainWaterQuarterTurns = quarterTurns, MainWaterGeometryVersion = 1, MainWaterQueueCells = [] };
            return;
        }
        if (movingId is not null)
        {
            _preparation = p with { WaterPlacements = EffectiveWaterPlacements(p).Select(item => item.Id == movingId ? new WaterPlacement(item.Id, cell) { QuarterTurns = quarterTurns, GeometryVersion = 1 } : item).ToArray() };
            _medical = _medical! with { ExtraWaterPoints = _medical.ExtraWaterPoints.Select(item => item.Id == movingId ? item with { Cell = cell, QuarterTurns = quarterTurns, GeometryVersion = 1, QueueCells = [] } : item).ToArray() };
            return;
        }
        var id = $"water.extra-{p.ExtraWaterSiteIds.Length + 1}";
        _preparation = p with { ExtraWaterSiteIds = p.ExtraWaterSiteIds.Append(id).Order(StringComparer.Ordinal).ToArray(),
            WaterPlacements = p.WaterPlacements.Append(new WaterPlacement(id, cell) { QuarterTurns = quarterTurns, GeometryVersion = 1 }).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray() };
        _medical = _medical! with { ExtraWaterPoints = _medical.ExtraWaterPoints.Append(new WaterPointState(id, cell, [], [], null, 0) { QuarterTurns = quarterTurns, GeometryVersion = 1 })
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray() };
    }

    public static GameSession CreatePreparedCampaign(ulong seed, int tier = 1, bool fixtureOutcomesEnabled = false)
    {
        if (tier is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(tier));
        var session = CreateCampaign(seed);
        session.Phase = SessionPhase.OpeningCheck;
        // Retain campaign identity, inherited farm and opening loan. The old planning-week
        // shell is dormant in this route; it remains available for legacy saves/fixtures.
        var owner = session._festivalFinances.Keys.Single();
        var stock = new EntityId(session.NextEntityId++);
        session._ownedStocks.Add(stock, new OwnedStockState
        { ServiceId = stock, OwnerId = owner, Quantity = 40, UnitCostBasisPennies = 60 });
        var people = Enumerable.Range(0, tier * 20 + 4).Select(index => new EditionPerson(
            session.NextEntityId++, index < tier * 20 ? $"Guest {index + 1:00}" : index == tier * 20 ? "Casey Vale" : new[] { "Alex Reed", "Blair Moss", "Kit Rowan" }[index - tier * 20 - 1],
            index < tier * 20 ? ProtectedPersonRole.Guest : index == tier * 20 ? ProtectedPersonRole.Staff : ProtectedPersonRole.Performer,
            index % 4 == 0 ? 1 - (int)(seed % 2) : (int)(seed % 2))).ToArray();
        foreach (var person in people)
            session._wallets.Add(new(person.AgentId), new WalletState { OwnerId = new(person.AgentId), CashPennies = 500 });
        session._preparation = new(1, tier, seed ^ ((ulong)tier * 0x9E3779B97F4A7C15UL), 1,
            PreparationStatus.Preparing, owner.Value, stock.Value, 0, fixtureOutcomesEnabled,
            [], [], [], [], [], people, [], 0, CampaignDefaults.OpeningCashPennies);
        return session;
    }

    public IReadOnlyList<LedgerEntry> GetPreparationLedgerEntries()
    {
        if (_preparation is not { } p) return [];
        var owner = new EntityId(p.FinanceOwnerId);
        return p.Payments.SelectMany(payment => new[]
        {
            new LedgerEntry(owner, payment.DebitAccount, payment.AmountPennies),
            new LedgerEntry(owner, LedgerAccountType.CashAsset, -payment.AmountPennies)
        }).Concat(new[]
        {
            new LedgerEntry(owner, LedgerAccountType.CostOfGoodsSold, p.StockConsumed * 60L),
            new LedgerEntry(owner, LedgerAccountType.InventoryAsset, -p.StockConsumed * 60L)
        }).ToArray();
    }

    public IReadOnlyList<PreparationOffer> GetPreparationOffers()
    {
        if (_preparation is null) return [];
        var premium = (int)(_preparation.OfferSeed % 3) * 500;
        PreparationOffer[] offers = [
            new("act.folk", "act", "Alex: meadow folk • folk fit", 6_000 + premium, 1_000, 0),
            new("act.punk", "act", "Alex: barn punk • punk fit", 6_000 + premium, 1_000, 1),
            new("staff.steward", "staff", "Casey: basic sound shift • +400 quality", 2_000, 400, -1),
            new("staff.engineer", "staff", "Casey: extended sound shift • +800 quality", 4_000, 800, -1),
            new("equipment.buy", "equipment", "Buy sound rig • +1000 quality; retained", 12_000, 1_000, -1),
            new("equipment.rent", "equipment", "Rent sound rig • +500 quality; this weekend", 3_000, 500, -1),
            new("contract.stock", "contract", "50 refreshments • unused stock resets on retry", 3_000, 0, -1)
        ];
        if (_equipment is not null) offers = offers.Append(new PreparationOffer("maintenance.worker", "maintenance", "Morgan: maintenance worker • physical repair", 1_500, 0, -1)).ToArray();
        if (_disorder is not null) offers = offers.Concat(new[] {
            new PreparationOffer("staff.extra-medic", "extra-medic", "Avery Brooks: extra medic • paid weekend contract", 3000, 0, -1),
            new PreparationOffer("staff.extra-steward", "extra-steward", "Sam Ellis: extra steward • paid weekend contract", 3000, 0, -1) }).ToArray();
        if (_programme is not null) offers = offers.Where(offer => offer.Category != "act").Concat(FestivalActs.Select(act => new PreparationOffer(act.Id, "act", act.Name, act.PricePennies, 1000, act.Genre))).ToArray();
        return offers;
    }

    private CommandResult? ValidatePreparationCommand(EntityId? target, SessionCommand command)
    {
        if (_preparation is not { Status: PreparationStatus.Preparing } p || target is not null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Available only during edition preparation.");
        var offers = GetPreparationOffers();
        if (command is AcceptPreparationOfferCommand accept)
        {
            if (_immersion is not null && accept.OfferId=="contract.stock") return CommandResult.Rejected(CommandReasonCode.InvalidParameter,"Buy physical food/drink starter stock instead.");
            var offer = offers.SingleOrDefault(item => item.Id == accept.OfferId);
            if (offer is null) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown preparation offer.");
            if (_programme is not null && offer.Category == "act") return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Confirm all three acts together through the programme.");
            if (offer.Category is "extra-medic" or "extra-steward" &&
                (!(offer.Category == "extra-medic" ? p.ExtraMedicSlotOwned : p.ExtraStewardSlotOwned) || p.People.Length >= 50))
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Requires the matching role slot and room below 50 active people.");
            if (p.AcceptedOffers.Any(id => offers.Single(item => item.Id == id).Category == offer.Category) ||
                offer.Category == "equipment" && p.OwnedEquipment.Contains("sound-rig"))
                return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "This category is already supplied for the edition.");
            if (_festivalFinances[new(p.FinanceOwnerId)].CashPennies < offer.PricePennies)
                return CommandResult.Rejected(CommandReasonCode.InsufficientFunds, "Insufficient cash for this commitment.");
        }
        else if ((_programme is null ? !p.AcceptedOffers.Any(id => id.StartsWith("act.", StringComparison.Ordinal)) : _programme.ActIds.Length != 3) || !p.WorkContracts.Any(id => id.StartsWith("staff.", StringComparison.Ordinal)))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Book one act and one worker for the fixed protected roster.");
        return null;
    }

    private void ApplyPreparationOffer(AcceptPreparationOfferCommand command)
    {
        var p = _preparation!;
        var offer = GetPreparationOffers().Single(item => item.Id == command.OfferId);
        _festivalFinances[new(p.FinanceOwnerId)].CashPennies -= offer.PricePennies;
        if (offer.Category == "contract") _ownedStocks[new(p.StockId)].Quantity += 50;
        _preparation = p with
        {
            AcceptedOffers = p.AcceptedOffers.Append(offer.Id).Order(StringComparer.Ordinal).ToArray(),
            OwnedEquipment = offer.Id == "equipment.buy" ? ["sound-rig"] : p.OwnedEquipment,
            Rentals = offer.Id == "equipment.rent" ? ["sound-rig"] : p.Rentals,
            Contacts = offer.Category is "staff" or "maintenance" or "extra-medic" or "extra-steward" ? p.Contacts.Append(offer.Category switch {
                "staff" => "contact.casey-vale", "maintenance" => "contact.morgan-finch", "extra-medic" => "contact.avery-brooks", _ => "contact.sam-ellis" }).Distinct().Order(StringComparer.Ordinal).ToArray() : p.Contacts,
            WorkContracts = offer.Category is "staff" or "maintenance" or "extra-medic" or "extra-steward" ? p.WorkContracts.Append(offer.Id).Order(StringComparer.Ordinal).ToArray() : p.WorkContracts,
            Payments = p.Payments.Append(new(p.Payments.Length + 1, offer.Id, p.Attempt, CurrentTick, offer.PricePennies,
                offer.Category is "equipment" && offer.Id == "equipment.buy" ? LedgerAccountType.EquipmentAsset :
                offer.Category == "contract" ? LedgerAccountType.InventoryAsset : LedgerAccountType.AdministrationExpense)).ToArray()
        };
        if (offer.Category is "extra-medic" or "extra-steward")
            HireOptionalStaff(offer.Category == "extra-medic" ? ResponseRole.Medic : ResponseRole.Steward);
        if (offer.Category == "maintenance")
        {
            var id = p.MaintenanceWorkerId ?? NextEntityId++;
            if (p.MaintenanceWorkerId is null)
                _wallets.Add(new(id), new WalletState { OwnerId = new(id), CashPennies = 500 });
            _preparation = _preparation with { MaintenanceWorkerId = id,
                People = _preparation.People.Append(new EditionPerson(id, "Morgan Finch", ProtectedPersonRole.Staff, 0)).OrderBy(item => item.AgentId).ToArray() };
            _equipment = _equipment! with { WorkerId = id };
        }
    }

    private void ApplyStartPreparedEdition()
    {
        var p = _preparation!;
        _traversalGrid = new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        if (_equipment is { } unit)
        {
            var terrain = _traversalGrid.Overrides.ToDictionary(item => item.Key, item => item.Value);
            var lower = TraversalGrid.WorldToCell(unit.XMillimetres - 1_500, unit.ZMillimetres - 1_000);
            var upper = TraversalGrid.WorldToCell(unit.XMillimetres + 1_500, unit.ZMillimetres + 1_000);
            for (var z = lower.Z; z <= upper.Z; z++)
            for (var x = lower.X; x <= upper.X; x++)
            {
                var cell = new GridCell(x, z);
                terrain[cell] = new(cell, GroundSurface.Grass, false);
            }
            _traversalGrid = new TraversalGrid(terrain.Values);
        }
        // The trailer is solid except for its visible stair at the south end and
        // the walkable deck. Performers use ground -> stair -> deck marks;
        // guests cannot cut through the trailer as if it were bare grass.
        {
            var terrain = _traversalGrid.Overrides.ToDictionary(item => item.Key, item => item.Value);
            for (var z = 140; z <= 159; z++)
            for (var x = 91; x <= 100; x++)
            {
                var cell = new GridCell(x, z);
                var deck = x is >= 92 and <= 98 && z is >= 143 and <= 157;
                var steps = x is >= 98 and <= 100 && z is >= 156 and <= 158;
                terrain[cell] = new(cell, GroundSurface.Grass, deck || steps);
            }
            _traversalGrid = new TraversalGrid(terrain.Values);
        }
        if (_medical is not null)
        {
            var terrain = _traversalGrid.Overrides.ToDictionary(item => item.Key, item => item.Value);
            foreach (var (centre, radius) in new[] { (p.PrimaryWaterCell, p.PrimaryWaterGeometryVersion == 1 ? 1 : 3), (MedicalTentCell, 3) }
                         .Concat(_medical.ExtraWaterPoints.Select(point => (point.Cell, WaterFootprintRadius(point)))))
            for (var z = centre.Z - radius; z <= centre.Z + radius; z++)
            for (var x = centre.X - radius; x <= centre.X + radius; x++)
            {
                var cell = new GridCell(x, z);
                terrain[cell] = new(cell, GroundSurface.Grass, false);
            }
            if (p.WaterTowerOwned)
            {
                // The approved 3.5 m reservation is a fixed farmhouse-side obstacle,
                // not a plumbing/flow simulation or another drinker location.
                for (var z = WaterTowerCell.Z - 3; z <= WaterTowerCell.Z + 3; z++)
                for (var x = WaterTowerCell.X - 3; x <= WaterTowerCell.X + 3; x++)
                {
                    var cell = new GridCell(x, z);
                    terrain[cell] = new(cell, GroundSurface.Grass, false);
                }
            }
            _traversalGrid = new TraversalGrid(terrain.Values);
        }
        BlockImmersionVendors();
        for (var index = 0; index < p.People.Length; index++)
        {
            var person = p.People[index];
            var id = new EntityId(person.AgentId);
            var position = TraversalGrid.CellCentre(PreparedStart(index));
            _navigationAgents.Add(id, new NavigationAgentState
            {
                Id = id, XMillimetres = position.XMillimetres, ZMillimetres = position.ZMillimetres,
                SegmentOriginXMillimetres = position.XMillimetres, SegmentOriginZMillimetres = position.ZMillimetres,
                WalkingSpeedPermille = GetResponseStaff().SingleOrDefault(item => item.AgentId == id.Value)?.WalkingSpeedPermille ?? GetWalkingSpeedPermille(id), Action = AgentNavigationAction.Idle
            });
            var profile = GetResponseStaff().SingleOrDefault(item => item.AgentId == person.AgentId);
            var dutyCell = profile is null ? PreparedPlace(index) : StaffDutyCell(person.AgentId, profile.Role);
            ApplyAgentDestination(id, new(dutyCell, "edition.arrival"));
        }
        _preparation = p with { Status = PreparationStatus.Running, StartedTick = CurrentTick };
        Phase = SessionPhase.Live;
        StartEquipmentLifecycle();
        StartLivePerformance();
    }

    private static GridCell PreparedStart(int index) => new(122 + index % 6 * 2, 190 + index / 6 * 2);
    private static GridCell PreparedPlace(int index) => new(122 + index % 6 * 2, 156 + index / 6 * 2);

    private void AdvancePreparation()
    {
        if (_preparation is not { Status: PreparationStatus.Running or PreparationStatus.Departing } p) return;
        // Boundary eligibility reads the start state, so persistence can stage exactly the
        // boundary tick without cloning every ordinary movement tick. Final physical arrival
        // is committed first; the following tick commits settlement and contract expiry.
        var transitionAtStart = PreparationBoundaryOnNextTick;
        var people = p.People.ToArray();
        var consumed = p.StockConsumed;
        for (var index = 0; index < people.Length; index++)
        {
            var person = people[index];
            var agent = _navigationAgents[new(person.AgentId)];
            if (agent.Action != AgentNavigationAction.Arrived) continue;
            if (p.Status == PreparationStatus.Running && !person.Admitted)
            {
                var satisfaction = 5_000;
                if (person.Role == ProtectedPersonRole.Guest && _immersion is null)
                {
                    var stock = _ownedStocks[new(p.StockId)];
                    if (stock.Quantity > 0) { stock.Quantity--; consumed++; satisfaction = Math.Min(10_000, satisfaction + 200); }
                }
                people[index] = person with { Admitted = true, Satisfaction = satisfaction, MusicRisk = 0 };
            }
            if (p.Status == PreparationStatus.Departing && !person.Departed && ImmersionCanMarkDeparted(person.AgentId, index))
                people[index] = person with { Departed = true };
        }
        _preparation = p = p with { People = people, StockConsumed = consumed };
        if (p.Status == PreparationStatus.Running && CurrentTick - p.StartedTick >= PreparedEditionDurationTicks && transitionAtStart)
        {
            if (_immersion is not null)
            {
                _preparation = p with { Status = PreparationStatus.Departing };
                Phase = SessionPhase.Egress;
                StartImmersionDeparture();
                return;
            }
            FinishProgrammeMedicalNeedsForDeparture();
            for (var index = 0; index < people.Length; index++)
                if (!people[index].Departed)
                    ApplyAgentDestination(new(people[index].AgentId), new(PreparedStart(index), "edition.departure"));
            FinishStaffResponsesForDeparture();
            _preparation = p with { Status = PreparationStatus.Departing };
            Phase = SessionPhase.Egress;
        }
        else if (p.Status == PreparationStatus.Departing && transitionAtStart)
        {
            _preparation = p with { Status = PreparationStatus.Finished, Rentals = [], WorkContracts = [] };
            if (p.CommunityShareAttempt == p.Attempt && !p.CommunityFavourClaimed && _lifecycle is { } lifecycle)
            {
                var claimId = $"community-water-favour:{CampaignId.Value}";
                lifecycle.FixtureFavourBalance++;
                lifecycle.CompletedOutcomeTransactionIds.Add(claimId);
                _preparation = _preparation with { CommunityFavourClaimed = true };
            }
        }
    }

    // Persistence probes only; no normal UI or lethal chain calls these hooks.
    public void SettlePreparationFailureFixture()
    {
        if (_preparation is not { FixtureOutcomesEnabled: true, Status: PreparationStatus.Running } p)
            throw new InvalidOperationException("Only a running persistence fixture can inject failure.");
        _preparation = p with { Status = PreparationStatus.Failed, Rentals = [], WorkContracts = [] };
        ReleaseInterventionsForBoundary("Labelled failure fixture released active interventions");
        FinishLivePerformance();
    }

    public void RetryPreparationFixture()
    {
        if (_preparation is not { FixtureOutcomesEnabled: true, Status: PreparationStatus.Failed } p)
            throw new InvalidOperationException("Only a failed persistence fixture can create a retry.");
        _navigationAgents.Clear();
        _livePerformance = null;
        _preparation = p with { Attempt = p.Attempt + 1, Status = PreparationStatus.Preparing, AcceptedOffers = [], StartedTick = 0,
            People = p.People.Select(item => item with { Admitted = false, Departed = false, Satisfaction = 5_000, MusicRisk = 0 }).ToArray() };
        Phase = SessionPhase.OpeningCheck;
    }

    private void RetryPreparedWeekend()
    {
        var p = _preparation!;
        var baseline = _immersion is not null ? CreateImmersionCampaign(CampaignSeed) : _programme is not null ? CreateTimetableCampaign(CampaignSeed) : _disorder is not null ? CreateDisorderCampaign(CampaignSeed, p.Tier) :
            _medical is not null ? CreateMedicalCampaign(CampaignSeed, p.Tier) : CreateEquipmentCampaign(CampaignSeed, p.Tier);
        _festivalFinances[new(p.FinanceOwnerId)].CashPennies = p.OpeningCashPennies;
        _ownedStocks[new(p.StockId)].Quantity = 40;
        _navigationAgents.Clear();
        _livePerformance = null;
        _programme = baseline._programme;
        if (_immersion is not null)
        {
            _immersion = baseline._immersion! with { Vendors = _immersion.Vendors.Select(v => v with { Queue = [], OwnerId = null, ServiceTicks = 0,QueueCells=v.QueueCells is null?null:[ImmersionServiceCell(v)] }).ToArray() };
            foreach (var person in _immersion.People) _wallets[new(person.AgentId)].CashPennies = person.OpeningBudgetPennies;
        }
        _equipment = baseline._equipment;
        _medical = baseline._medical;
        if (_medical is not null)
            _medical = _medical with { MainWaterCell = p.PrimaryWaterCell, MainWaterQuarterTurns = p.PrimaryWaterQuarterTurns, MainWaterGeometryVersion = p.PrimaryWaterGeometryVersion,
                ExtraWaterPoints = EffectiveWaterPlacements(p).Select(site =>
                    new WaterPointState(site.Id, site.Cell, [], [], null, 0) { QuarterTurns = site.QuarterTurns, GeometryVersion = site.GeometryVersion }).ToArray() };
        _disorder = baseline._disorder;
        _traversalGrid = null;
        _preparation = p with
        {
            Attempt = p.Attempt + 1, Status = PreparationStatus.Preparing, AcceptedOffers = [],
            Rentals = [], WorkContracts = [], StartedTick = 0, StockConsumed = 0,
            People = baseline._preparation!.People
        };
        Phase = SessionPhase.OpeningCheck;
    }

    private static string? ValidatePersistedPreparation(PreparationSnapshot? p, SessionPersistenceSnapshot snapshot)
    {
        if (p is null) return null;
        if (p.Version != 1 || p.Tier is < 1 or > 2 || p.Attempt < 1 || !Enum.IsDefined(p.Status) || p.StartedTick < 0 || p.StartedTick > snapshot.CurrentTick ||
            p.OfferSeed != (snapshot.CampaignSeed ^ ((ulong)p.Tier * 0x9E3779B97F4A7C15UL)) || p.OpeningCashPennies != CampaignDefaults.OpeningCashPennies || p.StockConsumed < 0 ||
            p.People is null || p.People.Any(item => item is null) || p.Payments is null || p.Payments.Any(item => item is null) ||
            p.OwnedEquipment is null || p.Rentals is null || p.Contacts is null || p.WorkContracts is null || p.AcceptedOffers is null ||
            p.ExtraWaterSiteIds is null || p.WaterPlacements is null || p.StaffProfiles is null ||
            p.StaffProfiles.Any(item => item is null) || p.StaffProfiles.Length > 2 ||
            !p.StaffProfiles.Select(item => item.AgentId).SequenceEqual(p.StaffProfiles.Select(item => item.AgentId).Distinct().Order()) ||
            p.StaffProfiles.Select(item => item.Role).Distinct().Count() != p.StaffProfiles.Length ||
            (p.ExtraMedicSlotOwned || p.ExtraStewardSlotOwned || p.RespondersUpgraded || p.StaffProfiles.Length > 0) && snapshot.Disorder is null ||
            !p.ExtraWaterSiteIds.SequenceEqual(p.ExtraWaterSiteIds.Distinct().Order(StringComparer.Ordinal)) ||
            p.ExtraWaterSiteIds.Length > 2 ||
            p.ExtraWaterSiteIds.Any(id => !ExtraWaterSites.Any(site => site.Id == id) && id is not ("water.extra-1" or "water.extra-2")) ||
            p.WaterPlacements.Any(item => item is null) ||
            !p.WaterPlacements.Select(item => item.Id).SequenceEqual(p.WaterPlacements.Select(item => item.Id).Distinct().Order(StringComparer.Ordinal)) ||
            p.PrimaryWaterQuarterTurns is < 0 or > 3 ||
            p.PrimaryWaterGeometryVersion is < 0 or > 1 ||
            p.WaterPlacements.Any(item => !p.ExtraWaterSiteIds.Contains(item.Id) || item.QuarterTurns is < 0 or > 3 || item.GeometryVersion is < 0 or > 1) ||
            p.ExtraWaterSiteIds.Any(id => id.StartsWith("water.extra-", StringComparison.Ordinal) &&
                !p.WaterPlacements.Any(item => item.Id == id)) ||
            (p.WaterTowerOwned || p.ExtraWaterSiteIds.Length > 0 || p.PrimaryWaterCell != MedicalWaterCell) && snapshot.Medical is null ||
            p.CommunityShareAttempt < 0 || p.CommunityShareAttempt > p.Attempt || p.CommunityShareAttempt > 0 && snapshot.Medical is null ||
            p.CommunityFavourClaimed != (p.CommunityShareAttempt == p.Attempt && p.Status == PreparationStatus.Finished) ||
            p.RetryEconomyFixtureEnabled && (p.FixtureOutcomesEnabled || snapshot.Equipment is null || snapshot.Medical is not null || snapshot.Disorder is not null) ||
            p.MaintenanceWorkerId is { } workerId && (workerId == 0 || workerId >= snapshot.NextEntityId ||
                !snapshot.Wallets.Any(item => item.OwnerId == workerId)))
            return "Preparation header or collections invalid.";
        if ((p.PrimaryWaterCell != MedicalWaterCell || p.PrimaryWaterQuarterTurns != 0) && ValidateWaterPlacementCell(p.PrimaryWaterCell,
                p with { ExtraWaterSiteIds = [], WaterPlacements = [], PrimaryWaterCell = MedicalWaterCell }, "water.main",
                snapshot.Equipment, p.PrimaryWaterQuarterTurns, p.PrimaryWaterGeometryVersion) is not null)
            return "Primary water position is invalid.";
        var preceding = p with { ExtraWaterSiteIds = [], WaterPlacements = [] };
        if(WaterOverlapsImmersion(snapshot.Immersion,p.PrimaryWaterCell,p.PrimaryWaterQuarterTurns,p.PrimaryWaterGeometryVersion)||EffectiveWaterPlacements(p).Any(w=>WaterOverlapsImmersion(snapshot.Immersion,w.Cell,w.QuarterTurns,w.GeometryVersion)))return "Saved water overlaps immersion vendor or queue.";
        foreach (var placement in EffectiveWaterPlacements(p))
        {
            if (placement.Id.StartsWith("water.extra-", StringComparison.Ordinal) &&
                ValidateWaterPlacementCell(placement.Cell, preceding, null, snapshot.Equipment, placement.QuarterTurns, placement.GeometryVersion) is not null)
                return "Placed water point or queue is invalid.";
            preceding = preceding with { ExtraWaterSiteIds = preceding.ExtraWaterSiteIds.Append(placement.Id).Order(StringComparer.Ordinal).ToArray(),
                WaterPlacements = preceding.WaterPlacements.Append(placement).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray() };
        }
        var maintenance = snapshot.Equipment?.WorkerId is not null ? 1 : 0;
        var medic = snapshot.Medical is null ? 0 : 1;
        var security = snapshot.Disorder is null ? 0 : 1;
        var extras = p.AcceptedOffers.Count(id => id is "staff.extra-medic" or "staff.extra-steward");
        var performerCount = snapshot.Programme is null ? 3 : 9;
        if (p.People.Length > 50 || p.People.Length != p.Tier * 20 + 1 + performerCount + maintenance + medic + security + extras || p.People.Count(item => item.Role == ProtectedPersonRole.Guest) != p.Tier * 20 ||
            p.People.Count(item => item.Role == ProtectedPersonRole.Staff) != 1 + maintenance + medic + security + extras || p.People.Count(item => item.Role == ProtectedPersonRole.Performer) != performerCount ||
            p.People.Any(item => item.AgentId == 0 || item.AgentId >= snapshot.NextEntityId || string.IsNullOrWhiteSpace(item.Name) || item.ExpectedGenre < 0 || item.ExpectedGenre > (snapshot.Programme is null ? 1 : 3) ||
                item.Satisfaction is < 0 or > 10_000 || item.MusicRisk is < 0 or > 3_000 || item.Departed && !item.Admitted) ||
            p.People.Select(item => item.AgentId).Distinct().Count() != p.People.Length)
            return "Fixed protected roster invalid.";
        var factory = snapshot.Programme is not null ? CreateTimetableCampaign(snapshot.CampaignSeed) : snapshot.Disorder is not null ? CreateDisorderCampaign(snapshot.CampaignSeed, p.Tier) :
            snapshot.Medical is not null ? CreateMedicalCampaign(snapshot.CampaignSeed, p.Tier) :
            snapshot.Equipment is null ? CreatePreparedCampaign(snapshot.CampaignSeed, p.Tier) : CreateEquipmentCampaign(snapshot.CampaignSeed, p.Tier);
        var offers = factory.GetPreparationOffers().ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var list in new[] { p.OwnedEquipment, p.Rentals, p.Contacts, p.WorkContracts, p.AcceptedOffers })
            if (list.Any(string.IsNullOrWhiteSpace) || !list.SequenceEqual(list.Distinct().Order(StringComparer.Ordinal))) return "Preparation collections must be sorted and unique.";
        if (p.OwnedEquipment.Any(id => id != "sound-rig") || p.Rentals.Any(id => id != "sound-rig") ||
            p.Contacts.Any(id => id != "contact.casey-vale" && (snapshot.Equipment is null || id != "contact.morgan-finch") &&
                (snapshot.Disorder is null || id is not ("contact.avery-brooks" or "contact.sam-ellis"))) ||
            p.WorkContracts.Any(id => !offers.TryGetValue(id, out var offer) || offer.Category is not ("staff" or "maintenance" or "extra-medic" or "extra-steward")) || p.AcceptedOffers.Any(id => !offers.ContainsKey(id)) ||
            p.AcceptedOffers.Select(id => offers[id].Category == "act" && snapshot.Programme is not null ? id : offers[id].Category).Distinct().Count() != p.AcceptedOffers.Length)
            return "Preparation entitlement invalid.";
        if (p.Payments.Where((item, index) => item.Id != index + 1 || item.Attempt < 1 || item.Attempt > p.Attempt || item.Tick < 0 || item.Tick > snapshot.CurrentTick ||
            string.IsNullOrWhiteSpace(item.OfferId) || !offers.TryGetValue(item.OfferId, out var offer) || item.AmountPennies != offer.PricePennies ||
            item.DebitAccount != (item.OfferId == "equipment.buy" ? LedgerAccountType.EquipmentAsset :
                item.OfferId == "contract.stock" ? LedgerAccountType.InventoryAsset : LedgerAccountType.AdministrationExpense)).Any() ||
            p.Payments.Select(item => (item.Attempt, Category: offers[item.OfferId].Category == "act" && snapshot.Programme is not null ? item.OfferId : offers[item.OfferId].Category)).Distinct().Count() != p.Payments.Length ||
            !p.AcceptedOffers.SequenceEqual(p.Payments.Where(item => item.Attempt == p.Attempt).Select(item => item.OfferId).Order(StringComparer.Ordinal)))
            return "Preparation commitments do not reconcile.";
        var settled = p.Status is PreparationStatus.Failed or PreparationStatus.Finished;
        if ((p.OwnedEquipment.Length == 1) != p.Payments.Any(item => item.OfferId == "equipment.buy") ||
            (p.Rentals.Length == 1) != (!settled && p.AcceptedOffers.Contains("equipment.rent")) ||
            p.Contacts.Contains("contact.casey-vale") != p.Payments.Any(item => offers[item.OfferId].Category == "staff") ||
            p.Contacts.Contains("contact.morgan-finch") != p.Payments.Any(item => offers[item.OfferId].Category == "maintenance") ||
            !p.WorkContracts.SequenceEqual(settled ? [] : p.AcceptedOffers.Where(id => offers[id].Category is "staff" or "maintenance" or "extra-medic" or "extra-steward")) ||
            p.OwnedEquipment.Length + p.Rentals.Length > 1 ||
            p.Payments.Count(item => item.OfferId == "equipment.buy") > 1 ||
            (p.MaintenanceWorkerId is not null) != p.Payments.Any(item => item.OfferId == "maintenance.worker") ||
            snapshot.Equipment?.WorkerId is { } activeWorker && activeWorker != p.MaintenanceWorkerId)
            return "Preparation property or contracts lack matching paid commitments.";
        if (p.Status == PreparationStatus.Preparing && (snapshot.Phase != (int)SessionPhase.OpeningCheck || (snapshot.NavigationAgents?.Length ?? 0) != 0 || p.People.Any(item => item.Admitted || item.Departed)) ||
            (p.Status == PreparationStatus.Running || p.Status == PreparationStatus.Failed && snapshot.Immersion is null) && snapshot.Phase != (int)SessionPhase.Live ||
            p.Status == PreparationStatus.Failed && snapshot.Immersion is not null && (snapshot.Phase is not ((int)SessionPhase.Live) and not ((int)SessionPhase.Egress) || snapshot.Phase == (int)SessionPhase.Egress && snapshot.CurrentTick < p.StartedTick + PreparedDayTicks) ||
            p.Status is PreparationStatus.Departing or PreparationStatus.Finished && snapshot.Phase != (int)SessionPhase.Egress ||
            p.Status == PreparationStatus.Failed && !p.FixtureOutcomesEnabled && snapshot.Equipment?.Stage != EquipmentStage.Terminal && snapshot.Medical?.Stage != MedicalStage.Terminal && snapshot.Disorder?.Evidence.LastOrDefault()?.Id != "disorder:death" ||
            p.Status != PreparationStatus.Preparing && (!p.AcceptedOffers.Any(id => offers[id].Category == "act") || !p.AcceptedOffers.Any(id => offers[id].Category == "staff")) ||
            p.Status == PreparationStatus.Finished && p.People.Any(item => !item.Departed))
            return "Preparation phase and protected-person progress disagree.";
        var originalPeople = factory.CapturePreparation()!.People;
        if (maintenance == 1) originalPeople = originalPeople.Append(new EditionPerson(p.MaintenanceWorkerId!.Value, "Morgan Finch", ProtectedPersonRole.Staff, 0)).ToArray();
        originalPeople = originalPeople.Concat(p.StaffProfiles.Where(profile => p.AcceptedOffers.Contains(profile.Role == ResponseRole.Medic ? "staff.extra-medic" : "staff.extra-steward"))
            .Select(profile => new EditionPerson(profile.AgentId, profile.Name, ProtectedPersonRole.Staff, 0))).OrderBy(item => item.AgentId).ToArray();
        if (snapshot.CampaignPlanning is null || snapshot.Lifecycle is not null && snapshot.Equipment is null ||
            p.MaintenanceWorkerId is { } retainedWorkerId && retainedWorkerId < factory.NextEntityId ||
            p.StaffProfiles.Any(profile => !Enum.IsDefined(profile.Role) || profile.AgentId < factory.NextEntityId || profile.AgentId >= snapshot.NextEntityId ||
                profile.AgentId == p.MaintenanceWorkerId || !snapshot.Wallets.Any(item => item.OwnerId == profile.AgentId) ||
                profile != CreateOptionalStaff(snapshot.CampaignSeed, profile.AgentId, profile.Role) ||
                !(profile.Role == ResponseRole.Medic ? p.ExtraMedicSlotOwned : p.ExtraStewardSlotOwned)) ||
            p.Contacts.Contains("contact.avery-brooks") != p.Payments.Any(item => item.OfferId == "staff.extra-medic") ||
            p.Contacts.Contains("contact.sam-ellis") != p.Payments.Any(item => item.OfferId == "staff.extra-steward") ||
            p.Payments.Any(item => item.OfferId == "staff.extra-medic") != p.StaffProfiles.Any(item => item.Role == ResponseRole.Medic) ||
            p.Payments.Any(item => item.OfferId == "staff.extra-steward") != p.StaffProfiles.Any(item => item.Role == ResponseRole.Steward) ||
            p.People.Where((person, index) => person.AgentId != originalPeople[index].AgentId || person.Name != originalPeople[index].Name ||
                person.Role != originalPeople[index].Role || person.ExpectedGenre != originalPeople[index].ExpectedGenre).Any() ||
            p.People.Any(person => !snapshot.Wallets.Any(wallet => wallet.OwnerId == person.AgentId)))
            return "Preparation farm identity or stable roster identity invalid.";
        var finance = snapshot.FestivalFinances.SingleOrDefault(item => item.OwnerId == p.FinanceOwnerId);
        var stock = snapshot.OwnedStocks.SingleOrDefault(item => item.ServiceId == p.StockId);
        if (finance is null || stock is null || stock.OwnerId != p.FinanceOwnerId || stock.UnitCostBasisPennies != 60 ||
            finance.CashPennies != p.OpeningCashPennies - p.Payments.Where(item => p.FixtureOutcomesEnabled || item.Attempt == p.Attempt).Sum(item => (long)item.AmountPennies) - (snapshot.Immersion?.StockPurchased == true ? 9600 : 0) + (snapshot.Immersion?.Purchases?.Sum(item => (long)item.PricePennies) ?? 0) ||
            stock.Quantity != 40 + 50 * p.Payments.Count(item => item.OfferId == "contract.stock" && (p.FixtureOutcomesEnabled || item.Attempt == p.Attempt)) - p.StockConsumed)
            return "Preparation cash or stock does not reconcile.";
        if (p.Status != PreparationStatus.Preparing &&
            !(snapshot.NavigationAgents ?? []).Select(item => item.Id).SequenceEqual(p.People.Select(item => item.AgentId)))
            return "Every protected person must have one physical agent.";
        if (p.Status is PreparationStatus.Finished or PreparationStatus.Failed && (p.Rentals.Length != 0 || p.WorkContracts.Length != 0))
            return "Edition contracts must expire at settlement.";
        var staffIssue = ValidatePersistedStaffResponses(snapshot);
        if (staffIssue is not null) return staffIssue;
        var interventionIssue = ValidatePersistedInterventions(snapshot);
        if (interventionIssue is not null) return interventionIssue;
        return null;
    }
}
