using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

/// <summary>
/// The band's own bits on the trailer stage. Tier 1 local acts barely dress the stage: one signature item per
/// genre, a rug, a lamp, a curtain or a hand-painted banner with their name on. The set belongs to whoever is on
/// stage, or next up between sets, and changes over instantly. Presentation only.
/// </summary>
public partial class Main
{
    private Node3D? _stageSet;
    private string? _stageSetActId;
    private double _stageSetSync;

    private static string? StageSetAsset(int genre) => genre switch
    {
        FestivalGenre.Folk => "folk", FestivalGenre.Indie => "indie", FestivalGenre.Pop => "pop",
        FestivalGenre.Electronic => "electronic", FestivalGenre.Punk => "punk", FestivalGenre.Metal => "metal",
        _ => null,
    };

    private void ProcessStageSet(double delta)
    {
        _stageSetSync -= delta;
        if (_stageSetSync > 0 || _session is null) return;
        _stageSetSync = .5;
        if (!_visualRegistry.TryGetValue("farm.trailer-stage", out var stage)) return;
        // A day stopped by a death stays as found: the band that was playing keeps its kit up.
        if (_session.PreparedStatus == PreparationStatus.Failed && _stageSet is not null && IsInstanceValid(_stageSet)) return;
        var live = _session.CaptureLivePerformance();
        var onStage = live?.Stage is LiveSetStage.Live or LiveSetStage.Interrupted;
        var act = onStage ? _session.CurrentFestivalAct : _session.UpcomingFestivalAct ?? _session.CurrentFestivalAct;
        // Once the day is over the last band's things stay where they were left.
        if (act is null && _session.PreparedStatus is PreparationStatus.Departing or PreparationStatus.Finished) return;
        // The drums stay the outgoing band's until their drummer has put the sticks down.
        var drummerStillPlaying = live?.Performers.Any(p => p.InstrumentAttached) == true;
        SetStageDrumHardware(drummerStillPlaying ? _session.CurrentFestivalAct ?? act : act);
        if (act?.Id == _stageSetActId && (act is null || _stageSet is not null && IsInstanceValid(_stageSet))) return;
        _stageSetActId = act?.Id;
        _stageSet?.QueueFree();
        _stageSet = null;
        if (act is null || StageSetAsset(act.Genre) is not { } genre) return;
        _stageSet = InstantiateAsset($"res://assets/environment/lwf_stage_set_{genre}_v1.glb");
        stage.AddChild(_stageSet);
        // A punk band paints its own name on the bedsheet.
        if (_stageSet.FindChild("LetteringArea", true, false) is Node3D area)
        {
            var font = GD.Load<FontFile>("res://assets/fonts/sign/CaveatBrush-Regular.ttf");
            var (text, pixel) = Fit(font, act.Name.ToUpperInvariant(), new Vector2(4.6f, .8f), 96);
            area.AddChild(new Label3D
            {
                Text = text, Font = font, FontSize = 96, PixelSize = pixel, Modulate = new Color("1b1b1b"),
                OutlineSize = 0, Shaded = true, DoubleSided = false, RenderPriority = 1,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Position = new Vector3(0, 0, .01f), RotationDegrees = new Vector3(0, 0, 1.5f),
            });
        }
    }
}
