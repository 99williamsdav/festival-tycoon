using Festival.Simulation;

namespace Festival.Tests;

// The one production campaign route: draft a perk, take the default layout, book a
// line-up, staff it and stock the bars. Tests that need a live edition start here.
internal static class BuildSession
{
    public static readonly string[] Acts = ["act.meadow-lanterns", "act.overdue-library-books", "act.glitter-rota"];

    public static CommandResult Send(GameSession s, SessionCommand command) => s.Execute(new(
        new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, command));

    public static void Accept(GameSession s, SessionCommand command)
    {
        var result = Send(s, command);
        Assert.IsTrue(result.IsAccepted, $"{command.GetType().Name}: {result.ReasonCode} {result.Message}");
    }

    /// <summary>
    /// Draft a quiet perk (a hiring slot no crew fills), redrawing the hand until it's offered, so
    /// a test's world doesn't change whenever a perk joins the catalogue and reshuffles every opening hand.
    /// </summary>
    public const int QuietPerk = -1;

    /// <summary>A Build campaign with its perk drafted and the default layout placed; no line-up, stock or staff.</summary>
    public static GameSession Drafted(ulong seed = 20260922, int perk = QuietPerk) =>
        Drafted(seed, perk == QuietPerk ? Quiet : null, perk);

    // Hiring slots that no test crew fills, so drafting either changes nothing on the day.
    private static readonly string[] Quiet = ["extra-pair-of-hands", "doctors-orders"];

    /// <summary>A Build campaign that drafted the named perk, redrawing its opening hand until it's offered.</summary>
    public static GameSession Drafted(ulong seed, string perkId) => Drafted(seed, [perkId]);

    private static GameSession Drafted(ulong seed, string[]? perkIds, int index = 0, bool pond = false)
    {
        var s = pond ? GameSession.CreateBuildCampaign(seed, FestivalStanding.Established, pondStageTrial: true) : GameSession.CreateBuildCampaign(seed, FestivalStanding.Established);
        var perks = s.CapturePerks()!;
        var redraw = typeof(GameSession).GetMethod("OpenPerkDraft", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        for (var draws = 0; perkIds is not null && !perks.Hand.Any(perkIds.Contains); draws++)
        {
            // A rare card can take a good many redraws to turn up.
            Assert.IsTrue(draws < 400, $"Seed {seed} never offered {string.Join(" or ", perkIds)}.");
            redraw.Invoke(s, []); perks = s.CapturePerks()!;
        }
        Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perkIds is null ? perks.Hand[index] : perks.Hand.First(perkIds.Contains)));
        Accept(s, new UseDefaultBuildLayoutCommand());
        return s;
    }

    /// <summary>
    /// Takes the default layout's second toilet away, so a test about placing a toilet of its own has the slot free.
    /// </summary>
    public static GameSession WithOneToilet(GameSession s)
    {
        Accept(s, new RemoveBuildServiceCommand("toilet.extra-1"));
        return s;
    }

    /// <summary>Takes every bin away, so a test about bins places exactly the ones it means.</summary>
    public static GameSession WithoutBins(GameSession s)
    {
        foreach (var bin in s.CaptureBuildPlacements().Where(p => p.Kind == BuildServiceKind.Bin).ToArray())
            Accept(s, new RemoveBuildServiceCommand(bin.Id));
        return s;
    }

    /// <summary>
    /// The corner of the field the litter tests are drawn on: no bins of the default's, and the steward post on its
    /// old spot by the gate (<see cref="GameSession.DisorderSecurityPostCell"/>), so the cells round it they stand
    /// people, litter and bins on are open grass.
    /// </summary>
    public static GameSession WithLitterCorner(GameSession s)
    {
        WithoutBins(s);
        Accept(s, new MoveBuildServiceCommand("steward-post", GameSession.DisorderSecurityPostCell, 1));
        return s;
    }

    /// <summary>A drafted campaign with a line-up and stock, but no staff.</summary>
    public static GameSession Planned(ulong seed = 20260922, int perk = QuietPerk) =>
        Planned(Drafted(seed, perk));

    /// <summary>A drafted campaign, with the named perk, a line-up and stock, but no staff.</summary>
    public static GameSession Planned(ulong seed, string perkId) => Planned(Drafted(seed, perkId));

    private static GameSession Planned(GameSession s)
    {
        Accept(s, new SetProgrammeCommand(Acts));
        Accept(s, new SetPreparationStockCommand(40, 32));
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
    public static GameSession Ready(ulong seed = 20260922, int perk = QuietPerk, params string[] offers)
    {
        var s = Planned(seed, perk);
        foreach (var hire in Crew(s)) Accept(s, hire);
        foreach (var offer in offers) Accept(s, new AcceptPreparationOfferCommand(offer));
        return s;
    }

    /// <summary>A Build campaign that drafted the named perk, crewed and started.</summary>
    public static GameSession Started(ulong seed, string perkId)
    {
        var s = Planned(seed, perkId);
        foreach (var hire in Crew(s)) Accept(s, hire);
        Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    /// <summary>A Build campaign whose edition has started.</summary>
    public static GameSession Started(ulong seed = 20260922, int perk = QuietPerk, params string[] offers)
    {
        var s = Ready(seed, perk, offers);
        Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    /// <summary>Three acts for the Pond Stage, none of them in <see cref="Acts"/>.</summary>
    public static readonly string[] PondActs = ["act.low-battery", "act.unlicensed-bouncy-castle", "act.septic-tank"];

    /// <summary>A Pond Stage trial campaign with its perk drafted and the default layout placed; no line-up, stock or staff.</summary>
    public static GameSession PondDrafted(ulong seed = 20260922) => Drafted(seed, Quiet, 0, pond: true);

    /// <summary>A Pond Stage trial campaign booked on both stages, stocked and fully crewed, its Pond Stage engineer included; not started.</summary>
    public static GameSession PondReady(ulong seed = 20260922, params string[] offers)
    {
        var s = PondDrafted(seed);
        Accept(s, new SetProgrammeCommand(Acts));
        Accept(s, new SetProgrammeCommand(PondActs) { StageId = FestivalStages.PondId });
        Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in Crew(s)) Accept(s, hire);
        Accept(s, new AcceptPreparationOfferCommand(ExtraId(s, StaffRole.Sound)));
        foreach (var offer in offers) Accept(s, new AcceptPreparationOfferCommand(offer));
        return s;
    }

    /// <summary>A Pond Stage trial campaign whose edition has started.</summary>
    public static GameSession PondStarted(ulong seed = 20260922, params string[] offers)
    {
        var s = PondReady(seed, offers);
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

    /// <summary>Test fixture: replaces the main stage's live set.</summary>
    public static void SetMainLive(GameSession s, LivePerformanceSnapshot? live) =>
        ((LivePerformanceSnapshot?[])typeof(GameSession).GetField("_livePerformances", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(s)!)[0] = live;

    /// <summary>Test fixture: replaces the main stage's running order.</summary>
    public static void SetMainProgramme(GameSession s, StageProgrammeSnapshot programme)
    {
        var field = typeof(GameSession).GetField("_programme", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var all = (ProgrammeSnapshot)field.GetValue(s)!;
        field.SetValue(s, all with { Stages = [programme, .. all.Stages.Skip(1)] });
    }

    /// <summary>A save with its main stage's running order or live set changed, for restore validation tests.</summary>
    public static SessionPersistenceSnapshot WithMainProgramme(SessionPersistenceSnapshot saved, Func<StageProgrammeSnapshot, StageProgrammeSnapshot> change) =>
        saved with { Programme = saved.Programme! with { Stages = [change(saved.Programme.Stages[0]), .. saved.Programme.Stages.Skip(1)] } };
    public static SessionPersistenceSnapshot WithMainLive(SessionPersistenceSnapshot saved, Func<LivePerformanceSnapshot, LivePerformanceSnapshot> change) =>
        saved with { LivePerformances = [change(saved.LivePerformances![0]!), .. saved.LivePerformances.Skip(1)] };

    public static GameSession Restored(GameSession s)
    {
        var loaded = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        return loaded.Session;
    }
}
