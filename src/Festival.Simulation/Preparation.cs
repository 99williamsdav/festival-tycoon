using System.Text.Json;

namespace Festival.Simulation;

public enum PreparationStatus { Preparing, Running, Departing, Finished, Failed }
public enum PreparationStartOwner { Programme, Staff, Overview }
public sealed record PreparationStartBlocker(PreparationStartOwner Owner, string Message);
public sealed record PreparationStartRequirement(string Id, PreparationStartOwner Owner, string Label,
    bool Complete, string Detail);
public sealed record PreparationOffer(string Id, string Category, string Name, int PricePennies, int MusicQuality, int Genre);
public sealed record EditionPerson(ulong AgentId, string Name, ProtectedPersonRole Role, int ExpectedGenre,
    bool Admitted = false, bool Departed = false, int Satisfaction = 5_000, int MusicRisk = 0);
public sealed record PreparationPayment(int Id, string OfferId, int Attempt, long Tick, int AmountPennies,
    LedgerAccountType DebitAccount);
public sealed record PreparationInventoryBalance(int OpeningUnits, int PurchasedUnits, int ConsumedUnits, int RemainingUnits, int UnitCostPennies);
public sealed record WaterPlacement(string Id, GridCell Cell) { public int QuarterTurns { get; init; } public int GeometryVersion { get; init; } }
public sealed record PreparationSnapshot(int Version, int Tier, ulong OfferSeed, int Attempt, PreparationStatus Status,
    ulong FinanceOwnerId, ulong StockId, long StartedTick,
    string[] OwnedEquipment, string[] Rentals, string[] Contacts, string[] WorkContracts, string[] AcceptedOffers,
    EditionPerson[] People, PreparationPayment[] Payments, int StockConsumed, long OpeningCashPennies)
{
    public int CommunityShareAttempt { get; init; }
    public bool CommunityFavourClaimed { get; init; }
    public ulong? MaintenanceWorkerId { get; init; }
    public bool WaterTowerOwned { get; init; }
    public bool ExtraMedicSlotOwned { get; init; }
    public bool ExtraStewardSlotOwned { get; init; }
    public bool RespondersUpgraded { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BuildPlacement[] BuildPlacements { get; init; } = null!;
    public StaffProfile[] StaffProfiles { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PreparationPlan? Plan { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PreparationSetupPayment[]? SetupPayments { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? FinishedBeerIds { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? GuestMedicalCollapses { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FestivalResult? Result { get; init; }
}
public sealed record AcceptPreparationOfferCommand(string OfferId) : SessionCommand;
public sealed record StartPreparedEditionCommand : SessionCommand;
public sealed record CommitCommunityWaterShareCommand : SessionCommand;

public sealed partial class GameSession
{
    // 160 festival minutes = eight live real minutes at the unchanged 80 ticks/s;
    // preparation and pauses target the remaining two minutes, pending playtesting.
    public const int PreparedWeekendTicks = 38_400;
    public const int PreparedDayTicks = 38_400;
    public int PreparedEditionDurationTicks => PreparedDayTicks;

    public PreparationStatus? PreparedStatus => _preparation?.Status;
    /// <summary>The current read model. Snapshots are shared immutable values: never write into their arrays.</summary>
    public PreparationSnapshot? CapturePreparation() => PreparationView;
    internal string? PreparationCanonicalJson => PreparationView is not { } p ? null : System.Text.Json.JsonSerializer.Serialize(p);
    public bool PreparationBoundaryOnNextTick => !IsPaused && _preparation is { } p &&
        (p.Status == PreparationStatus.Running && CurrentTick - p.StartedTick >= PreparedEditionDurationTicks - 1 && PeopleIn(PersonView.Roster).All(item => item.Admitted) ||
         p.Status == PreparationStatus.Departing && PeopleIn(PersonView.Roster).All(item => item.Departed));

    public PreparationInventoryBalance? GetPreparationInventoryBalance() => _preparation is not { } p ? null :
        new(40, p.Payments.Count(item => item.Attempt == p.Attempt && item.OfferId == "contract.stock") * 50,
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



    public string? WaterTapAdditionUnavailableReason => _medical is null || _preparation is not { Status: PreparationStatus.Preparing } p
        ? "Taps can only be added during preparation."
        : p.BuildPlacements.Count(item => item.Kind == BuildServiceKind.WaterTap) >= BuildServiceLimit(BuildServiceKind.WaterTap)
            ? "Both paid tap slots are placed; select one to move or remove it." : null;

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
        if (movingId != "water.main") others.Insert(0, new WaterPointState("water.main", PrimaryWaterCell(p), [], [], null, 0) { QuarterTurns = PrimaryWaterQuarterTurns(p), GeometryVersion = PrimaryWaterGeometryVersion(p) });
        var proposed = new WaterPointState(movingId ?? "water.proposed", cell, [], [], null, 0) { QuarterTurns = quarterTurns, GeometryVersion = geometryVersion };
        var occupied = new HashSet<GridCell>();
        static void Footprint(HashSet<GridCell> cells, GridCell centre, int radius)
        {
            for (var z = centre.Z - radius; z <= centre.Z + radius; z++)
            for (var x = centre.X - radius; x <= centre.X + radius; x++) cells.Add(new GridCell(x, z));
        }
        foreach (var point in others) Footprint(occupied, point.Cell, WaterFootprintRadius(point) + 1);
        Footprint(occupied, ResponsePost(p, ResponseRole.Medic).Cell, 4);
        Footprint(occupied, MedicalRestCell, 1);
        Footprint(occupied, ResponsePostHome(p, ResponseRole.Medic), 1);
        foreach(var postCell in ResponsePostReserved(p)) occupied.Add(postCell);
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
            new("staff.steward", "staff", "Casey: standard sound engineer • +400 quality", 2_000, 400, -1),
            new("staff.engineer", "staff", "Casey: better sound engineer • +800 quality", 4_000, 800, -1),
            new("equipment.buy", "equipment", "Buy sound rig • +1000 quality; retained", 12_000, 1_000, -1),
            new("equipment.rent", "equipment", "Rent sound rig • +500 quality; this weekend", 3_000, 500, -1),
            new("contract.stock", "contract", "50 refreshments • unused stock resets on retry", 3_000, 0, -1)
        ];
        if (_equipment is not null) offers = offers.Append(new PreparationOffer("maintenance.worker", "maintenance", "Morgan: maintenance worker • physical repair", 1_500, 0, -1)).ToArray();
        if (_disorder is not null) offers = offers.Concat(new[] {
            new PreparationOffer("staff.extra-medic", "extra-medic", "Avery Brooks: extra medic • paid weekend contract", 3000, 0, -1),
            new PreparationOffer("staff.extra-steward", "extra-steward", "Sam Ellis: extra steward • paid weekend contract", 3000, 0, -1) }).ToArray();
        offers = offers.Where(offer => offer.Category != "act").Concat(FestivalActs.Select(act => new PreparationOffer(act.Id, "act", act.Name, act.PricePennies, 1000, act.Genre))).ToArray();
        return offers;
    }

    // Derived presentation read model; these are the exact local Start conditions,
    // not extra safety recommendations or global command-envelope/perk guards.
    public IReadOnlyList<PreparationStartRequirement> GetPreparationStartRequirements()
    {
        if (_preparation is not { Status: PreparationStatus.Preparing } p) return [];
        var requirements = new List<PreparationStartRequirement>(7);
        {
            requirements.Add(new("water", PreparationStartOwner.Overview, "Water tap",
                p.BuildPlacements.Any(item => item.Kind == BuildServiceKind.WaterTap), "Place a water tap before opening."));
            requirements.Add(new("toilet", PreparationStartOwner.Overview, "Toilet",
                p.BuildPlacements.Any(item => item.Kind == BuildServiceKind.Toilet), "Place a toilet before opening."));
            requirements.Add(new("first-aid", PreparationStartOwner.Overview, "First aid",
                p.BuildPlacements.Any(item => item.Kind == BuildServiceKind.FirstAid), "Place first aid before opening."));
            requirements.Add(new("steward-post", PreparationStartOwner.Overview, "Steward post",
                p.BuildPlacements.Any(item => item.Kind == BuildServiceKind.StewardPost), "Place a steward post before opening."));
        }
        requirements.Add(new("programme", PreparationStartOwner.Programme,
            "Three acts booked", (p.Plan?.ActIds ?? _programme!.ActIds).Count(id => id != "") == 3,
            "Choose three different acts before opening. Each selection saves immediately."));
        requirements.Add(new("staff", PreparationStartOwner.Staff, "Worker hired",
            (p.Plan?.OfferIds ?? p.WorkContracts).Any(id => id.StartsWith("staff.", StringComparison.Ordinal)),
            "Hire one worker from the Staff tab before opening."));
            requirements.Add(new("budget", PreparationStartOwner.Overview, "Setup within budget",
                p.Plan is not { Committed: false } || PreparationRemainingCash >= 0,
                "Setup exceeds available funds. Remove or revise planned purchases."));
        return requirements;
    }

    public IReadOnlyList<PreparationStartBlocker> GetPreparationStartBlockers() =>
        GetPreparationStartRequirements().Where(requirement => !requirement.Complete)
            .Select(requirement => new PreparationStartBlocker(requirement.Owner, requirement.Detail)).ToArray();

    private CommandResult? ValidatePreparationCommand(EntityId? target, SessionCommand command)
    {
        if (_preparation is not { Status: PreparationStatus.Preparing } p || target is not null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Available only during edition preparation.");
        var offers = GetPreparationOffers();
        if (command is AcceptPreparationOfferCommand accept)
        {
            if (accept.OfferId=="contract.stock") return CommandResult.Rejected(CommandReasonCode.InvalidParameter,"Buy physical food/drink starter stock instead.");
            var offer = offers.SingleOrDefault(item => item.Id == accept.OfferId);
            if (offer is null) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown preparation offer.");
            if (offer.Category == "act") return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Confirm all three acts together through the programme.");
            if (offer.Category is "extra-medic" or "extra-steward" &&
                (!(offer.Category == "extra-medic" ? p.ExtraMedicSlotOwned : p.ExtraStewardSlotOwned) || PeopleIn(PersonView.Roster).Length >= 50))
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Requires the matching role slot and room below 50 active people.");
            if (p.Plan is null && p.AcceptedOffers.Any(id => offers.Single(item => item.Id == id).Category == offer.Category) ||
                offer.Category == "equipment" && p.OwnedEquipment.Contains("sound-rig"))
                return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "This category is already supplied for the edition.");
            if (p.Plan is null && _festivalFinances[new(p.FinanceOwnerId)].CashPennies < offer.PricePennies)
                return CommandResult.Rejected(CommandReasonCode.InsufficientFunds, "Insufficient cash for this commitment.");
        }
        else if (GetPreparationStartBlockers().Count != 0)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Book one act and one worker for the fixed protected roster.");
        return null;
    }

    private void ApplyPreparationOffer(AcceptPreparationOfferCommand command)
    {
        var p = _preparation!;
        var offer = GetPreparationOffers().Single(item => item.Id == command.OfferId);
        if (p.Plan is { Committed: false } plan && !_committingPreparationPlan)
        {
            _preparation = p with { Plan = plan with { OfferIds = plan.OfferIds.Where(id => GetPreparationOffers().Single(o => o.Id == id).Category != offer.Category).Append(offer.Id).Order(StringComparer.Ordinal).ToArray() } };
            return;
        }
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
            PreparationView = PreparationView! with { MaintenanceWorkerId = id,
                People = PreparationView.People.Append(new EditionPerson(id, "Morgan Finch", ProtectedPersonRole.Staff, 0)).OrderBy(item => item.AgentId).ToArray() };
            _equipment = _equipment! with { WorkerId = id };
        }
    }

    private void ApplyStartPreparedEdition()
    {
        CommitPreparationPlan();
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
            foreach (var (centre, radius) in new[] { (PrimaryWaterCell(p), PrimaryWaterGeometryVersion(p) == 1 ? 1 : 3), (ResponsePost(p,ResponseRole.Medic).Cell, 3) }
                         .Concat(Taps.Where(point => point.Id != "water.main").Select(point => (point.Cell, WaterFootprintRadius(point)))))
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
        if(StewardPostPlacement(p) is { } steward)
        {
            var terrain=_traversalGrid.Overrides.ToDictionary(item=>item.Key,item=>item.Value);
            foreach(var cell in ResponsePostFootprint(steward,ResponseRole.Steward))terrain[cell]=new(cell,GroundSurface.Grass,false);
            _traversalGrid=new TraversalGrid(terrain.Values);
        }
        BlockImmersionVendors();
        BlockToilet();
        for (var index = 0; index < PeopleIn(PersonView.Roster).Length; index++)
        {
            var person = PeopleIn(PersonView.Roster)[index];
            var id = new EntityId(person.Id);
            var position = TraversalGrid.CellCentre(PreparedStart(index));
            _navigationAgents.Add(id, new NavigationAgentState
            {
                Id = id, XMillimetres = position.XMillimetres, ZMillimetres = position.ZMillimetres,
                SegmentOriginXMillimetres = position.XMillimetres, SegmentOriginZMillimetres = position.ZMillimetres,
                WalkingSpeedPermille = GetResponseStaff().SingleOrDefault(item => item.AgentId == id.Value)?.WalkingSpeedPermille ?? GetWalkingSpeedPermille(id), Action = AgentNavigationAction.Idle
            });
            var profile = GetResponseStaff().SingleOrDefault(item => item.AgentId == person.Id);
            var dutyCell = StaffAssignedPost(person.Id) ?? PreparedPlace(index);
            if (person.Role != ProtectedPersonRole.Guest || GuestReleaseTick(CampaignSeed, person.Id) == 0)
                ApplyAgentDestination(id, new(dutyCell, "edition.arrival"));
        }
        _preparation = p with { Status = PreparationStatus.Running, StartedTick = CurrentTick };
        Phase = SessionPhase.Live;
        StartEquipmentLifecycle();
        StartLivePerformance();
    }

    private static GridCell PreparedStart(int index) => new(122 + index % 6 * 2, 190 + index / 6 * 2);
    // Physical presence includes collapsed guests until their recorded departure.
    public int OnSiteAttendeeCount => PeopleIn(PersonView.Roster).Count(person => person.Role == ProtectedPersonRole.Guest && person.Admitted && !person.Departed);
    private static GridCell PreparedPlace(int index) => new(122 + index % 6 * 2, 156 + index / 6 * 2);

    private void AdvancePreparation()
    {
        if (_preparation is not { Status: PreparationStatus.Running or PreparationStatus.Departing } p) return;
        // Boundary eligibility reads the start state, so persistence can stage exactly the
        // boundary tick without cloning every ordinary movement tick. Final physical arrival
        // is committed first; the following tick commits settlement and contract expiry.
        var transitionAtStart = PreparationBoundaryOnNextTick;
        var people = PeopleIn(PersonView.Roster).ToArray();
        for (var index = 0; index < people.Length; index++)
        {
            var person = people[index];
            var agent = _navigationAgents[new(person.Id)];
            if (p.Status == PreparationStatus.Running && person.Role == ProtectedPersonRole.Guest &&
                !person.Admitted && agent.Destination is null &&
                CurrentTick - p.StartedTick >= GuestReleaseTick(CampaignSeed, person.Id))
                ApplyAgentDestination(new(person.Id), new(PreparedPlace(index), "edition.arrival"));
            if (agent.Action != AgentNavigationAction.Arrived) continue;
            if (p.Status == PreparationStatus.Running && !person.Admitted)
            {
                var satisfaction = Math.Clamp(5_000 + AdmissionLineupAdjustment(person), 0, 10_000);
                people[index] = person with { Admitted = true, Satisfaction = satisfaction, MusicRisk = 0 };
            }
        }
        foreach (var person in people) SetPresence(person);
        if (p.Status == PreparationStatus.Running && CurrentTick - p.StartedTick >= PreparedEditionDurationTicks && transitionAtStart)
        {
            _preparation = p with { Status = PreparationStatus.Departing };
            Phase = SessionPhase.Egress;
            StartImmersionDeparture();
        }
    }

    private void RetryPreparedWeekend()
    {
        var p = _preparation!;
        var baseline = CreateFoodAndDrinkBaseline(CampaignSeed);
        _festivalFinances[new(p.FinanceOwnerId)].CashPennies = p.OpeningCashPennies;
        _ownedStocks[new(p.StockId)].Quantity = 40;
        _navigationAgents.Clear();
        _livePerformance = null;
        _programme = baseline._programme;
        if (_immersion is not null)
        {
            SetVendors(Vendors.Select(v => v with { Queue = [], OwnerId = null, ServiceTicks = 0,QueueCells=v.QueueCells is null?null:[ImmersionServiceCell(v)] }).ToArray());
            SetToilets(PlacedToilets(p));
            ImmersionView = baseline.ImmersionView!;
            foreach (var person in PeopleIn(PersonView.Consumption)) _wallets[new(person.Id)].CashPennies = person.OpeningBudgetPennies;
        }
        _equipment = baseline._equipment;
        MedicalView = baseline.MedicalView;
        if (_medical is not null)
            SetTaps((MainTapStanding(p) ? new[] { OpeningMainTap() with { Cell = PrimaryWaterCell(p), QuarterTurns = PrimaryWaterQuarterTurns(p), GeometryVersion = PrimaryWaterGeometryVersion(p) } } : [])
                .Concat(EffectiveWaterPlacements(p).Select(site =>
                    new WaterPointState(site.Id, site.Cell, [], [], null, 0) { QuarterTurns = site.QuarterTurns, GeometryVersion = site.GeometryVersion })).ToArray());
        DisorderView = baseline.DisorderView;
        _traversalGrid = null;
        PreparationView = p with
        {
            Attempt = p.Attempt + 1, Status = PreparationStatus.Preparing, AcceptedOffers = [],
            FinishedBeerIds = [], GuestMedicalCollapses = 0, Result = null,
            Rentals = [], WorkContracts = [], StartedTick = 0, StockConsumed = 0,
            People = baseline.PreparationView!.People
            ,Plan = p.Plan! with { Committed = false }
        };
        Phase = SessionPhase.OpeningCheck;
        SyncBuildPhysicalLayout();
        ApplyBuildGuestOpeningNeeds();
        OpenPerkDraft();
    }

    private static string? ValidatePersistedPreparation(PreparationSnapshot? p, SessionPersistenceSnapshot snapshot)
    {
        if (p is null) return null;
        if ((p.BuildPlacements is null || p.Plan is null ||
            !p.BuildPlacements.Select(item => item.Id).SequenceEqual(p.BuildPlacements.Select(item => item.Id).Order(StringComparer.Ordinal)) ||
            ValidateBuildLayout(p.BuildPlacements, snapshot.Equipment, p.WaterTowerOwned) is not null))
            return "Saved build layout is invalid.";
        if (ValidateBuildFacilities(p, snapshot) is { } buildMirrorIssue) return buildMirrorIssue;
        if (p.Plan is null || snapshot.Programme is null || p.FinishedBeerIds is null)
            return "Lineup reaction identity requires the current saved programme and results plan.";
        if (p.Version is not (1 or 2) || (p.Version == 2) != (p.Plan is not null) || p.Tier is < 1 or > 2 || p.Attempt < 1 || !Enum.IsDefined(p.Status) || p.StartedTick < 0 || p.StartedTick > snapshot.CurrentTick ||
            p.OfferSeed != (snapshot.CampaignSeed ^ ((ulong)p.Tier * 0x9E3779B97F4A7C15UL)) || p.OpeningCashPennies != CampaignDefaults.OpeningCashPennies || p.StockConsumed < 0 ||
            p.People is null || p.People.Any(item => item is null) || p.Payments is null || p.Payments.Any(item => item is null) ||
            p.OwnedEquipment is null || p.Rentals is null || p.Contacts is null || p.WorkContracts is null || p.AcceptedOffers is null ||
            p.StaffProfiles is null ||
            p.StaffProfiles.Any(item => item is null) || p.StaffProfiles.Length > 2 ||
            !p.StaffProfiles.Select(item => item.AgentId).SequenceEqual(p.StaffProfiles.Select(item => item.AgentId).Distinct().Order()) ||
            p.StaffProfiles.Select(item => item.Role).Distinct().Count() != p.StaffProfiles.Length ||
            (p.ExtraMedicSlotOwned || p.ExtraStewardSlotOwned || p.RespondersUpgraded || p.StaffProfiles.Length > 0) && snapshot.Disorder is null ||
            (p.WaterTowerOwned || EffectiveWaterPlacements(p).Length > 0 || PrimaryWaterCell(p) != MedicalWaterCell) && snapshot.Medical is null ||
            p.CommunityShareAttempt < 0 || p.CommunityShareAttempt > p.Attempt || p.CommunityShareAttempt > 0 && snapshot.Medical is null ||
            p.CommunityFavourClaimed != (p.CommunityShareAttempt == p.Attempt && p.Status == PreparationStatus.Finished) ||
            p.MaintenanceWorkerId is { } workerId && (workerId == 0 || workerId >= snapshot.NextEntityId ||
                !snapshot.Wallets.Any(item => item.OwnerId == workerId)))
            return "Preparation header or collections invalid.";
        var planIssue = ValidatePersistedPlan(p, snapshot);
        if (planIssue is not null) return planIssue;
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
        var factory = CreateProgrammeBaseline(snapshot.CampaignSeed);
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
            p.Status == PreparationStatus.Failed && snapshot.Equipment?.Stage != EquipmentStage.Terminal && snapshot.Medical?.Stage != MedicalStage.Terminal && snapshot.Disorder?.Evidence.LastOrDefault()?.Id != "disorder:death" ||
            p.Status != PreparationStatus.Preparing && (!p.AcceptedOffers.Any(id => offers[id].Category == "act") || !p.AcceptedOffers.Any(id => id.StartsWith("staff.", StringComparison.Ordinal))) ||
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
                snapshot.Perks is null && !(profile.Role == ResponseRole.Medic ? p.ExtraMedicSlotOwned : p.ExtraStewardSlotOwned)) ||
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
            finance.CashPennies != p.OpeningCashPennies - p.Payments.Where(item => item.Attempt == p.Attempt).Sum(item => (long)item.AmountPennies) - (snapshot.Immersion?.StockPurchased == true ? p.Plan is null ? 9600 : PlannedStockCost(p.Plan) : 0) - (p.Plan?.Committed == true ? p.SetupPayments?.LastOrDefault()?.BuildCostPennies ?? 0 : 0) + (snapshot.Immersion?.Purchases?.Sum(item => (long)item.PricePennies) ?? 0) ||
            stock.Quantity != 40 + 50 * p.Payments.Count(item => item.OfferId == "contract.stock" && item.Attempt == p.Attempt) - p.StockConsumed)
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
