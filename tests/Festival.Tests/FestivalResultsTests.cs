using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class FestivalResultsTests
{
    private static void Set(GameSession s, string field, object value) => typeof(GameSession).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(s, value);
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
        var s = GameSession.CreateResultsCampaign(20260922); var perks = s.CapturePerks()!;
        Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand[0]));
        Accept(s, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"]));
        Accept(s, new AcceptPreparationOfferCommand("staff.engineer")); Accept(s, new AcceptPreparationOfferCommand("equipment.buy"));
        Accept(s, new PurchaseImmersionStarterStockCommand()); Accept(s, new StartPreparedEditionCommand());
        s.AdvanceWithoutSnapshot(1200); return s;
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
    [TestMethod]
    public void NaturalDepartureFrozenReportAndActualFileReload()
    {
        var s = Open(); Closing(s); var prior = s.CapturePreparation()!;
        s = Reload(s); s.AdvanceWithoutSnapshot(15000);
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus); Assert.IsNotNull(s.CompletedFestivalResult);
        Assert.IsNotNull(s.CompletedFestivalAccounts); Assert.IsTrue(s.CompletedFestivalAccounts.Reconciles);
        Assert.AreEqual(20, s.CompletedFestivalResult.GuestCount); Assert.AreEqual(prior.People.Length, s.CapturePreparation()!.People.Length);
        var directory = Path.Combine(Path.GetTempPath(), "festival-results-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var compatibility = new SaveCompatibility("results", "content", "results");
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "finished", new(s, compatibility, "test", DateTimeOffset.UtcNow)).IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(directory, "finished", compatibility); Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            var hash = loaded.Session!.CaptureSnapshot().AuthoritativeHash; loaded.Session.AdvanceWithoutSnapshot(500);
            Assert.AreEqual(hash, loaded.Session.CaptureSnapshot().AuthoritativeHash); Assert.AreEqual(s.CompletedFestivalResult, loaded.Session.CompletedFestivalResult);
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
        Invoke(s, "SetNeed", guest.AgentId, (Func<MedicalNeed, MedicalNeed>)(n => n with { Intent = MedicalIntent.WatchShow }));
        Position(s, guest.AgentId, new(120, 125));
        var immersion = s.CaptureImmersion()!;
        Set(s, "_immersion", immersion with { People = immersion.People.Select(p => p.AgentId == guest.AgentId ? p with { Held = p.Held! with { ConsumedTicks = 2399 } } : p).ToArray() });
        Assert.AreEqual(0, s.CapturePreparation()!.FinishedBeerIds!.Length); Invoke(s, "AdvanceImmersion");
        Assert.AreEqual(1, s.CapturePreparation()!.FinishedBeerIds!.Length); Invoke(s, "AdvanceImmersion"); Assert.AreEqual(1, s.CapturePreparation()!.FinishedBeerIds!.Length);
        Reload(s);
        var performer = s.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Performer && !s.IsCurrentProgrammePerformer(person.AgentId) && !s.CaptureImmersion()!.People.Single(item => item.AgentId == person.AgentId).Abstains);
        Invoke(s, "CompleteImmersionSale", performer.AgentId, ImmersionProduct.Beer);
        Invoke(s, "LeaveWater", performer.AgentId, "Labelled performer consumption fixture", false, true);
        Invoke(s, "SetNeed", performer.AgentId, (Func<MedicalNeed, MedicalNeed>)(n => n with { Intent = MedicalIntent.WatchShow })); Position(s, performer.AgentId, new(124, 125));
        immersion = s.CaptureImmersion()!;
        Set(s, "_immersion", immersion with { People = immersion.People.Select(person => person.AgentId == performer.AgentId ? person with { Held = person.Held! with { ConsumedTicks = 2399 } } : person).ToArray() });
        Invoke(s, "AdvanceImmersion"); Assert.IsNull(s.CaptureImmersion()!.People.Single(person => person.AgentId == performer.AgentId).Held);
        Assert.AreEqual(1, s.CapturePreparation()!.FinishedBeerIds!.Length); Reload(s);
    }
    [TestMethod]
    public void LedgerOperatingProfitExcludesOpeningFundsAndCapital()
    {
        var s = Open(); Closing(s); s.AdvanceWithoutSnapshot(15000); var r = s.CompletedFestivalResult!;
        Assert.AreEqual(12000L, r.CapitalPurchasesPennies);
        Assert.AreEqual(r.RevenuePennies - r.ContractCostsPennies - r.ConsumedStockCostsPennies, r.ProfitPennies);
        Assert.AreEqual(s.CaptureSnapshot().FestivalFinances.Single().CashPennies - 80000L, r.NetCashChangePennies);
        Assert.IsTrue(r.ContractCostsPennies > 0); Reload(s);
    }
    [TestMethod]
    public void LegacyNullMetricsPreserveCanonicalHashAndInvalidCompletionRejected()
    {
        var legacy = GameSession.CreateEditableCampaign(20260922); Assert.IsFalse(legacy.FestivalResultsEnabled); Reload(legacy);
        var s = Open(); var p = s.CapturePreparation()!; Set(s, "_preparation", p with { FinishedBeerIds = ["fabricated"] });
        Assert.IsFalse(GameSession.Restore(s.CapturePersistenceSnapshot()).IsSuccess);
        Set(s, "_preparation", p with { FinishedBeerIds = null });
        Assert.IsFalse(GameSession.Restore(s.CapturePersistenceSnapshot()).IsSuccess, "A partial metrics tracker must not masquerade as a legacy payload.");
    }
    [TestMethod]
    public void ActualExitTickDeathPrecedesDepartureAndRetryClearsMetrics()
    {
        var s = Open(); Closing(s); var m = s.CaptureMedical()!; var id = m.AtRiskGuestId; var p = s.CapturePreparation()!;
        var index = Array.FindIndex(p.People, person => person.AgentId == id);
        Set(s, "_preparation", p with { People = p.People.Select(person => person.AgentId == id ? person : person with { Departed = true }).ToArray() });
        Position(s, id, new(122 + index % 6 * 2, 190 + index / 6 * 2));
        Set(s, "_medical", m with { Stage = MedicalStage.Critical, WarningTick = 20000, CollapseTick = 21601, CriticalTick = 22401 });
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus); Assert.IsFalse(s.CapturePreparation()!.People.Single(person => person.AgentId == id).Departed);
        Assert.IsNull(s.CompletedFestivalResult); Assert.AreEqual(1, s.CaptureLifecycleSnapshot()!.Casualties.Count);
        Accept(s, new SpendCouncilFavourCommand()); Assert.AreEqual(PreparationStatus.Preparing, s.PreparedStatus);
        Assert.AreEqual(0, s.CapturePreparation()!.GuestMedicalCollapses); Assert.AreEqual(0, s.CapturePreparation()!.FinishedBeerIds!.Length); Assert.IsNull(s.CompletedFestivalResult); Reload(s);
    }
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
    [TestMethod]
    public void EarlyUnhappyGuestRetainedInFinalMeanAndHistory()
    {
        var s = Open(); var p = s.CapturePreparation()!; var guest = p.People.First(person => person.Role == ProtectedPersonRole.Guest && person.Admitted);
        Set(s, "_preparation", p with { People = p.People.Select(person => person.AgentId == guest.AgentId ? person with { Satisfaction = 100 } : person).ToArray() });
        Invoke(s, "SetDisorderPerson", guest.AgentId, (Func<DisorderPerson, DisorderPerson>)(person => person with { Stage = DisorderStage.Complaint, Pressure = 4000, Grievance = DisorderGrievance.MusicCutoff, GrievanceTick = s.CurrentTick, StageTick = s.CurrentTick }));
        Accept(s, new DisorderCommand(DisorderAction.SafeEgress, guest.AgentId)); s.AdvanceWithoutSnapshot(5000);
        Assert.IsTrue(s.CapturePreparation()!.People.Single(person => person.AgentId == guest.AgentId).Departed);
        var finalEarly = s.CapturePreparation()!.People.Single(person => person.AgentId == guest.AgentId).Satisfaction;
        Closing(s); s.AdvanceWithoutSnapshot(15000); var final = s.CapturePreparation()!;
        Assert.AreEqual(finalEarly, final.People.Single(person => person.AgentId == guest.AgentId).Satisfaction);
        Assert.AreEqual(final.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.Admitted && person.Departed).Sum(person => (long)person.Satisfaction), s.CompletedFestivalResult!.SatisfactionTotal);
        Assert.AreEqual(20, s.CompletedFestivalResult.GuestCount); Reload(s);
    }
    [TestMethod]
    public void ActualMedicalCollapseCountsOnceAndFightKnockoutDoesNotCount()
    {
        var s = Open(); Closing(s); var m = s.CaptureMedical()!; var id = m.AtRiskGuestId;
        var expected = s.CapturePreparation()!.GuestMedicalCollapses + 1;
        Set(s, "_medical", m with { Stage = MedicalStage.Distress, WarningTick = s.CurrentTick - GameSession.MedicalCollapseDelayTicks });
        Invoke(s, "AdvanceImmersionDepartureMedicine"); Assert.AreEqual(expected, s.CapturePreparation()!.GuestMedicalCollapses);
        Invoke(s, "AdvanceImmersionDepartureMedicine"); Assert.AreEqual(expected, s.CapturePreparation()!.GuestMedicalCollapses);
        var nonGuest = s.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Performer);
        Invoke(s, "SetNeed", nonGuest.AgentId, (Func<MedicalNeed, MedicalNeed>)(need => need with { Stage = MedicalStage.Distress, WarningTick = s.CurrentTick - GameSession.MedicalCollapseDelayTicks }));
        Invoke(s, "AdvanceImmersionDepartureMedicine"); Assert.AreEqual(expected, s.CapturePreparation()!.GuestMedicalCollapses);
        var otherGuest = s.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Guest && person.AgentId != id);
        Invoke(s, "SetDisorderPerson", otherGuest.AgentId, (Func<DisorderPerson, DisorderPerson>)(person => person with { Stage = DisorderStage.Injured, InjuryTick = s.CurrentTick }));
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
        Set(s, "_disorder", disorder with { Incidents = disorder.Incidents.Append(new(performers[0].AgentId, performers[1].AgentId, DisorderGrievance.None, 0, s.CurrentTick, s.CurrentTick, -1, null)).ToArray() });
        var method = typeof(GameSession).GetMethod("MakeFestivalResult", BindingFlags.NonPublic | BindingFlags.Static)!;
        p = s.CapturePreparation()! with { People = s.CapturePreparation()!.People.Select(person => person with { Departed = true }).ToArray() };
        var result = (FestivalResult)method.Invoke(null, [p, s.CaptureImmersion(), s.CaptureMedical(), s.CaptureDisorder(), s.CurrentTick])!;
        Assert.AreEqual(1, result.Fights); Assert.AreEqual(2, s.CaptureDisorder()!.Incidents.Length);
    }
    [TestMethod]
    public void ActualHistoricalSaveRemainsReadableWithKnownHashAndNewIdentityRejectsIt()
    {
        var path = Path.GetFullPath("../../../../../reports/evidence/R0.05h/final-1280x720/saves/manual-preparation.ftsave", AppContext.BaseDirectory);
        var original = File.ReadAllBytes(path);
        Assert.AreEqual("CBBDAF6BE8409B401907F669F603BE3FFEF4E9212F7DE3BC327998F379A82E56", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)));
        var envelope = (SaveEnvelopeV1)typeof(SaveFileAdapter).GetMethod("ReadEnvelope", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [path])!;
        var header = envelope.Header;
        var loaded = SaveFileAdapter.LoadFile(path, new(header.BuildId, header.ContentHash, header.RulesetHash)); Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Assert.AreEqual("dd38b5a79df072eef2e06adf1694787c73069519f408c5a4a54c8fee7c6f3742", loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        Assert.IsFalse(SaveFileAdapter.LoadFile(path, new("0.0.1-r0.05m-results-v1", header.ContentHash, "r0-results-v1")).IsSuccess);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }
}
