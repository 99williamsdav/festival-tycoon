using Festival.Simulation;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Festival.Tests;

[TestClass]
public sealed class PerkArtworkTests
{
    [TestMethod]
    public void RuntimeMastersMatchApprovedManifestAndNativeCatalogue()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"ROGUELIKE_DESIGN.md"))) root=root.Parent;
        Assert.IsNotNull(root);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,"assets/source/perks/mosaic-suite-v1/manifest.json")));
        var cards = manifest.RootElement.GetProperty("cards").EnumerateArray().ToArray();
        Assert.AreEqual(8,cards.Length);
        CollectionAssert.AreEquivalent(PerkCatalogue.All.Select(p=>p.Id).ToArray(),cards.Select(c=>c.GetProperty("id").GetString()!).ToArray());
        foreach(var card in cards)
        {
            var id = card.GetProperty("id").GetString()!;
            var perk = PerkCatalogue.All.Single(p=>p.Id==id);
            Assert.AreEqual(perk.Name,card.GetProperty("name").GetString());
            Assert.AreEqual(perk.Effect,card.GetProperty("effect").GetString());
            Assert.AreEqual($"artwork/{id}.png",card.GetProperty("artwork").GetString());
            var bytes = File.ReadAllBytes(Path.Combine(root.FullName,$"game/assets/ui/perks/{id}.png"));
            Assert.AreEqual(card.GetProperty("sha256").GetString(),Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            CollectionAssert.AreEqual(new byte[]{137,80,78,71,13,10,26,10},bytes.Take(8).ToArray());
            Assert.AreEqual(1536,BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)));
            Assert.AreEqual(1024,BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4)));
            Assert.IsTrue(File.Exists(Path.Combine(root.FullName,$"game/assets/ui/perks/{id}.svg")),"Recoverable placeholder missing");
        }
    }
}
