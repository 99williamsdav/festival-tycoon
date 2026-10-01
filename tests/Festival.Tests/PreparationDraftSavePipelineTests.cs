using Festival.Persistence;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class PreparationDraftSavePipelineTests
{
    private static readonly SaveCompatibility Compatibility = new("draft-pipeline", "content", "rules");

    private static GameSession Open()
    {
        var session = GameSession.CreateBuildCampaign(20260929, FestivalStanding.Established);
        var perk = session.CapturePerks()!;
        var choice = session.Execute(new(new(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
            session.CurrentTick, session.NextSubmissionSequence, null,
            new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])));
        Assert.IsTrue(choice.IsAccepted, choice.Message);
        return session;
    }

    [TestMethod]
    public void RapidAlternatingHiresPublishInOrderWithoutPaymentOrLostEdit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "festival-draft-rapid-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var source = Open();
            var cash = source.CaptureSnapshot().FestivalFinances.Single().CashPennies;
            var pipeline = new PreparationDraftSavePipeline(directory, source, Compatibility, 0);
            for (var index = 0; index < 12; index++)
            {
                var selected = index % 2 == 0;
                var command = selected ? (SessionCommand)new AcceptPreparationOfferCommand("staff.sound.1") :
                    new RemovePreparationOfferCommand("staff.sound.1");
                var result = pipeline.Submit(command, DateTimeOffset.UtcNow);
                Assert.IsTrue(result.IsAccepted, result.Error);
                Assert.AreEqual(selected, pipeline.VisibleSession.CapturePreparationPlan()!.OfferIds.Contains("staff.sound.1"));
                Assert.AreEqual(0, pipeline.VisibleSession.CapturePreparation()!.Payments.Length);
                Assert.AreEqual(cash, pipeline.VisibleSession.CaptureSnapshot().FestivalFinances.Single().CashPennies);
            }
            Assert.AreEqual(12L, pipeline.NextGeneration);
            var finished = pipeline.FinishPending();
            Assert.IsFalse(finished.RolledBack, finished.Error);
            Assert.IsFalse(pipeline.HasPending);
            var loaded = SaveFileAdapter.LoadSlot(directory, AutosaveRotation.SlotForGeneration(11), Compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            Assert.AreEqual(pipeline.VisibleSession.CaptureSnapshot().AuthoritativeHash,
                loaded.Session!.CaptureSnapshot().AuthoritativeHash);
            Assert.IsFalse(loaded.Session.CapturePreparationPlan()!.OfferIds.Contains("staff.sound.1"));
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void FailedSaveRollsBackDependentClicksAndPreservesLastDurableSlot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "festival-draft-failure-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var pipeline = new PreparationDraftSavePipeline(directory, Open(), Compatibility, 0);
            Assert.IsTrue(pipeline.Submit(new AcceptPreparationOfferCommand("staff.sound.1"), DateTimeOffset.UtcNow).IsAccepted);
            Assert.IsFalse(pipeline.FinishPending().RolledBack);
            var durableHash = pipeline.VisibleSession.CaptureSnapshot().AuthoritativeHash;
            Assert.IsTrue(pipeline.Submit(new RemovePreparationOfferCommand("staff.sound.1"), DateTimeOffset.UtcNow,
                _ => throw new IOException("labelled disk failure")).IsAccepted);
            Assert.IsTrue(pipeline.Submit(new AcceptPreparationOfferCommand("staff.sound.1"), DateTimeOffset.UtcNow).IsAccepted);
            var failed = pipeline.FinishPending();
            Assert.IsTrue(failed.RolledBack);
            Assert.AreEqual(1L, pipeline.NextGeneration);
            Assert.AreEqual(durableHash, pipeline.VisibleSession.CaptureSnapshot().AuthoritativeHash);
            var prior = SaveFileAdapter.LoadSlot(directory, AutosaveRotation.SlotForGeneration(0), Compatibility);
            Assert.IsTrue(prior.IsSuccess, prior.Error);
            Assert.AreEqual(durableHash, prior.Session!.CaptureSnapshot().AuthoritativeHash);
            Assert.IsFalse(File.Exists(SaveFileAdapter.ResolveSlotPath(directory, AutosaveRotation.SlotForGeneration(2))));
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
            Assert.IsTrue(pipeline.Submit(new RemovePreparationOfferCommand("staff.sound.1"), DateTimeOffset.UtcNow).IsAccepted);
            Assert.IsFalse(pipeline.FinishPending().RolledBack);
            Assert.AreEqual(2L, pipeline.NextGeneration);
        }
        finally { Directory.Delete(directory, true); }
    }
}
