using Festival.Simulation;

namespace Festival.ContentAdapter;

public readonly record struct FightPairKey(ulong Initiator, ulong Opponent, long Tick);
public sealed record FightPresentationPair(FightPairKey Key, ulong? Handler, bool Moving, double DistanceMetres);

/// <summary>Only current authoritative encounters, never speech cues or historical injury records.</summary>
public static class FightPresentation
{
    public static IReadOnlyList<FightPresentationPair> Read(GameSession session)
    {
        if (session.PreparedStatus != PreparationStatus.Running || session.CaptureDisorder() is not { } disorder) return [];
        var people = session.CapturePreparation()!.People.ToDictionary(p => p.AgentId);
        var conduct = disorder.People.ToDictionary(p => p.AgentId);
        var navigation = session.CaptureObservation().NavigationAgents.ToDictionary(p => p.Id.Value);
        var unavailable = (session.CaptureMedical()?.Needs ?? []).Where(p => p.Stage is MedicalStage.Collapsed or MedicalStage.Critical or MedicalStage.Removed or MedicalStage.Terminal || p.Intent == MedicalIntent.Leaving)
            .Select(p => p.AgentId).ToHashSet();
        foreach (var medic in session.CaptureMedical()?.Medics ?? [])
            if (medic.Stage is MedicalResponseStage.Treating or MedicalResponseStage.Removing && medic.PatientId is { } patient) unavailable.Add(patient);
        var stewards = session.GetStewardResponses();
        bool Available(ulong id) => people.TryGetValue(id, out var person) && person.Admitted && !person.Departed &&
            !unavailable.Contains(id) && navigation.ContainsKey(id);
        var result = new List<FightPresentationPair>();
        foreach (var incident in disorder.Incidents)
        {
            if (incident.InjuryTick != -1 || !Available(incident.InitiatorId) || !Available(incident.OpponentId) ||
                !conduct.TryGetValue(incident.InitiatorId, out var first) || first.Stage != DisorderStage.Fight ||
                first.OpponentId != incident.OpponentId || first.StageTick != incident.FightTick) continue;
            var stewardOpponent = stewards.Any(p => p.WorkerId == incident.OpponentId && !p.Incapacitated);
            if (!stewardOpponent && (!conduct.TryGetValue(incident.OpponentId, out var second) || second.Stage != DisorderStage.Fight ||
                second.OpponentId != incident.InitiatorId || second.StageTick != incident.FightTick)) continue;
            ulong? handler = null;
            if (!stewardOpponent && incident.HandlingAttempt is { Outcome: FightHandlingOutcome.Handling } attempt &&
                stewards.Any(p => p.WorkerId == attempt.WorkerId && p.Stage == SecurityResponseStage.Confronting &&
                    p.TargetId is { } target && (target == incident.InitiatorId || target == incident.OpponentId))) handler = attempt.WorkerId;
            var a = navigation[incident.InitiatorId]; var b = navigation[incident.OpponentId];
            var dx = (double)a.XMillimetres - b.XMillimetres; var dz = (double)a.ZMillimetres - b.ZMillimetres;
            result.Add(new(new(incident.InitiatorId, incident.OpponentId, incident.FightTick), handler,
                a.Action == AgentNavigationAction.Travelling || b.Action == AgentNavigationAction.Travelling, Math.Sqrt(dx * dx + dz * dz) / 1000));
        }
        return result;
    }
}
