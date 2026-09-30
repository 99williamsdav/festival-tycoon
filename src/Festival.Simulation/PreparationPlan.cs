namespace Festival.Simulation;

// A saved unpaid intention. No purchased effect or contract exists until opening.
public sealed record PreparationPlan(int Version, string[] OfferIds, string[] ActIds,
    int Chips, int SoftDrinks, int Beers, bool Committed = false);
public sealed record RemovePreparationOfferCommand(string OfferId) : SessionCommand;
public sealed record SetPreparationStockCommand(int Chips, int SoftDrinks, int Beers) : SessionCommand;
public sealed record PreparationSetupPayment(string Id, int Attempt, long Tick, int Chips, int SoftDrinks, int Beers, long TotalPennies, LedgerEntry[] Entries)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public long BuildCostPennies { get; init; }
}

public sealed partial class GameSession
{
    private bool _committingPreparationPlan;
    public PreparationPlan? CapturePreparationPlan() => CapturePreparation()?.Plan;
    private static PreparationPlan EmptyPreparationPlan() => new(1, [], [], 0, 0, 0);
    private static int PlannedStockCost(PreparationPlan plan) => checked(plan.Chips * 100 + plan.SoftDrinks * 60 + plan.Beers * 100);
    public long PreparationPlanCost => _preparation?.Plan is { } plan
        ? plan.OfferIds.Concat(plan.ActIds.Where(id => id != "")).Sum(id => (long)GetPreparationOffers().Single(o => o.Id == id).PricePennies) + PlannedStockCost(plan) + BuildDraftCost : 0;
    public long PreparationRemainingCash => _preparation is { } p ? _festivalFinances[new(p.FinanceOwnerId)].CashPennies - (p.Plan is { Committed: false } ? PreparationPlanCost : 0) : 0;
    public int ExpectedPreparedPeopleCount => _preparation is not { } p ? 0 : PeopleIn(PersonView.Roster).Length +
        (p.Plan is { Committed: false } plan ? plan.OfferIds.Count(id => id is "maintenance.worker" or "staff.extra-medic" or "staff.extra-steward") : 0);
    private CommandResult? ValidatePlanEdit(EntityId? target, SessionCommand command)
    {
        if (target is not null || _preparation is not { Status: PreparationStatus.Preparing, Plan: { Committed: false } } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "An unpaid preparation plan is required.");
        if (command is RemovePreparationOfferCommand remove && !p.Plan.OfferIds.Contains(remove.OfferId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This purchase is not planned.");
        if (command is SetPreparationStockCommand stock && (stock.Chips is < 0 or > 10000 || stock.SoftDrinks is < 0 or > 10000 || stock.Beers is < 0 or > 10000))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Choose whole stock quantities from 0 to 10000.");
        return null;
    }
    private void ApplyPlanEdit(SessionCommand command)
    {
        var p = _preparation!; var plan = p.Plan!;
        _preparation = p with { Plan = command switch
        {
            RemovePreparationOfferCommand remove => plan with { OfferIds = plan.OfferIds.Where(id => id != remove.OfferId).ToArray() },
            SetPreparationStockCommand stock => plan with { Chips = stock.Chips, SoftDrinks = stock.SoftDrinks, Beers = stock.Beers },
            _ => throw new InvalidOperationException("Unknown preparation edit.")
        } };
    }
    private void CommitPreparationPlan()
    {
        if (_preparation?.Plan is not { Committed: false } plan) return;
        _committingPreparationPlan = true;
        try
        {
            foreach (var id in plan.OfferIds.Concat(plan.ActIds)) ApplyPreparationOffer(new(id));
            var buildCost = BuildDraftCost;
            if (buildCost > 0) _festivalFinances[new(_preparation!.FinanceOwnerId)].CashPennies -= buildCost;
            _programme = _programme! with { ActIds = plan.ActIds.ToArray(), Status = "Programme booked" };
            SynchronizeImmersionPeople();
            var cost = PlannedStockCost(plan);
            if (cost > 0)
            {
                var owner = new EntityId(_preparation.FinanceOwnerId);
                _festivalFinances[owner].CashPennies -= cost;
                _immersion = _immersion! with { StockPurchased = true, ChipsStock = plan.Chips, SoftStock = plan.SoftDrinks, BeerStock = plan.Beers,
                    StockPurchase = new($"immersion-stock:{CampaignId.Value}:{_preparation.Attempt}", _preparation.Attempt, CurrentTick,
                        [new(owner, LedgerAccountType.CashAsset, -cost), new(owner, LedgerAccountType.InventoryAsset, cost)]) };
            }
            _preparation = _preparation with { Plan = plan with { Committed = true } };
            var p = _preparation;
            var financeOwner = new EntityId(p.FinanceOwnerId);
            var entries = p.Payments.Where(payment => payment.Attempt == p.Attempt).SelectMany(payment => new LedgerEntry[] {
                new(financeOwner, payment.DebitAccount, payment.AmountPennies), new(financeOwner, LedgerAccountType.CashAsset, -payment.AmountPennies) }).ToArray();
            if (cost > 0) entries = entries.Concat(_immersion!.StockPurchase!.Entries).ToArray();
            if (buildCost > 0) entries = entries.Concat(new LedgerEntry[] {
                new(financeOwner, LedgerAccountType.AdministrationExpense, buildCost),
                new(financeOwner, LedgerAccountType.CashAsset, -buildCost) }).ToArray();
            _preparation = p with { SetupPayments = p.SetupPayments!.Append(new($"setup:{CampaignId.Value}:{p.Attempt}", p.Attempt, CurrentTick,
                plan.Chips, plan.SoftDrinks, plan.Beers, PreparationPlanCost, entries) { BuildCostPennies = buildCost }).ToArray() };
        }
        finally { _committingPreparationPlan = false; }
    }
    private static string? ValidatePersistedPlan(PreparationSnapshot p, SessionPersistenceSnapshot s)
    {
        if (p.Plan is not { } plan) return null;
        if (p.SetupPayments is null || p.SetupPayments.Any(payment => payment is null || payment.Entries is null)) return "Preparation setup history missing.";
        if (plan.Version != 1 || s.Immersion is null || s.Programme is null || s.Perks is null || plan.OfferIds is null || plan.ActIds is null ||
            !plan.OfferIds.SequenceEqual(plan.OfferIds.Distinct().Order(StringComparer.Ordinal)) ||
            plan.ActIds.Length is not (0 or 3) || plan.ActIds.Where(id => id != "").Distinct().Count() != plan.ActIds.Count(id => id != "") || plan.ActIds.Any(id => id != "" && !FestivalActs.Any(a => a.Id == id)) ||
            plan.Chips is < 0 or > 10000 || plan.SoftDrinks is < 0 or > 10000 || plan.Beers is < 0 or > 10000 ||
            plan.Committed != (p.Status != PreparationStatus.Preparing)) return "Preparation plan header or quantities invalid.";
        var factory = CreateFoodAndDrinkBaseline(s.CampaignSeed);
        var offers = factory.GetPreparationOffers();
        if (plan.OfferIds.Any(id => !offers.Any(o => o.Id == id && o.Category is not ("act" or "contract"))) ||
            plan.OfferIds.Select(id => offers.Single(o => o.Id == id).Category).Distinct().Count() != plan.OfferIds.Length ||
            plan.OfferIds.Contains("staff.extra-medic") && !p.ExtraMedicSlotOwned || plan.OfferIds.Contains("staff.extra-steward") && !p.ExtraStewardSlotOwned ||
            !plan.Committed && plan.OfferIds.Any(id => id.StartsWith("equipment.")) && p.OwnedEquipment.Length > 0)
            return "Preparation plan offers or slots invalid.";
        if (!plan.Committed && (p.AcceptedOffers.Length != 0 || p.WorkContracts.Length != 0 || p.Rentals.Length != 0 || s.Immersion.StockPurchased ||
            s.Programme.ActIds.Length != 0 || p.Payments.Any(payment => payment.Attempt == p.Attempt) || s.Equipment?.WorkerId is not null))
            return "Unpaid preparation plan contains active purchases.";
        if (plan.Committed && !p.AcceptedOffers.SequenceEqual(plan.OfferIds.Concat(plan.ActIds).Order(StringComparer.Ordinal)))
            return "Opening payment and committed plan disagree.";
        if (p.SetupPayments.Length != p.Attempt - (plan.Committed ? 0 : 1)) return "Exactly one setup payment is required for each opened attempt.";
        foreach (var setup in p.SetupPayments)
        {
            if (setup.Id != $"setup:{s.CampaignId}:{setup.Attempt}" || setup.Attempt < 1 || setup.Attempt > p.Attempt || setup.Attempt != Array.IndexOf(p.SetupPayments, setup) + 1 ||
                setup.Tick < 0 || setup.Tick > s.CurrentTick || setup.Chips is < 0 or > 10000 || setup.SoftDrinks is < 0 or > 10000 || setup.Beers is < 0 or > 10000)
                return "Setup payment identity, order or stock quantities invalid.";
            var owner = new EntityId(p.FinanceOwnerId);
            var stockCost = setup.Chips * 100 + setup.SoftDrinks * 60 + setup.Beers * 100;
            var payments = p.Payments.Where(payment => payment.Attempt == setup.Attempt).ToArray();
            var expected = payments.SelectMany(payment => new LedgerEntry[] { new(owner, payment.DebitAccount, payment.AmountPennies), new(owner, LedgerAccountType.CashAsset, -payment.AmountPennies) });
            if (stockCost > 0) expected = expected.Concat(new LedgerEntry[] { new(owner, LedgerAccountType.CashAsset, -stockCost), new(owner, LedgerAccountType.InventoryAsset, stockCost) });
            if (setup.BuildCostPennies > 0) expected = expected.Concat(new LedgerEntry[] {
                new(owner, LedgerAccountType.AdministrationExpense, setup.BuildCostPennies),
                new(owner, LedgerAccountType.CashAsset, -setup.BuildCostPennies) });
            if (setup.BuildCostPennies < 0 || !setup.Entries.SequenceEqual(expected) || setup.TotalPennies != payments.Sum(payment => (long)payment.AmountPennies) + stockCost + setup.BuildCostPennies || payments.Any(payment => payment.Tick != setup.Tick) ||
                setup.Attempt == p.Attempt && (!plan.Committed || setup.Chips != plan.Chips || setup.SoftDrinks != plan.SoftDrinks || setup.Beers != plan.Beers || setup.TotalPennies != plan.OfferIds.Concat(plan.ActIds).Sum(id => (long)offers.Single(o => o.Id == id).PricePennies) + PlannedStockCost(plan) + p.BuildPlacements.Sum(item => (long)BuildServiceFeePennies(item.Kind))))
                return "Setup payment ledger or planned total does not reconcile.";
        }
        return null;
    }
}
