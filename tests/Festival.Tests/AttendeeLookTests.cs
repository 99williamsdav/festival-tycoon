using Festival.ContentAdapter;

namespace Festival.Tests;

[TestClass]
public sealed class AttendeeLookTests
{
    private const int Folk = 0, Indie = 1, Punk = 4;

    private static AttendeeLook[] Crowd(bool male, int genre) =>
        Enumerable.Range(1, 4_000).Select(id => AttendeeLooks.For(20260922, (ulong)id, male, genre)).ToArray();

    [TestMethod]
    public void OnlyPunkFansWearMohawks()
    {
        foreach (var genre in Enumerable.Range(0, 6).Where(g => g != Punk))
            Assert.IsFalse(Crowd(true, genre).Concat(Crowd(false, genre)).Any(l => l.Hair == AttendeeHairStyle.Mohawk), $"genre {genre}");
        Assert.AreEqual(30, Crowd(true, Punk).Count(l => l.Hair == AttendeeHairStyle.Mohawk) * 100 / 4_000, 3);
        Assert.AreEqual(22, Crowd(false, Punk).Count(l => l.Hair == AttendeeHairStyle.Mohawk) * 100 / 4_000, 3);
    }

    [TestMethod]
    public void HeadwearFollowsTheRules()
    {
        foreach (var male in new[] { true, false })
            foreach (var genre in Enumerable.Range(0, 6))
                foreach (var look in Crowd(male, genre))
                {
                    Assert.IsFalse(look.Cap && look.FlowerCrown, "One thing on the head at most.");
                    Assert.IsFalse(look.Hair == AttendeeHairStyle.Mohawk && (look.Cap || look.FlowerCrown), "Nothing squashes a mohawk.");
                    Assert.IsFalse(look.FlowerCrown && look.Hair != AttendeeHairStyle.Default, "A crown needs hair to sit on.");
                    if (!male) Assert.IsFalse(look.Beard || look.Hair == AttendeeHairStyle.Bald);
                }
    }

    [TestMethod]
    public void FolkFansFavourFlowerCrownsAndBeardsAndTheChoiceIsStable()
    {
        Assert.IsTrue(Crowd(false, Folk).Count(l => l.FlowerCrown) > 2 * Crowd(false, Indie).Count(l => l.FlowerCrown));
        Assert.IsTrue(Crowd(true, Folk).Count(l => l.Beard) > Crowd(true, Indie).Count(l => l.Beard));
        Assert.AreEqual(AttendeeLooks.For(7, 42, true, Indie), AttendeeLooks.For(7, 42, true, Indie));
    }
}
