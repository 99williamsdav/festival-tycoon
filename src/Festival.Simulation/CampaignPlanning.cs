namespace Festival.Simulation;

public enum FestivalPalette
{
    Meadow = 0,
    Marigold = 1,
    Berry = 2,
    River = 3,
}

public enum PlanningCommitmentStatus
{
    Available = 1,
    Confirmed = 2,
    Paid = 3,
}

public static class CampaignDefaults
{
    public const string SiteId = "site.lower-wittering-farm";
    public const string BasicAdministrationCommitmentId = "commitment.basic-administration-cover";
    public const long OpeningCashPennies = 80_000;
    public const long OpeningLoanPrincipalPennies = 80_000;
    public const long EditionPrincipalPennies = 16_000;
    public const int InterestBasisPoints = 800;
    public const int LoanTermEditions = 5;
    public const long BasicAdministrationPennies = 4_000;

    private static readonly string[] FestivalNames =
    [
        "Wittering Field Notes",
        "Hedgerow Assembly",
        "The Lantern Acre",
        "Meadowline Festival",
        "Barn Door Weekender",
        "Lower Wittering Live",
    ];

    public static string NameFor(ulong seed) => FestivalNames[(int)(seed % (ulong)FestivalNames.Length)];
    public static FestivalPalette PaletteFor(ulong seed) => (FestivalPalette)((seed >> 8) % 4);
    public static ulong SiteSeedFor(ulong campaignSeed) => campaignSeed ^ 0x4c6f776572576974UL;
}

public sealed record LoanSnapshot(
    long OpeningPrincipalPennies,
    long OutstandingPrincipalPennies,
    long PrincipalDueAtSettlementPennies,
    long InterestDueAtSettlementPennies,
    int RemainingEditions,
    int InterestBasisPoints);

public sealed record PlanningCommitmentSnapshot(
    string Id,
    string DisplayName,
    long AmountPennies,
    int DueOnAdvanceFromWeek,
    PlanningCommitmentStatus Status,
    int? ConfirmedInWeek = null);

public sealed record PlanningLedgerTransactionSnapshot(
    ulong Id,
    string Reason,
    int PlanningWeek,
    IReadOnlyList<LedgerEntry> Entries)
{
    public bool IsBalanced => Entries.Sum(entry => (decimal)entry.AmountPennies) == 0m;
}

public sealed record WeeklyPaymentSnapshot(string CommitmentId, string DisplayName, long AmountPennies);

public sealed record WeeklyDigestSnapshot(
    int FromWeek,
    int ToWeek,
    SessionPhase PhaseAfter,
    IReadOnlyList<WeeklyPaymentSnapshot> Payments,
    int RemainingCommitments,
    long CashPennies,
    long OutstandingDebtPennies,
    IReadOnlyList<string> Warnings)
{
    public string Summary =>
        $"Planning W{FromWeek} advanced to {(PhaseAfter == SessionPhase.OpeningCheck ? "Opening Check" : $"W{ToWeek}")}. " +
        $"Payments: {(Payments.Count == 0 ? "none" : string.Join(", ", Payments.Select(item => $"{item.DisplayName} £{item.AmountPennies / 100m:0.00}")))}. " +
        $"Remaining commitments: {RemainingCommitments}. Cash: £{CashPennies / 100m:0.00}. Debt: £{OutstandingDebtPennies / 100m:0.00}. " +
        $"Warnings: {(Warnings.Count == 0 ? "none" : string.Join("; ", Warnings))}.";
}

public sealed record WeekAdvancePreview(
    int FromWeek,
    SessionPhase PhaseAfter,
    IReadOnlyList<WeeklyPaymentSnapshot> DuePayments,
    long CashBeforePennies,
    long CashAfterPennies,
    long PrincipalDueAtSettlementPennies,
    long InterestDueAtSettlementPennies);

public sealed record CampaignPlanningSnapshot(
    string FestivalName,
    FestivalPalette Palette,
    string SiteId,
    ulong SiteSeed,
    int EditionNumber,
    int PlanningWeek,
    EntityId FinanceOwnerId,
    LoanSnapshot Loan,
    IReadOnlyList<PlanningCommitmentSnapshot> Commitments,
    IReadOnlyList<PlanningLedgerTransactionSnapshot> LedgerTransactions,
    IReadOnlyList<WeeklyDigestSnapshot> WeeklyDigests,
    IReadOnlyList<string> DismissedTipIds);

internal sealed class CampaignPlanningState
{
    public required string FestivalName { get; set; }
    public FestivalPalette Palette { get; set; }
    public required string SiteId { get; init; }
    public ulong SiteSeed { get; init; }
    public int EditionNumber { get; init; }
    public int PlanningWeek { get; set; }
    public EntityId FinanceOwnerId { get; init; }
    public required LoanSnapshot Loan { get; init; }
    public List<PlanningCommitmentSnapshot> Commitments { get; } = [];
    public List<PlanningLedgerTransactionSnapshot> LedgerTransactions { get; } = [];
    public List<WeeklyDigestSnapshot> WeeklyDigests { get; } = [];
    public SortedSet<string> DismissedTipIds { get; } = new(StringComparer.Ordinal);
}

public sealed partial class GameSession
{
    private CampaignPlanningState? _campaignPlanning;

    internal CampaignPlanningState? CampaignPlanningState => _campaignPlanning;

    public static GameSession CreateCampaign(ulong campaignSeed, CampaignId? campaignId = null)
    {
        var session = new GameSession(campaignSeed, campaignId) { Phase = SessionPhase.Planning };
        var financeOwner = new EntityId(session.NextEntityId++);
        session._festivalFinances.Add(financeOwner, new FestivalFinanceState
        {
            OwnerId = financeOwner,
            CashPennies = CampaignDefaults.OpeningCashPennies,
        });
        var interest = CampaignDefaults.OpeningLoanPrincipalPennies * CampaignDefaults.InterestBasisPoints / 10_000;
        session._campaignPlanning = new CampaignPlanningState
        {
            FestivalName = CampaignDefaults.NameFor(campaignSeed),
            Palette = CampaignDefaults.PaletteFor(campaignSeed),
            SiteId = CampaignDefaults.SiteId,
            SiteSeed = CampaignDefaults.SiteSeedFor(campaignSeed),
            EditionNumber = 1,
            PlanningWeek = 8,
            FinanceOwnerId = financeOwner,
            Loan = new LoanSnapshot(
                CampaignDefaults.OpeningLoanPrincipalPennies,
                CampaignDefaults.OpeningLoanPrincipalPennies,
                CampaignDefaults.EditionPrincipalPennies,
                interest,
                CampaignDefaults.LoanTermEditions,
                CampaignDefaults.InterestBasisPoints),
        };
        session._campaignPlanning.Commitments.Add(new PlanningCommitmentSnapshot(
            CampaignDefaults.BasicAdministrationCommitmentId,
            "Basic administration and cover",
            CampaignDefaults.BasicAdministrationPennies,
            8,
            PlanningCommitmentStatus.Available));
        session._campaignPlanning.LedgerTransactions.Add(new PlanningLedgerTransactionSnapshot(
            1,
            "Starter loan funding",
            8,
            [
                new LedgerEntry(financeOwner, LedgerAccountType.CashAsset, CampaignDefaults.OpeningCashPennies),
                new LedgerEntry(financeOwner, LedgerAccountType.LoanPrincipalLiability, -CampaignDefaults.OpeningLoanPrincipalPennies),
            ]));
        return session;
    }

    public CampaignPlanningSnapshot? CaptureCampaignPlanningSnapshot()
    {
        if (_campaignPlanning is null) return null;
        return new CampaignPlanningSnapshot(
            _campaignPlanning.FestivalName,
            _campaignPlanning.Palette,
            _campaignPlanning.SiteId,
            _campaignPlanning.SiteSeed,
            _campaignPlanning.EditionNumber,
            _campaignPlanning.PlanningWeek,
            _campaignPlanning.FinanceOwnerId,
            _campaignPlanning.Loan,
            _campaignPlanning.Commitments.ToArray(),
            _campaignPlanning.LedgerTransactions.ToArray(),
            _campaignPlanning.WeeklyDigests.ToArray(),
            _campaignPlanning.DismissedTipIds.ToArray());
    }

    public void RenameFestival(string festivalName)
    {
        if (_campaignPlanning is null) throw new InvalidOperationException("This session has no campaign identity.");
        var normalized = festivalName.Trim();
        if (normalized.Length is < 1 or > 48 || normalized.Any(char.IsControl))
            throw new ArgumentException("Festival name must contain 1-48 printable characters.", nameof(festivalName));
        _campaignPlanning.FestivalName = normalized;
    }

    public void SelectPalette(FestivalPalette palette)
    {
        if (_campaignPlanning is null) throw new InvalidOperationException("This session has no campaign identity.");
        if (!Enum.IsDefined(palette)) throw new ArgumentOutOfRangeException(nameof(palette));
        _campaignPlanning.Palette = palette;
    }

    public WeekAdvancePreview GetWeekAdvancePreview()
    {
        if (_campaignPlanning is null || Phase != SessionPhase.Planning)
            throw new InvalidOperationException("A week preview is available only during campaign planning.");
        var due = _campaignPlanning.Commitments
            .Where(item => item.Status == PlanningCommitmentStatus.Confirmed && item.DueOnAdvanceFromWeek == _campaignPlanning.PlanningWeek)
            .Select(item => new WeeklyPaymentSnapshot(item.Id, item.DisplayName, item.AmountPennies))
            .ToArray();
        var cash = _festivalFinances[_campaignPlanning.FinanceOwnerId].CashPennies;
        return new WeekAdvancePreview(
            _campaignPlanning.PlanningWeek,
            _campaignPlanning.PlanningWeek == 1 ? SessionPhase.OpeningCheck : SessionPhase.Planning,
            due,
            cash,
            checked(cash - due.Sum(item => item.AmountPennies)),
            _campaignPlanning.Loan.PrincipalDueAtSettlementPennies,
            _campaignPlanning.Loan.InterestDueAtSettlementPennies);
    }

    private void ApplyConfirmPlanningCommitment(ConfirmPlanningCommitmentCommand command)
    {
        var index = _campaignPlanning!.Commitments.FindIndex(item => item.Id == command.CommitmentId);
        var commitment = _campaignPlanning.Commitments[index];
        _campaignPlanning.Commitments[index] = commitment with
        {
            Status = PlanningCommitmentStatus.Confirmed,
            ConfirmedInWeek = _campaignPlanning.PlanningWeek,
            DueOnAdvanceFromWeek = _campaignPlanning.PlanningWeek,
        };
    }

    private void ApplyDismissTip(DismissCampaignTipCommand command) => _campaignPlanning!.DismissedTipIds.Add(command.TipId);

    private void ApplyAdvancePlanningWeek()
    {
        var campaign = _campaignPlanning!;
        var fromWeek = campaign.PlanningWeek;
        var due = campaign.Commitments
            .Where(item => item.Status == PlanningCommitmentStatus.Confirmed && item.DueOnAdvanceFromWeek == fromWeek)
            .ToArray();
        var cash = _festivalFinances[campaign.FinanceOwnerId];
        foreach (var commitment in due)
        {
            cash.CashPennies = checked(cash.CashPennies - commitment.AmountPennies);
            var index = campaign.Commitments.FindIndex(item => item.Id == commitment.Id);
            campaign.Commitments[index] = commitment with { Status = PlanningCommitmentStatus.Paid };
            campaign.LedgerTransactions.Add(new PlanningLedgerTransactionSnapshot(
                checked((ulong)campaign.LedgerTransactions.Count + 1),
                commitment.DisplayName,
                fromWeek,
                [
                    new LedgerEntry(campaign.FinanceOwnerId, LedgerAccountType.AdministrationExpense, commitment.AmountPennies),
                    new LedgerEntry(campaign.FinanceOwnerId, LedgerAccountType.CashAsset, -commitment.AmountPennies),
                ]));
        }

        campaign.PlanningWeek--;
        if (campaign.PlanningWeek == 0) Phase = SessionPhase.OpeningCheck;
        var warnings = new[] { "Settlement forecast includes £160.00 principal and £64.00 interest; neither is due during planning." };
        campaign.WeeklyDigests.Add(new WeeklyDigestSnapshot(
            fromWeek,
            campaign.PlanningWeek,
            Phase,
            due.Select(item => new WeeklyPaymentSnapshot(item.Id, item.DisplayName, item.AmountPennies)).ToArray(),
            campaign.Commitments.Count(item => item.Status == PlanningCommitmentStatus.Confirmed),
            cash.CashPennies,
            campaign.Loan.OutstandingPrincipalPennies,
            warnings));
    }

    private PersistedCampaignPlanning? CapturePersistedCampaignPlanning()
    {
        if (_campaignPlanning is null) return null;
        var loan = _campaignPlanning.Loan;
        return new PersistedCampaignPlanning(
            _campaignPlanning.FestivalName,
            (int)_campaignPlanning.Palette,
            _campaignPlanning.SiteId,
            _campaignPlanning.SiteSeed,
            _campaignPlanning.EditionNumber,
            _campaignPlanning.PlanningWeek,
            _campaignPlanning.FinanceOwnerId.Value,
            loan.OpeningPrincipalPennies,
            loan.OutstandingPrincipalPennies,
            loan.PrincipalDueAtSettlementPennies,
            loan.InterestDueAtSettlementPennies,
            loan.RemainingEditions,
            loan.InterestBasisPoints,
            _campaignPlanning.Commitments.Select(item => new PersistedPlanningCommitment(
                item.Id, item.DisplayName, item.AmountPennies, item.DueOnAdvanceFromWeek, (int)item.Status, item.ConfirmedInWeek)).ToArray(),
            _campaignPlanning.LedgerTransactions.Select(item => new PersistedPlanningLedgerTransaction(
                item.Id, item.Reason, item.PlanningWeek,
                item.Entries.Select(entry => new PersistedLedgerEntry(entry.OwnerId.Value, (int)entry.Account, entry.AmountPennies)).ToArray())).ToArray(),
            _campaignPlanning.WeeklyDigests.Select(item => new PersistedWeeklyDigest(
                item.FromWeek, item.ToWeek, (int)item.PhaseAfter,
                item.Payments.Select(payment => new PersistedWeeklyPayment(payment.CommitmentId, payment.DisplayName, payment.AmountPennies)).ToArray(),
                item.RemainingCommitments, item.CashPennies, item.OutstandingDebtPennies, item.Warnings.ToArray())).ToArray(),
            _campaignPlanning.DismissedTipIds.ToArray());
    }

    private void RestoreCampaignPlanning(PersistedCampaignPlanning? persisted)
    {
        if (persisted is null) return;
        _campaignPlanning = new CampaignPlanningState
        {
            FestivalName = persisted.FestivalName,
            Palette = (FestivalPalette)persisted.Palette,
            SiteId = persisted.SiteId,
            SiteSeed = persisted.SiteSeed,
            EditionNumber = persisted.EditionNumber,
            PlanningWeek = persisted.PlanningWeek,
            FinanceOwnerId = new EntityId(persisted.FinanceOwnerId),
            Loan = new LoanSnapshot(
                persisted.LoanOpeningPrincipalPennies,
                persisted.LoanOutstandingPrincipalPennies,
                persisted.LoanPrincipalDueAtSettlementPennies,
                persisted.LoanInterestDueAtSettlementPennies,
                persisted.LoanRemainingEditions,
                persisted.LoanInterestBasisPoints),
        };
        _campaignPlanning.Commitments.AddRange(persisted.Commitments.Select(item => new PlanningCommitmentSnapshot(
            item.Id, item.DisplayName, item.AmountPennies, item.DueOnAdvanceFromWeek, (PlanningCommitmentStatus)item.Status,
            item.ConfirmedInWeek ?? (item.Status == (int)PlanningCommitmentStatus.Available ? null : item.DueOnAdvanceFromWeek))));
        _campaignPlanning.LedgerTransactions.AddRange(persisted.LedgerTransactions.Select(item => new PlanningLedgerTransactionSnapshot(
            item.Id, item.Reason, item.PlanningWeek,
            item.Entries.Select(entry => new LedgerEntry(new EntityId(entry.OwnerId), (LedgerAccountType)entry.Account, entry.AmountPennies)).ToArray())));
        _campaignPlanning.WeeklyDigests.AddRange(persisted.WeeklyDigests.Select(item => new WeeklyDigestSnapshot(
            item.FromWeek, item.ToWeek, (SessionPhase)item.PhaseAfter,
            item.Payments.Select(payment => new WeeklyPaymentSnapshot(payment.CommitmentId, payment.DisplayName, payment.AmountPennies)).ToArray(),
            item.RemainingCommitments, item.CashPennies, item.OutstandingDebtPennies, item.Warnings)));
        foreach (var tipId in persisted.DismissedTipIds) _campaignPlanning.DismissedTipIds.Add(tipId);
    }

    private static string? ValidatePersistedCampaignPlanning(PersistedCampaignPlanning? campaign, SessionPersistenceSnapshot snapshot)
    {
        if (campaign is null) return null; // M0 v1 saves intentionally carry no M1 campaign extension.
        if (string.IsNullOrWhiteSpace(campaign.FestivalName) || campaign.FestivalName.Length > 48 || campaign.FestivalName.Any(char.IsControl) ||
            !Enum.IsDefined(typeof(FestivalPalette), campaign.Palette) || campaign.SiteId != CampaignDefaults.SiteId || campaign.SiteSeed == 0 ||
            campaign.EditionNumber != 1 || campaign.PlanningWeek is < 0 or > 8 || campaign.FinanceOwnerId == 0 ||
            !snapshot.FestivalFinances.Any(item => item.OwnerId == campaign.FinanceOwnerId))
            return "Campaign identity, inherited site, edition, week or finance owner is invalid.";
        if (snapshot.Preparation is null && (snapshot.Phase == (int)SessionPhase.Planning && campaign.PlanningWeek is < 1 or > 8 ||
            snapshot.Phase == (int)SessionPhase.OpeningCheck && campaign.PlanningWeek != 0))
            return "Campaign phase and planning week are inconsistent.";
        if (campaign.LoanOpeningPrincipalPennies != CampaignDefaults.OpeningLoanPrincipalPennies ||
            campaign.LoanOutstandingPrincipalPennies < 0 || campaign.LoanOutstandingPrincipalPennies > campaign.LoanOpeningPrincipalPennies ||
            campaign.LoanPrincipalDueAtSettlementPennies < 0 || campaign.LoanInterestDueAtSettlementPennies < 0 ||
            campaign.LoanRemainingEditions is < 0 or > CampaignDefaults.LoanTermEditions || campaign.LoanInterestBasisPoints != CampaignDefaults.InterestBasisPoints)
            return "Campaign loan state is invalid.";
        if (campaign.Commitments is null || campaign.LedgerTransactions is null || campaign.WeeklyDigests is null || campaign.DismissedTipIds is null ||
            campaign.Commitments.Any(item => item is null) || campaign.LedgerTransactions.Any(item => item is null) || campaign.WeeklyDigests.Any(item => item is null))
            return "Campaign planning collections must be present and contain no null records.";
        if (campaign.Commitments.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != campaign.Commitments.Length ||
            campaign.Commitments.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.DisplayName) || item.AmountPennies <= 0 ||
                item.DueOnAdvanceFromWeek is < 1 or > 8 || !Enum.IsDefined(typeof(PlanningCommitmentStatus), item.Status) ||
                item.ConfirmedInWeek is < 1 or > 8 ||
                item.Status == (int)PlanningCommitmentStatus.Available && item.ConfirmedInWeek is not null ||
                item.Status != (int)PlanningCommitmentStatus.Available && item.ConfirmedInWeek is not null && item.ConfirmedInWeek != item.DueOnAdvanceFromWeek))
            return "Campaign commitment state is invalid.";
        if (!campaign.LedgerTransactions.Select(item => item.Id).SequenceEqual(Enumerable.Range(1, campaign.LedgerTransactions.Length).Select(value => (ulong)value)) ||
            campaign.LedgerTransactions.Any(item => string.IsNullOrWhiteSpace(item.Reason) || item.PlanningWeek is < 1 or > 8 || item.Entries is null ||
                item.Entries.Any(entry => entry is null || entry.OwnerId != campaign.FinanceOwnerId || !Enum.IsDefined(typeof(LedgerAccountType), entry.Account)) ||
                item.Entries.Sum(entry => (decimal)entry.AmountPennies) != 0m))
            return "Campaign ledger transactions must be contiguous, valid and balanced.";
        if (campaign.WeeklyDigests.Length != 8 - campaign.PlanningWeek || campaign.WeeklyDigests.Where((item, index) =>
                item.FromWeek != 8 - index || item.ToWeek != 7 - index || item.Payments is null || item.Warnings is null ||
                item.CashPennies < 0 || item.OutstandingDebtPennies < 0 || !Enum.IsDefined(typeof(SessionPhase), item.PhaseAfter) ||
                item.Payments.Any(payment => payment is null || string.IsNullOrWhiteSpace(payment.CommitmentId) ||
                    string.IsNullOrWhiteSpace(payment.DisplayName) || payment.AmountPennies <= 0) ||
                item.Warnings.Any(string.IsNullOrWhiteSpace)).Any())
            return "Campaign weekly digest history is not a contiguous factual planning sequence.";
        if (campaign.DismissedTipIds.Any(string.IsNullOrWhiteSpace) ||
            !campaign.DismissedTipIds.SequenceEqual(campaign.DismissedTipIds.Order(StringComparer.Ordinal)) ||
            campaign.DismissedTipIds.Distinct(StringComparer.Ordinal).Count() != campaign.DismissedTipIds.Length)
            return "Dismissed campaign tip IDs must be nonempty, sorted and unique.";
        return null;
    }
}
