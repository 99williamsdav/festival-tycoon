using Festival.ContentAdapter;
using Festival.Simulation;
using System.Collections;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class ServiceServerTests
{
    private static void Step(ServiceServerPresentation s, ulong? owner = null, ImmersionProduct? order = null,
        double remaining = 0, bool eligible = true, string? transaction = null, long saleTick = 100,
        long tick = 100, double delta = .02, bool frozen = false) =>
        s.Advance(owner, order, remaining, 160, eligible, transaction, saleTick, tick, delta, frozen,
            .375, .541666687, .541666687, .541666687, 1.041666627);

    [TestMethod]
    public void OfferHasOnlyCurrentEligibleProductAndSaleHidesItImmediately()
    {
        var s = new ServiceServerPresentation();
        Step(s, 1, ImmersionProduct.Beer, 150); Assert.AreEqual(ServerPhase.Turn, s.Phase); Assert.IsNull(s.Prop);
        Step(s, 1, ImmersionProduct.Beer, 90); Assert.AreEqual(ServerPhase.Pickup, s.Phase); Assert.IsNull(s.Prop);
        Step(s, 1, ImmersionProduct.Beer, 20); Assert.AreEqual(ServerPhase.Offer, s.Phase); Assert.AreEqual(ImmersionProduct.Beer, s.Prop);
        Step(s, transaction: "immersion:1:1"); Assert.AreEqual(ServerPhase.Retract, s.Phase); Assert.IsNull(s.Prop);
        Step(s, transaction: "immersion:1:1", delta: 2); Assert.AreEqual(ServerPhase.Idle, s.Phase);
        Step(s, transaction: "immersion:1:1"); Assert.AreEqual(ServerPhase.Idle, s.Phase);
    }

    [TestMethod]
    public void FailureCancellationAndPonchoNeverLeaveAnOfferedItem()
    {
        foreach (var order in new[] { ImmersionProduct.Beer, ImmersionProduct.Chips, ImmersionProduct.Poncho })
        {
            var s = new ServiceServerPresentation();
            Step(s, 1, order, 20, eligible: false); Assert.IsNull(s.Prop);
            Step(s, 1, order, 20); if (order == ImmersionProduct.Poncho) Assert.IsNull(s.Prop);
            Step(s, 1, order, 10, eligible: false); Assert.IsNull(s.Prop);
            Step(s); Assert.IsNull(s.Prop);
        }
    }

    [TestMethod]
    public void FrozenPhasesPropsAndTimersResumeWithoutHiddenTimeOrEventReplay()
    {
        foreach (var remaining in new[] { 150, 90, 20 })
        {
            var s = new ServiceServerPresentation(); Step(s, 1, ImmersionProduct.Pizza, remaining);
            var phase = s.Phase; var time = s.Seconds; var prop = s.Prop;
            Step(s, delta: 600, frozen: true, transaction: "immersion:1:1");
            Assert.AreEqual(phase, s.Phase); Assert.AreEqual(time, s.Seconds); Assert.AreEqual(prop, s.Prop);
            Step(s, 1, ImmersionProduct.Pizza, remaining, transaction: "immersion:1:1");
            Assert.AreEqual(phase, s.Phase); Assert.AreEqual(time, s.Seconds);
        }
        var retract = new ServiceServerPresentation(); Step(retract, transaction: "immersion:1:1");
        var before = retract.Seconds; Step(retract, transaction: "immersion:1:1", delta: 600, frozen: true);
        Assert.AreEqual(before, retract.Seconds);
        Step(retract, transaction: "immersion:1:1"); Assert.AreEqual(before + .02, retract.Seconds, 1e-9);
    }

    [TestMethod]
    public void FastForwardAndRebuildRetainOnlyNewestStableTransaction()
    {
        static ImmersionPurchase Sale(int i, ImmersionProduct product = ImmersionProduct.Beer, string? vendor = null) =>
            new($"immersion:1:{i}", 1, product, i, 400, 120, []) { VendorId = vendor };
        var cursor = new ServerPurchaseCursor(); var initial = new[] { Sale(1) }; cursor.Bind(initial);
        cursor.Observe(initial); Assert.IsNull(cursor.Latest(Stalls.FirstBar));
        var batch = initial.Concat(Enumerable.Range(2, 100).Select(i => Sale(i))).Append(Sale(102, ImmersionProduct.Curry, "van.2")).ToArray();
        cursor.Observe(batch); Assert.AreEqual("immersion:1:101", cursor.Latest(Stalls.FirstBar)!.Id);
        Assert.AreEqual("immersion:1:102", cursor.Latest("van.2")!.Id);
        var rebuilt = new ServiceServerPresentation(cursor.Latest(Stalls.FirstBar)!.Id);
        Step(rebuilt, transaction: "immersion:1:101", tick: 101); Assert.AreEqual(ServerPhase.Idle, rebuilt.Phase);
        var skipped = new ServiceServerPresentation();
        Step(skipped, transaction: "immersion:1:101", saleTick: 101, tick: 300); Assert.AreEqual(ServerPhase.Idle, skipped.Phase);
        cursor.Bind(batch); cursor.Observe(batch.ToArray()); Assert.IsNull(cursor.Latest(Stalls.FirstBar));
    }

    [TestMethod]
    public void SparseResetIsFrozenAndImmediatelyInterruptibleByNextService()
    {
        var s = new ServiceServerPresentation();
        for (var i = 1; i <= 4; i++)
        {
            Step(s, transaction: $"immersion:1:{i}");
            Step(s, transaction: $"immersion:1:{i}", delta: .6);
        }
        Assert.AreEqual(ServerPhase.Reset, s.Phase);
        var time = s.Seconds; Step(s, transaction: "immersion:1:4", delta: 600, frozen: true);
        Assert.AreEqual(time, s.Seconds);
        Step(s, 2, ImmersionProduct.SoftDrink, 150, transaction: "immersion:1:4");
        Assert.AreEqual(ServerPhase.Turn, s.Phase); Assert.IsNull(s.Prop);
    }

    [TestMethod]
    [DataRow("out-of-stock")]
    [DataRow("insufficient-wallet")]
    [DataRow("beer-refusal")]
    public void FailedAuthoritativeOrderNeverOffersChargesOrCreatesReceipt(string failure)
    {
        var session = BuildSession.Planned(20260926);
        foreach (var hire in BuildSession.Crew(session)) BuildSession.Accept(session, hire);
        BuildSession.Accept(session, new StartPreparedEditionCommand());
        var preparation = session.CapturePreparation()!;
        typeof(GameSession).GetProperty("PreparationView", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(session,
            preparation with { People = preparation.People.Select(p => p with { Admitted = true }).ToArray() });
        var state = session.CaptureImmersion()!;
        var buyer = state.People.First(p => !p.Abstains && session.CapturePerson(p.AgentId)!.Role == ProtectedPersonRole.Guest);
        var vendor = session.CaptureVendors().Single(v => v.Id == Stalls.FirstBar);
        BuildSession.SetVendor(session, vendor with { Queue = [buyer.AgentId], OwnerId = buyer.AgentId, ServiceTicks = 1 });
        var nav = ((IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!)[new EntityId(buyer.AgentId)]!;
        var cell = GameSession.ImmersionServiceCell(vendor); var centre = TraversalGrid.CellCentre(cell);
        foreach (var (key, value) in new (string, object)[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
            ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres),
            ("Route", new List<GridCell>()), ("RouteIndex", 0), ("SegmentProgressMicrometres", 0),
            ("Action", AgentNavigationAction.Arrived), ("Destination", cell), ("IntentId", "immersion.queue") }) nav.GetType().GetProperty(key)!.SetValue(nav, value);
        typeof(GameSession).GetProperty("ImmersionView", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(session, state with
        {
            BeerStock = failure == "out-of-stock" ? 0 : state.BeerStock,
            People = state.People.Select(p => p.AgentId == buyer.AgentId ? p with { VendorId = vendor.Id, Order = ImmersionProduct.Beer,
                Intoxication = failure == "beer-refusal" ? 7500 : 0 } : p with { LastDecisionTick = session.CurrentTick }).ToArray()
        });
        if (failure == "insufficient-wallet")
        {
            var wallet = ((IDictionary)typeof(GameSession).GetField("_wallets", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!)[new EntityId(buyer.AgentId)]!;
            wallet.GetType().GetProperty("CashPennies")!.SetValue(wallet, 0L);
        }
        var cash = session.CaptureSnapshot().Wallets.Single(w => w.OwnerId.Value == buyer.AgentId).CashPennies;
        var stock = session.CaptureImmersion()!.BeerStock;
        Assert.IsFalse(session.ImmersionOrderCanComplete(buyer.AgentId, ImmersionProduct.Beer));
        var visual = new ServiceServerPresentation(); Step(visual, buyer.AgentId, ImmersionProduct.Beer, 1,
            eligible: session.ImmersionOrderCanComplete(buyer.AgentId, ImmersionProduct.Beer)); Assert.IsNull(visual.Prop);
        typeof(GameSession).GetMethod("AdvanceImmersion", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(session, null);
        Assert.AreEqual(0, session.CaptureImmersion()!.Purchases.Length); Assert.AreEqual(stock, session.CaptureImmersion()!.BeerStock);
        Assert.AreEqual(cash, session.CaptureSnapshot().Wallets.Single(w => w.OwnerId.Value == buyer.AgentId).CashPennies);
        Assert.IsNull(session.CaptureImmersion()!.People.Single(p => p.AgentId == buyer.AgentId).Held);
    }

    [TestMethod]
    public void PhysicalDayPresentationAndReloadDoNotChangeAuthoritativeEconomyOrQueues()
    {
        var session = BuildSession.Planned(20260926);
        foreach (var hire in BuildSession.Crew(session)) BuildSession.Accept(session, hire);
        BuildSession.Accept(session, new StartPreparedEditionCommand());
        var cursor = new ServerPurchaseCursor(); cursor.Bind(session.CaptureImmersion()!.Purchases);
        var views = session.CaptureVendors().ToDictionary(v => v.Id, _ => new ServiceServerPresentation());
        var sales = 0;
        for (var frame = 0; frame < 600; frame++)
        {
            session.AdvanceWithoutSnapshot(16);
            var hash = session.CaptureSnapshot().AuthoritativeHash;
            var state = session.CaptureImmersion()!; cursor.Observe(state.Purchases);
            foreach (var v in session.CaptureVendors())
            {
                var order = v.OwnerId is { } id ? session.CapturePerson(id)?.Order : null;
                var eligible = v.OwnerId is { } owner && order is { } product && session.ImmersionOrderCanComplete(owner, product);
                var sale = cursor.Latest(v.Id);
                views[v.Id].Advance(v.OwnerId, order, v.ServiceTicks, order is { } o ? GameSession.ImmersionServiceDuration(o) : 0,
                    eligible, sale?.Id, sale?.Tick ?? -1, session.CurrentTick, .2, false, .375, .542, .542, .542, 1.042);
            }
            Assert.AreEqual(hash, session.CaptureSnapshot().AuthoritativeHash);
            sales = state.Purchases.Length;
            if (frame == 400)
            {
                var restored = GameSession.Restore(session.CapturePersistenceSnapshot()); Assert.IsTrue(restored.IsSuccess, restored.Error);
                session = restored.Session!; cursor.Bind(session.CaptureImmersion()!.Purchases);
                views = session.CaptureVendors().ToDictionary(v => v.Id, _ => new ServiceServerPresentation());
                Assert.AreEqual(hash, session.CaptureSnapshot().AuthoritativeHash);
            }
        }
        Assert.IsGreaterThan(0, sales);
    }
}
