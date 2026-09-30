using Festival.Simulation;
using Festival.Persistence;

namespace Festival.Tests;

[TestClass]
public sealed class EquipmentIncidentTests
{
    private static readonly SaveCompatibility Compatibility = new("r0.02-tests", "equipment", "v1");
    private static CommandResult Execute(GameSession s, SessionCommand command) => s.Execute(new(new CommandId(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, command));
    private static GameSession Started(ulong seed = 2, bool worker = false, int tier = 1)
    {
        var s = BuildSession.Planned(seed);
        foreach (var id in new[] { "staff.steward", "equipment.buy" }.Concat(worker ? new[] { "maintenance.worker" } : []))
            Assert.IsTrue(Execute(s, new AcceptPreparationOfferCommand(id)).IsAccepted, id);
        Assert.IsTrue(Execute(s, new StartPreparedEditionCommand()).IsAccepted);
        return s;
    }
    private static GameSession Restore(GameSession s)
    {
        var r = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(r.IsSuccess, r.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, r.Session!.CaptureSnapshot().AuthoritativeHash);
        return r.Session;
    }

    [TestMethod]
    public void BaselineCutoffNeedsNoWorkerOrCashAndRemainsSafeAtLastResponseTick()
    {
        var s = Started(); s.AdvanceWithoutSnapshot(7_199);
        var cash = s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        Assert.IsTrue(Execute(s, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        s = Restore(s); s.AdvanceWithoutSnapshot(200);
        Assert.AreEqual(0, s.CaptureLifecycleSnapshot()!.Casualties.Count);
        Assert.AreEqual(cash, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.IsNull(s.CaptureEquipment()!.WorkerId);
        for (ulong seed = 0; seed < 100; seed++)
        {
            var prep = BuildSession.Planned(seed);
            var offers = prep.GetPreparationOffers();
            Assert.IsTrue(offers.Where(item => item.Id is "act.folk" or "staff.engineer" or "equipment.buy" or "contract.stock" or "maintenance.worker").Sum(item => item.PricePennies) < prep.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        }
    }

    [TestMethod]
    public void InvalidEquipmentOwnershipAndCausalStagesAreRejected()
    {
        var s = Started(worker: true); s.AdvanceWithoutSnapshot(2_400);
        var snapshot = s.CapturePersistenceSnapshot();
        foreach (var equipment in new[] { snapshot.Equipment! with { WorkerId = 999_999 }, snapshot.Equipment! with { WarningTick = 1 },
            snapshot.Equipment! with { Version = 1 }, snapshot.Equipment! with { XMillimetres = -5_750, ZMillimetres = 15_250 },
            snapshot.Equipment! with { LoadPercent = 0 }, snapshot.Equipment! with { Evidence = [] } })
            Assert.IsFalse(GameSession.Restore(snapshot with { Equipment = equipment }).IsSuccess);
    }

}
