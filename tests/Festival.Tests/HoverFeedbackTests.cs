using Festival.ContentAdapter;
using Festival.Persistence;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class HoverFeedbackTests
{
    [TestMethod]
    public void FactualThirstyCopyKeepsExistingLayoutContentIdentity()
    {
        Assert.AreEqual("Guests grow thirsty faster.",PerkCatalogue.All.Single(p=>p.Id=="thirsty-crowd").Effect);
        Assert.AreEqual("0e6fa18d57decc54430afd5931e220b4477aa7cf793859883fc337ad600baa18",LowerWitteringFarmScenario.ContentCompatibilityHash);
    }
    [TestMethod]
    public void ActualPriorLayoutSaveLoadsWithoutMutationUnderCurrentCompatibility()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"ROGUELIKE_DESIGN.md")))root=root.Parent;
        Assert.IsNotNull(root);
        var path=Path.Combine(root.FullName,"reports/evidence/R0.05h/final-1280x720/saves/manual-preparation.ftsave");
        var bytes=File.ReadAllBytes(path);
        var compatibility=new SaveCompatibility("0.0.1-r0.05-hearing-v1",LowerWitteringFarmScenario.ContentCompatibilityHash,"r0-disorder-layout-v13");
        var loaded=SaveFileAdapter.LoadFile(path,compatibility);
        Assert.IsTrue(loaded.IsSuccess,loaded.Error);
        var restored=GameSession.Restore(loaded.Session!.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess,restored.Error);
        Assert.AreEqual(loaded.Session.CaptureSnapshot().AuthoritativeHash,restored.Session!.CaptureSnapshot().AuthoritativeHash);
        CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));
    }
}
