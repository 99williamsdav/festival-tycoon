using Festival.Persistence;
using Festival.Simulation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Festival.Tests;

[TestClass]
public sealed class SessionHostTests
{
    private static readonly SaveCompatibility Compatibility = new("host-test", "content", "rules");

    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "festival-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WaitForSave(SessionHost host)
    {
        for (var i = 0; i < 200 && host.Notice is not "Background save complete."; i++)
        {
            Thread.Sleep(20);
            host.AdvanceSaves(0);
        }
    }

    [TestMethod]
    public void ChangedStateIsWrittenInTheBackgroundOnceTheCadenceIsDue()
    {
        var directory = TempDirectory();
        try
        {
            var host = new SessionHost(BuildSession.Ready(), directory, Compatibility);
            Assert.IsTrue(host.Submit(new SetPreparationStockCommand(10, 10, 10)).IsAccepted);
            host.AdvanceSaves(RealTimeAutosaveScheduler.ProductionCadenceSeconds - 1);
            Assert.AreEqual(0, Directory.EnumerateFiles(directory).Count(), "Nothing is written before the cadence.");
            host.AdvanceSaves(2);
            WaitForSave(host);
            var saved = AutosaveRotation.LoadNewestValid(directory, Compatibility);
            Assert.IsTrue(saved.IsSuccess, saved.Error);
            Assert.AreEqual(host.Session.CaptureSnapshot().AuthoritativeHash, saved.Session!.CaptureSnapshot().AuthoritativeHash);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void RejectedCommandLeavesTheSessionExactlyAsItWas()
    {
        var host = new SessionHost(BuildSession.Ready(), TempDirectory(), Compatibility);
        var before = host.Session.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(host.Execute(new SpendCouncilFavourCommand(), out var error));
        Assert.IsNotNull(error);
        Assert.AreEqual(before, host.Session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void MilestoneThatCannotBeSavedDoesNotTakeEffect()
    {
        // A file where the save directory should be: every write fails.
        var blocked = Path.Combine(Path.GetTempPath(), "festival-host-blocked-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocked, "not a directory");
        try
        {
            var host = new SessionHost(BuildSession.Ready(), blocked, Compatibility);
            var before = host.Session.CaptureSnapshot().AuthoritativeHash;
            Assert.IsFalse(host.ExecuteMilestone(new StartPreparedEditionCommand(), "Festival start", out var error));
            StringAssert.StartsWith(error, "Festival start save failed");
            Assert.AreEqual(PreparationStatus.Preparing, host.Session.PreparedStatus);
            Assert.AreEqual(before, host.Session.CaptureSnapshot().AuthoritativeHash);
            Assert.IsNotNull(host.SaveError);
        }
        finally { File.Delete(blocked); }
    }

    [TestMethod]
    public void AdvanceRunsTheOwedTicksAndSavesAFailureAtOnce()
    {
        var directory = TempDirectory();
        try
        {
            var host = new SessionHost(BuildSession.Ready(), directory, Compatibility);
            Assert.IsTrue(host.ExecuteMilestone(new StartPreparedEditionCommand(), "Festival start", out var error), error);
            var seen = 0;
            Assert.AreEqual(8, host.Advance(0.1, _ => seen++), "One tenth of a second at 1× is eight ticks.");
            Assert.AreEqual(8, seen);
            host.Session.AdvanceWithoutSnapshot(6_000);
            var performer = host.Session.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Performer && host.Session.CapturePerson(p.AgentId)!.Admitted);
            var tick = host.Session.CurrentTick;
            typeof(GameSession).GetMethod("UpdatePerson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(host.Session,
                [performer.AgentId, (Func<Person, Person>)(p => p with { HealthStage = MedicalStage.Critical, Thirst = 10_000, HeatExposure = 10_000, Intent = MedicalIntent.Collapsed,
                    HealthWarningTick = tick - 4_000, HealthCollapseTick = tick - GameSession.MedicalDeathDelayTicks, HealthCriticalTick = tick - 1 })]);
            for (var frame = 0; frame < 100 && host.Session.PreparedStatus != PreparationStatus.Failed; frame++) host.Advance(0.2, _ => { });
            Assert.AreEqual(PreparationStatus.Failed, host.Session.PreparedStatus);
            Assert.AreEqual("Failure saved.", host.TakeNotice());
            var saved = AutosaveRotation.LoadNewestValid(directory, Compatibility);
            Assert.AreEqual(PreparationStatus.Failed, saved.Session!.PreparedStatus, "The failure checkpoint is durable.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void ManualSaveLoadsBackAndANewCampaignStartsClean()
    {
        var directory = TempDirectory();
        try
        {
            var host = new SessionHost(BuildSession.Ready(), directory, Compatibility);
            Assert.IsFalse(host.LoadManual(out _), "No manual save yet.");
            Assert.IsTrue(host.SaveManual().IsSuccess);
            var saved = host.Session.CaptureSnapshot().AuthoritativeHash;
            Assert.IsTrue(host.Submit(new SetPreparationStockCommand(7, 7, 7)).IsAccepted);
            Assert.IsTrue(host.LoadManual(out var error), error);
            Assert.AreEqual(saved, host.Session.CaptureSnapshot().AuthoritativeHash);
            var fresh = GameSession.CreateBuildCampaign(99);
            host.StartNewCampaign(fresh);
            Assert.AreSame(fresh, host.Session);
            Assert.IsNull(host.SaveError);
        }
        finally { Directory.Delete(directory, true); }
    }
}
