using Festival.Simulation;
using Festival.ContentAdapter;
using System.Security.Cryptography;

namespace Festival.Tests;

[TestClass]
public sealed class AttendeePoseTests
{
    [TestMethod]
    public void IdentityUsesStableSeedAndIdWithoutChangingAuthoritativeState()
    {
        var session = GameSession.CreateImmersionCampaign(20260926);
        var before = session.CaptureSnapshot().AuthoritativeHash;
        var variants = session.CapturePreparation()!.People.Select(p => AttendeePose.Variant(session.CampaignSeed, p.AgentId)).ToArray();
        Assert.IsTrue(variants.Contains("male")); Assert.IsTrue(variants.Contains("female"));
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot()).Session!;
        CollectionAssert.AreEqual(variants, restored.CapturePreparation()!.People.Select(p => AttendeePose.Variant(restored.CampaignSeed, p.AgentId)).ToArray());
        Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(before, restored.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    [DataRow(ImmersionProduct.Beer, "drink_hold", "drinking")]
    [DataRow(ImmersionProduct.SoftDrink, "drink_hold", "drinking")]
    [DataRow(ImmersionProduct.Chips, "food_hold", "eating")]
    public void CadenceUsesSavedConsumptionAndHandsPrecedeLift(ImmersionProduct product, string hold, string lifted)
    {
        ImmersionHeldItem Held(int ticks) => new("owned", product, ticks);
        Assert.AreEqual("relaxed", AttendeePose.State(null, true, true));
        Assert.AreEqual("relaxed", AttendeePose.State(Held(240), false, true));
        foreach (var ticks in new[] { 0, 239, 320, 559 }) Assert.AreEqual(hold, AttendeePose.State(Held(ticks), true, true));
        foreach (var ticks in new[] { 240, 319, 560, 639 }) Assert.AreEqual(lifted, AttendeePose.State(Held(ticks), true, true));
        Assert.AreEqual(hold, AttendeePose.State(Held(240), true, false));
        Assert.AreEqual(lifted, AttendeePose.State(Held(240), true, true), "Suspension must not reset saved progress.");
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "game", "assets", "characters", "attendee_poses_v2_manifest.json"))) return dir.FullName;
        throw new DirectoryNotFoundException("Pose manifest not found above test output.");
    }

    [TestMethod]
    public void DeliveredManifestFilesAndAllProductAnchorsRemainExact()
    {
        var folder = Path.Combine(RepositoryRoot(), "game", "assets", "characters");
        var catalog = AttendeePoseCatalog.Parse(File.ReadAllText(Path.Combine(folder, "attendee_poses_v2_manifest.json")));
        foreach (var variant in new[] { "male", "female" })
            foreach (var state in new[] { "relaxed", "drink_hold", "food_hold", "drinking", "eating" })
                foreach (var product in new[] { ImmersionProduct.Beer, ImmersionProduct.SoftDrink, ImmersionProduct.Chips })
                {
                    if (state == "drinking" && product == ImmersionProduct.Chips) continue;
                    var asset = catalog.Get(variant, state, product);
                    Assert.AreEqual(asset.Sha256.ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, asset.File)))));
                    if (state == "relaxed") { Assert.AreEqual(0, asset.Anchors.Count); continue; }
                    var key = state is "food_hold" or "eating" ? "tray" : product == ImmersionProduct.Beer ? "beer" : "soft";
                    var anchor = asset.Anchors[key];
                    Assert.AreEqual(state == "drinking" && product == ImmersionProduct.Beer ? 55f : 0f, anchor.RotationX);
                    Assert.AreEqual(0f, anchor.RotationY); Assert.AreEqual(0f, anchor.RotationZ);
                    Assert.IsTrue(anchor.Y > 1 && anchor.Y < 1.6f); Assert.IsTrue(anchor.Z < 0);
                }
        Assert.IsTrue(catalog.Get("female", "drinking", ImmersionProduct.Beer).Anchors["beer"].Y < catalog.Get("male", "drinking", ImmersionProduct.Beer).Anchors["beer"].Y);
    }
}
