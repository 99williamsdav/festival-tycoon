using Festival.Simulation;
using Festival.Persistence;

namespace Festival.Tests;

[TestClass]
public sealed class PreparationTests
{
    private static readonly SaveCompatibility Compatibility = new("r0.01-tests", "test-content", "test-rules");
    private static CommandResult Execute(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase, session.CurrentTick,
        session.NextSubmissionSequence, null, command));
    private static void Book(GameSession session, string equipment = "equipment.buy")
    {
        foreach (var id in new[] { "staff.steward", equipment })
            Assert.IsTrue(Execute(session, new AcceptPreparationOfferCommand(id)).IsAccepted);
    }
    private static GameSession Restore(GameSession session)
    {
        var result = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash);
        return result.Session;
    }

    [TestMethod]
    public void MalformedPreparationRejectsMissingRosterAndUnpaidProperty()
    {
        var session = BuildSession.Planned(2);
        var saved = session.CapturePersistenceSnapshot();
        var p = saved.Preparation!;
        foreach (var malformed in new[]
        {
            p with { People = p.People.Skip(1).ToArray() },
            p with { OwnedEquipment = ["sound-rig"] },
            p with { Contacts = ["staff.engineer"] },
            p with { AcceptedOffers = [null!] },
            p with { Payments = [new(1, "act.folk", 1, 0, -1, LedgerAccountType.AdministrationExpense)] },
            p with { People = p.People.Select((person, index) => index == 0 ? person with { AgentId = 9999 } : person).ToArray() }
        })
        {
            var result = GameSession.Restore(saved with { Preparation = malformed });
            Assert.IsFalse(result.IsSuccess);
            Assert.IsNotNull(result.Error);
        }
    }

    [TestMethod]
    public void PurchasedRigCostsMoreNowAndImprovesActualMusicQuality()
    {
        var owned = BuildSession.Planned(2);
        var rented = BuildSession.Planned(2);
        Book(owned); Book(rented, equipment: "equipment.rent");
        Execute(owned, new StartPreparedEditionCommand()); Execute(rented, new StartPreparedEditionCommand());
        owned.AdvanceWithoutSnapshot(3_000); rented.AdvanceWithoutSnapshot(3_000);
        Assert.AreEqual(9_000L, rented.CaptureSnapshot().FestivalFinances.Single().CashPennies - owned.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.IsTrue(owned.CapturePreparation()!.People[1].Satisfaction >= rented.CapturePreparation()!.People[1].Satisfaction);
        Assert.AreEqual(owned.CapturePreparation()!.People[1].AgentId, rented.CapturePreparation()!.People[1].AgentId);
    }

    [TestCategory("Slow")]
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EditionBoundariesSaveCandidatesAndFailedWritesPreserveVisibleState(bool completion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "festival-preparation-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var session = BuildSession.Planned(2);
            Book(session, equipment: "equipment.rent");
            Execute(session, new StartPreparedEditionCommand());
            session.AdvanceWithoutSnapshot(GameSession.PreparedWeekendTicks - 1);
            Assert.IsTrue(session.PreparationBoundaryOnNextTick);
            if (completion)
            {
                session.AdvanceWithoutSnapshot(1);
                for (var index = 0; index < 3_000 && !session.PreparationBoundaryOnNextTick; index++) session.AdvanceWithoutSnapshot(1);
                Assert.AreEqual(PreparationStatus.Departing, session.PreparedStatus);
                Assert.IsTrue(session.PreparationBoundaryOnNextTick);
            }
            Assert.AreEqual(1, session.CapturePreparation()!.Rentals.Length);
            Assert.AreEqual(1, session.CapturePreparation()!.WorkContracts.Length);
            Assert.IsTrue(AutosaveRotation.Save(directory, session, Compatibility, DateTimeOffset.UnixEpoch, 0).IsSuccess);
            var before = session.CaptureSnapshot().AuthoritativeHash;
            var captured = session.CapturePersistenceSnapshot();
            var failure = Task.Run(() => PreparationAdvanceCoordinator.AdvanceCapturedBoundary(directory, session, captured,
                Compatibility, DateTimeOffset.UnixEpoch.AddMinutes(1), 1,
                _ => throw new IOException("Injected boundary save failure"))).GetAwaiter().GetResult();
            Assert.IsFalse(failure.IsSuccess);
            Assert.AreSame(session, failure.Session);
            Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(before, AutosaveRotation.LoadNewestValid(directory, Compatibility).Session!.CaptureSnapshot().AuthoritativeHash);
            Assert.IsTrue(failure.Error!.Contains("retry", StringComparison.OrdinalIgnoreCase));
            var success = Task.Run(() => PreparationAdvanceCoordinator.AdvanceCapturedBoundary(directory, session, captured,
                Compatibility, DateTimeOffset.UnixEpoch.AddMinutes(2), 1)).GetAwaiter().GetResult();
            Assert.IsTrue(success.IsSuccess, success.Error);
            Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash, "Source must remain untouched even after candidate commit.");
            var committed = success.Session;
            Assert.AreEqual(completion ? PreparationStatus.Finished : PreparationStatus.Departing, committed.PreparedStatus);
            Assert.AreEqual(completion ? 0 : 1, committed.CapturePreparation()!.Rentals.Length);
            Assert.AreEqual(completion ? 0 : 1, committed.CapturePreparation()!.WorkContracts.Length);
            Assert.AreEqual(committed.CaptureSnapshot().AuthoritativeHash,
                AutosaveRotation.LoadNewestValid(directory, Compatibility).Session!.CaptureSnapshot().AuthoritativeHash);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void AsynchronousBoundaryClockMatchesSynchronousTicksThroughWaitPauseAndSpeedChange()
    {
        var root = Path.Combine(Path.GetTempPath(), "festival-async-boundary-clock-" + Guid.NewGuid().ToString("N"));
        var synchronousDirectory = Path.Combine(root, "synchronous");
        var asynchronousDirectory = Path.Combine(root, "asynchronous");
        Directory.CreateDirectory(synchronousDirectory);
        Directory.CreateDirectory(asynchronousDirectory);
        try
        {
            var synchronous = BuildSession.Planned(2);
            Book(synchronous, equipment: "equipment.rent");
            Execute(synchronous, new StartPreparedEditionCommand());
            synchronous.AdvanceWithoutSnapshot(GameSession.PreparedWeekendTicks - 3);
            var asynchronous = Restore(synchronous);
            var normalClock = new FoundationClock();
            var responsiveClock = new FoundationClock();
            var now = DateTimeOffset.UnixEpoch;

            void AdvanceNormal(int count)
            {
                for (var index = 0; index < count; index++)
                {
                    var result = PreparationAdvanceCoordinator.AdvanceOne(synchronousDirectory, synchronous,
                        Compatibility, now, 1);
                    Assert.IsTrue(result.IsSuccess, result.Error);
                    synchronous = result.Session;
                }
            }
            void AdvanceResponsive(int count)
            {
                for (var index = 0; index < count; index++)
                {
                    var result = PreparationAdvanceCoordinator.AdvanceOne(asynchronousDirectory, asynchronous,
                        Compatibility, now, 1);
                    Assert.IsTrue(result.IsSuccess, result.Error);
                    asynchronous = result.Session;
                }
            }

            Assert.AreEqual(8, normalClock.Schedule(.1));
            AdvanceNormal(8);
            Assert.AreEqual(8, responsiveClock.Schedule(.1));
            AdvanceResponsive(2);
            Assert.IsTrue(asynchronous.PreparationBoundaryOnNextTick);
            responsiveClock.RequeueUnprocessedTicks(5); // the worker owns the boundary tick, not this tail
            var sourceHash = asynchronous.CaptureSnapshot().AuthoritativeHash;
            var source = asynchronous;
            var captured = source.CapturePersistenceSnapshot();
            var pending = Task.Run(() => PreparationAdvanceCoordinator.AdvanceCapturedBoundary(
                asynchronousDirectory, source, captured, Compatibility, now, 1));
            Assert.AreEqual(0, responsiveClock.Schedule(.025, 0));
            Assert.AreEqual(sourceHash, source.CaptureSnapshot().AuthoritativeHash);
            var committed = pending.GetAwaiter().GetResult();
            Assert.IsTrue(committed.IsSuccess, committed.Error);
            asynchronous = committed.Session;
            Assert.AreEqual(source.CurrentTick + 1, asynchronous.CurrentTick);
            Assert.AreEqual(2, normalClock.Schedule(.025));
            AdvanceNormal(2);
            Assert.AreEqual(7, responsiveClock.Schedule(0));
            AdvanceResponsive(7);
            Assert.AreEqual(synchronous.CurrentTick, asynchronous.CurrentTick);
            Assert.AreEqual(synchronous.CaptureSnapshot().AuthoritativeHash, asynchronous.CaptureSnapshot().AuthoritativeHash);

            normalClock.IsPaused = responsiveClock.IsPaused = true;
            Assert.AreEqual(0, normalClock.Schedule(.1));
            Assert.AreEqual(0, responsiveClock.Schedule(.1));
            normalClock.IsPaused = responsiveClock.IsPaused = false;
            normalClock.RequestedSpeed = responsiveClock.RequestedSpeed = RequestedSpeed.FourX;
            var normalFastTicks = normalClock.Schedule(.025);
            var responsiveFastTicks = responsiveClock.Schedule(.025);
            Assert.AreEqual(normalFastTicks, responsiveFastTicks);
            AdvanceNormal(normalFastTicks);
            AdvanceResponsive(responsiveFastTicks);
            Assert.AreEqual(synchronous.CurrentTick, asynchronous.CurrentTick);
            Assert.AreEqual(synchronous.CaptureSnapshot().AuthoritativeHash, asynchronous.CaptureSnapshot().AuthoritativeHash);

            for (var index = 0; index < 3_000 && !synchronous.PreparationBoundaryOnNextTick; index++)
            {
                AdvanceNormal(1);
                AdvanceResponsive(1);
            }
            Assert.IsTrue(synchronous.PreparationBoundaryOnNextTick, "Expected the later completion boundary.");
            Assert.IsTrue(asynchronous.PreparationBoundaryOnNextTick);
            normalClock = new FoundationClock();
            responsiveClock = new FoundationClock();
            Assert.AreEqual(8, normalClock.Schedule(.1));
            AdvanceNormal(8);
            Assert.AreEqual(8, responsiveClock.Schedule(.1));
            responsiveClock.RequeueUnprocessedTicks(7);
            source = asynchronous;
            captured = source.CapturePersistenceSnapshot();
            pending = Task.Run(() => PreparationAdvanceCoordinator.AdvanceCapturedBoundary(
                asynchronousDirectory, source, captured, Compatibility, now, 2));
            Assert.AreEqual(0, responsiveClock.Schedule(.025, 0));
            committed = pending.GetAwaiter().GetResult();
            Assert.IsTrue(committed.IsSuccess, committed.Error);
            asynchronous = committed.Session;
            Assert.AreEqual(2, normalClock.Schedule(.025));
            AdvanceNormal(2);
            Assert.AreEqual(9, responsiveClock.Schedule(0));
            AdvanceResponsive(9);
            Assert.AreEqual(synchronous.CurrentTick, asynchronous.CurrentTick);
            Assert.AreEqual(synchronous.CaptureSnapshot().AuthoritativeHash, asynchronous.CaptureSnapshot().AuthoritativeHash);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void CapturedBoundarySaveAllowsLiveTicksAndFailureRestoresRetryPoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "festival-moving-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var live = BuildSession.Planned(2);
            Book(live, equipment: "equipment.rent");
            Execute(live, new StartPreparedEditionCommand());
            live.AdvanceWithoutSnapshot(GameSession.PreparedWeekendTicks - 1);
            Assert.IsTrue(live.PreparationBoundaryOnNextTick);
            var before = live.CapturePersistenceSnapshot();
            var beforeHash = live.CaptureSnapshot().AuthoritativeHash;
            live.AdvanceWithoutSnapshot(1);
            var checkpoint = live.CapturePersistenceSnapshot();
            var checkpointHash = live.CaptureSnapshot().AuthoritativeHash;
            var pending = Task.Run(() => AutosaveRotation.SaveCaptured(directory, checkpoint, Compatibility,
                DateTimeOffset.UnixEpoch, 0));
            live.AdvanceWithoutSnapshot(8);
            Assert.AreEqual(before.CurrentTick + 9, live.CurrentTick, "Simulation must advance while the save is pending.");
            Assert.IsTrue(pending.GetAwaiter().GetResult().IsSuccess);
            var saved = AutosaveRotation.LoadNewestValid(directory, Compatibility);
            Assert.IsTrue(saved.IsSuccess, saved.Error);
            Assert.AreEqual(before.CurrentTick + 1, saved.Session!.CurrentTick);
            Assert.AreEqual(checkpointHash, saved.Session.CaptureSnapshot().AuthoritativeHash);

            var failed = Task.Run(() => AutosaveRotation.SaveCaptured(directory, checkpoint, Compatibility,
                DateTimeOffset.UnixEpoch.AddSeconds(1), 1,
                _ => throw new IOException("Injected moving-boundary failure"))).GetAwaiter().GetResult();
            Assert.IsFalse(failed.IsSuccess);
            var rollback = GameSession.Restore(before);
            Assert.IsTrue(rollback.IsSuccess, rollback.Error);
            Assert.AreEqual(beforeHash, rollback.Session!.CaptureSnapshot().AuthoritativeHash);
            Assert.IsTrue(rollback.Session.PreparationBoundaryOnNextTick);
            saved = AutosaveRotation.LoadNewestValid(directory, Compatibility);
            Assert.IsTrue(saved.IsSuccess, saved.Error);
            Assert.AreEqual(checkpointHash, saved.Session!.CaptureSnapshot().AuthoritativeHash);
        }
        finally { Directory.Delete(directory, true); }
    }

}
