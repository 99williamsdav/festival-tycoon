namespace Festival.Simulation;

public sealed record FestivalResult(int Attempt, long Tick, int GuestCount, long SatisfactionTotal,
    long RevenuePennies, long ContractCostsPennies, long ConsumedStockCostsPennies,
    long CapitalPurchasesPennies, long NetCashChangePennies, int? BeersFinished, int? Fights, int? MedicalCollapses)
{
    public long ProfitPennies => RevenuePennies - ContractCostsPennies - ConsumedStockCostsPennies;
    public int? Stars => Rating(SatisfactionTotal, GuestCount);
    public decimal? SatisfactionPercent => GuestCount == 0 ? null : SatisfactionTotal / (100m * GuestCount);
    public static int? Rating(long total, int count) => count == 0 ? null :
        total < 2000L * count ? 1 : total < 4000L * count ? 2 : total < 6000L * count ? 3 : total < 8000L * count ? 4 : 5;
}

public sealed partial class GameSession
{
    public bool FestivalResultsEnabled => _preparation?.FinishedBeerIds is not null;
    public FestivalResult? CompletedFestivalResult => _preparation?.Result;

    private static FestivalResult MakeFestivalResult(PreparationSnapshot p, ImmersionSnapshot? immersion,
        MedicalSnapshot? medical, DisorderSnapshot? disorder, long tick)
    {
        var guests = p.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.Admitted && person.Departed).ToArray();
        var payments = p.Payments.Where(payment => payment.Attempt == p.Attempt).ToArray();
        var buildCost = p.SetupPayments?.SingleOrDefault(setup => setup.Attempt == p.Attempt)?.BuildCostPennies ?? 0;
        // Advance ticket sales are this festival's income too; they arrived as part of the opening budget.
        var tickets = FestivalTickets.RevenuePennies(p.Tier);
        return new(p.Attempt, tick, guests.Length, guests.Sum(person => (long)person.Satisfaction),
            tickets + (immersion?.Purchases.Sum(purchase => (long)purchase.PricePennies) ?? 0),
            payments.Where(payment => payment.DebitAccount == LedgerAccountType.AdministrationExpense).Sum(payment => (long)payment.AmountPennies) + buildCost + FreeWaterSpend(immersion),
            p.StockConsumed * 60L + (immersion?.Purchases.Sum(purchase => (long)purchase.CostPennies) ?? 0),
            payments.Where(payment => payment.DebitAccount == LedgerAccountType.EquipmentAsset).Sum(payment => (long)payment.AmountPennies),
            tickets + (immersion?.Purchases.Sum(purchase => (long)purchase.PricePennies) ?? 0) - payments.Sum(payment => (long)payment.AmountPennies) - buildCost - FreeWaterSpend(immersion) -
                (immersion?.StockPurchase?.Entries.Where(entry => entry.Account == LedgerAccountType.CashAsset && entry.OwnerId.Value == p.FinanceOwnerId).Sum(entry => -entry.AmountPennies) ?? 0),
            p.FinishedBeerIds?.Length,
            p.FinishedBeerIds is null ? null : disorder?.Incidents.Count(incident => guests.Any(guest => guest.AgentId == incident.InitiatorId || guest.AgentId == incident.OpponentId)),
            p.GuestMedicalCollapses);
    }

    private void RecordGuestMedicalCollapse(ulong id)
    {
        if (_preparation is { GuestMedicalCollapses: { } count } p && PersonIn(PersonView.Roster, id) is { Role: ProtectedPersonRole.Guest, Admitted: true, Departed: false })
            _preparation = p with { GuestMedicalCollapses = checked(count + 1) };
    }

    private void FinalizeFestivalDeparture()
    {
        if (_preparation is not { Status: PreparationStatus.Departing, FinishedBeerIds: not null } p || IsLifecycleEditionFrozen()) return;
        var departedAtStart = PeopleIn(PersonView.Roster).All(person => person.Admitted && person.Departed);
        // Hazards and paid ingestion have completed at this tick before physical exit is committed.
        var people = PeopleIn(PersonView.Roster).ToArray();
        for (var index = 0; index < people.Length; index++)
        {
            var person = people[index];
            var nav = _navigationAgents[new(person.Id)];
            if (!person.Departed && person.Admitted && nav.Action == AgentNavigationAction.Arrived && ImmersionCanMarkDeparted(person.Id, index))
            {
                ReleaseWasteAtExit(person.Id);
                people[index] = person with { Departed = true };
            }
        }
        foreach (var person in people) SetPresence(person);
        if (!departedAtStart) return;
        _preparation = p with { Status = PreparationStatus.Finished, Rentals = [], WorkContracts = [],
            Result = MakeFestivalResult(PreparationView!, ImmersionView, MedicalView, DisorderView, CurrentTick) };
        if (_preparation.Result!.Stars is { } stars && _programme is { ActIds.Length: 3 } programme)
        {
            var before = new FestivalStanding(p.Reputation, p.SceneCredibility.ToArray());
            var after = ActCatalogue.AfterFestival(before, stars, programme.ActIds.Select(id => ActCatalogue.Find(id)!.Genre));
            _preparation = _preparation with { Reputation = after.Reputation, SceneCredibility = after.SceneCredibility, StandingBefore = before };
        }
        if (p.CommunityShareAttempt == p.Attempt && !p.CommunityFavourClaimed && _lifecycle is { } lifecycle)
        {
            lifecycle.FavourBalance++;
            lifecycle.CompletedOutcomeTransactionIds.Add($"community-water-favour:{CampaignId.Value}");
            _preparation = _preparation with { CommunityFavourClaimed = true };
        }
    }

    private static string? ValidateFestivalResult(SessionPersistenceSnapshot s)
    {
        var p = s.Preparation;
        if (p is null) return null;
        if (p.FinishedBeerIds is null) return p.Result is null && p.GuestMedicalCollapses is null ? null : "Results require complete recorded metrics.";
        if (p.GuestMedicalCollapses is null or < 0 || p.GuestMedicalCollapses > (s.Medical?.Evidence.Count(item => item.Id is "medical:collapse" or "intoxication:collapse") ?? 0)) return "Guest collapse count invalid.";
        var ids = p.FinishedBeerIds;
        if (ids.Length > (s.Immersion?.Purchases.Length ?? 0) || ids.Any(id => id is null) || ids.Distinct().Count() != ids.Length ||
            ids.Any(id => s.Immersion?.Purchases.Any(purchase => purchase.Id == id && purchase.Product == ImmersionProduct.Beer && p.People.Any(person => person.AgentId == purchase.AgentId && person.Admitted && person.Role == ProtectedPersonRole.Guest)) != true) ||
            s.Immersion?.People.Any(person => person.Held is { } held && ids.Contains(held.TransactionId)) == true)
            return "Completed beer identities invalid.";
        if ((p.Status == PreparationStatus.Finished) != (p.Result is not null)) return "Terminal festival report missing or premature.";
        if (p.Result is { Stars: { } stars } && s.Programme is { ActIds.Length: 3 } programme)
        {
            if (p.StandingBefore is not { } before) return "Completed festival must record the standing it changed.";
            var after = ActCatalogue.AfterFestival(before, stars, programme.ActIds.Select(id => ActCatalogue.Find(id)!.Genre));
            if (after.Reputation != p.Reputation || !after.SceneCredibility.SequenceEqual(p.SceneCredibility))
                return "Festival standing does not follow from the completed festival.";
        }
        else if (p.StandingBefore is not null) return "Standing change recorded without a completed festival.";
        if (p.Result is { } result && (result.Tick != s.CurrentTick || s.Lifecycle?.Casualties.Any(c => c.AttemptId == s.Lifecycle.CurrentAttemptId) == true ||
            result != MakeFestivalResult(p, s.Immersion, s.Medical, s.Disorder, result.Tick))) return "Terminal festival report does not match final authoritative state.";
        return null;
    }
}
