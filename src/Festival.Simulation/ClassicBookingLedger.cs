namespace Festival.Simulation;

public enum ClassicBookingPhase { Booking, SitePreparation, Opened }
public enum ClassicBookingActionKind { Confirm, Rebook, Cancel, Open }
public enum ClassicBookingMoneyKind { Tickets, BandFee, BandRelease, TicketRefund }
public sealed record ClassicBookingAction(ClassicBookingActionKind Kind, int TicketPricePennies, ClassicAnnouncedAct[] Contracts);
/// <summary>Command facts only. Sales, refunds and cash movements are regenerated, never trusted from a second balance.</summary>
public sealed record ClassicBookingSnapshot(int Version, int Capacity, int InitialReputation, int DraftTicketPricePennies,
    ClassicBookingAction[] Actions)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ClassicBookingDevelopment? Development {get;init;}
}
public sealed record ClassicBookingDevelopment(int Slots,bool GuaranteeHundred);
public sealed record ClassicBookingMoney(int Sequence, int Attempt, ClassicBookingMoneyKind Kind, string? ActId,
    int Quantity, int UnitPennies)
{
    public long CashPennies => checked((long)Quantity * UnitPennies * (Kind is ClassicBookingMoneyKind.Tickets or ClassicBookingMoneyKind.BandRelease ? 1 : -1));
    public LedgerEntry[] Entries(EntityId owner) =>
    [
        new(owner, LedgerAccountType.CashAsset, CashPennies),
        new(owner, Kind is ClassicBookingMoneyKind.Tickets or ClassicBookingMoneyKind.TicketRefund
            ? LedgerAccountType.SalesRevenue : LedgerAccountType.AdministrationExpense, -CashPennies)
    ];
}
public sealed record ClassicBookingView(ClassicBookingPhase Phase, int Attempt, int Reputation,
    int TicketPricePennies, int OriginalTickets, int RefundedTickets, ClassicLineupChange HighestChange,
    ClassicAnnouncedAct[] Announced, ClassicAnnouncedAct[] Contracts, ClassicBookingMoney[] Money)
{
    public int TicketHolders => OriginalTickets - RefundedTickets;
    public long CashChangePennies => Money.Sum(item => item.CashPennies);
    public long NetTicketReceiptsPennies => Money.Where(item => item.Kind is ClassicBookingMoneyKind.Tickets or ClassicBookingMoneyKind.TicketRefund).Sum(item => item.CashPennies);
    public long NetBandCostsPennies => -Money.Where(item => item.Kind is ClassicBookingMoneyKind.BandFee or ClassicBookingMoneyKind.BandRelease).Sum(item => item.CashPennies);
    public int CancelledAttempts => Attempt - 1;
    public int Disappointment => ClassicBookingTerms.SatisfactionPenalty(HighestChange);
}

/// <summary>
/// Replays the small pre-opening contract journal into financial facts. The session adapter owns actual cash;
/// this journal proves its booking-related changes. There is no borrowing rule or programme allowance here.
/// </summary>
public static class ClassicBookingLedger
{
    public static ClassicBookingSnapshot Create(int reputation) => new(1, ClassicTicketDemand.InheritedCapacity,
        reputation, ClassicTicketDemand.DefaultTicketPricePennies, []);
    public static ClassicBookingSnapshot CreateDevelopment(int reputation,int slots,bool guaranteeHundred)=>
        Create(reputation) with {Capacity=GameSession.ClassicDevelopmentCapacity,Development=new(slots,guaranteeHundred)};
    public static ClassicBookingSnapshot Clone(ClassicBookingSnapshot state) => state with
    { Actions = state.Actions.Select(a => a with { Contracts = a.Contracts.ToArray() }).ToArray() };

    public static ClassicBookingSnapshot SetPrice(ulong seed, int number, ClassicBookingSnapshot state, int price)
    {
        if (Evaluate(seed, number, state).Phase != ClassicBookingPhase.Booking)
            throw new InvalidOperationException("The announced ticket price is locked after sales. Cancel before repricing.");
        if (price is < 0 or > ClassicTicketDemand.MaximumTicketPricePennies) throw new ArgumentOutOfRangeException(nameof(price));
        return state with { DraftTicketPricePennies = price };
    }
    public static ClassicBookingSnapshot Confirm(ulong seed, int number, ClassicBookingSnapshot state, ClassicAnnouncedAct[] contracts) =>
        Append(seed, number, state, new(ClassicBookingActionKind.Confirm, state.DraftTicketPricePennies, contracts));
    public static ClassicBookingSnapshot Rebook(ulong seed, int number, ClassicBookingSnapshot state, ClassicAnnouncedAct[] contracts) =>
        Append(seed, number, state, new(ClassicBookingActionKind.Rebook, 0, contracts));
    public static ClassicBookingSnapshot Cancel(ulong seed, int number, ClassicBookingSnapshot state) =>
        Append(seed, number, state, new(ClassicBookingActionKind.Cancel, 0, []));
    public static ClassicBookingSnapshot Open(ulong seed, int number, ClassicBookingSnapshot state) =>
        Append(seed, number, state, new(ClassicBookingActionKind.Open, 0, []));
    private static ClassicBookingSnapshot Append(ulong seed, int number, ClassicBookingSnapshot state, ClassicBookingAction action)
    {
        var next = state with { Actions = [.. state.Actions, action with { Contracts = action.Contracts.ToArray() }] };
        _ = Evaluate(seed, number, next);
        return next;
    }

    public static ClassicBookingView Evaluate(ulong seed, int number, ClassicBookingSnapshot state,
        Func<int, IReadOnlyList<ClassicAnnouncedAct>, bool>? validateQuotes = null)
    {
        if (state is null || state.Version != 1 || (state.Development is {} dev
                ?state.Capacity!=GameSession.ClassicDevelopmentCapacity||dev.Slots is not(3 or 6 or 9)
                :state.Capacity!=ClassicTicketDemand.InheritedCapacity) ||
            state.InitialReputation is < 0 or > 100 || state.DraftTicketPricePennies is < 0 or > ClassicTicketDemand.MaximumTicketPricePennies ||
            state.Actions is null || number < 1) throw new ArgumentException("Invalid Classic booking header.");
        var phase = ClassicBookingPhase.Booking; var attempt = 1; var reputation = state.InitialReputation;
        var price = state.DraftTicketPricePennies; var tickets = 0; var refunded = 0;
        var severity = ClassicLineupChange.None;
        ClassicAnnouncedAct[] announced = [], contracts = [];
        var money = new List<ClassicBookingMoney>();
        foreach (var action in state.Actions)
        {
            if (action is null || action.Contracts is null || !Enum.IsDefined(action.Kind) ||
                action.Kind != ClassicBookingActionKind.Confirm && action.TicketPricePennies != 0)
                throw new ArgumentException("Invalid Classic booking action.");
            switch (action.Kind)
            {
                case ClassicBookingActionKind.Confirm:
                    if (phase != ClassicBookingPhase.Booking || action.TicketPricePennies is < 0 or > ClassicTicketDemand.MaximumTicketPricePennies)
                        throw new InvalidOperationException("Only an unannounced complete booking can resolve sales.");
                    ValidateContracts(action.Contracts, reputation, false);
                    announced = action.Contracts.ToArray(); contracts = announced.ToArray(); price = action.TicketPricePennies;
                    tickets = state.Development?.GuaranteeHundred==true?GameSession.ClassicDevelopmentCapacity:
                        ClassicTicketDemand.Resolve(seed, number, new(state.Capacity, reputation, price, announced.Select(a => a.Popularity).OrderDescending().Take(3).ToArray()));
                    Add(ClassicBookingMoneyKind.Tickets, null, tickets, price);
                    foreach (var act in contracts) Add(ClassicBookingMoneyKind.BandFee, act.ActId, 1, act.PaidFeePennies);
                    refunded = 0; severity = ClassicLineupChange.None; phase = ClassicBookingPhase.SitePreparation;
                    break;
                case ClassicBookingActionKind.Rebook:
                    if (phase != ClassicBookingPhase.SitePreparation) throw new InvalidOperationException("Only an announced festival can rebook before opening.");
                    ValidateContracts(action.Contracts, reputation, true);
                    foreach (var held in contracts.Where(a => a.ActId != ""))
                    {
                        var retained = action.Contracts.SingleOrDefault(a => a.ActId == held.ActId);
                        if (retained is not null && retained != held) throw new ArgumentException("A retained contract cannot change its paid quote.");
                        if (retained is null) Add(ClassicBookingMoneyKind.BandRelease, held.ActId, 1, held.PaidFeePennies);
                    }
                    foreach (var added in action.Contracts.Where(a => a.ActId != "" && !contracts.Any(held => held.ActId == a.ActId)))
                        Add(ClassicBookingMoneyKind.BandFee, added.ActId, 1, added.PaidFeePennies);
                    contracts = action.Contracts.ToArray();
                    severity = ClassicBookingTerms.Assess(announced,
                        contracts.Select(a => a.ActId == "" ? null : new FestivalAct(a.ActId, a.ActId, a.Genre, a.PaidFeePennies, a.Popularity)).ToArray(), severity);
                    var target = ClassicBookingTerms.RefundTarget(tickets, severity);
                    if (target > refunded) Add(ClassicBookingMoneyKind.TicketRefund, null, target - refunded, price);
                    refunded = target;
                    break;
                case ClassicBookingActionKind.Cancel:
                    if (phase != ClassicBookingPhase.SitePreparation || action.Contracts.Length != 0)
                        throw new InvalidOperationException("Only an announced, unopened festival can be cancelled.");
                    foreach (var held in contracts.Where(a => a.ActId != "")) Add(ClassicBookingMoneyKind.BandRelease, held.ActId, 1, held.PaidFeePennies);
                    if (tickets > refunded) Add(ClassicBookingMoneyKind.TicketRefund, null, tickets - refunded, price);
                    reputation = Math.Max(0, reputation - ClassicBookingTerms.CancellationReputationPenalty);
                    attempt++; phase = ClassicBookingPhase.Booking; tickets = refunded = 0;
                    announced = []; contracts = []; severity = ClassicLineupChange.None;
                    break;
                case ClassicBookingActionKind.Open:
                    if (phase != ClassicBookingPhase.SitePreparation || action.Contracts.Length != 0 || contracts.Any(a => a.ActId == ""))
                        throw new InvalidOperationException("Only confirmed site preparation can open.");
                    phase = ClassicBookingPhase.Opened;
                    break;
            }
        }
        if (phase != ClassicBookingPhase.Booking && state.DraftTicketPricePennies != price)
            throw new ArgumentException("The saved draft price differs from the announced ticket price.");
        if (phase == ClassicBookingPhase.Booking) price = state.DraftTicketPricePennies;
        return new(phase, attempt, reputation, price, tickets, refunded, severity, announced, contracts, money.ToArray());

        void Add(ClassicBookingMoneyKind kind, string? act, int quantity, int unit) =>
            money.Add(new(money.Count + 1, attempt, kind, act, quantity, unit));
        void ValidateContracts(ClassicAnnouncedAct[] quotes, int rep, bool allowEmpty)
        {
            var active = quotes.Where(a => a is not null && a.ActId != "").ToArray();
            if (quotes.Length != (state.Development?.Slots??3) || quotes.Any(a => a is null ||
                (a.ActId == "" ? !allowEmpty || a != ClassicAnnouncedAct.EmptySlot :
                    string.IsNullOrWhiteSpace(a.ActId) || a.Genre is < 0 or >= FestivalGenre.Count || a.Popularity is < 0 or > 100 || a.PaidFeePennies < 0)) ||
                active.Select(a => a.ActId).Distinct().Count() != active.Length || validateQuotes is not null && !validateQuotes(rep, active))
                throw new ArgumentException("Invalid, ineligible or mispriced act contracts.");
        }
    }
}
