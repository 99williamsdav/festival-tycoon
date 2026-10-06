using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class PreparationPlanTests
{
    private static readonly SaveCompatibility Compatibility = new("r005k", "test-content", "unpaid-plan-v1");
    private static CommandResult Send(GameSession s, SessionCommand c) => s.Execute(new(new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, c));
    private static void Accept(GameSession s, SessionCommand c) { var result = Send(s, c); Assert.IsTrue(result.IsAccepted, result.Message); }
    private static GameSession New()
    {
        return BuildSession.Drafted(20260922);
    }
    private static GameSession Restored(GameSession s)
    {
        var result = GameSession.Restore(s.CapturePersistenceSnapshot()); Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash); return result.Session;
    }
    private static void Ready(GameSession s)
    {
        Accept(s, new SetProgrammeCommand(["act.meadow-lanterns", "act.overdue-library-books", "act.low-battery"]));
        foreach (var hire in BuildSession.Crew(s)) Accept(s, hire);
        Accept(s, new AcceptPreparationOfferCommand("equipment.rent"));
        Accept(s, new SetPreparationStockCommand(40, 32));
    }
    [TestMethod]
    public void MissingRoleSlotRejectsHireWithoutChangingDraftOrIds()
    {
        var s = New(); var p = s.CapturePreparation()!;
        var missing = p.ExtraMedicSlotOwned ? "staff.extra-steward.2" : "staff.extra-medic.2";
        var hash = s.CaptureSnapshot().AuthoritativeHash; var next = s.NextEntityId;
        Assert.IsFalse(Send(s, new AcceptPreparationOfferCommand(missing)).IsAccepted);
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash); Assert.AreEqual(next, s.NextEntityId); Restored(s);
    }
    [TestMethod]
    public void OverBudgetDraftSavesButCannotOpenAndMalformedPlanRejects()
    {
        var s = New(); Ready(s); Accept(s, new SetPreparationStockCommand(10000, 10000));
        var before = s.CaptureSnapshot().AuthoritativeHash; Assert.IsTrue(s.PreparationRemainingCash < 0);
        var budget = s.GetPreparationStartRequirements().Single(item => item.Id == "budget");
        Assert.IsFalse(budget.Complete);
        Assert.AreEqual(budget.Detail, s.GetPreparationStartBlockers().Single().Message);
        Assert.IsFalse(Send(s, new StartPreparedEditionCommand()).IsAccepted); Assert.AreEqual(before, s.CaptureSnapshot().AuthoritativeHash);
        Restored(s);
        var directory = Path.Combine(Path.GetTempPath(), "festival-plan-" + Guid.NewGuid());
        Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "draft", new(s, Compatibility, "manual", DateTimeOffset.UtcNow)).IsSuccess);
        Assert.IsTrue(SaveFileAdapter.LoadSlot(directory, "draft", Compatibility).IsSuccess);
        var snapshot = s.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = snapshot.Preparation! with { Plan = null } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = snapshot.Preparation! with { Plan = snapshot.Preparation.Plan! with { SoftDrinks = -1 } } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = snapshot.Preparation! with { Plan = snapshot.Preparation.Plan! with { OfferIds = ["staff.sound.1", "staff.sound.3"] } } }).IsSuccess);
    }
    [TestMethod]
    public void ActualStartCoordinatorIsFailureSafeAndExactlyOnceAcrossLoad()
    {
        var s = New(); Ready(s); Accept(s, new AcceptPreparationOfferCommand("maintenance.worker"));
        var before = s.CaptureSnapshot().AuthoritativeHash; var total = s.PreparationPlanCost;
        var directory = Path.Combine(Path.GetTempPath(), "festival-plan-start-" + Guid.NewGuid());
        var failed = EquipmentCommandCoordinator.Execute(directory, s, new StartPreparedEditionCommand(), Compatibility, DateTimeOffset.UtcNow, 1,
            _ => throw new IOException("injected save failure"));
        Assert.IsFalse(failed.IsSuccess); Assert.AreSame(s, failed.Session); Assert.AreEqual(before, s.CaptureSnapshot().AuthoritativeHash);
        var started = EquipmentCommandCoordinator.Execute(directory, s, new StartPreparedEditionCommand(), Compatibility, DateTimeOffset.UtcNow, 1);
        Assert.IsTrue(started.IsSuccess, started.Error); s = started.Session;
        Assert.AreEqual(CampaignDefaults.OpeningCashPennies - total + s.CapturePreparation()!.SetupPayments!.Last().PitchFeePennies, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.AreEqual(1, s.CapturePreparation()!.SetupPayments!.Length);
        Assert.AreEqual(-total + s.CapturePreparation()!.SetupPayments!.Last().PitchFeePennies, s.CaptureFestivalCashFeedbackEvents().Single().FestivalCashPennies);
        Assert.AreEqual(0L, s.CapturePreparation()!.SetupPayments!.Single().Entries.Sum(e => e.AmountPennies));
        Assert.IsTrue(s.CaptureImmersion()!.StockPurchased); Assert.IsNotNull(s.CaptureEquipment()!.WorkerId);
        s = Restored(s); var loaded = AutosaveRotation.LoadNewestValid(directory, Compatibility); Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        var hash = s.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(EquipmentCommandCoordinator.Execute(directory, s, new StartPreparedEditionCommand(), Compatibility, DateTimeOffset.UtcNow, 2).IsSuccess);
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
        s.AdvanceWithoutSnapshot(1800); var resumed = Restored(s); s.AdvanceWithoutSnapshot(300); resumed.AdvanceWithoutSnapshot(300);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, resumed.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void MalformedSetupAndCollectionsRejectWithoutThrowing()
    {
        var s = New(); Ready(s); Accept(s, new StartPreparedEditionCommand()); var snapshot = s.CapturePersistenceSnapshot(); var p = snapshot.Preparation!; var payment = p.SetupPayments!.Single();
        foreach (var malformed in new[] { payment with { Id = "wrong" }, payment with { SoftDrinks = -1 }, payment with { TotalPennies = payment.TotalPennies + 1 }, payment with { Entries = [] } })
            Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = p with { SetupPayments = [malformed] } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = p with { Payments = null! } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = p with { Payments = [null!] } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = p with { SetupPayments = null } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = p with { AcceptedOffers = null! } }).IsSuccess);
    }
}
