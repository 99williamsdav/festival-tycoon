using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class ConfrontationSpacingTests
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Position(GameSession s, ulong id, GridCell cell) => typeof(StaffAutonomyTests).GetMethod("PositionFixture", Private)!.Invoke(null, [s, id, cell]);
    private static (GameSession S, ulong A, ulong B) Fixture(int dx, int dz)
    {
        var s = (GameSession)typeof(StaffAutonomyTests).GetMethod("Started", Private)!.Invoke(null, [false])!;
        var guests = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).Take(2).ToArray();
        Position(s, guests[0].AgentId, new(121, 178)); Position(s, guests[1].AgentId, new(121 + dx, 178 + dz));
        Position(s, s.CaptureDisorder()!.SecurityId, new(117, 180));
        var d = s.CaptureDisorder()!;
        typeof(GameSession).GetProperty("DisorderView", Instance)!.SetValue(s, d with { People = d.People.Select(p => guests.Any(g => g.AgentId == p.AgentId)
            ? p with { Stage = DisorderStage.Argument, Grievance = DisorderGrievance.MusicCutoff, Pressure = 8000, StageTick = s.CurrentTick } : p).ToArray() });
        return (s, guests[0].AgentId, guests[1].AgentId);
    }
    private static void Begin(GameSession s, ulong a, ulong b) => typeof(GameSession).GetMethod("BeginDisorderFight", Instance)!.Invoke(s, [a, b, "labelled:spacing-test"]);
    private static NavigationObservation Nav(GameSession s, ulong id) => s.CaptureObservation().NavigationAgents.Single(n => n.Id.Value == id);
    private static long Gap(NavigationObservation a, NavigationObservation b) { var dx = (long)a.XMillimetres - b.XMillimetres; var dz = (long)a.ZMillimetres - b.ZMillimetres; return dx * dx + dz * dz; }

    [TestMethod]
    [DataRow(4, 0)] [DataRow(3, 0)] [DataRow(3, 2)]
    public void OpenGroundApproachIsShortPhysicalAndKeepsAuthorityAndDeadline(int dx, int dz)
    {
        var (s, a, b) = Fixture(dx, dz); var beforeA = Nav(s, a); var beforeB = Nav(s, b);
        Begin(s, a, b); var tick = s.CurrentTick;
        Assert.AreEqual((beforeA.XMillimetres, beforeA.ZMillimetres), (Nav(s, a).XMillimetres, Nav(s, a).ZMillimetres), "No teleport at state change.");
        Assert.AreEqual((beforeB.XMillimetres, beforeB.ZMillimetres), (Nav(s, b).XMillimetres, Nav(s, b).ZMillimetres));
        Assert.IsTrue(Nav(s, a).Action == AgentNavigationAction.Travelling || Nav(s, b).Action == AgentNavigationAction.Travelling);
        var previous = new[] { Nav(s, a), Nav(s, b) };
        for (var i = 0; i < 100 && previous.Any(n => n.Action == AgentNavigationAction.Travelling); i++)
        {
            s.AdvanceWithoutSnapshot(1); var now = new[] { Nav(s, a), Nav(s, b) };
            for (var p = 0; p < 2; p++)
            {
                Assert.IsTrue(Gap(previous[p], now[p]) <= 40 * 40, "Uses normal continuous navigation, no cosmetic/root displacement.");
                Assert.IsTrue(TraversalSweep.IsWalkable(s.TraversalGrid!, previous[p].XMillimetres, previous[p].ZMillimetres, now[p].XMillimetres, now[p].ZMillimetres));
            }
            previous = now;
        }
        var finalGap = Gap(Nav(s, a), Nav(s, b)); Assert.IsTrue(finalGap >= 700L * 700 && finalGap <= 800L * 800);
        Assert.IsTrue(Gap(beforeA, Nav(s, a)) <= 1250L * 1250 && Gap(beforeB, Nav(s, b)) <= 1250L * 1250);
        Assert.AreEqual(tick, s.CaptureDisorder()!.Incidents.Single().FightTick);
        Assert.AreEqual("disorder.confrontation", Nav(s, a).IntentId); Assert.AreEqual("disorder.confrontation", Nav(s, b).IntentId);
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot()); Assert.IsTrue(restored.IsSuccess, restored.Error);
        s.AdvanceWithoutSnapshot(400); restored.Session!.AdvanceWithoutSnapshot(400);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void GuestApproachesFixedStewardOnShortLegalDiagonalWithoutTakingItsNavigation()
    {
        var (s, a, b) = Fixture(3, 0); var steward = s.CaptureDisorder()!.SecurityId;
        Position(s, a, new(108, 178)); Position(s, b, new(134, 178)); Position(s, steward, new(111, 178));
        var disorder = s.CaptureDisorder()!;
        typeof(GameSession).GetProperty("DisorderView", Instance)!.SetValue(s, disorder with { Stewards = disorder.Stewards.Select(p => p.WorkerId == steward
            ? p with { Stage = SecurityResponseStage.Confronting, TargetId = a, StartedTick = s.CurrentTick } : p).ToArray() });
        var before = Nav(s, steward); Begin(s, a, steward);
        for (var i = 0; i < 140 && Nav(s, a).Action == AgentNavigationAction.Travelling; i++)
        {
            var prior = Nav(s, a); s.AdvanceWithoutSnapshot(1);
            Assert.IsTrue(TraversalSweep.IsWalkable(s.TraversalGrid!, prior.XMillimetres, prior.ZMillimetres, Nav(s, a).XMillimetres, Nav(s, a).ZMillimetres));
        }
        Assert.AreEqual((before.XMillimetres, before.ZMillimetres, before.Action, before.IntentId),
            (Nav(s, steward).XMillimetres, Nav(s, steward).ZMillimetres, Nav(s, steward).Action, Nav(s, steward).IntentId));
        Assert.IsTrue(Gap(Nav(s, a), Nav(s, steward)) >= 700L * 700 && Gap(Nav(s, a), Nav(s, steward)) <= 800L * 800,
            $"Gap={Math.Sqrt(Gap(Nav(s, a), Nav(s, steward)))}mm action={Nav(s, a).Action}");
    }

    [TestMethod]
    public void BlockedApproachFallsBackWithoutHoldingTheFightClock()
    {
        var (s, a, b) = Fixture(4, 0); var ca = TraversalGrid.WorldToCell(Nav(s, a).XMillimetres, Nav(s, a).ZMillimetres);
        var cells = s.TraversalGrid!.Overrides.ToDictionary(p => p.Key, p => p.Value);
        for (var dz = -1; dz <= 1; dz++) for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dz == 0) continue;
            var cell = new GridCell(ca.X + dx, ca.Z + dz); cells[cell] = new(cell, GroundSurface.Grass, false);
        }
        typeof(GameSession).GetField("_traversalGrid", Instance)!.SetValue(s, new TraversalGrid(cells.Values));
        var before = (Nav(s, a), Nav(s, b)); Begin(s, a, b);
        Assert.AreEqual(Gap(before.Item1, before.Item2), Gap(Nav(s, a), Nav(s, b)));
        Assert.AreEqual(AgentNavigationAction.Arrived, Nav(s, a).Action); Assert.AreEqual(AgentNavigationAction.Arrived, Nav(s, b).Action);
        Assert.AreEqual(s.CurrentTick, s.CaptureDisorder()!.Incidents.Single().FightTick);
        var started = s.CurrentTick; s.AdvanceWithoutSnapshot(800);
        Assert.AreEqual(started + 800, s.CurrentTick);
        s.AdvanceWithoutSnapshot((int)((8 - s.CurrentTick % 8) % 8)); // Existing disorder evaluation cadence.
        Assert.AreNotEqual(DisorderStage.Fight, s.CaptureDisorder()!.People.Single(p => p.AgentId == a).Stage);
    }

    [TestMethod]
    public void CloseExistingPairIsNotPushedAndServiceQueueIsNotClaimedForCloserPoses()
    {
        var (s, a, b) = Fixture(1, 0); Begin(s, a, b); Assert.AreEqual(500L * 500, Gap(Nav(s, a), Nav(s, b)));
        var (queued, first, second) = Fixture(4, 0); var service = GameSession.ImmersionServiceCell(queued.CaptureVendors().First());
        Position(queued, first, service); Position(queued, second, new(service.X + 4, service.Z));
        var before = Gap(Nav(queued, first), Nav(queued, second)); Begin(queued, first, second);
        Assert.AreEqual(before, Gap(Nav(queued, first), Nav(queued, second)));
        Assert.AreEqual(AgentNavigationAction.Arrived, Nav(queued, first).Action);
    }
}
