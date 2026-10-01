using Festival.Simulation;
using Festival.Persistence;
using System.Security.Cryptography;

namespace Festival.Tests;

[TestClass]
public sealed class BookingTableViewTests
{
    private static readonly FestivalAct[] Acts =
    [
        new("c", "Copper", 1, 9000, 80, 80, 70),
        new("b", "Barnstorm", 1, 5500, 55, 35, 65),
        new("z", "Same", 0, 4000, 40, 20, 80),
        new("a", "Same", 0, 4000, 40, 20, 80),
        new("n", "Neon", 2, 11000, 90, 90, 85),
        new("f", "Field", 3, 8500, 75, 60, 75)
    ];

    [TestMethod]
    public void HalfStarMidpointsRoundUpIncludingZeroAndMaximum()
    {
        foreach (var (score, expected) in new[] { (0, 0), (4, 0), (5, 1), (10, 1), (15, 2), (35, 4), (65, 7), (95, 10), (100, 10) })
            Assert.AreEqual(expected, BookingTableView.HalfStarUnits(score), $"{score}/100");
    }

    [TestMethod]
    public void EveryHeaderUsesRawFieldDirectionAndDeterministicNameIdTies()
    {
        var expected = new Dictionary<BookingSortField, (string[] Asc, string[] Desc)>
        {
            [BookingSortField.Band] = (["b", "c", "f", "n", "a", "z"], ["a", "z", "n", "f", "c", "b"]),
            [BookingSortField.Genre] = (["f", "a", "z", "b", "c", "n"], ["n", "b", "c", "a", "z", "f"]),
            [BookingSortField.Price] = (["a", "z", "b", "f", "c", "n"], ["n", "c", "f", "b", "a", "z"]),
            [BookingSortField.Popularity] = (["a", "z", "b", "f", "c", "n"], ["n", "c", "f", "b", "a", "z"]),
            [BookingSortField.Ego] = (["a", "z", "b", "f", "c", "n"], ["n", "c", "f", "b", "a", "z"]),
            [BookingSortField.Professionalism] = (["b", "c", "f", "a", "z", "n"], ["n", "a", "z", "f", "c", "b"])
        };
        foreach (var field in Enum.GetValues<BookingSortField>())
        {
            foreach (var descending in new[] { false, true })
            {
                var first = BookingTableView.Project(Acts, null, field, descending).Select(a => a.Id).ToArray();
                var reverseInput = BookingTableView.Project(Acts.Reverse(), null, field, descending).Select(a => a.Id).ToArray();
                CollectionAssert.AreEqual(descending ? expected[field].Desc : expected[field].Asc, first, $"Expected raw order {field}/{descending}");
                CollectionAssert.AreEqual(first, reverseInput, $"{field}/{descending}");
                Assert.IsTrue(Array.IndexOf(first, "a") < Array.IndexOf(first, "z"), $"ID tie {field}/{descending}");
            }
        }
        Assert.AreEqual("b", BookingTableView.Project(Acts, null, BookingSortField.Ego, false)[2].Id); // raw35, despite half-star rounding
    }

    [TestMethod]
    public void GenreFilteringOnlyProjectsRowsAndNeverMutatesCatalog()
    {
        var original = Acts.Select(a => a.Id).ToArray();
        Assert.AreEqual(6, BookingTableView.Project(Acts, null, BookingSortField.Price, false).Length);
        CollectionAssert.AreEqual(new[] { "a", "z" }, BookingTableView.Project(Acts, 0, BookingSortField.Price, false).Select(a => a.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "b", "c" }, BookingTableView.Project(Acts, 1, BookingSortField.Price, false).Select(a => a.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "n" }, BookingTableView.Project(Acts, 2, BookingSortField.Price, false).Select(a => a.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "f" }, BookingTableView.Project(Acts, 3, BookingSortField.Price, false).Select(a => a.Id).ToArray());
        CollectionAssert.AreEqual(original, Acts.Select(a => a.Id).ToArray());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BookingTableView.Project(Acts, 4, BookingSortField.Price, false));
    }

}
