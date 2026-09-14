namespace Festival.Simulation.Fixtures;

public sealed record ServiceQueueFixtureState(
    GameSession Session, EntityId QueueId, EntityId FestivalId, EntityId ServiceId,
    IReadOnlyList<EntityId> AgentIds, IReadOnlyList<GridCell> QueueSlots);

/// <summary>Development-only five-attendee setup for M0.08 verification.</summary>
public static class ServiceQueueFixture
{
    // CONTENT_AND_BALANCE §4: small bar service is 90 festival seconds at four ticks/second.
    public const int DefaultServiceDurationTicks = 360;
    public const long DefaultPricePennies = 300;

    public static ServiceQueueFixtureState Create(
        IReadOnlyList<long>? cash = null, int stock = 5, int durationTicks = DefaultServiceDurationTicks,
        IReadOnlyList<GridCell>? starts = null)
    {
        var session = new GameSession(20260915, new CampaignId(20260915));
        var serviceObject = LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.service-point");
        var service = TraversalGrid.WorldToCell((int)(serviceObject.XMetres * 1000), (int)(serviceObject.ZMetres * 1000));
        var queueSlots = Enumerable.Range(0, 5).Select(index => new GridCell(service.X - index * 3, service.Z + index * 2)).ToArray();
        var gate = TraversalGrid.WorldToCell(0, 30_000);
        var startCells = starts ?? Enumerable.Range(0, 5).Select(index => new GridCell(gate.X - 4 + index * 2, gate.Z - index)).ToArray();
        var exits = Enumerable.Range(0, 5).Select(index => new GridCell(gate.X - 4 + index * 2, gate.Z + 4 + index)).ToArray();
        var command = new InitializeServiceQueueFixtureCommand(
            startCells, queueSlots, exits, NavigationFixture.CreateLowerWitteringTerrain(),
            cash ?? Enumerable.Repeat(500L, 5).ToArray(), stock, 120, DefaultPricePennies, durationTicks);
        var result = session.Execute(Envelope(session, new CommandId(1), null, command));
        if (!result.IsAccepted || result.TargetId is null) throw new InvalidOperationException(result.Message);
        var queue = session.CaptureSnapshot().ServiceQueues.Single();
        return new ServiceQueueFixtureState(session, queue.Id, queue.FestivalId, queue.ServiceId,
            queue.Agents.Select(item => item.AgentId).ToArray(), queueSlots);
    }

    public static CommandResult SetOpen(ServiceQueueFixtureState fixture, bool open, ulong commandId) =>
        fixture.Session.Execute(Envelope(fixture.Session, new CommandId(commandId), fixture.QueueId, new SetServiceQueueOpenCommand(open)));

    public static CommandResult Enqueue(ServiceQueueFixtureState fixture, EntityId agentId, ulong arrivalSequence, ulong commandId) =>
        fixture.Session.Execute(Envelope(fixture.Session, new CommandId(commandId), fixture.QueueId,
            new EnqueueServiceQueueAgentCommand(agentId, arrivalSequence)));

    public static CommandResult Abandon(ServiceQueueFixtureState fixture, EntityId agentId, ulong commandId) =>
        fixture.Session.Execute(Envelope(fixture.Session, new CommandId(commandId), fixture.QueueId, new AbandonServiceQueueCommand(agentId)));

    public static void AdvanceUntilResolved(GameSession session, int maximumTicks = 10_000)
    {
        var ticks = 0;
        while (session.CaptureSnapshot().ServiceQueues.Single().OrderedMembers.Count > 0 && ticks++ < maximumTicks) session.AdvanceTicks(1);
        if (ticks >= maximumTicks) throw new InvalidOperationException("Queue fixture did not resolve within its deterministic tick budget.");
    }

    private static CommandEnvelope Envelope(GameSession session, CommandId commandId, EntityId? target, SessionCommand command) =>
        new(commandId, session.CampaignId, session.Phase, session.CurrentTick, session.NextSubmissionSequence, target, command);
}
