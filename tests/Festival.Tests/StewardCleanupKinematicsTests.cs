using Festival.ContentAdapter;
using System.Numerics;

namespace Festival.Tests;

[TestClass]
public sealed class StewardCleanupKinematicsTests
{
    private static CleanupRig Rig(string variant)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "game", "assets", "characters", "steward-cleanup-v1", "manifest.json");
            if (File.Exists(file)) return CleanupRig.Parse(File.ReadAllText(file), variant);
        }
        throw new FileNotFoundException("Cleanup manifest missing.");
    }
    [TestMethod]
    [DataRow("male")]
    [DataRow("female")]
    public void BothFittedArmsRetainTheirLengthsForNearFootTargetsThroughoutPickup(string variant)
    {
        var rig = Rig(variant);
        foreach (var x in new[] { -.25f, 0, .25f })
        foreach (var z in new[] { -.25f, 0, .25f })
        foreach (var extra in new[] { 0, .075f })
        foreach (var y in new[] { -.015f, .01f, .04f, .055f, .1f })
        {
            var contact = new Vector3(0, y, -new Vector2(x, z).Length() - extra);
            for (var tick = 0; tick <= 80; tick++)
            {
                var frame = StewardCleanupKinematics.Evaluate(rig, tick / 80d, contact);
                Assert.IsTrue(frame.Left.Reachable && frame.Right.Reachable, $"{variant} {contact} tick {tick}");
                void Arm(CleanupArmRig arm, CleanupArmPose pose)
                {
                    Assert.AreEqual(arm.UpperLength, Vector3.Distance(pose.Upper.Position, pose.Fore.Position), .00001f);
                    Assert.AreEqual(arm.ForeLength, Vector3.Distance(pose.Fore.Position, pose.Hand.Position), .00001f);
                    var upperTip = pose.Upper.Position + Vector3.Transform(arm.Elbow - arm.Shoulder, pose.Upper.Rotation);
                    Assert.IsTrue(Vector3.Distance(upperTip, pose.Fore.Position) < .00001f);
                }
                Arm(rig.Left, frame.Left); Arm(rig.Right, frame.Right);
                Assert.IsTrue(frame.Bag.Position.Y - .48f > .4f);
            }
            var reached = StewardCleanupKinematics.Evaluate(rig, .45, contact);
            Assert.IsTrue(Vector3.Distance(contact, reached.Jaw) < .00001f);
            var bagged = StewardCleanupKinematics.Evaluate(rig, .85, contact);
            Assert.IsTrue(Vector3.Distance(bagged.BagMouth, bagged.Jaw) < .00001f);
        }
    }
    [TestMethod]
    public void SameAuthorityInputSeeksExactlyAndHandGripsFollowProps()
    {
        foreach (var variant in new[] { "male", "female" })
        {
            var rig = Rig(variant); var contact = new Vector3(0, -.012f, -.26f);
            foreach (var phase in new[] { -1, 0, .35, .45, .5, .75, .85, .999 })
            {
                var frame = StewardCleanupKinematics.Evaluate(rig, phase, contact);
                Assert.AreEqual(frame, StewardCleanupKinematics.Evaluate(rig, phase, contact));
                Assert.AreEqual(frame.Picker.Position + Vector3.Transform(new(0, .044f, 0), frame.Picker.Rotation), frame.Right.Hand.Position);
                Assert.AreEqual(frame.Bag.Position + new Vector3(0, .044f, 0), frame.Left.Hand.Position);
                Assert.AreEqual(rig.Hip, frame.Hip.Position);
            }
        }
    }
}
