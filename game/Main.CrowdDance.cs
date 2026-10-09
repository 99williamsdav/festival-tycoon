using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The audience dances while a set is live: a sway when they're mildly into it, a bop when they're enjoying it, arms up
/// when they love it, at the tempo of the act's genre, each a touch off the beat. How much they move follows how much
/// they're enjoying it: their taste for the act, taken down by a poor performance; anyone not enjoying it just stands
/// and watches. It follows the festival clock, so it stops when paused and quickens at 2× and 4×. Presentation only.
/// </summary>
public partial class Main
{
    // The dance clips are one bar at 120 bpm; each genre plays them at its own tempo.
    private const float DanceClipBpm = 120;
    private static float? DanceBpm(int genre) => genre switch
    {
        FestivalGenre.Folk => 96, FestivalGenre.Indie => 118, FestivalGenre.Pop => 120,
        FestivalGenre.Electronic => 126, FestivalGenre.Punk => 170, FestivalGenre.Metal => 140, _ => null,
    };

    // Each dancing listener's tempo and how into it they are, from their own stage's set.
    private readonly Dictionary<ulong, (float Bpm, int Keenness)> _dance = [];
    // Below this much enjoyment they don't dance at all; above the next two they bop, then let go.
    private const int DanceFrom = 40, BopFrom = 60, FullFrom = 80;

    /// <summary>Works out once a frame which crowds should be dancing, and to what: each to its own stage's act.</summary>
    private void PrepareCrowdDance(IReadOnlyList<LivePerformanceSnapshot> lives)
    {
        _dance.Clear();
        foreach (var live in lives)
        {
            if (live.Stage != LiveSetStage.Live || !_session.StagePoweredAt(live.StageId) ||
                _session.CurrentStageAct(live.StageId) is not { } act || DanceBpm(act.Genre) is not { } bpm) continue;
            // A sloppy or badly mixed set takes the edge off even a fan's enjoyment.
            var played = _session.CurrentStagePerformance(live.StageId) is { } performance ? 50 + Math.Clamp(performance.Overall, 0, 100) / 2 : 100;
            foreach (var listener in live.Listeners.Where(l => l.AtPlace)) _dance[listener.AgentId] = (bpm, listener.Enthusiasm * played / 100);
        }
    }

    /// <summary>Tells a listener's rig which dance to do this frame, if any; the rig plays it when they're standing with free hands.</summary>
    private void SetCrowdDance(EntityId id, Node3D visual)
    {
        if (!_dance.TryGetValue(id.Value, out var dance) || dance.Keenness < DanceFrom)
        { Bodies.SetDance(visual, null, 0, 0); return; }
        var keenness = dance.Keenness;
        var clip = keenness >= FullFrom ? "dance_full" : keenness >= BopFrom ? "dance_bop" : "dance_sway";
        // Each person a little off the beat, so it reads as a crowd rather than a drill.
        var offset = (id.Value * 2654435761UL % 1000) / 1000f * .35f;
        Bodies.SetDance(visual, clip, dance.Bpm / DanceClipBpm * (float)_host.Clock.RequestedSpeed, offset);
    }
}
