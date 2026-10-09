namespace Festival.Simulation;

public sealed record FestivalAccountsSale(ImmersionProduct Product, int UnitPricePennies, int Quantity, long AmountPennies);
public sealed record FestivalAccountsCost(string Label, int Quantity, int UnitCostPennies, long AmountPennies);
public sealed record FestivalAccountsExpense(string Category, string Label, long AmountPennies);
public sealed record FestivalAccountsStock(string Label, int Quantity, int UnitCostPennies, long AmountPennies);
public sealed record FestivalAccounts(
    FestivalAccountsSale[] Sales, FestivalAccountsCost[] SoldItemCosts, FestivalAccountsExpense[] OperatingExpenses,
    FestivalAccountsStock[] PurchasedStock, int TicketsSold, int TicketPricePennies,
    long OpeningCashPennies, long ClosingCashPennies, long StockPurchasesPennies, long CapitalPurchasesPennies,
    bool StockPurchaseRecorded, bool StockDetailRecorded, bool FacilityDetailRecorded, bool Reconciles)
{
    /// <summary>Advance ticket sales: received before preparation, as part of the opening budget.</summary>
    public long TicketSalesPennies => (long)TicketsSold * TicketPricePennies;
    /// <summary>What the food trader paid to pitch, and who they were.</summary>
    public long PitchFeePennies { get; init; }
    public string PitchFeeTrader { get; init; } = "";
    /// <summary>The cash the previous festival closed on, from Tier 2; null at Tier 1, where the opening cash is the starter loan.</summary>
    public long? CarriedInPennies { get; init; }
    /// <summary>The loan principal still owed at the close. Settlement is parked, so nothing has been repaid.</summary>
    public long DebtOwedPennies { get; init; }
    public long IncomePennies => TicketSalesPennies + PitchFeePennies + Sales.Sum(line => line.AmountPennies);
    public long SoldItemCostPennies => SoldItemCosts.Sum(line => line.AmountPennies);
    public long OperatingExpensesPennies => OperatingExpenses.Sum(line => line.AmountPennies);
    public long OperatingResultPennies => IncomePennies - SoldItemCostPennies - OperatingExpensesPennies;
    public long NetCashChangePennies => ClosingCashPennies - OpeningCashPennies;
}

public sealed partial class GameSession
{
    /// <summary>What it's called on a receipt or in a sentence: "Chips", "Free water".</summary>
    public static string ProductName(ImmersionProduct product) => product switch
    {
        ImmersionProduct.Chips => "Chips",
        ImmersionProduct.Pizza => "Pizza",
        ImmersionProduct.SoftDrink => "Soft drink",
        ImmersionProduct.Beer => "Beer",
        ImmersionProduct.Water => "Free water",
        _ => throw new ArgumentOutOfRangeException(nameof(product))
    };
    private static string FacilityName(BuildServiceKind kind) => kind switch
    {
        BuildServiceKind.Bin => "Litter bin",
        BuildServiceKind.WaterTap => "Water tap",
        BuildServiceKind.Toilet => "Toilet",
        BuildServiceKind.FoodVan => "Food van",
        BuildServiceKind.Bar => "Bar",
        BuildServiceKind.FirstAid => "First aid",
        BuildServiceKind.StewardPost => "Steward post",
        _ => kind.ToString()
    };

    public FestivalAccounts? CompletedFestivalAccounts
    {
        get
        {
            if (_preparation is not { Result: { } result } p) return null;
            // The food trader's sales are their own takings; the festival's are the bar's.
            var purchases = (_immersion?.Purchases ?? []).Where(purchase => !purchase.Product.IsFood()).ToArray();
            var sales = purchases.GroupBy(purchase => (purchase.Product, purchase.PricePennies))
                .OrderBy(group => group.Key.Product).ThenByDescending(group => group.Key.PricePennies)
                .Select(group => new FestivalAccountsSale(group.Key.Product, group.Key.PricePennies, group.Count(),
                    group.Sum(purchase => (long)purchase.PricePennies))).ToArray();
            var costs = purchases.GroupBy(purchase => (purchase.Product, purchase.CostPennies))
                .OrderBy(group => group.Key.Product).ThenBy(group => group.Key.CostPennies)
                .Select(group => new FestivalAccountsCost(ProductName(group.Key.Product), group.Count(), group.Key.CostPennies,
                    group.Sum(purchase => (long)purchase.CostPennies))).ToList();
            if (p.StockConsumed > 0)
                costs.Add(new("Other recorded refreshments", p.StockConsumed, 60, p.StockConsumed * 60L));

            var offers = GetPreparationOffers().ToDictionary(offer => offer.Id);
            var payments = p.Payments.Where(payment => payment.Attempt == p.Attempt).ToArray();
            var expenses = payments.Where(payment => payment.DebitAccount == LedgerAccountType.AdministrationExpense)
                .Select(payment => new FestivalAccountsExpense(
                    payment.OfferId.StartsWith("act.", StringComparison.Ordinal) ? "Act bookings" : "Staff and rental",
                    offers.TryGetValue(payment.OfferId, out var offer) ? offer.Name.Split('•')[0].Trim() : payment.OfferId,
                    payment.AmountPennies)).ToList();
            var setup = p.SetupPayments?.SingleOrDefault(payment => payment.Attempt == p.Attempt);
            var buildCost = setup?.BuildCostPennies ?? 0;
            var facilityDetailRecorded = true;
            if (buildCost > 0)
            {
                var facilities = p.BuildPlacements is not null
                    ? p.BuildPlacements.GroupBy(placement => placement.Kind).OrderBy(group => group.Key)
                        .Where(group => BuildServiceFeePennies(group.Key) > 0)
                        .Select(group => new FestivalAccountsExpense("Facilities",
                            $"{FacilityName(group.Key)} × {group.Count()}", group.Count() * (long)BuildServiceFeePennies(group.Key))).ToArray()
                    : [];
                facilityDetailRecorded = facilities.Sum(line => line.AmountPennies) == buildCost;
                if (facilityDetailRecorded) expenses.AddRange(facilities);
                else expenses.Add(new("Facilities", "Facilities (recorded total)", buildCost));
            }

            if (_immersion is { FreeWaterChargeTicks.Length: > 0 } water)
                expenses.Add(new("Emergency measures", $"Free water at the bar × {water.FreeWaterChargeTicks.Length}", FreeWaterSpend(water)));
            if (_lavSucker is { Calls.Length: > 0 } lav)
                expenses.Add(new("Emergency measures", $"Dav's Lav-Sucker × {lav.Calls.Length}", LavSuckerSpend(lav)));
            var owner = new EntityId(p.FinanceOwnerId);
            var stockPurchase = _immersion?.StockPurchase;
            var stockRecorded = stockPurchase is not null || p.Plan is { Committed: true };
            var stockCash = stockPurchase is { Attempt: var attempt } && attempt == p.Attempt
                ? stockPurchase.Entries.Where(entry => entry.OwnerId == owner && entry.Account == LedgerAccountType.CashAsset)
                    .Sum(entry => -entry.AmountPennies) : 0;
            var purchasedStock = p.Plan is { Committed: true } plan
                ? new FestivalAccountsStock[] {
                    new("Soft drink", plan.SoftDrinks, ImmersionCost(ImmersionProduct.SoftDrink), plan.SoftDrinks * (long)ImmersionCost(ImmersionProduct.SoftDrink)),
                    new("Beer", plan.Beers, ImmersionCost(ImmersionProduct.Beer), plan.Beers * (long)ImmersionCost(ImmersionProduct.Beer))
                }.Where(line => line.Quantity > 0).ToArray() : [];
            var stockDetailRecorded = p.Plan is { Committed: true } && purchasedStock.Sum(line => line.AmountPennies) == stockCash;
            var capital = payments.Where(payment => payment.DebitAccount == LedgerAccountType.EquipmentAsset)
                .Sum(payment => (long)payment.AmountPennies);
            var closing = _festivalFinances[owner].CashPennies;
            // Opening cash here is the loan (Tier 1) or the cash carried in (later tiers): the ticket money in the
            // opening budget is this festival's income.
            var tickets = FestivalTickets.RevenuePennies(p.Tier);
            var accounts = new FestivalAccounts(sales, costs.ToArray(), expenses.ToArray(), purchasedStock,
                FestivalTickets.Sold(p.Tier), FestivalTickets.PricePennies(p.Tier), p.OpeningCashPennies - tickets,
                closing, stockCash, capital, stockRecorded, stockDetailRecorded, facilityDetailRecorded, false)
                { PitchFeePennies = ReceivedPitchFee(p), PitchFeeTrader = FoodTraders.Find(p.Plan?.TraderId)?.Name ?? "",
                  CarriedInPennies = p.CarriedIn?.CashPennies, DebtOwedPennies = _campaignPlanning?.Loan.OutstandingPrincipalPennies ?? 0 };
            var reconciles = accounts.IncomePennies == result.RevenuePennies &&
                accounts.SoldItemCostPennies == result.ConsumedStockCostsPennies &&
                accounts.OperatingExpensesPennies == result.ContractCostsPennies &&
                accounts.CapitalPurchasesPennies == result.CapitalPurchasesPennies &&
                accounts.NetCashChangePennies == result.NetCashChangePennies &&
                closing == accounts.OpeningCashPennies + accounts.IncomePennies - accounts.OperatingExpensesPennies -
                    accounts.StockPurchasesPennies - accounts.CapitalPurchasesPennies;
            return accounts with { Reconciles = reconciles };
        }
    }
}
