namespace Festival.Simulation;

// A saved unpaid intention. No purchased effect or contract exists until opening.
/// <param name="TraderId">The food trader pitched at the food van; they bring their own food.</param>
public sealed record PreparationPlan(int Version, string[] OfferIds, string[] ActIds,
    int SoftDrinks, int Beers, bool Committed = false, string? TraderId = null);
public sealed record RemovePreparationOfferCommand(string OfferId) : SessionCommand;
/// <summary>The bar's stock: the food trader brings their own.</summary>
public sealed record SetPreparationStockCommand(int SoftDrinks, int Beers) : SessionCommand;
public sealed record PreparationSetupPayment(string Id, int Attempt, long Tick, int SoftDrinks, int Beers, long TotalPennies, LedgerEntry[] Entries)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public long BuildCostPennies { get; init; }
    /// <summary>The food trader's pitch fee, received at opening.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public long PitchFeePennies { get; init; }
}

public sealed partial class GameSession
{
    private bool _committingPreparationPlan;
    public PreparationPlan? CapturePreparationPlan() => CapturePreparation()?.Plan;
    private static PreparationPlan EmptyPreparationPlan() => new(1, [], [], 0, 0, TraderId: FoodTraders.Default.Id);
    private static int StockCost(int softDrinks, int beers) =>
        checked(softDrinks * ImmersionCost(ImmersionProduct.SoftDrink) + beers * ImmersionCost(ImmersionProduct.Beer));
    private static int PlannedStockCost(PreparationPlan plan) => StockCost(plan.SoftDrinks, plan.Beers);
    public long PreparationPlanCost => _preparation?.Plan is { } plan
        ? plan.OfferIds.Concat(plan.ActIds.Where(id => id != "")).Sum(id => (long)GetPreparationOffers().Single(o => o.Id == id).PricePennies) + PlannedStockCost(plan) + BuildDraftCost : 0;
    /// <summary>What's left once the plan is paid for, counting the food trader's pitch fee coming in.</summary>
    public long PreparationRemainingCash => _preparation is { } p ? _festivalFinances[new(p.FinanceOwnerId)].CashPennies -
        (p.Plan is { Committed: false } ? PreparationPlanCost - PlannedPitchFeePennies : 0) : 0;
    public int ExpectedPreparedPeopleCount => _preparation is not { } p ? 0 : PeopleIn(PersonView.Roster).Length +
        (p.Plan is { Committed: false } plan ? plan.OfferIds.Count(id => id == "maintenance.worker" ||
            id.StartsWith("staff.extra-", StringComparison.Ordinal) && !id.StartsWith("staff.extra-sound.", StringComparison.Ordinal)) : 0);
    private CommandResult? ValidatePlanEdit(EntityId? target, SessionCommand command)
    {
        if (target is not null || _preparation is not { Status: PreparationStatus.Preparing, Plan: { Committed: false } } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "An unpaid preparation plan is required.");
        if (command is RemovePreparationOfferCommand remove && !p.Plan.OfferIds.Contains(remove.OfferId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This purchase is not planned.");
        if (command is SetPreparationStockCommand stock && (stock.SoftDrinks is < 0 or > 10000 || stock.Beers is < 0 or > 10000))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Choose whole stock quantities from 0 to 10000.");
        return null;
    }
    private void ApplyPlanEdit(SessionCommand command)
    {
        var p = _preparation!; var plan = p.Plan!;
        _preparation = p with { Plan = command switch
        {
            RemovePreparationOfferCommand remove => plan with { OfferIds = plan.OfferIds.Where(id => id != remove.OfferId).ToArray() },
            SetPreparationStockCommand stock => plan with { SoftDrinks = stock.SoftDrinks, Beers = stock.Beers },
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
            for (var stage = 0; stage < Stages.Count; stage++)
                SetStageProgramme(stage, StageProgramme(stage)! with { ActIds = Stages.Count == 1 ? plan.ActIds.ToArray() : PlanStageActs(plan, stage), Status = "Programme booked" });
            SynchronizeImmersionPeople();
            var cost = PlannedStockCost(plan);
            var pitchFee = PlannedPitchFeePennies;
            if (pitchFee > 0) _festivalFinances[new(_preparation!.FinanceOwnerId)].CashPennies += pitchFee;
            if (cost > 0)
            {
                var owner = new EntityId(_preparation.FinanceOwnerId);
                _festivalFinances[owner].CashPennies -= cost;
                _immersion = _immersion! with { StockPurchased = true, SoftStock = plan.SoftDrinks, BeerStock = plan.Beers,
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
            if (pitchFee > 0) entries = entries.Concat(PitchFeeEntries(financeOwner, pitchFee)).ToArray();
            _preparation = p with { SetupPayments = p.SetupPayments!.Append(new($"setup:{CampaignId.Value}:{p.Attempt}", p.Attempt, CurrentTick,
                plan.SoftDrinks, plan.Beers, PreparationPlanCost, entries) { BuildCostPennies = buildCost, PitchFeePennies = pitchFee }).ToArray() };
        }
        finally { _committingPreparationPlan = false; }
    }
    private static LedgerEntry[] PitchFeeEntries(EntityId owner, long fee) =>
        [new(owner, LedgerAccountType.CashAsset, fee), new(owner, LedgerAccountType.SalesRevenue, -fee)];
    private static string? ValidatePersistedPlan(PreparationSnapshot p, SessionPersistenceSnapshot s)
    {
        if (p.Plan is not { } plan) return null;
        if (p.SetupPayments is null || p.SetupPayments.Any(payment => payment is null || payment.Entries is null)) return "Preparation setup history missing.";
        if (plan.Version != 1 || s.Immersion is null || s.Programme is null || s.Perks is null || plan.OfferIds is null || plan.ActIds is null ||
            !plan.OfferIds.SequenceEqual(plan.OfferIds.Distinct().Order(StringComparer.Ordinal)) ||
            plan.ActIds.Length != 0 && plan.ActIds.Length != SavedStages(s).Count * 3 || plan.ActIds.Where(id => id != "").Distinct().Count() != plan.ActIds.Count(id => id != "") || plan.ActIds.Any(id => id != "" && (ActCatalogue.Find(id) is not { } act ||
                !plan.Committed && ActCatalogue.StandingOf(new(p.Reputation, p.SceneCredibility), act) == ActStanding.Locked)) ||
            plan.SoftDrinks is < 0 or > 10000 || plan.Beers is < 0 or > 10000 || FoodTraders.Find(plan.TraderId) is null ||
            plan.Committed != (p.Status != PreparationStatus.Preparing)) return "Preparation plan header or quantities invalid.";
        var factory = CreateFoodAndDrinkBaseline(s.CampaignSeed, p.Tier, pondStageTrial: s.PondStageTrial).WithPaymentStanding(p);
        var offers = factory.GetPreparationOffers();
        if (plan.OfferIds.Any(id => !offers.Any(o => o.Id == id && o.Category is not ("act" or "contract"))) ||
            plan.OfferIds.Select(id => offers.Single(o => o.Id == id).Category).Distinct().Count() != plan.OfferIds.Length ||
            plan.OfferIds.Any(id => id.StartsWith("staff.extra-medic.", StringComparison.Ordinal)) && !p.ExtraMedicSlotOwned ||
            plan.OfferIds.Any(id => id.StartsWith("staff.extra-steward.", StringComparison.Ordinal)) && !p.ExtraStewardSlotOwned ||
            plan.OfferIds.Select(id => id.Replace("staff.extra-", "staff.", StringComparison.Ordinal)).Distinct().Count() != plan.OfferIds.Length ||
            !plan.Committed && plan.OfferIds.Any(id => id.StartsWith("equipment.")) && p.OwnedEquipment.Length > 0)
            return "Preparation plan offers or slots invalid.";
        if (!plan.Committed && (p.AcceptedOffers.Length != 0 || p.WorkContracts.Length != 0 || p.Rentals.Length != 0 || s.Immersion.StockPurchased ||
            s.Programme.Stages.Any(q => q.ActIds.Length != 0) || p.Payments.Any(payment => payment.Attempt == p.Attempt) || s.Equipment?.WorkerId is not null))
            return "Unpaid preparation plan contains active purchases.";
        if (plan.Committed && !p.AcceptedOffers.SequenceEqual(plan.OfferIds.Concat(plan.ActIds).Order(StringComparer.Ordinal)))
            return "Opening payment and committed plan disagree.";
        if (p.SetupPayments.Length != p.Attempt - (plan.Committed ? 0 : 1)) return "Exactly one setup payment is required for each opened attempt.";
        foreach (var setup in p.SetupPayments)
        {
            if (setup.Id != $"setup:{s.CampaignId}:{setup.Attempt}" || setup.Attempt < 1 || setup.Attempt > p.Attempt || setup.Attempt != Array.IndexOf(p.SetupPayments, setup) + 1 ||
                setup.Tick < 0 || setup.Tick > s.CurrentTick || setup.SoftDrinks is < 0 or > 10000 || setup.Beers is < 0 or > 10000 || setup.PitchFeePennies < 0)
                return "Setup payment identity, order or stock quantities invalid.";
            var owner = new EntityId(p.FinanceOwnerId);
            var stockCost = StockCost(setup.SoftDrinks, setup.Beers);
            var payments = p.Payments.Where(payment => payment.Attempt == setup.Attempt).ToArray();
            var expected = payments.SelectMany(payment => new LedgerEntry[] { new(owner, payment.DebitAccount, payment.AmountPennies), new(owner, LedgerAccountType.CashAsset, -payment.AmountPennies) });
            if (stockCost > 0) expected = expected.Concat(new LedgerEntry[] { new(owner, LedgerAccountType.CashAsset, -stockCost), new(owner, LedgerAccountType.InventoryAsset, stockCost) });
            if (setup.BuildCostPennies > 0) expected = expected.Concat(new LedgerEntry[] {
                new(owner, LedgerAccountType.AdministrationExpense, setup.BuildCostPennies),
                new(owner, LedgerAccountType.CashAsset, -setup.BuildCostPennies) });
            if (setup.PitchFeePennies > 0) expected = expected.Concat(PitchFeeEntries(owner, setup.PitchFeePennies));
            if (setup.BuildCostPennies < 0 || !setup.Entries.SequenceEqual(expected) || setup.TotalPennies != payments.Sum(payment => (long)payment.AmountPennies) + stockCost + setup.BuildCostPennies || payments.Any(payment => payment.Tick != setup.Tick) ||
                setup.Attempt == p.Attempt && (!plan.Committed || setup.PitchFeePennies != (p.BuildPlacements.Any(item => item.Kind == BuildServiceKind.FoodVan) ? FoodTraders.Find(plan.TraderId)!.PitchFeePennies : 0) || setup.SoftDrinks != plan.SoftDrinks || setup.Beers != plan.Beers || setup.TotalPennies != plan.OfferIds.Concat(plan.ActIds).Sum(id => (long)offers.Single(o => o.Id == id).PricePennies) + PlannedStockCost(plan) + p.BuildPlacements.Sum(item => (long)BuildServiceFeePennies(item.Kind))))
                return "Setup payment ledger or planned total does not reconcile.";
        }
        return null;
    }
}
