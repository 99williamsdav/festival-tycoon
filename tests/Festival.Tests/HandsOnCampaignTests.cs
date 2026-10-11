using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// The hands-on campaign: the ordinary campaign with the player's own steward on every festival day, under their
/// control through the steward shift's machinery. Classic campaigns and the standalone shift are untouched.
/// </summary>
[TestClass]
public sealed class HandsOnCampaignTests
{
    private const ulong Seed = 20260922;

    /// <summary>A hands-on Tier 1 drafted, laid out, booked, stocked and crewed as the classic tests' campaign is.</summary>
    private static GameSession Ready(ulong seed = Seed, bool handsOn = true)
    {
        var s = handsOn ? GameSession.CreateHandsOnCampaign(seed) : GameSession.CreateBuildCampaign(seed);
        DraftQuiet(s);
        BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        return s;
    }

    /// <summary>Drafts a hiring-slot perk nobody fills, redrawing until it's offered.</summary>
    private static void DraftQuiet(GameSession s)
    {
        var perks = s.CapturePerks()!;
        var redraw = typeof(GameSession).GetMethod("OpenPerkDraft", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string[] quiet = ["extra-pair-of-hands", "doctors-orders"];
        for (var draws = 0; !perks.Hand.Any(quiet.Contains); draws++)
        {
            Assert.IsTrue(draws < 400);
            redraw.Invoke(s, []); perks = s.CapturePerks()!;
        }
        BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand.First(quiet.Contains)));
    }

    private static GameSession Started(ulong seed = Seed)
    {
        var s = Ready(seed);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    private static void RunToEnd(GameSession s)
    {
        for (var i = 0; i < 80 && s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing; i++) s.AdvanceWithoutSnapshot(1_000);
    }

    private static GameSession Fatal(GameSession s)
    {
        s.AdvanceWithoutSnapshot(6_000);
        var performer = s.CapturePreparation()!.People.First(person =>
            person.Role == ProtectedPersonRole.Performer && s.CapturePerson(person.AgentId)!.Admitted);
        var tick = s.CurrentTick;
        typeof(GameSession).GetMethod("UpdatePerson", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(s,
            [performer.AgentId, (Func<Person, Person>)(p => p with
            {
                HealthStage = MedicalStage.Critical, Thirst = 10_000, HeatExposure = 10_000, Intent = MedicalIntent.Collapsed,
                HealthWarningTick = tick - 4_000, HealthCollapseTick = tick - GameSession.MedicalDeathDelayTicks, HealthCriticalTick = tick - 1,
            })]);
        for (var i = 0; i < 6_000 && s.PreparedStatus == PreparationStatus.Running; i++) s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus);
        return s;
    }

    private static void AssertOnShift(GameSession s)
    {
        var me = s.HandsOnStewardId!.Value;
        Assert.AreEqual(me, s.PlayerStewardId, "The player's own steward is the one on shift.");
        Assert.IsFalse(s.IsStewardShift, "A hands-on day isn't the standalone shift.");
        var person = s.CapturePreparation()!.People.Single(p => p.AgentId == me);
        Assert.IsTrue(person is { Role: ProtectedPersonRole.Staff, Admitted: true, Departed: false }, "On duty from the gates opening.");
        Assert.AreEqual(AgentNavigationAction.Idle, s.CaptureSnapshot().NavigationAgents.Single(a => a.Id.Value == me).Action);
    }

    [TestMethod]
    public void AHandsOnCampaignHasThePlayersStewardOnDayOneOnTopOfTheCrewForNothing()
    {
        var ready = Ready();
        Assert.AreEqual(CampaignMode.HandsOn, ready.Mode);
        var me = ready.HandsOnStewardId!.Value;
        Assert.AreEqual(GameSession.HandsOnStewardName, ready.CapturePreparation()!.People.Single(p => p.AgentId == me).Name);
        Assert.IsNull(ready.CaptureStewardShift(), "Nobody's on shift before the gates open.");
        Assert.IsNull(ready.PlayerStewardId);

        // The same plan as a classic campaign costs the same: no wage, no slot, no offer.
        var classic = Ready(handsOn: false);
        Assert.AreEqual(classic.CapturePreparation()!.Payments.Sum(p => p.AmountPennies), ready.CapturePreparation()!.Payments.Sum(p => p.AmountPennies));
        CollectionAssert.AreEqual(classic.CapturePreparation()!.AcceptedOffers, ready.CapturePreparation()!.AcceptedOffers);
        Assert.AreEqual(classic.PreparationRemainingCash, ready.PreparationRemainingCash);

        BuildSession.Accept(ready, new StartPreparedEditionCommand());
        AssertOnShift(ready);
        Assert.AreEqual(classic.GetStewardResponses().Count, ready.GetStewardResponses().Count, "Every hired steward is still there.");
        Assert.IsFalse(ready.GetStewardResponses().Any(job => job.WorkerId == me), "The player is nobody's hire.");
        Assert.IsFalse(ready.GetResponseStaff().Any(profile => profile.AgentId == me));
        BuildSession.Accept(ready, new SetPlayerMoveCommand(0, -1000, false, 180));
        ready.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(GameSession.CreateHandsOnCampaign(Seed).CaptureSnapshot().AuthoritativeHash, GameSession.CreateHandsOnCampaign(Seed).CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void TheModeCarriesThroughNextFestivalAndCallsMountUpUnanswered()
    {
        var s = Started();
        RunToEnd(s);
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        var shift = s.CaptureStewardShift()!;
        Assert.IsNull(shift.PlayerId, "Closing hands the steward back.");
        Assert.IsTrue(shift.Score!.Calls > 0 && shift.Score.Answered == 0, $"Left standing all day, the radio's calls go unanswered: {shift.Score.Calls}.");
        Assert.IsTrue(s.CanStartNextFestival);
        BuildSession.Restored(s);
        var next = s.CreateNextFestival();
        Assert.AreEqual(CampaignMode.HandsOn, next.Mode);
        Assert.AreEqual(2, next.CapturePreparation()!.Tier);
        Assert.IsNotNull(next.HandsOnStewardId);
        Assert.IsNull(next.CaptureStewardShift());
        BuildSession.Restored(next);
    }

    [TestMethod]
    public void ADeathFailsTheFestivalThroughTheHearingAndTheRetryIsStillHandsOn()
    {
        var s = Fatal(Started());
        Assert.IsNull(s.PlayerStewardId, "A death hands the steward back.");
        Assert.IsFalse(s.IsStewardShift);
        Assert.AreEqual(HearingStatus.Open, s.CaptureLifecycleSnapshot()!.Hearings.Single().Status, "The Council's hearing, as in any campaign.");
        Assert.IsFalse(BuildSession.Send(s, new SetPlayerMoveCommand(1000, 0, false, 90)).IsAccepted);
        BuildSession.Restored(s);
        BuildSession.Accept(s, new SpendCouncilFavourCommand());
        Assert.AreEqual(PreparationStatus.Preparing, s.PreparedStatus);
        Assert.AreEqual(CampaignMode.HandsOn, s.Mode);
        Assert.IsNull(s.CaptureStewardShift(), "The retry is the day again, with nobody on shift yet.");
        Assert.IsNotNull(s.HandsOnStewardId);
        BuildSession.Restored(s);
        DraftQuiet(s);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        AssertOnShift(s);
        Assert.AreEqual(2, s.CapturePreparation()!.Attempt);
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void AClassicCampaignHasNoPlayerStewardAndWritesNoMode()
    {
        var s = BuildSession.Started();
        Assert.AreEqual(CampaignMode.Classic, s.Mode);
        Assert.IsNull(s.HandsOnStewardId);
        Assert.IsNull(s.PlayerStewardId);
        Assert.IsNull(s.CaptureStewardShift());
        Assert.IsFalse(s.CapturePreparation()!.People.Any(p => p.Name == GameSession.HandsOnStewardName));
        var json = System.Text.Json.JsonSerializer.Serialize(s.CapturePersistenceSnapshot());
        Assert.IsFalse(json.Contains("\"Mode\"", StringComparison.Ordinal), "A classic save carries no mode.");
        Assert.IsFalse(BuildSession.Send(s, new SetPlayerMoveCommand(1000, 0, false, 90)).IsAccepted);
        // The roster is the classic one, person for person.
        var handsOn = GameSession.CreateHandsOnCampaign(Seed).CapturePreparation()!.People;
        var plain = GameSession.CreateBuildCampaign(Seed).CapturePreparation()!.People;
        CollectionAssert.AreEqual(plain.Select(p => p.AgentId).ToArray(), handsOn.Where(p => p.Name != GameSession.HandsOnStewardName).Select(p => p.AgentId).ToArray());
    }

    [TestMethod]
    public void AMidDaySaveRestoresWithThePlayerStewardAndReplaysTheSame()
    {
        var s = Started();
        BuildSession.Accept(s, new SetPlayerMoveCommand(707, -707, true, 135));
        s.AdvanceWithoutSnapshot(400);
        BuildSession.Accept(s, new SetPlayerMoveCommand(0, 0, false, 135));
        s.AdvanceWithoutSnapshot(3_000);
        var restored = BuildSession.Restored(s);
        AssertOnShift(restored);
        Assert.AreEqual(CampaignMode.HandsOn, restored.Mode);
        foreach (var session in new[] { s, restored })
        {
            BuildSession.Accept(session, new SetPlayerMoveCommand(-1000, 0, false, 270));
            session.AdvanceWithoutSnapshot(500);
        }
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.CaptureSnapshot().AuthoritativeHash);

        // Strict: the mode, the steward and the shift must all agree.
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(saved with { Mode = CampaignMode.Classic }).IsSuccess, "A classic campaign has no player steward on its roster.");
        Assert.IsFalse(GameSession.Restore(saved with { Mode = (CampaignMode)7 }).IsSuccess);
        Assert.AreEqual("A hands-on festival day needs the player's steward.", GameSession.Restore(saved with { StewardShift = null }).Error);
        Assert.AreEqual("The player steward must be the player's own.",
            GameSession.Restore(saved with { StewardShift = saved.StewardShift! with { PlayerId = BuildSession.LastGuest(s) } }).Error);
        var classicSaved = BuildSession.Started().CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(classicSaved with { Mode = CampaignMode.HandsOn }).IsSuccess, "A classic roster isn't a hands-on one.");
    }

    [TestMethod]
    public void TheStandaloneShiftStillWorks()
    {
        var s = GameSession.CreateStewardShift(Seed);
        Assert.IsTrue(s.IsStewardShift);
        Assert.AreEqual(CampaignMode.Classic, s.Mode);
        Assert.IsNull(s.HandsOnStewardId);
        Assert.AreEqual(s.GetStewardResponses()[1].WorkerId, s.PlayerStewardId, "Still the second steward.");
        BuildSession.Accept(s, new SetPlayerMoveCommand(0, -1000, false, 180));
        s.AdvanceWithoutSnapshot(2_000);
        var restored = BuildSession.Restored(s);
        Assert.IsTrue(restored.IsStewardShift);
        Assert.IsFalse(s.CanStartNextFestival);
    }
}
