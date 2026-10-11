using System.Collections;
using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// The first-person radio menu's jobs (the medic, Dirty Henry, a named crew member), each through the manager's own
/// dispatch and refused when it can't go; and the held site plan, which slows the walk, stops running and the hands,
/// and is saved and replayed. Forged saves are refused.
/// </summary>
[TestClass]
public sealed class StewardRadioTests
{
    private const ulong Seed = 20260922;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static object Call(GameSession s, string method, params object[] args) => typeof(GameSession).GetMethod(method, Private)!.Invoke(s, args)!;
    private static void Forge(GameSession s, ulong id, Func<Person, Person> change) => Call(s, "UpdatePerson", id, change);

    /// <summary>A hands-on Tier 1, crewed, with the player's steward taken over (first person).</summary>
    private static GameSession Started(bool faults = false)
    {
        var s = GameSession.CreateHandsOnCampaign(Seed);
        var perks = s.CapturePerks()!;
        string[] quiet = ["extra-pair-of-hands", "doctors-orders"];
        for (var draws = 0; !perks.Hand.Any(quiet.Contains); draws++)
        {
            Assert.IsTrue(draws < 400);
            Call(s, "OpenPerkDraft"); perks = s.CapturePerks()!;
        }
        BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand.First(quiet.Contains)));
        BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        if (!faults) BuildSession.WithoutFaults(s);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        BuildSession.Accept(s, new SetPlayerStewardControlCommand(false));
        return s;
    }

    private static StewardShiftSnapshot Shift(GameSession s) => s.CaptureStewardShift()!;
    private static ulong Me(GameSession s) => s.PlayerStewardId!.Value;

    private static (int X, int Z) At(GameSession s, ulong id)
    {
        var agent = s.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == id);
        return (agent.XMillimetres, agent.ZMillimetres);
    }

    private static void Teleport(GameSession s, int x, int z)
    {
        var agents = (IDictionary)typeof(GameSession).GetField("_navigationAgents", Private)!.GetValue(s)!;
        var nav = agents[new EntityId(Me(s))]!;
        void Set(string name, object value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
        Set("XMillimetres", x); Set("ZMillimetres", z); Set("SegmentOriginXMillimetres", x); Set("SegmentOriginZMillimetres", z);
    }

    private static ulong StandingGuest(GameSession s, Func<ulong, bool>? also = null)
    {
        var agents = s.CaptureObservation().NavigationAgents.ToDictionary(a => a.Id.Value);
        return s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).Select(p => p.AgentId)
            .Where(id => agents[id].Action != AgentNavigationAction.Travelling && StewardShiftRules.InField(TraversalGrid.WorldToCell(agents[id].XMillimetres, agents[id].ZMillimetres)) &&
                s.CapturePerson(id) is { ConductStage: DisorderStage.Calm, Intent: MedicalIntent.WatchShow, VendorId: null, ToiletStage: ToiletVisitStage.None, HealthStage: MedicalStage.Clear } &&
                (also?.Invoke(id) ?? true))
            .OrderBy(id => id).First();
    }

    private static void Collapse(GameSession s, ulong guest)
    {
        var tick = s.CurrentTick;
        Forge(s, guest, p => p with { HealthStage = MedicalStage.Collapsed, Intent = MedicalIntent.Collapsed, HealthWarningTick = tick - 1, HealthCollapseTick = tick });
    }

    private static void Argue(GameSession s, ulong id) =>
        Forge(s, id, p => p with { ConductStage = DisorderStage.Argument, Pressure = 6_000, Temperament = 2_000, Grievance = DisorderGrievance.QueueWait,
            ConductStageTick = s.CurrentTick, CooldownUntilTick = 0 });

    private static CommandResult Radio(GameSession s, StewardRadioJob job, string target, ulong? worker = null) =>
        BuildSession.Send(s, new StewardRadioCommand(job, target, worker));

    [TestMethod]
    public void TheMedicGoesThroughTheManagersDispatchAndIsRefusedWhenThereIsNobodyToSend()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(9_000);
        var guest = StandingGuest(s);
        Assert.IsFalse(Radio(s, StewardRadioJob.Medic, $"person:{guest}").IsAccepted, "Nobody's down.");
        Collapse(s, guest);
        s.AdvanceWithoutSnapshot(1);
        var call = Shift(s).Calls!.Single(c => c.Open && c.Kind == StewardCallKind.Collapse);
        Assert.AreEqual($"person:{guest}", s.StewardRadioTarget(StewardRadioJob.Medic, 0, call.Id), "The current call is the target.");
        Assert.IsFalse(Radio(s, StewardRadioJob.Medic, $"person:{guest}", Me(s)).IsAccepted, "The medic isn't radioed by name.");
        Assert.IsNull(s.StewardRadioIssue(StewardRadioJob.Medic, $"person:{guest}"));
        BuildSession.Accept(s, new StewardRadioCommand(StewardRadioJob.Medic, $"person:{guest}"));
        Assert.IsTrue(s.GetMedicResponses().Any(m => m.PatientId == guest), "The manager's dispatch sent the medic.");
        Assert.AreEqual(1, Shift(s).Score!.RadioJobs);
        Assert.AreEqual(-1, Shift(s).Calls!.Single(c => c.Id == call.Id).AnsweredTick, "Radioing from afar sends help; it isn't answering the call yourself.");
        var again = Radio(s, StewardRadioJob.Medic, $"person:{guest}");
        Assert.IsFalse(again.IsAccepted);
        Assert.AreEqual("The medic's already with them.", again.Message);
        Assert.AreEqual(1, Shift(s).Score!.RadioJobs);
        // A second collapse: with the medic already at the first, the menu points at the second (busy, not "already there").
        var second = StandingGuest(s, id => id != guest);
        Collapse(s, second);
        s.AdvanceWithoutSnapshot(StewardShiftRules.RadioDelayTicks(StewardCallKind.Collapse) + 1);
        Assert.AreEqual($"person:{second}", s.StewardRadioTarget(StewardRadioJob.Medic, 0, call.Id));
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void HenryIsTheManagersTankerCallAndOneLooAtATime()
    {
        var s = Started();
        ToiletFacility? used = null;
        for (var i = 0; i < 200 && used is null; i++)
        {
            s.AdvanceWithoutSnapshot(80);
            used = s.CaptureToilets().FirstOrDefault(t => t.UsedMillilitres > 0);
        }
        Assert.IsNotNull(used);
        var cash = s.CaptureLavSucker()!.Calls.Length;
        Assert.IsFalse(Radio(s, StewardRadioJob.Henry, $"tap:{s.CaptureWaterPoints()[0].Id}").IsAccepted, "Henry only empties loos.");
        Assert.IsNotNull(s.StewardRadioTarget(StewardRadioJob.Henry, 0), "With nothing in sight, the nearest loo that needs him.");
        BuildSession.Accept(s, new StewardRadioCommand(StewardRadioJob.Henry, $"toilet:{used.Id}"));
        Assert.AreEqual(cash + 1, s.CaptureLavSucker()!.Calls.Length);
        Assert.AreEqual(used.Id, s.CaptureLavSucker()!.Calls[^1].ToiletId);
        var again = Radio(s, StewardRadioJob.Henry, $"toilet:{used.Id}");
        Assert.IsFalse(again.IsAccepted, "No queue behind his own job: refused.");
        Assert.AreEqual(CommandReasonCode.AlreadyCommitted, again.ReasonCode, "Refused as busy.");
        Assert.AreEqual("Dav's already on the way.", again.Message);
        Assert.AreEqual(1, Shift(s).Score!.RadioJobs);
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void TheCrewAreSentByNameThroughTheManagersPathsAndRefusedWhenBusyOrWrong()
    {
        var s = Started(faults: true);
        s.AdvanceWithoutSnapshot(9_000);
        var crew = s.StewardRadioCrew();
        Assert.IsTrue(crew.Count >= 1, "Tier 1's hired steward is on the radio.");
        Assert.IsFalse(crew.Any(member => member.WorkerId == Me(s)), "Never the player's own steward.");
        var steward = crew.First(member => !member.Maintenance);
        Assert.IsNull(steward.Busy);

        // An argument: the steward goes as the manager's Send a steward sends them.
        var guest = StandingGuest(s);
        Argue(s, guest);
        Assert.IsFalse(Radio(s, StewardRadioJob.Crew, $"person:{guest}").IsAccepted, "Nobody named.");
        Assert.IsFalse(Radio(s, StewardRadioJob.Crew, $"person:{guest}", Me(s)).IsAccepted, "Not your own steward.");
        Assert.IsFalse(Radio(s, StewardRadioJob.Crew, $"person:{StandingGuest(s, id => id != guest)}", steward.WorkerId).IsAccepted, "Nothing going on there.");
        BuildSession.Accept(s, new StewardRadioCommand(StewardRadioJob.Crew, $"person:{guest}", steward.WorkerId));
        Assert.AreEqual(guest, s.GetStewardResponses().Single(job => job.WorkerId == steward.WorkerId).TargetId);
        Assert.IsNotNull(s.StewardRadioCrew().Single(member => member.WorkerId == steward.WorkerId).Busy);

        // Busy now: a loose cow isn't queued behind the job; it's refused, as busy.
        typeof(GameSession).GetField("_cows", Private)!.SetValue(s, Call(s, "BreakGate", s.CaptureCows()!, s.CapturePreparation()!));
        s.AdvanceWithoutSnapshot(1);
        var cow = s.CaptureCows()!.Loose[0].Id;
        Assert.AreEqual(1, s.StewardRadioCrew().Count(member => !member.Maintenance), "Tier 1 hires one steward.");
        {
            var busy = Radio(s, StewardRadioJob.Crew, $"cow:{cow}", steward.WorkerId);
            Assert.IsFalse(busy.IsAccepted);
            Assert.AreEqual(CommandReasonCode.AlreadyCommitted, busy.ReasonCode);
        }
        Assert.AreEqual(1, Shift(s).Score!.RadioJobs);
        BuildSession.Restored(s);

        // Once free again, the cow: herded by the steward named, through the manager's herd order.
        for (var i = 0; i < 6_000 && s.StewardRadioCrew().Single(member => member.WorkerId == steward.WorkerId).Busy is not null; i++) s.AdvanceWithoutSnapshot(8);
        var loose = s.CaptureCows()!.Loose.FirstOrDefault(c => c.HerderId is null);
        Assert.IsNotNull(loose, "A cow is still loose.");
        Assert.IsNull(s.StewardRadioCrew().Single(member => member.WorkerId == steward.WorkerId).Busy, "The steward came free.");
        {
            BuildSession.Accept(s, new StewardRadioCommand(StewardRadioJob.Crew, $"cow:{loose.Id}", steward.WorkerId));
            Assert.AreEqual(steward.WorkerId, s.CaptureCows()!.Loose.Single(c => c.Id == loose.Id).HerderId);
            Assert.AreEqual(2, Shift(s).Score!.RadioJobs);
        }
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void AJammedLooGoesToTheNamedStewardAsTheFaultRuleWouldSend()
    {
        var s = Started(faults: true);
        ToiletFacility? loo = null;
        for (var i = 0; i < 400 && loo is null; i++)
        {
            s.AdvanceWithoutSnapshot(40);
            loo = s.CaptureToilets().FirstOrDefault(t => t.OwnerId is { } owner && s.CapturePerson(owner)!.ToiletStage == ToiletVisitStage.Using && s.ActiveFault(t.Id) is null);
        }
        Assert.IsNotNull(loo);
        var steward = s.StewardRadioCrew().First(member => !member.Maintenance && member.Busy is null);
        Call(s, "AddFault", "stuck:test", FacilityFaultKind.StuckInToilet, loo.Id, loo.OwnerId!.Value);
        var maintenance = s.StewardRadioCrew().FirstOrDefault(member => member.Maintenance);
        if (maintenance is not null)
            Assert.IsFalse(Radio(s, StewardRadioJob.Crew, $"toilet:{loo.Id}", maintenance.WorkerId).IsAccepted, "A jammed loo is a steward's.");
        BuildSession.Accept(s, new StewardRadioCommand(StewardRadioJob.Crew, $"toilet:{loo.Id}", steward.WorkerId));
        Assert.AreEqual(steward.WorkerId, s.ActiveFault(loo.Id)!.WorkerId);
        var twice = Radio(s, StewardRadioJob.Crew, $"toilet:{loo.Id}", steward.WorkerId);
        Assert.IsFalse(twice.IsAccepted, "Already on it.");
        s.AdvanceWithoutSnapshot(1);
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void TheRadioIsTheFirstPersonPlayersOnly()
    {
        // Left to themselves (the manager's view), the steward doesn't use the radio menu.
        var s = Started();
        s.AdvanceWithoutSnapshot(9_000);
        var guest = StandingGuest(s);
        Collapse(s, guest);
        s.AdvanceWithoutSnapshot(1);
        BuildSession.Accept(s, new SetPlayerStewardControlCommand(true));
        Assert.IsFalse(Radio(s, StewardRadioJob.Medic, $"person:{guest}").IsAccepted);
        BuildSession.Accept(s, new SetPlayerStewardControlCommand(false));
        // With the plan up, the radio waits until it's away.
        BuildSession.Accept(s, new SetSitePlanCommand(true));
        Assert.AreEqual("Put the site plan away first.", Radio(s, StewardRadioJob.Medic, $"person:{guest}").Message);
        BuildSession.Accept(s, new SetSitePlanCommand(false));
        Assert.IsFalse(Radio(s, (StewardRadioJob)9, $"person:{guest}").IsAccepted);
        Assert.IsFalse(Radio(s, StewardRadioJob.Medic, "").IsAccepted);
        BuildSession.Accept(s, new StewardRadioCommand(StewardRadioJob.Medic, $"person:{guest}"));

        // A classic campaign has nobody on shift.
        var classic = BuildSession.Started();
        classic.AdvanceWithoutSnapshot(2_000);
        Assert.AreEqual(CommandReasonCode.WrongPhase, Radio(classic, StewardRadioJob.Henry, "toilet:toilet.main").ReasonCode);
        Assert.AreEqual(CommandReasonCode.WrongPhase, BuildSession.Send(classic, new SetSitePlanCommand(true)).ReasonCode);
    }

    [TestMethod]
    public void StewardOnlyRadiosTheMedicAndHenryButNotTheCrew()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        Assert.AreEqual(0, s.StewardRadioCrew().Count, "Control runs the crew on the standalone shift.");
        var hired = s.GetStewardResponses().First(job => job.WorkerId != s.PlayerStewardId).WorkerId;
        var guest = StandingGuest(s);
        Argue(s, guest);
        Assert.AreEqual("Control runs the crew on this shift.", Radio(s, StewardRadioJob.Crew, $"person:{guest}", hired).Message);
        var down = StandingGuest(s, id => id != guest);
        Collapse(s, down);
        s.AdvanceWithoutSnapshot(1);
        Assert.IsFalse(s.GetMedicResponses().Any(m => m.PatientId == down), "The shift's medic waits for the radio.");
        BuildSession.Accept(s, new StewardRadioCommand(StewardRadioJob.Medic, $"person:{down}"));
        Assert.IsTrue(s.GetMedicResponses().Any(m => m.PatientId == down));
        var loo = s.CaptureToilets().OrderByDescending(t => t.UsedMillilitres).ThenBy(t => t.Id, StringComparer.Ordinal).First();
        Assert.IsTrue(loo.UsedMillilitres > 0);
        BuildSession.Accept(s, new StewardRadioCommand(StewardRadioJob.Henry, $"toilet:{loo.Id}"));
        Assert.AreEqual(2, Shift(s).Score!.RadioJobs);
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void ThePlanUpSlowsTheWalkStopsTheHandsAndIsSavedAndReplayed()
    {
        // Two identical days: one walks (asking to run) with the plan up, one walks plainly.
        int Walked(bool plan)
        {
            var s = Started();
            s.AdvanceWithoutSnapshot(400);
            if (plan) BuildSession.Accept(s, new SetSitePlanCommand(true));
            var from = At(s, Me(s));
            BuildSession.Accept(s, new SetPlayerMoveCommand(0, -1000, plan, 180));
            s.AdvanceWithoutSnapshot(40);
            var to = At(s, Me(s));
            return Math.Abs(to.Z - from.Z) + Math.Abs(to.X - from.X);
        }
        var walk = Walked(false);
        var slow = Walked(true);
        Assert.IsTrue(walk > 1_000, $"Walked {walk} mm.");
        Assert.IsTrue(Math.Abs(slow * 1000L / walk - StewardRadioRules.PlanWalkPermille) <= 20, $"Plan up: {slow} mm against {walk} mm walking, and no running.");

        // Up, the hands wait: no action, and a held one is let go.
        var s = Started();
        s.AdvanceWithoutSnapshot(9_000);
        BuildSession.Accept(s, new SetSitePlanCommand(true));
        Assert.AreEqual(CommandReasonCode.AlreadyCommitted, BuildSession.Send(s, new SetSitePlanCommand(true)).ReasonCode);
        var guest = StandingGuest(s);
        Argue(s, guest);
        var at = At(s, guest);
        Teleport(s, at.X - 1_200, at.Z);
        var reach = s.StewardReach(90).FirstOrDefault(item => item.PersonId == guest);
        Assert.IsTrue(reach?.Action is not null, "The arguing guest is in reach.");
        var action = reach!.Action!.Value;
        {
            Assert.AreEqual("Put the site plan away first.", BuildSession.Send(s, new StewardActionCommand(action, reach.TargetId, 90)).Message);
            BuildSession.Accept(s, new SetSitePlanCommand(false));
            BuildSession.Accept(s, new StewardActionCommand(action, reach.TargetId, 90));
            BuildSession.Accept(s, new SetSitePlanCommand(true));
            Assert.IsNull(Shift(s).Action, "Getting the plan out lets go.");
        }
        // Back to the manager's view puts it away.
        BuildSession.Accept(s, new SetPlayerStewardControlCommand(true));
        Assert.IsFalse(Shift(s).PlanUp);
        Assert.AreEqual(CommandReasonCode.WrongPhase, BuildSession.Send(s, new SetSitePlanCommand(true)).ReasonCode, "Not while they work by themselves.");

        // Saved and replayed: the same inputs, with a save and load in the middle, give the same day.
        GameSession Play(bool restoreMidway)
        {
            var day = Started();
            day.AdvanceWithoutSnapshot(2_000);
            BuildSession.Accept(day, new SetSitePlanCommand(true));
            BuildSession.Accept(day, new SetPlayerMoveCommand(700, -700, true, 135));
            day.AdvanceWithoutSnapshot(300);
            if (restoreMidway) day = BuildSession.Restored(day);
            day.AdvanceWithoutSnapshot(300);
            BuildSession.Accept(day, new SetSitePlanCommand(false));
            day.AdvanceWithoutSnapshot(300);
            BuildSession.Accept(day, new SetSitePlanCommand(true));
            return day;
        }
        var one = Play(false);
        var two = Play(true);
        Assert.AreEqual(one.CaptureSnapshot().AuthoritativeHash, two.CaptureSnapshot().AuthoritativeHash);
        var saved = one.CapturePersistenceSnapshot();
        Assert.IsTrue(saved.StewardShift!.PlanUp);
        Assert.IsTrue(System.Text.Json.JsonSerializer.Serialize(saved.StewardShift).Contains("\"PlanUp\":true", StringComparison.Ordinal));
        Assert.IsTrue(GameSession.Restore(saved).IsSuccess);

        // Forged saves are refused: the plan with nobody's hands to hold it, with the steward working alone, or with an action held.
        Assert.AreEqual("Steward shift site plan invalid.", GameSession.Restore(saved with { StewardShift = saved.StewardShift with { Autopilot = new() } }).Error);
        var held = new StewardActionProgress(StewardActionKind.PickLitter, "litter:x", saved.CurrentTick, StewardShiftRules.DurationTicks(StewardActionKind.PickLitter));
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift with { Action = held } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift with { PlayerId = null, MoveX = 0, MoveZ = 0, Run = false, RemainderX = 0, RemainderZ = 0 } }).IsSuccess);
        Assert.AreEqual("Steward shift score out of bounds.", GameSession.Restore(saved with { StewardShift = saved.StewardShift with { Score = saved.StewardShift.Score! with { RadioJobs = -1 } } }).Error);
    }

    [TestMethod]
    public void TheStandaloneShiftsPlanIsSavedToo()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(400);
        BuildSession.Accept(s, new SetSitePlanCommand(true));
        s.AdvanceWithoutSnapshot(80);
        var loaded = BuildSession.Restored(s);
        Assert.IsTrue(loaded.CaptureStewardShift()!.PlanUp);
    }
}
