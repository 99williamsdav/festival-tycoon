using Godot;
using Festival.Simulation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Opt-in comparison fixture; synthetic ground maps never enter the simulation or a save.
public partial class Main
{
    private string? _grassEvidenceOutput;
    private int _grassEvidenceFrame;
    private Shader _grassCandidate = null!, _grassBefore = null!;
    private Variant _grassNoise;
    private Control? _grassReceipt;
    private string _grassEvidenceHash = "";
    private readonly List<double> _grassTimes = [], _grassDraws = [];
    private readonly List<object> _grassMeasurements = [];
    private long _grassTimestamp;

    private void GrassEvidenceShader(bool before)
    {
        _groundMaterial.Shader = before ? _grassBefore : _grassCandidate;
        if (_pastureGrassMesh is not null) _pastureGrassMesh.MaterialOverride = before ? _pastureGrassOriginal : _pastureGrassMaterial;
        _groundMaterial.SetShaderParameter("noise", _grassNoise);
        _groundMaterial.SetShaderParameter("wear_map", _groundTexture);
        _groundMaterial.SetShaderParameter("wet_map", _wetTexture);
        _groundMaterial.SetShaderParameter("palette", GD.Load<Texture2D>("res://assets/environment/lwf_field_grass_tile_8m_v1_field_track_palette.png"));
    }

    private void ProcessGrassEvidence()
    {
        if (_grassEvidenceOutput is null) return;
        try { CaptureGrassEvidence(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(2); _grassEvidenceOutput = null; }
    }

    private void CaptureGrassEvidence()
    {
        if (_grassEvidenceFrame == 0)
        {
            Directory.CreateDirectory(_grassEvidenceOutput!);
            var perk = _session.CapturePerks()!;
            foreach (var command in new SessionCommand[] { new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]), new UseDefaultBuildLayoutCommand() })
            {
                var result = _session.Execute(CampaignEnvelope(command));
                if (!result.IsAccepted) throw new InvalidOperationException(result.Message);
            }
            SyncBuildWorld(); _hudWorkspaceOpen = _buildDrawerOpen = false;
            _boxOfficeSeenAttempt = _session.CapturePreparation()!.Attempt; RefreshPreparationHud();
            RedrawGroundWear(); RedrawGroundWater(); _groundShown = (_session, _session.GroundVersion);
            _grassEvidenceHash = _session.CaptureSnapshot().AuthoritativeHash.ToString();
            _grassCandidate = _groundMaterial.Shader; _grassNoise = _groundMaterial.GetShaderParameter("noise");
            var args = OS.GetCmdlineUserArgs(); var at = Array.IndexOf(args, "--grass-before-shader");
            if (at < 0 || at + 1 >= args.Length) throw new InvalidOperationException("Comparison requires --grass-before-shader path");
            _grassBefore = new Shader { Code = File.ReadAllText(args[at + 1]) };
            _grassReceipt = typeof(PreparationDock).GetField("_receipt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(Dock) as Control;
            Engine.MaxFps = 0; DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        }
        Dock.Readiness?.Hide(); _grassReceipt?.CallDeferred(CanvasItem.MethodName.Hide);
        var frame = _grassEvidenceFrame++;
        // Same light, terrain, camera and scene for each before/after pair.
        if (frame < 192)
        {
            var index = frame / 12; var within = frame % 12; var view = index % 4;
            if (within == 0)
            {
                if (view == 0 && index > 0) _rig.Rotate(1);
                _rig.Frame(view < 2 ? Vector3.Zero : new Vector3(12, 0, 16), view < 2 ? 66 : 18);
                GrassEvidenceShader(view % 2 == 0);
            }
            if (within == 10) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_grassEvidenceOutput!,
                $"{index / 4}-{_rig.OrientationName.ToLowerInvariant()}-{(view < 2 ? "farm" : "close")}-{(view % 2 == 0 ? "before" : "after")}.png"));
            return;
        }
        if (frame == 192)
        {
            GrassEvidenceShader(false);
            var wear = new byte[GroundMapCells * GroundMapCells]; var wet = new byte[wear.Length * 2];
            var dry = new[] { 0f, .25f, .55f, 1f, 0f, 0f, 0f, 0f };
            var water = new[] { 0f, 0f, 0f, 0f, .2f, 1f, .2f, 1f };
            var mud = new[] { 0f, 0f, 0f, 0f, 0f, 0f, .5f, 1f };
            for (var z = 0; z < GroundMapCells; z++) for (var x = 0; x < GroundMapCells; x++)
            {
                var band = Math.Min(7, x * 8 / GroundMapCells); var pixel = z * GroundMapCells + x;
                wear[pixel] = (byte)(dry[band] * 255); wet[pixel * 2] = (byte)(water[band] * 255); wet[pixel * 2 + 1] = (byte)(mud[band] * 255);
            }
            _groundImage.SetData(GroundMapCells, GroundMapCells, false, Image.Format.L8, wear); _groundTexture.Update(_groundImage);
            _wetImage.SetData(GroundMapCells, GroundMapCells, false, Image.Format.Rg8, wet); _wetTexture.Update(_wetImage);
            _rig.Frame(new(0, 0, 20), 42);
        }
        if (frame == 202) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_grassEvidenceOutput!, "synthetic-wear-wet-mud-bands.png"));
        if (frame < 204) return;
        var pf = frame - 204; var phase = pf / 120; var sample = pf % 120;
        if (phase < 2)
        {
            if (sample == 0)
            {
                RedrawGroundWear(); RedrawGroundWater(); _rig.Frame(Vector3.Zero, 66); GrassEvidenceShader(phase == 0);
                _grassTimes.Clear(); _grassDraws.Clear();
            }
            var now = Stopwatch.GetTimestamp();
            if (sample >= 30) { _grassTimes.Add(Stopwatch.GetElapsedTime(_grassTimestamp, now).TotalMilliseconds); _grassDraws.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)); }
            _grassTimestamp = now;
            if (sample == 119)
            {
                var sorted = _grassTimes.Order().ToArray();
                _grassMeasurements.Add(new { before = phase == 0, frames = 90, medianMs = sorted[45], p95Ms = sorted[85], draws = _grassDraws.Average() });
            }
            return;
        }
        var unchanged = _grassEvidenceHash == _session.CaptureSnapshot().AuthoritativeHash.ToString();
        if (!unchanged) throw new InvalidOperationException("Grass presentation changed simulation");
        File.WriteAllText(Path.Combine(_grassEvidenceOutput!, "grass-evidence.json"), JsonSerializer.Serialize(new { passed = true, simulationUnchanged = unchanged,
            measurements = _grassMeasurements, bandOrder = new[] { "healthy", "flattened", "browned", "earth", "soaked", "puddle", "mud", "sludge" },
            caveat = "Scripted preparation comparison, synthetic display maps only. Whole-app uncapped wall-frame timings, not isolated GPU cost or live-festival FPS certification. No manual playthrough." }, new JsonSerializerOptions { WriteIndented = true }));
        GetTree().Quit();
    }
}
