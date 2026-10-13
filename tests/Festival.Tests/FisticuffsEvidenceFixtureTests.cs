using Festival.ContentAdapter;
using Festival.Simulation;
using System.Reflection;
using System.Text.Json;

namespace Festival.Tests;

/// <summary>Opt-in, labelled initial encounter fixtures for native/exported evidence; not production setup.</summary>
[TestClass]
public sealed class FisticuffsEvidenceFixtureTests
{
    [TestMethod]
    public void WriteControlledNativeFixturesWhenRequested()
    {
        var folder = Environment.GetEnvironmentVariable("FESTIVAL_FISTICUFFS_FIXTURES_DIR");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var started = typeof(StaffAutonomyTests).GetMethod("Started", flags)!;
        var position = typeof(StaffAutonomyTests).GetMethod("PositionFixture", flags)!;
        var metadata = new List<object>();
        foreach (var (name, sexA, sexB, north, handling, staff) in new[] {
            ("male-female", "male", "female", false, false, false), ("male-male", "male", "male", false, false, false),
            ("female-female", "female", "female", false, false, false), ("female-male", "female", "male", false, false, false),
            ("male-female-north", "male", "female", true, false, false), ("handling", "male", "female", false, true, false),
            ("guest-steward", "male", "female", false, false, true) })
        {
            var s = (GameSession)started.Invoke(null, [false])!;
            ulong[] Free(string sex) => s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed &&
                AttendeePose.Variant(s.CampaignSeed, p.AgentId) == sex &&
                (s.CaptureClaims(p.AgentId) & (PersonClaims.AwayFromAudience | PersonClaims.ResponseAssigned)) == 0).Select(p => p.AgentId).ToArray();
            for (var i = 0; i < 80 && (Free(sexA).Length < 2 || Free(sexB).Length < 2); i++) s.AdvanceWithoutSnapshot(80);
            var a = Free(sexA).First(); var d = s.CaptureDisorder()!; var b = staff ? d.SecurityId : Free(sexB).First(id => id != a);
            // Initial fixture placement precedes Fight. Normal BeginDisorderFight must do the closer movement.
            position.Invoke(null, [s, a, new GridCell(108, 178)]);
            position.Invoke(null, [s, b, north ? new GridCell(108, 182) : new GridCell(staff ? 111 : 112, 178)]);
            if (!staff) position.Invoke(null, [s, d.SecurityId, handling ? new GridCell(115, 180) : new GridCell(136, 178)]);
            d = s.CaptureDisorder()!;
            typeof(GameSession).GetProperty("DisorderView", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(s, d with {
                Stewards = d.Stewards.Select(p => p with { Stage = staff ? SecurityResponseStage.Confronting : SecurityResponseStage.None,
                    TargetId = staff ? a : null, StartedTick = s.CurrentTick }).ToArray(),
                People = d.People.Select(p => p.AgentId == a || p.AgentId == b ? p with { Stage = DisorderStage.Argument,
                    Grievance = DisorderGrievance.MusicCutoff, GrievanceTick = s.CurrentTick, StageTick = s.CurrentTick,
                    Pressure = 8000, OpponentId = staff && p.AgentId == a ? b : null } : p).ToArray() });
            var snapshot = s.CapturePersistenceSnapshot(); var restored = GameSession.Restore(snapshot); Assert.IsTrue(restored.IsSuccess, restored.Error);
            var json = JsonSerializer.Serialize(snapshot);
            var roundTrip = GameSession.Restore(JsonSerializer.Deserialize<SessionPersistenceSnapshot>(json)!); Assert.IsTrue(roundTrip.IsSuccess, roundTrip.Error);
            File.WriteAllText(Path.Combine(folder, name + ".json"), json);
            metadata.Add(new { name, a, b, steward = d.SecurityId, startingTick = s.CurrentTick, initialSpacingMetres = staff ? 1.5 : 2,
                caveat = "Controlled initial positions before Fight, not a natural encounter or gameplay teleport. Runtime closer movement uses production navigation." });
        }
        File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }
}
