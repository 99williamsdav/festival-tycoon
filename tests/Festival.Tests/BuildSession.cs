using Festival.Simulation;

namespace Festival.Tests;

// The one production campaign route: draft a perk, take the default layout, book a
// line-up, staff it and stock the bars. Tests that need a live edition start here.
internal static class BuildSession
{
    public static readonly string[] Acts = ["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"];

    public static CommandResult Send(GameSession s, SessionCommand command) => s.Execute(new(
        new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, command));

    public static void Accept(GameSession s, SessionCommand command)
    {
        var result = Send(s, command);
        Assert.IsTrue(result.IsAccepted, $"{command.GetType().Name}: {result.ReasonCode} {result.Message}");
    }

    /// <summary>A Build campaign with its perk drafted and the default layout placed; no line-up, stock or staff.</summary>
    public static GameSession Drafted(ulong seed = 20260922, int perk = 0)
    {
        var s = GameSession.CreateBuildCampaign(seed, FestivalStanding.Established);
        var perks = s.CapturePerks()!;
        Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand[perk]));
        Accept(s, new UseDefaultBuildLayoutCommand());
        return s;
    }

    /// <summary>A drafted campaign with a line-up and stock, but no staff.</summary>
    public static GameSession Planned(ulong seed = 20260922, int perk = 0)
    {
        var s = Drafted(seed, perk);
        Accept(s, new SetProgrammeCommand(Acts));
        Accept(s, new SetPreparationStockCommand(40, 40, 32));
        return s;
    }

    /// <summary>The first campaign seed at or after <paramref name="from"/> whose opening hand offers the perk.</summary>
    public static (ulong Seed, int Index) SeedOffering(string perkId, ulong from = 20260922)
    {
        for (var seed = from; seed < from + 500; seed++)
        {
            var index = Array.IndexOf(GameSession.CreateBuildCampaign(seed, FestivalStanding.Established).CapturePerks()!.Hand, perkId);
            if (index >= 0) return (seed, index);
        }
        throw new InvalidOperationException($"No seed near {from} offers {perkId}.");
    }

    /// <summary>A planned campaign that drafted the named perk, e.g. "doctors-orders" for an extra medic slot.</summary>
    public static GameSession PlannedWith(string perkId, ulong from = 20260922)
    {
        var (seed, index) = SeedOffering(perkId, from);
        return Planned(seed, index);
    }

    /// <summary>
    /// One hire for every role this campaign offers: sound, medic and steward. Each is the standard
    /// candidate unless they have traits, in which case the closest trait-free one, so tests see no quirks.
    /// </summary>
    public static string[] CrewIds(GameSession s) => s.GetStaffCandidates().GroupBy(c => c.Role)
        .Select(role => role.OrderBy(c => c.Traits.Length > 0).ThenBy(c => c.Grade > 0).ThenBy(c => Math.Abs(c.Grade)).ThenBy(c => c.Id, StringComparer.Ordinal).First().Id)
        .ToArray();

    /// <summary>A trait-free hire for the role's perk-granted extra slot, never the person <see cref="CrewIds"/> picks.</summary>
    public static string ExtraId(GameSession s, StaffRole role)
    {
        var crew = CrewIds(s);
        return s.GetStaffCandidates().Where(c => c.Role == role && !crew.Contains(c.Id))
            .OrderBy(c => c.Traits.Length > 0).ThenBy(c => c.Grade > 0).ThenBy(c => Math.Abs(c.Grade)).ThenBy(c => c.Id, StringComparer.Ordinal).First().ExtraOfferId;
    }

    /// <summary>
    /// Labelled fixture: no stuck toilets or broken taps from here on (and none open now), for tests about
    /// something else that a random fault would disturb. Saved, so restored copies stay fault-free.
    /// </summary>
    public static GameSession WithoutFaults(GameSession s)
    {
        typeof(GameSession).GetField("_faults", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(s, new FaultsSnapshot(1, [], Disabled: true));
        return s;
    }

    /// <summary>Hire commands for <see cref="CrewIds"/>.</summary>
    public static AcceptPreparationOfferCommand[] Crew(GameSession s) => CrewIds(s).Select(id => new AcceptPreparationOfferCommand(id)).ToArray();

    /// <summary>A Build campaign in preparation with a legal default plan, ready to start.</summary>
    public static GameSession Ready(ulong seed = 20260922, int perk = 0, params string[] offers)
    {
        var s = Planned(seed, perk);
        foreach (var hire in Crew(s)) Accept(s, hire);
        foreach (var offer in offers) Accept(s, new AcceptPreparationOfferCommand(offer));
        return s;
    }

    /// <summary>A Build campaign whose edition has started.</summary>
    public static GameSession Started(ulong seed = 20260922, int perk = 0, params string[] offers)
    {
        var s = Ready(seed, perk, offers);
        Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    /// <summary>The last guest on the roster: the person the retired at-risk scenario used to single out.</summary>
    public static ulong LastGuest(GameSession s) => s.CapturePreparation()!.People.Last(person => person.Role == ProtectedPersonRole.Guest).AgentId;

    /// <summary>The disorder record with its baseline steward changed.</summary>
    public static DisorderSnapshot WithSecurity(DisorderSnapshot d, SecurityResponseStage stage, ulong? target) =>
        d with { Stewards = [d.Stewards[0] with { Stage = stage, TargetId = target }, .. d.Stewards.Skip(1)] };

    /// <summary>The standing main water tap.</summary>
    public static WaterPointState MainTap(GameSession s) => s.CaptureWaterPoints().Single(point => point.Id == "water.main");

    /// <summary>Test fixture: replaces one standing tap's live state.</summary>
    public static void SetTap(GameSession s, WaterPointState tap) => typeof(GameSession)
        .GetMethod("SetWaterPoint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(s, [tap]);

    /// <summary>Test fixture: replaces one vendor's live state.</summary>
    public static void SetVendor(GameSession s, ImmersionVendor vendor) => typeof(GameSession)
        .GetMethod("SetImmersionVendor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(s, [vendor]);

    /// <summary>Test fixture: replaces one toilet's live state.</summary>
    public static void SetToilet(GameSession s, ToiletFacility toilet) => typeof(GameSession)
        .GetMethod("SetToilet", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(s, [toilet]);

    public static GameSession Restored(GameSession s)
    {
        var loaded = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        return loaded.Session;
    }
}
