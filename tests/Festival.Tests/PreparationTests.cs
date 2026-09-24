using Festival.Simulation;
using Festival.Persistence;

namespace Festival.Tests;

[TestClass]
public sealed class PreparationTests
{
    private static readonly SaveCompatibility Compatibility = new("r0.01-tests", "test-content", "test-rules");
    private static CommandResult Execute(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase, session.CurrentTick,
        session.NextSubmissionSequence, null, command));
    private static void Book(GameSession session, string act = "act.folk", string equipment = "equipment.buy")
    {
        foreach (var id in new[] { act, "staff.steward", equipment })
            Assert.IsTrue(Execute(session, new AcceptPreparationOfferCommand(id)).IsAccepted);
    }
    private static GameSession Restore(GameSession session)
    {
        var result = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash);
        return result.Session;
    }

    [TestMethod]
    public void StableOffersHaveBoundedAffordableChoicesWithoutReroll()
    {
        for (ulong seed = 0; seed < 100; seed++)
        {
            var session = GameSession.CreatePreparedCampaign(seed);
            var offers = session.GetPreparationOffers();
            Assert.AreEqual(7, offers.Count);
            CollectionAssert.AreEqual(offers.ToArray(), Restore(session).GetPreparationOffers().ToArray());
            Assert.IsTrue(offers.Where(item => item.Id is "act.folk" or "staff.steward" or "equipment.rent").Sum(item => item.PricePennies) <= 12_000);
            Assert.AreEqual(20, session.CapturePreparation()!.People.Count(item => item.Role == ProtectedPersonRole.Guest));
            var before = session.CaptureSnapshot().AuthoritativeHash;
            Assert.IsFalse(Execute(session, new StartPreparedEditionCommand()).IsAccepted);
            Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void EntireProtectedRosterPhysicallyArrivesAndDeparts(int tier)
    {
        var session = GameSession.CreatePreparedCampaign(2, tier);
        Book(session);
        Assert.IsTrue(Execute(session, new StartPreparedEditionCommand()).IsAccepted);
        Assert.AreEqual(tier * 20 + 4, session.CaptureSnapshot().NavigationAgents.Count);
        session.AdvanceWithoutSnapshot(3_000);
        var arrived = session.CapturePreparation()!;
        Assert.IsTrue(arrived.People.All(item => item.Admitted), string.Join(",", arrived.People.Where(item => !item.Admitted).Select(item => item.Name)));
        var restored = Restore(session);
        session.AdvanceWithoutSnapshot(38_000);
        restored.AdvanceWithoutSnapshot(38_000);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(PreparationStatus.Finished, session.CapturePreparation()!.Status);
        Assert.IsTrue(session.CapturePreparation()!.People.All(item => item.Departed));
        Assert.AreEqual(40 - tier * 20, session.CaptureSnapshot().OwnedStocks.Single().Quantity);
        Restore(session);
    }

    [TestMethod]
    public void FailurePreservesPropertyContactsCashAndUnusedStockButExpiresContracts()
    {
        foreach (var equipment in new[] { "equipment.buy", "equipment.rent" })
        {
            var session = GameSession.CreatePreparedCampaign(2, fixtureOutcomesEnabled: true);
            Book(session, equipment: equipment);
            Assert.IsTrue(Execute(session, new AcceptPreparationOfferCommand("contract.stock")).IsAccepted);
            var unchanged = session.CaptureSnapshot().AuthoritativeHash;
            Assert.IsFalse(Execute(session, new AcceptPreparationOfferCommand("contract.stock")).IsAccepted);
            Assert.AreEqual(unchanged, session.CaptureSnapshot().AuthoritativeHash);
            Execute(session, new StartPreparedEditionCommand());
            session.AdvanceWithoutSnapshot(3_000);
            var cash = session.CaptureSnapshot().FestivalFinances.Single().CashPennies;
            var stock = session.CaptureSnapshot().OwnedStocks.Single().Quantity;
            session.SettlePreparationFailureFixture();
            var frozen = session.CaptureSnapshot().AuthoritativeHash;
            session.AdvanceWithoutSnapshot(10_000);
            Assert.AreEqual(frozen, session.CaptureSnapshot().AuthoritativeHash);
            session = Restore(session);
            Assert.AreEqual(0, session.CapturePreparation()!.Rentals.Length);
            Assert.AreEqual(0, session.CapturePreparation()!.WorkContracts.Length);
            Assert.AreEqual(1, session.CapturePreparation()!.Contacts.Length);
            Assert.AreEqual(equipment == "equipment.buy" ? 1 : 0, session.CapturePreparation()!.OwnedEquipment.Length);
            var seed = session.CapturePreparation()!.OfferSeed;
            session.RetryPreparationFixture();
            Assert.AreEqual(seed, session.CapturePreparation()!.OfferSeed);
            Assert.AreEqual(cash, session.CaptureSnapshot().FestivalFinances.Single().CashPennies);
            Assert.AreEqual(stock, session.CaptureSnapshot().OwnedStocks.Single().Quantity);
            Assert.IsFalse(Execute(session, new StartPreparedEditionCommand()).IsAccepted);
            Restore(session);
        }
    }

    [TestMethod]
    public void WeakMusicFitChangesNamedGuestSatisfactionAndRiskWithoutBlockingStart()
    {
        var good = GameSession.CreatePreparedCampaign(2);
        var weak = GameSession.CreatePreparedCampaign(2);
        Book(good, "act.folk", "equipment.rent");
        Book(weak, "act.punk", "equipment.rent");
        Execute(good, new StartPreparedEditionCommand());
        Execute(weak, new StartPreparedEditionCommand());
        good.AdvanceWithoutSnapshot(3_000); weak.AdvanceWithoutSnapshot(3_000);
        var a = good.CapturePreparation()!.People.Single(item => item.Name == "Guest 02");
        var b = weak.CapturePreparation()!.People.Single(item => item.Name == "Guest 02");
        Assert.IsTrue(a.Admitted && b.Admitted);
        Assert.IsTrue(a.Satisfaction > b.Satisfaction);
        Assert.IsTrue(a.MusicRisk < b.MusicRisk);
        Assert.AreEqual(good.CaptureSnapshot().FestivalFinances.Single().CashPennies, weak.CaptureSnapshot().FestivalFinances.Single().CashPennies);
    }

    [TestMethod]
    public void MalformedPreparationRejectsMissingRosterAndUnpaidProperty()
    {
        var session = GameSession.CreatePreparedCampaign(2);
        var saved = session.CapturePersistenceSnapshot();
        var p = saved.Preparation!;
        foreach (var malformed in new[]
        {
            p with { People = p.People.Skip(1).ToArray() },
            p with { OwnedEquipment = ["sound-rig"] },
            p with { Contacts = ["staff.engineer"] },
            p with { AcceptedOffers = [null!] },
            p with { Payments = [new(1, "act.folk", 1, 0, -1, LedgerAccountType.AdministrationExpense)] },
            p with { People = p.People.Select((person, index) => index == 0 ? person with { AgentId = 9999 } : person).ToArray() }
        })
        {
            var result = GameSession.Restore(saved with { Preparation = malformed });
            Assert.IsFalse(result.IsSuccess);
            Assert.IsNotNull(result.Error);
        }
    }

    [TestMethod]
    public void PurchasedRigCostsMoreNowAndImprovesActualMusicQuality()
    {
        var owned = GameSession.CreatePreparedCampaign(2);
        var rented = GameSession.CreatePreparedCampaign(2);
        Book(owned); Book(rented, equipment: "equipment.rent");
        Execute(owned, new StartPreparedEditionCommand()); Execute(rented, new StartPreparedEditionCommand());
        owned.AdvanceWithoutSnapshot(3_000); rented.AdvanceWithoutSnapshot(3_000);
        Assert.AreEqual(9_000L, rented.CaptureSnapshot().FestivalFinances.Single().CashPennies - owned.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.IsTrue(owned.CapturePreparation()!.People[1].Satisfaction >= rented.CapturePreparation()!.People[1].Satisfaction);
        Assert.AreEqual(owned.CapturePreparation()!.People[1].AgentId, rented.CapturePreparation()!.People[1].AgentId);
    }

    [TestMethod]
    public void SuccessfulRentalExpiresAndLedgerReconcilesExactly()
    {
        var session = GameSession.CreatePreparedCampaign(2);
        Book(session, equipment: "equipment.rent");
        Execute(session, new StartPreparedEditionCommand());
        session.AdvanceWithoutSnapshot(42_000);
        var p = session.CapturePreparation()!;
        Assert.AreEqual(PreparationStatus.Finished, p.Status);
        Assert.AreEqual(0, p.Rentals.Length);
        Assert.AreEqual(0, p.WorkContracts.Length);
        Assert.AreEqual(1, p.Contacts.Length);
        Assert.AreEqual(0L, session.GetPreparationLedgerEntries().Sum(item => item.AmountPennies));
        Assert.AreEqual(80_000L - p.Payments.Sum(item => item.AmountPennies), session.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Restore(session);
    }

    [TestMethod]
    public void CommittedRetryCostsEventuallyRejectWithoutNegativeCashOrMutation()
    {
        var session = GameSession.CreatePreparedCampaign(2, fixtureOutcomesEnabled: true);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var before = session.CaptureSnapshot().AuthoritativeHash;
            var act = Execute(session, new AcceptPreparationOfferCommand("act.folk"));
            if (!act.IsAccepted)
            {
                Assert.AreEqual(CommandReasonCode.InsufficientFunds, act.ReasonCode);
                Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
                Assert.IsTrue(session.CaptureSnapshot().FestivalFinances.Single().CashPennies >= 0);
                Restore(session);
                return;
            }
            var staff = Execute(session, new AcceptPreparationOfferCommand("staff.steward"));
            if (!staff.IsAccepted)
            {
                Assert.AreEqual(CommandReasonCode.InsufficientFunds, staff.ReasonCode);
                Restore(session); return;
            }
            Execute(session, new StartPreparedEditionCommand());
            session.SettlePreparationFailureFixture();
            session.RetryPreparationFixture();
        }
        Assert.Fail("Bounded costs should exhaust cash; no free retry contracts.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EditionBoundariesSaveCandidatesAndFailedWritesPreserveVisibleState(bool completion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "festival-preparation-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var session = GameSession.CreatePreparedCampaign(2);
            Book(session, equipment: "equipment.rent");
            Execute(session, new StartPreparedEditionCommand());
            session.AdvanceWithoutSnapshot(GameSession.PreparedWeekendTicks - 1);
            Assert.IsTrue(session.PreparationBoundaryOnNextTick);
            if (completion)
            {
                session.AdvanceWithoutSnapshot(1);
                for (var index = 0; index < 3_000 && !session.PreparationBoundaryOnNextTick; index++) session.AdvanceWithoutSnapshot(1);
                Assert.AreEqual(PreparationStatus.Departing, session.PreparedStatus);
                Assert.IsTrue(session.PreparationBoundaryOnNextTick);
            }
            Assert.AreEqual(1, session.CapturePreparation()!.Rentals.Length);
            Assert.AreEqual(1, session.CapturePreparation()!.WorkContracts.Length);
            Assert.IsTrue(AutosaveRotation.Save(directory, session, Compatibility, DateTimeOffset.UnixEpoch, 0).IsSuccess);
            var before = session.CaptureSnapshot().AuthoritativeHash;
            var failure = PreparationAdvanceCoordinator.AdvanceOne(directory, session, Compatibility, DateTimeOffset.UnixEpoch.AddMinutes(1), 1,
                _ => throw new IOException("Injected boundary save failure"));
            Assert.IsFalse(failure.IsSuccess);
            Assert.AreSame(session, failure.Session);
            Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(before, AutosaveRotation.LoadNewestValid(directory, Compatibility).Session!.CaptureSnapshot().AuthoritativeHash);
            Assert.IsTrue(failure.Error!.Contains("retry", StringComparison.OrdinalIgnoreCase));
            var success = PreparationAdvanceCoordinator.AdvanceOne(directory, session, Compatibility, DateTimeOffset.UnixEpoch.AddMinutes(2), 1);
            Assert.IsTrue(success.IsSuccess, success.Error);
            Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash, "Source must remain untouched even after candidate commit.");
            var committed = success.Session;
            Assert.AreEqual(completion ? PreparationStatus.Finished : PreparationStatus.Departing, committed.PreparedStatus);
            Assert.AreEqual(completion ? 0 : 1, committed.CapturePreparation()!.Rentals.Length);
            Assert.AreEqual(completion ? 0 : 1, committed.CapturePreparation()!.WorkContracts.Length);
            Assert.AreEqual(committed.CaptureSnapshot().AuthoritativeHash,
                AutosaveRotation.LoadNewestValid(directory, Compatibility).Session!.CaptureSnapshot().AuthoritativeHash);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void OpeningInventoryPlusDeliveriesLessActualConsumptionEqualsRemainingValue()
    {
        var session = GameSession.CreatePreparedCampaign(2);
        var opening = session.GetPreparationInventoryBalance()!;
        Assert.AreEqual(40, opening.OpeningUnits);
        Assert.AreEqual(2_400, opening.OpeningUnits * opening.UnitCostPennies);
        Book(session);
        Execute(session, new AcceptPreparationOfferCommand("contract.stock"));
        Execute(session, new StartPreparedEditionCommand());
        session.AdvanceWithoutSnapshot(3_000);
        var balance = Restore(session).GetPreparationInventoryBalance()!;
        Assert.AreEqual(50, balance.PurchasedUnits);
        Assert.AreEqual(20, balance.ConsumedUnits);
        Assert.AreEqual(balance.RemainingUnits, balance.OpeningUnits + balance.PurchasedUnits - balance.ConsumedUnits);
        Assert.AreEqual((long)balance.RemainingUnits * balance.UnitCostPennies,
            (long)balance.OpeningUnits * balance.UnitCostPennies + session.GetPreparationLedgerEntries()
                .Where(entry => entry.Account == LedgerAccountType.InventoryAsset).Sum(entry => entry.AmountPennies));
    }
}
