using Festival.Simulation;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The player steward up on a stage: the camera and their body ride up the steps to the deck, and the crowd's and the
/// band's shouts at them (the sim's keyed pick, <see cref="GameSession.StageShouts"/>) show as speech bubbles that stay
/// visible in first person, where other remarks are hidden. Control says something on the radio as they get up there.
/// Presentation only.
/// </summary>
public partial class Main
{
    private const string StageShoutMeta = "StageShout";
    private readonly Dictionary<(bool Band, long Tick), (Label3D Label, ulong Speaker)> _stageShouts = [];
    private (string? Stage, bool Invaded) _stageSeen;

    /// <summary>How far up a stage's steps or deck the player steward stands, in metres above the grass; 0 off them.</summary>
    private float PlayerStageLift(int xMillimetres, int zMillimetres)
    {
        var cell = TraversalGrid.WorldToCell(xMillimetres, zMillimetres);
        // The fixed Barn deck exists even before its programme receives a campaign allocation.
        if (_session.BarnStageOpen && (BarnLayout.Deck(cell) || BarnLayout.Stairs(cell)))
            return BandHeight(BarnLayout.Stage,new Vector3(xMillimetres/1000f,0,zMillimetres/1000f),true)-.04f;
        foreach (var stage in _session.Stages)
            if (stage.Deck(cell) || stage.Stairs(cell))
                // The band's own climb (Main.LivePerformance.cs), less the few centimetres every body stands off the grass.
                return BandHeight(stage, new Vector3(xMillimetres / 1000f, 0, zMillimetres / 1000f), true) - 0.04f;
        return 0;
    }

    private void ProcessStageInvasion()
    {
        // The player's body climbs too, for the manager's view (in first person it's on a layer the eyes skip).
        if (_session.PlayerStewardId is { } id && _attendeeVisuals.TryGetValue(new EntityId(id), out var body) && IsInstanceValid(body))
        {
            var lift = PlayerStageLift((int)(body.Position.X * 1000), (int)(body.Position.Z * 1000));
            if (lift > 0) body.Position = body.Position with { Y = 0.04f + lift };
        }
        var shouts = _session.PreparedStatus == PreparationStatus.Running ? _session.StageShouts() : [];
        var live = shouts.Select(shout => (shout.Band, shout.StartedTick)).ToHashSet();
        foreach (var key in _stageShouts.Keys.Where(key => !live.Contains(key)).ToArray())
        {
            if (IsInstanceValid(_stageShouts[key].Label)) _stageShouts[key].Label.QueueFree();
            _stageShouts.Remove(key);
        }
        foreach (var shout in shouts)
        {
            var key = (shout.Band, shout.StartedTick);
            if (!_stageShouts.TryGetValue(key, out var shown))
            {
                // Bubbles at eye level are drawn on the main layer, so the eye camera sees them; marked so it leaves them there.
                var label = WorldText.Speech(new Label3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Text = shout.Text,
                    Modulate = MoodColour(shout.Angry ? Mood.Angry : shout.Band ? Mood.Grumble : Mood.Neutral) }, 40);
                label.SetMeta(StageShoutMeta, true);
                AddChild(label);
                shown = _stageShouts[key] = (label, shout.SpeakerId);
                GD.Print($"STAGE_SHOUT tick={_session.CurrentTick} band={shout.Band} speaker={shout.SpeakerId} text=\"{shout.Text}\"");
            }
            if (_attendeeVisuals.TryGetValue(new EntityId(shown.Speaker), out var speaker) && IsInstanceValid(speaker))
                shown.Label.Position = speaker.Position + new Vector3(0, ChatterHeight, 0);
            shown.Label.Visible = speaker?.Visible == true;
        }
        // Control's word on the radio as they get up there, and as a set starts under them.
        var shift = _session.CaptureStewardShift();
        var now = (shift?.OnStage?.StageId, shift?.OnStage?.InvadedTick is not null);
        if (StewardShiftActive && now != _stageSeen && now.StageId is not null && (now.Item2 || _stageSeen.Stage is null))
            PushShiftRadio(now.Item2 ? "Control: get off the stage! You're not on the bill." : "Control: that's the stage. Down before the band's on, please.");
        _stageSeen = now;
    }

    private void ClearStageShouts()
    {
        foreach (var (label, _) in _stageShouts.Values) if (IsInstanceValid(label)) label.QueueFree();
        _stageShouts.Clear();
    }

    private IEnumerable<Label3D> StageShoutLabels() => _stageShouts.Values.Select(item => item.Label);
}
