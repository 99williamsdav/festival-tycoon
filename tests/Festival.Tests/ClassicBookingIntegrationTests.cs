using Festival.Simulation;
using Festival.Persistence;

namespace Festival.Tests;

[TestClass]
public sealed class ClassicBookingIntegrationTests
{
    private static void Accept(GameSession s, SessionCommand c) => BuildSession.Accept(s, c);
    private static GameSession RoundTrip(GameSession s)
    {
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        return restored.Session;
    }
    private static void ChoosePerk(GameSession s)
    {
        var p = s.CapturePerks()!;
        Accept(s, new ChoosePerkCommand(p.DraftAttempt, p.Cursor, p.Hand[0]));
    }
    private static string[] CheapBands(GameSession s) => s.GetFestivalActs().Where(a => s.ActStandingOf(a) == ActStanding.Available)
        .OrderBy(s.ActFee).ThenBy(a => a.Id, StringComparer.Ordinal).Take(3).Select(a => a.Id).ToArray();
    private static void Site(GameSession s)
    {
        Accept(s, new UseDefaultBuildLayoutCommand());
        Accept(s, new SetPreparationStockCommand(24, 16));
        foreach (var candidate in s.GetStaffCandidates().GroupBy(c => c.Role).Select(g => g.OrderBy(c => c.WagePennies).First()))
            Accept(s, new AcceptPreparationOfferCommand(candidate.Id));
    }
    private static void Finish(GameSession s)
    {
        for (var tick = 0; tick < 60_000 && s.PreparedStatus != PreparationStatus.Finished; tick += 800) s.AdvanceWithoutSnapshot(800);
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
    }

    [TestMethod]
    public void ReleasedHeadlinePersistsBlocksOpeningAndCanBeRefilledWithoutReselling()
    {
        var s = GameSession.CreateClassicCampaign(14); ChoosePerk(s);
        var ids = CheapBands(s);
        Accept(s, new SetProgrammeCommand(ids)); Accept(s, new ConfirmClassicBookingsCommand());
        var original = s.CaptureClassicBookingDetails()!;
        var empty = ids.ToArray(); empty[2] = "";
        Accept(s, new RebookClassicProgrammeCommand(empty));
        s = RoundTrip(s); Site(s);
        Assert.IsFalse(BuildSession.Send(s, new StartPreparedEditionCommand()).IsAccepted);
        var released = s.CaptureClassicBookingDetails()!;
        Assert.AreEqual(ClassicLineupChange.Substantial, released.HighestChange);
        Assert.AreEqual(original.NetBandCostsPennies - original.Contracts[2].PaidFeePennies, released.NetBandCostsPennies);
        Accept(s, new RebookClassicProgrammeCommand(ids));
        Assert.AreEqual(released.RefundedTickets, s.CaptureClassicBookingDetails()!.RefundedTickets);
        Assert.AreEqual(1, s.CaptureClassicBookingDetails()!.Money.Count(m => m.Kind == ClassicBookingMoneyKind.Tickets));
        s = RoundTrip(s); Accept(s, new StartPreparedEditionCommand()); Finish(s);
        Assert.IsTrue(s.CompletedFestivalAccounts!.Reconciles);
        RoundTrip(s);
    }

    [TestMethod]
    public void PaidSaveRejectsForgedHeadersQuotesAndPhasesAndCaptureArraysAreIsolated()
    {
        var s = GameSession.CreateClassicCampaign(14); ChoosePerk(s);
        Accept(s, new SetProgrammeCommand(CheapBands(s))); Accept(s, new ConfirmClassicBookingsCommand());
        var saved = s.CapturePersistenceSnapshot(); var p = saved.Preparation!; var booking = p.ClassicBooking!;
        var quote = booking.Actions[0].Contracts[0];
        Assert.IsFalse(GameSession.Restore(saved with { Preparation = p with { ClassicBooking = null } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Preparation = p with { ClassicBooking = booking with { Capacity = 26 } } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Preparation = p with { ClassicBooking = booking with { DraftTicketPricePennies = 999 } } }).IsSuccess);
        foreach (var forged in new[] { quote with { PaidFeePennies = quote.PaidFeePennies + 1 }, quote with { Popularity = quote.Popularity + 1 } })
        {
            var altered = ClassicBookingLedger.Clone(booking); altered.Actions[0].Contracts[0] = forged;
            Assert.IsFalse(GameSession.Restore(saved with { Preparation = p with { ClassicBooking = altered } }).IsSuccess);
        }
        var opened = ClassicBookingLedger.Open(s.CampaignSeed, s.FestivalNumber, booking);
        Assert.IsFalse(GameSession.Restore(saved with { Preparation = p with { ClassicBooking = opened } }).IsSuccess);
        var hash = s.CaptureSnapshot().AuthoritativeHash;
        var captured = s.CaptureClassicBooking()!; captured.Actions[0].Contracts[0] = quote with { PaidFeePennies = 1 };
        var details = s.CaptureClassicBookingDetails()!; details.Contracts[0] = ClassicAnnouncedAct.EmptySlot;
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
        var restored = RoundTrip(s);
        saved.Preparation!.ClassicBooking!.Actions[0].Contracts[0] = quote with { PaidFeePennies = 1 };
        Assert.AreEqual(hash, restored.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void FreeTicketsUseActualCapacityAndCannotCreateRefundCash()
    {
        var s = GameSession.CreateClassicCampaign(14); ChoosePerk(s);
        Accept(s, new SetProgrammeCommand(CheapBands(s))); Accept(s, new SetClassicTicketPriceCommand(0));
        Accept(s, new ConfirmClassicBookingsCommand());
        var booking = s.CaptureClassicBookingDetails()!;
        Assert.IsTrue(booking.TicketHolders > 0 && booking.TicketHolders <= s.ClassicSiteCapacity);
        Assert.AreEqual(0L, booking.NetTicketReceiptsPennies);
        Accept(s, new RebookClassicProgrammeCommand(["", "", ""]));
        Assert.AreEqual(0L, s.CaptureClassicBookingDetails()!.NetTicketReceiptsPennies);
        Accept(s, new CancelClassicFestivalCommand());
        Assert.AreEqual(20_000L, s.CapturePersistentCampaignState()!.CashPennies);
        RoundTrip(s);
    }

    [TestMethod]
    public void EssentialSetupShortfallWarnsBeforeOverdraftAndStillPermitsConfirmation()
    {
        var s = GameSession.CreateClassicCampaign(14); ChoosePerk(s);
        var ids = s.GetFestivalActs().Where(a => s.ActStandingOf(a) != ActStanding.Locked).OrderByDescending(s.ActFee).Take(3).Select(a => a.Id).ToArray();
        Accept(s, new SetProgrammeCommand(ids)); Accept(s, new SetClassicTicketPriceCommand(10_000));
        var guidance = s.ClassicBookingEstimate!;
        Assert.IsTrue(guidance.ConservativeSetupCashPennies < guidance.MinimumRequiredSitePennies);
        Assert.IsTrue(guidance.ConservativeSetupCashPennies - guidance.MinimumRequiredSitePennies >= -CampaignDefaults.OverdraftPennies);
        Assert.IsTrue(guidance.Risky);
        Accept(s, new ConfirmClassicBookingsCommand()); RoundTrip(s);
    }

    [TestMethod]
    public void PostSaleReleaseRefillCancellationAndZeroSalesSettlementCanSave()
    {
        var s = GameSession.CreateClassicCampaign(14); ChoosePerk(s); var acts = CheapBands(s);
        Accept(s, new SetProgrammeCommand(acts)); Accept(s, new SetClassicTicketPriceCommand(1125));
        Accept(s, new ConfirmClassicBookingsCommand());
        var released = acts.ToArray(); released[2] = "";
        Accept(s, new RebookClassicProgrammeCommand(released)); Accept(s, new RebookClassicProgrammeCommand(acts));
        Accept(s, new CancelClassicFestivalCommand());
        Accept(s, new SetProgrammeCommand(acts)); Accept(s, new SetClassicTicketPriceCommand(10_000));
        Accept(s, new ConfirmClassicBookingsCommand()); Site(s); Accept(s, new StartPreparedEditionCommand()); Finish(s);
        Assert.AreEqual(0, s.CompletedFestivalResult!.AdmittedGuests);
        Assert.IsTrue(s.CompletedFestivalAccounts!.Reconciles);
        s = RoundTrip(s); RoundTrip(s.CreateNextFestival());
    }

    [TestMethod]
    public void CompletedStretchContractKeepsOpeningFeeWhenEarnedSceneStandingImproves()
    {
        var s = GameSession.CreateClassicCampaign(14); ChoosePerk(s);
        var ids = new[] { "act.bus-replacement", "act.two-men-harmonium", "act.glitter-rota" };
        Accept(s, new SetProgrammeCommand(ids)); Accept(s, new ConfirmClassicBookingsCommand());
        var quote = s.CaptureClassicBookingDetails()!.Contracts.Single(a => a.ActId == "act.glitter-rota");
        Assert.AreEqual(4_800, quote.PaidFeePennies);
        Site(s); Accept(s, new StartPreparedEditionCommand()); RoundTrip(s); Finish(s);
        Assert.IsTrue(s.CompletedFestivalAccounts!.Reconciles);
        Assert.IsTrue(s.Standing.SceneCredibility[quote.Genre] > 0);
        var actual = s.GetFestivalActs().Single(a => a.Id == quote.ActId);
        Assert.IsTrue(s.ActFee(actual) < quote.PaidFeePennies, "The played result must actually improve the later quote.");
        s = RoundTrip(s); s = RoundTrip(s.CreateNextFestival());
        Assert.AreEqual(2, s.FestivalNumber);
    }

    [TestMethod]
    public void BookingStartsWithoutTicketCreditAndConfirmationResizesActualGuests()
    {
        var s = RoundTrip(GameSession.CreateClassicCampaign(14));
        Assert.AreEqual(20_000L, s.CapturePersistentCampaignState()!.CashPennies);
        Assert.AreEqual(0, s.CurrentFestival.Attendance);
        Assert.AreEqual(25, s.ClassicSiteCapacity);
        ChoosePerk(s);
        Assert.IsFalse(BuildSession.Send(s, new UseDefaultBuildLayoutCommand()).IsAccepted);
        Accept(s, new SetProgrammeCommand(CheapBands(s)));
        var guidance = s.ClassicBookingEstimate!;
        var before = s.CaptureSnapshot().AuthoritativeHash;
        _ = s.ClassicBookingEstimate;
        Assert.AreEqual(before, s.CaptureSnapshot().AuthoritativeHash);
        s = RoundTrip(s);
        Accept(s, new ConfirmClassicBookingsCommand());
        var booked = s.CaptureClassicBookingDetails()!;
        Assert.AreEqual(ClassicBookingPhase.SitePreparation, booked.Phase);
        Assert.AreEqual(20_000 + booked.CashChangePennies, s.CapturePersistentCampaignState()!.CashPennies);
        Assert.IsTrue(booked.TicketHolders >= guidance.Sales.MinimumSales && booked.TicketHolders <= guidance.Sales.MaximumSales);
        Assert.AreEqual(booked.TicketHolders, s.CapturePreparation()!.People.Count(a => a.Role == ProtectedPersonRole.Guest));
        Assert.AreEqual(booked.TicketHolders, s.CurrentFestival.Attendance);
        Assert.IsFalse(BuildSession.Send(s, new ConfirmClassicBookingsCommand()).IsAccepted);
        RoundTrip(s);
    }

    [TestMethod]
    public void CancellationRefundsActualMoneyAndRestoresSameCandidateIdentities()
    {
        var s = GameSession.CreateClassicCampaign(14);
        var original = s.CapturePreparation()!.People.Select(a => a.AgentId).ToArray();
        var forecast = System.Text.Json.JsonSerializer.Serialize(s.CaptureWeather());
        ChoosePerk(s);
        var acts = CheapBands(s);
        Accept(s, new SetProgrammeCommand(acts));
        Accept(s, new ConfirmClassicBookingsCommand());
        var firstSales = s.CaptureClassicBookingDetails()!.OriginalTickets;
        Accept(s, new UseDefaultBuildLayoutCommand());
        Accept(s, new CancelClassicFestivalCommand());
        Assert.AreEqual(20_000L, s.CapturePersistentCampaignState()!.CashPennies);
        Assert.AreEqual(0, s.CapturePreparationPlan()!.ActIds.Length);
        Assert.AreEqual(0, s.CaptureBuildPlacements().Count);
        Assert.AreEqual(ClassicBookingPhase.Booking, s.CaptureClassicBookingDetails()!.Phase);
        CollectionAssert.AreEqual(original, s.CapturePreparation()!.People.Select(a => a.AgentId).ToArray());
        Assert.AreEqual(forecast, System.Text.Json.JsonSerializer.Serialize(s.CaptureWeather()));
        s = RoundTrip(s);
        Accept(s, new SetProgrammeCommand(acts));
        Accept(s, new ConfirmClassicBookingsCommand());
        Assert.AreEqual(firstSales, s.CaptureClassicBookingDetails()!.OriginalTickets);
        RoundTrip(s);
    }

    [TestMethod]
    public void OpeningPaysOnlyRemainingSiteAndStockCharges()
    {
        var s = GameSession.CreateClassicCampaign(14);
        ChoosePerk(s);
        Accept(s, new SetProgrammeCommand(CheapBands(s)));
        Accept(s, new ConfirmClassicBookingsCommand());
        var bandCost = s.CaptureClassicBookingDetails()!.NetBandCostsPennies;
        Accept(s, new UseDefaultBuildLayoutCommand());
        Accept(s, new SetPreparationStockCommand(24, 16));
        foreach (var candidate in s.GetStaffCandidates().GroupBy(c => c.Role).Select(g => g.OrderBy(c => c.WagePennies).First()))
            Accept(s, new AcceptPreparationOfferCommand(candidate.Id));
        Assert.AreEqual(0, s.GetPreparationStartBlockers().Count);
        var before = s.CapturePersistentCampaignState()!.CashPennies;
        var remaining = s.PreparationRemainingCash;
        s = RoundTrip(s);
        Accept(s, new StartPreparedEditionCommand());
        Assert.AreEqual(remaining, s.CapturePersistentCampaignState()!.CashPennies);
        Assert.AreEqual(bandCost, s.CaptureClassicBookingDetails()!.NetBandCostsPennies);
        Assert.IsTrue(before > remaining);
        Assert.IsFalse(s.CapturePreparation()!.Payments.Any(p => p.OfferId.StartsWith("act.", StringComparison.Ordinal)));
        Assert.AreEqual(ClassicBookingPhase.Opened, s.CaptureClassicBookingDetails()!.Phase);
        RoundTrip(s);
    }

    [TestCategory("Slow")]
    [TestMethod]
    [DataRow(14UL)]
    [DataRow(20260922UL)]
    public void FourPlayedFestivalsReconcileActualTicketsBandFeesAndCashCarry(ulong seed)
    {
        var s = GameSession.CreateClassicCampaign(seed);
        long carried = 20_000;
        var evidence = new List<object>();
        for (var number = 1; number <= 4; number++)
        {
            Assert.AreEqual(carried, s.CapturePersistentCampaignState()!.CashPennies);
            Assert.AreEqual(0, s.CurrentFestival.Attendance);
            ChoosePerk(s);
            Accept(s, new SetProgrammeCommand(CheapBands(s)));
            Accept(s, new ConfirmClassicBookingsCommand());
            if (number == 2)
            {
                var reputation = s.Standing.Reputation;
                Assert.IsTrue(reputation > 5, "Use reputation earned by the actual first festival.");
                var weather = System.Text.Json.JsonSerializer.Serialize(s.CaptureWeather());
                Accept(s, new CancelClassicFestivalCommand());
                Assert.AreEqual(reputation - 5, s.Standing.Reputation);
                Assert.AreEqual(carried, s.CapturePersistentCampaignState()!.CashPennies);
                Assert.IsFalse(BuildSession.Send(s, new CancelClassicFestivalCommand()).IsAccepted);
                s = RoundTrip(s);
                Assert.AreEqual(weather, System.Text.Json.JsonSerializer.Serialize(s.CaptureWeather()));
                Accept(s, new SetProgrammeCommand(CheapBands(s)));
                Accept(s, new ConfirmClassicBookingsCommand());
            }
            var tickets = s.CaptureClassicBookingDetails()!;
            Site(s);
            s = RoundTrip(s);
            Accept(s, new StartPreparedEditionCommand());
            Finish(s);
            var accounts = s.CompletedFestivalAccounts!;
            Assert.IsTrue(accounts.Reconciles, $"Festival {number} accounts do not reconcile.");
            Assert.AreEqual(carried, accounts.OpeningCashPennies);
            Assert.AreEqual(tickets.NetTicketReceiptsPennies, accounts.TicketSalesPennies);
            Assert.AreEqual(tickets.TicketHolders, s.CompletedFestivalResult!.AdmittedGuests);
            Assert.AreEqual(accounts.ClosingCashPennies - carried, s.CompletedFestivalResult.NetCashChangePennies);
            Assert.AreEqual(tickets.NetBandCostsPennies, accounts.OperatingExpenses.Where(e => e.Category == "Act bookings").Sum(e => e.AmountPennies));
            Assert.AreEqual(20_000L, s.CapturePersistentCampaignState()!.Loan.OutstandingPrincipalPennies);
            carried = accounts.ClosingCashPennies;
            s = RoundTrip(s);
            evidence.Add(new { number, accounts, tickets, state = s.CapturePersistenceSnapshot() });
            if (number < 4) s = RoundTrip(s.CreateNextFestival());
        }
        if (Environment.GetEnvironmentVariable("FESTIVAL_CLASSIC_TICKET_EVIDENCE_ROOT") is { Length: > 0 } root)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, $"four-festivals-{seed}.json"), System.Text.Json.JsonSerializer.Serialize(evidence,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }

    [TestMethod]
    public void ZeroSalesCanFinishAndActualShortfallCanBeCancelledWithoutCampaignEnding()
    {
        var s = GameSession.CreateClassicCampaign(14);
        ChoosePerk(s);
        Accept(s, new SetProgrammeCommand(CheapBands(s)));
        Accept(s, new SetClassicTicketPriceCommand(10_000));
        Accept(s, new ConfirmClassicBookingsCommand());
        Assert.AreEqual(0, s.CurrentFestival.Attendance);
        Site(s);
        Accept(s, new StartPreparedEditionCommand());
        Finish(s);
        Assert.AreEqual(0, s.CompletedFestivalResult!.AdmittedGuests);
        Assert.IsNull(s.CompletedFestivalResult.Stars);
        Assert.IsTrue(s.CompletedFestivalAccounts!.Reconciles);
        s = RoundTrip(s);
        s = RoundTrip(s.CreateNextFestival());
        var opening = s.CapturePersistentCampaignState()!.CashPennies;
        ChoosePerk(s);
        var costly = s.GetFestivalActs().Where(a => s.ActStandingOf(a) != ActStanding.Locked)
            .OrderByDescending(s.ActFee).Take(3).Select(a => a.Id).ToArray();
        Accept(s, new SetProgrammeCommand(costly));
        Accept(s, new SetClassicTicketPriceCommand(10_000));
        Assert.IsTrue(s.ClassicBookingEstimate!.Risky);
        Accept(s, new ConfirmClassicBookingsCommand());
        Assert.IsTrue(s.CapturePersistentCampaignState()!.CashPennies < 0);
        Assert.IsTrue(s.GetPreparationStartBlockers().Count > 0);
        s = RoundTrip(s);
        if (Environment.GetEnvironmentVariable("FESTIVAL_CLASSIC_TICKET_EVIDENCE_ROOT") is { Length: > 0 } root)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root,"actual-negative-confirmation.json"),System.Text.Json.JsonSerializer.Serialize(
                new {opening,booking=s.CaptureClassicBookingDetails(),state=s.CapturePersistenceSnapshot()},
                new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
        }
        var forecast = System.Text.Json.JsonSerializer.Serialize(s.CaptureWeather());
        Accept(s, new CancelClassicFestivalCommand());
        Assert.AreEqual(opening, s.CapturePersistentCampaignState()!.CashPennies);
        Assert.AreEqual(2, s.FestivalNumber);
        Assert.AreEqual(ClassicBookingPhase.Booking, s.CaptureClassicBookingDetails()!.Phase);
        Assert.AreEqual(forecast, System.Text.Json.JsonSerializer.Serialize(s.CaptureWeather()));
        s=RoundTrip(s);
        Accept(s,new SetProgrammeCommand(CheapBands(s)));
        Accept(s,new SetClassicTicketPriceCommand(1_000));
        Accept(s,new ConfirmClassicBookingsCommand());
        Site(s);
        Accept(s,new StartPreparedEditionCommand());
        Finish(s);
        Assert.AreEqual(2,s.FestivalNumber);
        Assert.IsTrue(s.CompletedFestivalAccounts!.Reconciles);
        RoundTrip(s);
    }

    [TestMethod]
    public void ActualRebookingPersistsRefundsAndFansWithoutExtraSalesOrRepeatedCredits()
    {
        var s = GameSession.CreateClassicCampaign(14);
        ChoosePerk(s);
        var ids = CheapBands(s);
        Accept(s, new SetProgrammeCommand(ids));
        Accept(s, new ConfirmClassicBookingsCommand());
        var original = s.CaptureClassicBookingDetails()!;
        var fans = s.CapturePreparation()!.People.Where(p=>p.Role==ProtectedPersonRole.Guest).ToDictionary(p=>p.AgentId,p=>p.ExpectedGenre);
        var replacement = s.GetFestivalActs().Where(a=>s.ActStandingOf(a)!=ActStanding.Locked&&!ids.Contains(a.Id))
            .OrderBy(s.ActFee).First();
        var next = ids.ToArray(); next[0]=replacement.Id;
        Assert.IsFalse(BuildSession.Send(s,new SetProgrammeCommand(next)).IsAccepted);
        Accept(s,new RebookClassicProgrammeCommand(next));
        s=RoundTrip(s);
        var revised=s.CaptureClassicBookingDetails()!;
        Assert.AreEqual(original.OriginalTickets,revised.OriginalTickets);
        Assert.IsTrue(revised.RefundedTickets>0);
        Assert.AreEqual(original.Money.Single(m=>m.Kind==ClassicBookingMoneyKind.BandFee&&m.ActId==ids[0]).UnitPennies,
            revised.Money.Single(m=>m.Kind==ClassicBookingMoneyKind.BandRelease&&m.ActId==ids[0]).UnitPennies);
        Assert.IsTrue(s.CapturePreparation()!.People.Where(p=>p.Role==ProtectedPersonRole.Guest).All(p=>fans[p.AgentId]==p.ExpectedGenre));
        var cash=s.CapturePersistentCampaignState()!.CashPennies;
        Accept(s,new RebookClassicProgrammeCommand(next));
        Assert.AreEqual(cash,s.CapturePersistentCampaignState()!.CashPennies);
        Accept(s,new RebookClassicProgrammeCommand(ids));
        Assert.AreEqual(revised.RefundedTickets,s.CaptureClassicBookingDetails()!.RefundedTickets);
        Assert.AreEqual(original.NetBandCostsPennies,s.CaptureClassicBookingDetails()!.NetBandCostsPennies);
        Assert.AreEqual(original.OriginalTickets-revised.RefundedTickets,s.CurrentFestival.Attendance);
        Site(s);
        Accept(s,new StartPreparedEditionCommand());
        Finish(s);
        Assert.IsTrue(s.CompletedFestivalAccounts!.Reconciles);
        Assert.AreEqual(revised.RefundedTickets,s.CompletedFestivalAccounts.TicketLines!.Where(l=>l.Description=="Ticket refunds").Sum(l=>l.Quantity));
        RoundTrip(s);
    }

    [TestMethod]
    public void FileHostPreservesDraftConfirmationCancellationManualLoadAndFreshReset()
    {
        var directory=Path.Combine(Path.GetTempPath(),"festival-classic-booking-host-"+Guid.NewGuid());
        var compatibility=new SaveCompatibility("classic-booking-test","content","rules");
        var s=GameSession.CreateClassicCampaign(14);ChoosePerk(s);Accept(s,new SetProgrammeCommand(CheapBands(s)));
        var host=new SessionHost(s,directory,compatibility);
        Assert.IsTrue(host.SaveMilestone("booking draft"),host.SaveError);
        var loaded=AutosaveRotation.LoadNewestValid(directory,compatibility);
        Assert.IsTrue(loaded.IsSuccess,loaded.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(host.ExecuteMilestone(new ConfirmClassicBookingsCommand(),"bands and ticket sales",out var error),error);
        Assert.IsTrue(host.SaveManual().IsSuccess);
        var paidHash=host.Session.CaptureSnapshot().AuthoritativeHash;
        var paidCash=host.Session.CapturePersistentCampaignState()!.CashPennies;
        Assert.IsTrue(host.ExecuteMilestone(new CancelClassicFestivalCommand(),"cancel booking",out error),error);
        Assert.AreEqual(ClassicBookingPhase.Booking,host.Session.CaptureClassicBookingDetails()!.Phase);
        Assert.IsTrue(host.LoadManual(out error),error);
        Assert.AreEqual(paidHash,host.Session.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(paidCash,host.Session.CapturePersistentCampaignState()!.CashPennies);
        Assert.AreEqual(1,host.Session.CaptureClassicBookingDetails()!.Money.Count(m=>m.Kind==ClassicBookingMoneyKind.Tickets));
        host.StartNewCampaign(GameSession.CreateClassicCampaign(99));
        Assert.AreEqual(20_000L,host.Session.CapturePersistentCampaignState()!.CashPennies);
        Assert.AreEqual(0,host.Session.CaptureClassicBooking()!.Actions.Length);
        Assert.IsTrue(host.SaveMilestone("new Classic campaign"),host.SaveError);
        loaded=AutosaveRotation.LoadNewestValid(directory,compatibility);
        Assert.IsTrue(loaded.IsSuccess,loaded.Error);
        Assert.AreEqual(host.Session.CaptureSnapshot().AuthoritativeHash,loaded.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void FailedConfirmationSaveKeepsAcceptedSalesAndEarlierValidDraft()
    {
        var directory=Path.Combine(Path.GetTempPath(),"festival-classic-booking-failure-"+Guid.NewGuid());
        var compatibility=new SaveCompatibility("classic-booking-test","content","rules");
        var fail=false;
        var s=GameSession.CreateClassicCampaign(14);ChoosePerk(s);Accept(s,new SetProgrammeCommand(CheapBands(s)));
        var host=new SessionHost(s,directory,compatibility,_=>{if(fail)throw new IOException("Labelled booking checkpoint failure");});
        Assert.IsTrue(host.SaveMilestone("draft"),host.SaveError);
        var previous=AutosaveRotation.LoadNewestValid(directory,compatibility).Session!.CaptureSnapshot().AuthoritativeHash;
        fail=true;
        Assert.IsTrue(host.ExecuteMilestone(new ConfirmClassicBookingsCommand(),"confirmation",out var error),error);
        Assert.IsTrue(host.SaveWarningVisible);
        Assert.AreEqual(ClassicBookingPhase.SitePreparation,host.Session.CaptureClassicBookingDetails()!.Phase);
        Assert.AreEqual(previous,AutosaveRotation.LoadNewestValid(directory,compatibility).Session!.CaptureSnapshot().AuthoritativeHash);
        var accepted=host.Session.CaptureSnapshot().AuthoritativeHash;
        host.DismissSaveWarning();fail=false;
        Assert.IsTrue(host.SaveMilestone("retry"),host.SaveError);
        Assert.IsFalse(host.SaveWarningVisible);
        Assert.AreEqual(accepted,AutosaveRotation.LoadNewestValid(directory,compatibility).Session!.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(1,host.Session.CaptureClassicBookingDetails()!.Money.Count(m=>m.Kind==ClassicBookingMoneyKind.Tickets));
    }
}
