using Festival.Simulation.TrafficProof;

namespace Festival.Tests;

[TestClass]
public sealed class TrafficKernelProofTests
{
    [TestMethod]
    public void SlowLeaderAndFasterFollowerBothComplete()
    {
        var kernel = new TrafficKernel();
        kernel.AddPerson(new(1, new(300, 0), 50, [new(1_600, 0)]));
        kernel.AddPerson(new(2, new(0, 0), 150, [new(1_300, 0)]));
        // Leader needs 26 moving ticks; 80 allows bounded alternating follower grants.
        Assert.AreEqual(TrafficRunOutcome.Completed, AdvanceChecked(kernel, 80), kernel.Serialize());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PerpendicularAndDiagonalCrossingsDischarge(bool diagonal)
    {
        var kernel = new TrafficKernel();
        var aStart = new TrafficPoint(-600, diagonal ? -600 : 0);
        var aEnd = new TrafficPoint(600, diagonal ? 600 : 0);
        var bStart = new TrafficPoint(diagonal ? 600 : 0, -600);
        var bEnd = new TrafficPoint(diagonal ? -600 : 0, 600);
        // Independent feasibility witness: A traverses while B waits at its start;
        // then B traverses while A waits at its end. Either path can be subdivided
        // into <= 100 mm steps, totaling <= 36 ticks for both diagonal paths.
        Assert.IsTrue(OracleMinimumSquaredDistance(aStart, aEnd, bStart, bStart) >= 90_000m);
        Assert.IsTrue(OracleMinimumSquaredDistance(aEnd, aEnd, bStart, bEnd) >= 90_000m);
        kernel.AddPerson(new(1, aStart, 100, [aEnd]));
        kernel.AddPerson(new(2, bStart, 100, [bEnd]));
        Assert.AreEqual(TrafficRunOutcome.Completed, AdvanceChecked(kernel, 80), kernel.Serialize());
    }

    [TestMethod]
    public void WaitingAndInsideClaimsContinueIdenticallyAfterRestore()
    {
        var kernel = Passage();
        kernel.AddPerson(Eastbound(1, -300, 100));
        kernel.AddPerson(Westbound(2, 1_300, 100));
        for (var tick = 0; tick < 4; tick++) AdvanceChecked(kernel, 1);
        Assert.IsTrue(kernel.CaptureSnapshot().People.Any(person => person.RegionPhase == TrafficRegionPhase.Inside));
        Assert.IsTrue(kernel.CaptureSnapshot().People.Any(person => person.RegionPhase == TrafficRegionPhase.Waiting));
        var restored = TrafficKernel.Restore(kernel.Serialize());
        for (var tick = 0; tick < 80; tick++)
        {
            Assert.AreEqual(kernel.ComputeHash(), restored.ComputeHash());
            AdvanceChecked(kernel, 1);
            AdvanceChecked(restored, 1);
        }
        Assert.AreEqual(TrafficRunOutcome.Completed, kernel.RunBounded(0));
        Assert.AreEqual(kernel.ComputeHash(), restored.ComputeHash());
    }

    [TestMethod]
    public void HeldExitDeniesEntryThenReleasedExitPermitsDischarge()
    {
        var kernel = Passage();
        kernel.AddPerson(Eastbound(1, -300, 100));
        kernel.AddPerson(new(2, new(1_300, -600), 100, [new(1_300, -600)]));
        AdvanceChecked(kernel, 12);
        var waiting = kernel.CaptureSnapshot().People.Single(person => person.Id == 1);
        Assert.AreEqual(TrafficRegionPhase.Waiting, waiting.RegionPhase);
        Assert.AreEqual(0, waiting.RouteIndex);
        Assert.AreEqual(0, kernel.CaptureSnapshot().Regions.Single().Occupants.Length);
        var ticket = waiting.RegionTicket;
        kernel.Retarget(2, [new(1_300, -1_200)]);
        Assert.AreEqual(TrafficRunOutcome.Completed, AdvanceChecked(kernel, 60), kernel.Serialize());
        Assert.IsTrue(ticket > 0);
        Assert.AreEqual(1, kernel.CaptureSnapshot().RegionDischarges);
    }

    [TestMethod]
    public void AlreadyReachedWaypointCompletesWithoutMovement()
    {
        var kernel = new TrafficKernel();
        kernel.AddPerson(new(1, new(0, 0), 100, [new(0, 0)]));

        // Exact arrival needs no travel; one tick is sufficient to consume it.
        Assert.AreEqual(TrafficRunOutcome.Completed, AdvanceChecked(kernel, 1), kernel.Serialize());
    }

    [TestMethod]
    public void BlockedExitPreventsEntryEvenWhenOneStepCrossesWholeCore()
    {
        var kernel = Passage();
        kernel.AddPerson(new(1, new(-300, 0), 1_500,
            [new(1_200, 0), new(1_300, -600)], "lane", TrafficDirection.East));
        kernel.AddPerson(new(2, new(1_300, -600), 100, [new(1_300, -600)]));
        var before = kernel.CaptureSnapshot();

        kernel.Advance();

        var after = kernel.CaptureSnapshot();
        AssertTransitionSafe(before, after);
        Assert.AreEqual(before.People[0].Position, after.People[0].Position,
            "An occupied exit must deny a segment that crosses the controlled core, even when its endpoint is outside.");
        Assert.AreEqual(0, after.People[0].RouteIndex, "Denied entry must preserve route progress.");
    }

    [TestMethod]
    public void RestoreRejectsInsidePersonMissingFromRegionOccupants()
    {
        var kernel = Passage();
        kernel.AddPerson(Eastbound(1, -300, 100));
        for (var tick = 0; tick < 3; tick++) kernel.Advance();
        var snapshot = kernel.CaptureSnapshot();
        Assert.AreEqual(TrafficRegionPhase.Inside, snapshot.People.Single().RegionPhase);
        var malformed = snapshot with
        {
            Regions = [snapshot.Regions.Single() with { Occupants = [] }]
        };

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            TrafficKernel.Restore(System.Text.Json.JsonSerializer.Serialize(malformed)));
    }

    [TestMethod]
    public void OpposingPeopleUseFiniteBatchPassageWithSafeSweptMotion()
    {
        var kernel = Passage();
        kernel.AddPerson(Eastbound(1, -300, 100));
        kernel.AddPerson(Westbound(2, 1_600, 100));

        var totalDischarges = 0;
        var outcome = AdvanceChecked(kernel, 80, result => totalDischarges += result.RegionDischarges);

        Assert.AreEqual(TrafficRunOutcome.Completed, outcome, kernel.Serialize());
        Assert.AreEqual(2, totalDischarges);
        var final = kernel.CaptureSnapshot();
        Assert.IsTrue(final.People.All(person => person.RouteIndex == person.Route.Length));
        Assert.IsTrue(final.People.All(person => person.UsefulProgressTicks > 0));
        Assert.AreEqual(0, final.Regions.Single().Occupants.Length);
    }

    private static TrafficKernel Passage(int batchLimit = 1)
    {
        var kernel = new TrafficKernel();
        kernel.AddRegion(new("lane", new(0, -100, 1_000, 100), new(-300, 0), new(1_300, 0),
            new(1_300, -600), new(-300, 600), batchLimit));
        return kernel;
    }

    private static TrafficPersonDefinition Eastbound(ulong id, int startX, int speed) => new(id, new(startX, 0), speed,
        [new(0, 0), new(300, 0), new(600, 0), new(900, 0), new(1_300, -600), new(1_600, -600)], "lane", TrafficDirection.East);

    private static TrafficPersonDefinition Westbound(ulong id, int startX, int speed) => new(id, new(startX, 0), speed,
        [new(1_300, 0), new(1_000, 0), new(700, 0), new(400, 0), new(100, 0), new(-300, 600), new(-600, 600)], "lane", TrafficDirection.West);

    private static TrafficRunOutcome AdvanceChecked(TrafficKernel kernel, int maximumTicks, Action<TrafficTickResult>? inspect = null)
    {
        for (var tick = 0; tick < maximumTicks; tick++)
        {
            var before = kernel.CaptureSnapshot();
            if (before.People.All(person => person.RouteIndex == person.Route.Length)) return TrafficRunOutcome.Completed;
            var result = kernel.Advance();
            var after = kernel.CaptureSnapshot();
            AssertTransitionSafe(before, after);
            inspect?.Invoke(result);
        }
        return kernel.CaptureSnapshot().People.All(person => person.RouteIndex == person.Route.Length)
            ? TrafficRunOutcome.Completed : TrafficRunOutcome.PendingWithinBound;
    }

    private static void AssertTransitionSafe(TrafficKernelSnapshot before, TrafficKernelSnapshot after)
    {
        var prior = before.People.ToDictionary(person => person.Id);
        foreach (var person in after.People)
        {
            var start = prior[person.Id].Position;
            var dx = (long)person.Position.XMillimetres - start.XMillimetres;
            var dz = (long)person.Position.ZMillimetres - start.ZMillimetres;
            Assert.IsTrue(dx * dx + dz * dz <= (long)person.SpeedMillimetresPerTick * person.SpeedMillimetresPerTick,
                $"Person {person.Id} exceeded its tick speed.");
        }
        for (var left = 0; left < after.People.Length; left++)
        for (var right = left + 1; right < after.People.Length; right++)
        {
            var a = after.People[left]; var b = after.People[right];
            Assert.IsTrue(OracleMinimumSquaredDistance(prior[a.Id].Position, a.Position, prior[b.Id].Position, b.Position) >= 90_000m,
                $"Unsafe sweep between {a.Id} and {b.Id}.");
            var dx = (long)a.Position.XMillimetres - b.Position.XMillimetres;
            var dz = (long)a.Position.ZMillimetres - b.Position.ZMillimetres;
            Assert.IsTrue(dx * dx + dz * dz >= 90_000, $"Endpoint separation failed for {a.Id}/{b.Id}.");
        }
    }

    // Independent decimal projection onto the relative segment, rather than the production
    // cross-product comparison. Evaluate endpoints too; all fixture coordinates are small.
    private static decimal OracleMinimumSquaredDistance(TrafficPoint a0, TrafficPoint a1, TrafficPoint b0, TrafficPoint b1)
    {
        decimal x = (decimal)a0.XMillimetres - b0.XMillimetres;
        decimal z = (decimal)a0.ZMillimetres - b0.ZMillimetres;
        decimal dx = (decimal)a1.XMillimetres - a0.XMillimetres - b1.XMillimetres + b0.XMillimetres;
        decimal dz = (decimal)a1.ZMillimetres - a0.ZMillimetres - b1.ZMillimetres + b0.ZMillimetres;
        var length = dx * dx + dz * dz;
        var t = length == 0 ? 0 : Math.Clamp(-(x * dx + z * dz) / length, 0m, 1m);
        return (x + t * dx) * (x + t * dx) + (z + t * dz) * (z + t * dz);
    }
}
