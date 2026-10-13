using Festival.ContentAdapter;
using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class FightPresentationTests
{
    private static (GameSession Session, ulong A, ulong B) Fixture() =>
        ((GameSession, ulong, ulong))typeof(StaffAutonomyTests).GetMethod("FightFixture", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [true, false, 0])!;
    private static void Disorder(GameSession s, DisorderSnapshot value) => typeof(GameSession).GetProperty("DisorderView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, value);
    private static GameSession Restore(GameSession s) { var result = GameSession.Restore(s.CapturePersistenceSnapshot()); Assert.IsTrue(result.IsSuccess, result.Error); return result.Session!; }

    [TestMethod]
    public void ActualReciprocalFightReadIsReadonlyAndHandlingRequiresPhysicalAttempt()
    {
        var (s, a, b) = Fixture(); var before = s.CaptureSnapshot().AuthoritativeHash;
        var pair = FightPresentation.Read(s).Single();
        Assert.AreEqual(a, pair.Key.Initiator); Assert.AreEqual(b, pair.Key.Opponent); Assert.IsNull(pair.Handler);
        Assert.AreEqual(before, s.CaptureSnapshot().AuthoritativeHash);
        BuildSession.Accept(s, new SetPausedCommand(true)); Assert.AreEqual(pair, FightPresentation.Read(s).Single()); BuildSession.Accept(s, new SetPausedCommand(false));
        s.AdvanceWithoutSnapshot(1); Assert.IsNull(FightPresentation.Read(s).Single().Handler, "Dispatch/travel is not intervention.");
        for (var i = 0; i < 400 && s.CaptureDisorder()!.Incidents.Single().HandlingAttempt is null; i++) s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(s.CaptureDisorder()!.SecurityId, FightPresentation.Read(s).Single().Handler);
        var restored = Restore(s); Assert.AreEqual(FightPresentation.Read(s).Single(), FightPresentation.Read(restored).Single());
    }

    [TestMethod]
    public void ArgumentsHistoryUnavailableAndBrokenPairsNeverBecomeFightPresentation()
    {
        var (s, a, b) = Fixture(); var original = s.CaptureDisorder()!;
        Disorder(s, original with { People = original.People.Select(p => p.AgentId == a ? p with { Stage = DisorderStage.Argument } : p).ToArray() });
        Assert.AreEqual(0, FightPresentation.Read(s).Count);
        Disorder(s, original with { Incidents = original.Incidents.Select(p => p with { InjuryTick = -2 }).ToArray() });
        Assert.AreEqual(0, FightPresentation.Read(s).Count);
        Disorder(s, original with { People = original.People.Select(p => p.AgentId == b ? p with { OpponentId = null } : p).ToArray() });
        Assert.AreEqual(0, FightPresentation.Read(s).Count);
        Disorder(s, original);
        var m = s.CaptureMedical()!;
        typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, m with {
            Needs = m.Needs.Select(p => p.AgentId == a ? p with { Stage = MedicalStage.Collapsed, Intent = MedicalIntent.Collapsed } : p).ToArray() });
        Assert.AreEqual(0, FightPresentation.Read(s).Count);
        Assert.AreEqual(0, FightPresentation.Read(BuildSession.Ready()).Count);
    }

    [TestMethod]
    public void StewardOpponentIsNotMistakenForThirdPartyHandling()
    {
        var (s, a, _) = Fixture(); var d = s.CaptureDisorder()!;
        Disorder(s, d with { Incidents = [], People = d.People.Select(p => p.Stage == DisorderStage.Fight ? p with { Stage = DisorderStage.Argument, OpponentId = null } : p).ToArray() });
        typeof(GameSession).GetMethod("BeginDisorderFight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, [a, d.SecurityId, "labelled:test-steward-fight"]);
        var pair = FightPresentation.Read(s).Single();
        Assert.AreEqual(d.SecurityId, pair.Key.Opponent); Assert.IsNull(pair.Handler);
    }
}
