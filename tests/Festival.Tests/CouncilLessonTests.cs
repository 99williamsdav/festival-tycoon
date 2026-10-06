using System.Collections;
using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class CouncilLessonTests
{
    [TestMethod]
    public void AFightDeathGetsTheFightLesson()
    {
        var s = BuildSession.Ready();
        Assert.IsTrue(BuildSession.Send(s, new StartPreparedEditionCommand()).IsAccepted);
        Assert.IsNull(s.CouncilLesson(), "No death, no remark.");
        // A fight's death goes on record as a disorder death and never marks the medical record fatal; staged here as
        // the casualty ApplyDisorderDeath writes.
        var lifecycle = typeof(GameSession).GetField("_lifecycle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;
        var casualties = (IList)lifecycle.GetType().GetProperty("Casualties")!.GetValue(lifecycle)!;
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest);
        casualties.Add(new CasualtySnapshot(1, 1, guest.Name, ProtectedPersonRole.Guest, "Confrontation injury", s.CurrentTick, "disorder-death:1:1"));
        Assert.IsFalse(s.CaptureMedical()!.Fatal);
        Assert.AreEqual("Perhaps someone could have stepped in before it came to blows?", s.CouncilLesson());
    }
}
