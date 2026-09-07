namespace Festival.Simulation;

public enum SessionPhase
{
    Live = 1,
    Egress = 2,
}

public enum RequestedSpeed
{
    OneX = 1,
    TwoX = 2,
    FourX = 4,
}

public enum CommandReasonCode
{
    Accepted = 0,
    DuplicateCommand = 1,
    WrongCampaign = 2,
    WrongPhase = 3,
    WrongTick = 4,
    OutOfOrderSubmission = 5,
    UnknownTarget = 6,
    InvalidParameter = 7,
    UnknownCommand = 8,
}

public abstract record SessionCommand;

/// <summary>Development fixture: creates the smallest state record used by M0.02.</summary>
public sealed record CreateFixtureRecordCommand(int InitialValue, int ExpiresAfterTicks) : SessionCommand;

/// <summary>Development fixture: changes a record value without advancing time.</summary>
public sealed record ChangeFixtureValueCommand(int NewValue) : SessionCommand;

public sealed record SetPausedCommand(bool IsPaused) : SessionCommand;

public sealed record CommandEnvelope(
    CommandId CommandId,
    CampaignId CampaignId,
    SessionPhase ExpectedPhase,
    long Tick,
    ulong SubmissionSequence,
    EntityId? TargetId,
    SessionCommand Command);

public sealed record CommandResult(
    bool IsAccepted,
    CommandReasonCode ReasonCode,
    string Message,
    EntityId? TargetId = null)
{
    public static CommandResult Accepted(EntityId? targetId = null) =>
        new(true, CommandReasonCode.Accepted, "Command accepted.", targetId);

    public static CommandResult Rejected(CommandReasonCode reasonCode, string message) =>
        new(false, reasonCode, message);
}

public sealed record AppliedCommand(
    CommandId CommandId,
    long Tick,
    ulong SubmissionSequence,
    string CommandType,
    EntityId? TargetId);
