using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class LineupBookingTests
{
    private static CommandResult Send(GameSession s, SessionCommand c) => s.Execute(new(new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, c));
    private static void Accept(GameSession s, SessionCommand c) { var r = Send(s, c); Assert.IsTrue(r.IsAccepted, r.Message); }
    private static GameSession New(bool reactions = true)
    {
        return BuildSession.Drafted(20260922);
    }
    private static void Ready(GameSession s)
    {
        Accept(s, new SetProgrammeCommand(["act.copper-static", "act.meadow-lanterns", "act.neon-postcards"]));
        Accept(s, new AcceptPreparationOfferCommand("staff.steward"));
    }
    private static void RestoreExact(GameSession s)
    {
        var r = GameSession.Restore(s.CapturePersistenceSnapshot()); Assert.IsTrue(r.IsSuccess, r.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, r.Session!.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void ExactApprovedTraitsAndIntegerFormulasAreMonotonicAndBounded()
    {
        var acts = New().GetFestivalActs(); CollectionAssert.AreEqual(new[] { 20, 45, 35, 80, 90, 60 }, acts.Select(a => a.Ego).ToArray());
        CollectionAssert.AreEqual(new[] { 80, 90, 65, 70, 85, 75 }, acts.Select(a => a.Professionalism).ToArray());
        var s = New(); var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest);
        foreach (var act in acts)
            Assert.AreEqual(guest.ExpectedGenre == act.Genre ? 80 + (int)(guest.AgentId % 21) : 15 + (int)((guest.AgentId * 37 + (ulong)act.Genre * 17 + s.CampaignSeed) % 56), s.FestivalAffinity(guest.AgentId, act));
        for (var sum = 0; sum <= 300; sum++) Assert.AreEqual(Math.Clamp((sum - 150) * 10, -1500, 1500), GameSession.GuestLineupAdjustment(sum));
        Assert.AreEqual(780, GameSession.PerformerLineupPenalty(acts[3], 0)); Assert.AreEqual(776, GameSession.PerformerLineupPenalty(acts[4], 1));
        foreach (var a in acts) Assert.AreEqual(0, GameSession.PerformerLineupPenalty(a, 2));
        var sample = acts[3];
        for (var prof = 0; prof < 100; prof++) Assert.IsTrue(GameSession.PerformerLineupPenalty(sample with { Professionalism = prof }, 0) >= GameSession.PerformerLineupPenalty(sample with { Professionalism = prof + 1 }, 0));
        Assert.AreEqual(0, GameSession.PerformerLineupPenalty(sample with { Ego = 69 }, 0)); Assert.AreEqual(750, GameSession.PerformerLineupPenalty(sample with { Ego = 100, Professionalism = 100 }, 0));
    }
    [TestMethod]
    public void SharedResolverAssignsMovesSwapsReplacesRemovesAndRejectsStaleOrInvalid()
    {
        var s = New(); var cash = s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        void Edit(string id, int source, int target, bool remove = false) { var p = s.PreviewLineupEdit(id, source, target, remove); Assert.IsTrue(p.IsValid, p.Message); Accept(s, new SetProgrammeCommand(p.ActIds)); RestoreExact(s); }
        Edit("act.meadow-lanterns", -1, 0); Edit("act.meadow-lanterns", 0, 2); Edit("act.copper-static", -1, 0); Edit("act.meadow-lanterns", 2, 0);
        CollectionAssert.AreEqual(new[] { "act.meadow-lanterns", "", "act.copper-static" }, s.CapturePreparationPlan()!.ActIds);
        Edit("act.orchard-chorus", -1, 0); Edit("act.orchard-chorus", 0, 0, true);
        var hash = s.CaptureSnapshot().AuthoritativeHash;
        foreach (var p in new[] { s.PreviewLineupEdit("bad", -1, 0), s.PreviewLineupEdit("act.copper-static", 0, 1), s.PreviewLineupEdit("act.copper-static", 2, -1), s.PreviewLineupEdit("act.copper-static", 2, 3) }) Assert.IsFalse(p.IsValid);
        Assert.IsTrue(s.PreviewLineupEdit("act.copper-static", 2, 2).IsNoOp);
        Assert.IsFalse(Send(s, new SetProgrammeCommand(["act.copper-static", "act.copper-static", ""])).IsAccepted);
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash); Assert.AreEqual(cash, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
    }
    [TestMethod]
    public void CoordinatorEditsAndFailedStartAreAtomicWithOneSetupPaymentAndLiveLock()
    {
        var s = New(); Ready(s); var compatibility = new SaveCompatibility("booking-test", "content", "booking-v1");
        var directory = Path.Combine(Path.GetTempPath(), "festival-booking-" + Guid.NewGuid()); var hash = s.CaptureSnapshot().AuthoritativeHash;
        var preview = s.PreviewLineupEdit("act.orchard-chorus", -1, 0);
        var failed = EquipmentCommandCoordinator.Execute(directory, s, new SetProgrammeCommand(preview.ActIds), compatibility, DateTimeOffset.UtcNow, 1, _ => throw new IOException("test save failure"));
        Assert.IsFalse(failed.IsSuccess); Assert.AreSame(s, failed.Session); Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
        var startFailed = EquipmentCommandCoordinator.Execute(directory, s, new StartPreparedEditionCommand(), compatibility, DateTimeOffset.UtcNow, 1, _ => throw new IOException("test start failure"));
        Assert.IsFalse(startFailed.IsSuccess); Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash); Assert.IsFalse(s.CapturePreparation()!.People.Any(p => p.Admitted));
        var started = EquipmentCommandCoordinator.Execute(directory, s, new StartPreparedEditionCommand(), compatibility, DateTimeOffset.UtcNow, 1); Assert.IsTrue(started.IsSuccess, started.Error); s = started.Session;
        Assert.AreEqual(1, s.CapturePreparation()!.SetupPayments!.Length); Assert.IsFalse(s.PreviewLineupEdit("act.orchard-chorus", -1, 0).IsValid);
        var loaded = AutosaveRotation.LoadNewestValid(directory, compatibility); Assert.IsTrue(loaded.IsSuccess, loaded.Error); RestoreExact(loaded.Session!);
        Assert.IsFalse(EquipmentCommandCoordinator.Execute(directory, s, new StartPreparedEditionCommand(), compatibility, DateTimeOffset.UtcNow, 2).IsSuccess);
    }
}
