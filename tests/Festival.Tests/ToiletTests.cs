using Festival.Simulation;
using System.Collections;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class ToiletTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase, session.CurrentTick,
        session.NextSubmissionSequence, null, command));
    private static void SetImmersion(GameSession session, ImmersionSnapshot snapshot) =>
        typeof(GameSession).GetProperty("ImmersionView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, snapshot);
    private static void Invoke(GameSession session, string method) =>
        typeof(GameSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, null);
    private static void Position(GameSession session, ulong id, GridCell cell, string intent)
    {
        var agents = (IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var agent = agents[new EntityId(id)]!;
        var centre = TraversalGrid.CellCentre(cell);
        void Set(string name, object value) => agent.GetType().GetProperty(name)!.SetValue(agent, value);
        Set("XMillimetres", centre.XMillimetres); Set("ZMillimetres", centre.ZMillimetres);
        Set("SegmentOriginXMillimetres", centre.XMillimetres); Set("SegmentOriginZMillimetres", centre.ZMillimetres);
        Set("Route", new List<GridCell>()); Set("RouteIndex", 0); Set("SegmentProgressMicrometres", 0);
        Set("Action", AgentNavigationAction.Arrived); Set("Destination", cell); Set("IntentId", intent);
    }
    private static GameSession Open()
    {
        var session = BuildSession.Planned(20260928);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"])).IsAccepted);
        Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        var preparation = session.CapturePreparation()!;
        typeof(GameSession).GetProperty("PreparationView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            preparation with { People = preparation.People.Select(p => p with { Admitted = true }).ToArray() });
        return session;
    }

    private static GameSession OpenWithStock()
    {
        var session = BuildSession.Planned(20260928);
        Assert.IsTrue(Send(session, new SetPreparationStockCommand(40, 40, 32)).IsAccepted);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"])).IsAccepted);
        Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        var preparation = session.CapturePreparation()!;
        typeof(GameSession).GetProperty("PreparationView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            preparation with { People = preparation.People.Select(p => p with { Admitted = true }).ToArray() });
        return session;
    }

    private static GameSession OpenBuildWithTwoToilets()
    {
        var session = GameSession.CreateBuildCampaign(20260929);
        var perk = session.CapturePerks()!;
        Assert.IsTrue(Send(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])).IsAccepted);
        Assert.IsTrue(Send(session, new UseDefaultBuildLayoutCommand()).IsAccepted);
        GridCell? second = null;
        for (var x = 140; x <= 185 && second is null; x += 5)
        for (var z = 160; z <= 175 && second is null; z += 5)
        {
            var cell = new GridCell(x, z);
            if (session.ValidateCommand(new(new(session.NextSubmissionSequence + 1), session.CampaignId,
                session.Phase, session.CurrentTick, session.NextSubmissionSequence, null,
                new PlaceBuildServiceCommand(BuildServiceKind.Toilet, cell, 2))) is null) second = cell;
        }
        Assert.IsNotNull(second, "A second reachable Build toilet site should exist.");
        Assert.IsTrue(Send(session, new PlaceBuildServiceCommand(BuildServiceKind.Toilet, second.Value, 2)).IsAccepted);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"])).IsAccepted);
        Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand("equipment.rent")).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        var baseline = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(baseline.IsSuccess, $"second={second}: {baseline.Error}");
        return session;
    }

    [TestMethod]
    public void QueuedGuestForfeitsLongToiletLineButUsingOwnerStaysAndChoicePersists()
    {
        var session = OpenBuildWithTwoToilets();
        var toilets = session.CaptureToilets();
        var longLine = toilets.Single(toilet => toilet.Id == "toilet.main");
        var alternative = toilets.Single(toilet => toilet.Id != longLine.Id);
        var guests = session.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest)
            .Select(person => person.AgentId).ToArray();
        var seeker = guests.First(id => id % QueuedServiceChoice.ReviewStagger ==
            (ulong)(session.CurrentTick % QueuedServiceChoice.ReviewStagger));
        var ahead = guests.Where(id => id != seeker && id % QueuedServiceChoice.ReviewStagger !=
            (ulong)(session.CurrentTick % QueuedServiceChoice.ReviewStagger)).Take(5).ToArray();
        var members = ahead.Append(seeker).ToArray();
        var prep = session.CapturePreparation()!;
        typeof(GameSession).GetProperty("PreparationView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            prep with { People = prep.People.Select(person => members.Contains(person.AgentId)
                ? person with { Admitted = true } : person).ToArray() });
        var immersion = session.CaptureImmersion()!;
        BuildSession.SetToilet(session, session.CaptureToilet()! with { Queue = members, OwnerId = ahead[0], DoorOpen = false,
            ServiceTicks = ToiletRules.PooServiceTicks });
        SetImmersion(session, immersion with
        {
            People = immersion.People.Select(person => members.Contains(person.AgentId) ? person with
            {
                ToiletNeed = 9_000, ToiletId = longLine.Id,
                ToiletStage = person.AgentId == ahead[0] ? ToiletVisitStage.Using : ToiletVisitStage.Queued,
                ToiletChoice = person.AgentId == seeker ? ToiletVisitKind.Wee : ToiletVisitKind.Poo
            } : person).ToArray()
        });
        Position(session, ahead[0], GameSession.ToiletInsideCell(longLine), "toilet.enter");
        for (var index = 1; index < members.Length; index++)
            Position(session, members[index], GameSession.ToiletQueueCell(longLine, index), "toilet.queue");

        Invoke(session, "AdvanceToilet");
        var after = session.CaptureImmersion()!.People.Single(person => person.AgentId == seeker);
        Assert.AreEqual(alternative.Id, after.ToiletId);
        Assert.AreEqual(ToiletVisitStage.Approaching, after.ToiletStage);
        Assert.AreEqual(session.CurrentTick, after.LastToiletChoiceReviewTick);
        Assert.IsFalse(session.CaptureToilets().Single(toilet => toilet.Id == longLine.Id).Queue.Contains(seeker));
        Assert.AreEqual(ahead[0], session.CaptureToilets().Single(toilet => toilet.Id == longLine.Id).OwnerId,
            "A person already using the toilet must keep exclusive ownership.");
        Invoke(session, "AdvanceToilet");
        Assert.AreEqual(alternative.Id, session.CaptureImmersion()!.People.Single(person => person.AgentId == seeker).ToiletId);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(after.LastToiletChoiceReviewTick,
            restored.Session.CaptureImmersion()!.People.Single(person => person.AgentId == seeker).LastToiletChoiceReviewTick);
        session.AdvanceWithoutSnapshot(170);
        restored.Session.AdvanceWithoutSnapshot(170);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash,
            "A switched seeker must continue deterministically through the next choice review after restore.");
        var snapshot = session.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(snapshot with { Immersion = snapshot.Immersion! with
        {
            People = snapshot.Immersion.People.Select(person => person.AgentId == seeker
                ? person with { LastToiletChoiceReviewTick = snapshot.CurrentTick + 1 } : person).ToArray()
        } }).IsSuccess);
    }

    [TestMethod]
    public void QueuedGuestReroutesFromFullToiletButNeverOverridesMedicalRoute()
    {
        static (GameSession Session, ulong Seeker, string Main, string Alternative) Setup()
        {
            var session = OpenBuildWithTwoToilets();
            var toilets = session.CaptureToilets();
            var main = toilets.Single(toilet => toilet.Id == "toilet.main");
            var alternative = toilets.Single(toilet => toilet.Id != main.Id);
            var seeker = session.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Guest &&
                person.AgentId % QueuedServiceChoice.ReviewStagger ==
                (ulong)(session.CurrentTick % QueuedServiceChoice.ReviewStagger)).AgentId;
            var prep = session.CapturePreparation()!;
            typeof(GameSession).GetProperty("PreparationView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
                prep with { People = prep.People.Select(person => person.AgentId == seeker
                    ? person with { Admitted = true } : person).ToArray() });
            var immersion = session.CaptureImmersion()!;
            BuildSession.SetToilet(session, main with { Queue = [seeker], WeeCount = 40 });
            SetImmersion(session, immersion with
            {
                People = immersion.People.Select(person => person.AgentId == seeker ? person with
                { ToiletNeed = 9_000, ToiletId = main.Id, ToiletStage = ToiletVisitStage.Queued,
                  ToiletChoice = ToiletVisitKind.Wee } : person).ToArray()
            });
            Position(session, seeker, GameSession.ToiletQueueCell(main, 0), "toilet.queue");
            return (session, seeker, main.Id, alternative.Id);
        }

        var (rerouting, seeker, mainId, alternativeId) = Setup();
        Invoke(rerouting, "AdvanceToilet");
        Assert.AreEqual(alternativeId, rerouting.CaptureImmersion()!.People.Single(p => p.AgentId == seeker).ToiletId);
        Assert.IsFalse(rerouting.CaptureToilets().Single(t => t.Id == mainId).Queue.Contains(seeker));

        var (interrupted, interruptedId, interruptedMain, _) = Setup();
        Position(interrupted, interruptedId, GameSession.ToiletQueueCell(interrupted.CaptureToilets().Single(t => t.Id == interruptedMain), 0),
            "medical.collapsed");
        Invoke(interrupted, "AdvanceToilet");
        Assert.AreEqual(ToiletVisitStage.None, interrupted.CaptureImmersion()!.People.Single(p => p.AgentId == interruptedId).ToiletStage);
        Assert.IsFalse(interrupted.CaptureToilets().Single(t => t.Id == interruptedMain).Queue.Contains(interruptedId));
        Assert.AreEqual("medical.collapsed", interrupted.CaptureSnapshot().NavigationAgents.Single(n => n.Id.Value == interruptedId).IntentId);
    }

    [TestMethod]
    public void ActuallyDrinkingBeerAddsNeedOnlyDuringConsumptionNotPurchaseOrHolding()
    {
        var beer = OpenWithStock(); var soft = OpenWithStock(); var held = OpenWithStock(); var control = OpenWithStock();
        var id = beer.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        void Sell(GameSession session, ImmersionProduct product) =>
            typeof(GameSession).GetMethod("CompleteImmersionSale", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(session, [id, product]);
        Sell(beer, ImmersionProduct.Beer); Sell(soft, ImmersionProduct.SoftDrink); Sell(held, ImmersionProduct.Beer);
        var baseline = beer.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletNeed;
        Assert.AreEqual(baseline, held.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletNeed,
            "Buying or holding beer alone must not raise toilet need.");
        foreach (var session in new[] { beer, soft, control }) Position(session, id, new GridCell(130, 165), "fixture.drink-away-from-counter");
        var vendor = held.CaptureVendors().Single(v => v.Id == "drinks");
        Position(held, id, GameSession.ImmersionServiceCell(vendor), "fixture.holding-at-counter");
        Assert.IsTrue(beer.ImmersionConsumptionEligible(id));
        Assert.IsFalse(held.ImmersionConsumptionEligible(id));
        foreach (var session in new[] { beer, soft, held, control }) session.AdvanceWithoutSnapshot(80);
        var beerPerson = beer.CaptureImmersion()!.People.Single(p => p.AgentId == id);
        var softPerson = soft.CaptureImmersion()!.People.Single(p => p.AgentId == id);
        var heldPerson = held.CaptureImmersion()!.People.Single(p => p.AgentId == id);
        var controlPerson = control.CaptureImmersion()!.People.Single(p => p.AgentId == id);
        Assert.IsTrue(beerPerson.Held!.ConsumedTicks >= 79);
        Assert.AreEqual(beerPerson.Held.ConsumedTicks, softPerson.Held!.ConsumedTicks);
        Assert.AreEqual(0, heldPerson.Held!.ConsumedTicks);
        Assert.AreEqual(controlPerson.ToiletNeed, heldPerson.ToiletNeed);
        Assert.AreEqual(controlPerson.ToiletNeed, softPerson.ToiletNeed);
        Assert.AreEqual(beerPerson.Held.ConsumedTicks / ToiletRules.BeerConsumptionExtraGainEveryTicks,
            beerPerson.ToiletNeed - softPerson.ToiletNeed);
        // Labelled completion fixture: skip only the remaining sipping duration.
        var completed = OpenWithStock(); Sell(completed, ImmersionProduct.Beer);
        var state = completed.CaptureImmersion()!;
        SetImmersion(completed, state with { People = state.People.Select(p => p.AgentId == id ?
            p with { Held = p.Held! with { ConsumedTicks = GameSession.ImmersionConsumeTicks(ImmersionProduct.Beer) - 1 } } : p).ToArray() });
        Position(completed, id, new GridCell(130, 165), "fixture.finish-drink-away-from-counter");
        Assert.IsTrue(completed.ImmersionConsumptionEligible(id));
        Invoke(completed, "AdvanceImmersion");
        Assert.IsNull(completed.CaptureImmersion()!.People.Single(p => p.AgentId == id).Held);
        Assert.AreEqual(1, completed.CaptureImmersion()!.Purchases.Length);
        var finishedNeed = completed.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletNeed;
        completed.AdvanceWithoutSnapshot(40);
        var laterGain = completed.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletNeed - finishedNeed;
        Assert.IsTrue(laterGain is 9 or 10,
            "Completed beer must not leave a lasting extra need modifier.");
    }

    [TestMethod]
    public void FullTankRejectsNewVisitsAndSmellPenaltyIsLocalAndCapped()
    {
        var session = Open();
        var id = session.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        var other = session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).Skip(1).First().AgentId;
        var toilet = session.CaptureToilet()!;
        var state = session.CaptureImmersion()!;
        BuildSession.SetToilet(session, toilet with { PooCount = 17 });
        SetImmersion(session, state with {
            People = state.People.Select(p => p.AgentId == id ? p with { ToiletNeed = 9_000 } : p).ToArray() });
        Assert.IsTrue(session.CaptureToilet()!.IsFull);
        Assert.AreEqual(100, session.CaptureToilet()!.FullPercent);
        Position(session, id, toilet.Cell, "test.near-smell");
        Position(session, other, new GridCell(toilet.Cell.X + 30, toilet.Cell.Z + 30), "test.far-smell");
        var near = session.ToiletSmellPenaltyPerSecond(id);
        Assert.IsTrue(near is > 0 and <= ToiletRules.SmellMaximumPenaltyPerSecond);
        Assert.AreEqual(0, session.ToiletSmellPenaltyPerSecond(other));
        var before = session.CapturePreparation()!.People.Single(p => p.AgentId == id).Satisfaction;
        Invoke(session, "ApplyToiletSmell");
        Assert.AreEqual(before - near, session.CapturePreparation()!.People.Single(p => p.AgentId == id).Satisfaction);
        state = session.CaptureImmersion()!;
        BuildSession.SetToilet(session, session.CaptureToilet()! with { Queue = [id] });
        SetImmersion(session, state with {
            People = state.People.Select(p => p.AgentId == id ? p with
            { ToiletStage = ToiletVisitStage.Queued, ToiletChoice = ToiletVisitKind.Wee } : p).ToArray() });
        Position(session, id, GameSession.ToiletQueueCell(toilet, 0), "toilet.queue");
        Invoke(session, "AdvanceToilet");
        Assert.IsTrue(session.CaptureImmersion()!.People.All(p => p.ToiletStage == ToiletVisitStage.None));
        Assert.AreEqual(0, session.CaptureToilet()!.Queue.Length);
        Assert.AreEqual(11_900, session.CaptureToilet()!.UsedMillilitres);
    }

    [TestMethod]
    public void MedicalInterruptionReleasesExclusiveOwnerButKeepsDoorOpenUntilInteriorClears()
    {
        var session = Open();
        var id = session.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        var state = session.CaptureImmersion()!;
        var toilet = session.CaptureToilet()!;
        BuildSession.SetToilet(session, toilet with { Queue = [id], OwnerId = id, ServiceTicks = 100, DoorOpen = false });
        SetImmersion(session, state with
        {
            People = state.People.Select(p => p.AgentId == id ? p with
            { ToiletStage = ToiletVisitStage.Using, ToiletChoice = ToiletVisitKind.Wee } : p).ToArray()
        });
        Position(session, id, GameSession.ToiletInsideCell(toilet), "medical.collapsed");
        Invoke(session, "AdvanceToilet");
        var interrupted = session.CaptureToilet()!;
        Assert.IsNull(interrupted.OwnerId);
        Assert.AreEqual(id, interrupted.InterruptedOccupantId);
        Assert.IsTrue(interrupted.DoorOpen);
        Assert.IsFalse(interrupted.OccupiedIndicator, "An interrupted interior blocks admission but shows green while open.");
        Assert.AreEqual(0, interrupted.UsedMillilitres);
        Assert.AreEqual(0, interrupted.Queue.Length);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Position(session, id, GameSession.ToiletExitCell(toilet), "medical.safe-removal");
        Invoke(session, "AdvanceToilet");
        Assert.IsNull(session.CaptureToilet()!.InterruptedOccupantId);
        Assert.IsFalse(session.CaptureToilet()!.DoorOpen);
        Assert.AreEqual(0, session.CaptureToilet()!.UsedMillilitres);
    }

}
