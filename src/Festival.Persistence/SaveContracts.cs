using Festival.Simulation;

namespace Festival.Persistence;

public sealed record SaveCompatibility(string BuildId, string ContentHash, string RulesetHash);

public sealed record SaveHeaderV1(
    string Format,
    int SchemaVersion,
    string BuildId,
    string ContentHash,
    string RulesetHash,
    ulong CampaignId,
    string TimestampUtc,
    int Phase,
    string Purpose,
    string PayloadChecksum);

public sealed record SaveEnvelopeV1(SaveHeaderV1 Header, SessionPersistenceSnapshot Payload);

public sealed record SaveWriteRequest(
    GameSession Session,
    SaveCompatibility Compatibility,
    string Purpose,
    DateTimeOffset TimestampUtc);

public sealed record SaveOperationResult(bool IsSuccess, string? Error, string? Path)
{
    public static SaveOperationResult Success(string path) => new(true, null, path);
    public static SaveOperationResult Failure(string error, string? path = null) => new(false, error, path);
}

public sealed record SaveLoadResult(
    GameSession? Session,
    SaveHeaderV1? Header,
    bool BuildMismatch,
    string? Warning,
    string? Error)
{
    public bool IsSuccess => Session is not null;
    public static SaveLoadResult Success(GameSession session, SaveHeaderV1 header, bool buildMismatch) => new(
        session,
        header,
        buildMismatch,
        buildMismatch ? $"Save build '{header.BuildId}' differs from current build; schema/content/ruleset are compatible." : null,
        null);
    public static SaveLoadResult Failure(string error) => new(null, null, false, null, error);
}

public enum SaveFailurePoint
{
    AfterTemporaryValidationBeforeReplace = 1,
}

public sealed record SaveMigrationResult(SaveEnvelopeV1? Envelope, string? Error)
{
    public bool IsSuccess => Envelope is not null;
}

public static class SaveMigrationPipeline
{
    public const int CurrentSchemaVersion = 1;

    public static SaveMigrationResult Migrate(SaveEnvelopeV1 envelope) => envelope.Header.SchemaVersion switch
    {
        CurrentSchemaVersion => new SaveMigrationResult(envelope, null),
        _ => new SaveMigrationResult(null,
            $"Save schema version {envelope.Header.SchemaVersion} is not supported. This build supports v1 only; an explicit migration is required."),
    };
}
