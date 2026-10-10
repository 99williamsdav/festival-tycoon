namespace Festival.Simulation;

/// <summary>Fixed presentation assignments. No random draw or weather-simulation switch.</summary>
public sealed record FestivalChallenge(string Id, string Title, string Description, string Artwork, bool EffectsImplemented);

public static class FestivalChallenges
{
    public static FestivalChallenge? ForTier(int tier) => tier switch
    {
        1 => new("heatwave", "Heatwave", "Guests build up heat and thirst. Water and shade help them cool down.", "heatwave-b.png", true),
        2 => new("heavy-rain", "Heavy rain", "Heavy showers soak guests and turn the field muddy. Ponchos and shelter help keep people dry.", "heavy-rain-a.png", true),
        _ => null
    };
}

public sealed record AcknowledgeFestivalChallengeCommand(int Tier, int Attempt) : SessionCommand;

public sealed partial class GameSession
{
    private CommandResult? ValidateChallengeAcknowledgement(EntityId? target, AcknowledgeFestivalChallengeCommand command) =>
        target is not null || _preparation is not { Status: PreparationStatus.Preparing } p ||
        p.Tier != command.Tier || p.Attempt != command.Attempt || p.ChallengeAcknowledged || FestivalChallenges.ForTier(p.Tier) is null
            ? CommandResult.Rejected(CommandReasonCode.WrongPhase, "This festival challenge is not awaiting acknowledgement.") : null;
}
