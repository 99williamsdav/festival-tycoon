using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Festival.Game;

public partial class Main
{
#if DEBUG
    private sealed record BuildOriginMap(string Key, Dictionary<GridCell, string> Invalid);
    private bool _buildOriginOverlayEnabled;
    private Button? _buildOriginToggle;
    private PanelContainer? _buildOriginPanel;
    private Label? _buildOriginLabel;
    private MultiMeshInstance3D? _buildOriginVisual;
    private readonly Dictionary<string, BuildOriginMap> _buildOriginCache = [];
    private CancellationTokenSource? _buildOriginCancellation;
    private Task<BuildOriginMap>? _buildOriginTask;
    private string? _buildOriginRequestedKey;

    private void BuildDebugControls(VBoxContainer diagnostics, CanvasLayer layer, Vector2 size)
    {
        _buildOriginToggle = ButtonText("Invalid origins overlay · Off", () =>
        {
            _buildOriginOverlayEnabled = !_buildOriginOverlayEnabled;
            _buildOriginToggle!.Text = $"Invalid origins overlay · {(_buildOriginOverlayEnabled ? "On" : "Off")}";
            RequestBuildOriginOverlay();
        });
        _buildOriginToggle.TooltipText = "Debug only: mark invalid placement origins for the selected service/orientation using the authoritative validator.";
        diagnostics.AddChild(_buildOriginToggle);
        _buildOriginPanel = HudPanel(layer, new Vector2(15, size.Y - 166), new Vector2(Math.Min(650, size.X - 30), 46));
        _buildOriginPanel.Visible = false;
        _buildOriginPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _buildOriginLabel = HudLabel("", 12); _buildOriginPanel.AddChild(_buildOriginLabel);
        _buildOriginLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    private void ClearBuildOriginOverlay()
    {
        _buildOriginCancellation?.Cancel();
        _buildOriginTask = null; _buildOriginRequestedKey = null;
        if (_buildOriginVisual is not null) { _buildOriginVisual.QueueFree(); _buildOriginVisual = null; }
        if (_buildOriginPanel is not null) _buildOriginPanel.Visible = false;
    }

    private void RequestBuildOriginOverlay()
    {
        ClearBuildOriginOverlay();
        if (!_buildOriginOverlayEnabled || _buildGhostKind is not { } kind || _buildOriginPanel is null) return;
        _buildOriginPanel.Visible = true;
        var key = $"{_session.CaptureSnapshot().AuthoritativeHash}:{kind}:{_buildQuarterTurns}:{_buildMovingId}";
        _buildOriginRequestedKey = key;
        if (_buildOriginCache.TryGetValue(key, out var cached)) { DrawBuildOriginOverlay(cached); return; }
        _buildOriginLabel!.Text = $"DEBUG · mapping invalid {BuildName(kind)} origins ({_buildQuarterTurns * 90}°)…";
        var snapshot = _session.CapturePersistenceSnapshot();
        var movingId = _buildMovingId; var turns = _buildQuarterTurns;
        _buildOriginCancellation = new CancellationTokenSource();
        var token = _buildOriginCancellation.Token;
        _buildOriginTask = Task.Run(() => ScanBuildOrigins(snapshot, key, kind, movingId, turns, token), token);
    }

    private static BuildOriginMap ScanBuildOrigins(SessionPersistenceSnapshot snapshot, string key,
        BuildServiceKind kind, string? movingId, int turns, CancellationToken token)
    {
        // Each worker owns its restored session. Validation can rebuild traversal state, so
        // sharing one session across workers would not be safe even though no commands commit.
        var invalid = new ConcurrentDictionary<GridCell, string>();
        Parallel.For(68, 191,
            new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Min(4, System.Environment.ProcessorCount) },
            () =>
            {
                var restored = GameSession.Restore(snapshot);
                if (!restored.IsSuccess) throw new InvalidOperationException($"Debug origin scan could not restore: {restored.Error}");
                return restored.Session!;
            },
            (x, _, session) =>
            {
                for (var z = 108; z <= 190; z++)
                {
                    token.ThrowIfCancellationRequested();
                    var cell = new GridCell(x, z);
                    SessionCommand command = movingId is null
                        ? new PlaceBuildServiceCommand(kind, cell, turns)
                        : new MoveBuildServiceCommand(movingId, cell, turns);
                    var envelope = new CommandEnvelope(new CommandId(session.NextSubmissionSequence + 1),
                        session.CampaignId, session.Phase, session.CurrentTick, session.NextSubmissionSequence, null, command);
                    if (session.ValidateCommand(envelope) is { } issue) invalid[cell] = issue.Message;
                }
                return session;
            },
            _ => { });
        return new(key, new Dictionary<GridCell, string>(invalid));
    }

    private void ProcessBuildDebugOverlay()
    {
        if (_buildOriginTask is not { IsCompleted: true } task) return;
        _buildOriginTask = null;
        if (task.IsCanceled || _buildOriginRequestedKey is null) return;
        if (task.IsFaulted)
        {
            _buildOriginLabel!.Text = "DEBUG · origin scan failed: " + task.Exception?.GetBaseException().Message;
            return;
        }
        var map = task.Result;
        if (map.Key != _buildOriginRequestedKey || !_buildOriginOverlayEnabled || _buildGhostKind is null) return;
        _buildOriginCache[map.Key] = map;
        DrawBuildOriginOverlay(map);
    }

    private void DrawBuildOriginOverlay(BuildOriginMap map)
    {
        if (_buildOriginVisual is not null) { _buildOriginVisual.QueueFree(); _buildOriginVisual = null; }
        var plane = new PlaneMesh { Size = new Vector2(.48f, .48f), Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(.96f, .02f, .025f, .60f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled
        } };
        var mesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = plane,
            InstanceCount = map.Invalid.Count };
        var index = 0;
        foreach (var cell in map.Invalid.Keys)
            mesh.SetInstanceTransform(index++, new Transform3D(Basis.Identity, ImmersionPosition(cell) + new Vector3(0, .14f, 0)));
        _buildOriginVisual = new MultiMeshInstance3D { Multimesh = mesh };
        AddChild(_buildOriginVisual);
        RefreshBuildDebugHover();
    }

    private void RefreshBuildDebugHover()
    {
        if (_buildOriginPanel?.Visible != true || _buildOriginLabel is null) return;
        if (_buildCandidate is not { } cell)
        { _buildOriginLabel.Text = "DEBUG · hover the field for an exact origin and reason."; return; }
        _buildOriginLabel.Text = $"DEBUG · origin ({cell.X},{cell.Z}) · {(_buildCandidateIssue ?? "allowed by Build validator")}";
    }

    private void EnableBuildOriginCapture()
    {
        _buildOriginOverlayEnabled = true;
        if (_buildOriginToggle is not null) _buildOriginToggle.Text = "Invalid origins overlay · On";
        RequestBuildOriginOverlay();
    }

    private bool BuildOriginOverlayReady => _buildOriginVisual is not null;
#else
    private void BuildDebugControls(VBoxContainer diagnostics, CanvasLayer layer, Vector2 size) { }
    private void ClearBuildOriginOverlay() { }
    private void RequestBuildOriginOverlay() { }
    private void ProcessBuildDebugOverlay() { }
    private void RefreshBuildDebugHover() { }
    private void EnableBuildOriginCapture() { }
    private bool BuildOriginOverlayReady => true;
#endif
}
