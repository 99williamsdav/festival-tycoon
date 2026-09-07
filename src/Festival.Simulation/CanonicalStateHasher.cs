using System.Security.Cryptography;
using System.Text;

namespace Festival.Simulation;

internal static class CanonicalStateHasher
{
    private const int SchemaVersion = 1;

    public static string Compute(GameSession session)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);

        writer.Write(SchemaVersion);
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

        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(memory.GetBuffer().AsSpan(0, checked((int)memory.Length))))
            .ToLowerInvariant();
    }
}
