using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace Festival.Game;

public partial class Main : Node
{
    private const float MinZoom = 38f;
    private const float MaxZoom = 82f;
    private const float PanLimit = 24f;
    private readonly Dictionary<ulong, FarmObjectReadModel> _pickRegistry = [];
    private readonly Dictionary<string, Node3D> _visualRegistry = new(StringComparer.Ordinal);
    private Camera3D _camera = null!;
    private Vector3 _focus = Vector3.Zero;
    private int _orientation;
    private FarmObjectReadModel? _selected;
    private MeshInstance3D _highlight = null!;
    private Label _orientationLabel = null!;
    private Label _inspectorTitle = null!;
    private Label _inspectorBody = null!;
    private Label _hashLabel = null!;
    private GameSession _session = null!;
    private string _pausedHash = "";
    private bool _middleDragging;
    private int _captureFrame;
    private string? _captureDirectory;
    private static readonly string[] OrientationNames = ["South", "West", "North", "East"];

    public override void _Ready()
    {
        ConfigureCaptureMode();
        _session = new GameSession(606, new CampaignId(606));
        _ = _session.Execute(new CommandEnvelope(new CommandId(1), _session.CampaignId, _session.Phase,
            _session.CurrentTick, _session.NextSubmissionSequence, null, new SetPausedCommand(true)));
        _pausedHash = _session.CaptureSnapshot().AuthoritativeHash;
        BuildWorld();
        BuildHud();
        ApplyCamera();
        if (_captureDirectory is not null)
            SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.farmhouse"));
        var version = Engine.GetVersionInfo()["string"].AsString();
        GD.Print($"FESTIVAL_TYCOON_LAUNCHED build={ToolchainSmoke.BuildVersion} godot={version}");
        GD.Print($"FARM_SCENE_READY scenario={LowerWitteringFarmScenario.ScenarioId} objects={_visualRegistry.Count} hash={_pausedHash}");
    }

    public override void _Process(double delta)
    {
        var input = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) input.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) input.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) input.X += 1;
        if (input.LengthSquared() > 0) Pan(input.Normalized() * (float)delta * 18f);
        UpdateHashStatus();
        if (_captureDirectory is not null) ProcessCapture();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Q) Rotate(-1);
            else if (key.Keycode == Key.E) Rotate(1);
            else if (key.Keycode == Key.Space) ReportPause();
        }
        else if (inputEvent is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed) Zoom(-4);
            else if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed) Zoom(4);
            else if (mouse.ButtonIndex == MouseButton.Middle) _middleDragging = mouse.Pressed;
            else if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed) Pick(mouse.Position);
        }
        else if (inputEvent is InputEventMouseMotion motion && _middleDragging)
            Pan(motion.Relative * 0.055f);
    }

    private void BuildWorld()
    {
        var worldEnvironment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("8fc4dc"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("d9e7c2"),
                AmbientLightEnergy = 0.72f,
            },
        };
        AddChild(worldEnvironment);
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-54, -32, 0), LightColor = new Color("fff1c5"),
            LightEnergy = 1.25f, ShadowEnabled = true,
        });
        BuildGrass();
        BuildTrack();
        BuildHedgeBoundary();
        foreach (var item in LowerWitteringFarmScenario.CreateReadModel().Objects) AddFarmObject(item);
        _highlight = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.08f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.73f, 0.08f, 0.72f), EmissionEnabled = true,
                Emission = new Color("ffb923"), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            }, Visible = false,
        };
        AddChild(_highlight);
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 62, Current = true };
        AddChild(_camera);
    }

    private void BuildGrass()
    {
        for (var x = -4; x < 4; x++)
        for (var z = -4; z < 4; z++)
            AddAsset("res://assets/environment/lwf_field_grass_tile_8m_v1.glb", new Vector3(x * 8, 0, z * 8));
    }

    private void BuildTrack()
    {
        for (var z = -3; z < 4; z++)
        {
            var track = AddAsset("res://assets/environment/lwf_vehicle_track_straight_8x4m_v1.glb", new Vector3(0, 0.052f, z * 8));
            track.RotationDegrees = new Vector3(0, 90, 0);
        }
    }

    private void BuildHedgeBoundary()
    {
        const string hedge = "res://assets/environment/lwf_hedge_straight_8m_a_v1.glb";
        for (var i = -4; i < 4; i++)
        {
            AddAsset(hedge, new Vector3(i * 8, 0, -32));
            if (i is not -1 and not 0) AddAsset(hedge, new Vector3(i * 8, 0, 32));
            var left = AddAsset(hedge, new Vector3(-32, 0, i * 8)); left.RotationDegrees = new Vector3(0, 90, 0);
            var right = AddAsset(hedge, new Vector3(32, 0, i * 8)); right.RotationDegrees = new Vector3(0, 90, 0);
        }
        AddAsset("res://assets/environment/lwf_hedge_straight_4m_a_v1.glb", new Vector3(-8, 0, 32));
        AddAsset("res://assets/environment/lwf_hedge_straight_4m_a_v1.glb", new Vector3(4, 0, 32));
        AddAsset("res://assets/environment/lwf_hedge_gate_end_v1.glb", new Vector3(-3, 0, 32));
        var northRight = AddAsset("res://assets/environment/lwf_hedge_gate_end_v1.glb", new Vector3(3, 0, 32));
        northRight.RotationDegrees = new Vector3(0, 180, 0);
    }

    private void AddFarmObject(FarmObjectReadModel item)
    {
        var (path, size) = item.Kind switch
        {
            FarmObjectKind.Farmhouse => ("res://assets/environment/lwf_farmhouse_v1.glb", new Vector3(12.64f, 9.93f, 10.29f)),
            FarmObjectKind.SmallBarn => ("res://assets/environment/lwf_barn_v2.glb", new Vector3(14.65f, 7.1f, 10.06f)),
            FarmObjectKind.LargeBarn => ("res://assets/environment/lwf_large_barn_v1.glb", new Vector3(20.93f, 11.14f, 13.42f)),
            FarmObjectKind.TrailerStage => ("res://assets/environment/lwf_trailer_stage_v2.glb", new Vector3(9.91f, 2.2f, 4.92f)),
            FarmObjectKind.ServicePoint => ("res://assets/environment/lwf_service_point_v2.glb", new Vector3(4.13f, 3.09f, 3.8f)),
            FarmObjectKind.Gate => ("res://assets/environment/lwf_farm_gate_posts_v1.glb", new Vector3(4f, 1.7f, 1.2f)),
            _ => throw new ArgumentOutOfRangeException(),
        };
        var body = new StaticBody3D { Position = new Vector3((float)item.XMetres, 0, (float)item.ZMetres) };
        body.RotationDegrees = new Vector3(0, item.YawQuarterTurns * 90, 0);
        AddChild(body);
        body.AddChild(InstantiateAsset(path));
        if (item.Kind == FarmObjectKind.Gate)
        {
            var leaf = InstantiateAsset("res://assets/environment/lwf_farm_gate_leaf_v1.glb");
            leaf.Position = new Vector3(-1.65f, 0, 0); leaf.RotationDegrees = new Vector3(0, 72, 0); body.AddChild(leaf);
        }
        body.AddChild(new CollisionShape3D
        {
            Position = new Vector3(0, size.Y / 2, 0), Shape = new BoxShape3D { Size = size },
        });
        _pickRegistry.Add(body.GetInstanceId(), item);
        _visualRegistry.Add(item.StableId, body);
    }

    private Node3D AddAsset(string path, Vector3 position)
    {
        var instance = InstantiateAsset(path); instance.Position = position; AddChild(instance); return instance;
    }

    private static Node3D InstantiateAsset(string path)
    {
        var packed = GD.Load<PackedScene>(path) ?? throw new InvalidOperationException($"Missing farm asset: {path}");
        return packed.Instantiate<Node3D>();
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        var ink = new Color("29352c");
        var top = new PanelContainer(); top.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        top.OffsetLeft = 16; top.OffsetTop = 16; top.OffsetRight = -16; top.OffsetBottom = 76;
        top.AddThemeStyleboxOverride("panel", PaperStyle(new Color("f5e9c9"))); layer.AddChild(top);
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; bar.AddThemeConstantOverride("separation", 18); top.AddChild(bar);
        bar.AddChild(LabelText("LOWER WITTERING FARM", 19, ink)); bar.AddChild(LabelText("DAY 1  •  12:00", 17, ink));
        bar.AddChild(ButtonText("PAUSED", ReportPause));
        _orientationLabel = LabelText("VIEW: SOUTH", 17, ink); bar.AddChild(_orientationLabel);
        bar.AddChild(ButtonText("↶ Q", () => Rotate(-1))); bar.AddChild(ButtonText("E ↷", () => Rotate(1)));
        bar.AddChild(ButtonText("−", () => Zoom(4))); bar.AddChild(ButtonText("+", () => Zoom(-4)));

        var inspector = new PanelContainer(); inspector.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        inspector.OffsetLeft = -324; inspector.OffsetTop = 92; inspector.OffsetRight = -16; inspector.OffsetBottom = -16;
        inspector.AddThemeStyleboxOverride("panel", PaperStyle(new Color("f5e9c9"))); layer.AddChild(inspector);
        var margin = new MarginContainer(); margin.AddThemeConstantOverride("margin_left", 20); margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 18); margin.AddThemeConstantOverride("margin_bottom", 18); inspector.AddChild(margin);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 12); margin.AddChild(box);
        box.AddChild(LabelText("SELECTION INSPECTOR", 15, new Color("6f5937")));
        _inspectorTitle = LabelText("Nothing selected", 24, ink); _inspectorTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_inspectorTitle);
        _inspectorBody = LabelText("Click a building, gate, stage or service point.\nClick empty ground to clear.", 16, ink);
        _inspectorBody.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_inspectorBody);
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        _hashLabel = LabelText("SIM HASH: CHECKING", 13, new Color("47603b")); _hashLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_hashLabel);

        var help = new PanelContainer(); help.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        help.OffsetLeft = 16; help.OffsetTop = -92; help.OffsetRight = 620; help.OffsetBottom = -16;
        help.AddThemeStyleboxOverride("panel", PaperStyle(new Color(0.96f, 0.91f, 0.78f, 0.94f))); layer.AddChild(help);
        var helpLabel = LabelText("PAN  WASD / ARROWS / MIDDLE-DRAG    ZOOM  WHEEL / + −\nROTATE  Q E / BUTTONS    SELECT  LEFT CLICK    PAUSE  SPACE", 14, ink);
        helpLabel.HorizontalAlignment = HorizontalAlignment.Center; helpLabel.VerticalAlignment = VerticalAlignment.Center; help.AddChild(helpLabel);
    }

    private static StyleBoxFlat PaperStyle(Color color) => new()
    {
        BgColor = color, BorderColor = new Color("5f5a43"), BorderWidthLeft = 2, BorderWidthTop = 2,
        BorderWidthRight = 2, BorderWidthBottom = 2, CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5, ShadowColor = new Color(0, 0, 0, 0.24f), ShadowSize = 5,
    };

    private static Label LabelText(string text, int size, Color color)
    {
        var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color); return label;
    }

    private static Button ButtonText(string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(68, 38) }; button.Pressed += action; return button;
    }

    private void ApplyCamera()
    {
        var yaw = Mathf.DegToRad(45 + _orientation * 90);
        _camera.Position = _focus + new Vector3(Mathf.Sin(yaw) * 72, 58, Mathf.Cos(yaw) * 72);
        _camera.LookAt(_focus, Vector3.Up);
        if (_orientationLabel is not null) _orientationLabel.Text = $"VIEW: {OrientationNames[_orientation].ToUpperInvariant()}";
    }

    private void Rotate(int step) { _orientation = (_orientation + step + 4) % 4; ApplyCamera(); }
    private void Zoom(float amount) { _camera.Size = Mathf.Clamp(_camera.Size + amount, MinZoom, MaxZoom); }

    private void Pan(Vector2 amount)
    {
        var angle = Mathf.DegToRad(_orientation * 90);
        _focus += new Vector3(Mathf.Cos(angle), 0, -Mathf.Sin(angle)) * amount.X + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * amount.Y;
        _focus.X = Mathf.Clamp(_focus.X, -PanLimit, PanLimit); _focus.Z = Mathf.Clamp(_focus.Z, -PanLimit, PanLimit); ApplyCamera();
    }

    private void Pick(Vector2 screenPosition)
    {
        var origin = _camera.ProjectRayOrigin(screenPosition);
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + _camera.ProjectRayNormal(screenPosition) * 250); query.CollisionMask = 1;
        var result = _camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!result.ContainsKey("collider")) { ClearSelection(); return; }
        var collider = result["collider"].AsGodotObject() as CollisionObject3D;
        if (collider is not null && _pickRegistry.TryGetValue(collider.GetInstanceId(), out var item)) SelectObject(item); else ClearSelection();
    }

    private void SelectObject(FarmObjectReadModel item)
    {
        _selected = item;
        var radius = item.Kind switch
        {
            FarmObjectKind.LargeBarn => 11.5f, FarmObjectKind.SmallBarn => 8.3f,
            FarmObjectKind.Farmhouse => 7.3f, FarmObjectKind.TrailerStage => 6f,
            FarmObjectKind.ServicePoint => 3f, _ => 3.5f,
        };
        _highlight.Position = _visualRegistry[item.StableId].Position + new Vector3(0, 0.08f, 0);
        _highlight.Scale = new Vector3(radius, 1, radius); _highlight.Visible = true;
        _inspectorTitle.Text = item.DisplayName;
        var permanence = item.IsPermanent ? "Permanent • Immovable" : "Inherited • Fixed for this blockout";
        _inspectorBody.Text = $"ID  {item.StableId}\nTYPE  {DisplayKind(item.Kind)}\nSTATE  {item.State}\nSITE  {item.XMetres:0.#} m, {item.ZMetres:0.#} m\n{permanence}";
        GD.Print($"FARM_SELECTED id={item.StableId} orientation={OrientationNames[_orientation]}");
    }

    private void ClearSelection()
    {
        _selected = null; _highlight.Visible = false; _inspectorTitle.Text = "Nothing selected";
        _inspectorBody.Text = "Click a building, gate, stage or service point.\nClick empty ground to clear."; GD.Print("FARM_SELECTION_CLEARED");
    }

    private static string DisplayKind(FarmObjectKind kind) => kind switch
    {
        FarmObjectKind.SmallBarn => "Small barn", FarmObjectKind.LargeBarn => "Large barn",
        FarmObjectKind.TrailerStage => "Trailer stage", FarmObjectKind.ServicePoint => "Generic service point", _ => kind.ToString(),
    };

    private static void ReportPause() => GD.Print("FARM_PAUSE_CONTROL state=paused m0.06-no-live-clock");

    private void UpdateHashStatus()
    {
        var current = _session.CaptureSnapshot().AuthoritativeHash; var unchanged = current == _pausedHash;
        _hashLabel.Text = $"SIMULATION PAUSED\nHASH {(unchanged ? "UNCHANGED" : "CHANGED")}  {current[..12]}";
        _hashLabel.AddThemeColorOverride("font_color", unchanged ? new Color("47603b") : new Color("9c2f25"));
    }

    private void ConfigureCaptureMode()
    {
        var args = OS.GetCmdlineUserArgs();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--capture-farm" && i + 1 < args.Length) _captureDirectory = args[++i];
            else if (args[i] == "--capture-size" && i + 1 < args.Length)
            {
                var size = args[++i].Split('x');
                if (size.Length == 2 && int.TryParse(size[0], out var width) && int.TryParse(size[1], out var height)) GetWindow().Size = new Vector2I(width, height);
            }
        }
        if (_captureDirectory is not null) DirAccess.MakeDirRecursiveAbsolute(_captureDirectory);
    }

    private void ProcessCapture()
    {
        _captureFrame++;
        if (_captureFrame < 12 || (_captureFrame - 12) % 8 != 0) return;
        var index = (_captureFrame - 12) / 8;
        if (index >= 4)
        {
            var verification = VerifyRuntimeInteractions();
            File.WriteAllText(Path.Combine(_captureDirectory!, $"verification-{GetWindow().Size.X}x{GetWindow().Size.Y}.txt"), verification.Report);
            GD.Print($"FARM_CAPTURE_COMPLETE selected={_selected?.StableId} verified={verification.Passed} size={GetWindow().Size}");
            _captureDirectory = null; GetTree().Quit(verification.Passed ? 0 : 2); return;
        }
        var path = Path.Combine(_captureDirectory!, $"farm-{OrientationNames[_orientation].ToLowerInvariant()}-{GetWindow().Size.X}x{GetWindow().Size.Y}.png");
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"FARM_CAPTURE orientation={OrientationNames[_orientation]} selected={_selected?.StableId} path={path} result={error}");
        if (index < 3) { _orientation = index + 1; ApplyCamera(); }
    }

    private (bool Passed, string Report) VerifyRuntimeInteractions()
    {
        var lines = new List<string>();
        var passed = true;
        foreach (var id in new[] { "farm.farmhouse", "farm.main-gate", "farm.service-point" })
        {
            var visual = _visualRegistry[id];
            Pick(_camera.UnprojectPosition(visual.GlobalPosition + new Vector3(0, 1, 0)));
            var resolved = _selected?.StableId;
            var correct = resolved == id;
            passed &= correct;
            lines.Add($"pick expected={id} resolved={resolved ?? "none"} correct={correct}");
        }

        Pick(new Vector2(2, GetWindow().Size.Y - 2));
        var cleared = _selected is null;
        passed &= cleared;
        lines.Add($"empty_selection_cleared={cleared}");

        SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.farmhouse"));
        var retained = true;
        for (var step = 0; step < 4; step++)
        {
            Rotate(1);
            retained &= _selected?.StableId == "farm.farmhouse";
        }
        passed &= retained;
        lines.Add($"selection_retained_four_rotations={retained} id={_selected?.StableId}");

        Zoom(-1000); var minBounded = Mathf.IsEqualApprox(_camera.Size, MinZoom);
        Zoom(1000); var maxBounded = Mathf.IsEqualApprox(_camera.Size, MaxZoom);
        Pan(new Vector2(10000, -10000));
        var panBounded = Math.Abs(_focus.X) <= PanLimit && Math.Abs(_focus.Z) <= PanLimit;
        passed &= minBounded && maxBounded && panBounded;
        lines.Add($"zoom_min_bounded={minBounded} zoom_max_bounded={maxBounded} pan_bounded={panBounded}");

        var currentHash = _session.CaptureSnapshot().AuthoritativeHash;
        var hashUnchanged = currentHash == _pausedHash && _session.IsPaused;
        passed &= hashUnchanged;
        lines.Add($"paused={_session.IsPaused} hash_before={_pausedHash} hash_after={currentHash} unchanged={hashUnchanged}");
        lines.Insert(0, $"M0.06 exported-runtime verification passed={passed} resolution={GetWindow().Size}");
        return (passed, string.Join(System.Environment.NewLine, lines) + System.Environment.NewLine);
    }
}
