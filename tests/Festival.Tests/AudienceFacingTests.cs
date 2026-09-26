using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class AudienceFacingTests
{
    [TestMethod]
    public void ShortActualDensityRetreatBackstepsButSideForwardAndStoppedStepsDoNot()
    {
        const string retreat = "performance.listen-local-retreat";
        Assert.IsTrue(AudienceFacingMath.ShouldBackstep(retreat, AgentNavigationAction.Travelling,
            -10_000, 11_000, -9_000, 11_000, 30, 0));
        Assert.IsFalse(AudienceFacingMath.ShouldBackstep(retreat, AgentNavigationAction.Travelling,
            -10_000, 11_000, -9_000, 11_000, -30, 0), "An avoidance step towards stage faces travel.");
        Assert.IsFalse(AudienceFacingMath.ShouldBackstep(retreat, AgentNavigationAction.Travelling,
            -10_000, 11_000, -9_000, 11_000, 0, 30), "Pure lateral movement is not a backstep.");
        Assert.IsFalse(AudienceFacingMath.ShouldBackstep(retreat, AgentNavigationAction.Arrived,
            -10_000, 11_000, -9_000, 11_000, 30, 0));
        Assert.IsFalse(AudienceFacingMath.ShouldBackstep(retreat, AgentNavigationAction.Travelling,
            -10_000, 11_000, -7_000, 11_000, 30, 0), "Long retreat travel has ordinary facing.");
        Assert.IsFalse(AudienceFacingMath.ShouldBackstep(retreat, AgentNavigationAction.Travelling,
            -10_000, 11_000, -9_000, 11_000, 0, 0));
    }

    [TestMethod]
    public void NormalRouteImmediatelyResetsHeadingAndStageRelativeGeometryWorksOffCentre()
    {
        foreach (var intent in new[] { "performance.listen-local", "performance.listen", "medical.seek-water",
            "medical.rest", "staff.intervention-approach", "staff.escort-guest", "performance.stage-exit" })
            Assert.IsFalse(AudienceFacingMath.ShouldBackstep(intent, AgentNavigationAction.Travelling,
                -10_000, 11_000, -9_000, 11_000, 30, 0), intent);
        Assert.IsTrue(AudienceFacingMath.ShouldBackstep("performance.listen-local-retreat", AgentNavigationAction.Travelling,
            -10_000, 14_000, -9_000, 14_500, 30, 15));
        Assert.IsFalse(AudienceFacingMath.ShouldBackstep("performance.listen-local-retreat", AgentNavigationAction.Travelling,
            -10_000, 14_000, -11_000, 13_500, -30, -15), "A destination closer to stage is not retreating.");
    }
}
