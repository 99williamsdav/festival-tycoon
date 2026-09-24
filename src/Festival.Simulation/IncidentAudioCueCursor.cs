namespace Festival.Simulation;

/// <summary>
/// Presentation-only cursor over authoritative incident evidence. It never changes
/// simulation state, and loading a snapshot resets the cursor instead of replaying
/// historical sound effects.
/// </summary>
public sealed class IncidentAudioCueCursor
{
    private int _equipmentEvidenceCount;
    private int _medicalEvidenceCount;
    private bool _initialized;

    public void Reset(EquipmentSnapshot? equipment, MedicalSnapshot? medical)
    {
        _equipmentEvidenceCount = equipment?.Evidence.Length ?? 0;
        _medicalEvidenceCount = medical?.Evidence.Length ?? 0;
        _initialized = true;
    }

    public IReadOnlyList<IncidentAudioCue> Observe(EquipmentSnapshot? equipment, MedicalSnapshot? medical, long currentTick)
    {
        if (!_initialized)
        {
            Reset(equipment, medical);
            return [];
        }

        var cues = new List<IncidentAudioCue>();
        var equipmentEvidence = equipment?.Evidence ?? [];
        if (equipmentEvidence.Length >= _equipmentEvidenceCount)
            foreach (var item in equipmentEvidence.Skip(_equipmentEvidenceCount))
                if (Fresh(item.Tick, currentTick) && item.Id == "equipment:death")
                {
                    cues.Add(new(IncidentAudioCueKind.GeneratorExplosion, item.Tick));
                    cues.Add(new(IncidentAudioCueKind.DeathScream, item.Tick));
                }
        _equipmentEvidenceCount = equipmentEvidence.Length;

        var medicalEvidence = medical?.Evidence ?? [];
        if (medicalEvidence.Length >= _medicalEvidenceCount)
            foreach (var item in medicalEvidence.Skip(_medicalEvidenceCount))
                if (Fresh(item.Tick, currentTick) && item.Id == "medical:death")
                    cues.Add(new(IncidentAudioCueKind.DeathScream, item.Tick));
        _medicalEvidenceCount = medicalEvidence.Length;
        return cues;
    }

    private static bool Fresh(long eventTick, long currentTick) =>
        eventTick >= 0 && eventTick <= currentTick && currentTick - eventTick <= 80;
}

public enum IncidentAudioCueKind { GeneratorExplosion, DeathScream }
public sealed record IncidentAudioCue(IncidentAudioCueKind Kind, long Tick);
