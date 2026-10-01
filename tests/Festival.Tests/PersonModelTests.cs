using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class PersonModelTests
{
    private static void Accept(GameSession s, SessionCommand command)
    {
        var result = s.Execute(new(new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, command));
        Assert.IsTrue(result.IsAccepted, result.Message);
    }

    private static GameSession Started(ulong seed)
    {
        var s = GameSession.CreateBuildCampaign(seed, FestivalStanding.Established);
        var perk = s.CapturePerks()!;
        Accept(s, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
        Accept(s, new UseDefaultBuildLayoutCommand());
        Accept(s, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
        foreach (var hire in BuildSession.Crew(s)) Accept(s, hire);
        Accept(s, new SetPreparationStockCommand(40, 40, 32));
        Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    private static void AssertPeopleMatchSystemReadModels(GameSession s)
    {
        var roster = s.CapturePreparation()!.People;
        var consumption = s.CaptureImmersion()!.People.ToDictionary(item => item.AgentId);
        var medical = s.CaptureMedical()!.Needs.ToDictionary(item => item.AgentId);
        foreach (var edition in roster)
        {
            var person = s.CapturePerson(edition.AgentId)!;
            Assert.AreEqual(edition.Name, person.Name);
            Assert.AreEqual(edition.Role, person.Role);
            Assert.AreEqual(edition.Admitted, person.Admitted);
            Assert.AreEqual(edition.Satisfaction, person.Satisfaction);
            Assert.AreEqual(consumption[edition.AgentId].Hunger, person.Hunger);
            Assert.AreEqual(consumption[edition.AgentId].ToiletNeed, person.ToiletNeed);
            Assert.AreEqual(consumption[edition.AgentId].Held, person.Held);
            Assert.AreEqual(medical[edition.AgentId].Thirst, person.Thirst);
            Assert.AreEqual(medical[edition.AgentId].Intent, person.Intent);
            Assert.AreEqual(medical[edition.AgentId].Stage, person.HealthStage);
        }
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void OnePersonCarriesEveryPerSystemRecordThroughAFestivalAndRestore()
    {
        var s = Started(20260922);
        AssertPeopleMatchSystemReadModels(s);
        for (var step = 0; step < 12; step++)
        {
            s.AdvanceWithoutSnapshot(1_600);
            AssertPeopleMatchSystemReadModels(s);
        }
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        foreach (var edition in s.CapturePreparation()!.People)
            Assert.AreEqual(s.CapturePerson(edition.AgentId), restored.Session!.CapturePerson(edition.AgentId));
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void ClaimsDescribeWhatEachPersonIsDoing()
    {
        var s = Started(20260929);
        Assert.IsNull(s.CapturePerson(ulong.MaxValue));
        var seen = PersonClaim.None;
        for (var step = 0; step < 240; step++)
        {
            s.AdvanceWithoutSnapshot(80);
            var water = s.CaptureWaterPoints().SelectMany(point => point.Queue).ToHashSet();
            foreach (var edition in s.CapturePreparation()!.People)
            {
                var person = s.CapturePerson(edition.AgentId)!;
                var claims = s.CaptureClaims(edition.AgentId);
                seen |= claims;
                Assert.AreEqual(person.Intent == MedicalIntent.SeekWater, claims.HasFlag(PersonClaim.SeekingWater));
                Assert.AreEqual(water.Contains(person.Id), claims.HasFlag(PersonClaim.WaterPlace));
                Assert.AreEqual(person.VendorId is not null, claims.HasFlag(PersonClaim.Shopping));
                Assert.AreEqual(person.ToiletStage != ToiletVisitStage.None, claims.HasFlag(PersonClaim.ToiletVisit));
                if (claims.HasFlag(PersonClaim.Shopping) || claims.HasFlag(PersonClaim.ToiletVisit))
                    Assert.AreNotEqual(PersonClaim.None, claims & PersonClaims.AwayFromAudience);
            }
        }
        // A normal day exercises the everyday claims, not only incident ones.
        Assert.IsTrue(seen.HasFlag(PersonClaim.SeekingWater) && seen.HasFlag(PersonClaim.WaterPlace), seen.ToString());
        Assert.IsTrue(seen.HasFlag(PersonClaim.Shopping) && seen.HasFlag(PersonClaim.ToiletVisit), seen.ToString());
        Assert.IsTrue(seen.HasFlag(PersonClaim.Performing), seen.ToString());
    }
}
