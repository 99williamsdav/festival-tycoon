namespace Festival.Simulation;

public abstract record ClassicBookingCommand : SessionCommand;
public sealed record SetClassicTicketPriceCommand(int PricePennies) : ClassicBookingCommand;
public sealed record ConfirmClassicBookingsCommand : ClassicBookingCommand;
public sealed record RebookClassicProgrammeCommand(string[] ActIds) : ClassicBookingCommand;
public sealed record CancelClassicFestivalCommand : ClassicBookingCommand;
public sealed record ClassicBookingGuidance(int Capacity, ClassicTicketEstimate Sales, long BandFeesPennies,
    long ConservativeSetupCashPennies, long MinimumRequiredSitePennies, bool Risky);

public sealed partial class GameSession
{
    private (ClassicBookingSnapshot State, int Number, ClassicBookingView View)? _classicBookingCache;
    private ClassicBookingView? ClassicBookings => _preparation?.ClassicBooking is not { } state ? null :
        _classicBookingCache is { } cache && cache.Number == FestivalNumber && ReferenceEquals(cache.State, state) ? cache.View : CacheClassicBooking(state);
    private ClassicBookingView CacheClassicBooking(ClassicBookingSnapshot state)
    {
        var view = ClassicBookingLedger.Evaluate(CampaignSeed, FestivalNumber, state);
        _classicBookingCache = (state, FestivalNumber, view); return view;
    }
    public ClassicBookingSnapshot? CaptureClassicBooking() => _preparation?.ClassicBooking is { } state ? ClassicBookingLedger.Clone(state) : null;
    public ClassicBookingView? CaptureClassicBookingDetails() => ClassicBookings is not { } view ? null : view with
    { Announced = view.Announced.ToArray(), Contracts = view.Contracts.ToArray(), Money = view.Money.ToArray() };
    public int ClassicSiteCapacity => ClassicTicketDemand.InheritedCapacity;
    public ClassicBookingGuidance? ClassicBookingEstimate
    {
        get
        {
            if (!IsClassicCampaign || ClassicBookings is not { Phase: ClassicBookingPhase.Booking } || _preparation?.Plan is not { } plan) return null;
            var acts = plan.ActIds.Where(id => id != "").Select(id => FestivalActs.Single(a => a.Id == id)).ToArray();
            var sales = ClassicTicketDemand.Estimate(new(ClassicSiteCapacity, _preparation.Reputation,
                _preparation.ClassicBooking!.DraftTicketPricePennies, acts.Select(a => a.Popularity).ToArray()));
            var fees = acts.Sum(a => (long)ActFee(a));
            var minimum = GetStaffCandidates().GroupBy(c => c.Role).Sum(g => (long)g.Min(c => c.WagePennies)) +
                new[] { BuildServiceKind.WaterTap, BuildServiceKind.Toilet, BuildServiceKind.FirstAid, BuildServiceKind.StewardPost }
                    .Sum(kind => (long)RuleProfile.BuildFee(kind));
            var budget = sales.ConservativeSetupCash(_festivalFinances[new(_preparation.FinanceOwnerId)].CashPennies,
                _preparation.ClassicBooking.DraftTicketPricePennies, fees);
            return new(ClassicSiteCapacity, sales, fees, budget, minimum, budget - minimum < -CampaignDefaults.OverdraftPennies);
        }
    }

    private void InitializeClassicBooking()
    {
        if (!IsClassicCampaign || _preparation is not { } p) return;
        var opening = p.CarriedIn?.CashPennies ?? CampaignDefaults.OpeningLoanPrincipalPennies;
        var owner = new EntityId(p.FinanceOwnerId);
        _festivalFinances[owner].CashPennies = opening;
        _preparation = p with { OpeningCashPennies = opening, ClassicBooking = ClassicBookingLedger.Create(p.Reputation) };
        _campaignPlanning!.LedgerTransactions[0] = ClassicPrebookingOpening(owner, p.CarriedIn);
    }
    private static PlanningLedgerTransactionSnapshot ClassicPrebookingOpening(EntityId owner, FestivalCarryOver? carry) => carry is null
        ? new(1, "Starter loan before ticket sales", 8,
            [new(owner, LedgerAccountType.CashAsset, CampaignDefaults.OpeningLoanPrincipalPennies),
             new(owner, LedgerAccountType.LoanPrincipalLiability, -CampaignDefaults.OpeningLoanPrincipalPennies)])
        : new(1, $"Carried forward before Festival booking", 8,
            [new(owner, LedgerAccountType.CashAsset, carry.CashPennies),
             new(owner, LedgerAccountType.LoanPrincipalLiability, -carry.DebtPennies),
             new(owner, LedgerAccountType.CarriedBalance, carry.DebtPennies - carry.CashPennies)]);

    private ClassicAnnouncedAct[] ClassicQuote(string[] ids, bool allowEmpty = false)
    {
        if (ids is null || ids.Length != 3 || ids.Any(id => id is null || !allowEmpty && id == "") ||
            ids.Where(id => id != "").Distinct().Count() != ids.Count(id => id != ""))
            throw new ArgumentException("Choose three distinct acts before confirming.");
        var quotes = new List<ClassicAnnouncedAct>();
        foreach (var id in ids)
        {
            if (id == "") { quotes.Add(ClassicAnnouncedAct.EmptySlot); continue; }
            var act = FestivalActs.SingleOrDefault(a => a.Id == id);
            if (act is null || !ActWillPlay(id)) throw new ArgumentException("Every booked act must be willing to play.");
            quotes.Add(new(id, act.Genre, act.Popularity, ActFee(act)));
        }
        return quotes.ToArray();
    }
    private ClassicBookingSnapshot NextClassicBooking(ClassicBookingCommand command)
    {
        var state = _preparation!.ClassicBooking!;
        return command switch
        {
            SetClassicTicketPriceCommand price => ClassicBookingLedger.SetPrice(CampaignSeed, FestivalNumber, state, price.PricePennies),
            ConfirmClassicBookingsCommand => ClassicBookingLedger.Confirm(CampaignSeed, FestivalNumber, state, ClassicQuote(_preparation.Plan!.ActIds)),
            RebookClassicProgrammeCommand rebook => ClassicBookingLedger.Rebook(CampaignSeed, FestivalNumber, state, ClassicQuote(rebook.ActIds, true)),
            CancelClassicFestivalCommand => ClassicBookingLedger.Cancel(CampaignSeed, FestivalNumber, state),
            _ => throw new ArgumentException("Unknown Classic booking action.")
        };
    }
    private CommandResult? ValidateClassicBookingCommand(EntityId? target, ClassicBookingCommand command)
    {
        if (target is not null || !IsClassicCampaign || _preparation is not { Status: PreparationStatus.Preparing, ClassicBooking: not null, Plan: not null })
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Classic booking is available before opening only.");
        try { _ = NextClassicBooking(command); return null; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { return CommandResult.Rejected(CommandReasonCode.InvalidParameter, error.Message); }
    }
    private void ApplyClassicBookingCommand(ClassicBookingCommand command)
    {
        var before = ClassicBookings!;
        var next = NextClassicBooking(command);
        var after = ClassicBookingLedger.Evaluate(CampaignSeed, FestivalNumber, next);
        var p = _preparation!;
        _festivalFinances[new(p.FinanceOwnerId)].CashPennies = checked(_festivalFinances[new(p.FinanceOwnerId)].CashPennies + after.CashChangePennies - before.CashChangePennies);
        _preparation = p with { ClassicBooking = next, Reputation = after.Reputation };
        if (command is ConfirmClassicBookingsCommand or RebookClassicProgrammeCommand)
            _preparation = _preparation with { Plan = _preparation.Plan! with { ActIds = after.Contracts.Select(a => a.ActId).ToArray() } };
        if (command is CancelClassicFestivalCommand)
            _preparation = _preparation with { Plan = EmptyPreparationPlan(),
                BuildPlacements = [], StrawCells = null, MatPanels = null };
        if (command is not SetClassicTicketPriceCommand)
        {
            SetClassicTicketRoster(after.Phase == ClassicBookingPhase.Booking ? ClassicSiteCapacity : after.TicketHolders, after.Announced);
            SyncBuildPhysicalLayout(); ApplyBuildGuestOpeningNeeds();
        }
    }

    // Reserve capacity identities at construction. Before opening, keep only actual ticket holders in every person
    // view/wallet/navigation set. Recovering a cancelled plan restores candidates with the same reserved identities.
    private void SetClassicTicketRoster(int count, ClassicAnnouncedAct[] announced)
    {
        var p = PreparationView!; var currentIds = p.People.Where(a => a.Role == ProtectedPersonRole.Guest).Select(a => a.AgentId).ToHashSet();
        var baseline = CreateBuildCampaign(CampaignSeed, 1, p.CarriedIn, CampaignId, identity: _identity, classicFestivalNumber: FestivalNumber);
        var candidates = baseline.PreparationView!.People.Where(a => a.Role == ProtectedPersonRole.Guest).OrderBy(a => a.AgentId).ToArray();
        var selected = candidates.Take(count).Select(a => a.AgentId).ToHashSet();
        var existing = p.People.Where(a => a.Role == ProtectedPersonRole.Guest).ToDictionary(a => a.AgentId);
        var roster = candidates.Where(a => selected.Contains(a.AgentId)).Select(candidate =>
            existing.GetValueOrDefault(candidate.AgentId, candidate) with { ExpectedGenre = announced.Length == 0
                ? candidate.ExpectedGenre : ClassicTicketFanGenre(candidate.AgentId, announced) });
        PreparationView = p with { People = p.People.Where(a => a.Role != ProtectedPersonRole.Guest).Concat(roster).OrderBy(a => a.AgentId).ToArray() };
        var allGuests = candidates.Select(a => a.AgentId).ToHashSet();
        var consumption = ImmersionView!;
        ImmersionView = consumption with { People = consumption.People.Where(a => !allGuests.Contains(a.AgentId) || selected.Contains(a.AgentId))
            .Concat(baseline.ImmersionView!.People.Where(a => selected.Contains(a.AgentId) && !currentIds.Contains(a.AgentId))).OrderBy(a => a.AgentId).ToArray() };
        var medical = MedicalView!;
        MedicalView = medical with { Needs = medical.Needs.Where(a => !allGuests.Contains(a.AgentId) || selected.Contains(a.AgentId))
            .Concat(baseline.MedicalView!.Needs.Where(a => selected.Contains(a.AgentId) && !currentIds.Contains(a.AgentId))).OrderBy(a => a.AgentId).ToArray() };
        var disorder = DisorderView!;
        DisorderView = disorder with { People = disorder.People.Where(a => selected.Contains(a.AgentId))
            .Concat(baseline.DisorderView!.People.Where(a => selected.Contains(a.AgentId) && !currentIds.Contains(a.AgentId))).OrderBy(a => a.AgentId).ToArray() };
        foreach (var id in allGuests)
        {
            var key = new EntityId(id);
            // Physical agents are created at opening, not while the plan is being edited.
            _navigationAgents.Remove(key);
            if (!selected.Contains(id)) _wallets.Remove(key);
            else if (!currentIds.Contains(id))
            {
                _wallets[key] = baseline._wallets[key];
            }
        }
    }
    private int ClassicTicketFanGenre(ulong id, ClassicAnnouncedAct[] announced)
    {
        var random = RandomStreamFactory.Create(FestivalSeed ^ id ^ 0xFA471C4E20261015UL, RandomStreamId.Demand);
        var pick = (int)(random.NextUInt32() % (uint)announced.Sum(a => a.Popularity + 10));
        foreach (var act in announced) { pick -= act.Popularity + 10; if (pick < 0) return act.Genre; }
        return announced[^1].Genre;
    }

    private static ClassicBookingView? SavedBookings(SessionPersistenceSnapshot s) => s.Preparation?.ClassicBooking is not { } state
        ? null : ClassicBookingLedger.Evaluate(s.CampaignSeed, s.CampaignPlanning!.EditionNumber, state);
    private static int SavedGuestCount(SessionPersistenceSnapshot s) => SavedBookings(s) is { } booking
        ? booking.Phase == ClassicBookingPhase.Booking ? ClassicTicketDemand.InheritedCapacity : booking.TicketHolders
        : FestivalTickets.Sold(s.Preparation!.Tier);
    private static int SavedTicketPrice(SessionPersistenceSnapshot s) => SavedBookings(s)?.TicketPricePennies ?? FestivalTickets.PricePennies(s.Preparation!.Tier);
    private static long SavedOpeningCash(SessionPersistenceSnapshot s) => s.Preparation!.ClassicBooking is not null
        ? s.Preparation.CarriedIn?.CashPennies ?? CampaignDefaults.OpeningLoanPrincipalPennies : OpeningCashFor(s.Preparation.Tier, s.Preparation.CarriedIn);
    private static long SavedBookingCashChange(SessionPersistenceSnapshot s) => SavedBookings(s)?.CashChangePennies ?? 0;
    private (int Tickets, int Price) ClassicTicketFactsAt(int number)
    {
        var view = number == FestivalNumber ? ClassicBookings! : ClassicBookingLedger.Evaluate(CampaignSeed, number,
            _classicHistory!.CompletedFestivals.Single(r => r.Number == number).Booking!);
        return (view.TicketHolders, view.TicketPricePennies);
    }
    private static (int Tickets, int Price) SavedClassicTicketFactsAt(SessionPersistenceSnapshot s, int number)
    {
        var view = number == s.CampaignPlanning!.EditionNumber ? SavedBookings(s)! : ClassicBookingLedger.Evaluate(s.CampaignSeed, number,
            s.ClassicHistory!.CompletedFestivals.Single(r => r.Number == number).Booking!);
        return (view.TicketHolders, view.TicketPricePennies);
    }
    private void FilterClassicReferenceRoster(SessionPersistenceSnapshot s)
    {
        if (s.Preparation?.ClassicBooking is null) return;
        var booking = SavedBookings(s)!;
        var p = PreparationView!;
        var ids = p.People.Where(a => a.Role == ProtectedPersonRole.Guest).OrderBy(a => a.AgentId)
            .Take(SavedGuestCount(s)).Select(a => a.AgentId).ToHashSet();
        PreparationView = p with { People = p.People.Where(a => a.Role != ProtectedPersonRole.Guest || ids.Contains(a.AgentId))
            .Select(a => a.Role == ProtectedPersonRole.Guest && booking.Announced.Length > 0
                ? a with { ExpectedGenre = ClassicTicketFanGenre(a.AgentId, booking.Announced) } : a).ToArray() };
    }
    private static string? ValidateClassicBookingHeader(SessionPersistenceSnapshot s)
    {
        if (!SavedClassic(s)) return s.Preparation?.ClassicBooking is null ? null : "A Classic booking journal belongs only to Classic.";
        if (s.Preparation?.ClassicBooking is not { } state || s.CampaignPlanning is not { EditionNumber: >= 1 } planning)
            return "Classic booking journal is required; earlier Classic saves need an explicit migration.";
        try
        {
            var current = ClassicBookingLedger.Evaluate(s.CampaignSeed, planning.EditionNumber, state);
            if (s.Preparation.Status == PreparationStatus.Preparing ? current.Phase == ClassicBookingPhase.Opened : current.Phase != ClassicBookingPhase.Opened)
                return "Classic booking and preparation phases disagree.";
            if (s.ClassicHistory?.CompletedFestivals is not { } records || records.Length != planning.EditionNumber - 1)
                return "Classic booking history is missing.";
            foreach (var (record, index) in records.Select((r, i) => (r, i)))
                if (record is null || record.Number != index + 1 || record.Booking is null ||
                    ClassicBookingLedger.Evaluate(s.CampaignSeed, record.Number, record.Booking).Phase != ClassicBookingPhase.Opened)
                    return "A closed Classic festival lacks its opened booking journal.";
            return null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { return "Classic booking journal is invalid: " + error.Message; }
    }
    private static string? ValidateClassicBookingQuotes(SessionPersistenceSnapshot s)
    {
        if (!SavedClassic(s)) return null;
        var p = s.Preparation!;
        var factory = CreateFoodAndDrinkBaseline(s.CampaignSeed, 1, identity: s.Identity,
            classicFestivalNumber: s.CampaignPlanning!.EditionNumber).WithPaymentStanding(p);
        try
        {
            var booking = ClassicBookingLedger.Evaluate(s.CampaignSeed, s.CampaignPlanning.EditionNumber, p.ClassicBooking!, (reputation, quotes) =>
            {
                factory.PreparationView = factory.PreparationView! with { Reputation = reputation,
                    SceneCredibility = p.StandingBefore?.SceneCredibility ?? p.SceneCredibility,
                    ActPopularity = p.ActPopularity, ActRelationships = p.ActRelationships };
                return quotes.All(q => factory.FestivalActs.SingleOrDefault(a => a.Id == q.ActId) is { } act && factory.ActWillPlay(act.Id) &&
                    q.Genre == act.Genre && q.Popularity == act.Popularity && q.PaidFeePennies == factory.ActFee(act));
            });
            if (p.Status == PreparationStatus.Preparing && p.Reputation != booking.Reputation)
                return "Classic preparation reputation differs from its cancellation history.";
            if (booking.Phase != ClassicBookingPhase.Booking && !p.Plan!.ActIds.SequenceEqual(booking.Contracts.Select(a => a.ActId)))
                return "Classic programme differs from its current paid contracts.";
            return null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { return "Classic paid contract quote is invalid: " + error.Message; }
    }
    private static string? ValidateClassicHistoricalQuotes(SessionPersistenceSnapshot s, ClassicFestivalRecord record)
    {
        var (relationships, popularity) = Replay(s.Preparation!.RelationshipHistory?.Where(g => g.Tier < record.Number), false, true,
            number => SavedClassicTicketFactsAt(s, number));
        var factory = CreateFoodAndDrinkBaseline(s.CampaignSeed, 1, identity: s.Identity, classicFestivalNumber: record.Number);
        try
        {
            _ = ClassicBookingLedger.Evaluate(s.CampaignSeed, record.Number, record.Booking!, (reputation, quotes) =>
            {
                factory.PreparationView = factory.PreparationView! with { Reputation = reputation, SceneCredibility = record.OpeningStanding.SceneCredibility,
                    ActRelationships = relationships, ActPopularity = popularity };
                return quotes.All(q => factory.FestivalActs.SingleOrDefault(a => a.Id == q.ActId) is { } act && factory.ActWillPlay(act.Id) &&
                    q.Genre == act.Genre && q.Popularity == act.Popularity && q.PaidFeePennies == factory.ActFee(act));
            });
            return null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { return "Classic historical contract quote is invalid: " + error.Message; }
    }
}
