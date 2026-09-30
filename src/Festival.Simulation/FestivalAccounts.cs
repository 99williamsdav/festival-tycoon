namespace Festival.Simulation;

public sealed record FestivalAccountsSale(ImmersionProduct Product, int UnitPricePennies, int Quantity, long AmountPennies);
public sealed record FestivalAccountsCost(string Label, int Quantity, int UnitCostPennies, long AmountPennies);
public sealed record FestivalAccountsExpense(string Category, string Label, long AmountPennies);
public sealed record FestivalAccountsStock(string Label, int Quantity, int UnitCostPennies, long AmountPennies);
public sealed record FestivalAccounts(
    FestivalAccountsSale[] Sales, FestivalAccountsCost[] SoldItemCosts, FestivalAccountsExpense[] OperatingExpenses,
    FestivalAccountsStock[] PurchasedStock,
    long OpeningCashPennies, long ClosingCashPennies, long StockPurchasesPennies, long CapitalPurchasesPennies,
    bool StockPurchaseRecorded, bool StockDetailRecorded, bool FacilityDetailRecorded, bool Reconciles)
{
    public long IncomePennies => Sales.Sum(line => line.AmountPennies);
    public long SoldItemCostPennies => SoldItemCosts.Sum(line => line.AmountPennies);
    public long OperatingExpensesPennies => OperatingExpenses.Sum(line => line.AmountPennies);
    public long OperatingResultPennies => IncomePennies - SoldItemCostPennies - OperatingExpensesPennies;
    public long NetCashChangePennies => ClosingCashPennies - OpeningCashPennies;
}

public sealed partial class GameSession
{
    private static string ProductName(ImmersionProduct product) => product switch
    {
        ImmersionProduct.Chips => "Chips",
        ImmersionProduct.SoftDrink => "Soft drink",
        ImmersionProduct.Beer => "Beer",
        _ => product.ToString()
    };
    private static string FacilityName(BuildServiceKind kind) => kind switch
    {
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
            var purchases = _immersion?.Purchases ?? [];
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
                        .Select(group => new FestivalAccountsExpense("Facilities",
                            $"{FacilityName(group.Key)} × {group.Count()}", group.Count() * (long)BuildServiceFeePennies(group.Key))).ToArray()
                    : [];
                facilityDetailRecorded = facilities.Sum(line => line.AmountPennies) == buildCost;
                if (facilityDetailRecorded) expenses.AddRange(facilities);
                else expenses.Add(new("Facilities", "Facilities (recorded total)", buildCost));
            }

            var owner = new EntityId(p.FinanceOwnerId);
            var stockPurchase = _immersion?.StockPurchase;
            var stockRecorded = stockPurchase is not null || p.Plan is { Committed: true };
            var stockCash = stockPurchase is { Attempt: var attempt } && attempt == p.Attempt
                ? stockPurchase.Entries.Where(entry => entry.OwnerId == owner && entry.Account == LedgerAccountType.CashAsset)
                    .Sum(entry => -entry.AmountPennies) : 0;
            var purchasedStock = p.Plan is { Committed: true } plan
                ? new FestivalAccountsStock[] {
                    new("Chips", plan.Chips, 100, plan.Chips * 100L),
                    new("Soft drink", plan.SoftDrinks, 60, plan.SoftDrinks * 60L),
                    new("Beer", plan.Beers, 100, plan.Beers * 100L)
                }.Where(line => line.Quantity > 0).ToArray() : [];
            var stockDetailRecorded = p.Plan is { Committed: true } && purchasedStock.Sum(line => line.AmountPennies) == stockCash;
            var capital = payments.Where(payment => payment.DebitAccount == LedgerAccountType.EquipmentAsset)
                .Sum(payment => (long)payment.AmountPennies);
            var closing = _festivalFinances[owner].CashPennies;
            var accounts = new FestivalAccounts(sales, costs.ToArray(), expenses.ToArray(), purchasedStock, p.OpeningCashPennies,
                closing, stockCash, capital, stockRecorded, stockDetailRecorded, facilityDetailRecorded, false);
            var reconciles = accounts.IncomePennies == result.RevenuePennies &&
                accounts.SoldItemCostPennies == result.ConsumedStockCostsPennies &&
                accounts.OperatingExpensesPennies == result.ContractCostsPennies &&
                accounts.CapitalPurchasesPennies == result.CapitalPurchasesPennies &&
                accounts.NetCashChangePennies == result.NetCashChangePennies &&
                closing == p.OpeningCashPennies + accounts.IncomePennies - accounts.OperatingExpensesPennies -
                    accounts.StockPurchasesPennies - accounts.CapitalPurchasesPennies;
            return accounts with { Reconciles = reconciles };
        }
    }
}
