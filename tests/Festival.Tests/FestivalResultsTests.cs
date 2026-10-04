using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class FestivalResultsTests
{
    private static void Set(GameSession s, string field, object value) => SetMember(typeof(GameSession), field, BindingFlags.NonPublic | BindingFlags.Instance, s, value);
    private static void Invoke(GameSession s, string name, params object[] args) => typeof(GameSession).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(s, args);
    private static void Position(GameSession s, ulong id, GridCell cell)
    {
        var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
        var nav = agents[new EntityId(id)]!; var center = TraversalGrid.CellCentre(cell);
        void Property(string name, object value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
        Property("XMillimetres", center.XMillimetres); Property("ZMillimetres", center.ZMillimetres);
        Property("SegmentOriginXMillimetres", center.XMillimetres); Property("SegmentOriginZMillimetres", center.ZMillimetres);
        Property("Route", new List<GridCell>()); Property("RouteIndex", 0); Property("SegmentProgressMicrometres", 0);
        Property("Action", AgentNavigationAction.Arrived); Property("Destination", cell); Property("IntentId", "edition.departure");
    }
    private static void Accept(GameSession s, SessionCommand c)
    {
        var r = s.Execute(new(new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, c)); Assert.IsTrue(r.IsAccepted, r.Message);
    }
    private static GameSession Open()
    {
        var s = BuildSession.Drafted(20260922);
        Accept(s, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"]));
        foreach (var id in new[] { "staff.sound.3", "staff.medic.1", "staff.steward.1" }) Accept(s, new AcceptPreparationOfferCommand(id)); Accept(s, new AcceptPreparationOfferCommand("equipment.rent"));
        Accept(s, new SetPreparationStockCommand(40, 40, 32)); Accept(s, new StartPreparedEditionCommand());
        s.AdvanceWithoutSnapshot(1200);
        // Band members walk in to backstage, the far side of the field from the gate.
        while (s.CapturePreparation()!.People.Any(person => person.Role == ProtectedPersonRole.Performer && !person.Admitted) && s.CurrentTick < 4_000)
            s.AdvanceWithoutSnapshot(1);
        return s;
    }
    private static GameSession Reload(GameSession s)
    {
        var result = GameSession.Restore(s.CapturePersistenceSnapshot()); Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash); return result.Session;
    }
    private static void Closing(GameSession s)
    {
        s.AdvanceWithoutSnapshot((int)(s.PreparedEditionDurationTicks - s.CurrentTick));
        Assert.AreEqual(PreparationStatus.Departing, s.PreparedStatus);
    }
    [TestMethod]
    [DataRow(0L, 1)] [DataRow(1999L, 1)] [DataRow(2000L, 2)] [DataRow(3999L, 2)] [DataRow(4000L, 3)]
    [DataRow(5999L, 3)] [DataRow(6000L, 4)] [DataRow(7999L, 4)] [DataRow(8000L, 5)] [DataRow(10000L, 5)]
    public void ExactStarBoundaries(long satisfaction, int stars) => Assert.AreEqual(stars, FestivalResult.Rating(satisfaction * 7, 7));
    [TestMethod]
    public void NoGuestsIsUnratedAndRoundingCannotChangeBoundary()
    {
        Assert.IsNull(FestivalResult.Rating(0, 0)); Assert.AreEqual(1, FestivalResult.Rating(3999, 2));
    }
    [TestCategory("Slow")]
    [TestMethod]
    public void NaturalDepartureFrozenReportAndActualFileReload()
    {
        var s = Open(); Closing(s); var prior = s.CapturePreparation()!;
        s = Reload(s); s.AdvanceWithoutSnapshot(15000);
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus); Assert.IsNotNull(s.CompletedFestivalResult);
        Assert.IsNotNull(s.CompletedFestivalAccounts); Assert.IsTrue(s.CompletedFestivalAccounts.Reconciles);
        Assert.AreEqual(20, s.CompletedFestivalResult.GuestCount); Assert.AreEqual(prior.People.Length, s.CapturePreparation()!.People.Length);
        // The completed festival moves the established festival's standing toward its result.
        var finished = s.CapturePreparation()!;
        Assert.IsNotNull(finished.StandingBefore);
        var expected = ActCatalogue.AfterFestival(finished.StandingBefore, s.CompletedFestivalResult.Stars!.Value,
            new[] { "act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency" }.Select(id => ActCatalogue.Find(id)!.Genre));
        Assert.AreEqual(expected.Reputation, s.Standing.Reputation);
        CollectionAssert.AreEqual(expected.SceneCredibility, s.Standing.SceneCredibility);
        var directory = Path.Combine(Path.GetTempPath(), "festival-results-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var compatibility = new SaveCompatibility("results", "content", "results");
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "finished", new(s, compatibility, "test", DateTimeOffset.UtcNow)).IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(directory, "finished", compatibility); Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            var hash = loaded.Session!.CaptureSnapshot().AuthoritativeHash; loaded.Session.AdvanceWithoutSnapshot(500);
            Assert.AreEqual(hash, loaded.Session.CaptureSnapshot().AuthoritativeHash); Assert.AreEqual(s.CompletedFestivalResult, loaded.Session.CompletedFestivalResult);
            Assert.AreEqual(s.Standing.Reputation, loaded.Session.Standing.Reputation);
            Assert.IsTrue(loaded.Session.CompletedFestivalAccounts!.Reconciles);
            Assert.AreEqual(s.CompletedFestivalAccounts!.ClosingCashPennies, loaded.Session.CompletedFestivalAccounts.ClosingCashPennies);
            Assert.IsTrue(s.CompletedFestivalAccounts.Sales.SequenceEqual(loaded.Session.CompletedFestivalAccounts.Sales));
        }
        finally { Directory.Delete(directory, true); }
    }
    [TestMethod]
    public void BeerPartialGuestCompletionAndPerformerExclusionAreExact()
    {
        var s = Open(); var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !s.CaptureImmersion()!.People.Single(i => i.AgentId == p.AgentId).Abstains);
        Invoke(s, "CompleteImmersionSale", guest.AgentId, ImmersionProduct.Beer);
        Invoke(s, "LeaveWater", guest.AgentId, "Labelled consumption fixture", false, true);
        Invoke(s, "UpdatePerson", guest.AgentId, (Func<Person,Person>)(n => n with { Intent = MedicalIntent.WatchShow }));
        Position(s, guest.AgentId, new(120, 125));
        var immersion = s.CaptureImmersion()!;
        Set(s, "ImmersionView", immersion with { People = immersion.People.Select(p => p.AgentId == guest.AgentId ? p with { Held = p.Held! with { ConsumedTicks = 2399 } } : p).ToArray() });
        // This completion fixture also advances the authoritative clock to the earliest possible finish.
        typeof(GameSession).GetProperty("CurrentTick")!.SetValue(s, s.CurrentTick + GameSession.ImmersionConsumeTicks(ImmersionProduct.Beer));
        Assert.AreEqual(0, s.CapturePreparation()!.FinishedBeerIds!.Length); Invoke(s, "AdvanceImmersion");
        Assert.AreEqual(1, s.CapturePreparation()!.FinishedBeerIds!.Length); Invoke(s, "AdvanceImmersion"); Assert.AreEqual(1, s.CapturePreparation()!.FinishedBeerIds!.Length);
        Reload(s);
        var performer = s.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Performer && !s.IsCurrentProgrammePerformer(person.AgentId) && !s.CaptureImmersion()!.People.Single(item => item.AgentId == person.AgentId).Abstains);
        Invoke(s, "CompleteImmersionSale", performer.AgentId, ImmersionProduct.Beer);
        Invoke(s, "LeaveWater", performer.AgentId, "Labelled performer consumption fixture", false, true);
        Invoke(s, "UpdatePerson", performer.AgentId, (Func<Person,Person>)(n => n with { Intent = MedicalIntent.WatchShow })); Position(s, performer.AgentId, new(124, 125));
        immersion = s.CaptureImmersion()!;
        Set(s, "ImmersionView", immersion with { People = immersion.People.Select(person => person.AgentId == performer.AgentId ? person with { Held = person.Held! with { ConsumedTicks = 2399 } } : person).ToArray() });
        typeof(GameSession).GetProperty("CurrentTick")!.SetValue(s, s.CurrentTick + GameSession.ImmersionConsumeTicks(ImmersionProduct.Beer));
        Invoke(s, "AdvanceImmersion"); Assert.IsNull(s.CaptureImmersion()!.People.Single(person => person.AgentId == performer.AgentId).Held);
        Assert.AreEqual(1, s.CapturePreparation()!.FinishedBeerIds!.Length); Reload(s);
    }
    [TestCategory("Slow")]
    [TestMethod]
    public void LedgerOperatingProfitExcludesOpeningFundsAndCapital()
    {
        var s = Open(); Closing(s); s.AdvanceWithoutSnapshot(15000); var r = s.CompletedFestivalResult!;
        Assert.AreEqual(12000L, r.CapitalPurchasesPennies);
        Assert.AreEqual(r.RevenuePennies - r.ContractCostsPennies - r.ConsumedStockCostsPennies, r.ProfitPennies);
        // Opening funds are the £600 loan; the £200 of advance tickets is this festival's income.
        Assert.AreEqual(s.CaptureSnapshot().FestivalFinances.Single().CashPennies - 60000L, r.NetCashChangePennies);
        Assert.IsTrue(r.ContractCostsPennies > 0); Reload(s);
    }
    [TestCategory("Slow")]
    [TestMethod]
    public void FinalSettlementSaveFailureDoesNotPublishAndOrdinaryDepartureDoesNotAutosave()
    {
        var s = Open(); Closing(s); Assert.IsFalse(s.PreparationBoundaryOnNextTick);
        while (s.CapturePreparation()!.People.Any(person => !person.Departed)) s.AdvanceWithoutSnapshot(1);
        Assert.IsNull(s.CompletedFestivalResult); Assert.IsTrue(s.PreparationBoundaryOnNextTick); s = Reload(s);
        var directory = Path.Combine(Path.GetTempPath(), "festival-terminal-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var compatibility = new SaveCompatibility("results", "content", "results"); var hash = s.CaptureSnapshot().AuthoritativeHash;
            var failed = PreparationAdvanceCoordinator.AdvanceOne(directory, s, compatibility, DateTimeOffset.UtcNow, 1, _ => throw new IOException("Labelled terminal save failure"));
            Assert.IsFalse(failed.IsSuccess); Assert.AreSame(s, failed.Session); Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
            var succeeded = PreparationAdvanceCoordinator.AdvanceOne(directory, s, compatibility, DateTimeOffset.UtcNow, 2);
            Assert.IsTrue(succeeded.IsSuccess, succeeded.Error); Assert.IsNotNull(succeeded.Session.CompletedFestivalResult); Assert.IsNotNull(succeeded.Autosave);
        }
        finally { Directory.Delete(directory, true); }
    }
    [TestCategory("Slow")]
    [TestMethod]
    public void EarlyUnhappyGuestRetainedInFinalMeanAndHistory()
    {
        var s = Open(); var p = s.CapturePreparation()!; var guest = p.People.First(person => person.Role == ProtectedPersonRole.Guest && person.Admitted);
        Set(s, "PreparationView", p with { People = p.People.Select(person => person.AgentId == guest.AgentId ? person with { Satisfaction = 100 } : person).ToArray() });
        Invoke(s, "UpdatePerson", guest.AgentId, (Func<Person,Person>)(person => person with { ConductStage = DisorderStage.Complaint, Pressure = 4000, Grievance = DisorderGrievance.MusicCutoff, GrievanceTick = s.CurrentTick, ConductStageTick = s.CurrentTick }));
        Accept(s, new DisorderCommand(DisorderAction.SafeEgress, guest.AgentId)); s.AdvanceWithoutSnapshot(5000);
        Assert.IsTrue(s.CapturePreparation()!.People.Single(person => person.AgentId == guest.AgentId).Departed);
        var finalEarly = s.CapturePreparation()!.People.Single(person => person.AgentId == guest.AgentId).Satisfaction;
        Closing(s); s.AdvanceWithoutSnapshot(15000); var final = s.CapturePreparation()!;
        Assert.AreEqual(finalEarly, final.People.Single(person => person.AgentId == guest.AgentId).Satisfaction);
        Assert.AreEqual(final.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.Admitted && person.Departed).Sum(person => (long)person.Satisfaction), s.CompletedFestivalResult!.SatisfactionTotal);
        Assert.AreEqual(20, s.CompletedFestivalResult.GuestCount); Reload(s);
    }
    [TestCategory("Slow")]
    [TestMethod]
    public void ActualMedicalCollapseCountsOnceAndFightKnockoutDoesNotCount()
    {
        var s = Open(); Closing(s); var m = s.CaptureMedical()!; var id = BuildSession.LastGuest(s);
        var expected = s.CapturePreparation()!.GuestMedicalCollapses + 1;
        Invoke(s, "UpdatePerson", id, (Func<Person,Person>)(need => need with { HealthStage = MedicalStage.Distress, HealthWarningTick = s.CurrentTick - GameSession.MedicalCollapseDelayTicks }));
        Invoke(s, "AdvanceImmersionDepartureMedicine"); Assert.AreEqual(expected, s.CapturePreparation()!.GuestMedicalCollapses);
        Invoke(s, "AdvanceImmersionDepartureMedicine"); Assert.AreEqual(expected, s.CapturePreparation()!.GuestMedicalCollapses);
        var nonGuest = s.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Performer);
        Invoke(s, "UpdatePerson", nonGuest.AgentId, (Func<Person,Person>)(need => need with { HealthStage = MedicalStage.Distress, HealthWarningTick = s.CurrentTick - GameSession.MedicalCollapseDelayTicks }));
        Invoke(s, "AdvanceImmersionDepartureMedicine"); Assert.AreEqual(expected, s.CapturePreparation()!.GuestMedicalCollapses);
        var otherGuest = s.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Guest && person.AgentId != id);
        Invoke(s, "UpdatePerson", otherGuest.AgentId, (Func<Person,Person>)(person => person with { ConductStage = DisorderStage.Injured, InjuryTick = s.CurrentTick }));
        Invoke(s, "AdvanceImmersionDepartureMedicine"); Assert.AreEqual(expected, s.CapturePreparation()!.GuestMedicalCollapses);
    }
    [TestMethod]
    public void FightMetricUsesOneActualEncounterIncludingGuestAndExcludesPerformerPair()
    {
        var s = Open(); var p = s.CapturePreparation()!; var guests = p.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.Admitted).Take(2).ToArray();
        Invoke(s, "BeginDisorderFight", guests[0].AgentId, guests[1].AgentId, "labelled-metric-fight");
        Assert.AreEqual(1, s.CaptureDisorder()!.Incidents.Length);
        var performers = p.People.Where(person => person.Role == ProtectedPersonRole.Performer && person.Admitted).Take(2).ToArray();
        // Non-guest initiators are not an eligible production encounter; initialize a
        // structured origin solely to prove the report's defensive role filter.
        var disorder = s.CaptureDisorder()!;
        Set(s, "DisorderView", disorder with { Incidents = disorder.Incidents.Append(new(performers[0].AgentId, performers[1].AgentId, DisorderGrievance.None, 0, s.CurrentTick, s.CurrentTick, -1, null)).ToArray() });
        var method = typeof(GameSession).GetMethod("MakeFestivalResult", BindingFlags.NonPublic | BindingFlags.Static)!;
        p = s.CapturePreparation()! with { People = s.CapturePreparation()!.People.Select(person => person with { Departed = true }).ToArray() };
        var result = (FestivalResult)method.Invoke(null, [p, s.CaptureImmersion(), s.CaptureMedical(), s.CaptureDisorder(), s.CurrentTick])!;
        Assert.AreEqual(1, result.Fights); Assert.AreEqual(2, s.CaptureDisorder()!.Incidents.Length);
    }
}
