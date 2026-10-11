using Festival.Persistence;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>The steward shift prototype: the player's steward in the sim, the self-running festival, saves.</summary>
[TestClass]
public sealed class StewardShiftTests
{
    private const ulong Seed = 20260922;

    private static ulong Player(GameSession s) => s.PlayerStewardId!.Value;

    private static (int X, int Z) At(GameSession s, ulong id)
    {
        var agent = s.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == id);
        return (agent.XMillimetres, agent.ZMillimetres);
    }

    private static void Move(GameSession s, int dx, int dz, bool run = false, int facing = 0) =>
        BuildSession.Accept(s, new SetPlayerMoveCommand(dx, dz, run, facing));

    private static int FacingTowards(int dx, int dz) => StewardShiftRules.FacingTowards(dx, dz);

    [TestMethod]
    public void FacingIsWholeDegreesEveryPlatformAgreesOn()
    {
        Assert.AreEqual((0, 1000), StewardShiftRules.Facing(0));
        Assert.AreEqual((1000, 0), StewardShiftRules.Facing(90));
        Assert.AreEqual((0, -1000), StewardShiftRules.Facing(180));
        Assert.AreEqual(90, StewardShiftRules.FacingTowards(5, 0));
        Assert.AreEqual(225, StewardShiftRules.FacingTowards(-3, -3));
        // The sine table is rounded once from doubles: no entry lies near enough a half for an ulp to tip it.
        for (var d = 0; d < 360; d++)
        {
            var scaled = Math.Sin(d * Math.PI / 180) * 1000;
            Assert.IsTrue(Math.Abs(scaled - Math.Floor(scaled) - .5) > 1e-4, $"{d}°");
        }
    }

    [TestMethod]
    public void TheShiftPlansItselfAndHandsThePlayerTheSecondSteward()
    {
        var s = GameSession.CreateStewardShift(Seed);
        Assert.IsTrue(s.IsStewardShift);
        Assert.AreEqual(PreparationStatus.Running, s.PreparedStatus);
        var stewards = s.GetStewardResponses();
        Assert.AreEqual(2, stewards.Count, "Two stewards: the crew's and the player's.");
        Assert.AreEqual(Player(s), stewards[1].WorkerId, "The player is the second steward; the main one keeps the post.");
        Assert.AreEqual(1, s.GetMedicResponses().Count);
        var p = s.CapturePreparation()!;
        CollectionAssert.IsSubsetOf(new[] { "maintenance.worker" }, p.AcceptedOffers);
        Assert.AreEqual(3, s.CaptureProgramme()!.ActIds.Length);
        var me = p.People.Single(person => person.AgentId == Player(s));
        Assert.IsTrue(me.Admitted && !me.Departed && me.Role == ProtectedPersonRole.Staff, "On duty from the first tick.");
        Assert.AreEqual(AgentNavigationAction.Idle, s.CaptureSnapshot().NavigationAgents.Single(a => a.Id.Value == Player(s)).Action);
        Assert.AreEqual(GameSession.CreateStewardShift(Seed).CaptureSnapshot().AuthoritativeHash, s.CaptureSnapshot().AuthoritativeHash, "The same seed plans the same day.");
        // A Tier 2 shift plans both stages.
        var two = GameSession.CreateStewardShift(Seed, 2);
        Assert.AreEqual(2, two.Stages.Count);
        Assert.AreEqual(PreparationStatus.Running, two.PreparedStatus);
    }

    [TestMethod]
    public void MovementIsHeldUntilChangedAndTheSameInputsGiveTheSameDay()
    {
        GameSession Walk()
        {
            var s = GameSession.CreateStewardShift(Seed);
            Move(s, 0, -1000, facing: 180); s.AdvanceWithoutSnapshot(80);
            Move(s, 707, -707, run: true, facing: 135); s.AdvanceWithoutSnapshot(120);
            Move(s, 0, 0, facing: 90); s.AdvanceWithoutSnapshot(80);
            return s;
        }
        var one = Walk(); var two = Walk();
        Assert.AreEqual(one.CaptureSnapshot().AuthoritativeHash, two.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(90, one.CaptureStewardShift()!.FacingDegrees);

        var s = GameSession.CreateStewardShift(Seed);
        var start = At(s, Player(s));
        Move(s, 0, -1000);
        s.AdvanceWithoutSnapshot(10);
        var walked = start.Z - At(s, Player(s)).Z;
        Assert.IsTrue(walked > 0 && walked <= 10 * 35, $"A steward's walk is a few centimetres a tick; went {walked} mm.");
        Assert.AreEqual(start.X, At(s, Player(s)).X);
        var before = At(s, Player(s));
        Move(s, 0, -1000, run: true);
        s.AdvanceWithoutSnapshot(10);
        Assert.IsTrue(before.Z - At(s, Player(s)).Z > walked, "Running is faster.");
        Move(s, 0, 0);
        var stopped = At(s, Player(s));
        s.AdvanceWithoutSnapshot(200);
        Assert.AreEqual(stopped, At(s, Player(s)), "No input, no movement, and nobody else moves them.");
        Assert.AreEqual(CommandReasonCode.InvalidParameter, BuildSession.Send(s, new SetPlayerMoveCommand(1001, 0, false, 0)).ReasonCode);

        // The part of a millimetre each axis misses is carried, so a diagonal keeps a straight walk's pace.
        long Covered(int dx, int dz)
        {
            var g = GameSession.CreateStewardShift(Seed);
            var from = At(g, Player(g));
            Move(g, dx, dz);
            g.AdvanceWithoutSnapshot(40);
            var to = At(g, Player(g));
            return (long)Math.Sqrt((double)(to.X - from.X) * (to.X - from.X) + (double)(to.Z - from.Z) * (to.Z - from.Z));
        }
        var straight = Covered(0, -1000);
        var diagonal = Covered(-707, -707);
        Assert.IsTrue(Math.Abs(straight - diagonal) <= 10, $"Straight {straight} mm, diagonal {diagonal} mm.");
        Assert.AreEqual(CommandReasonCode.InvalidParameter, BuildSession.Send(s, new SetPlayerMoveCommand(0, 0, false, 360)).ReasonCode);
    }

    [TestMethod]
    public void NeverWalksThroughBuildingsHedgesOrTheStage()
    {
        var s = GameSession.CreateStewardShift(Seed);
        var grid = s.TraversalGrid!;
        var id = Player(s);
        // Straight at the nearest blocked ground in the field, then out along each compass point to the hedge.
        var start = At(s, id);
        var startCell = TraversalGrid.WorldToCell(start.X, start.Z);
        var blocked = grid.Overrides.Values.Where(cell => !cell.IsWalkable && StewardShiftRules.InField(cell.Cell))
            .OrderBy(cell => (cell.Cell.X - startCell.X) * (cell.Cell.X - startCell.X) + (cell.Cell.Z - startCell.Z) * (cell.Cell.Z - startCell.Z)).ThenBy(cell => cell.Cell).First().Cell;
        var target = TraversalGrid.CellCentre(blocked);
        var legs = new List<(int X, int Z)> { (target.XMillimetres - start.X, target.ZMillimetres - start.Z) };
        legs.AddRange([(1000, 0), (0, 1000), (-1000, 0), (0, -1000), (707, 707), (-707, -707), (707, -707), (-707, 707)]);
        foreach (var (lx, lz) in legs)
        {
            var length = Math.Sqrt((double)lx * lx + (double)lz * lz);
            Move(s, (int)(lx * 1000 / length), (int)(lz * 1000 / length), run: true);
            var previous = At(s, id);
            var still = 0;
            for (var tick = 0; tick < 1_500; tick++)
            {
                s.AdvanceWithoutSnapshot(1);
                var now = At(s, id);
                var cell = TraversalGrid.WorldToCell(now.X, now.Z);
                Assert.IsTrue(grid.Get(cell).IsWalkable && StewardShiftRules.InField(cell), $"Stood on blocked ground at {cell}.");
                Assert.IsTrue(TraversalSweep.IsWalkable(grid, previous.X, previous.Z, now.X, now.Z), $"Crossed blocked ground {previous}→{now}.");
                still = now == previous ? still + 1 : 0;
                previous = now;
            }
            // Head-on they stop dead; at an angle they may slide along a while.
            if (lx == 0 || lz == 0 || legs.IndexOf((lx, lz)) == 0) Assert.IsTrue(still >= 100, $"Leg ({lx},{lz}) never came up against anything.");
        }
    }

    [TestMethod]
    public void ALooseCowBlocksTheWay()
    {
        var s = GameSession.CreateStewardShift(Seed);
        var id = Player(s);
        // Out into open grass first, then a cow standing 3 m to the west, grazing for the rest of the day.
        var (x, z) = At(s, id);
        var cow = new LooseCow("cow.test", x - 3_000, z, CowActivity.Grazing, [], 0, long.MaxValue);
        var grid = s.TraversalGrid!;
        Assert.IsTrue(grid.Get(TraversalGrid.WorldToCell(x - 3_000, z)).IsWalkable && grid.Get(TraversalGrid.WorldToCell(x - 1_500, z)).IsWalkable);
        typeof(GameSession).GetField("_cows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(s, new CowsSnapshot(1, PastureGateState.Broken, [cow], 1));
        Move(s, -1000, 0, run: true, facing: 270);
        s.AdvanceWithoutSnapshot(400);
        var live = s.CaptureCows()!.Loose.Single();
        var at = At(s, id);
        Assert.IsTrue(at.X > live.XMillimetres, "Never through the cow.");
        Assert.IsTrue(Math.Abs(at.X - live.XMillimetres) >= CowRules.BodyMillimetres, $"Stopped clear of it, {live.XMillimetres - at.X} mm away.");
        Assert.IsTrue(x - at.X > 1_000, "Got up close first.");
    }

    [TestMethod]
    public void NoJobNeedOrBreakEverReachesThePlayerAllDayAndTheCrewStillWorks()
    {
        var s = GameSession.CreateStewardShift(Seed);
        var id = Player(s);
        var others = s.GetResponseStaff().Where(staff => staff.AgentId != id).Select(staff => staff.AgentId).ToArray();
        var crewWorked = false;
        var directions = new[] { (1000, 0), (0, 1000), (-1000, 0), (0, -1000), (0, 0) };
        for (var step = 0; s.PreparedStatus == PreparationStatus.Running; step++)
        {
            if (step % 25 == 0) { var (dx, dz) = directions[step / 25 % directions.Length]; Move(s, dx, dz, facing: FacingTowards(dx, dz)); }
            s.AdvanceWithoutSnapshot(40);
            if (s.PreparedStatus != PreparationStatus.Running) break;
            var nav = s.CaptureSnapshot().NavigationAgents.Single(a => a.Id.Value == id);
            Assert.AreEqual(AgentNavigationAction.Idle, nav.Action, $"Tick {s.CurrentTick}: someone routed the player ({nav.IntentId}).");
            Assert.IsNull(nav.Destination);
            Assert.AreEqual(StewardShiftRules.Intent, nav.IntentId);
            var job = s.GetStewardResponses().Single(item => item.WorkerId == id);
            Assert.IsTrue(job.Stage == SecurityResponseStage.None && job.TargetId is null, $"Tick {s.CurrentTick}: dispatched to {job.TargetId}.");
            Assert.IsFalse(s.CaptureStaffInterventions().Any(item => item.WorkerId == id || item.GuestId == id));
            Assert.AreEqual("Unavailable · On shift under your control.", s.ResponseStaffStatus(id));
            var me = s.CapturePerson(id)!;
            Assert.IsTrue(me.Hunger == 0 && me.ToiletNeed == 0 && me.Intoxication == 0, "No hunger, loo or drink on shift; thirst and heat are their own.");
            Assert.AreEqual(MedicalStage.Clear, me.HealthStage, "However thirsty and hot, the player never collapses.");
            Assert.IsTrue(me.ConductStage == DisorderStage.Calm || me.ConductStage == default, "Nobody picks a fight with them.");
            Assert.AreEqual(PersonClaim.None, s.CaptureClaims(id));
            Assert.IsTrue(me.Intent == MedicalIntent.WatchShow && me.Held is null && me.VendorId is null, "No errand of their own.");
            if (step % 100 == 50) Assert.IsTrue(GameSession.Restore(s.CapturePersistenceSnapshot()).IsSuccess, $"Tick {s.CurrentTick}: the shift's save wouldn't load.");
            crewWorked |= others.Any(other => !s.ResponseStaffStatus(other).StartsWith("Idle", StringComparison.Ordinal) &&
                !s.ResponseStaffStatus(other).Contains("Not physically on duty", StringComparison.Ordinal));
            if (step % 50 == 0 && s.CapturePreparation()!.People.FirstOrDefault(person => person.Role == ProtectedPersonRole.Guest && person.Admitted && !person.Departed) is { } guest)
            {
                Assert.IsFalse(BuildSession.Send(s, new DisorderCommand(DisorderAction.DispatchSecurity, guest.AgentId, id)).IsAccepted, "No order reaches them either.");
                Assert.IsFalse(BuildSession.Send(s, new StaffInterventionCommand(guest.AgentId, id, StaffInterventionAction.GuideToWater)).IsAccepted);
            }
        }
        Assert.IsTrue(crewWorked, "The rest of the crew still took jobs.");
        Assert.AreEqual(PreparationStatus.Departing, s.PreparedStatus);
        Assert.IsNull(s.PlayerStewardId, "Closing ends the shift.");
        Assert.IsTrue(s.IsStewardShift, "The day is still a steward shift.");
        for (var i = 0; i < 40 && s.PreparedStatus == PreparationStatus.Departing; i++) s.AdvanceWithoutSnapshot(1_000);
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus, s.CaptureLifecycleSnapshot()?.Casualties.LastOrDefault()?.Cause);
        Assert.IsTrue(s.CapturePreparation()!.People.Single(person => person.AgentId == id).Departed, "They walk home with everyone else.");
        Assert.IsFalse(s.CanStartNextFestival, "A shift is one day; there's no next tier.");
    }

    [TestMethod]
    public void ASaveMidShiftRestoresAndReplaysIdentically()
    {
        var s = GameSession.CreateStewardShift(Seed);
        Move(s, 1000, 0, facing: 90); s.AdvanceWithoutSnapshot(3_000);
        Move(s, 0, 1000, run: true, facing: 0); s.AdvanceWithoutSnapshot(500);
        var directory = Path.Combine(Path.GetTempPath(), "festival-steward-shift-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var compatibility = new SaveCompatibility("shift", "content", "shift");
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "middle", new(s, compatibility, "test", DateTimeOffset.UtcNow)).IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(directory, "middle", compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            var copy = loaded.Session!;
            Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(s.CaptureStewardShift()), System.Text.Json.JsonSerializer.Serialize(copy.CaptureStewardShift()));
            foreach (var game in new[] { s, copy })
            {
                game.AdvanceWithoutSnapshot(200);
                Move(game, -707, 707, facing: 315); game.AdvanceWithoutSnapshot(1_000);
                Move(game, 0, 0, facing: 10); game.AdvanceWithoutSnapshot(2_000);
            }
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, copy.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(At(s, Player(s)), At(copy, Player(copy)));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void ADeathEndsTheShiftAndItsSaveStillLoads()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(6_000);
        var performer = s.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Performer && s.CapturePerson(person.AgentId)!.Admitted);
        var tick = s.CurrentTick;
        typeof(GameSession).GetMethod("UpdatePerson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(s,
            [performer.AgentId, (Func<Person, Person>)(p => p with
            {
                HealthStage = MedicalStage.Critical, Thirst = 10_000, HeatExposure = 10_000, Intent = MedicalIntent.Collapsed,
                HealthWarningTick = tick - 4_000, HealthCollapseTick = tick - GameSession.MedicalDeathDelayTicks, HealthCriticalTick = tick - 1,
            })]);
        for (var i = 0; i < 6_000 && s.PreparedStatus == PreparationStatus.Running; i++) s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus);
        Assert.IsTrue(s.IsStewardShift && s.PlayerStewardId is null, "A death ends the shift: the end panel shows.");
        Assert.IsFalse(BuildSession.Send(s, new SetPlayerMoveCommand(1000, 0, false, 90)).IsAccepted);
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    [TestMethod]
    public void RestoreRefusesAPlayerStewardHoldingAnyOtherWork()
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        GameSession Shift() { var s = GameSession.CreateStewardShift(Seed); s.AdvanceWithoutSnapshot(3_000); return s; }
        void Person(GameSession s, Func<Person, Person> change) =>
            typeof(GameSession).GetMethod("UpdatePerson", Private)!.Invoke(s, [Player(s), change]);
        void Field(GameSession s, string name, Func<object, object> change)
        {
            var field = typeof(GameSession).GetField(name, Private)!;
            field.SetValue(s, change(field.GetValue(s)!));
        }
        var forgeries = new (string Why, Action<GameSession> Forge)[]
        {
            ("an errand of their own", s => Person(s, p => p with { Intent = MedicalIntent.SeekWater })),
            ("something in hand", s => Person(s, p => p with { Held = new ImmersionHeldItem("forged", ImmersionProduct.Beer, 0) })),
            ("a place at the bar", s => Person(s, p => p with { VendorId = "drinks", Order = ImmersionProduct.Beer })),
            ("the water queue", s => BuildSession.SetTap(s, BuildSession.MainTap(s) with { Queue = [.. BuildSession.MainTap(s).Queue, Player(s)] })),
            ("the bar queue", s => { var bar = s.CaptureVendors().First(v => v.Id == "drinks"); BuildSession.SetVendor(s, bar with { Queue = [.. bar.Queue, Player(s)] }); }),
            ("the loo queue", s => { var loo = s.CaptureToilets()[0]; BuildSession.SetToilet(s, loo with { Queue = [.. loo.Queue, Player(s)] }); }),
            ("a sweep", s => Field(s, "_litter", l => ((LitterSnapshot)l) with { Sweeps = [.. ((LitterSnapshot)l).Sweeps,
                new CleanupSweep(Player(s), new GridCell(120, 150), 2, 5, s.CurrentTick + 800, true)] })),
            ("a bin to empty", s => Field(s, "_litter", l => ((LitterSnapshot)l) with { Sweeps = [.. ((LitterSnapshot)l).Sweeps,
                new CleanupSweep(Player(s), new GridCell(120, 150), 1, 1, s.CurrentTick + 800, true, s.CaptureBins()[0].Id, true)] })),
            ("a cow to herd", s => Field(s, "_cows", c => ((CowsSnapshot)c) with { Gate = PastureGateState.Broken, Escaped = 1,
                Loose = [new LooseCow("cow.forged", 0, 0, CowActivity.Herded, [], 0, long.MaxValue) { HerderId = Player(s), HerdStartedTick = s.CurrentTick }] })),
            ("a steward response", s => Field(s, "_disorder", d => ((DisorderSnapshot)d) with { Stewards = [((DisorderSnapshot)d).Stewards[0],
                ((DisorderSnapshot)d).Stewards[1] with { Stage = SecurityResponseStage.Travelling, TargetId = BuildSession.LastGuest(s), StartedTick = s.CurrentTick, DispatchedTick = s.CurrentTick }] })),
        };
        var failures = new List<string>();
        foreach (var (why, forge) in forgeries)
        {
            var s = Shift();
            forge(s);
            var result = GameSession.Restore(s.CapturePersistenceSnapshot());
            if (result.IsSuccess) failures.Add($"Restored a player steward with {why}.");
            // Each is refused somewhere; the shift's own check names the work even where another check would miss it.
            if (typeof(GameSession).GetMethod("PlayerStewardWorkIssue", Private)!.Invoke(s, []) is null) failures.Add($"The shift check missed {why}.");
        }
        Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
    }

    [TestMethod]
    public void RestoreRejectsAShiftThatCouldNotHappen()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(400);
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsTrue(GameSession.Restore(saved).IsSuccess);
        var shift = saved.StewardShift!;
        var medic = s.GetMedicResponses()[0].WorkerId;
        var guest = s.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Guest).AgentId;
        var blocked = s.TraversalGrid!.Overrides.Values.First(cell => !cell.IsWalkable && StewardShiftRules.InField(cell.Cell)).Cell;
        var (bx, bz) = TraversalGrid.CellCentre(blocked);
        var cases = new (string Why, SessionPersistenceSnapshot Snapshot)[]
        {
            ("a medic", saved with { StewardShift = shift with { PlayerId = medic } }),
            ("the main steward", saved with { StewardShift = shift with { PlayerId = s.GetStewardResponses()[0].WorkerId } }),
            ("nobody on a running first day", saved with { StewardShift = shift with { PlayerId = null, MoveX = 0, MoveZ = 0, Run = false, RemainderX = 0, RemainderZ = 0 } }),
            ("no second steward's perk", saved with { Perks = saved.Perks! with { Equipped = [] } }),
            ("a remainder past a millimetre", saved with { StewardShift = shift with { RemainderX = 1_000 } }),
            ("a guest", saved with { StewardShift = shift with { PlayerId = guest } }),
            ("nobody", saved with { StewardShift = shift with { PlayerId = 999_999 } }),
            ("a later version", saved with { StewardShift = shift with { Version = StewardShiftRules.Version + 1 } }),
            ("facing", saved with { StewardShift = shift with { FacingDegrees = 360 } }),
            ("input", saved with { StewardShift = shift with { MoveX = 1001 } }),
            ("input after the shift", saved with { StewardShift = shift with { PlayerId = null, MoveX = 1000 } }),
            ("blocked ground", saved with { NavigationAgents = saved.NavigationAgents!.Select(a => a.Id == shift.PlayerId ? a with { XMillimetres = bx, ZMillimetres = bz } : a).ToArray() }),
            ("off the field", saved with { NavigationAgents = saved.NavigationAgents!.Select(a => a.Id == shift.PlayerId ? a with { XMillimetres = 40_000 } : a).ToArray() }),
            ("someone else's route", saved with { NavigationAgents = saved.NavigationAgents!.Select(a => a.Id == shift.PlayerId ? a with { IntentId = "staff.return-to-post" } : a).ToArray() }),
        };
        foreach (var (why, snapshot) in cases)
        {
            var result = GameSession.Restore(snapshot);
            Assert.IsFalse(result.IsSuccess, $"Restored a shift with {why}.");
            // Taking the perk away is caught by the perks' own check first.
            if (!why.Contains("perk")) StringAssert.Contains(result.Error, "steward", StringComparison.OrdinalIgnoreCase, why);
        }
    }

    [TestMethod]
    public void OrdinaryCampaignsCarryNoShift()
    {
        var s = BuildSession.Started();
        s.AdvanceWithoutSnapshot(400);
        Assert.IsFalse(s.IsStewardShift);
        Assert.IsNull(s.PlayerStewardId);
        var json = System.Text.Json.JsonSerializer.Serialize(s.CapturePersistenceSnapshot());
        Assert.IsFalse(json.Contains("StewardShift", StringComparison.Ordinal), "An ordinary save has no new JSON.");
        Assert.AreEqual(CommandReasonCode.WrongPhase, BuildSession.Send(s, new SetPlayerMoveCommand(1000, 0, false, 90)).ReasonCode);
        Assert.AreEqual(0, s.StewardReach(0, 100_000).Count);
    }

    [TestMethod]
    public void ReachFindsWhoIsInFrontBestActionFirst()
    {
        var s = GameSession.CreateStewardShift(Seed);
        var id = Player(s);
        s.AdvanceWithoutSnapshot(2_000);
        var me = At(s, id);
        var people = s.CapturePreparation()!.People.Where(person => person.AgentId != id && person.Admitted && !person.Departed).ToArray();
        var nearest = people.Select(person => (person, at: At(s, person.AgentId)))
            .OrderBy(item => (long)(item.at.X - me.X) * (item.at.X - me.X) + (long)(item.at.Z - me.Z) * (item.at.Z - me.Z)).First();
        var facing = FacingTowards(nearest.at.X - me.X, nearest.at.Z - me.Z);
        var reach = s.StewardReach(facing, 100_000);
        var person = reach.Single(item => item.PersonId == nearest.person.AgentId);
        Assert.AreEqual($"person:{nearest.person.AgentId}", person.TargetId);
        Assert.AreEqual(nearest.person.Name, person.Name);
        Assert.AreEqual(StewardTargetKind.Person, person.Kind);
        // Something to do comes first, best action first; then nearest.
        var keys = reach.Select(item => (Rank: item.Action is { } action ? (int)action : int.MaxValue, item.DistanceMillimetres)).ToArray();
        CollectionAssert.AreEqual(keys.OrderBy(key => key.Rank).ThenBy(key => key.DistanceMillimetres).ToArray(), keys);
        Assert.IsFalse(s.StewardReach((facing + 180) % 360, 100_000).Any(item => item.PersonId == nearest.person.AgentId), "Not behind them.");
        Assert.IsFalse(reach.Any(item => item.PersonId == id));
        Assert.IsFalse(BuildSession.Send(s, new StewardActionCommand(StewardActionKind.Calm, "person:999999", facing)).IsAccepted, "Nobody like that.");
        Assert.IsFalse(BuildSession.Send(s, new StewardActionCommand(StewardActionKind.Release, "", facing)).IsAccepted, "Nothing held to let go of.");
    }
}
