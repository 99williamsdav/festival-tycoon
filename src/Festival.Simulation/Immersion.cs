using System.Text.Json;

namespace Festival.Simulation;

/// <summary>Water is the bar's free cup when free water is on: no price, no stock, no pleasure, just thirst relief.</summary>
public enum ImmersionProduct { Chips, SoftDrink, Beer, Water, Pizza }

public static class ImmersionProducts
{
    /// <summary>Sold at a food van by its trader, rather than at the festival's own bar.</summary>
    public static bool IsFood(this ImmersionProduct product) => product is ImmersionProduct.Chips or ImmersionProduct.Pizza;
}
public sealed record ImmersionHeldItem(string TransactionId, ImmersionProduct Product, int ConsumedTicks);
public sealed record ImmersionPerson(ulong AgentId, int OpeningBudgetPennies, int Hunger, bool Abstains, int BeerTaste, int SoftTaste, int PriceReluctance,
    ImmersionHeldItem? Held = null, int Intoxication = 0, int PendingDose = 0, int FoodProtectionTicks = 0, long LastDecisionTick = -800,
    string? VendorId = null, ImmersionProduct? Order = null, long WarningTick = -1, int SevereTicks = 0)
{
    public int HungerResidue { get; init; }
    public int RecoveryResidue { get; init; }
    public int AbsorptionResidue { get; init; }
    public long CollapseTick { get; init; } = -1;
    public int CareTicks { get; init; }
    public MedicalStage PriorMedicalStage { get; init; }
    public int StaffThirst { get; init; } = 3000;
    public int ToiletNeed { get; init; }
    public int ToiletVisits { get; init; }
    public ToiletVisitStage ToiletStage { get; init; }
    public ToiletVisitKind? ToiletChoice { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ToiletId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? LastToiletChoiceReviewTick { get; init; }
}
public sealed record ImmersionVendor(string Id, GridCell Cell, int QuarterTurns, ulong[] Queue, ulong? OwnerId = null, int ServiceTicks = 0)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public GridCell[]? QueueCells { get; init; }
}
public sealed record ImmersionPurchase(string Id, ulong AgentId, ImmersionProduct Product, long Tick, int PricePennies, int CostPennies, LedgerEntry[] Entries);
public sealed record ImmersionStockPurchase(string Id,int Attempt,long Tick,LedgerEntry[] Entries);
public sealed record ImmersionSnapshot(int Version, bool StockPurchased, int SoftStock, int BeerStock,
    ImmersionPerson[] People, ImmersionPurchase[] Purchases)
{
    public ImmersionStockPurchase? StockPurchase { get; init; }
    /// <summary>Whether the bar is giving out free water now.</summary>
    public bool FreeWater { get; init; }
    /// <summary>When free water was switched on, each switch-on paid for at <see cref="GameSession.FreeWaterChargePennies"/>.</summary>
    public long[] FreeWaterChargeTicks { get; init; } = [];
}

public sealed partial class GameSession
{

    /// <summary>The current read model. Snapshots are shared immutable values: never write into their arrays.</summary>
    public ImmersionSnapshot? CaptureImmersion() => ImmersionView;
    internal string? ImmersionCanonicalJson => ImmersionView is not { } view ? null : JsonSerializer.Serialize(view);
    public static int ImmersionPrice(ImmersionProduct product) => product switch
    { ImmersionProduct.Water => 0, ImmersionProduct.SoftDrink => 250, ImmersionProduct.Beer => 400, ImmersionProduct.Chips or ImmersionProduct.Pizza => FoodTraders.Selling(product).PricePennies,
        _ => throw new ArgumentOutOfRangeException(nameof(product)) };
    public static int ImmersionPrice(ImmersionProduct product, bool beerFestival) =>
        beerFestival && product == ImmersionProduct.Beer ? ImmersionPrice(product) * BeerFestivalPricePercent / 100 : ImmersionPrice(product);
    // Whole-penny prices: round a half-penny down in the buyer's favour. A sneaky drinker buys beer as a punter, since
    // the bar would refuse them as staff, so pays full price for it.
    public static int ImmersionPrice(ImmersionProduct product, ProtectedPersonRole role, bool beerFestival = false, bool undercover = false) =>
        role is ProtectedPersonRole.Staff or ProtectedPersonRole.Performer && !(undercover && product == ImmersionProduct.Beer)
            ? ImmersionPrice(product, beerFestival) / 2 : ImmersionPrice(product, beerFestival);
    public int ImmersionPriceFor(ulong id, ImmersionProduct product) =>
        ImmersionPrice(product, _persons[id].Role, HasPerk(PerkCatalogue.BeerFestival), StaffHas(id, StaffTrait.SneakyAlcoholic));
    /// <summary>Whether someone has the money for one, at their price.</summary>
    public bool CanAffordImmersion(ulong id, ImmersionProduct product) =>
        PersonIn(PersonView.Roster, id) is not null && _wallets.TryGetValue(new(id), out var wallet) && wallet.CashPennies >= ImmersionPriceFor(id, product);
    /// <summary>What the stall's board says a customer pays.</summary>
    public int ImmersionListPrice(ImmersionProduct product) => ImmersionPrice(product, HasPerk(PerkCatalogue.BeerFestival));
    /// <summary>Abstains from beer: never at a Beer Festival, where everyone came for it.</summary>
    public bool Teetotal(ulong id) => PersonIn(PersonView.Roster, id) is not null && Teetotal(_persons[id]);
    private bool Teetotal(Person person) => person.Abstains && !HasPerk(PerkCatalogue.BeerFestival);

    /// <summary>What a sale costs the festival: the bar's stock. Food is the trader's, bought and sold by them.</summary>
    public static int ImmersionCost(ImmersionProduct product) => product switch
    { ImmersionProduct.Water or ImmersionProduct.Chips or ImmersionProduct.Pizza => 0, ImmersionProduct.SoftDrink => 60, ImmersionProduct.Beer => 120,
        _ => throw new ArgumentOutOfRangeException(nameof(product)) };
    // A cup of water goes down quicker than anything bought.
    public static int ImmersionConsumeTicks(ImmersionProduct product) => product switch { ImmersionProduct.Chips or ImmersionProduct.Pizza => 3200, ImmersionProduct.SoftDrink => 2800, ImmersionProduct.Water => 1600, _ => 2400 };
    /// <summary>How long a serving takes: food at the pace of the trader who sells it, drinks quicker.</summary>
    public static int ImmersionServiceDuration(ImmersionProduct product) =>
        product.IsFood() ? 320 * FoodTraders.Selling(product).ServicePermille / 1000 : 160;
    public static GridCell ImmersionServiceCell(ImmersionVendor vendor) { var d=RotateWaterOffset(new(0,4),vendor.QuarterTurns);return new(vendor.Cell.X+d.X,vendor.Cell.Z+d.Z); }
    public static GridCell ImmersionQueueCell(ImmersionVendor vendor, int index) { if(vendor.QueueCells is { Length:>0 } cells)return cells[Math.Min(index,cells.Length-1)];var d = RotateWaterOffset(new(0, 4 + index * 2), vendor.QuarterTurns); return new(vendor.Cell.X + d.X, vendor.Cell.Z + d.Z); }
    public static GridCell[] ImmersionFootprint(ImmersionVendor vendor) => Enumerable.Range(vendor.Id=="food"?-7:-3,vendor.Id=="food"?13:7).SelectMany(x=>Enumerable.Range(vendor.Id=="food"?-3:-2,vendor.Id=="food"?7:5).Select(z=>{var offset=RotateWaterOffset(new(x,z),vendor.QuarterTurns); return new GridCell(vendor.Cell.X+offset.X,vendor.Cell.Z+offset.Z);})).ToArray();
    private string? ImmersionPlacementError(ImmersionVendor proposed)
    {
        var terrain=new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        var reserved=new HashSet<GridCell>();
        // A round berth around each tap, as taps and bins keep from each other, not a square that reads as a diamond on
        // screen; plus the whole solid footprint, which for the big water main fills its 7×7 corners.
        foreach(var point in WaterPoints()) for(var x=point.Cell.X-3;x<=point.Cell.X+3;x++) for(var z=point.Cell.Z-3;z<=point.Cell.Z+3;z++) if(Math.Max(Math.Abs(x-point.Cell.X),Math.Abs(z-point.Cell.Z))<=WaterFootprintRadius(point) || (x-point.Cell.X)*(x-point.Cell.X)+(z-point.Cell.Z)*(z-point.Cell.Z)<=11) reserved.Add(new(x,z));
        foreach(var point in WaterPoints())foreach(var cell in LooseQueueGeometry.Corridor(CaptureWaterQueueCells(point.Id)))reserved.Add(cell);
        foreach(var vendor in Vendors.Where(v=>v.Id!=proposed.Id)) { foreach(var cell in ImmersionFootprint(vendor).Concat(LooseQueueGeometry.Corridor(VendorQueueCells(vendor))))reserved.Add(cell); }
        foreach (var toilet in EffectiveToilets(_facilities))
            foreach (var cell in ToiletReservedCells(toilet).Append(ToiletQueueCell(toilet, 0)).Append(ToiletExitCell(toilet))) reserved.Add(cell);
        for(var x=90;x<=101;x++)for(var z=140;z<=159;z++)reserved.Add(new(x,z));
        foreach(var cell in ResponsePostReserved(_preparation))reserved.Add(cell);
        // The rest area only exists once first aid is placed; until then nothing is kept clear for it.
        var firstAidPlaced=FirstAidPlacement(_preparation) is not null;
        var restCentre=CaptureRestCentre();if(firstAidPlaced)for(var x=restCentre.X-1;x<=restCentre.X+1;x++)for(var z=restCentre.Z-1;z<=restCentre.Z+1;z++)reserved.Add(new(x,z));
        if(_equipment is { } unit) { var centre=TraversalGrid.WorldToCell(unit.XMillimetres,unit.ZMillimetres);for(var x=centre.X-5;x<=centre.X+5;x++)for(var z=centre.Z-5;z<=centre.Z+5;z++)reserved.Add(new(x,z)); }
        if(_preparation!.WaterTowerOwned)for(var x=WaterTowerCell.X-4;x<=WaterTowerCell.X+4;x++)for(var z=WaterTowerCell.Z-4;z<=WaterTowerCell.Z+4;z++)reserved.Add(new(x,z));
        var needed=ImmersionFootprint(proposed).Append(ImmersionServiceCell(proposed)).ToArray();
        var otherCorridors=WaterPoints().SelectMany(point=>LooseQueueGeometry.Corridor(CaptureWaterQueueCells(point.Id))).Concat(ImmersionQueueCorridor(proposed.Id));
        var service=ImmersionServiceCell(proposed);
        if(otherCorridors.Any(cell=>Math.Abs(cell.X-service.X)<=1&&Math.Abs(cell.Z-service.Z)<=1))return "Vendor service overlaps an existing physical queue corridor.";
        if(needed.Any(InAudienceArea))return "Keep the audience area in front of the stage clear.";
        if(needed.Any(c=>!terrain.Contains(c)||!terrain.Get(c).IsWalkable||reserved.Contains(c)))return "Vendor footprint or queue overlaps an obstacle, protected service or stage.";
        if((FirstAidPlacement(_preparation) is not null || StewardPostPlacement(_preparation) is not null) && !PlacementAccessClear(_preparation,_equipment,ImmersionView,_medical,(_facilities ?? NoFacilities) with{Vendors=Vendors.Select(v=>v.Id==proposed.Id?proposed:v).ToArray()}))return "Vendor blocks a response post or essential approach.";
        var blocked=terrain.Overrides.ToDictionary(p=>p.Key,p=>p.Value);
        foreach(var vendor in Vendors.Where(v=>v.Id!=proposed.Id).Append(proposed))foreach(var cell in ImmersionFootprint(vendor))blocked[cell]=new(cell,GroundSurface.Grass,false);
        var grid=new TraversalGrid(blocked.Values);
        foreach(var cell in Vendors.Where(v=>v.Id!=proposed.Id).Append(proposed).Select(ImmersionServiceCell).Concat(firstAidPlaced?[restCentre]:[]))if(!DeterministicPathfinder.FindPath(grid,MedicalExitCell,cell).Found)return "Vendor blocks an essential approach.";
        return null;
    }
    private void BlockImmersionVendors()
    {
        if(_immersion is null||_traversalGrid is null)return;
        var terrain=_traversalGrid.Overrides.ToDictionary(p=>p.Key,p=>p.Value);
        foreach(var vendor in Vendors)foreach(var cell in ImmersionFootprint(vendor))terrain[cell]=new(cell,GroundSurface.Grass,false);
        _traversalGrid=new TraversalGrid(terrain.Values);
    }
    private static ImmersionPerson NewImmersionPerson(ulong seed, EditionPerson person)
    {
        var stable = person.AgentId * 6364136223846793005UL + seed;
        stable=(stable^(stable>>30))*0xBF58476D1CE4E5B9UL;stable=(stable^(stable>>27))*0x94D049BB133111EBUL;stable^=stable>>31;
        // A bounded mixture spans £8..£25 near £15 without assigning social stereotypes; crew and bands carry £15.
        var budget = person.Role == ProtectedPersonRole.Guest ? stable%10<7 ? 800+(int)((stable/13)%1001) : 1800+(int)((stable/13)%701) : 1500;
        return new ImmersionPerson(person.AgentId, budget, 2500 + (int)(stable % 2001), stable % 4 == 0, (int)(stable % 101), (int)((stable / 13) % 101), (int)((stable / 71) % 101))
        { ToiletNeed = 2000 + (int)((stable / 29) % 2001) };
    }
    private static ImmersionSnapshot NewImmersion(ulong seed, EditionPerson[] people) => new(3, false, 0, 0,
        people.Select(p => NewImmersionPerson(seed, p)).ToArray(), []);
    /// <summary>The food van and bar a campaign opens with before any Build layout exists.</summary>
    private static ImmersionVendor[] OpeningVendors() => [new("food", new(144, 119), 0, []), new("drinks", new(160, 120), 0, [])];
    private void SynchronizeImmersionPeople()
    {
        if (_immersion is null || _preparation is null) return;
        foreach (var person in PeopleIn(PersonView.Roster).Where(person=>!InView(PersonView.Consumption, person.Id)).ToArray()) { var p=NewImmersionPerson(CampaignSeed,person.ToEditionPerson()); _wallets[new(p.AgentId)].CashPennies=p.OpeningBudgetPennies; ImmersionView=ImmersionView! with { People=ImmersionView.People.Append(p).OrderBy(n=>n.AgentId).ToArray() }; }
        if(_medical is not null) foreach(var person in PeopleIn(PersonView.Roster).Where(p=>!InView(PersonView.Medical, p.Id)).ToArray()) MedicalView=MedicalView! with { Needs=MedicalView.Needs.Append(new MedicalNeed(person.Id,3000,2500,MedicalIntent.WatchShow,"Idle staff: food, soft drinks and free water available",-MedicalDecisionCooldownTicks,null,-1,MedicalNeedProfile.Staff)).OrderBy(n=>n.AgentId).ToArray() };
    }
    // Free water is the emergency measure, so the thirsty and overheated are exactly who may queue for it.
    private bool ImmersionShoppingEligible(ulong id, ImmersionProduct? product = null) => _preparation?.Status == PreparationStatus.Running && ImmersionHandsAvailable(id) && !IsCurrentProgrammePerformer(id) &&
        !ToiletOwnsNavigation(id) &&
        (PersonIn(PersonView.Medical, id) is null or { Intent: MedicalIntent.WatchShow, Thirst: < MedicalDistressThirst, HeatExposure: < MedicalDistressHeat } ||
         product == ImmersionProduct.Water && PersonIn(PersonView.Medical, id) is { Intent: MedicalIntent.WatchShow });
    // The same read-only eligibility drives ingestion and its presentation. Pause
    // freezes the tick scheduler, not this predicate, so a paused sip stays a sip.
    public bool ImmersionConsumptionEligible(ulong id) => MedicalOperationsActive &&
        PersonIn(PersonView.Consumption, id)?.Held is not null &&
        _navigationAgents.ContainsKey(new(id)) && ImmersionHandsAvailable(id) &&
        !ToiletOwnsNavigation(id) &&
        !ImmersionOwnsNavigation(id) && ImmersionAwayFromCounters(id) && StaffAtConsumptionPost(id);

    private bool StaffAtConsumptionPost(ulong id)
    {
        if (StaffAssignedPost(id) is not { } post) return true;
        var nav = _navigationAgents[new(id)]; var centre = TraversalGrid.CellCentre(post);
        return nav.Action == AgentNavigationAction.Arrived && nav.XMillimetres == centre.XMillimetres && nav.ZMillimetres == centre.ZMillimetres;
    }
    private bool ImmersionAwayFromCounters(ulong id)
    {
        var nav=_navigationAgents[new(id)];return Vendors.All(v=>{var centre=TraversalGrid.CellCentre(ImmersionServiceCell(v));var x=(long)nav.XMillimetres-centre.XMillimetres;var z=(long)nav.ZMillimetres-centre.ZMillimetres;return x*x+z*z>1000000;});
    }
    private void SetImmersionVendor(ImmersionVendor vendor) => SetVendors(Vendors.Select(v => v.Id == vendor.Id ? vendor : v).ToArray());
    // Free water is from the tap behind the bar: no stock to run out of, and only while it is on.
    // A food trader brings what they need and never sells out; the van sells only its own trader's food, once it's open.
    private int ImmersionStock(ImmersionProduct product) => product switch { _ when product.IsFood() => product == FoodTrader.Product && FoodVanPitched && _preparation!.Plan is { Committed: true } ? int.MaxValue : 0, ImmersionProduct.SoftDrink => _immersion!.SoftStock,
        ImmersionProduct.Water => _immersion!.FreeWater ? int.MaxValue : 0, _ => _immersion!.BeerStock };
    // Switching free water off stops new cups, but anyone already queuing for one is still served.
    private bool ImmersionOrderEligible(Person p, ImmersionProduct product) => p.Held is null && ImmersionShoppingEligible(p.Id, product) &&
        (ImmersionStock(product) > 0 || product == ImmersionProduct.Water && p.VendorId is not null && p.Order == ImmersionProduct.Water) &&
        _wallets[new(p.Id)].CashPennies >= ImmersionPriceFor(p.Id, product) && (product != ImmersionProduct.Beer || BeerAllowed(p)) &&
        // A stall switched off to spare the generator sells nothing; free water from the bar's tap needs no power.
        (product == ImmersionProduct.Water || StallPowered(ImmersionVendorFor(product)));
    /// <summary>The stall that sells it: food at the food van, everything else at the bar.</summary>
    internal static string ImmersionVendorFor(ImmersionProduct product) => product.IsFood() ? "food" : "drinks";
    private void LeaveImmersionQueue(ulong id, bool reroute)
    {
        var p = _persons[id];
        foreach (var vendor in Vendors.Where(v => v.Queue.Contains(id) || v.OwnerId == id).ToArray()) SetImmersionVendor(vendor with { Queue = vendor.Queue.Where(q => q != id).ToArray(), OwnerId = vendor.OwnerId == id ? null : vendor.OwnerId, ServiceTicks = vendor.OwnerId == id ? 0 : vendor.ServiceTicks });
        SetConsumption(p with { VendorId = null, Order = null, ShoppingDecisionTick = CurrentTick });
        if (reroute) ReturnToListening(id);
    }
    private void CompleteImmersionSale(ulong id, ImmersionProduct product)
    {
        var p = _persons[id]; var owner = new EntityId(_preparation!.FinanceOwnerId); var buyer = new EntityId(id);
        var price = ImmersionPriceFor(id, product); var cost = ImmersionCost(product); var transaction = $"immersion:{_preparation.Attempt}:{_immersion!.Purchases.Length+1}";
        _wallets[buyer].CashPennies -= price;
        // Food money goes to the trader; the bar's takings to the festival.
        if (!product.IsFood()) _festivalFinances[owner].CashPennies += price;
        var purchase = new ImmersionPurchase(transaction, id, product, CurrentTick, price, cost, SaleEntries(buyer, owner, product, price, cost));
        _immersion = _immersion with { SoftStock = _immersion.SoftStock - (product == ImmersionProduct.SoftDrink ? 1 : 0), BeerStock = _immersion.BeerStock - (product == ImmersionProduct.Beer ? 1 : 0), Purchases = _immersion.Purchases.Append(purchase).ToArray() };
        LeaveImmersionQueue(id, true); SetConsumption(_persons[id] with { Held = new(transaction, product, 0) });
    }
    private static LedgerEntry[] SaleEntries(EntityId buyer, EntityId festival, ImmersionProduct product, int price, int cost) =>
        product.IsFood()
            ? [new(buyer, LedgerAccountType.CashAsset, -price), new(buyer, LedgerAccountType.GuestSpending, price)]
            : [new(buyer, LedgerAccountType.CashAsset, -price), new(buyer, LedgerAccountType.GuestSpending, price), new(festival, LedgerAccountType.CashAsset, price),
               new(festival, LedgerAccountType.SalesRevenue, -price), new(festival, LedgerAccountType.CostOfGoodsSold, cost), new(festival, LedgerAccountType.InventoryAsset, -cost)];

    /// <summary>The trader a saved festival pitched at its food van, and how many portions they brought.</summary>
    private static FoodTrader SavedFoodTrader(PreparationSnapshot prep) => FoodTraders.Find(prep.Plan?.TraderId) ?? FoodTraders.Default;

    private void AdvanceImmersion()
    {
        if (_immersion is null || !MedicalOperationsActive) return;
        if(!ImmersionDepartureActive)GrowImmersionQueues();
        foreach (var original in PeopleIn(PersonView.Consumption))
        {
            var presence = _persons[original.Id];
            // Not yet through the gate: guests, and tardy staff still held off site, gain no needs.
            if (presence.Departed || presence.Role == ProtectedPersonRole.Guest && !presence.Admitted || GuestWaitingForRelease(original.Id)) continue;
            var p = original with { }; // One scratch copy per person; updated in place below.
            var recovery = p.RecoveryResidue + 10; var hunger = p.HungerResidue + 12;
            { p.Intoxication = Math.Max(0,p.Intoxication-recovery/80); p.RecoveryResidue = recovery%80; p.Hunger = Math.Min(10000,p.Hunger+hunger/80); p.HungerResidue = hunger%80; p.FoodProtectionTicks = Math.Max(0,p.FoodProtectionTicks-1); p.ToiletNeed = _preparation!.Status == PreparationStatus.Running && p.ToiletStage != ToiletVisitStage.Using && CurrentTick % ToiletNeedGainEveryTicks(p.Id) == 0 ? Math.Min(ToiletRules.NeedMaximum, p.ToiletNeed + 1) : p.ToiletNeed; }
            if(!InView(PersonView.Medical, p.Id)&&CurrentTick%4==0)p.StaffThirst = Math.Min(10000,p.StaffThirst+1);
            if (RobotWorker(p.Id)) { p.Hunger = 0; p.ToiletNeed = 0; p.StaffThirst = 0; }
            // Previously ingested dose keeps absorbing even when hands are owned by stage or care.
            if (p.PendingDose > 0) { var absorb = Math.Min(1,p.PendingDose); var residue = p.AbsorptionResidue + absorb*(p.FoodProtectionTicks>0 ? 1 : 2)*AbsorptionFactor(p.Id); { p.PendingDose = p.PendingDose-absorb; p.Intoxication = Math.Min(10000,p.Intoxication+residue/2); p.AbsorptionResidue = residue%2; } }
            if (p.Held is { } held && ImmersionConsumptionEligible(p.Id))
            {
                var duration = ImmersionConsumeTicks(held.Product); var elapsed = held.ConsumedTicks+1;
                if (held.Product == ImmersionProduct.Beer && _preparation.Status == PreparationStatus.Running &&
                    CurrentTick % ToiletRules.BeerConsumptionExtraGainEveryTicks == 0)
                    p.ToiletNeed = Math.Min(ToiletRules.NeedMaximum, p.ToiletNeed + 1);
                // A free cup of water quenches thirst fully and cools a little, as a tap does.
                // Food fills and pleases by what it is, whoever's van is pitched now.
                var effect = held.Product switch { _ when held.Product.IsFood() => 5500 * FoodTraders.Selling(held.Product).FillingPercent / 100, ImmersionProduct.SoftDrink => 6000, ImmersionProduct.Water => 10_000, _ => 1500 };
                var delta = elapsed*effect/duration-held.ConsumedTicks*effect/duration;
                if (held.Product.IsFood()) { p.Hunger = Math.Max(0,p.Hunger-delta); p.FoodProtectionTicks = 4800; }
                // Heat falls with the thirst actually relieved, as at a tap and as the activity chooser projects.
                else if (InView(PersonView.Medical, p.Id)) MutatePerson(p.Id, n => { var removed = Math.Min(delta, n.Thirst); n.Thirst -= removed;
                    if (held.Product == ImmersionProduct.Water) n.HeatExposure = Math.Max(0, n.HeatExposure - removed / 4); });
                else p.StaffThirst = Math.Max(0,p.StaffThirst-delta);
                if (held.Product == ImmersionProduct.Beer) p.PendingDose = p.PendingDose + elapsed*2400/duration-held.ConsumedTicks*2400/duration;
                var enjoyment = held.Product switch { _ when held.Product.IsFood() => FoodTraders.Selling(held.Product).EnjoymentPercent, ImmersionProduct.SoftDrink => 75, ImmersionProduct.Water => 0, _ => 150*BeerTasteOf(p)/100 };
                var gain = elapsed*enjoyment/duration-held.ConsumedTicks*enjoyment/duration;
                ChangeSatisfaction(p.Id, gain, MoodCause.FoodAndDrink);
                if (elapsed == duration && held.Product == ImmersionProduct.Beer && _preparation.FinishedBeerIds is { } finished && PersonIn(PersonView.Roster, p.Id) is { Role: ProtectedPersonRole.Guest, Admitted: true })
                    _preparation = _preparation with { FinishedBeerIds = finished.Append(held.TransactionId).ToArray() };
                if (elapsed == duration) RecordCompletedWaste(p.Id, held);
                p.Held = elapsed == duration ? null : held with { ConsumedTicks = elapsed };
            }
            if (p.Intoxication >= 7500 && p.IntoxicationWarningTick < 0) { p.IntoxicationWarningTick = CurrentTick; MedicalEvent("intoxication:warning", $"Person {p.Id}: heavy intoxication {p.Intoxication}; {(StaffHas(p.Id, StaffTrait.SneakyAlcoholic) ? "still getting served on the sly" : "no further beer served")}, water, rest and medic available."); }
            p.SevereTicks = p.Intoxication >= 8500 ? p.SevereTicks+1 : 0;
            SetConsumption(p);
            if (p.IntoxicationCollapseTick>=0 && PersonIn(PersonView.Medical, p.Id) is { } collapsed)
            {
                if(collapsed.HealthStage==MedicalStage.Collapsed && CurrentTick>=p.IntoxicationCollapseTick+MedicalCriticalDelayTicks) { MutatePerson(p.Id, n => { n.HealthStage = MedicalStage.Critical; n.HealthCriticalTick = CurrentTick; }); MedicalEvent("intoxication:critical",$"Person {p.Id}: untreated intoxication collapse; physical response deadline remains."); }
                if(CurrentTick>=p.IntoxicationCollapseTick+MedicalDeathDelayTicks && _persons[p.Id].HealthStage==MedicalStage.Critical) { var need=_persons[p.Id];ApplyMedicalDeath(p.Id,p.IntoxicationWarningTick,p.IntoxicationCollapseTick,need.HealthCriticalTick);return; }
            }
            if (p.SevereTicks >= 1600 && p.IntoxicationWarningTick >= 0 && p.IntoxicationCollapseTick < 0 && !ExistingMedicalHazardOwns(p.Id) && InView(PersonView.Medical, p.Id))
            {
                var need = _persons[p.Id];
                { p.IntoxicationCollapseTick = CurrentTick; p.PriorMedicalStage = need.HealthStage; } SetConsumption(p);
                LeaveImmersionQueue(p.Id,false); LeaveWater(p.Id,"Intoxication collapse",false); MedicalRelinquishPerformerStage(p.Id);
                var nav=_navigationAgents[new(p.Id)]; if (!StuckInToilet(p.Id)) ApplyAgentDestination(new(p.Id),new(TraversalGrid.WorldToCell(nav.XMillimetres,nav.ZMillimetres),"medical.intoxication-collapse"));
                MutatePerson(p.Id, n => { n.HealthStage = MedicalStage.Collapsed; n.HealthWarningTick = p.IntoxicationWarningTick; n.HealthCollapseTick = CurrentTick; n.Intent = MedicalIntent.Collapsed; n.Reason = "Intoxication collapse; physical medic response required"; });
                ReleaseCollapsedStewardResponse(p.Id);
                MedicalEvent("intoxication:collapse",$"Person {p.Id}: exposure continuously above8500 for20s after warning {p.IntoxicationWarningTick}; critical and fatal response deadlines begin now.");
                RecordGuestMedicalCollapse(p.Id);
            }
            if (p.VendorId is not null && (p.Order is not { } order || !ImmersionOrderEligible(p,order))) { LeaveImmersionQueue(p.Id,ImmersionShoppingEligible(p.Id)); continue; }
        }
        if (ImmersionDepartureActive) return;
        foreach (var original in Vendors)
        {
            var vendor = original;
            foreach (var p in PeopleIn(PersonView.Consumption).Where(p=>p.VendorId==vendor.Id && !vendor.Queue.Contains(p.Id)).OrderBy(p=>p.Id).ToArray())
            { var nav = _navigationAgents[new(p.Id)]; if (vendor.Queue.Length<10 && nav.Action==AgentNavigationAction.Arrived && nav.IntentId=="immersion.approach") vendor=vendor with { Queue=vendor.Queue.Append(p.Id).ToArray() }; }
            // Nearest the end of the line takes the next place, as at the toilets.
            var approachers=NearestFirst(PeopleIn(PersonView.Consumption).Where(p=>p.VendorId==vendor.Id&&!vendor.Queue.Contains(p.Id)).Select(p=>p.Id),ImmersionQueueCell(vendor,vendor.Queue.Length));
            for(var i=0;i<approachers.Length;i++){var cell=ImmersionQueueCell(vendor,Math.Min(14,vendor.Queue.Length+i));var id=approachers[i];if(_navigationAgents[new(id)].Destination!=cell)ApplyAgentDestination(new(id),new(cell,"immersion.approach"));}
            for (var i=0;i<vendor.Queue.Length;i++) { var id=vendor.Queue[i]; var cell=ImmersionQueueCell(vendor,i); if (_navigationAgents[new(id)].Destination!=cell) ApplyAgentDestination(new(id),new(cell,"immersion.queue")); }
            if (vendor.OwnerId is null && vendor.Queue.Length>0 && _navigationAgents[new(vendor.Queue[0])].Action==AgentNavigationAction.Arrived && _navigationAgents[new(vendor.Queue[0])].Destination==ImmersionServiceCell(vendor)) vendor=vendor with { OwnerId=vendor.Queue[0],ServiceTicks=ImmersionServiceDuration(PeopleIn(PersonView.Consumption).Single(p=>p.Id==vendor.Queue[0]).Order!.Value) };
            if (vendor.OwnerId is { } owner) { vendor=vendor with { ServiceTicks=vendor.ServiceTicks-1 }; SetImmersionVendor(vendor); if (vendor.ServiceTicks==0) { var p=_persons[owner]; if (p.Order is { } product && ImmersionOrderEligible(p,product)) CompleteImmersionSale(owner,product); else LeaveImmersionQueue(owner,true); } }
            else SetImmersionVendor(vendor);
        }
    }
    private bool IntoxicationWarning(ulong id) => PersonIn(PersonView.Consumption, id) is { IntoxicationWarningTick: >=0 } p && (p.Intoxication>=7500 || p.IntoxicationCollapseTick>=0 || p.CareTicks>0);
    private bool ExistingMedicalHazardOwns(ulong id) => _medical is { } medical &&
        (PersonIn(PersonView.Medical, id)?.HealthStage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical ||
         PersonIn(PersonView.Disorder, id)?.ConductStage==DisorderStage.Injured);
    private bool IntoxicationCareOwns(MedicResponse job)
    {
        if(job.PatientId is not { } id || !IntoxicationWarning(id) || _immersion is null || _medical is null)return false;
        var p=_persons[id];var need=_persons[id];
        return !(p.IntoxicationCollapseTick<0&&ExistingMedicalHazardOwns(id) || p.IntoxicationCollapseTick>=0&&(need.HealthStage is MedicalStage.Collapsed or MedicalStage.Critical)&&need.HealthCollapseTick!=p.IntoxicationCollapseTick || PersonIn(PersonView.Disorder, id)?.ConductStage==DisorderStage.Injured);
    }
    private bool AdvanceIntoxicationCare(MedicResponse job)
    {
        if (!IntoxicationCareOwns(job)) return false;
        var id=job.PatientId!.Value;var p=_persons[id];
        var need=_persons[id];
        // Existing heat/injury ownership keeps its own causal treatment path and deadlines.
        if(p.CareTicks==0 && p.IntoxicationCollapseTick<0)p=p with { PriorMedicalStage=need.HealthStage };
        // Only the physical treating owner calls this; neither dispatch nor water sobers.
        var care=p.CareTicks+1; p=p with { CareTicks=care,Intoxication=Math.Max(0,p.Intoxication-4) }; SetConsumption(p);
        if (care<1600 || p.Intoxication>=7500) return true;
        var restored=p.PriorMedicalStage==MedicalStage.Treated?MedicalStage.Treated:MedicalStage.Clear;
        SetConsumption(p with { IntoxicationWarningTick=-1,IntoxicationCollapseTick=-1,SevereTicks=0,CareTicks=0 });
        MutatePerson(id, n => { n.HealthStage = restored; n.Intent = MedicalIntent.WatchShow; n.Reason = "Gradual intoxication stabilization completed; future exposure can warn again"; n.HealthWarningTick = -1; n.HealthCollapseTick = -1; n.HealthCriticalTick = -1; });
        SetMedicResponse(job with { Stage=MedicalResponseStage.Completed,Description="Gradual intoxication stabilization completed" }); ReturnToListening(id);
        MedicalEvent("intoxication:treatment-complete",$"Physical medic {job.WorkerId} stabilized {id} gradually over20s; exposure remains {p.Intoxication}."); return true;
    }
    private int ImmersionAggression(ulong id,int temperament) => PersonIn(PersonView.Consumption, id) is { } person ? Math.Clamp(person.Intoxication*temperament/25000000,0,2) : 0;
    private int ImmersionCoordinationPace(ulong id) => PersonIn(PersonView.Consumption, id) is { Intoxication:>=5000 } person && (_persons[id].Role==ProtectedPersonRole.Guest || StaffHas(id, StaffTrait.SneakyAlcoholic)) && !MedicalOwnsNavigation(id) && !DisorderOwnsNavigation(id) && !InterventionOwnsTarget(id) ? Math.Max(800,1000-(person.Intoxication-5000)/25) : 1000;
    public bool ImmersionBoundaryOnNextTick => !IsPaused && MedicalOperationsActive && _immersion is { } m &&
        (EffectiveToilets(_facilities).Any(toilet => toilet.OwnerId is not null && toilet.ServiceTicks <= 1 &&
             (toilet.OwnerId is { } owner && PersonIn(PersonView.Consumption, owner) is { ToiletStage: ToiletVisitStage.Using })) ||
         Vendors.Any(v=>v.OwnerId is not null && v.ServiceTicks<=1) || PeopleIn(PersonView.Consumption).Any(p=>!_persons[p.Id].Departed && (p.IntoxicationWarningTick<0 && p.Intoxication>=7500-GuestCharacters.LightweightAbsorption || p.SevereTicks>=1599 && p.Intoxication>=8500 && p.IntoxicationWarningTick>=0 && p.IntoxicationCollapseTick<0 && !ExistingMedicalHazardOwns(p.Id) || p.IntoxicationCollapseTick>=0 && (CurrentTick+1==p.IntoxicationCollapseTick+MedicalCriticalDelayTicks || CurrentTick+1==p.IntoxicationCollapseTick+MedicalDeathDelayTicks))));
    private bool IntoxicationCareBoundary(MedicResponse job) => IntoxicationCareOwns(job) && _persons[job.PatientId!.Value].CareTicks>=1599;
    private void CleanupImmersionDeparture()
    {
        if(_immersion is null||_preparation is not { Status:PreparationStatus.Departing or PreparationStatus.Finished } p)return;
        SetVendors(Vendors.Select(v=>v with { Queue=[],OwnerId=null,ServiceTicks=0 }).ToArray());
        ImmersionView=ImmersionView! with { People=ImmersionView.People.Select(person=>person with { VendorId=null,Order=null,Held=_persons[person.AgentId].Departed?null:person.Held }).ToArray() };
    }
    private static string? ValidatePersistedImmersion(SessionPersistenceSnapshot s)
    {
        if (s.Immersion is not { } m) return null;
        if (s.Facilities is not { Vendors: { } vendors, Toilets: { } savedToilets } f) return "Saved facilities have no vendors or toilets.";
        if (m.Version!=3 || s.Programme is null || s.Preparation is not { } prep || m.People is null || m.Purchases is null || m.People.Any(p=>p is null) || vendors.Any(v=>v is null || v.Queue is null) || m.Purchases.Any(p=>p is null || p.Entries is null)) return "Immersion version or collections invalid.";
        if(!m.People.Select(p=>p.AgentId).SequenceEqual(prep.People.Select(p=>p.AgentId)))return "Immersion protected person identities invalid.";
        if (((!vendors.Select(v=>v.Id).SequenceEqual(prep.BuildPlacements.Where(item=>item.Kind is BuildServiceKind.FoodVan or BuildServiceKind.Bar).Select(item=>item.Id).Order(StringComparer.Ordinal)) ||
                 savedToilets.Any(item=>item.Id=="toilet.main") != prep.BuildPlacements.Any(item=>item.Id=="toilet.main"))) ||
            vendors.Any(v=>v.QuarterTurns is <0 or >3))return "Immersion vendor identities invalid.";
        var geometry=CreateFoodAndDrinkBaseline(s.CampaignSeed);geometry.ImmersionView=m;geometry._facilities=f;geometry.PreparationView=prep;geometry.MedicalView=s.Medical;geometry._equipment=s.Equipment;
        foreach(var vendor in vendors)if(geometry.ImmersionPlacementError(vendor) is { } issue)return issue;
        if (ValidatePersistedToilets(s, m, f, geometry) is { } toiletError) return toiletError;
        var queueGrid=s.TraversalGrid is { } savedTerrain?new TraversalGrid(savedTerrain.Cells.Select(c=>new TerrainCellOverride(new(c.X,c.Z),(GroundSurface)c.Surface,c.IsWalkable))):new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        foreach(var vendor in vendors.Where(v=>v.QueueCells is not null))
        {
            var cells=vendor.QueueCells!;var members=m.People.Count(person=>person.VendorId==vendor.Id);
            var reserved=geometry.WaterPoints().SelectMany(point=>LooseQueueGeometry.Corridor(geometry.CaptureWaterQueueCells(point.Id))).Concat(geometry.ImmersionQueueCorridor(vendor.Id)).ToArray();
            if(cells.Length is <1 or >10 || cells.Length<members || cells[0]!=ImmersionServiceCell(vendor) || LooseQueueGeometry.Corridor(cells).Any(cell=>!QueueGroundAllowed(cell,prep)) || !LooseQueueGeometry.Valid(cells,RotateWaterOffset(new(0,1),vendor.QuarterTurns),queueGrid,reserved))return "Immersion saved loose queue geometry invalid.";
        }
        if(prep.Status!=PreparationStatus.Preparing && (s.TraversalGrid is null || vendors.SelectMany(ImmersionFootprint).Any(cell=>!s.TraversalGrid.Cells.Any(c=>c.X==cell.X&&c.Z==cell.Z&&!c.IsWalkable))))return "Immersion vendor solid footprint absent from saved traversal.";
        var festival=new EntityId(prep.FinanceOwnerId);
        var stockCost = prep.Plan is null ? StockCost(40, 32) : PlannedStockCost(prep.Plan);
        if(prep.Plan is { Committed:true } && m.StockPurchased != (stockCost > 0)) return "Committed stock plan activation invalid.";
        if(m.StockPurchased!=(m.StockPurchase is not null) || m.StockPurchase is { } purchased && (purchased.Id!=$"immersion-stock:{s.CampaignId}:{prep.Attempt}" || purchased.Attempt!=prep.Attempt || purchased.Tick<0 || purchased.Tick>s.CurrentTick || purchased.Entries is null || !purchased.Entries.SequenceEqual(new LedgerEntry[]{new(festival,LedgerAccountType.CashAsset,-stockCost),new(festival,LedgerAccountType.InventoryAsset,stockCost)})))return "Immersion starter stock purchase ledger invalid.";
        var hired = SavedHiredAgents(s);
        var beerFestival = SavedPerkEffect(s.Perks, PerkCatalogue.BeerFestival);
        int SavedPrice(ImmersionPurchase purchase) => ImmersionPrice(purchase.Product, prep.People.Single(n=>n.AgentId==purchase.AgentId).Role, beerFestival,
            hired.GetValueOrDefault(purchase.AgentId)?.Has(StaffTrait.SneakyAlcoholic) ?? false);
        foreach(var purchase in m.Purchases)
        {
            var buyer=new EntityId(purchase.AgentId);var price=SavedPrice(purchase);var cost=ImmersionCost(purchase.Product);
            if(!purchase.Entries.SequenceEqual(SaleEntries(buyer,festival,purchase.Product,price,cost)))return "Immersion sale ledger accounts/owners invalid.";
            if(purchase.Product==ImmersionProduct.Beer && m.People.Any(p=>p.AgentId==purchase.AgentId && SavedBeerBarred(p, prep, hired, beerFestival)))return "Immersion beer eligibility invalid.";
        }
        if(m.Purchases.Where((purchase,index)=>purchase.Id!=$"immersion:{prep.Attempt}:{index+1}" || (purchase.Product != ImmersionProduct.Water && !purchase.Product.IsFood() && (m.StockPurchase is null || purchase.Tick<m.StockPurchase.Tick)) || index>0&&purchase.Tick<m.Purchases[index-1].Tick).Any())return "Immersion sale immutable identity/order invalid.";
        var freeWaterIssue = ValidateFreeWater(m, prep, s); if (freeWaterIssue is not null) return freeWaterIssue;
        if(m.People.Any(p=>p.StaffThirst is <0 or >10000 || p.CareTicks<0 || !Enum.IsDefined(p.PriorMedicalStage) || p.CollapseTick>s.CurrentTick || p.CollapseTick>=0 && (p.WarningTick<0 || p.CollapseTick<p.WarningTick+1599) || p.Order==ImmersionProduct.Beer && SavedBeerBarred(p, prep, hired, beerFestival)) || vendors.Any(v=>v.Queue.Length>10 || v.OwnerId is { } owner && (!m.People.Any(p=>p.AgentId==owner && p.Order is { } order && v.ServiceTicks<=ImmersionServiceDuration(order)) || s.NavigationAgents?.SingleOrDefault(n=>n.Id==owner) is not { Action:(int)AgentNavigationAction.Arrived } nav || nav.DestinationX!=ImmersionServiceCell(v).X || nav.DestinationZ!=ImmersionServiceCell(v).Z)))return "Immersion cause/role/physical service owner invalid.";
        if(prep.Status is PreparationStatus.Departing or PreparationStatus.Finished && (vendors.Any(v=>v.Queue.Length>0||v.OwnerId is not null)||m.People.Any(p=>p.VendorId is not null||prep.People.Single(n=>n.AgentId==p.AgentId).Departed&&p.Held is not null)))return "Immersion departure ownership invalid.";
        if (!m.People.Select(p=>p.AgentId).SequenceEqual(prep.People.Select(p=>p.AgentId)) || vendors.Any(v=>v.QuarterTurns is <0 or >3 || !new TraversalGrid().Contains(v.Cell) || v.ServiceTicks<0 || v.OwnerId is { } id && (v.Queue.Length==0 || v.Queue[0]!=id) || v.OwnerId is null && v.ServiceTicks!=0) || vendors.SelectMany(v=>v.Queue).Distinct().Count()!=vendors.Sum(v=>v.Queue.Length)) return "Immersion vendor identity/ownership invalid.";
        foreach (var p in m.People) { var stable=NewImmersionPerson(s.CampaignSeed,prep.People.Single(n=>n.AgentId==p.AgentId)); if (p.OpeningBudgetPennies!=stable.OpeningBudgetPennies || p.Abstains!=stable.Abstains || p.BeerTaste!=stable.BeerTaste || p.SoftTaste!=stable.SoftTaste || p.PriceReluctance!=stable.PriceReluctance || p.Hunger is <0 or >10000 || p.Intoxication is <0 or >10000 || p.PendingDose is <0 or >2400 || p.FoodProtectionTicks is <0 or >4800 || p.HungerResidue is <0 or >79 || p.RecoveryResidue is <0 or >79 || p.AbsorptionResidue is <0 or >1 || p.LastDecisionTick>s.CurrentTick || p.WarningTick>s.CurrentTick || p.SevereTicks<0 || (p.VendorId is null)!=(p.Order is null) || p.VendorId is not null && !vendors.Any(v=>v.Id==p.VendorId) || p.Order is { } order && (!Enum.IsDefined(order) || order.IsFood() && order != SavedFoodTrader(prep).Product) || p.Held is { } h && (!Enum.IsDefined(h.Product) || h.ConsumedTicks<0 || h.ConsumedTicks>=ImmersionConsumeTicks(h.Product) || p.VendorId is not null || !m.Purchases.Any(t=>t.Id==h.TransactionId && t.AgentId==p.AgentId && t.Product==h.Product)) || s.Wallets.Single(w=>w.OwnerId==p.AgentId).CashPennies!=p.OpeningBudgetPennies-m.Purchases.Where(t=>t.AgentId==p.AgentId).Sum(t=>t.PricePennies)) return "Immersion person, wallet or retained item invalid."; }
if (m.Purchases.Select(p=>p.Id).Distinct().Count()!=m.Purchases.Length || m.Purchases.Any(p=>!Enum.IsDefined(p.Product) || !m.People.Any(n=>n.AgentId==p.AgentId) || p.PricePennies!=SavedPrice(p) || p.CostPennies!=ImmersionCost(p.Product) || p.Tick<0 || p.Tick>s.CurrentTick || p.Entries.Sum(e=>e.AmountPennies)!=0) || m.Purchases.Any(p=>p.Product.IsFood() && (p.Product!=SavedFoodTrader(prep).Product || !(prep.Plan is { Committed: true } && prep.BuildPlacements.Any(b=>b.Kind==BuildServiceKind.FoodVan)))) || m.SoftStock!=(m.StockPurchased?prep.Plan?.SoftDrinks??40:0)-m.Purchases.Count(p=>p.Product==ImmersionProduct.SoftDrink) || m.BeerStock!=(m.StockPurchased?prep.Plan?.Beers??32:0)-m.Purchases.Count(p=>p.Product==ImmersionProduct.Beer) || Math.Min(m.SoftStock,m.BeerStock)<0 || vendors.Any(v=>v.Queue.Any(id=>!m.People.Any(p=>p.AgentId==id && p.VendorId==v.Id)))) return "Immersion stock/transactions/FIFO invalid.";
        return null;
    }
}
