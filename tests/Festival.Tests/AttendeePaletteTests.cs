using Festival.ContentAdapter;
using Festival.Simulation;
using System.Text.Json;

namespace Festival.Tests;

[TestClass]
public sealed class AttendeePaletteTests
{
    private static string Contract()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, "game", "assets", "characters", "attendee_palette_v1_contract.json");
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException("Designer palette contract not found.");
    }
    private static byte[] Original(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var slots = doc.RootElement.GetProperty("original_hex").EnumerateArray().Select(v => Convert.FromHexString(v.GetString()!)).ToArray();
        var result = new byte[96 * 8 * 4];
        for (var y = 0; y < 8; y++) for (var x = 0; x < 96; x++)
        { slots[x / 8].CopyTo(result, (y * 96 + x) * 4); result[(y * 96 + x) * 4 + 3] = 255; }
        return result;
    }
    [TestMethod]
    public void SixteenDesignerCombinationsPreserveProtectedBytesAlphaAndOriginal()
    {
        var json = Contract(); var contract = AttendeePalette.Parse(json); var original = Original(json); var guard = original.ToArray();
        using var doc = JsonDocument.Parse(json);
        for (var clothing = 0; clothing < 4; clothing++) for (var hair = 0; hair < 4; hair++)
        {
            var output = contract.Apply(original, new(clothing, hair));
            Assert.AreEqual(96 * 8 * 4, output.Length); Assert.IsFalse(ReferenceEquals(original, output));
            var expected = doc.RootElement.GetProperty("clothing_colourways")[clothing].GetProperty("slots").EnumerateObject()
                .Concat(doc.RootElement.GetProperty("hair_colours")[hair].GetProperty("slots").EnumerateObject())
                .ToDictionary(v => int.Parse(v.Name), v => Convert.FromHexString(v.Value.GetString()!));
            for (var y = 0; y < 8; y++) for (var x = 0; x < 96; x++)
            {
                var offset = (y * 96 + x) * 4; Assert.AreEqual(original[offset + 3], output[offset + 3]);
                for (var channel = 0; channel < 3; channel++)
                    Assert.AreEqual(expected.TryGetValue(x / 8, out var colour) ? colour[channel] : original[offset + channel], output[offset + channel]);
            }
            CollectionAssert.AreEqual(guard, original);
        }
        CollectionAssert.AreEqual(original, contract.Apply(original, new(0, 0)), "Original teal/brown combination must be unchanged.");
    }
    [TestMethod]
    public void IndependentClothingHairChoicesAreStablePureAndCoverAllPairs()
    {
        var session = BuildSession.Planned(20260922); var hash = session.CaptureSnapshot().AuthoritativeHash;
        var pairs = Enumerable.Range(1, 256).Select(id => AttendeePalette.Choice(session.CampaignSeed, (ulong)id)).ToArray();
        Assert.AreEqual(16, pairs.Distinct().Count());
        Assert.IsTrue(pairs.Any(p => p.Clothing != p.Hair));
        var loaded = GameSession.Restore(session.CapturePersistenceSnapshot()).Session!;
        CollectionAssert.AreEqual(pairs, Enumerable.Range(1, 256).Select(id => AttendeePalette.Choice(loaded.CampaignSeed, (ulong)id)).ToArray());
        Assert.AreEqual(hash, session.CaptureSnapshot().AuthoritativeHash); Assert.AreEqual(hash, loaded.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void InvalidDimensionsIndicesAndProtectedSlotEditsAreRejected()
    {
        var json = Contract(); var contract = AttendeePalette.Parse(json);
        Assert.ThrowsExactly<ArgumentException>(() => contract.Apply(new byte[16], new(0, 0)));
        Assert.ThrowsExactly<ArgumentException>(() => contract.Apply(Original(json), new(4, 0)));
        Assert.ThrowsExactly<InvalidDataException>(() => AttendeePalette.Parse(json.Replace("\"3\":\"397677\"", "\"9\":\"397677\"")));
    }
}
