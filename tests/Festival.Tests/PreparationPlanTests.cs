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
        var s = GameSession.CreateEditableCampaign(20260922); var p = s.CapturePerks()!;
        Accept(s, new ChoosePerkCommand(p.DraftAttempt, p.Cursor, p.Hand[0])); return s;
    }
    private static GameSession Restored(GameSession s)
    {
        var result = GameSession.Restore(s.CapturePersistenceSnapshot()); Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash); return result.Session;
    }
    private static void Ready(GameSession s)
    {
        Accept(s, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"]));
        Accept(s, new AcceptPreparationOfferCommand("staff.steward"));
        Accept(s, new AcceptPreparationOfferCommand("equipment.buy"));
        Accept(s, new PurchaseImmersionStarterStockCommand());
    }
    [TestMethod]
    public void SoundEngineerLabelsDescribeExistingQualityAndPrices()
    {
        var offers=New().GetPreparationOffers();
        var standard=offers.Single(o=>o.Id=="staff.steward");var better=offers.Single(o=>o.Id=="staff.engineer");
        Assert.AreEqual("Casey: standard sound engineer • +400 quality",standard.Name);
        Assert.AreEqual("Casey: better sound engineer • +800 quality",better.Name);
        Assert.AreEqual(2000L,standard.PricePennies);Assert.AreEqual(400,standard.MusicQuality);
        Assert.AreEqual(4000L,better.PricePennies);Assert.AreEqual(800,better.MusicQuality);
    }
    [TestMethod]
    public void EditsAreUnpaidReplaceableRemovableAndPersistIncompleteLineup()
    {
        var s = New(); var next = s.NextEntityId; var cash = s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        Accept(s, new SetProgrammeCommand(["act.meadow-lanterns", "", ""])); s = Restored(s);
        Assert.AreEqual("act.meadow-lanterns", s.CapturePreparationPlan()!.ActIds[0]);
        Assert.IsFalse(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        Ready(s); Accept(s, new AcceptPreparationOfferCommand("staff.engineer"));
        Accept(s, new SetProgrammeCommand(["act.orchard-chorus", "act.barnstorm-circuit", "act.field-frequency"]));
        Assert.AreEqual(21500L + 4000 + 12000 + 9600, s.PreparationPlanCost);
        Accept(s, new SetProgrammeCommand(["", "act.barnstorm-circuit", "act.field-frequency"]));
        Assert.IsFalse(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        Accept(s, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"]));
        Accept(s, new AcceptPreparationOfferCommand("equipment.rent"));
        Assert.IsFalse(s.CapturePreparationPlan()!.OfferIds.Contains("equipment.buy"));
        Assert.AreEqual(0, s.CapturePreparation()!.Rentals.Length);
        Accept(s, new AcceptPreparationOfferCommand("equipment.buy"));
        Assert.IsFalse(s.CapturePreparationPlan()!.OfferIds.Contains("staff.steward"));
        Accept(s, new AcceptPreparationOfferCommand("maintenance.worker"));
        Accept(s, new RemovePreparationOfferCommand("maintenance.worker"));
        Accept(s, new RemovePreparationOfferCommand("equipment.buy"));
        Accept(s, new SetPreparationStockCommand(3, 7, 2));
        Assert.AreEqual(22920L, s.PreparationPlanCost);
        Assert.AreEqual(cash, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.AreEqual(next, s.NextEntityId); Assert.AreEqual(0, s.CapturePreparation()!.Payments.Length);
        Assert.AreEqual(0, s.CaptureProgramme()!.ActIds.Length); Assert.IsFalse(s.CaptureImmersion()!.StockPurchased);
        Assert.AreEqual(0, s.CaptureFestivalCashFeedbackEvents().Count); Restored(s);
        var perk = s.CapturePerks()!;
        var hash = s.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(Send(s, new RerollPerksCommand(perk.DraftAttempt, perk.Cursor)).IsAccepted);
        Assert.IsFalse(Send(s, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Equipped.Single())).IsAccepted);
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void MissingRoleSlotRejectsHireWithoutChangingDraftOrIds()
    {
        var s = New(); var p = s.CapturePreparation()!;
        var missing = p.ExtraMedicSlotOwned ? "staff.extra-steward" : "staff.extra-medic";
        var hash = s.CaptureSnapshot().AuthoritativeHash; var next = s.NextEntityId;
        Assert.IsFalse(Send(s, new AcceptPreparationOfferCommand(missing)).IsAccepted);
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash); Assert.AreEqual(next, s.NextEntityId); Restored(s);
    }
    [TestMethod]
    public void OverBudgetDraftSavesButCannotOpenAndMalformedPlanRejects()
    {
        var s = New(); Ready(s); Accept(s, new SetPreparationStockCommand(10000, 10000, 10000));
        var before = s.CaptureSnapshot().AuthoritativeHash; Assert.IsTrue(s.PreparationRemainingCash < 0);
        Assert.IsFalse(Send(s, new StartPreparedEditionCommand()).IsAccepted); Assert.AreEqual(before, s.CaptureSnapshot().AuthoritativeHash);
        Restored(s);
        var directory = Path.Combine(Path.GetTempPath(), "festival-plan-" + Guid.NewGuid());
        Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "draft", new(s, Compatibility, "manual", DateTimeOffset.UtcNow)).IsSuccess);
        Assert.IsTrue(SaveFileAdapter.LoadSlot(directory, "draft", Compatibility).IsSuccess);
        var snapshot = s.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = snapshot.Preparation! with { Plan = null } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = snapshot.Preparation! with { Plan = snapshot.Preparation.Plan! with { Chips = -1 } } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = snapshot.Preparation! with { Plan = snapshot.Preparation.Plan! with { OfferIds = ["staff.steward", "staff.engineer"] } } }).IsSuccess);
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
        Assert.AreEqual(80000L - total, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.AreEqual(1, s.CapturePreparation()!.SetupPayments!.Length);
        Assert.AreEqual(-total, s.CaptureFestivalCashFeedbackEvents().Single().FestivalCashPennies);
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
    public void OptionalOnlyStaffRetryRetainsImmutableSetupAndRehiresStablePerson()
    {
        GameSession? candidate = null;
        for (ulong seed = 0; seed < 100; seed++)
        {
            var possible = GameSession.CreateEditableCampaign(seed); var hand = possible.CapturePerks()!;
            if (!hand.Hand.Contains("doctors-orders")) continue;
            Accept(possible, new ChoosePerkCommand(hand.DraftAttempt, hand.Cursor, "doctors-orders")); candidate = possible; break;
        }
        var s = candidate!; Assert.IsNotNull(s);
        Accept(s, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"]));
        Accept(s, new AcceptPreparationOfferCommand("staff.extra-medic"));
        Accept(s, new AcceptPreparationOfferCommand("equipment.buy")); Accept(s, new PurchaseImmersionStarterStockCommand());
        var directory = Path.Combine(Path.GetTempPath(), "festival-plan-retry-" + Guid.NewGuid());
        var opened = EquipmentCommandCoordinator.Execute(directory, s, new StartPreparedEditionCommand(), Compatibility, DateTimeOffset.UtcNow, 1);
        Assert.IsTrue(opened.IsSuccess, opened.Error); s = Restored(opened.Session);
        var profile = s.CapturePreparation()!.StaffProfiles.Single(); var next = s.NextEntityId;
        var setup = s.CapturePreparation()!.SetupPayments!.Single(); var cashEvent = s.CaptureFestivalCashFeedbackEvents().Single();
        // Labelled fixture: admitted roster + severe alcohol exposure; actual warning/deadline chain, no response.
        var prep = s.CapturePreparation()!;
        typeof(GameSession).GetField("_preparation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(s, prep with { People = prep.People.Select(p => p with { Admitted = true }).ToArray() });
        var immersion = s.CaptureImmersion()!; var guest = prep.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        typeof(GameSession).GetField("_immersion", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(s, immersion with { People = immersion.People.Select(p => p.AgentId == guest ? p with { Intoxication = 10000 } : p).ToArray() });
        s.AdvanceWithoutSnapshot(4000); Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus); s = Restored(s);
        Accept(s, new SpendCouncilFavourCommand()); s = Restored(s);
        Assert.AreEqual(cashEvent, s.CaptureFestivalCashFeedbackEvents().Single(e => e.TransactionId == cashEvent.TransactionId));
        Assert.AreEqual(setup.TotalPennies, s.CapturePreparation()!.SetupPayments!.Single().TotalPennies);
        CollectionAssert.AreEqual(setup.Entries, s.CapturePreparation()!.SetupPayments!.Single().Entries);
        Assert.AreEqual(80000L, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.AreEqual(0, s.CaptureImmersion()!.ChipsStock); Assert.AreEqual(0, s.CapturePreparation()!.WorkContracts.Length);
        Assert.IsFalse(s.CapturePreparation()!.People.Any(p => p.AgentId == profile.AgentId));
        var draft = s.CapturePerks()!; Accept(s, new ChoosePerkCommand(draft.DraftAttempt, draft.Cursor, draft.Hand[0]));
        Assert.IsFalse(Send(s, new AcceptPreparationOfferCommand("equipment.buy")).IsAccepted);
        Accept(s, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"]));
        Accept(s, new AcceptPreparationOfferCommand("staff.extra-medic"));
        Accept(s, new RemovePreparationOfferCommand("staff.extra-medic")); Accept(s, new AcceptPreparationOfferCommand("staff.extra-medic"));
        Assert.AreEqual(next, s.NextEntityId);
        Accept(s, new StartPreparedEditionCommand()); s = Restored(s);
        Assert.AreEqual(next, s.NextEntityId); Assert.AreEqual(profile, s.CapturePreparation()!.StaffProfiles.Single());
        Assert.AreEqual(1, s.CapturePreparation()!.People.Count(p => p.AgentId == profile.AgentId));
        Assert.AreEqual(2, s.CapturePreparation()!.SetupPayments!.Length);
        Assert.AreEqual(80000L - 18000 - 3000, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
    }
    [TestMethod]
    public void MalformedSetupAndCollectionsRejectWithoutThrowing()
    {
        var s = New(); Ready(s); Accept(s, new StartPreparedEditionCommand()); var snapshot = s.CapturePersistenceSnapshot(); var p = snapshot.Preparation!; var payment = p.SetupPayments!.Single();
        foreach (var malformed in new[] { payment with { Id = "wrong" }, payment with { Chips = -1 }, payment with { TotalPennies = payment.TotalPennies + 1 }, payment with { Entries = [] } })
            Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = p with { SetupPayments = [malformed] } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = p with { Payments = null! } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = p with { Payments = [null!] } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = p with { SetupPayments = null } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with { Preparation = p with { AcceptedOffers = null! } }).IsSuccess);
    }
    [TestMethod]
    public void PriorChargedSaveIsRejectedByNormalEconomicsIdentityAndFilePreserved()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ROGUELIKE_DESIGN.md"))) directory = directory.Parent;
        var path = Path.Combine(directory!.FullName, "reports", "evidence", "R0.05h", "final-1280x720", "saves", "manual-preparation.ftsave");
        var bytes = File.ReadAllBytes(path);
        var normal = new SaveCompatibility("0.0.1-r0.05k-unpaid-plan-v1", LowerWitteringFarmScenario.ContentCompatibilityHash, "r0-editable-preparation-v1");
        var result = SaveFileAdapter.LoadSlot(Path.GetDirectoryName(path)!, "manual-preparation", normal);
        Assert.IsFalse(result.IsSuccess); Assert.IsNotNull(result.Error);
        var historical = new SaveCompatibility("0.0.1-r0.05-hearing-v1", LowerWitteringFarmScenario.ContentCompatibilityHash, "r0-disorder-layout-v13");
        var legacy = SaveFileAdapter.LoadFile(path, historical); Assert.IsTrue(legacy.IsSuccess, legacy.Error);
        Assert.IsNull(legacy.Session!.CapturePreparationPlan()); Assert.AreEqual(1, legacy.Session.CapturePreparation()!.Version);
        Assert.IsTrue(legacy.Session.CapturePreparation()!.Payments.Length > 0);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }
}
