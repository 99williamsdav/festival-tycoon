using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// The walk-out after the close (13 Oct): the day finishes when the last guest is off site. Stewards (the player's
/// own included) and the medic stay on duty until then; whoever is still on site at the finish goes with the results.
/// </summary>
[TestClass]
public sealed class WalkOutTests
{
    private const int DepartureBudgetTicks = 3_000;

    private static void Invoke(GameSession s, string name, params object[] args) =>
        typeof(GameSession).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(s, args);

    /// <summary>A Tier 1 day in each way to play, started: Management Only, All-Rounder or Steward Only.</summary>
    private static GameSession Started(string mode, ulong seed)
    {
        if (mode == "steward-only") return GameSession.CreateStewardShift(seed);
        GameSession s;
        if (mode == "all-rounder")
        {
            s = GameSession.CreateHandsOnCampaign(seed);
            var perks = s.CapturePerks()!;
            var redraw = typeof(GameSession).GetMethod("OpenPerkDraft", BindingFlags.NonPublic | BindingFlags.Instance)!;
            string[] quiet = ["extra-pair-of-hands", "doctors-orders"];
            for (var draws = 0; !perks.Hand.Any(quiet.Contains); draws++)
            {
                Assert.IsTrue(draws < 400);
                redraw.Invoke(s, []); perks = s.CapturePerks()!;
            }
            BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand.First(quiet.Contains)));
            BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
            BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
            BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
            foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        }
        else
        {
            s = BuildSession.Planned(seed);
            foreach (var id in BuildSession.CrewIds(s).Append("equipment.rent")) BuildSession.Accept(s, new AcceptPreparationOfferCommand(id));
        }
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    private static long RunToClose(GameSession s)
    {
        s.AdvanceWithoutSnapshot((int)(s.CapturePreparation()!.StartedTick + s.PreparedEditionDurationTicks - s.CurrentTick));
        Assert.AreEqual(PreparationStatus.Departing, s.PreparedStatus);
        return s.CurrentTick;
    }

    private static bool GuestsOnSite(GameSession s) =>
        s.CapturePreparation()!.People.Any(person => person.Role == ProtectedPersonRole.Guest && !person.Departed);

    [TestCategory("Slow")]
    [TestMethod]
    [DataRow("management-only")]
    [DataRow("all-rounder")]
    [DataRow("steward-only")]
    public void StewardsAndTheMedicStayUntilTheLastGuestIsOut(string mode)
    {
        var s = Started(mode, 20260922);
        var player = mode == "all-rounder" ? s.HandsOnStewardId : s.PlayerStewardId;
        if (mode != "management-only") Assert.IsNotNull(player);
        var close = RunToClose(s);
        var stewards = s.GetStewardResponses().Select(job => job.WorkerId).ToArray();
        var onDuty = stewards.Concat(s.GetMedicResponses().Select(job => job.WorkerId)).Concat(player is { } me ? [me] : []).Distinct().ToArray();
        if (mode == "steward-only") CollectionAssert.Contains(stewards, player!.Value, "Steward Only's player is one of the hired stewards.");
        Assert.IsTrue(onDuty.Length >= 2);
        if (player is not null)
        {
            // The shift itself still ends at the close: control goes back, the report reads the close.
            var shift = s.CaptureStewardShift()!;
            Assert.IsNull(shift.PlayerId);
            Assert.AreEqual(close, shift.EndedTick);
            Assert.IsNotNull(shift.ClosingMood);
        }
        var standing = player is { } p0 ? s.CaptureSnapshot().NavigationAgents.Single(n => n.Id.Value == p0).Destination : null;
        while (s.CurrentTick - close < DepartureBudgetTicks && s.PreparedStatus == PreparationStatus.Departing)
        {
            s.AdvanceWithoutSnapshot(1);
            if (s.PreparedStatus != PreparationStatus.Departing) break;
            var people = s.CapturePreparation()!.People;
            var navs = s.CaptureSnapshot().NavigationAgents;
            foreach (var id in onDuty)
            {
                Assert.IsFalse(people.Single(person => person.AgentId == id).Departed, $"Staff {id} left at +{s.CurrentTick - close} with guests still on site.");
                var intent = navs.Single(n => n.Id.Value == id).IntentId;
                Assert.IsTrue(intent is "disorder.departure-standby" or "medical.departure-standby", $"Staff {id} is on '{intent}' at +{s.CurrentTick - close}.");
            }
            // The player's own steward stands where control went back.
            if (mode == "all-rounder" && s.CurrentTick - close > 1)
                Assert.AreEqual(standing ??= navs.Single(n => n.Id.Value == player!.Value).Destination, navs.Single(n => n.Id.Value == player!.Value).Destination);
        }
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        Assert.IsTrue(s.CapturePreparation()!.People.All(person => person.Departed));
        Assert.AreEqual(close, s.CaptureStewardShift()?.EndedTick ?? close);
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void AGuestCollapsingOnTheWayOutHoldsTheDayOpenUntilTheyAreSeenTo()
    {
        var s = Started("management-only", 2);
        var close = RunToClose(s);
        // Late in the walk-out, with only a few guests left: one of them goes down.
        while (s.CapturePreparation()!.People.Count(person => person.Role == ProtectedPersonRole.Guest && !person.Departed) > 3)
        {
            Assert.IsTrue(s.CurrentTick - close < DepartureBudgetTicks);
            s.AdvanceWithoutSnapshot(1);
        }
        var guest = s.CapturePreparation()!.People.Last(person => person.Role == ProtectedPersonRole.Guest && !person.Departed).AgentId;
        Invoke(s, "UpdatePerson", guest, (Func<Person, Person>)(need => need with
            { HealthStage = MedicalStage.Distress, HealthWarningTick = s.CurrentTick - GameSession.MedicalCollapseDelayTicks }));
        var collapses = s.CapturePreparation()!.GuestMedicalCollapses;
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Collapsed, s.CapturePerson(guest)!.HealthStage);
        Assert.AreEqual(collapses + 1, s.CapturePreparation()!.GuestMedicalCollapses);
        var medicCame = false;
        var othersOutWhileDown = false;
        long guestOut = -1;
        for (var tick = 0; tick < DepartureBudgetTicks && s.PreparedStatus == PreparationStatus.Departing; tick++)
        {
            s.AdvanceWithoutSnapshot(1);
            medicCame |= s.GetMedicResponses().Any(job => job.PatientId == guest);
            var people = s.CapturePreparation()!.People;
            var down = !people.Single(person => person.AgentId == guest).Departed;
            othersOutWhileDown |= down && people.All(person => person.Role != ProtectedPersonRole.Guest || person.AgentId == guest || person.Departed);
            if (down) Assert.AreEqual(PreparationStatus.Departing, s.PreparedStatus, "The day can't finish with a guest down on site.");
            else if (guestOut < 0) guestOut = s.CurrentTick;
        }
        Assert.IsTrue(medicCame, "The medic should come to the collapsed guest.");
        Assert.IsTrue(othersOutWhileDown, "The other guests should be out while this one was still being seen to.");
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        Assert.IsTrue(s.CapturePerson(guest)!.HealthStage is MedicalStage.Treated or MedicalStage.Removed, $"{s.CapturePerson(guest)!.HealthStage}");
        Assert.AreEqual(guestOut + 1, s.CurrentTick, "The day finishes the tick after the last guest is off site.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }
}
