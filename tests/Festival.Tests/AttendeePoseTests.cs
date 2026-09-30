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
        var session = BuildSession.Planned(20260926);
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

    // v2: male-v5/female-v3 bodies kept for staff and performers (garments fitted to them).
    // v6: guest bodies; soft drinking tilts the cup 25 degrees so the straw reaches the lips.
    [TestMethod]
    [DataRow("attendee_poses_v2_manifest.json", 0f)]
    [DataRow("attendee_poses_v6_manifest.json", 25f)]
    public void DeliveredManifestFilesAndAllProductAnchorsRemainExact(string manifest, float softDrinkingTilt)
    {
        var folder = Path.Combine(RepositoryRoot(), "game", "assets", "characters");
        var catalog = AttendeePoseCatalog.Parse(File.ReadAllText(Path.Combine(folder, manifest)));
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
                    var tilt = state != "drinking" ? 0f : product == ImmersionProduct.Beer ? 55f : softDrinkingTilt;
                    Assert.AreEqual(tilt, anchor.RotationX);
                    Assert.AreEqual(0f, anchor.RotationY); Assert.AreEqual(0f, anchor.RotationZ);
                    Assert.IsTrue(anchor.Y > 1 && anchor.Y < 1.6f); Assert.IsTrue(anchor.Z < 0);
                }
        Assert.IsTrue(catalog.Get("female", "drinking", ImmersionProduct.Beer).Anchors["beer"].Y < catalog.Get("male", "drinking", ImmersionProduct.Beer).Anchors["beer"].Y);
    }

    // Staff overlays, performer bodies and band kits refitted to v6 (assets/source/characters/role-assets-v2).
    [TestMethod]
    public void RoleAssetsV2MatchTheirBuildReports()
    {
        var root = RepositoryRoot();
        var folder = Path.Combine(root, "game", "assets", "characters");
        var reports = Directory.GetFiles(Path.Combine(root, "assets", "source", "characters", "role-assets-v2"), "*.json");
        Assert.AreEqual(17, reports.Length);
        foreach (var report in reports)
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(report));
            var file = json.RootElement.GetProperty("file").GetString()!;
            Assert.AreEqual(json.RootElement.GetProperty("sha256").GetString()!.ToUpperInvariant(),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, file)))), file);
            var nodes = json.RootElement.TryGetProperty("nodes", out var list)
                ? list.EnumerateArray().Select(n => n.GetString()!).ToArray()
                : [json.RootElement.GetProperty("node").GetString()!];
            if (file.Contains("_kit_")) Assert.AreEqual(2, nodes.Count(n => n.EndsWith("PlayingArm")), file);
            if (file.Contains("_performer_")) Assert.AreEqual(2, nodes.Count(n => n.Contains("Idle") && n.EndsWith("Arm")), file);
            if (file.Contains("_overlay_")) Assert.AreEqual(0, json.RootElement.GetProperty("inside_new").GetInt32(), file);
        }
    }
}
