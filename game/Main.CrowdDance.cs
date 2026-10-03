using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The audience moves to the music while a set is live: folk fans sway, indie fans nod, pop and electronic fans
/// bounce, punks pogo and metalheads headbang. Mostly on the beat, each a touch off it, and keener listeners move
/// more. It follows the festival clock, so it stops when paused and quickens at 2× and 4×. Presentation only.
/// </summary>
public partial class Main
{
    private sealed record DanceStyle(float Bpm, float Bounce, float Sway, float Nod);

    // Bounce in metres, sway and nod in radians.
    private static DanceStyle? DanceFor(int genre) => genre switch
    {
        FestivalGenre.Folk => new(96, .02f, .07f, 0),
        FestivalGenre.Indie => new(118, .03f, 0, .08f),
        FestivalGenre.Pop => new(120, .07f, .03f, 0),
        FestivalGenre.Electronic => new(126, .06f, 0, .05f),
        FestivalGenre.Punk => new(170, .16f, 0, 0),
        FestivalGenre.Metal => new(140, 0, 0, .22f),
        _ => null,
    };

    private (DanceStyle Style, Dictionary<ulong, int> Keenness)? _dance;

    /// <summary>Works out once a frame whether the crowd should be dancing, and to what.</summary>
    private void PrepareCrowdDance(LivePerformanceSnapshot? live)
    {
        _dance = null;
        if (live?.Stage != LiveSetStage.Live || (_session.CaptureEquipment()?.LoadPercent ?? 80) == 0 ||
            _session.CurrentFestivalAct is not { } act || DanceFor(act.Genre) is not { } style) return;
        _dance = (style, live.Listeners.Where(l => l.AtPlace).ToDictionary(l => l.AgentId, l => l.Enthusiasm));
    }

    /// <summary>Moves one listener to the beat; their resting position and facing have already been set this frame.</summary>
    private void ApplyCrowdDance(EntityId id, Node3D visual)
    {
        if (_dance is not { } dance || !dance.Keenness.TryGetValue(id.Value, out var keenness)) return;
        var style = dance.Style;
        var seconds = (_session.CurrentTick + _host.Clock.InterpolationFraction) / 80.0;
        // Each person a little off the beat, so it reads as a crowd rather than a drill.
        var offset = (id.Value * 2654435761UL % 1000) / 1000f * .35f;
        var beat = (float)(seconds * style.Bpm / 60.0) + offset;
        var phase = beat - MathF.Floor(beat);
        var keen = .45f + Math.Clamp(keenness, 0, 100) / 100f * .75f;
        // A bounce is a quick lift and a drop on every beat; a sway and a nod swing back and forth over two.
        var lift = MathF.Sin(phase * MathF.PI);
        var swing = MathF.Sin(beat * MathF.PI);
        visual.Position += new Vector3(0, style.Bounce * keen * lift, 0);
        visual.Rotation = new Vector3(style.Nod * keen * lift, visual.Rotation.Y, style.Sway * keen * swing);
    }
}
