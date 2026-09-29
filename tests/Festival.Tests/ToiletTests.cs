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
        typeof(GameSession).GetField("_immersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, snapshot);
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
        var session = GameSession.CreateImmersionCampaign(20260928);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"])).IsAccepted);
        Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        var preparation = session.CapturePreparation()!;
        typeof(GameSession).GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            preparation with { People = preparation.People.Select(p => p with { Admitted = true }).ToArray() });
        return session;
    }

    private static GameSession OpenWithStock()
    {
        var session = GameSession.CreateImmersionCampaign(20260928);
        Assert.IsTrue(Send(session, new PurchaseImmersionStarterStockCommand()).IsAccepted);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"])).IsAccepted);
        Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        var preparation = session.CapturePreparation()!;
        typeof(GameSession).GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
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
        typeof(GameSession).GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            prep with { People = prep.People.Select(person => members.Contains(person.AgentId)
                ? person with { Admitted = true } : person).ToArray() });
        var immersion = session.CaptureImmersion()!;
        SetImmersion(session, immersion with
        {
            Toilet = immersion.Toilet! with { Queue = members, OwnerId = ahead[0], DoorOpen = false,
                ServiceTicks = ToiletRules.PooServiceTicks },
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
            typeof(GameSession).GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
                prep with { People = prep.People.Select(person => person.AgentId == seeker
                    ? person with { Admitted = true } : person).ToArray() });
            var immersion = session.CaptureImmersion()!;
            SetImmersion(session, immersion with
            {
                Toilet = main with { Queue = [seeker], WeeCount = 40 },
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
    public void OwnedToiletMoveIsAtomicAndCurrentVersionRoundTrips()
    {
        var session = GameSession.CreateImmersionCampaign(20260928);
        var original = session.CaptureToilet()!;
        var reserved = GameSession.ToiletReservedCells(original).ToHashSet();
        for (var slot = 0; slot < ToiletRules.MaximumQueue; slot++)
            Assert.IsTrue(reserved.Contains(GameSession.ToiletQueueCell(original, slot)),
                $"Physical queue slot {slot} must be protected by placement checks.");
        var hash = session.CaptureSnapshot().AuthoritativeHash;
        var cash = session.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        var rejected = Send(session, new MoveToiletCommand(new GridCell(95, 145)));
        Assert.IsFalse(rejected.IsAccepted);
        Assert.AreEqual(hash, session.CaptureSnapshot().AuthoritativeHash);
        var choice = Enumerable.Range(-9, 19).SelectMany(dx => Enumerable.Range(-9, 19)
            .Select(dz => new GridCell(original.Cell.X + dx, original.Cell.Z + dz)))
            .First(cell => cell != original.Cell && session.ValidateCommand(new(
                new(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase, session.CurrentTick,
                session.NextSubmissionSequence, null, new MoveToiletCommand(cell, 0))) is null);
        Assert.IsTrue(Send(session, new MoveToiletCommand(choice)).IsAccepted);
        Assert.AreEqual(choice, session.CaptureToilet()!.Cell);
        Assert.AreEqual(cash, session.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        Assert.IsFalse(GameSession.Restore(session.CapturePersistenceSnapshot() with
        { Immersion = session.CaptureImmersion()! with { Version = 1 } }).IsSuccess);
    }

    [TestMethod]
    public void PhysicalVisitOpensClosesThenExitsAndCountsWasteOnlyOnce()
    {
        var session = Open();
        var id = session.CapturePreparation()!.People.First(p =>
            p.Role == ProtectedPersonRole.Guest && p.AgentId % 4 == 0).AgentId;
        var delay = (int)(id % ToiletRules.DecisionEveryTicks);
        session.AdvanceWithoutSnapshot(delay);
        var state = session.CaptureImmersion()!;
        SetImmersion(session, state with { People = state.People.Select(p => p.AgentId == id ? p with { ToiletNeed = 9_000 } : p).ToArray() });
        var toilet = session.CaptureToilet()!;
        Position(session, id, GameSession.ToiletQueueCell(toilet, 0), "toilet.approach");
        Invoke(session, "AdvanceToilet");
        var approaching = session.CaptureImmersion()!.People.Single(p => p.AgentId == id);
        Assert.AreEqual(ToiletVisitStage.Approaching, approaching.ToiletStage);
        Position(session, id, GameSession.ToiletQueueCell(toilet, 0), "toilet.approach");
        Invoke(session, "AdvanceToilet");
        Assert.AreEqual(id, session.CaptureToilet()!.OwnerId);
        Assert.IsTrue(session.CaptureToilet()!.DoorOpen);
        Position(session, id, GameSession.ToiletInsideCell(toilet), "toilet.enter");
        Invoke(session, "AdvanceToilet");
        Assert.AreEqual(ToiletVisitStage.Using, session.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletStage);
        Assert.IsFalse(session.CaptureToilet()!.DoorOpen);
        Assert.AreEqual(ToiletVisitKind.Poo, approaching.ToiletChoice);
        var current = session.CaptureImmersion()!;
        SetImmersion(session, current with { Toilet = current.Toilet! with { ServiceTicks = 1 } });
        Invoke(session, "AdvanceToilet");
        var completed = session.CaptureToilet()!;
        Assert.AreEqual(1, completed.PooCount);
        Assert.AreEqual(0, completed.WeeCount);
        Assert.AreEqual(ToiletRules.PooMillilitres, completed.UsedMillilitres);
        Assert.AreEqual(1_000, session.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletNeed);
        Assert.IsTrue(completed.DoorOpen);
        Invoke(session, "AdvanceToilet");
        Assert.AreEqual(completed.UsedMillilitres, session.CaptureToilet()!.UsedMillilitres);
        Position(session, id, GameSession.ToiletExitCell(toilet), "toilet.exit");
        Invoke(session, "AdvanceToilet");
        Assert.IsNull(session.CaptureToilet()!.OwnerId);
        Assert.IsFalse(session.CaptureToilet()!.DoorOpen);
        Assert.AreEqual(ToiletVisitStage.None, session.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletStage);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    [TestMethod]
    public void NeedFallsOnlyDuringUseAndPartialReliefSurvivesSaveAndInterruption()
    {
        var session = Open();
        var id = session.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        var toilet = session.CaptureToilet()!;
        var state = session.CaptureImmersion()!;
        SetImmersion(session, state with { Toilet = toilet with { Queue = [id] },
            People = state.People.Select(p => p.AgentId == id ? p with
            { ToiletNeed = 9_000, ToiletStage = ToiletVisitStage.Queued, ToiletChoice = ToiletVisitKind.Wee } : p).ToArray() });
        Position(session, id, GameSession.ToiletQueueCell(toilet, 0), "toilet.queue");
        Invoke(session, "AdvanceToilet");
        Assert.IsTrue(session.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletNeed >= 9_000);
        state = session.CaptureImmersion()!;
        SetImmersion(session, state with { Toilet = state.Toilet! with { OwnerId = id, Queue = [id], DoorOpen = false, ServiceTicks = 100 },
            People = state.People.Select(p => p.AgentId == id ? p with { ToiletNeed = 9_000,
                ToiletStage = ToiletVisitStage.Using, ToiletChoice = ToiletVisitKind.Wee } : p).ToArray() });
        Position(session, id, GameSession.ToiletInsideCell(toilet), "toilet.enter");
        session.AdvanceWithoutSnapshot(20);
        var partial = session.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletNeed;
        Assert.IsTrue(partial is > 1_000 and < 9_000, $"Partial relief should be visible mid-use: {partial}.");
        Assert.AreEqual(0, session.CaptureToilet()!.UsedMillilitres);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        session.AdvanceWithoutSnapshot(20); restored.Session!.AdvanceWithoutSnapshot(20);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash);
        var beforeInterruption = session.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletNeed;
        Position(session, id, GameSession.ToiletInsideCell(toilet), "medical.collapsed");
        Invoke(session, "AdvanceToilet");
        Assert.AreEqual(beforeInterruption, session.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletNeed);
        Assert.AreEqual(0, session.CaptureToilet()!.UsedMillilitres);
        Assert.AreEqual(0, session.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletVisits);
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
        var vendor = held.CaptureImmersion()!.Vendors.Single(v => v.Id == "drinks");
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
        SetImmersion(session, state with { Toilet = toilet with { PooCount = 17 },
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
        SetImmersion(session, state with { Toilet = state.Toilet! with { Queue = [id] },
            People = state.People.Select(p => p.AgentId == id ? p with
            { ToiletStage = ToiletVisitStage.Queued, ToiletChoice = ToiletVisitKind.Wee } : p).ToArray() });
        Position(session, id, GameSession.ToiletQueueCell(toilet, 0), "toilet.queue");
        Invoke(session, "AdvanceToilet");
        Assert.IsTrue(session.CaptureImmersion()!.People.All(p => p.ToiletStage == ToiletVisitStage.None));
        Assert.AreEqual(0, session.CaptureToilet()!.Queue.Length);
        Assert.AreEqual(11_900, session.CaptureToilet()!.UsedMillilitres);
    }

    [TestMethod]
    public void TwoGuestsPhysicallyQueueAndCompleteWithoutOverlappingOwnership()
    {
        var session = Open();
        var ids = session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest)
            .Skip(3).Take(2).Select(p => p.AgentId).ToArray();
        var toilet = session.CaptureToilet()!;
        var state = session.CaptureImmersion()!;
        SetImmersion(session, state with { People = state.People.Select(p => ids.Contains(p.AgentId) ?
            p with { ToiletNeed = 9_000 } : p).ToArray() });
        var medical = session.CaptureMedical()!;
        typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            medical with { Needs = medical.Needs.Select(n => ids.Contains(n.AgentId) ? n with { Thirst = 0 } : n).ToArray() });
        Position(session, ids[0], GameSession.ToiletQueueCell(toilet, 0), "fixture.near-toilet");
        Position(session, ids[1], GameSession.ToiletQueueCell(toilet, 1), "fixture.near-toilet");
        var sawQueue = false;
        var sawClosedOccupied = false;
        for (var tick = 0; tick < 2_400; tick++)
        {
            session.AdvanceWithoutSnapshot(1);
            var current = session.CaptureToilet()!;
            if (current.Queue.Length >= 2) sawQueue = true;
            if (current.OwnerId is not null && !current.DoorOpen) sawClosedOccupied = true;
            if (current.WeeCount + current.PooCount == 2 && current.OwnerId is null) break;
        }
        var finished = session.CaptureToilet()!;
        Assert.IsTrue(sawQueue, "Both visitors must enter the physical queue.");
        Assert.IsTrue(sawClosedOccupied, $"Door must close during exclusive service. owner={finished.OwnerId} queue={string.Join(',',finished.Queue)} " +
            $"stages={string.Join(',', session.CaptureImmersion()!.People.Where(p => ids.Contains(p.AgentId)).Select(p => p.ToiletStage))} " +
            $"nav={string.Join(',', session.CaptureSnapshot().NavigationAgents.Where(n => ids.Contains(n.Id.Value)).Select(n => $"{n.Id}:{n.Action}:{n.IntentId}:{n.Destination}"))}");
        Assert.AreEqual(2, finished.WeeCount + finished.PooCount);
        Assert.IsNull(finished.OwnerId);
        Assert.AreEqual(0, finished.Queue.Length);
        Assert.IsTrue(ids.All(id => session.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletVisits == 1));
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void DepartureOpensOccupiedDoorAndReleasesVisitorWithoutCountingIncompleteUse()
    {
        var session = Open();
        var id = session.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        var state = session.CaptureImmersion()!;
        var toilet = state.Toilet!;
        SetImmersion(session, state with
        {
            Toilet = toilet with { Queue = [id], OwnerId = id, ServiceTicks = 100, DoorOpen = false },
            People = state.People.Select(p => p.AgentId == id ? p with
            { ToiletStage = ToiletVisitStage.Using, ToiletChoice = ToiletVisitKind.Wee } : p).ToArray()
        });
        Position(session, id, GameSession.ToiletInsideCell(toilet), "toilet.enter");
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        session.AdvanceWithoutSnapshot(5);
        restored.Session!.AdvanceWithoutSnapshot(5);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash);
        session = restored.Session!;
        var preparation = session.CapturePreparation()!;
        typeof(GameSession).GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            preparation with { Status = PreparationStatus.Departing });
        Invoke(session, "AdvanceToilet");
        Assert.IsTrue(session.CaptureToilet()!.DoorOpen);
        Assert.AreEqual(ToiletVisitStage.Leaving, session.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletStage);
        Position(session, id, GameSession.ToiletExitCell(toilet), "toilet.exit");
        Invoke(session, "AdvanceToilet");
        Assert.IsNull(session.CaptureToilet()!.OwnerId);
        Assert.AreEqual(0, session.CaptureToilet()!.UsedMillilitres);
        Assert.AreEqual(ToiletVisitStage.None, session.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletStage);
        Invoke(session, "AdvanceImmersionDepartureRoutes");
        Assert.AreEqual("edition.departure", session.CaptureSnapshot().NavigationAgents.Single(n => n.Id.Value == id).IntentId);
    }

    [TestMethod]
    public void MedicalInterruptionReleasesExclusiveOwnerButKeepsDoorOpenUntilInteriorClears()
    {
        var session = Open();
        var id = session.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        var state = session.CaptureImmersion()!;
        var toilet = state.Toilet!;
        SetImmersion(session, state with
        {
            Toilet = toilet with { Queue = [id], OwnerId = id, ServiceTicks = 100, DoorOpen = false },
            People = state.People.Select(p => p.AgentId == id ? p with
            { ToiletStage = ToiletVisitStage.Using, ToiletChoice = ToiletVisitKind.Wee } : p).ToArray()
        });
        Position(session, id, GameSession.ToiletInsideCell(toilet), "medical.collapsed");
        Invoke(session, "AdvanceToilet");
        var interrupted = session.CaptureToilet()!;
        Assert.IsNull(interrupted.OwnerId);
        Assert.AreEqual(id, interrupted.InterruptedOccupantId);
        Assert.IsTrue(interrupted.DoorOpen);
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

    [TestMethod]
    public void CouncilRetryKeepsApprovedSiteButResetsTankAndVisits()
    {
        var session = GameSession.CreateImmersionCampaign(20260928);
        var original = session.CaptureToilet()!;
        var moved = Enumerable.Range(-9, 19).SelectMany(dx => Enumerable.Range(-9, 19)
            .Select(dz => new GridCell(original.Cell.X + dx, original.Cell.Z + dz)))
            .First(cell => cell != original.Cell && session.ValidateCommand(new(
                new(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase, session.CurrentTick,
                session.NextSubmissionSequence, null, new MoveToiletCommand(cell, 0))) is null);
        Assert.IsTrue(Send(session, new MoveToiletCommand(moved)).IsAccepted);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"])).IsAccepted);
        Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        var prep = session.CapturePreparation()!;
        typeof(GameSession).GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            prep with { People = prep.People.Select(p => p with { Admitted = true }).ToArray() });
        var state = session.CaptureImmersion()!;
        var doomed = state.People.First().AgentId;
        SetImmersion(session, state with
        {
            Toilet = state.Toilet! with { WeeCount = 7 },
            People = state.People.Select(p => p.AgentId == doomed ? p with { Intoxication = 10_000 } : p).ToArray()
        });
        session.AdvanceWithoutSnapshot(4_000);
        Assert.AreEqual(PreparationStatus.Failed, session.PreparedStatus);
        Assert.IsTrue(Send(session, new SpendCouncilFavourCommand()).IsAccepted);
        var retry = session.CaptureToilet()!;
        Assert.AreEqual(moved, retry.Cell);
        Assert.AreEqual(0, retry.QuarterTurns);
        Assert.AreEqual(0, retry.WeeCount);
        Assert.AreEqual(0, retry.PooCount);
        Assert.AreEqual(0, retry.UsedMillilitres);
        Assert.AreEqual(0, retry.FullPercent);
        Assert.AreEqual(0, retry.Queue.Length);
        Assert.IsNull(retry.OwnerId);
        Assert.IsNull(retry.InterruptedOccupantId);
        Assert.IsTrue(session.CaptureImmersion()!.People.All(p => p.ToiletVisits == 0 && p.ToiletStage == ToiletVisitStage.None));
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }
}
