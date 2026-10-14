namespace Festival.Simulation;

/// <summary>Fixed presentation assignments. No random draw or weather-simulation switch.</summary>
public sealed record FestivalChallenge(string Id, string Title, string Description, string Artwork, bool EffectsImplemented);

public static class FestivalChallenges
{
    public static FestivalChallenge? ForTier(int tier) => CampaignRulesProfile.For(CampaignRuleset.Survival).ChallengeForTier(tier);
}

public sealed record AcknowledgeFestivalChallengeCommand(int Tier, int Attempt) : SessionCommand;

public sealed partial class GameSession
{
    private CommandResult? ValidateChallengeAcknowledgement(EntityId? target, AcknowledgeFestivalChallengeCommand command) =>
        target is not null || _preparation is not { Status: PreparationStatus.Preparing } p ||
        p.Tier != command.Tier || p.Attempt != command.Attempt || p.ChallengeAcknowledged || FestivalChallenges.ForTier(p.Tier) is null
            ? CommandResult.Rejected(CommandReasonCode.WrongPhase, "This festival challenge is not awaiting acknowledgement.") : null;
}
