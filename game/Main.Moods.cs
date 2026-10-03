using Festival.Simulation;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// A guest's mood at a glance: a small face pops up over their head when their satisfaction rises or falls sharply,
/// green for a lift, amber for a dip, red when an unhappy guest takes another knock. Each shows for a few seconds,
/// and a guest won't pop again straight away. Read from the simulation; presentation only.
/// </summary>
public partial class Main
{
    // Satisfaction drifts slowly, so a swing of 60 (of 10,000) over ten seconds is a noticeable change of heart:
    // in a test day about a hundred of them across twenty guests, one every few seconds.
    private const int MoodSwing = 60, MoodWindowSamples = 20;
    private const double MoodShowSeconds = 3.5, MoodCooldownSeconds = 12;
    private readonly Dictionary<ulong, Queue<int>> _moodHistory = [];
    private readonly Dictionary<ulong, (Sprite3D Face, double Age)> _moodFaces = [];
    private readonly Dictionary<ulong, double> _moodCooldown = [];
    private long _moodSampledTick = long.MinValue;
    private GameSession? _moodSession;

    private static Texture2D MoodTexture(string mood) => GD.Load<Texture2D>($"res://assets/ui/moods/mood-{mood}.svg");

    private void ProcessMoods(double delta)
    {
        var step = _session.IsPaused ? 0 : delta;
        foreach (var id in _moodFaces.Keys.ToArray())
        {
            var (face, age) = _moodFaces[id];
            age += step;
            if (age >= MoodShowSeconds || !IsInstanceValid(face)) { if (IsInstanceValid(face)) face.QueueFree(); _moodFaces.Remove(id); continue; }
            _moodFaces[id] = (face, age);
            // A little pop in, then a fade out.
            face.Modulate = new Color(1, 1, 1, Mathf.Clamp((float)(MoodShowSeconds - age) / .6f, 0, 1));
            face.Position = new Vector3(0, 2.25f + Mathf.Min((float)age, .25f) * .6f, 0);
        }
        foreach (var id in _moodCooldown.Keys.ToArray()) _moodCooldown[id] -= step;
        if (_moodSession != _session)
        {
            _moodSession = _session; _moodHistory.Clear(); _moodCooldown.Clear(); _moodSampledTick = long.MinValue;
            foreach (var (face, _) in _moodFaces.Values) if (IsInstanceValid(face)) face.QueueFree();
            _moodFaces.Clear();
        }
        if (_session.PreparedStatus is not (PreparationStatus.Running or PreparationStatus.Departing) || _session.IsPaused) return;
        // Sampled every half festival-second, so the window means the same at 1×, 2× and 4×.
        if (_session.CurrentTick - _moodSampledTick < 40) return;
        _moodSampledTick = _session.CurrentTick;
        foreach (var person in _session.CapturePreparation()!.People)
        {
            if (person.Role != ProtectedPersonRole.Guest || !person.Admitted || person.Departed) { _moodHistory.Remove(person.AgentId); continue; }
            if (!_moodHistory.TryGetValue(person.AgentId, out var history)) _moodHistory[person.AgentId] = history = new();
            history.Enqueue(person.Satisfaction);
            if (history.Count < MoodWindowSamples) continue;
            var then = history.Dequeue();
            var change = person.Satisfaction - then;
            if (System.Math.Abs(change) < MoodSwing || _moodCooldown.GetValueOrDefault(person.AgentId) > 0 ||
                !_attendeeVisuals.TryGetValue(new EntityId(person.AgentId), out var visual) || !visual.Visible) continue;
            var mood = change > 0 ? "happy" : person.Satisfaction < 3_500 ? "angry" : "unhappy";
            ShowMood(person.AgentId, visual, mood);
            history.Clear();
        }
    }

    private void ShowMood(ulong id, Node3D visual, string mood)
    {
        if (_moodFaces.Remove(id, out var old) && IsInstanceValid(old.Face)) old.Face.QueueFree();
        var face = new Sprite3D
        {
            Texture = MoodTexture(mood), PixelSize = .0009f, FixedSize = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true, RenderPriority = 3, Position = new Vector3(0, 2.25f, 0), TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            // Kept out of the first-person view, like the other floating labels.
            Layers = EyeHiddenLayer,
        };
        visual.AddChild(face);
        _moodFaces[id] = (face, 0);
        _moodCooldown[id] = MoodCooldownSeconds;
    }
}
