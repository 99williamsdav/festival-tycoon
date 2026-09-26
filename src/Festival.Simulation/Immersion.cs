using System.Text.Json;

namespace Festival.Simulation;

public enum ImmersionProduct { Chips, SoftDrink, Beer }
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
}
public sealed record ImmersionVendor(string Id, GridCell Cell, int QuarterTurns, ulong[] Queue, ulong? OwnerId = null, int ServiceTicks = 0)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public GridCell[]? QueueCells { get; init; }
}
public sealed record ImmersionPurchase(string Id, ulong AgentId, ImmersionProduct Product, long Tick, int PricePennies, int CostPennies, LedgerEntry[] Entries);
public sealed record ImmersionStockPurchase(string Id,int Attempt,long Tick,LedgerEntry[] Entries);
public sealed record ImmersionSnapshot(int Version, bool StockPurchased, int ChipsStock, int SoftStock, int BeerStock,
    ImmersionVendor[] Vendors, ImmersionPerson[] People, ImmersionPurchase[] Purchases)
{
    public ImmersionStockPurchase? StockPurchase { get; init; }
}
public sealed record PurchaseImmersionStarterStockCommand : SessionCommand;
public sealed record PlaceImmersionVendorCommand(string VendorId, GridCell Cell, int QuarterTurns = 0) : SessionCommand;

public sealed partial class GameSession
{
    private ImmersionSnapshot? _immersion;
    public ImmersionSnapshot? CaptureImmersion() => _immersion is null ? null : JsonSerializer.Deserialize<ImmersionSnapshot>(JsonSerializer.Serialize(_immersion));
    internal string? ImmersionCanonicalJson => _immersion is null ? null : JsonSerializer.Serialize(_immersion);
    public static int ImmersionPrice(ImmersionProduct product) => product == ImmersionProduct.SoftDrink ? 200 : 300;
    public static int ImmersionCost(ImmersionProduct product) => product == ImmersionProduct.SoftDrink ? 60 : 100;
    public static int ImmersionConsumeTicks(ImmersionProduct product) => product switch { ImmersionProduct.Chips => 3200, ImmersionProduct.SoftDrink => 2800, _ => 2400 };
    public static int ImmersionServiceDuration(ImmersionProduct product) => product == ImmersionProduct.Chips ? 320 : 160;
    public static GridCell ImmersionServiceCell(ImmersionVendor vendor) { var d=RotateWaterOffset(new(0,4),vendor.QuarterTurns);return new(vendor.Cell.X+d.X,vendor.Cell.Z+d.Z); }
    public static GridCell ImmersionQueueCell(ImmersionVendor vendor, int index) { if(vendor.QueueCells is { Length:>0 } cells)return cells[Math.Min(index,cells.Length-1)];var d = RotateWaterOffset(new(0, 4 + index * 2), vendor.QuarterTurns); return new(vendor.Cell.X + d.X, vendor.Cell.Z + d.Z); }
    public static GridCell[] ImmersionFootprint(ImmersionVendor vendor) => Enumerable.Range(vendor.Id=="food"?-7:-3,vendor.Id=="food"?13:7).SelectMany(x=>Enumerable.Range(vendor.Id=="food"?-3:-2,vendor.Id=="food"?7:5).Select(z=>{var offset=RotateWaterOffset(new(x,z),vendor.QuarterTurns); return new GridCell(vendor.Cell.X+offset.X,vendor.Cell.Z+offset.Z);})).ToArray();
    private static bool WaterOverlapsImmersion(ImmersionSnapshot? immersion,GridCell cell,int quarterTurns,int geometryVersion=1)
    {
        if(immersion is null)return false;var radius=geometryVersion==1?1:3;
        var water=Enumerable.Range(-radius,radius*2+1).SelectMany(x=>Enumerable.Range(-radius,radius*2+1).Select(z=>new GridCell(cell.X+x,cell.Z+z))).Append(WaterServiceCell(cell,quarterTurns,geometryVersion)).ToHashSet();
        return immersion.Vendors.Any(v=>ImmersionFootprint(v).Concat(LooseQueueGeometry.Corridor(VendorQueueCells(v,immersion))).Any(water.Contains));
    }
    private string? ImmersionPlacementError(ImmersionVendor proposed)
    {
        var terrain=new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        var reserved=new HashSet<GridCell>();
        foreach(var point in WaterPoints()) for(var x=point.Cell.X-3;x<=point.Cell.X+3;x++) for(var z=point.Cell.Z-3;z<=point.Cell.Z+3;z++) reserved.Add(new(x,z));
        foreach(var point in WaterPoints())foreach(var cell in LooseQueueGeometry.Corridor(CaptureWaterQueueCells(point.Id)))reserved.Add(cell);
        foreach(var vendor in _immersion!.Vendors.Where(v=>v.Id!=proposed.Id)) { foreach(var cell in ImmersionFootprint(vendor).Concat(LooseQueueGeometry.Corridor(VendorQueueCells(vendor,_immersion))))reserved.Add(cell); }
        for(var x=90;x<=101;x++)for(var z=140;z<=159;z++)reserved.Add(new(x,z));
        for(var x=113;x<=123;x++)for(var z=116;z<=127;z++)reserved.Add(new(x,z));
        if(_equipment is { } unit) { var centre=TraversalGrid.WorldToCell(unit.XMillimetres,unit.ZMillimetres);for(var x=centre.X-5;x<=centre.X+5;x++)for(var z=centre.Z-5;z<=centre.Z+5;z++)reserved.Add(new(x,z)); }
        if(_preparation!.WaterTowerOwned)for(var x=WaterTowerCell.X-4;x<=WaterTowerCell.X+4;x++)for(var z=WaterTowerCell.Z-4;z<=WaterTowerCell.Z+4;z++)reserved.Add(new(x,z));
        var needed=ImmersionFootprint(proposed).Append(ImmersionServiceCell(proposed)).ToArray();
        var otherCorridors=WaterPoints().SelectMany(point=>LooseQueueGeometry.Corridor(CaptureWaterQueueCells(point.Id))).Concat(ImmersionQueueCorridor(proposed.Id));
        var service=ImmersionServiceCell(proposed);
        if(otherCorridors.Any(cell=>Math.Abs(cell.X-service.X)<=1&&Math.Abs(cell.Z-service.Z)<=1))return "Vendor service overlaps an existing physical queue corridor.";
        if(needed.Any(c=>!terrain.Contains(c)||!terrain.Get(c).IsWalkable||reserved.Contains(c)))return "Vendor footprint or queue overlaps an obstacle, protected service or stage.";
        var blocked=terrain.Overrides.ToDictionary(p=>p.Key,p=>p.Value);
        foreach(var vendor in _immersion.Vendors.Where(v=>v.Id!=proposed.Id).Append(proposed))foreach(var cell in ImmersionFootprint(vendor))blocked[cell]=new(cell,GroundSurface.Grass,false);
        var grid=new TraversalGrid(blocked.Values);
        foreach(var cell in _immersion.Vendors.Where(v=>v.Id!=proposed.Id).Append(proposed).Select(ImmersionServiceCell).Append(MedicalRestCell))if(!DeterministicPathfinder.FindPath(grid,MedicalExitCell,cell).Found)return "Vendor blocks an essential approach.";
        return null;
    }
    private void BlockImmersionVendors()
    {
        if(_immersion is null||_traversalGrid is null)return;
        var terrain=_traversalGrid.Overrides.ToDictionary(p=>p.Key,p=>p.Value);
        foreach(var vendor in _immersion.Vendors)foreach(var cell in ImmersionFootprint(vendor))terrain[cell]=new(cell,GroundSurface.Grass,false);
        _traversalGrid=new TraversalGrid(terrain.Values);
    }
    private static ImmersionPerson NewImmersionPerson(ulong seed, EditionPerson person)
    {
        var stable = person.AgentId * 6364136223846793005UL + seed;
        stable=(stable^(stable>>30))*0xBF58476D1CE4E5B9UL;stable=(stable^(stable>>27))*0x94D049BB133111EBUL;stable^=stable>>31;
        // A bounded mixture spans £8..£25 near £15 without assigning social stereotypes.
        var budget = person.Role == ProtectedPersonRole.Guest ? stable%10<7 ? 800+(int)((stable/13)%1001) : 1800+(int)((stable/13)%701) : 1000;
        return new(person.AgentId, budget, 2500 + (int)(stable % 2001), stable % 4 == 0, (int)(stable % 101), (int)((stable / 13) % 101), (int)((stable / 71) % 101));
    }
    private static ImmersionSnapshot NewImmersion(ulong seed, EditionPerson[] people) => new(1, false, 0, 0, 0,
        [new("food", new(144, 119), 0, []), new("drinks", new(160, 120), 0, [])], people.Select(p => NewImmersionPerson(seed, p)).ToArray(), []);
    public static GameSession CreateImmersionCampaign(ulong seed)
    {
        var session = CreateTimetableCampaign(seed);
        session._immersion = NewImmersion(seed, session._preparation!.People);
        session._immersion=session._immersion with { Vendors=session._immersion.Vendors.Select(NewLooseVendor).ToArray() };
        session.SynchronizeImmersionPeople();
        foreach (var person in session._immersion.People) session._wallets[new(person.AgentId)].CashPennies = person.OpeningBudgetPennies;
        return session;
    }
    public bool ImmersionHandsAvailable(ulong id) => _immersion is not null && _preparation?.People.Any(p => p.AgentId == id && p.Admitted && !p.Departed) == true &&
        !(_livePerformance?.Performers.Any(p => p.AgentId == id && (p.OnStage || p.InstrumentAttached)) == true) &&
        !(MedicalOwnsNavigation(id) && !(ImmersionDepartureActive && !ImmersionDepartureJobOwns(id) && _medical!.Needs.Single(n=>n.AgentId==id).Intent==MedicalIntent.Leaving)) && !DisorderOwnsNavigation(id) && !InterventionOwnsTarget(id) && !InterventionOwnsWorker(id) &&
        !(_equipment is { WorkerId:{ } worker,JobStage:MaintenanceStage.Travelling or MaintenanceStage.Repairing } && worker==id) &&
        !GetMedicResponses().Any(j => MedicBusy(j) && j.WorkerId == id) && !GetStewardResponses().Any(j => StewardBusy(j) && j.WorkerId == id);
    private void SynchronizeImmersionPeople()
    {
        if (_immersion is null || _preparation is null) return;
        foreach (var person in _preparation.People.Where(person=>!_immersion.People.Any(p=>p.AgentId==person.AgentId)).ToArray()) { var p=NewImmersionPerson(CampaignSeed,person); _wallets[new(p.AgentId)].CashPennies=p.OpeningBudgetPennies; _immersion=_immersion with { People=_immersion.People.Append(p).OrderBy(n=>n.AgentId).ToArray() }; }
        if(_medical is not null) foreach(var person in _preparation.People.Where(p=>!_medical.Needs.Any(n=>n.AgentId==p.AgentId)).ToArray()) _medical=_medical with { Needs=_medical.Needs.Append(new MedicalNeed(person.AgentId,3000,2500,MedicalIntent.WatchShow,"Idle staff: food, soft drinks and free water available",-MedicalDecisionCooldownTicks,null,-1,MedicalNeedProfile.Staff)).OrderBy(n=>n.AgentId).ToArray() };
    }
    private bool ImmersionShoppingEligible(ulong id) => _preparation?.Status == PreparationStatus.Running && ImmersionHandsAvailable(id) && !IsCurrentProgrammePerformer(id) &&
        (_medical!.Needs.SingleOrDefault(p => p.AgentId == id) is null or { Intent: MedicalIntent.WatchShow, Thirst: < MedicalDistressThirst, HeatExposure: < MedicalDistressHeat });
    private bool ImmersionOwnsNavigation(ulong id) => _immersion?.People.Any(p => p.AgentId == id && p.VendorId is not null) == true;
    private bool ImmersionAwayFromCounters(ulong id)
    {
        var nav=_navigationAgents[new(id)];return _immersion!.Vendors.All(v=>{var centre=TraversalGrid.CellCentre(ImmersionServiceCell(v));var x=(long)nav.XMillimetres-centre.XMillimetres;var z=(long)nav.ZMillimetres-centre.ZMillimetres;return x*x+z*z>1000000;});
    }
    private void SetImmersionPerson(ImmersionPerson person) => _immersion = _immersion! with { People = _immersion.People.Select(p => p.AgentId == person.AgentId ? person : p).ToArray() };
    private void SetImmersionVendor(ImmersionVendor vendor) => _immersion = _immersion! with { Vendors = _immersion.Vendors.Select(v => v.Id == vendor.Id ? vendor : v).ToArray() };
    private int ImmersionStock(ImmersionProduct product) => product switch { ImmersionProduct.Chips => _immersion!.ChipsStock, ImmersionProduct.SoftDrink => _immersion!.SoftStock, _ => _immersion!.BeerStock };
    private bool ImmersionOrderEligible(ImmersionPerson p, ImmersionProduct product) => p.Held is null && ImmersionShoppingEligible(p.AgentId) && ImmersionStock(product) > 0 &&
        _wallets[new(p.AgentId)].CashPennies >= ImmersionPrice(product) && (product != ImmersionProduct.Beer || !p.Abstains && p.Intoxication < 7000 && _preparation!.People.Single(person => person.AgentId == p.AgentId).Role != ProtectedPersonRole.Staff);
    private CommandResult? ValidateImmersionCommand(EntityId? target, SessionCommand command)
    {
        if (target is not null || _immersion is null || _preparation?.Status != PreparationStatus.Preparing) return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Food and drink are prepared before opening.");
        if (command is PurchaseImmersionStarterStockCommand)
        {
            if (_immersion.StockPurchased) return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "Starter stock already purchased.");
            if (_festivalFinances[new(_preparation.FinanceOwnerId)].CashPennies < 9600) return CommandResult.Rejected(CommandReasonCode.InsufficientFunds, "Starter stock costs £96.");
        }
        if (command is PlaceImmersionVendorCommand c)
        {
            if (!_immersion.Vendors.Any(v => v.Id == c.VendorId) || c.QuarterTurns is < 0 or > 3 || !new TraversalGrid().Contains(c.Cell)) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Invalid vendor placement.");
            var vendor = new ImmersionVendor(c.VendorId, c.Cell, c.QuarterTurns, []);
            if (ImmersionPlacementError(vendor) is { } issue) return CommandResult.Rejected(CommandReasonCode.InvalidParameter,issue);
        }
        return null;
    }
    private void ApplyImmersionCommand(SessionCommand command)
    {
        if (command is PurchaseImmersionStarterStockCommand) { var owner=new EntityId(_preparation!.FinanceOwnerId); _festivalFinances[owner].CashPennies -= 9600; _immersion = _immersion! with { StockPurchased = true, ChipsStock = 40, SoftStock = 40, BeerStock = 32,StockPurchase=new($"immersion-stock:{CampaignId.Value}:{_preparation.Attempt}",_preparation.Attempt,CurrentTick,[new(owner,LedgerAccountType.CashAsset,-9600),new(owner,LedgerAccountType.InventoryAsset,9600)]) }; }
        if (command is PlaceImmersionVendorCommand c) SetImmersionVendor(NewLooseVendor(new(c.VendorId, c.Cell, c.QuarterTurns, [])));
    }
    private void LeaveImmersionQueue(ulong id, bool reroute)
    {
        var p = _immersion!.People.Single(p => p.AgentId == id);
        foreach (var vendor in _immersion.Vendors.Where(v => v.Queue.Contains(id) || v.OwnerId == id).ToArray()) SetImmersionVendor(vendor with { Queue = vendor.Queue.Where(q => q != id).ToArray(), OwnerId = vendor.OwnerId == id ? null : vendor.OwnerId, ServiceTicks = vendor.OwnerId == id ? 0 : vendor.ServiceTicks });
        SetImmersionPerson(p with { VendorId = null, Order = null, LastDecisionTick = CurrentTick });
        if (reroute) ReturnToListening(id);
    }
    private void CompleteImmersionSale(ulong id, ImmersionProduct product)
    {
        var p = _immersion!.People.Single(p => p.AgentId == id); var owner = new EntityId(_preparation!.FinanceOwnerId); var buyer = new EntityId(id);
        var price = ImmersionPrice(product); var cost = ImmersionCost(product); var transaction = $"immersion:{_preparation.Attempt}:{_immersion.Purchases.Length+1}";
        _wallets[buyer].CashPennies -= price; _festivalFinances[owner].CashPennies += price;
        var purchase = new ImmersionPurchase(transaction, id, product, CurrentTick, price, cost,
            [new(buyer, LedgerAccountType.CashAsset, -price), new(buyer, LedgerAccountType.GuestSpending, price), new(owner, LedgerAccountType.CashAsset, price), new(owner, LedgerAccountType.SalesRevenue, -price), new(owner, LedgerAccountType.CostOfGoodsSold, cost), new(owner, LedgerAccountType.InventoryAsset, -cost)]);
        _immersion = _immersion with { ChipsStock = _immersion.ChipsStock - (product == ImmersionProduct.Chips ? 1 : 0), SoftStock = _immersion.SoftStock - (product == ImmersionProduct.SoftDrink ? 1 : 0), BeerStock = _immersion.BeerStock - (product == ImmersionProduct.Beer ? 1 : 0), Purchases = _immersion.Purchases.Append(purchase).ToArray() };
        LeaveImmersionQueue(id, true); SetImmersionPerson(_immersion.People.Single(p => p.AgentId == id) with { Held = new(transaction, product, 0) });
    }
    public int ImmersionDecisionScore(ulong id, ImmersionProduct product, ImmersionVendor vendor)
    {
        var p = _immersion!.People.Single(p => p.AgentId == id); var thirst = _medical!.Needs.SingleOrDefault(n => n.AgentId == id)?.Thirst??p.StaffThirst; var nav = _navigationAgents[new(id)]; var from = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        var drive = product == ImmersionProduct.Chips ? p.Hunger + (p.Hunger >= 6000 ? 3500 : 0) : product == ImmersionProduct.SoftDrink ? thirst + p.SoftTaste*35 : 3000 + p.BeerTaste*45 + thirst/4;
        return drive - ImmersionPrice(product)*(2+p.PriceReluctance/25) - (Math.Abs(from.X-vendor.Cell.X)+Math.Abs(from.Z-vendor.Cell.Z))*30 - vendor.Queue.Length*400 - FestivalNeedMusicAppeal(id)/2;
    }
    private void AdvanceImmersion()
    {
        if (_immersion is null || !MedicalOperationsActive) return;
        if(!ImmersionDepartureActive)GrowImmersionQueues();
        foreach (var original in _immersion.People)
        {
            if (_preparation!.People.Single(person => person.AgentId == original.AgentId).Departed) continue;
            var p = original;
            var recovery = p.RecoveryResidue + 10; var hunger = p.HungerResidue + 12;
            p = p with { Intoxication = Math.Max(0,p.Intoxication-recovery/80), RecoveryResidue = recovery%80, Hunger = Math.Min(10000,p.Hunger+hunger/80), HungerResidue = hunger%80, FoodProtectionTicks = Math.Max(0,p.FoodProtectionTicks-1) };
            if(!_medical!.Needs.Any(n=>n.AgentId==p.AgentId)&&CurrentTick%4==0)p=p with { StaffThirst=Math.Min(10000,p.StaffThirst+1) };
            // Previously ingested dose keeps absorbing even when hands are owned by stage or care.
            if (p.PendingDose > 0) { var absorb = Math.Min(1,p.PendingDose); var residue = p.AbsorptionResidue + absorb*(p.FoodProtectionTicks>0 ? 1 : 2); p = p with { PendingDose = p.PendingDose-absorb, Intoxication = Math.Min(10000,p.Intoxication+residue/2), AbsorptionResidue = residue%2 }; }
            if (p.Held is { } held && ImmersionHandsAvailable(p.AgentId) && !ImmersionOwnsNavigation(p.AgentId) && ImmersionAwayFromCounters(p.AgentId))
            {
                var duration = ImmersionConsumeTicks(held.Product); var elapsed = held.ConsumedTicks+1;
                var effect = held.Product == ImmersionProduct.Chips ? 5500 : held.Product == ImmersionProduct.SoftDrink ? 6000 : 1500;
                var delta = elapsed*effect/duration-held.ConsumedTicks*effect/duration;
                if (held.Product == ImmersionProduct.Chips) p = p with { Hunger = Math.Max(0,p.Hunger-delta), FoodProtectionTicks = 4800 };
                else if (_medical!.Needs.Any(n=>n.AgentId==p.AgentId)) SetNeed(p.AgentId,n => n with { Thirst = Math.Max(0,n.Thirst-delta) });
                else p=p with { StaffThirst=Math.Max(0,p.StaffThirst-delta) };
                if (held.Product == ImmersionProduct.Beer) p = p with { PendingDose = p.PendingDose + elapsed*2400/duration-held.ConsumedTicks*2400/duration };
                var enjoyment = held.Product == ImmersionProduct.Chips ? 100 : held.Product == ImmersionProduct.SoftDrink ? 75 : 150*p.BeerTaste/100;
                var gain = elapsed*enjoyment/duration-held.ConsumedTicks*enjoyment/duration;
                _preparation = _preparation with { People = _preparation.People.Select(person => person.AgentId == p.AgentId ? person with { Satisfaction = Math.Min(10000,person.Satisfaction+gain) } : person).ToArray() };
                p = p with { Held = elapsed == duration ? null : held with { ConsumedTicks = elapsed } };
            }
            if (p.Intoxication >= 7500 && p.WarningTick < 0) { p = p with { WarningTick = CurrentTick }; MedicalEvent("intoxication:warning", $"Person {p.AgentId}: heavy intoxication {p.Intoxication}; no further beer served, water, rest and medic available."); }
            p = p with { SevereTicks = p.Intoxication >= 8500 ? p.SevereTicks+1 : 0 };
            SetImmersionPerson(p);
            if(!ImmersionDepartureActive && _medical!.Needs.Single(n=>n.AgentId==p.AgentId) is { Profile:MedicalNeedProfile.Staff,Thirst:>=MedicalDistressThirst,Intent:MedicalIntent.WatchShow } && ImmersionHandsAvailable(p.AgentId)) { if(p.VendorId is not null)LeaveImmersionQueue(p.AgentId,false);SeekWater(p.AgentId,"Urgent staff thirst: free water precedes shopping");continue; }
            if (p.CollapseTick>=0 && _medical!.Needs.SingleOrDefault(n=>n.AgentId==p.AgentId) is { } collapsed)
            {
                if(collapsed.Stage==MedicalStage.Collapsed && CurrentTick>=p.CollapseTick+MedicalCriticalDelayTicks) { SetNeed(p.AgentId,n=>n with { Stage=MedicalStage.Critical,CriticalTick=CurrentTick }); MedicalEvent("intoxication:critical",$"Person {p.AgentId}: untreated intoxication collapse; physical response deadline remains."); }
                if(CurrentTick>=p.CollapseTick+MedicalDeathDelayTicks && _medical.Needs.Single(n=>n.AgentId==p.AgentId).Stage==MedicalStage.Critical) { var need=_medical.Needs.Single(n=>n.AgentId==p.AgentId);ApplyMedicalDeath(p.AgentId,p.WarningTick,p.CollapseTick,need.CriticalTick);return; }
            }
            if (p.SevereTicks >= 1600 && p.WarningTick >= 0 && p.CollapseTick < 0 && !ExistingMedicalHazardOwns(p.AgentId) && _medical!.Needs.Any(n=>n.AgentId==p.AgentId))
            {
                var need = _medical.Needs.Single(n=>n.AgentId==p.AgentId);
                p=p with { CollapseTick=CurrentTick, PriorMedicalStage=p.AgentId==_medical.AtRiskGuestId?_medical.Stage:need.Stage }; SetImmersionPerson(p);
                LeaveImmersionQueue(p.AgentId,false); LeaveWater(p.AgentId,"Intoxication collapse",false); MedicalRelinquishPerformerStage(p.AgentId);
                var nav=_navigationAgents[new(p.AgentId)]; ApplyAgentDestination(new(p.AgentId),new(TraversalGrid.WorldToCell(nav.XMillimetres,nav.ZMillimetres),"medical.intoxication-collapse"));
                SetNeed(p.AgentId,n=>n with { Stage=MedicalStage.Collapsed,WarningTick=p.WarningTick,CollapseTick=CurrentTick,Intent=MedicalIntent.Collapsed,Reason="Intoxication collapse; physical medic response required" });
                if (p.AgentId==_medical.AtRiskGuestId) _medical=_medical with { Stage=MedicalStage.Collapsed,WarningTick=p.WarningTick,CollapseTick=CurrentTick };
                MedicalEvent("intoxication:collapse",$"Person {p.AgentId}: exposure continuously above8500 for20s after warning {p.WarningTick}; critical and fatal response deadlines begin now.");
            }
            if (p.VendorId is not null && (p.Order is not { } order || !ImmersionOrderEligible(p,order))) { LeaveImmersionQueue(p.AgentId,ImmersionShoppingEligible(p.AgentId)); continue; }
            if (p.Held is null && p.VendorId is null && CurrentTick%80 == (long)(p.AgentId%80) && CurrentTick-p.LastDecisionTick >= 800 && ImmersionShoppingEligible(p.AgentId))
            {
                SetImmersionPerson(p with { LastDecisionTick = CurrentTick });
                var choices = Enum.GetValues<ImmersionProduct>().Where(product => ImmersionOrderEligible(p,product)).Select(product => (Product:product, Vendor:_immersion.Vendors.Single(v=>v.Id==(product == ImmersionProduct.Chips ? "food":"drinks")))).Where(c=>_immersion.People.Count(n=>n.VendorId==c.Vendor.Id)<10 && (c.Vendor.QueueCells is null || c.Vendor.QueueCells.Length>_immersion.People.Count(n=>n.VendorId==c.Vendor.Id))).Select(c=>(c.Product,c.Vendor,Score:ImmersionDecisionScore(p.AgentId,c.Product,c.Vendor))).OrderByDescending(c=>c.Score).ToArray();
                if (choices.Length>0 && choices[0].Score>1000 && (choices[0].Product != ImmersionProduct.Chips || p.Hunger>=6000) && MedicalRouteExists(p.AgentId,ImmersionQueueCell(choices[0].Vendor,choices[0].Vendor.Queue.Length)))
                { var choice = choices[0]; SetImmersionPerson(_immersion.People.Single(n=>n.AgentId==p.AgentId) with { VendorId=choice.Vendor.Id,Order=choice.Product }); ApplyAgentDestination(new(p.AgentId),new(ImmersionQueueCell(choice.Vendor,choice.Vendor.Queue.Length),"immersion.approach")); }
            }
        }
        if (ImmersionDepartureActive) return;
        foreach (var original in _immersion.Vendors)
        {
            var vendor = original;
            foreach (var p in _immersion.People.Where(p=>p.VendorId==vendor.Id && !vendor.Queue.Contains(p.AgentId)).OrderBy(p=>p.AgentId).ToArray())
            { var nav = _navigationAgents[new(p.AgentId)]; if (vendor.Queue.Length<10 && nav.Action==AgentNavigationAction.Arrived && nav.IntentId=="immersion.approach") vendor=vendor with { Queue=vendor.Queue.Append(p.AgentId).ToArray() }; }
            var approachers=_immersion.People.Where(p=>p.VendorId==vendor.Id&&!vendor.Queue.Contains(p.AgentId)).OrderBy(p=>p.AgentId).ToArray();
            for(var i=0;i<approachers.Length;i++){var cell=ImmersionQueueCell(vendor,Math.Min(14,vendor.Queue.Length+i));var id=approachers[i].AgentId;if(_navigationAgents[new(id)].Destination!=cell)ApplyAgentDestination(new(id),new(cell,"immersion.approach"));}
            for (var i=0;i<vendor.Queue.Length;i++) { var id=vendor.Queue[i]; var cell=ImmersionQueueCell(vendor,i); if (_navigationAgents[new(id)].Destination!=cell) ApplyAgentDestination(new(id),new(cell,"immersion.queue")); }
            if (vendor.OwnerId is null && vendor.Queue.Length>0 && _navigationAgents[new(vendor.Queue[0])].Action==AgentNavigationAction.Arrived && _navigationAgents[new(vendor.Queue[0])].Destination==ImmersionServiceCell(vendor)) vendor=vendor with { OwnerId=vendor.Queue[0],ServiceTicks=ImmersionServiceDuration(_immersion.People.Single(p=>p.AgentId==vendor.Queue[0]).Order!.Value) };
            if (vendor.OwnerId is { } owner) { vendor=vendor with { ServiceTicks=vendor.ServiceTicks-1 }; SetImmersionVendor(vendor); if (vendor.ServiceTicks==0) { var p=_immersion.People.Single(p=>p.AgentId==owner); if (p.Order is { } product && ImmersionOrderEligible(p,product)) CompleteImmersionSale(owner,product); else LeaveImmersionQueue(owner,true); } }
            else SetImmersionVendor(vendor);
        }
    }
    private bool IntoxicationWarning(ulong id) => _immersion?.People.Any(p=>p.AgentId==id && p.WarningTick>=0 && (p.Intoxication>=7500 || p.CollapseTick>=0 || p.CareTicks>0))==true;
    private bool ExistingMedicalHazardOwns(ulong id) => _medical is { } medical &&
        (medical.Needs.Any(n=>n.AgentId==id&&n.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical) ||
         medical.AtRiskGuestId==id&&medical.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical ||
         _disorder?.People.Any(p=>p.AgentId==id&&p.Stage==DisorderStage.Injured)==true);
    private bool IntoxicationCareOwns(MedicResponse job)
    {
        if(job.PatientId is not { } id || !IntoxicationWarning(id) || _immersion is null || _medical is null)return false;
        var p=_immersion.People.Single(p=>p.AgentId==id);var need=_medical.Needs.Single(n=>n.AgentId==id);
        return !(p.CollapseTick<0&&ExistingMedicalHazardOwns(id) || p.CollapseTick>=0&&(need.Stage is MedicalStage.Collapsed or MedicalStage.Critical)&&need.CollapseTick!=p.CollapseTick || _disorder?.People.Any(person=>person.AgentId==id&&person.Stage==DisorderStage.Injured)==true);
    }
    private bool AdvanceIntoxicationCare(MedicResponse job)
    {
        if (!IntoxicationCareOwns(job)) return false;
        var id=job.PatientId!.Value;var p=_immersion!.People.Single(p=>p.AgentId==id);
        var need=_medical!.Needs.Single(n=>n.AgentId==id);
        // Existing heat/injury ownership keeps its own causal treatment path and deadlines.
        if(p.CareTicks==0 && p.CollapseTick<0)p=p with { PriorMedicalStage=id==_medical.AtRiskGuestId?_medical.Stage:need.Stage };
        // Only the physical treating owner calls this; neither dispatch nor water sobers.
        var care=p.CareTicks+1; p=p with { CareTicks=care,Intoxication=Math.Max(0,p.Intoxication-4) }; SetImmersionPerson(p);
        if (care<1600 || p.Intoxication>=7500) return true;
        var restored=p.PriorMedicalStage==MedicalStage.Treated?MedicalStage.Treated:MedicalStage.Clear;
        SetImmersionPerson(p with { WarningTick=-1,CollapseTick=-1,SevereTicks=0,CareTicks=0 });
        SetNeed(id,n=>n with { Stage=restored,Intent=MedicalIntent.WatchShow,Reason="Gradual intoxication stabilization completed; future exposure can warn again",WarningTick=-1,CollapseTick=-1,CriticalTick=-1 });
        if (id==_medical!.AtRiskGuestId) _medical=_medical with { Stage=restored,WarningTick=-1,CollapseTick=-1,CriticalTick=-1 };
        SetMedicResponse(job with { Stage=MedicalResponseStage.Completed,Description="Gradual intoxication stabilization completed" }); ReturnToListening(id);
        MedicalEvent("intoxication:treatment-complete",$"Physical medic {job.WorkerId} stabilized {id} gradually over20s; exposure remains {p.Intoxication}."); return true;
    }
    private int ImmersionAggression(ulong id,int temperament) => _immersion?.People.SingleOrDefault(p=>p.AgentId==id) is { } person ? Math.Clamp(person.Intoxication*temperament/25000000,0,2) : 0;
    private int ImmersionCoordinationPace(ulong id) => _immersion?.People.SingleOrDefault(p=>p.AgentId==id) is { Intoxication:>=5000 } person && _preparation?.People.Single(p=>p.AgentId==id).Role==ProtectedPersonRole.Guest && !MedicalOwnsNavigation(id) && !DisorderOwnsNavigation(id) && !InterventionOwnsTarget(id) ? Math.Max(800,1000-(person.Intoxication-5000)/25) : 1000;
    public bool ImmersionBoundaryOnNextTick => !IsPaused && MedicalOperationsActive && _immersion is { } m &&
        (m.Vendors.Any(v=>v.OwnerId is not null && v.ServiceTicks<=1) || m.People.Any(p=>!_preparation!.People.Single(person=>person.AgentId==p.AgentId).Departed && (p.WarningTick<0 && p.Intoxication>=7499 || p.SevereTicks>=1599 && p.Intoxication>=8500 && p.WarningTick>=0 && p.CollapseTick<0 && !ExistingMedicalHazardOwns(p.AgentId) || p.CollapseTick>=0 && (CurrentTick+1==p.CollapseTick+MedicalCriticalDelayTicks || CurrentTick+1==p.CollapseTick+MedicalDeathDelayTicks))));
    private bool IntoxicationCareBoundary(MedicResponse job) => IntoxicationCareOwns(job) && _immersion!.People.Single(p=>p.AgentId==job.PatientId).CareTicks>=1599;
    private void CleanupImmersionDeparture()
    {
        if(_immersion is null||_preparation is not { Status:PreparationStatus.Departing or PreparationStatus.Finished } p)return;
        _immersion=_immersion with { Vendors=_immersion.Vendors.Select(v=>v with { Queue=[],OwnerId=null,ServiceTicks=0 }).ToArray(),People=_immersion.People.Select(person=>person with { VendorId=null,Order=null,Held=p.People.Single(n=>n.AgentId==person.AgentId).Departed?null:person.Held }).ToArray() };
    }
    private static string? ValidatePersistedImmersion(SessionPersistenceSnapshot s)
    {
        if (s.Immersion is not { } m) return null;
        if (m.Version!=1 || s.Programme is null || s.Preparation is not { } prep || m.People is null || m.Vendors is null || m.Purchases is null || m.People.Any(p=>p is null) || m.Vendors.Any(v=>v is null || v.Queue is null) || m.Purchases.Any(p=>p is null || p.Entries is null)) return "Immersion version or collections invalid.";
        if(!m.People.Select(p=>p.AgentId).SequenceEqual(prep.People.Select(p=>p.AgentId)))return "Immersion protected person identities invalid.";
        if(m.Vendors.Length!=2 || !m.Vendors.Select(v=>v.Id).Order().SequenceEqual(new[]{"drinks","food"}) || m.Vendors.Any(v=>v.QuarterTurns is <0 or >3))return "Immersion vendor identities invalid.";
        var geometry=CreateImmersionCampaign(s.CampaignSeed);geometry._immersion=m;geometry._preparation=prep;geometry._medical=s.Medical;geometry._equipment=s.Equipment;
        foreach(var vendor in m.Vendors)if(geometry.ImmersionPlacementError(vendor) is { } issue)return issue;
        var queueGrid=s.TraversalGrid is { } savedTerrain?new TraversalGrid(savedTerrain.Cells.Select(c=>new TerrainCellOverride(new(c.X,c.Z),(GroundSurface)c.Surface,c.IsWalkable))):new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        foreach(var vendor in m.Vendors.Where(v=>v.QueueCells is not null))
        {
            var cells=vendor.QueueCells!;var members=m.People.Count(person=>person.VendorId==vendor.Id);
            var reserved=geometry.WaterPoints().SelectMany(point=>LooseQueueGeometry.Corridor(geometry.CaptureWaterQueueCells(point.Id))).Concat(geometry.ImmersionQueueCorridor(vendor.Id)).ToArray();
            if(cells.Length is <1 or >10 || cells.Length<members || cells[0]!=ImmersionServiceCell(vendor) || LooseQueueGeometry.Corridor(cells).Any(cell=>!QueueGroundAllowed(cell)) || !LooseQueueGeometry.Valid(cells,RotateWaterOffset(new(0,1),vendor.QuarterTurns),queueGrid,reserved))return "Immersion saved loose queue geometry invalid.";
        }
        if(prep.Status!=PreparationStatus.Preparing && (s.TraversalGrid is null || m.Vendors.SelectMany(ImmersionFootprint).Any(cell=>!s.TraversalGrid.Cells.Any(c=>c.X==cell.X&&c.Z==cell.Z&&!c.IsWalkable))))return "Immersion vendor solid footprint absent from saved traversal.";
        var festival=new EntityId(prep.FinanceOwnerId);
        if(m.StockPurchased!=(m.StockPurchase is not null) || m.StockPurchase is { } purchased && (purchased.Id!=$"immersion-stock:{s.CampaignId}:{prep.Attempt}" || purchased.Attempt!=prep.Attempt || purchased.Tick<0 || purchased.Tick>s.CurrentTick || purchased.Entries is null || !purchased.Entries.SequenceEqual(new LedgerEntry[]{new(festival,LedgerAccountType.CashAsset,-9600),new(festival,LedgerAccountType.InventoryAsset,9600)})))return "Immersion starter stock purchase ledger invalid.";
        foreach(var purchase in m.Purchases)
        {
            var buyer=new EntityId(purchase.AgentId);var price=ImmersionPrice(purchase.Product);var cost=ImmersionCost(purchase.Product);
            if(!purchase.Entries.SequenceEqual(new LedgerEntry[]{new(buyer,LedgerAccountType.CashAsset,-price),new(buyer,LedgerAccountType.GuestSpending,price),new(festival,LedgerAccountType.CashAsset,price),new(festival,LedgerAccountType.SalesRevenue,-price),new(festival,LedgerAccountType.CostOfGoodsSold,cost),new(festival,LedgerAccountType.InventoryAsset,-cost)}))return "Immersion sale ledger accounts/owners invalid.";
            if(purchase.Product==ImmersionProduct.Beer && m.People.Any(p=>p.AgentId==purchase.AgentId && (p.Abstains||prep.People.Single(n=>n.AgentId==p.AgentId).Role==ProtectedPersonRole.Staff)))return "Immersion beer eligibility invalid.";
        }
        if(m.Purchases.Where((purchase,index)=>purchase.Id!=$"immersion:{prep.Attempt}:{index+1}" || m.StockPurchase is null || purchase.Tick<m.StockPurchase.Tick || index>0&&purchase.Tick<m.Purchases[index-1].Tick).Any())return "Immersion sale immutable identity/order invalid.";
        if(m.People.Any(p=>p.StaffThirst is <0 or >10000 || p.CareTicks<0 || !Enum.IsDefined(p.PriorMedicalStage) || p.CollapseTick>s.CurrentTick || p.CollapseTick>=0 && (p.WarningTick<0 || p.CollapseTick<p.WarningTick+1599) || p.Order==ImmersionProduct.Beer && (p.Abstains||prep.People.Single(n=>n.AgentId==p.AgentId).Role==ProtectedPersonRole.Staff)) || m.Vendors.Any(v=>v.Queue.Length>10 || v.OwnerId is { } owner && (!m.People.Any(p=>p.AgentId==owner && p.Order is { } order && v.ServiceTicks<=ImmersionServiceDuration(order)) || s.NavigationAgents?.SingleOrDefault(n=>n.Id==owner) is not { Action:(int)AgentNavigationAction.Arrived } nav || nav.DestinationX!=ImmersionServiceCell(v).X || nav.DestinationZ!=ImmersionServiceCell(v).Z)))return "Immersion cause/role/physical service owner invalid.";
        if(prep.Status is PreparationStatus.Departing or PreparationStatus.Finished && (m.Vendors.Any(v=>v.Queue.Length>0||v.OwnerId is not null)||m.People.Any(p=>p.VendorId is not null||prep.People.Single(n=>n.AgentId==p.AgentId).Departed&&p.Held is not null)))return "Immersion departure ownership invalid.";
        if (!m.People.Select(p=>p.AgentId).SequenceEqual(prep.People.Select(p=>p.AgentId)) || m.Vendors.Length!=2 || !m.Vendors.Select(v=>v.Id).Order().SequenceEqual(new[]{"drinks","food"}) || m.Vendors.Any(v=>v.QuarterTurns is <0 or >3 || !new TraversalGrid().Contains(v.Cell) || v.ServiceTicks<0 || v.OwnerId is { } id && (v.Queue.Length==0 || v.Queue[0]!=id) || v.OwnerId is null && v.ServiceTicks!=0) || m.Vendors.SelectMany(v=>v.Queue).Distinct().Count()!=m.Vendors.Sum(v=>v.Queue.Length)) return "Immersion vendor identity/ownership invalid.";
        foreach (var p in m.People) { var stable=NewImmersionPerson(s.CampaignSeed,prep.People.Single(n=>n.AgentId==p.AgentId)); if (p.OpeningBudgetPennies!=stable.OpeningBudgetPennies || p.Abstains!=stable.Abstains || p.BeerTaste!=stable.BeerTaste || p.SoftTaste!=stable.SoftTaste || p.PriceReluctance!=stable.PriceReluctance || p.Hunger is <0 or >10000 || p.Intoxication is <0 or >10000 || p.PendingDose is <0 or >2400 || p.FoodProtectionTicks is <0 or >4800 || p.HungerResidue is <0 or >79 || p.RecoveryResidue is <0 or >79 || p.AbsorptionResidue is <0 or >1 || p.LastDecisionTick>s.CurrentTick || p.WarningTick>s.CurrentTick || p.SevereTicks<0 || (p.VendorId is null)!=(p.Order is null) || p.VendorId is not null && !m.Vendors.Any(v=>v.Id==p.VendorId) || p.Order is { } order && !Enum.IsDefined(order) || p.Held is { } h && (!Enum.IsDefined(h.Product) || h.ConsumedTicks<0 || h.ConsumedTicks>=ImmersionConsumeTicks(h.Product) || p.VendorId is not null || !m.Purchases.Any(t=>t.Id==h.TransactionId && t.AgentId==p.AgentId && t.Product==h.Product)) || s.Wallets.Single(w=>w.OwnerId==p.AgentId).CashPennies!=p.OpeningBudgetPennies-m.Purchases.Where(t=>t.AgentId==p.AgentId).Sum(t=>t.PricePennies)) return "Immersion person, wallet or retained item invalid."; }
        if (m.Purchases.Select(p=>p.Id).Distinct().Count()!=m.Purchases.Length || m.Purchases.Any(p=>!Enum.IsDefined(p.Product) || !m.People.Any(n=>n.AgentId==p.AgentId) || p.PricePennies!=ImmersionPrice(p.Product) || p.CostPennies!=ImmersionCost(p.Product) || p.Tick<0 || p.Tick>s.CurrentTick || p.Entries.Sum(e=>e.AmountPennies)!=0) || m.ChipsStock!=(m.StockPurchased?40:0)-m.Purchases.Count(p=>p.Product==ImmersionProduct.Chips) || m.SoftStock!=(m.StockPurchased?40:0)-m.Purchases.Count(p=>p.Product==ImmersionProduct.SoftDrink) || m.BeerStock!=(m.StockPurchased?32:0)-m.Purchases.Count(p=>p.Product==ImmersionProduct.Beer) || Math.Min(m.ChipsStock,Math.Min(m.SoftStock,m.BeerStock))<0 || m.Vendors.Any(v=>v.Queue.Any(id=>!m.People.Any(p=>p.AgentId==id && p.VendorId==v.Id)))) return "Immersion stock/transactions/FIFO invalid.";
        return null;
    }
}
