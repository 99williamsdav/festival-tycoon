using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class WaterFoundationsTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command));

    private static GameSession Restore(GameSession session)
    {
        var result = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash);
        return result.Session;
    }

    private static GameSession StartWithWest(bool tower = false, bool share = false)
    {
        var session = GameSession.CreateMedicalCampaign(20260922);
        Assert.IsTrue(Send(session, new ApplyWaterFoundationEffectCommand("water.west")).IsAccepted);
        if (tower) Assert.IsTrue(Send(session, new ApplyWaterFoundationEffectCommand("water.tower")).IsAccepted);
        if (share) Assert.IsTrue(Send(session, new CommitCommunityWaterShareCommand()).IsAccepted);
        session = Restore(session);
        foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(offer)).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        return Restore(session);
    }

    private static void PutNear(GameSession session, ulong id, GridCell cell)
    {
        var field = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var agents = field.GetValue(session)!;
        var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(id)])!;
        var centre = TraversalGrid.CellCentre(cell);
        foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
                     ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres) })
            agent.GetType().GetProperty(name)!.SetValue(agent, value);
    }

    private static void SetArrived(GameSession session, ulong id, GridCell cell)
    {
        PutNear(session, id, cell);
        var field = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var agents = field.GetValue(session)!;
        var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(id)])!;
        agent.GetType().GetProperty("Destination")!.SetValue(agent, cell);
        agent.GetType().GetProperty("Action")!.SetValue(agent, AgentNavigationAction.Arrived);
        agent.GetType().GetProperty("Route")!.SetValue(agent, new List<GridCell>());
        agent.GetType().GetProperty("RouteIndex")!.SetValue(agent, 0);
    }

    [TestMethod]
    public void TowerAndCouncilSharingComposeWithoutTapPressureLoss()
    {
        var baseSession = GameSession.CreateMedicalCampaign(20260922);
        var extra = GameSession.CreateMedicalCampaign(20260922);
        Assert.IsTrue(Send(extra, new ApplyWaterFoundationEffectCommand("water.west")).IsAccepted);
        for (ulong id = 1; id <= 4; id++)
            Assert.AreEqual(baseSession.EffectiveMedicalDrinkThirstPerTickFor(id), extra.EffectiveMedicalDrinkThirstPerTickFor(id));
        Assert.IsTrue(Send(extra, new ApplyWaterFoundationEffectCommand("water.tower")).IsAccepted);
        for (ulong id = 1; id <= 4; id++)
            Assert.AreEqual(GameSession.MedicalDrinkThirstPerTickFor(id) + 4, extra.EffectiveMedicalDrinkThirstPerTickFor(id));
        Assert.IsTrue(Send(extra, new CommitCommunityWaterShareCommand()).IsAccepted);
        foreach (var id in Enumerable.Range(1, 4).Select(value => (ulong)value))
            Assert.AreEqual(Math.Min(12, GameSession.MedicalDrinkThirstPerTickFor(id)) + 4,
                // Sharing activates when the weekend opens.
                StartSharedRate(extra, id));
        Restore(extra);
    }

    private static int StartSharedRate(GameSession session, ulong id)
    {
        if (session.PreparedStatus == PreparationStatus.Preparing)
        {
            foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" })
                Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(offer)).IsAccepted);
            Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        }
        return session.EffectiveMedicalDrinkThirstPerTickFor(id);
    }

    [TestMethod]
    public void SeparatePhysicalArrivalsOwnSeparateTapsAndSaveTheirChoices()
    {
        var session = StartWithWest(tower: true, share: true);
        var ids = session.CapturePreparation()!.People.Where(item => item.Role == ProtectedPersonRole.Guest)
            .Take(2).Select(item => item.AgentId).ToArray();
        var west = GameSession.ExtraWaterSites.Single(item => item.Id == "water.west").Cell;
        PutNear(session, ids[0], new GridCell(GameSession.MedicalWaterCell.X, GameSession.MedicalWaterCell.Z + 5));
        PutNear(session, ids[1], new GridCell(west.X, west.Z + 5));
        foreach (var id in ids)
            Assert.IsTrue(Send(session, new MedicalCommand(id, MedicalAction.GuideToWater)).IsAccepted);
        var initial = session.CaptureMedical()!;
        Assert.AreEqual("water.main", initial.Needs.Single(item => item.AgentId == ids[0]).WaterPointId);
        Assert.AreEqual("water.west", initial.Needs.Single(item => item.AgentId == ids[1]).WaterPointId);
        Assert.AreEqual(0, session.CaptureWaterPoints().Sum(point => point.Queue.Length), "Approach does not reserve a place.");
        session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(2, session.CaptureWaterPoints().Sum(point => point.Queue.Length));
        session = Restore(session);
        for (var tick = 0; tick < 150 && session.CaptureWaterPoints().Count(point => point.OwnerId is not null) < 2; tick++)
            session.AdvanceWithoutSnapshot(1);
        var points = session.CaptureWaterPoints();
        Assert.AreEqual(ids[0], points.Single(point => point.Id == "water.main").OwnerId);
        Assert.AreEqual(ids[1], points.Single(point => point.Id == "water.west").OwnerId);
        var before = session.CaptureMedical()!.Needs.Where(item => ids.Contains(item.AgentId)).ToDictionary(item => item.AgentId);
        session.AdvanceWithoutSnapshot(1);
        var after = session.CaptureMedical()!.Needs.Where(item => ids.Contains(item.AgentId)).ToDictionary(item => item.AgentId);
        foreach (var id in ids)
        {
            Assert.AreEqual(session.EffectiveMedicalDrinkThirstPerTickFor(id), before[id].Thirst - after[id].Thirst);
            Assert.AreEqual(session.EffectiveMedicalDrinkHeatPerTickFor(id), before[id].HeatExposure - after[id].HeatExposure);
        }
        Restore(session);
    }

    [TestMethod]
    public void WestTapUsesItsOwnPhysicalArrivalFifoAndWaterWaitGrievance()
    {
        var session = GameSession.CreateDisorderCampaign(20260925);
        Assert.IsTrue(Send(session, new ApplyWaterFoundationEffectCommand("water.west")).IsAccepted);
        foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(offer)).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        var ids = session.CaptureDisorder()!.People.Take(2).Select(item => item.AgentId).ToArray();
        var west = GameSession.ExtraWaterSites.Single(item => item.Id == "water.west").Cell;
        // This labelled positioning fixture starts the two already-admitted guests beside the west tap.
        var preparationField = typeof(GameSession).GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var preparation = session.CapturePreparation()!;
        preparationField.SetValue(session, preparation with { People = preparation.People.Select(person => ids.Contains(person.AgentId)
            ? person with { Admitted = true } : person).ToArray() });
        foreach (var id in ids)
        {
            PutNear(session, id, new GridCell(west.X, west.Z + 5));
            Assert.IsTrue(Send(session, new MedicalCommand(id, MedicalAction.GuideToWater)).IsAccepted);
        }
        Assert.AreEqual(0, session.CaptureWaterPoints().Sum(point => point.Queue.Length));
        session.AdvanceWithoutSnapshot(1);
        var westPoint = session.CaptureWaterPoints().Single(point => point.Id == "water.west");
        CollectionAssert.AreEqual(ids, westPoint.Queue);
        Assert.AreEqual(0, session.CaptureWaterPoints().Single(point => point.Id == "water.main").Queue.Length);
        session = Restore(session);
        // Diagnostic fixture accelerates this person's tolerance only to exercise the grievance branch.
        var disorderField = typeof(GameSession).GetField("_disorder", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var disorder = session.CaptureDisorder()!;
        disorderField.SetValue(session, disorder with { People = disorder.People.Select(person => person.AgentId == ids[1]
            ? person with { QueueToleranceTicks = 1 } : person).ToArray() });
        for (var tick = 0; tick < 24 && session.CaptureDisorder()!.People.Single(item => item.AgentId == ids[1]).Grievance != DisorderGrievance.WaterWait; tick++)
            session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(DisorderGrievance.WaterWait,
            session.CaptureDisorder()!.People.Single(item => item.AgentId == ids[1]).Grievance);
        Assert.AreEqual("water.west", session.CaptureMedical()!.Needs.Single(item => item.AgentId == ids[1]).WaterPointId);
        Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.CloseWater)).IsAccepted);
        Assert.IsTrue(session.CaptureWaterPoints().All(point => point.Queue.Length == 0 && point.Overflow.Length == 0 && point.OwnerId is null));
    }

    [TestMethod]
    public void EarlierWestArrivalBeatsLowerIdAndChoiceStaysPutWhenOtherWaitChanges()
    {
        var session = StartWithWest();
        var ids = session.CapturePreparation()!.People.Where(item => item.Role == ProtectedPersonRole.Guest)
            .Take(3).Select(item => item.AgentId).ToArray();
        var west = GameSession.ExtraWaterSites.Single(item => item.Id == "water.west").Cell;
        PutNear(session, ids[1], new GridCell(west.X, west.Z + 5));
        Assert.IsTrue(Send(session, new MedicalCommand(ids[1], MedicalAction.GuideToWater)).IsAccepted);
        session.AdvanceWithoutSnapshot(1);
        PutNear(session, ids[0], new GridCell(west.X, west.Z + 6));
        Assert.IsTrue(Send(session, new MedicalCommand(ids[0], MedicalAction.GuideToWater)).IsAccepted);
        PutNear(session, ids[2], GameSession.MedicalQueueSlot(0));
        Assert.IsTrue(Send(session, new MedicalCommand(ids[2], MedicalAction.GuideToWater)).IsAccepted);
        session.AdvanceWithoutSnapshot(1);
        var points = session.CaptureWaterPoints();
        CollectionAssert.AreEqual(new[] { ids[1], ids[0] }, points.Single(point => point.Id == "water.west").Queue);
        Assert.AreEqual(ids[2], points.Single(point => point.Id == "water.main").Queue[0]);
        Assert.AreEqual("water.west", session.CaptureMedical()!.Needs.Single(need => need.AgentId == ids[0]).WaterPointId);
        session.AdvanceWithoutSnapshot(12);
        Assert.AreEqual("water.west", session.CaptureMedical()!.Needs.Single(need => need.AgentId == ids[0]).WaterPointId);
        Restore(session);
    }

    [TestMethod]
    public void WestOverflowPromotesOldestTailMemberAndPersists()
    {
        var session = StartWithWest();
        var ids = session.CapturePreparation()!.People.Where(item => item.Role == ProtectedPersonRole.Guest)
            .Take(11).Select(item => item.AgentId).ToArray();
        var west = GameSession.ExtraWaterSites.Single(item => item.Id == "water.west").Cell;
        var medicalField = typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var medical = session.CaptureMedical()!;
        medicalField.SetValue(session, medical with
        {
            Needs = medical.Needs.Select(need =>
            {
                var index = Array.IndexOf(ids, need.AgentId);
                return index < 0 ? need : need with { Intent = MedicalIntent.SeekWater,
                    QueueSlot = index < 10 ? index : null, WaterPointId = "water.west" };
            }).ToArray(),
            ExtraWaterPoints = [new WaterPointState("water.west", west, ids.Take(10).ToArray(), [ids[10]], null, 0)]
        });
        for (var index = 0; index < ids.Length; index++)
            SetArrived(session, ids[index], index < 10
                ? new GridCell(west.X + index / 2, west.Z + 5 + index * 2)
                : new GridCell(west.X + 5, west.Z + 25));
        session = Restore(session); // labelled coherent full-line/overflow fixture
        Assert.IsTrue(Send(session, new MedicalCommand(ids[0], MedicalAction.ReturnToShow)).IsAccepted);
        var point = session.CaptureWaterPoints().Single(item => item.Id == "water.west");
        CollectionAssert.AreEqual(ids.Skip(1).ToArray(), point.Queue);
        Assert.AreEqual(0, point.Overflow.Length);
        Assert.AreEqual(9, session.CaptureMedical()!.Needs.Single(need => need.AgentId == ids[10]).QueueSlot);
        Restore(session);
    }

    [TestMethod]
    public void FoundationEffectsPersistAsDurablePropertyAcrossFailedWeekendRetry()
    {
        var session = StartWithWest(tower: true);
        session.AdvanceWithoutSnapshot(6_200); // labelled untreated Hot-death fixture
        Assert.AreEqual(PreparationStatus.Failed, session.PreparedStatus);
        session = Restore(session);
        Assert.IsTrue(Send(session, new SpendCouncilFavourCommand()).IsAccepted);
        Assert.AreEqual(PreparationStatus.Preparing, session.PreparedStatus);
        CollectionAssert.AreEqual(new[] { "water.west" }, session.CapturePreparation()!.ExtraWaterSiteIds);
        Assert.IsTrue(session.CapturePreparation()!.WaterTowerOwned);
        Assert.AreEqual(2, session.CaptureWaterPoints().Count);
        Assert.IsTrue(session.CaptureWaterPoints().All(point => point.Queue.Length == 0 && point.OwnerId is null));
        Restore(session);
    }
}
