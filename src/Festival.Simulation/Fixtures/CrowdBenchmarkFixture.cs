using System.Security.Cryptography;
using System.Text;

namespace Festival.Simulation.Fixtures;

public enum BenchmarkPassage { Narrow, Wide }

public sealed record CrowdBenchmarkWave(GameSession Session, EntityId QueueId, long StartTick);

/// <summary>Reproducible M0.10 harness: multiple production queue sessions represent independent service destinations/waves.</summary>
public sealed class CrowdBenchmarkFixture
{
    public const int DestinationCount = 8;
    public const ulong Seed = 20260916;
    public IReadOnlyList<CrowdBenchmarkWave> Waves { get; }
    public long ControllerTick { get; private set; }
    public int TotalAgents { get; }
    public BenchmarkPassage Passage { get; }

    private CrowdBenchmarkFixture(int totalAgents, BenchmarkPassage passage, IReadOnlyList<CrowdBenchmarkWave> waves)
    { TotalAgents = totalAgents; Passage = passage; Waves = waves; }

    public static CrowdBenchmarkFixture Create(int totalAgents, BenchmarkPassage passage)
    {
        if (totalAgents is not (50 or 200 or 500 or 1200)) throw new ArgumentOutOfRangeException(nameof(totalAgents));
        var waves = new List<CrowdBenchmarkWave>();
        var remaining = totalAgents;
        for (var wave = 0; wave < DestinationCount; wave++)
        {
            var count = remaining / (DestinationCount - wave); remaining -= count;
            var terrain = CreatePassage(passage, wave);
            var starts = Enumerable.Range(0, count).Select(i => new GridCell(28 + i % 20, 34 + i / 20 * 2)).ToArray();
            var slots = Enumerable.Range(0, count).Select(i => new GridCell(188 + i % 20, 34 + i / 20 * 2)).ToArray();
            var exits = Enumerable.Range(0, count).Select(i => new GridCell(220 + i % 20, 34 + i / 20 * 2)).ToArray();
            var session = new GameSession(Seed + (ulong)wave, new CampaignId(Seed + (ulong)wave));
            var command = new InitializeServiceQueueFixtureCommand(starts, slots, exits, terrain,
                Enumerable.Repeat(500L, count).ToArray(), count, 120, 300, 1, PhysicalArrivalAdmission: true);
            var result = session.Execute(new CommandEnvelope(new CommandId(1), session.CampaignId, session.Phase,
                session.CurrentTick, session.NextSubmissionSequence, null, command));
            if (!result.IsAccepted || result.TargetId is null) throw new InvalidOperationException(result.Message);
            waves.Add(new CrowdBenchmarkWave(session, result.TargetId.Value, wave * 80L));
        }
        return new(totalAgents, passage, waves);
    }

    public void AdvanceOneTick()
    {
        foreach (var wave in Waves.Where(item => item.StartTick <= ControllerTick)) wave.Session.AdvanceTicks(1);
        ControllerTick++;
    }

    public int Completed => Waves.Sum(wave => wave.Session.Transactions.Count);
    public int Backlog => Waves.Sum(wave => wave.Session.CaptureSnapshot().ServiceQueues.Single().OrderedMembers.Count);
    public int RouteFailures => Waves.Sum(wave => wave.Session.CaptureSnapshot().ServiceQueues.Single().Agents.Count(agent => agent.Action == ServiceQueueAgentAction.Failed));
    public int Reserved => Waves.Sum(wave => wave.Session.CaptureSnapshot().ServiceQueues.Single().Agents.Count(agent => agent.ReservedSlotIndex is not null || agent.OwnsExitReservation));
    public bool AllCompleted => Completed == TotalAgents && Waves.All(wave => wave.Session.CaptureSnapshot().ServiceQueues.Single().Agents.All(agent => agent.Action == ServiceQueueAgentAction.Completed));

    public string CompositeHash()
    {
        var input = string.Join('|', Waves.Select(wave => wave.Session.CaptureSnapshot().AuthoritativeHash));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static TerrainCellOverride[] CreatePassage(BenchmarkPassage passage, int wave)
    {
        var width = passage == BenchmarkPassage.Narrow ? 3 : 17;
        var centre = 80 + wave * 12;
        var openMin = centre - width / 2; var openMax = openMin + width - 1;
        return Enumerable.Range(0, TraversalGrid.Depth)
            .Where(z => z < openMin || z > openMax)
            .Select(z => new TerrainCellOverride(new GridCell(128, z), GroundSurface.Grass, false)).ToArray();
    }
}
