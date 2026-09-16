using System.Security.Cryptography;
using System.Text;

namespace Festival.Simulation;

internal static class CanonicalStateHasher
{
    private const int PreviousSchemaVersion = 2;
    private const int QueueSchemaVersion = 3;
    private const int PhysicalQueueModeSchemaVersion = 4;

    public static string Compute(GameSession session) => Compute(session, PhysicalQueueModeSchemaVersion, includePhysicalQueueMode: true);
    internal static string ComputeQueueCompatibility(GameSession session, bool includePhysicalQueueMode) =>
        Compute(session, QueueSchemaVersion, includePhysicalQueueMode);

    private static string Compute(GameSession session, int queueSchemaVersion, bool includePhysicalQueueMode)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);

        var includesQueues = session.ServiceQueues.Count > 0;
        writer.Write(includesQueues ? queueSchemaVersion : PreviousSchemaVersion);
        writer.Write(GameSession.TickDurationMilliseconds);
        writer.Write(Pcg32Random.AlgorithmVersion);
        writer.Write(session.CampaignId.Value);
        writer.Write(session.CampaignSeed);
        writer.Write((int)session.Phase);
        writer.Write(session.CurrentTick);
        writer.Write(session.IsPaused);
        writer.Write(session.NextEntityId);
        writer.Write(session.NextSubmissionSequence);

        writer.Write(session.FixtureRecords.Count);
        foreach (var pair in session.FixtureRecords)
        {
            writer.Write(pair.Key.Value);
            writer.Write(pair.Value.Value);
            writer.Write(pair.Value.RemainingTicks);
            writer.Write(pair.Value.HasExpired);
        }

        writer.Write(session.Wallets.Count);
        foreach (var pair in session.Wallets)
        {
            writer.Write(pair.Key.Value);
            writer.Write(pair.Value.CashPennies);
        }

        writer.Write(session.FestivalFinances.Count);
        foreach (var pair in session.FestivalFinances)
        {
            writer.Write(pair.Key.Value);
            writer.Write(pair.Value.CashPennies);
        }

        writer.Write(session.OwnedStocks.Count);
        foreach (var pair in session.OwnedStocks)
        {
            writer.Write(pair.Key.Value);
            writer.Write(pair.Value.OwnerId.Value);
            writer.Write(pair.Value.Quantity);
            writer.Write(pair.Value.UnitCostBasisPennies);
        }

        writer.Write(session.Transactions.Count);
        foreach (var transaction in session.Transactions)
        {
            writer.Write(transaction.Id.Value);
            writer.Write(transaction.CommandId.Value);
            writer.Write(transaction.Tick);
            writer.Write(transaction.BuyerId.Value);
            writer.Write(transaction.FestivalId.Value);
            writer.Write(transaction.ServiceId.Value);
            writer.Write(transaction.Quantity);
            writer.Write(transaction.UnitPricePennies);
            writer.Write(transaction.Entries.Count);
            foreach (var entry in transaction.Entries)
            {
                writer.Write(entry.OwnerId.Value);
                writer.Write((int)entry.Account);
                writer.Write(entry.AmountPennies);
            }
        }

        writer.Write(session.AcceptedCommandIds.Count);
        foreach (var commandId in session.AcceptedCommandIds)
        {
            writer.Write(commandId.Value);
        }

        writer.Write(session.AppliedCommands.Count);
        foreach (var command in session.AppliedCommands)
        {
            writer.Write(command.CommandId.Value);
            writer.Write(command.Tick);
            writer.Write(command.SubmissionSequence);
            writer.Write(command.CommandType);
            writer.Write(command.TargetId.HasValue);
            if (command.TargetId is { } targetId)
            {
                writer.Write(targetId.Value);
            }
        }

        var authoritativeStreams = session.RandomStreams
            .Where(pair => pair.Key != RandomStreamId.Cosmetic)
            .OrderBy(pair => pair.Key)
            .ToArray();
        writer.Write(authoritativeStreams.Length);
        foreach (var pair in authoritativeStreams)
        {
            writer.Write((int)pair.Key);
            writer.Write(pair.Value.State);
            writer.Write(pair.Value.Increment);
        }

        if (session.TraversalGrid is not null)
        {
            writer.Write("navigation-v1");
            writer.Write(TraversalGrid.Width);
            writer.Write(TraversalGrid.Depth);
            writer.Write(TraversalGrid.CellSizeMillimetres);
            writer.Write(session.TraversalGrid.Overrides.Count);
            foreach (var item in session.TraversalGrid.Overrides.Values)
            {
                writer.Write(item.Cell.X); writer.Write(item.Cell.Z); writer.Write((int)item.Surface);
                writer.Write(item.IsWalkable); writer.Write(item.CostPermille);
                writer.Write(item.ElevationMillimetres); writer.Write(item.SlopePermille);
            }
            writer.Write(session.NavigationAgents.Count);
            foreach (var agent in session.NavigationAgents.Values)
            {
                writer.Write(agent.Id.Value); writer.Write(agent.XMillimetres); writer.Write(agent.ZMillimetres);
                writer.Write((int)agent.Action); writer.Write(agent.Destination.HasValue);
                if (agent.Destination is { } destination) { writer.Write(destination.X); writer.Write(destination.Z); }
                writer.Write(agent.Route.Count);
                foreach (var cell in agent.Route) { writer.Write(cell.X); writer.Write(cell.Z); }
                writer.Write(agent.RouteIndex); writer.Write(agent.SegmentOriginXMillimetres); writer.Write(agent.SegmentOriginZMillimetres);
                writer.Write(agent.SegmentProgressMicrometres);
                writer.Write(agent.MovementRemainder); writer.Write(agent.LastSearchExpandedNodes);
                writer.Write(agent.WalkingSpeedPermille);
                if (includesQueues)
                {
                    writer.Write(agent.IntentId is not null);
                    if (agent.IntentId is not null) writer.Write(agent.IntentId);
                }
            }
        }


        if (includesQueues)
        {
            writer.Write(session.ServiceQueues.Count);
            foreach (var queue in session.ServiceQueues.Values)
            {
                writer.Write(queue.Id.Value); writer.Write(queue.FestivalId.Value); writer.Write(queue.ServiceId.Value);
                writer.Write(queue.IsOpen); writer.Write(queue.UnitPricePennies); writer.Write(queue.ServiceDurationTicks);
                writer.Write(queue.OrderedMembers.Count); foreach (var id in queue.OrderedMembers) writer.Write(id.Value);
                writer.Write(queue.ActiveOwnerId.HasValue); if (queue.ActiveOwnerId is { } owner) writer.Write(owner.Value);
                writer.Write(queue.RemainingServiceTicks); writer.Write(queue.CompletionSequence); writer.Write(queue.NeedsReassignment);
                writer.Write(queue.NextArrivalSequence);
                if (includePhysicalQueueMode) writer.Write(queue.PhysicalArrivalAdmission);
                writer.Write(queue.QueueSlots.Count); foreach (var cell in queue.QueueSlots) { writer.Write(cell.X); writer.Write(cell.Z); }
                writer.Write(queue.ExitCells.Count); foreach (var cell in queue.ExitCells) { writer.Write(cell.X); writer.Write(cell.Z); }
                writer.Write(queue.Agents.Count);
                foreach (var agent in queue.Agents.Values)
                {
                    writer.Write(agent.AgentId.Value); writer.Write((int)agent.Action); writer.Write(agent.ReservedSlotIndex.HasValue);
                    if (agent.ReservedSlotIndex is { } slot) writer.Write(slot);
                    writer.Write(agent.ExitIndex); writer.Write(agent.OwnsExitReservation); writer.Write(agent.AdmissionTick); writer.Write(agent.ArrivalSequence);
                }
            }
        }

        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(memory.GetBuffer().AsSpan(0, checked((int)memory.Length))))
            .ToLowerInvariant();
    }
}
