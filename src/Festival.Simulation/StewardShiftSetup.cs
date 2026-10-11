namespace Festival.Simulation;

public sealed partial class GameSession
{
    /// <summary>The perk that opens the second steward slot: the player's own.</summary>
    private const string StewardShiftPerk = "extra-pair-of-hands";
    /// <summary>Where the shift's marquee goes: beside the main stage's crowd.</summary>
    private static readonly GridCell ShiftMarqueeCell = new(104, 174);

    /// <summary>
    /// Prototype steward shift: a festival that plans and runs itself, with the player as its second steward.
    /// The same seed and tier always give the same day. Tier 1 is a fresh campaign; later tiers open as the
    /// development route does, as if the ones below had just been played.
    /// <para>
    /// The plan: the extra-steward perk (the hand is redrawn until it turns up), the default layout, the three
    /// cheapest available acts on each open stage, the standard trait-free hire for every role, the second
    /// steward, the Pond Stage's engineer if it plays, the maintenance worker and a marquee if the budget allows, and the
    /// usual bar stock (with eight straw bales on a rainy day). The festival starts at once and the second steward is the player's.
    /// </para>
    /// </summary>
    public static GameSession CreateStewardShift(ulong seed, int tier = 1)
    {
        var s = CreateDevelopmentFestival(seed, tier);
        s.PlanStewardShift();
        s.RunSetupCommand(new StartPreparedEditionCommand());
        var player = s._preparation!.StaffProfiles.Single(profile => profile.Role == ResponseRole.Steward).AgentId;
        s.TakeStewardControl(player);
        return s;
    }

    private void PlanStewardShift()
    {
        for (var draws = 0; !_perks!.Hand.Contains(StewardShiftPerk); draws++)
        {
            if (draws >= 400) throw new InvalidOperationException($"Seed {CampaignSeed} never offered the second steward.");
            OpenPerkDraft();
        }
        RunSetupCommand(new ChoosePerkCommand(_perks.DraftAttempt, _perks.Cursor, StewardShiftPerk));
        RunSetupCommand(new UseDefaultBuildLayoutCommand());
        var acts = GetFestivalActs().Where(act => ActStandingOf(act) == ActStanding.Available)
            .OrderBy(ActFee).ThenBy(act => act.Id, StringComparer.Ordinal).Select(act => act.Id).ToArray();
        RunSetupCommand(new SetProgrammeCommand(acts[..3]));
        if (PondStageOpen) RunSetupCommand(new SetProgrammeCommand(acts[3..6]) { StageId = FestivalStages.PondId });
        // A rainy day's store has bales for the mud; the crew's and the player's.
        RunSetupCommand(WeatherRules.ForTier(_preparation!.Tier) == WeatherKind.Rain ? new SetPreparationStockCommand(40, 32) { StrawBales = 8 } : new SetPreparationStockCommand(40, 32));
        // The steadiest hand in each role: no traits, then the standard grade.
        var picks = GetStaffCandidates().GroupBy(candidate => candidate.Role).Select(role => role
            .OrderBy(c => c.Traits.Length > 0).ThenBy(c => c.Grade > 0).ThenBy(c => Math.Abs(c.Grade)).ThenBy(c => c.Id, StringComparer.Ordinal).ToArray()).ToArray();
        foreach (var role in picks) RunSetupCommand(new AcceptPreparationOfferCommand(role[0].Id));
        RunSetupCommand(new AcceptPreparationOfferCommand(picks.Single(role => role[0].Role == StaffRole.Steward)[1].ExtraOfferId));
        if (PondStageOpen) RunSetupCommand(new AcceptPreparationOfferCommand(picks.Single(role => role[0].Role == StaffRole.Sound)[1].ExtraOfferId));
        if (ValidateCommand(SetupEnvelope(new AcceptPreparationOfferCommand("maintenance.worker"))) is null)
            RunSetupCommand(new AcceptPreparationOfferCommand("maintenance.worker"));
        // A marquee by the crowd, if the budget runs to it: shade in a heatwave, shelter in the rain.
        var marquee = new PlaceBuildServiceCommand(BuildServiceKind.Marquee, ShiftMarqueeCell);
        if (ValidateCommand(SetupEnvelope(marquee)) is null) RunSetupCommand(marquee);
    }

    private CommandEnvelope SetupEnvelope(SessionCommand command) =>
        new(new CommandId(NextSubmissionSequence + 1), CampaignId, Phase, CurrentTick, NextSubmissionSequence, null, command);

    private void RunSetupCommand(SessionCommand command)
    {
        var result = Execute(SetupEnvelope(command));
        if (!result.IsAccepted) throw new InvalidOperationException($"Steward shift setup: {command.GetType().Name} {result.ReasonCode} {result.Message}");
    }
}
