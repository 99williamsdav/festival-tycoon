namespace Festival.Simulation;

public sealed class GameSession
{
    public const int TickDurationMilliseconds = 250;

    private readonly SortedDictionary<EntityId, FixtureRecordState> _fixtureRecords = [];
    private readonly SortedSet<CommandId> _acceptedCommandIds = [];
    private readonly List<AppliedCommand> _appliedCommands = [];
    private readonly SortedDictionary<RandomStreamId, Pcg32Random> _randomStreams = [];

    public GameSession(ulong campaignSeed, CampaignId? campaignId = null)
    {
        CampaignSeed = campaignSeed;
        CampaignId = campaignId ?? new CampaignId(campaignSeed);

        foreach (var streamId in Enum.GetValues<RandomStreamId>())
        {
            _randomStreams.Add(streamId, RandomStreamFactory.Create(campaignSeed, streamId));
        }
    }

    public CampaignId CampaignId { get; }

    public ulong CampaignSeed { get; }

    public SessionPhase Phase { get; private set; } = SessionPhase.Live;

    public long CurrentTick { get; private set; }

    public bool IsPaused { get; private set; }

    public RequestedSpeed RequestedSpeed { get; private set; } = RequestedSpeed.OneX;

    public ulong NextEntityId { get; private set; } = 1;

    public ulong NextSubmissionSequence { get; private set; }

    public IReadOnlyList<AppliedCommand> AppliedCommands => _appliedCommands.AsReadOnly();

    internal IReadOnlyDictionary<EntityId, FixtureRecordState> FixtureRecords => _fixtureRecords;

    internal IReadOnlySet<CommandId> AcceptedCommandIds => _acceptedCommandIds;

    internal IReadOnlyDictionary<RandomStreamId, Pcg32Random> RandomStreams => _randomStreams;

    public CommandResult Execute(CommandEnvelope envelope)
    {
        var rejection = ValidateEnvelope(envelope);
        if (rejection is not null)
        {
            return rejection;
        }

        EntityId? affectedTarget;
        switch (envelope.Command)
        {
            case CreateFixtureRecordCommand create:
                affectedTarget = new EntityId(NextEntityId);
                _fixtureRecords.Add(affectedTarget.Value, new FixtureRecordState
                {
                    Id = affectedTarget.Value,
                    Value = create.InitialValue,
                    RemainingTicks = create.ExpiresAfterTicks,
                });
                NextEntityId++;
                break;

            case ChangeFixtureValueCommand change:
                affectedTarget = envelope.TargetId;
                _fixtureRecords[affectedTarget!.Value].Value = change.NewValue;
                break;

            case SetPausedCommand pause:
                affectedTarget = null;
                IsPaused = pause.IsPaused;
                break;

            default:
                return CommandResult.Rejected(CommandReasonCode.UnknownCommand, "Command type is not supported.");
        }

        _acceptedCommandIds.Add(envelope.CommandId);
        _appliedCommands.Add(new AppliedCommand(
            envelope.CommandId,
            CurrentTick,
            envelope.SubmissionSequence,
            envelope.Command.GetType().Name,
            affectedTarget));
        NextSubmissionSequence++;
        return CommandResult.Accepted(affectedTarget);
    }

    public AdvanceResult AdvanceTicks(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (IsPaused || count == 0)
        {
            return new AdvanceResult(CaptureSnapshot(), Array.Empty<SessionEvent>());
        }

        var events = new List<SessionEvent>();
        for (var index = 0; index < count; index++)
        {
            CurrentTick++;
            foreach (var record in _fixtureRecords.Values)
            {
                if (record.HasExpired || record.RemainingTicks <= 0)
                {
                    continue;
                }

                record.RemainingTicks--;
                if (record.RemainingTicks == 0)
                {
                    record.HasExpired = true;
                    events.Add(new SessionEvent(CurrentTick, "fixture_record_expired", record.Id));
                }
            }
        }

        return new AdvanceResult(CaptureSnapshot(), events);
    }

    public uint NextRandom(RandomStreamId streamId) => _randomStreams[streamId].NextUInt32();

    public void RequestSpeed(RequestedSpeed speed)
    {
        if (!Enum.IsDefined(speed))
        {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }

        RequestedSpeed = speed;
    }

    public SessionSnapshot CaptureSnapshot()
    {
        var records = _fixtureRecords.Values
            .Select(record => new FixtureRecordSnapshot(
                record.Id,
                record.Value,
                record.RemainingTicks,
                record.HasExpired))
            .ToArray();

        return new SessionSnapshot(
            CampaignId,
            CampaignSeed,
            Phase,
            CurrentTick,
            IsPaused,
            RequestedSpeed,
            NextEntityId,
            NextSubmissionSequence,
            records,
            CanonicalStateHasher.Compute(this));
    }

    private CommandResult? ValidateEnvelope(CommandEnvelope envelope)
    {
        if (_acceptedCommandIds.Contains(envelope.CommandId))
        {
            return CommandResult.Rejected(CommandReasonCode.DuplicateCommand, "Command ID was already accepted.");
        }

        if (envelope.CampaignId != CampaignId)
        {
            return CommandResult.Rejected(CommandReasonCode.WrongCampaign, "Command campaign does not match this session.");
        }

        if (envelope.ExpectedPhase != Phase)
        {
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Command is not valid in the current phase.");
        }

        if (envelope.Tick != CurrentTick)
        {
            return CommandResult.Rejected(CommandReasonCode.WrongTick, "Command must target the current deterministic tick.");
        }

        if (envelope.SubmissionSequence != NextSubmissionSequence)
        {
            return CommandResult.Rejected(CommandReasonCode.OutOfOrderSubmission, "Command submission sequence is not next.");
        }

        return envelope.Command switch
        {
            CreateFixtureRecordCommand create when envelope.TargetId is not null || create.ExpiresAfterTicks <= 0 =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Fixture creation requires no target and a positive expiry."),
            ChangeFixtureValueCommand when envelope.TargetId is null || !_fixtureRecords.ContainsKey(envelope.TargetId.Value) =>
                CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Fixture target does not exist."),
            SetPausedCommand when envelope.TargetId is not null =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Pause command does not accept a target."),
            CreateFixtureRecordCommand or ChangeFixtureValueCommand or SetPausedCommand => null,
            _ => CommandResult.Rejected(CommandReasonCode.UnknownCommand, "Command type is not supported."),
        };
    }
}
