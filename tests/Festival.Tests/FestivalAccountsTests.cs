using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class FestivalAccountsTests
{
    private static void Accept(GameSession session, SessionCommand command)
    {
        var result = session.Execute(new(new(session.NextSubmissionSequence + 1), session.CampaignId,
            session.Phase, session.CurrentTick, session.NextSubmissionSequence, null, command));
        Assert.IsTrue(result.IsAccepted, result.Message);
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void NaturalBuildCompletionAndSavedTerminalAccountsReconcile()
    {
        var session = GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established);
        var perk = session.CapturePerks()!;
        Accept(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
        Accept(session, new UseDefaultBuildLayoutCommand());
        Accept(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
        foreach (var hire in BuildSession.Crew(session)) Accept(session, hire);
        Accept(session, new AcceptPreparationOfferCommand("equipment.rent"));
        Accept(session, new SetPreparationStockCommand(8, 8, 8));
        Accept(session, new StartPreparedEditionCommand());
        session.AdvanceWithoutSnapshot((int)session.PreparedEditionDurationTicks + 15000);
        Assert.AreEqual(PreparationStatus.Finished, session.PreparedStatus);
        var accounts = session.CompletedFestivalAccounts!;
        Assert.IsTrue(accounts.Reconciles);
        Assert.IsTrue(accounts.FacilityDetailRecorded);
        Assert.IsTrue(accounts.OperatingExpenses.Any(line => line.Category == "Facilities"));
        var directory = Path.Combine(Path.GetTempPath(), "festival-build-accounts-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var compatibility = new SaveCompatibility("accounts", "content", "accounts");
            var saved = SaveFileAdapter.SaveSlot(directory, "finished", new(session, compatibility, "test", DateTimeOffset.UtcNow));
            Assert.IsTrue(saved.IsSuccess, saved.Error);
            var loaded = SaveFileAdapter.LoadSlot(directory, "finished", compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            var reloaded = loaded.Session!.CompletedFestivalAccounts!;
            Assert.IsTrue(reloaded.Reconciles);
            Assert.AreEqual(accounts.ClosingCashPennies, reloaded.ClosingCashPennies);
            Assert.IsTrue(accounts.OperatingExpenses.SequenceEqual(reloaded.OperatingExpenses));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void RecordedRatesBuildFeesStockAndCapitalReconcileWithoutDoubleCounting()
    {
        var session = GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established);
        var perk = session.CapturePerks()!;
        Accept(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
        Accept(session, new UseDefaultBuildLayoutCommand());
        Accept(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
        foreach (var hire in BuildSession.Crew(session)) Accept(session, hire);
        Accept(session, new AcceptPreparationOfferCommand("equipment.rent"));
        Accept(session, new SetPreparationStockCommand(8, 8, 8));
        Accept(session, new StartPreparedEditionCommand());
        var people = session.CapturePreparation()!.People;
        var guests = people.Where(person => person.Role == ProtectedPersonRole.Guest).Take(3).ToArray();
        var staff = people.First(person => person.Role == ProtectedPersonRole.Staff);
        var performer = people.First(person => person.Role == ProtectedPersonRole.Performer);
        var sell = typeof(GameSession).GetMethod("CompleteImmersionSale", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var (id, product) in new (ulong, ImmersionProduct)[] {
            (guests[0].AgentId, ImmersionProduct.Chips), (staff.AgentId, ImmersionProduct.Chips),
            (guests[1].AgentId, ImmersionProduct.SoftDrink), (staff.AgentId, ImmersionProduct.SoftDrink),
            (guests[2].AgentId, ImmersionProduct.Beer), (performer.AgentId, ImmersionProduct.Beer) })
            sell.Invoke(session, [id, product]);
        var preparation = session.CapturePreparation()!;
        var make = typeof(GameSession).GetMethod("MakeFestivalResult", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (FestivalResult)make.Invoke(null, [preparation, session.CaptureImmersion(),
            session.CaptureMedical(), session.CaptureDisorder(), session.CurrentTick])!;
        typeof(GameSession).GetProperty("PreparationView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(session, preparation with { Result = result });
        var report = session.CompletedFestivalAccounts!;
        var setup = preparation.SetupPayments!.Single(payment => payment.Attempt == preparation.Attempt);
        var ordinaryPayments = preparation.Payments.Where(payment => payment.Attempt == preparation.Attempt).ToArray();
        Assert.IsTrue(setup.BuildCostPennies > 0);
        Assert.AreEqual(6, report.Sales.Length);
        CollectionAssert.AreEquivalent(new[] { 300, 150, 200, 100, 300, 150 }, report.Sales.Select(line => line.UnitPricePennies).ToArray());
        Assert.AreEqual(20, report.TicketsSold); Assert.AreEqual(1_000, report.TicketPricePennies);
        Assert.AreEqual(20_000L + 1_200L, report.IncomePennies, "Advance ticket sales plus food and drink.");
        Assert.AreEqual(60_000L, report.OpeningCashPennies, "Opening cash is the loan; the ticket money is income.");
        Assert.AreEqual(520L, report.SoldItemCostPennies);
        Assert.AreEqual(ordinaryPayments.Where(payment => payment.DebitAccount == LedgerAccountType.AdministrationExpense)
            .Sum(payment => (long)payment.AmountPennies) + setup.BuildCostPennies, report.OperatingExpensesPennies);
        Assert.AreEqual(setup.BuildCostPennies, report.OperatingExpenses.Where(line => line.Category == "Facilities").Sum(line => line.AmountPennies));
        Assert.IsTrue(report.FacilityDetailRecorded && report.StockDetailRecorded);
        Assert.AreEqual(2_080L, report.StockPurchasesPennies);
        Assert.AreEqual(0L, report.CapitalPurchasesPennies, "Rigs are hired, not bought, so nothing is capital.");
        Assert.AreEqual(report.OperatingExpensesPennies, result.ContractCostsPennies);
        Assert.AreEqual(report.ClosingCashPennies - report.OpeningCashPennies, result.NetCashChangePennies);
        Assert.AreEqual(report.IncomePennies - report.SoldItemCostPennies - report.OperatingExpensesPennies, result.ProfitPennies);
        Assert.IsTrue(report.Reconciles, "Recorded setup, sales, stock and capital must reconcile to actual festival cash.");
    }
}
