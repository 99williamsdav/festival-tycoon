using Festival.Simulation;
using System.Collections;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class LitterCuePlannerTests
{
    [TestMethod]
    public void DisposalComplainsOnceOnlyWhenActualFullBinArrivalIsObserved()
    {
        var planner = new LitterCuePlanner(); planner.Reset([new(1, null, 0)], 0);
        Assert.IsNull(planner.Observe([new(1, null, 0)], 80)); // Approaching is not disposal.
        var atBin = new LitterRemarkSituation[] { new(1, "purchase.1", 0) };
        var cue = planner.Observe(atBin, 160); Assert.IsNotNull(cue);
        CollectionAssert.Contains(LitterCuePlanner.FullBinLines, cue.Text);
        Assert.AreEqual(cue, planner.Observe(atBin, 160), "Pause does not advance the cue lifetime.");
        Assert.IsNull(planner.Observe(atBin, 400));
        Assert.IsNull(planner.Observe(atBin, 800), "The same disposal cannot repeat.");
        planner.Reset(atBin, 900); Assert.IsNull(planner.Observe(atBin, 980), "Load must not replay the existing disposal.");
    }

    [TestMethod]
    public void GroundRemarksNeedFourPiecesAndRespectSpacingCooldownAndSafety()
    {
        var planner = new LitterCuePlanner(); planner.Reset([new(1, null, 0), new(2, null, 0)], 0);
        Assert.IsNull(planner.Observe([new(1, null, 3), new(2, null, 0)], 80));
        var lots = new LitterRemarkSituation[] { new(1, null, 4), new(2, null, 5) };
        Assert.AreEqual(1UL, planner.Observe(lots, 160)!.AgentId);
        Assert.IsNull(planner.Observe(lots, 200, speechBlocked: true), "Safety speech suppresses routine remarks immediately.");
        Assert.IsNull(planner.Observe(lots, 400), "No replacement crowd chatter before global spacing.");
        Assert.IsNull(planner.Observe(lots, 800), "Stale queued complaints expire.");
        planner.Observe([new(1, null, 0)], 880);
        Assert.IsNull(planner.Observe([new(1, null, 5)], 960), "Leaving and re-entering cannot bypass the per-person cooldown.");
        planner.Observe([new(1, null, 0)], 3200);
        Assert.IsNotNull(planner.Observe([new(1, null, 5)], 3360));
        planner.Reset([new(1, null, 5)], 4000); Assert.IsNull(planner.Observe([new(1, null, 5)], 4080));
    }

    [TestMethod]
    public void FullBinRemarkTakesPriorityOverGroundAndPresentationDoesNotChangeState()
    {
        var planner = new LitterCuePlanner(); planner.Reset([], 0);
        var cue = planner.Observe([new(1, null, 5), new(2, "purchase.2", 0)], 80);
        Assert.AreEqual(2UL, cue!.AgentId);
        Assert.IsNull(planner.Observe([], 81), "A departed or ineligible person loses their remark.");
        var session = BuildSession.Started(); var hash = session.CaptureSnapshot().AuthoritativeHash;
        session.CaptureLitterRemarkSituations(); Assert.AreEqual(hash, session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void SituationQueryRequiresPhysicalDisposalAndMovingPastLitter()
    {
        var s = BuildSession.Ready(); BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(118, 166)));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var person = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        typeof(GameSession).GetMethod("MutatePerson", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s,
            [person, (Action<Person>)(p => { p.Admitted = true; p.Thirst = p.Hunger = p.ToiletNeed = p.HeatExposure = 2000; })]);
        var bin = s.CaptureBins().Single(); var side = new GridCell(bin.Cell.X - 2, bin.Cell.Z);
        var centre = TraversalGrid.CellCentre(side);
        var agents = (IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
        var nav = agents[new EntityId(person)]!;
        void Set(string name, object value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
        Set("XMillimetres", centre.XMillimetres); Set("ZMillimetres", centre.ZMillimetres); Set("Destination", side);
        Set("Action", AgentNavigationAction.Travelling);
        var pieces = Enumerable.Range(0, 20).Select(i => new WastePiece("full:" + i, 1, ImmersionProduct.Chips, 0,
            WasteLocation.Bin, 0, 0, bin.Id)).Concat(Enumerable.Range(0, 4).Select(i => new WastePiece("ground:" + i, 1,
            ImmersionProduct.Chips, 0, WasteLocation.Ground, centre.XMillimetres, centre.ZMillimetres))).Append(
                new("carried", person, ImmersionProduct.SoftDrink, 0, WasteLocation.Carried, centre.XMillimetres, centre.ZMillimetres, bin.Id, side, 0)).ToArray();
        typeof(GameSession).GetField("_litter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(s, new LitterSnapshot(1, pieces, []));
        var walking = s.CaptureLitterRemarkSituations().Single(p => p.AgentId == person);
        Assert.IsNull(walking.FullBinDisposalId); Assert.AreEqual(4, walking.NearbyGroundPieces);
        Set("Action", AgentNavigationAction.Arrived);
        var disposing = s.CaptureLitterRemarkSituations().Single(p => p.AgentId == person);
        Assert.AreEqual("carried", disposing.FullBinDisposalId); Assert.AreEqual(0, disposing.NearbyGroundPieces);
    }
}
