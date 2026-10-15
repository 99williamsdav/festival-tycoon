namespace Festival.Simulation;

public sealed record FestivalResult(int Attempt, long Tick, int GuestCount, long SatisfactionTotal,
    long RevenuePennies, long ContractCostsPennies, long ConsumedStockCostsPennies,
    long CapitalPurchasesPennies, long NetCashChangePennies, int? BeersFinished, int? Fights, int? MedicalCollapses)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int Deaths { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int AdmittedGuests { get; init; }
    /// <summary>The ledger's view: cost of stock used only. The accounts' operating result also writes off the stock
    /// left over (<see cref="FestivalAccounts.StockWrittenOffPennies"/>); this saved record can't know the order.</summary>
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
        MedicalSnapshot? medical, DisorderSnapshot? disorder, long tick, LavSuckerSnapshot? lav = null, WeatherSnapshot? weather = null)=>
        MakeCampaignFestivalResult(p,immersion,medical,disorder,tick,lav,weather,false);

    private static FestivalResult MakeCampaignFestivalResult(PreparationSnapshot p, ImmersionSnapshot? immersion,
        MedicalSnapshot? medical, DisorderSnapshot? disorder, long tick, LavSuckerSnapshot? lav, WeatherSnapshot? weather, bool classic,
        ClassicBookingView? booking = null)
    {
        var dead = classic ? medical?.Needs.Where(n=>n.Stage==MedicalStage.Terminal).Select(n=>n.AgentId).ToHashSet() ?? [] : [];
        var admitted = p.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.Admitted).ToArray();
        var guests = admitted.Where(person=>person.Departed && !dead.Contains(person.AgentId)).ToArray();
        var payments = p.Payments.Where(payment => payment.Attempt == p.Attempt).ToArray();
        var buildCost = p.SetupPayments?.SingleOrDefault(setup => setup.Attempt == p.Attempt)?.BuildCostPennies ?? 0;
        // Advance ticket sales are this festival's income too; they arrived as part of the opening budget.
        var tickets = booking?.NetTicketReceiptsPennies ?? FestivalTickets.RevenuePennies(p.Tier);
        var bands = booking?.NetBandCostsPennies ?? 0;
        return new(p.Attempt, tick, guests.Length, guests.Sum(person => (long)person.Satisfaction),
            tickets + FestivalTakings(immersion) + ReceivedPitchFee(p),
            bands + payments.Where(payment => payment.DebitAccount == LedgerAccountType.AdministrationExpense).Sum(payment => (long)payment.AmountPennies) + buildCost + FreeWaterSpend(immersion) + LavSuckerSpend(lav),
            p.StockConsumed * 60L + (immersion?.Purchases.Sum(purchase => (long)purchase.CostPennies) ?? 0) + StrawSpreadCost(p, weather),
            payments.Where(payment => payment.DebitAccount == LedgerAccountType.EquipmentAsset).Sum(payment => (long)payment.AmountPennies),
            tickets + FestivalTakings(immersion) + ReceivedPitchFee(p) - bands - payments.Sum(payment => (long)payment.AmountPennies) - buildCost - FreeWaterSpend(immersion) - LavSuckerSpend(lav) -
                (immersion?.StockPurchase?.Entries.Where(entry => entry.Account == LedgerAccountType.CashAsset && entry.OwnerId.Value == p.FinanceOwnerId).Sum(entry => -entry.AmountPennies) ?? 0),
            p.FinishedBeerIds?.Length,
            p.FinishedBeerIds is null ? null : disorder?.Incidents.Count(incident => (classic?admitted:guests).Any(guest => guest.AgentId == incident.InitiatorId || guest.AgentId == incident.OpponentId)),
            p.GuestMedicalCollapses) { Deaths=dead.Count, AdmittedGuests=classic?admitted.Length:0 };
    }

    private void RecordGuestMedicalCollapse(ulong id)
    {
        if (_preparation is { GuestMedicalCollapses: { } count } p && PersonIn(PersonView.Roster, id) is { Role: ProtectedPersonRole.Guest, Admitted: true, Departed: false })
            _preparation = p with { GuestMedicalCollapses = checked(count + 1) };
    }

    private void FinalizeFestivalDeparture()
    {
        if (_preparation is not { Status: PreparationStatus.Departing, FinishedBeerIds: not null } p || IsLifecycleEditionFrozen()) return;
        // The day finishes when the last guest is off site and nobody is down (see FestivalFinishDue), as it stood at the tick's start.
        var finishDue = _finishDueAtTickStart;
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
        if (!finishDue) return;
        SendRemainingWorkersHome();
        _preparation = p with { Status = PreparationStatus.Finished, Rentals = [], WorkContracts = [],
            Result = MakeCampaignFestivalResult(PreparationView!, ImmersionView, MedicalView, DisorderView, CurrentTick, _lavSucker, _weather, IsClassicCampaign, ClassicBookings) };
        if (_preparation.Result!.Stars is { } stars && BookedActIds(_programme) is { } played)
        {
            var before = new FestivalStanding(p.Reputation, p.SceneCredibility.ToArray());
            var after = ActCatalogue.AfterFestival(before, stars, played.Select(id => ActCatalogue.Find(id)!.Genre));
            _preparation = _preparation with { Reputation = after.Reputation, SceneCredibility = after.SceneCredibility, StandingBefore = before };
        }
        if (p.CommunityShareAttempt == p.Attempt && !p.CommunityFavourClaimed && _lifecycle is { } lifecycle)
        {
            lifecycle.FavourBalance++;
            lifecycle.CompletedOutcomeTransactionIds.Add($"community-water-favour:{CampaignId.Value}");
            _preparation = _preparation with { CommunityFavourClaimed = true };
        }
    }

    /// <summary>
    /// At the finish, whoever is still here (stewards and medics standing by, crew, a band packing up, and at the
    /// walk-out's limit any guest still walking) goes off with the results: jobs end, loos are given up and they
    /// leave the site as the day closes, so a finished day has nobody on it.
    /// </summary>
    private void SendRemainingWorkersHome()
    {
        FinishStaffResponsesForDeparture();
        var leaving = PeopleIn(PersonView.Roster).Where(person => !person.Departed).ToArray();
        var ids = leaving.Select(person => person.Id).ToHashSet();
        // Nobody left on site keeps a loo: an occupant steps out and the queue clears.
        bool Leaving(ulong? id) => id is { } some && ids.Contains(some);
        if (_immersion is not null && Toilets.Any(toilet => toilet.Queue.Any(ids.Contains) || Leaving(toilet.OwnerId) || Leaving(toilet.InterruptedOccupantId)))
            SetToilets(Toilets.Select(toilet => toilet with { Queue = toilet.Queue.Where(id => !ids.Contains(id)).ToArray(),
                OwnerId = Leaving(toilet.OwnerId) ? null : toilet.OwnerId,
                InterruptedOccupantId = Leaving(toilet.InterruptedOccupantId) ? null : toilet.InterruptedOccupantId,
                DoorOpen = !Leaving(toilet.OwnerId) && toilet.DoorOpen,
                ServiceTicks = Leaving(toilet.OwnerId) ? 0 : toilet.ServiceTicks }).ToArray());
        // Nor a place at a tap: the lines empty and the drinking stops.
        if (Taps.Any(tap => tap.Queue.Any(ids.Contains) || tap.Overflow.Any(ids.Contains) || Leaving(tap.OwnerId)))
            SetTaps(Taps.Select(tap => tap with { Queue = tap.Queue.Where(id => !ids.Contains(id)).ToArray(),
                Overflow = tap.Overflow.Where(id => !ids.Contains(id)).ToArray(),
                OwnerId = Leaving(tap.OwnerId) ? null : tap.OwnerId, DrinkTicks = Leaving(tap.OwnerId) ? 0 : tap.DrinkTicks }).ToArray());
        foreach (var person in leaving)
        {
            ReleaseWasteAtExit(person.Id);
            if (_medical is not null && (_persons[person.Id].WaterQueueSlot is not null || _persons[person.Id].Intent is MedicalIntent.SeekWater or MedicalIntent.Drinking))
                MutatePerson(person.Id, need => { need.WaterQueueSlot = null; need.Intent = MedicalIntent.Leaving; need.Reason = "Sent home at the finish"; });
            if (person.ToiletStage != ToiletVisitStage.None || person.ToiletId is not null)
                SetConsumption(_persons[person.Id] with { ToiletStage = ToiletVisitStage.None, ToiletChoice = null, ToiletId = null });
            SetPresence(_persons[person.Id] with { Departed = true });
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
        if (p.Result is { Stars: { } stars } && BookedActIds(s.Programme) is { } played)
        {
            if (p.StandingBefore is not { } before) return "Completed festival must record the standing it changed.";
            var after = ActCatalogue.AfterFestival(before, stars, played.Select(id => ActCatalogue.Find(id)!.Genre));
            if (after.Reputation != p.Reputation || !after.SceneCredibility.SequenceEqual(p.SceneCredibility))
                return "Festival standing does not follow from the completed festival.";
        }
        else if (p.StandingBefore is not null) return "Standing change recorded without a completed festival.";
        if (p.Result is { } result && (result.Tick != s.CurrentTick || !SavedClassic(s)&&s.Lifecycle?.Casualties.Any(c => c.AttemptId == s.Lifecycle.CurrentAttemptId) == true ||
            result != MakeCampaignFestivalResult(p, s.Immersion, s.Medical, s.Disorder, result.Tick, s.LavSucker, s.Weather, SavedClassic(s), SavedBookings(s)))) return "Terminal festival report does not match final authoritative state.";
        return null;
    }
}
