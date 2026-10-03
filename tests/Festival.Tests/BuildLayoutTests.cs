using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class BuildLayoutTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase, session.CurrentTick,
        session.NextSubmissionSequence, null, command));

    [TestMethod]
    public void FreshBuildSeedsStayDeterministicAndRerolledHandSurvivesDiskLoad()
    {
        const ulong seed = 0x6E84_C2A9_FD03_41B7;
        var session = GameSession.CreateBuildCampaign(seed, FestivalStanding.Established);
        var sameSeed = GameSession.CreateBuildCampaign(seed, FestivalStanding.Established);
        var differentSeed = GameSession.CreateBuildCampaign(seed + 1, FestivalStanding.Established);
        Assert.AreNotEqual(0UL, session.CampaignSeed);
        Assert.AreEqual(seed, session.CampaignId.Value);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, sameSeed.CaptureSnapshot().AuthoritativeHash);
        Assert.AreNotEqual(session.CampaignId, differentSeed.CampaignId);
        var initial = session.CapturePerks()!;
        CollectionAssert.AreEqual(initial.Hand, sameSeed.CapturePerks()!.Hand);
        Assert.AreEqual(initial.Cursor, sameSeed.CapturePerks()!.Cursor);

        var reroll = Send(session, new RerollPerksCommand(initial.DraftAttempt, initial.Cursor));
        Assert.IsTrue(reroll.IsAccepted, reroll.Message);
        var rerolled = session.CapturePerks()!;
        var compatibility = new SaveCompatibility("r0.05x-seed-test", LowerWitteringFarmScenario.ContentCompatibilityHash,
            "build-seed-test");
        var directory = Path.Combine(Path.GetTempPath(), "festival-build-seed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var saved = AutosaveRotation.Save(directory, session, compatibility, DateTimeOffset.UtcNow, 0);
            Assert.IsTrue(saved.IsSuccess, saved.Error);
            var loaded = AutosaveRotation.LoadNewestValid(directory, compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(seed, loaded.Session.CampaignSeed);
            var restoredHand = loaded.Session.CapturePerks()!;
            CollectionAssert.AreEqual(rerolled.Hand, restoredHand.Hand);
            Assert.AreEqual(rerolled.Cursor, restoredHand.Cursor);
            Assert.IsTrue(restoredHand.RerollUsed);
            var beforeRejectedReroll = loaded.Session.CaptureSnapshot().AuthoritativeHash;
            Assert.IsFalse(Send(loaded.Session, new RerollPerksCommand(restoredHand.DraftAttempt, restoredHand.Cursor)).IsAccepted);
            Assert.AreEqual(beforeRejectedReroll, loaded.Session.CaptureSnapshot().AuthoritativeHash);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DraftPlacementRemovalAndCancelAccountingAreUnpaid()
    {
        var session = GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established);
        var perk = session.CapturePerks()!;
        Assert.IsTrue(Send(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])).IsAccepted);
        var cash = session.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        var water = GameSession.StandardBuildLayout().Single(item => item.Kind == BuildServiceKind.WaterTap);
        Assert.IsTrue(Send(session, new PlaceBuildServiceCommand(water.Kind, water.Cell)).IsAccepted);
        Assert.AreEqual(2_000L, session.BuildDraftCost);
        Assert.AreEqual(1, session.CaptureWaterPoints().Count);
        var before = session.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(Send(session, new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, water.Cell)).IsAccepted);
        Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(Send(session, new RemoveBuildServiceCommand("water.main")).IsAccepted);
        Assert.AreEqual(0L, session.BuildDraftCost);
        Assert.AreEqual(0, session.CaptureWaterPoints().Count);
        Assert.AreEqual(cash, session.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    [TestMethod]
    public void CustomEastFieldTapRemainsSaveable()
    {
        var session = GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established);
        var perk = session.CapturePerks()!;
        Assert.IsTrue(Send(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])).IsAccepted);
        var placed = Send(session, new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, new(150, 160)));
        Assert.IsTrue(placed.IsAccepted, placed.Message);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void ValidBuildToiletAtFormerPostSiteAutosavesAndRestoresWithoutLosingPriorSlot()
    {
        var session = GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established);
        var perk = session.CapturePerks()!;
        Assert.IsTrue(Send(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])).IsAccepted);
        var placement = new PlaceBuildServiceCommand(BuildServiceKind.Toilet, new(115, 115), 2);
        Assert.IsNull(session.ValidateCommand(new(new(session.NextSubmissionSequence + 1), session.CampaignId,
            session.Phase, session.CurrentTick, session.NextSubmissionSequence, null, placement)));
        var compatibility = new SaveCompatibility("r0.05w-tests", LowerWitteringFarmScenario.ContentCompatibilityHash, "build-layout-tests");
        var directory = Path.Combine(Path.GetTempPath(), "festival-r005w-toilet-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.IsTrue(AutosaveRotation.Save(directory, session, compatibility, DateTimeOffset.UtcNow, 0).IsSuccess);
            var before = session.CaptureSnapshot().AuthoritativeHash;
            var committed = EquipmentCommandCoordinator.Execute(directory, session, placement, compatibility,
                DateTimeOffset.UtcNow, 3);
            Assert.IsTrue(committed.IsSuccess, committed.Error);
            Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash, "Original state is immutable until autosave succeeds.");
            Assert.AreEqual(1, committed.Session.CaptureToilets().Count);
            var loaded = SaveFileAdapter.LoadSlot(directory, "autosave-0", compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            Assert.AreEqual(committed.Session.CaptureSnapshot().AuthoritativeHash,
                loaded.Session!.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(new GridCell(115, 115), loaded.Session.CaptureToilet()!.Cell);
            Assert.AreEqual(2, loaded.Session.CaptureToilet()!.QuarterTurns);
            var preserved = loaded.Session.CaptureSnapshot().AuthoritativeHash;
            var failed = EquipmentCommandCoordinator.Execute(directory, loaded.Session,
                new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, new(150, 160)), compatibility,
                DateTimeOffset.UtcNow, 6, _ => throw new IOException("injected post-validation failure"));
            Assert.IsFalse(failed.IsSuccess);
            Assert.AreEqual(preserved, failed.Session.CaptureSnapshot().AuthoritativeHash);
            var stillLoaded = SaveFileAdapter.LoadSlot(directory, "autosave-0", compatibility);
            Assert.IsTrue(stillLoaded.IsSuccess, stillLoaded.Error);
            Assert.AreEqual(preserved, stillLoaded.Session!.CaptureSnapshot().AuthoritativeHash);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ASecondTapNeedsTheAnotherRoundPerk()
    {
        var without = GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established);
        var hand = without.CapturePerks()!;
        Assert.IsTrue(Send(without, new ChoosePerkCommand(hand.DraftAttempt, hand.Cursor, hand.Hand.First(id => id != "another-round"))).IsAccepted);
        Assert.IsTrue(Send(without, new UseDefaultBuildLayoutCommand()).IsAccepted);
        Assert.AreEqual(1, without.ServiceLimit(BuildServiceKind.WaterTap));
        StringAssert.Contains(Send(without, new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, new(150, 160))).Message, "Another Round");

        var (seed, index) = BuildSession.SeedOffering("another-round");
        var session = GameSession.CreateBuildCampaign(seed, FestivalStanding.Established);
        var perk = session.CapturePerks()!;
        Assert.IsTrue(Send(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[index])).IsAccepted);
        Assert.IsTrue(Send(session, new UseDefaultBuildLayoutCommand()).IsAccepted);
        Assert.AreEqual(2, session.ServiceLimit(BuildServiceKind.WaterTap));
        GridCell? second = null;
        for (var x = 105; x <= 180 && second is null; x += 5)
        for (var z = 115; z <= 180 && second is null; z += 5)
        {
            var candidate = new GridCell(x, z);
            if (session.ValidateCommand(new(new(session.NextSubmissionSequence + 1), session.CampaignId,
                session.Phase, session.CurrentTick, session.NextSubmissionSequence, null,
                new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, candidate))) is null) second = candidate;
        }
        Assert.IsNotNull(second);
        Assert.IsTrue(Send(session, new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, second.Value)).IsAccepted);
        Assert.AreEqual(2, session.CaptureWaterPoints().Count);
        Assert.AreEqual(32_000L, session.BuildDraftCost);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(2, restored.Session!.CaptureWaterPoints().Count);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void EachCustomServiceMoveRestoresWithItsPhysicalMirror()
    {
        foreach (var standard in GameSession.StandardBuildLayout())
        {
            var session = GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established);
            var perk = session.CapturePerks()!;
            Assert.IsTrue(Send(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])).IsAccepted);
            Assert.IsTrue(Send(session, new UseDefaultBuildLayoutCommand()).IsAccepted);
            GridCell? site = null;
            for (var x = 140; x <= 180 && site is null; x += 10)
            for (var z = 130; z <= 180 && site is null; z += 10)
            {
                var candidate = new GridCell(x, z);
                if (candidate == standard.Cell) continue;
                if (session.ValidateCommand(new(new(session.NextSubmissionSequence + 1), session.CampaignId,
                    session.Phase, session.CurrentTick, session.NextSubmissionSequence, null,
                    new MoveBuildServiceCommand(standard.Id, candidate, standard.QuarterTurns))) is null) site = candidate;
            }
            Assert.IsNotNull(site, $"No alternative site for {standard.Kind}.");
            var moved = Send(session, new MoveBuildServiceCommand(standard.Id, site.Value, standard.QuarterTurns));
            Assert.IsTrue(moved.IsAccepted, moved.Message);
            var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
            Assert.IsTrue(restored.IsSuccess, $"{standard.Kind}: {restored.Error}");
            Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        }
    }

    [TestMethod]
    public void TwoToiletsHaveDistinctSavedFacilityStateAndRoutes()
    {
        var session = GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established);
        var perk = session.CapturePerks()!;
        Assert.IsTrue(Send(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])).IsAccepted);
        Assert.IsTrue(Send(session, new UseDefaultBuildLayoutCommand()).IsAccepted);
        GridCell? second = null;
        for (var x = 145; x <= 190 && second is null; x += 5)
        for (var z = 115; z <= 190 && second is null; z += 5)
        {
            var cell = new GridCell(x, z);
            if (session.ValidateCommand(new(new(session.NextSubmissionSequence + 1), session.CampaignId,
                session.Phase, session.CurrentTick, session.NextSubmissionSequence, null,
                new PlaceBuildServiceCommand(BuildServiceKind.Toilet, cell))) is null) second = cell;
        }
        Assert.IsNotNull(second, "A second independent portaloo site should exist on the festival field.");
        var placed = Send(session, new PlaceBuildServiceCommand(BuildServiceKind.Toilet, second.Value));
        Assert.IsTrue(placed.IsAccepted, placed.Message);
        Assert.AreEqual(2, session.CaptureToilets().Count);
        Assert.AreNotEqual(session.CaptureToilets()[0].Id, session.CaptureToilets()[1].Id);
        Assert.AreNotEqual(session.CaptureToilets()[0].Cell, session.CaptureToilets()[1].Cell);
        Assert.AreEqual(34_000L, session.BuildDraftCost);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"])).IsAccepted);
        foreach (var hire in BuildSession.Crew(session)) Assert.IsTrue(Send(session, hire).IsAccepted);
        Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand("equipment.rent")).IsAccepted);
        Assert.IsTrue(Send(session, new SetPreparationStockCommand(40, 40, 32)).IsAccepted);
        var started = Send(session, new StartPreparedEditionCommand());
        Assert.IsTrue(started.IsAccepted, started.Message);
        Assert.AreEqual(34_000L, session.CapturePreparation()!.SetupPayments!.Single().BuildCostPennies);
        Assert.AreEqual(10_600L, session.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        var paidHash = session.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        Assert.AreEqual(paidHash, session.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(2, session.CaptureToilets().Count);
        session.AdvanceWithoutSnapshot(400);
        var liveRestored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(liveRestored.IsSuccess, liveRestored.Error);
    }

    [TestMethod]
    public void TwoUrgentGuestsChooseDifferentNearbyToilets()
    {
        var session = GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established);
        var perk = session.CapturePerks()!;
        Assert.IsTrue(Send(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])).IsAccepted);
        Assert.IsTrue(Send(session, new UseDefaultBuildLayoutCommand()).IsAccepted);
        GridCell? candidate = null;
        for (var x = 100; x <= 145 && candidate is null; x += 5)
        for (var z = 115; z <= 175 && candidate is null; z += 5)
        {
            var site = new GridCell(x, z);
            if (session.ValidateCommand(new(new(session.NextSubmissionSequence + 1), session.CampaignId,
                session.Phase, session.CurrentTick, session.NextSubmissionSequence, null,
                new PlaceBuildServiceCommand(BuildServiceKind.Toilet, site))) is null) candidate = site;
        }
        Assert.IsNotNull(candidate, "A separated second toilet should have a valid site.");
        Assert.IsTrue(Send(session, new PlaceBuildServiceCommand(BuildServiceKind.Toilet, candidate.Value)).IsAccepted);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"])).IsAccepted);
        foreach (var hire in BuildSession.Crew(session)) Assert.IsTrue(Send(session, hire).IsAccepted);
        Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand("equipment.rent")).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        Assert.IsFalse(session.IsPaused);
        var guests = session.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest).Take(2).ToArray();
        var prep = session.CapturePreparation()!;
        typeof(GameSession).GetProperty("PreparationView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            prep with { People = prep.People.Select(person => guests.Any(guest => guest.AgentId == person.AgentId) ?
                person with { Admitted = true } : person).ToArray() });
        var immersion = session.CaptureImmersion()!;
        var ids = guests.Select(person => person.AgentId).ToHashSet();
        typeof(GameSession).GetProperty("ImmersionView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            immersion with { People = immersion.People.Select(person => ids.Contains(person.AgentId) ?
                person with { ToiletNeed = 9_000 } : person).ToArray() });
        var medical = session.CaptureMedical()!;
        typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            medical with { Needs = medical.Needs.Select(need => ids.Contains(need.AgentId) ?
                need with { Thirst = 0, HeatExposure = 0, Intent = MedicalIntent.WatchShow, LastDecisionTick = 0 } : need).ToArray() });
        var toilets = session.CaptureToilets().OrderBy(item => item.Cell.X).ToArray();
        var navigation = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var getNavigation = navigation.GetType().GetMethod("get_Item")!;
        void PlaceIdleGuestsNearToilets()
        {
            for (var index = 0; index < 2; index++)
            {
                if (session.CaptureImmersion()!.People.Single(person => person.AgentId == guests[index].AgentId).ToiletStage != ToiletVisitStage.None) continue;
                var nav = getNavigation.Invoke(navigation, [new EntityId(guests[index].AgentId)])!;
                var centre = TraversalGrid.CellCentre(GameSession.ToiletQueueCell(toilets[index], 0));
                nav.GetType().GetProperty("XMillimetres")!.SetValue(nav, centre.XMillimetres);
                nav.GetType().GetProperty("ZMillimetres")!.SetValue(nav, centre.ZMillimetres);
                nav.GetType().GetProperty("Route")!.SetValue(nav, new List<GridCell>());
                nav.GetType().GetProperty("Destination")!.SetValue(nav, null);
                nav.GetType().GetProperty("IntentId")!.SetValue(nav, null);
                nav.GetType().GetProperty("Action")!.SetValue(nav, AgentNavigationAction.Arrived);
            }
        }
        for (var tick = 0; tick < 120; tick++)
        {
            PlaceIdleGuestsNearToilets();
            session.AdvanceWithoutSnapshot(1);
            if (session.CaptureImmersion()!.People.Where(person => ids.Contains(person.AgentId)).All(person => person.ToiletId is not null)) break;
        }
        var routed = session.CaptureImmersion()!.People.Where(person => ids.Contains(person.AgentId))
            .Select(person => person.ToiletId).ToArray();
        Assert.AreEqual(2, routed.Distinct().Count(), $"tick={session.CurrentTick}; routes={string.Join(",", routed)}; stages={string.Join(",", session.CaptureImmersion()!.People.Where(person => ids.Contains(person.AgentId)).Select(person => person.ToiletStage))}; admitted={string.Join(",", session.CapturePreparation()!.People.Where(person => ids.Contains(person.AgentId)).Select(person => person.Admitted))}; intents={string.Join(",", session.CaptureMedical()!.Needs.Where(person => ids.Contains(person.AgentId)).Select(person => person.Intent))}; vendor={string.Join(",", session.CaptureImmersion()!.People.Where(person => ids.Contains(person.AgentId)).Select(person => person.VendorId ?? "none"))}");
        Assert.IsTrue(routed.All(id => toilets.Any(toilet => toilet.Id == id)));
    }

    [TestMethod]
    public void FailureRetryKeepsEditableLayoutAndOtherPlanChoicesWithoutDoubleCharging()
    {
        var session = GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established);
        var perk = session.CapturePerks()!;
        Assert.IsTrue(Send(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])).IsAccepted);
        Assert.IsTrue(Send(session, new UseDefaultBuildLayoutCommand()).IsAccepted);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"])).IsAccepted);
        foreach (var hire in BuildSession.Crew(session)) Assert.IsTrue(Send(session, hire).IsAccepted);
        Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand("equipment.rent")).IsAccepted);
        Assert.IsTrue(Send(session, new SetPreparationStockCommand(40, 40, 32)).IsAccepted);
        var firstCost = session.PreparationPlanCost;
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        Assert.AreEqual(65_400L, firstCost);
        var prep = session.CapturePreparation()!;
        typeof(GameSession).GetProperty("PreparationView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            prep with { People = prep.People.Select(person => person with { Admitted = true }).ToArray() });
        var guest = prep.People.First(person => person.Role == ProtectedPersonRole.Guest).AgentId;
        var immersion = session.CaptureImmersion()!;
        typeof(GameSession).GetProperty("ImmersionView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            immersion with { People = immersion.People.Select(person => person.AgentId == guest ?
                person with { Intoxication = 10_000 } : person).ToArray() });
        var medical = session.CaptureMedical()!;
        var medics = session.GetMedicResponses().Select(job => job.WorkerId).ToHashSet();
        typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            medical with { Needs = medical.Needs.Select(need => medics.Contains(need.AgentId) ?
                need with { Intent = MedicalIntent.Rest, Reason = "Labelled unavailable medic retry fixture" } : need).ToArray() });
        session.AdvanceWithoutSnapshot(4_000);
        Assert.AreEqual(PreparationStatus.Failed, session.PreparedStatus);
        Assert.IsTrue(Send(session, new SpendCouncilFavourCommand()).IsAccepted);
        var retry = session.CapturePreparationPlan()!;
        Assert.IsFalse(retry.Committed);
        CollectionAssert.AreEqual(GameSession.StandardBuildLayout().Select(item => item.Id).Order().ToArray(),
            session.CaptureBuildPlacements().Select(item => item.Id).Order().ToArray());
        Assert.AreEqual(firstCost, session.PreparationPlanCost);
        Assert.AreEqual(80_000L, session.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.AreEqual(6, session.CaptureBuildPlacements().Count);
        Assert.AreEqual(0, session.CaptureToilets().Single().WeeCount);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        session = restored.Session!;
        var secondPerk = session.CapturePerks()!;
        Assert.IsTrue(Send(session, new ChoosePerkCommand(secondPerk.DraftAttempt, secondPerk.Cursor, secondPerk.Hand[0])).IsAccepted);
        Assert.IsTrue(Send(session, new RemoveBuildServiceCommand("drinks")).IsAccepted);
        Assert.AreEqual(firstCost - 7_000, session.PreparationPlanCost);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        Assert.AreEqual(2, session.CapturePreparation()!.SetupPayments!.Length);
        Assert.AreEqual(7_000L, session.CaptureSnapshot().FestivalFinances.Single().CashPennies + firstCost - 80_000L);
    }
}
