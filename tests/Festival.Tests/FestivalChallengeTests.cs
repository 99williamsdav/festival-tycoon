using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;
using System.Text.Json;

namespace Festival.Tests;

[TestClass]
public sealed class FestivalChallengeTests
{
    [TestMethod]
    public void FixedAssignmentDoesNotInventWeatherOrConsumeRandomStreams()
    {
        Assert.AreEqual("heatwave-b.png", FestivalChallenges.ForTier(1)!.Artwork);
        Assert.AreEqual("heavy-rain-a.png", FestivalChallenges.ForTier(2)!.Artwork);
        Assert.IsTrue(FestivalChallenges.ForTier(2)!.EffectsImplemented);
        Assert.IsNull(FestivalChallenges.ForTier(3));
        foreach (var tier in new[] { 1, 2 })
        {
            var session = GameSession.CreateDevelopmentFestival(20260922, tier);
            var before = session.CaptureSnapshot().AuthoritativeHash;
            for (var i = 0; i < 100; i++) Assert.IsNotNull(FestivalChallenges.ForTier(tier));
            Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(tier == 1, session.CaptureMedical()!.IsHot, "Reveal metadata must preserve the existing tier weather.");
        }
    }

    [TestMethod]
    public void AcknowledgementPreservesDraftRngCashAndTickAndRoundTrips()
    {
        foreach (var tier in new[] { 1, 2 })
        {
            var session = GameSession.CreateDevelopmentFestival(20260922, tier);
            var before = session.CapturePersistenceSnapshot();
            var perks = JsonSerializer.Serialize(session.CapturePerks());
            BuildSession.Accept(session, new AcknowledgeFestivalChallengeCommand(tier, 1));
            var after = session.CapturePersistenceSnapshot();
            Assert.IsTrue(session.CapturePreparation()!.ChallengeAcknowledged);
            Assert.AreEqual(perks, JsonSerializer.Serialize(session.CapturePerks()));
            Assert.AreEqual(JsonSerializer.Serialize(before.RandomStreams), JsonSerializer.Serialize(after.RandomStreams));
            Assert.AreEqual(JsonSerializer.Serialize(before.FestivalFinances), JsonSerializer.Serialize(after.FestivalFinances));
            Assert.AreEqual(before.CurrentTick, after.CurrentTick);
            Assert.IsTrue(session.CapturePerks()!.Pending);
            Assert.IsTrue(BuildSession.Restored(session).CapturePreparation()!.ChallengeAcknowledged);
        }
    }

    [TestMethod]
    public void DuplicateOrStaleAcknowledgementIsRejectedWithoutMutation()
    {
        var session = GameSession.CreateBuildCampaign(20260922);
        var host = new SessionHost(session, "unused", new("challenge", "content", "rules"));
        foreach (var command in new[] { new AcknowledgeFestivalChallengeCommand(2, 1), new AcknowledgeFestivalChallengeCommand(1, 2) })
        {
            var before = session.CaptureSnapshot().AuthoritativeHash;
            Assert.IsFalse(session.Execute(host.Envelope(command)).IsAccepted);
            Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
        }
        BuildSession.Accept(session, new AcknowledgeFestivalChallengeCommand(1, 1));
        var hash = session.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(session.Execute(host.Envelope(new AcknowledgeFestivalChallengeCommand(1, 1))).IsAccepted);
        Assert.AreEqual(hash, session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void RetryResetsOnlyAttemptAcknowledgementAndFixedAssignment()
    {
        foreach (var tier in new[] { 1, 2 })
        {
            var session = GameSession.CreateDevelopmentFestival(20260922, tier);
            BuildSession.Accept(session, new AcknowledgeFestivalChallengeCommand(tier, 1));
            var draft = session.CapturePerks()!;
            BuildSession.Accept(session, new ChoosePerkCommand(draft.DraftAttempt, draft.Cursor, draft.Hand[0]));
            BuildSession.Accept(session, new UseDefaultBuildLayoutCommand());
            var plan = JsonSerializer.Serialize(session.CaptureBuildPlacements());
            var cash = session.CapturePreparation()!.OpeningCashPennies;
            // Exercise the existing reset directly; real hearing/Favour transitions are covered by NextFestivalTests.
            typeof(GameSession).GetMethod("RetryPreparedWeekend", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, []);
            var retried = session.CapturePreparation()!;
            Assert.AreEqual(tier, retried.Tier); Assert.AreEqual(2, retried.Attempt);
            Assert.IsFalse(retried.ChallengeAcknowledged); Assert.IsTrue(session.CapturePerks()!.Pending);
            Assert.AreEqual(plan, JsonSerializer.Serialize(session.CaptureBuildPlacements()));
            Assert.AreEqual(cash, session.CaptureSnapshot().FestivalFinances.Single().CashPennies);
            Assert.AreEqual(FestivalChallenges.ForTier(tier), FestivalChallenges.ForTier(retried.Tier));
        }
    }
}
