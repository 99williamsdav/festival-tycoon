using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class LineupBookingRetryTests
{
    private static void Accept(GameSession s, SessionCommand c) { var r = s.Execute(new(new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, c)); Assert.IsTrue(r.IsAccepted, r.Message); }
    [TestMethod]
    public void ActualWarningDeathFavourRetryRetainsReactionIdentityAndResetsAdmission()
    {
        var s = GameSession.CreateBookingCampaign(20260922); var draft = s.CapturePerks()!; Accept(s, new ChoosePerkCommand(draft.DraftAttempt, draft.Cursor, draft.Hand[0]));
        Accept(s, new SetProgrammeCommand(["act.copper-static", "act.neon-postcards", "act.field-frequency"])); Accept(s, new AcceptPreparationOfferCommand("staff.steward")); Accept(s, new StartPreparedEditionCommand());
        // Labelled severe-exposure/unavailable-medic fixture; production warning and death own the transition.
        var prep = s.CapturePreparation()!; var guest = prep.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        typeof(GameSession).GetField("_preparation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(s, prep with { People = prep.People.Select(p => p with { Admitted = true }).ToArray() });
        var immersion = s.CaptureImmersion()!; typeof(GameSession).GetField("_immersion", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(s, immersion with { People = immersion.People.Select(p => p.AgentId == guest ? p with { Intoxication = 10000 } : p).ToArray() });
        var medical = s.CaptureMedical()!; var medics = s.GetMedicResponses().Select(m => m.WorkerId).ToArray();
        typeof(GameSession).GetField("_medical", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(s, medical with { Needs = medical.Needs.Select(n => medics.Contains(n.AgentId) ? n with { Intent = MedicalIntent.Rest, Reason = "Labelled unavailable medic fixture" } : n).ToArray() });
        s.AdvanceWithoutSnapshot(4000); Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus);
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot()); Assert.IsTrue(restored.IsSuccess, restored.Error); s = restored.Session!;
        Accept(s, new SpendCouncilFavourCommand()); Assert.AreEqual(1, s.CapturePreparation()!.LineupReactionsVersion); Assert.IsTrue(s.CapturePreparation()!.People.All(p => !p.Admitted && p.Satisfaction == 5000));
        Assert.AreEqual(0, s.CapturePreparationPlan()!.ActIds.Length);
        draft = s.CapturePerks()!; Accept(s, new ChoosePerkCommand(draft.DraftAttempt, draft.Cursor, draft.Hand[0]));
        Accept(s, new SetProgrammeCommand(["act.meadow-lanterns", "act.orchard-chorus", "act.neon-postcards"])); Accept(s, new AcceptPreparationOfferCommand("staff.steward")); Accept(s, new StartPreparedEditionCommand());
        s.AdvanceWithoutSnapshot(1800); Assert.IsTrue(s.CapturePreparation()!.People.Any(p => p.Admitted)); Assert.AreEqual(2, s.CapturePreparation()!.SetupPayments!.Length);
        restored = GameSession.Restore(s.CapturePersistenceSnapshot()); Assert.IsTrue(restored.IsSuccess, restored.Error); Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void ActualResultsSaveStillLoadsWithOriginalHashAndNewHeaderRejectsWithoutRewrite()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROGUELIKE_DESIGN.md"))) dir = dir.Parent;
        var file = Path.Combine(dir!.FullName, "reports", "evidence", "R0.05m", "final-dev5-1280x720", "saves", "manual-preparation.ftsave"); var bytes = File.ReadAllBytes(file);
        var old = new SaveCompatibility("0.0.1-r0.05m-results-v1", LowerWitteringFarmScenario.ContentCompatibilityHash, "r0-results-v1");
        var loaded = SaveFileAdapter.LoadFile(file, old); Assert.IsTrue(loaded.IsSuccess, loaded.Error); Assert.IsNull(loaded.Session!.CapturePreparation()!.LineupReactionsVersion);
        Assert.AreEqual("549d7d78a57d0e1322383af67a1bdd36353d2a658c337cf8faeffce19ac7b4c3", loaded.Session.CaptureSnapshot().AuthoritativeHash);
        var current = new SaveCompatibility("0.0.1-r0.05n-booking-v1", LowerWitteringFarmScenario.ContentCompatibilityHash, "r0-booking-v1"); Assert.IsFalse(SaveFileAdapter.LoadFile(file, current).IsSuccess); CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file));
    }
}
