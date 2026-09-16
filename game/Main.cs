using Festival.Simulation;
using Festival.Simulation.Fixtures;
using Festival.Persistence;
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
    private readonly Dictionary<ulong, EntityId> _attendeePickRegistry = [];
    private readonly Dictionary<string, Node3D> _visualRegistry = new(StringComparer.Ordinal);
    private readonly List<Node3D> _grassVisuals = [];
    private readonly List<Node3D> _trackVisuals = [];
    private Camera3D _camera = null!;
    private Vector3 _focus = Vector3.Zero;
    private int _orientation;
    private FarmObjectReadModel? _selected;
    private EntityId? _selectedAttendeeId;
    private MeshInstance3D _highlight = null!;
    private Label _orientationLabel = null!;
    private Label _inspectorTitle = null!;
    private Label _inspectorBody = null!;
    private Label _hashLabel = null!;
    private GameSession _session = null!;
    private string _pausedHash = "";
    private bool _middleDragging;
    private Node3D _gateLeafCollider = null!;
    private int _captureFrame;
    private string? _captureDirectory;
    private string? _navigationCaptureDirectory;
    private string? _queueCaptureDirectory;
    private string? _foundationCaptureDirectory;
    private NavigationFixtureState? _navigationReference;
    private ServiceQueueFixtureState? _queueFixture;
    private ServiceQueueFixtureState? _queueReference;
    private Node3D _attendeeVisual = null!;
    private readonly Dictionary<EntityId, Node3D> _attendeeVisuals = [];
    private double _navigationTickDebt;
    private Vector3 _presentationFrom;
    private Vector3 _presentationTo;
    private int _navigationCaptureStage;
    private int _navigationArrivalFrames;
    private int _navigationPresentedFrames;
    private bool _queueClosed;
    private bool _queueReopened;
    private int _queuePurchaseFrames;
    private FiftyAgentFoundationFixtureState? _foundationFixture;
    private CrowdBenchmarkFixture? _benchmarkFixture;
    private string? _benchmarkOutputPath;
    private int _benchmarkFrames;
    private readonly List<double> _benchmarkFrameMilliseconds = [];
    private FiftyAgentFoundationFixtureState? _foundationReference;
    private readonly FoundationClock _foundationClock = new();
    private readonly FoundationPresentationInterpolator _foundationPresentation = new();
    private RealTimeAutosaveScheduler _autosaveScheduler = null!;
    private long _autosaveGeneration;
    private int _autosaveWrites;
    private int _foundationCaptureStage;
    private string _saveStatus = "READY";
    private string _manualSaveHash = "";
    private bool _manualRestoreVerified;
    private int _manualMutationTicks;
    private bool _overloadObserved;
    private double _pressureAttained;
    private double _pressureDebt;
    private int _completionFrames;
    private int _pauseCaptureFrames;
    private int _pausePickFrames;
    private bool _pauseCapturePrepared;
    private string _pauseHash = "";
    private bool _pauseVerified;
    private bool _pauseInputRouteVerified;
    private bool _attendeePickVerified;
    private bool _saveCapturePrepared;
    private int _saveCaptureFrames;
    private double _maximumPressureWorkMilliseconds;
    private bool _selectionRetainedAfterLoad;
    private bool _pressureInputVerified;
    private double _pressureInputLatencyMilliseconds;
    private readonly SaveCompatibility _saveCompatibility = new("0.0.1-m0.09", "d7e7597670c2f9bc2552fa5df29f4afe294270e346643160e92feb1436bb1dd9", "m0-rules-v1");
    private static readonly string[] OrientationNames = ["South", "West", "North", "East"];

    public override void _Ready()
    {
        ConfigureCaptureMode();
        _autosaveScheduler = new RealTimeAutosaveScheduler(_foundationCaptureDirectory is null ?
            RealTimeAutosaveScheduler.ProductionCadenceSeconds : 2);
        _autosaveGeneration = AutosaveRotation.NextGeneration(SaveDirectory, _saveCompatibility);
        if (_benchmarkFixture is not null)
        {
            _session = _benchmarkFixture.Waves[0].Session;
        }
        else if (_foundationCaptureDirectory is not null || (_captureDirectory is null && _navigationCaptureDirectory is null && _queueCaptureDirectory is null))
        {
            _foundationFixture = FiftyAgentFoundationFixture.Create();
            if (_foundationCaptureDirectory is not null) _foundationReference = FiftyAgentFoundationFixture.Create();
            _session = _foundationFixture.Session;
        }
        else if (_queueCaptureDirectory is not null)
        {
            _queueFixture = ServiceQueueFixture.Create();
            _queueReference = ServiceQueueFixture.Create();
            _session = _queueFixture.Session;
        }
        else
        {
            var navigation = NavigationFixture.CreateGateToServiceSession();
            _session = navigation.Session;
            if (_navigationCaptureDirectory is not null)
            {
                _ = NavigationFixture.IssueAutonomousServiceIntent(navigation);
                _navigationReference = NavigationFixture.CreateGateToServiceSession();
                _ = NavigationFixture.IssueAutonomousServiceIntent(_navigationReference);
            }
            else
                _ = _session.Execute(new CommandEnvelope(new CommandId(2), _session.CampaignId, _session.Phase,
                    _session.CurrentTick, _session.NextSubmissionSequence, null, new SetPausedCommand(true)));
        }
        _pausedHash = _session.CaptureSnapshot().AuthoritativeHash;
        BuildWorld();
        BuildAttendee();
        if (_foundationFixture is not null) _foundationPresentation.Reset(_session.CaptureSnapshot());
        BuildHud();
        if (_queueCaptureDirectory is not null || _foundationFixture is not null) { _focus = new Vector3(10, 0, 4); _camera.Size = 58; }
        ApplyCamera();
        if (_captureDirectory is not null)
            SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.farmhouse"));
        var version = Engine.GetVersionInfo()["string"].AsString();
        GD.Print($"FESTIVAL_TYCOON_LAUNCHED build={ToolchainSmoke.BuildVersion} godot={version}");
        GD.Print($"FARM_SCENE_READY scenario={LowerWitteringFarmScenario.ScenarioId} objects={_visualRegistry.Count} hash={_pausedHash}");
    }

    public override void _Process(double delta)
    {
        if (_middleDragging && !Input.IsMouseButtonPressed(MouseButton.Middle)) _middleDragging = false;
        var input = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) input.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) input.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) input.X += 1;
        if (input.LengthSquared() > 0) Pan(input.Normalized() * (float)delta * 18f);
        if (_benchmarkFixture is not null) AdvanceRenderedBenchmark(delta);
        else if (_foundationFixture is not null) AdvanceFoundationPresentation(delta);
        else if (_queueCaptureDirectory is not null) AdvanceQueuePresentation(delta);
        else if (_navigationCaptureDirectory is not null) AdvanceNavigationPresentation(delta);
        else UpdateHashStatus();
        if (_captureDirectory is not null) ProcessCapture();
    }

    public override void _Input(InputEvent inputEvent)
    {
        // Release must be observed before a HUD Control consumes the mouse event.
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: false })
            _middleDragging = false;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_captureDirectory is not null || _navigationCaptureDirectory is not null || _queueCaptureDirectory is not null || _foundationCaptureDirectory is not null) return;
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Q) Rotate(-1);
            else if (key.Keycode == Key.E) Rotate(1);
            else if (key.Keycode == Key.Space)
            {
                if (_foundationFixture is not null) HandleFoundationPauseInput(); else ReportPause();
            }
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

    private void BuildAttendee()
    {
        var agents = _session.CaptureSnapshot().NavigationAgents;
        foreach (var agent in agents)
        {
            var visual = AddAsset("res://assets/characters/lwf_generic_attendee_v1.glb", ToWorld(agent));
            if (_foundationFixture is not null)
            {
                var ordinal = Array.IndexOf(_foundationFixture.AgentIds.ToArray(), agent.Id);
                var palette = AttendeePaletteAssignment.FromOrdinal(ordinal) + 1;
                var material = GD.Load<Material>($"res://assets/characters/colourways/palette-{palette:00}.tres");
                foreach (var child in visual.FindChildren("*", "MeshInstance3D", true, false))
                    if (child is MeshInstance3D mesh) mesh.MaterialOverride = material;
                var pickBody = new StaticBody3D { CollisionLayer = 1, CollisionMask = 1 };
                pickBody.AddChild(new CollisionShape3D
                {
                    Position = new Vector3(0, 0.85f, 0),
                    Shape = new CapsuleShape3D { Radius = 0.38f, Height = 1.7f },
                });
                visual.AddChild(pickBody);
                _attendeePickRegistry.Add(pickBody.GetInstanceId(), agent.Id);
            }
            _attendeeVisuals.Add(agent.Id, visual);
        }
        _attendeeVisual = _attendeeVisuals[agents[0].Id];
        _presentationFrom = _presentationTo = ToWorld(agents[0]);
    }

    private void BuildGrass()
    {
        for (var x = -4; x < 4; x++)
        for (var z = -4; z < 4; z++)
        {
            // Imported bounds are local X 0..8, Z -8..0. The +8 Z origin offset
            // therefore covers world -32..32 instead of leaving the north edge bare.
            _grassVisuals.Add(AddAsset("res://assets/environment/lwf_field_grass_tile_8m_v1.glb", new Vector3(x * 8, 0, (z + 1) * 8)));
        }
    }

    private void BuildTrack()
    {
        for (var z = -3; z <= 4; z++)
        {
            var track = AddAsset("res://assets/environment/lwf_vehicle_track_straight_8x4m_v1.glb", new Vector3(0, 0.052f, z * 8));
            track.RotationDegrees = new Vector3(0, 90, 0);
            _trackVisuals.Add(track);
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
            _gateLeafCollider = new StaticBody3D
            {
                Position = new Vector3(-1.65f, 0, 0),
                RotationDegrees = new Vector3(0, 72, 0),
            };
            body.AddChild(_gateLeafCollider);
            var leaf = InstantiateAsset("res://assets/environment/lwf_farm_gate_leaf_v1.glb");
            _gateLeafCollider.AddChild(leaf);
            _gateLeafCollider.AddChild(new CollisionShape3D
            {
                Position = new Vector3(1.695f, 0.79f, 0.08f),
                Shape = new BoxShape3D { Size = new Vector3(3.5f, 1.1f, 0.36f) },
            });
            _pickRegistry.Add(_gateLeafCollider.GetInstanceId(), item);
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
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bar.AddThemeConstantOverride("separation", _foundationFixture is null ? 18 : 7); top.AddChild(bar);
        bar.AddChild(LabelText(_foundationFixture is null ? "LOWER WITTERING FARM" : "LOWER WITTERING", 19, ink)); bar.AddChild(LabelText("DAY 1  •  12:00", 17, ink));
        if (_foundationFixture is not null)
        {
            bar.AddChild(ButtonText("PAUSE", HandleFoundationPauseInput));
            bar.AddChild(ButtonText("1×", () => SetFoundationSpeed(RequestedSpeed.OneX)));
            bar.AddChild(ButtonText("2×", () => SetFoundationSpeed(RequestedSpeed.TwoX)));
            bar.AddChild(ButtonText("4×", () => SetFoundationSpeed(RequestedSpeed.FourX)));
            bar.AddChild(ButtonText("SAVE", ManualSave));
            bar.AddChild(ButtonText("LOAD", ManualLoad));
        }
        else bar.AddChild(ButtonText(_navigationCaptureDirectory is null && _queueCaptureDirectory is null ? "PAUSED" : "AI DEMO 1X", ReportPause));
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
        // Pan in screen axes, including the camera's 45-degree isometric yaw.
        // amount.X is screen-right and amount.Y is screen-down.
        var world = CameraControlMath.ScreenPanToWorld(_orientation, amount.X, amount.Y);
        _focus += new Vector3((float)world.X, 0, (float)world.Z);
        _focus.X = Mathf.Clamp(_focus.X, -PanLimit, PanLimit); _focus.Z = Mathf.Clamp(_focus.Z, -PanLimit, PanLimit); ApplyCamera();
    }

    private void Pick(Vector2 screenPosition)
    {
        var origin = _camera.ProjectRayOrigin(screenPosition);
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + _camera.ProjectRayNormal(screenPosition) * 250); query.CollisionMask = 1;
        var result = _camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!result.ContainsKey("collider")) { ClearSelection(); return; }
        var collider = result["collider"].AsGodotObject() as CollisionObject3D;
        if (collider is not null && _attendeePickRegistry.TryGetValue(collider.GetInstanceId(), out var attendeeId)) SelectAttendee(attendeeId);
        else if (collider is not null && _pickRegistry.TryGetValue(collider.GetInstanceId(), out var item)) SelectObject(item);
        else ClearSelection();
    }

    private void SelectObject(FarmObjectReadModel item)
    {
        _selectedAttendeeId = null;
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
        _selected = null; _selectedAttendeeId = null; _highlight.Visible = false; _inspectorTitle.Text = "Nothing selected";
        _inspectorBody.Text = "Click a building, gate, stage or service point.\nClick empty ground to clear."; GD.Print("FARM_SELECTION_CLEARED");
    }

    private void SelectAttendee(EntityId id)
    {
        _selected = null; _selectedAttendeeId = id;
        _highlight.Position = _attendeeVisuals[id].Position + new Vector3(0, 0.08f, 0);
        _highlight.Scale = new Vector3(0.7f, 1, 0.7f); _highlight.Visible = true;
        RefreshAttendeeInspector();
        GD.Print($"ATTENDEE_SELECTED id={id.Value} orientation={OrientationNames[_orientation]}");
    }

    private void RefreshAttendeeInspector()
    {
        if (_selectedAttendeeId is not { } id) return;
        var snapshot = _session.CaptureSnapshot();
        var navigation = snapshot.NavigationAgents.Single(item => item.Id == id);
        var queue = snapshot.ServiceQueues.Single();
        var queueAgent = queue.Agents.Single(item => item.AgentId == id);
        var ordinal = Array.IndexOf(_foundationFixture!.AgentIds.ToArray(), id);
        var service = queue.ActiveOwnerId == id ? $"Active • {queue.RemainingServiceTicks} ticks" : "None";
        _highlight.Position = _attendeeVisuals[id].Position + new Vector3(0, 0.08f, 0);
        _inspectorTitle.Text = $"Attendee {id.Value}";
        _inspectorBody.Text = $"PALETTE  {(AttendeePaletteAssignment.FromOrdinal(ordinal) + 1):00}\nACTION  {navigation.Action}\nINTENT  {navigation.IntentId ?? "None"}\nQUEUE  {queueAgent.Action}\nSERVICE  {service}\nPOSITION  {navigation.XMillimetres / 1000.0:0.00} m, {navigation.ZMillimetres / 1000.0:0.00} m\nREAD-ONLY • AUTONOMOUS";
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
            else if (args[i] == "--capture-navigation" && i + 1 < args.Length) _navigationCaptureDirectory = args[++i];
            else if (args[i] == "--capture-queue" && i + 1 < args.Length) _queueCaptureDirectory = args[++i];
            else if (args[i] == "--capture-foundation" && i + 1 < args.Length) _foundationCaptureDirectory = args[++i];
            else if (args[i] == "--benchmark-launch" && i + 3 < args.Length)
            {
                var agents = int.Parse(args[++i]);
                var passage = Enum.Parse<BenchmarkPassage>(args[++i], true);
                _benchmarkOutputPath = ProjectSettings.GlobalizePath(args[++i]);
                _benchmarkFixture = CrowdBenchmarkFixture.Create(agents, passage);
            }
            else if (args[i] == "--capture-size" && i + 1 < args.Length)
            {
                var size = args[++i].Split('x');
                if (size.Length == 2 && int.TryParse(size[0], out var width) && int.TryParse(size[1], out var height)) GetWindow().Size = new Vector2I(width, height);
            }
        }
        if (_captureDirectory is not null) DirAccess.MakeDirRecursiveAbsolute(_captureDirectory);
        if (_navigationCaptureDirectory is not null) DirAccess.MakeDirRecursiveAbsolute(_navigationCaptureDirectory);
        if (_queueCaptureDirectory is not null) DirAccess.MakeDirRecursiveAbsolute(_queueCaptureDirectory);
        if (_foundationCaptureDirectory is not null) DirAccess.MakeDirRecursiveAbsolute(_foundationCaptureDirectory);
        if (_benchmarkOutputPath is not null) DirAccess.MakeDirRecursiveAbsolute(Path.GetDirectoryName(_benchmarkOutputPath)!);
    }

    private void AdvanceRenderedBenchmark(double delta)
    {
        var fixture = _benchmarkFixture!;
        _benchmarkFrameMilliseconds.Add(delta * 1000);
        fixture.AdvanceOneTick();
        _benchmarkFrames++;
        var snapshot = fixture.Waves[0].Session.CaptureSnapshot();
        foreach (var agent in snapshot.NavigationAgents)
            if (_attendeeVisuals.TryGetValue(agent.Id, out var visual)) visual.Position = ToWorld(agent);
        _hashLabel.Text = $"M0.10 RENDERED BENCHMARK • {fixture.TotalAgents} TOTAL\nVISIBLE DESTINATION {snapshot.NavigationAgents.Count} • OFF-CAMERA LOGIC IDENTICAL\nFRAME {_benchmarkFrames} TICK {fixture.ControllerTick} HASH {fixture.CompositeHash()[..12]}";
        if (_benchmarkFrames < 30) return;
        var ordered = _benchmarkFrameMilliseconds.Skip(5).Order().ToArray();
        double P(double p) => ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * p) - 1, 0, ordered.Length - 1)];
        var report = $"M0.10 exported benchmark launched=True agents={fixture.TotalAgents} passage={fixture.Passage.ToString().ToLowerInvariant()} resolution={GetWindow().Size}{System.Environment.NewLine}" +
            $"rendered_frames={ordered.Length} frame_p50_ms={P(.5):0.###} frame_p95_ms={P(.95):0.###} frame_p99_ms={P(.99):0.###} fps_p50={1000/P(.5):0.###}{System.Environment.NewLine}" +
            $"controller_tick={fixture.ControllerTick} completed={fixture.Completed} backlog={fixture.Backlog} route_failures={fixture.RouteFailures} hash={fixture.CompositeHash()}{System.Environment.NewLine}" +
            "presentation=one-visible-destination other-destinations=off-camera-same-authoritative-logic no-despawn=True" + System.Environment.NewLine;
        File.WriteAllText(_benchmarkOutputPath!, report);
        GetViewport().GetTexture().GetImage().SavePng(Path.ChangeExtension(_benchmarkOutputPath!, ".png"));
        GD.Print(report); GetTree().Quit(0);
    }

    private void SetFoundationSpeed(RequestedSpeed speed)
    {
        _session.RequestSpeed(speed); _foundationClock.RequestedSpeed = speed; _foundationClock.ResetMeasurement();
        GD.Print($"FOUNDATION_SPEED requested={(int)speed}x");
    }

    private void HandleFoundationPauseInput()
    {
        _pauseInputRouteVerified = true;
        ToggleFoundationPause();
        _foundationClock.ResetBoundary();
        _foundationPresentation.Reset(_session.CaptureSnapshot());
    }

    private void ToggleFoundationPause()
    {
        var paused = !_session.IsPaused;
        _ = _session.Execute(new CommandEnvelope(new CommandId(900_000UL + _session.NextSubmissionSequence), _session.CampaignId,
            _session.Phase, _session.CurrentTick, _session.NextSubmissionSequence, null, new SetPausedCommand(paused)));
        _foundationClock.IsPaused = paused;
        GD.Print($"FOUNDATION_PAUSE paused={paused}");
    }

    private string SaveDirectory => ProjectSettings.GlobalizePath("user://saves");
    private void ManualSave()
    {
        var result = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-foundation", new SaveWriteRequest(_session, _saveCompatibility, "manual", DateTimeOffset.UtcNow));
        _saveStatus = result.IsSuccess ? "SAVED" : "SAVE ERROR";
        if (result.IsSuccess) _manualSaveHash = _session.CaptureSnapshot().AuthoritativeHash;
        GD.Print($"FOUNDATION_MANUAL_SAVE success={result.IsSuccess} tick={_session.CurrentTick} error={result.Error ?? "none"}");
    }

    private void ManualLoad()
    {
        var selectedBeforeLoad = _selectedAttendeeId;
        var result = SaveFileAdapter.LoadSlot(SaveDirectory, "manual-foundation", _saveCompatibility);
        if (result.IsSuccess)
        {
            _session = result.Session!;
            _foundationFixture = _foundationFixture! with { Session = _session };
            if (_foundationCaptureDirectory is not null)
            {
                var reference = SaveFileAdapter.LoadSlot(SaveDirectory, "manual-foundation", _saveCompatibility);
                if (reference.IsSuccess) _foundationReference = _foundationReference! with { Session = reference.Session! };
            }
            _foundationClock.IsPaused = _session.IsPaused; _foundationClock.RequestedSpeed = _session.RequestedSpeed;
            _foundationClock.ResetBoundary();
            _foundationPresentation.Reset(_session.CaptureSnapshot());
            _autosaveScheduler.Rebase();
            _manualRestoreVerified = _manualSaveHash.Length > 0 && _session.CaptureSnapshot().AuthoritativeHash == _manualSaveHash;
            _selectionRetainedAfterLoad = selectedBeforeLoad is { } selected && _session.CaptureSnapshot().NavigationAgents.Any(item => item.Id == selected) && _selectedAttendeeId == selected;
            _saveStatus = "LOADED";
        }
        else _saveStatus = "LOAD ERROR";
        GD.Print($"FOUNDATION_MANUAL_LOAD success={result.IsSuccess} tick={_session.CurrentTick} error={result.Error ?? "none"}");
    }

    private void AdvanceFoundationPresentation(double delta)
    {
        var cap = _foundationCaptureDirectory is not null && _foundationCaptureStage == 3 ? 2 : FoundationClock.MaximumTicksPerFrame;
        var ticks = _foundationClock.Schedule(delta, cap);
        for (var tick = 0; tick < ticks; tick++)
        {
            _session.AdvanceTicks(1);
            if (_foundationCaptureDirectory is not null) _foundationReference!.Session.AdvanceTicks(1);
            _foundationPresentation.Advance(_session.CaptureSnapshot());
        }
        if (_autosaveScheduler.Advance(delta))
        {
            var saved = AutosaveRotation.Save(SaveDirectory, _session, _saveCompatibility, DateTimeOffset.UtcNow, _autosaveGeneration);
            _saveStatus = saved.IsSuccess ? "AUTOSAVED" : "AUTOSAVE ERROR";
            if (saved.IsSuccess) { _autosaveGeneration++; _autosaveWrites++; }
        }
        var snapshot = _session.CaptureSnapshot();
        foreach (var agent in snapshot.NavigationAgents)
        {
            var sample = _foundationPresentation.Sample(agent.Id, _foundationClock.InterpolationFraction);
            _attendeeVisuals[agent.Id].Position = new Vector3((float)(sample.XMillimetres / 1000), 0.04f, (float)(sample.ZMillimetres / 1000));
        }
        RefreshAttendeeInspector();
        var queue = snapshot.ServiceQueues.Single();
        var counts = FoundationDiagnostics.Count(snapshot);
        var clockStatus = _session.IsPaused ? "PAUSED • CAMERA / INSPECT / SAVE ACTIVE" : $"REQUEST {(int)_session.RequestedSpeed}×  ATTAINED {_foundationClock.AttainedSpeed:0.00}×  {(_foundationClock.IsOverloaded ? "⚠ REDUCED" : "ON TARGET")}";
        _hashLabel.Text = $"M0 FOUNDATION • 50 AUTONOMOUS ATTENDEES\nTRAVELLING {counts.Travelling}  QUEUED {counts.QueueMembers}  WAITING {counts.Waiting}  IN SERVICE {counts.InService}\nSERVED {counts.Served}  FAILED {counts.Failed}  {clockStatus}  {_saveStatus}\nTICK {snapshot.CurrentTick}  HASH {snapshot.AuthoritativeHash[..12]}";
        if (_foundationCaptureDirectory is not null) ProcessFoundationCapture(snapshot, queue);
    }

    private void ProcessFoundationCapture(SessionSnapshot snapshot, ServiceQueueSnapshot queue)
    {
        if (_foundationCaptureStage == 0 && snapshot.CurrentTick >= 250) CaptureFoundation("busy-approach");
        else if (_foundationCaptureStage == 1 && queue.ActiveOwnerId is not null)
        {
            if (!_pauseCapturePrepared)
            {
                HandleFoundationPauseInput(); _pauseHash = _session.CaptureSnapshot().AuthoritativeHash;
                Rotate(1); _camera.Size = 72; ApplyCamera(); _pauseCaptureFrames = 3; _pauseCapturePrepared = true; return;
            }
            if (--_pauseCaptureFrames > 0) return;
            if (_pausePickFrames == 0)
            {
                // Exercise the same ray-pick route as a real click. Crowding can put another
                // attendee in front of the requested one, so success means any stable attendee
                // collider was resolved through Pick rather than selecting by ID directly.
                foreach (var attendeeId in _foundationFixture!.AgentIds)
                {
                    Pick(_camera.UnprojectPosition(_attendeeVisuals[attendeeId].GlobalPosition + new Vector3(0, 0.85f, 0)));
                    if (_selectedAttendeeId.HasValue) break;
                }
                _attendeePickVerified = _selectedAttendeeId.HasValue;
                _pausePickFrames = 2;
                return;
            }
            if (--_pausePickFrames > 0) return;
            _pauseVerified = _session.IsPaused && _session.CaptureSnapshot().AuthoritativeHash == _pauseHash && _orientation == 1;
            CaptureFoundation("paused-inspection");
        }
        else if (_foundationCaptureStage == 2)
        {
            if (!_saveCapturePrepared)
            {
                HandleFoundationPauseInput(); ManualSave();
                _manualMutationTicks = 12; _saveCaptureFrames = 3; _saveCapturePrepared = true; return;
            }
            if (--_saveCaptureFrames > 0) return;
            CaptureFoundation("save-load-diagnostics");
            SetFoundationSpeed(RequestedSpeed.FourX);
        }
        else if (_foundationCaptureStage == 3 && _manualMutationTicks > 0)
        {
            if (_manualMutationTicks == 12)
            {
                var inputTimer = System.Diagnostics.Stopwatch.StartNew();
                Rotate(1); Rotate(-1);
                inputTimer.Stop();
                _pressureInputLatencyMilliseconds = inputTimer.Elapsed.TotalMilliseconds;
                _pressureInputVerified = _pressureInputLatencyMilliseconds < 50 && _selectedAttendeeId.HasValue;
            }
            var pressure = System.Diagnostics.Stopwatch.StartNew();
            ulong work = 0;
            while (pressure.ElapsedMilliseconds < 18) work = unchecked(work * 6364136223846793005UL + 1442695040888963407UL);
            pressure.Stop();
            GC.KeepAlive(work);
            _maximumPressureWorkMilliseconds = Math.Max(_maximumPressureWorkMilliseconds, pressure.Elapsed.TotalMilliseconds);
            _manualMutationTicks--;
            if (_manualMutationTicks <= 0)
            {
                _overloadObserved |= _foundationClock.IsOverloaded;
                _pressureAttained = _foundationClock.AttainedSpeed;
                _pressureDebt = _foundationClock.DebtTicks;
                ManualLoad(); SetFoundationSpeed(RequestedSpeed.FourX); CaptureFoundation("overloaded-reduced-speed");
            }
        }
        else if (_foundationCaptureStage == 4 && FiftyAgentFoundationFixture.AllCompleted(_foundationFixture!))
        {
            if (++_completionFrames < 3) return;
            CaptureFoundation("complete");
            var restoredExact = _manualRestoreVerified;
            var reference = _foundationReference!.Session.CaptureSnapshot();
            var parity = snapshot.AuthoritativeHash == reference.AuthoritativeHash;
            var autosaves = Enumerable.Range(0, 3).Count(i => File.Exists(SaveFileAdapter.ResolveSlotPath(SaveDirectory, $"autosave-{i}")));
            var passed = snapshot.Transactions.Count == 50 && queue.OrderedMembers.Count == 0 && failedCount(queue) == 0 && parity && restoredExact && autosaves == 3 && _autosaveWrites >= 3 &&
                _pauseVerified && _pauseInputRouteVerified && _attendeePickVerified && _overloadObserved;
            passed &= _selectionRetainedAfterLoad && _pressureInputVerified;
            var report = $"M0.09 exported-runtime verification passed={passed} resolution={GetWindow().Size}{System.Environment.NewLine}" +
                $"tick={snapshot.CurrentTick} transactions={snapshot.Transactions.Count} queue={queue.OrderedMembers.Count} failed={failedCount(queue)} festival_cash_p={snapshot.FestivalFinances.Single().CashPennies} stock={snapshot.OwnedStocks.Single().Quantity}{System.Environment.NewLine}" +
                $"rendered_hash={snapshot.AuthoritativeHash} headless_hash={reference.AuthoritativeHash} parity={parity}{System.Environment.NewLine}" +
                $"manual_save_restore={restoredExact} autosave_slots={autosaves} autosave_writes={_autosaveWrites} autosave_cadence_real_seconds={_autosaveScheduler.CadenceSeconds:0.###} requested=4x completion_attained={_foundationClock.AttainedSpeed:0.000}x pressure_attained={_pressureAttained:0.000}x pressure_debt_ticks={_pressureDebt:0.###} pressure_work_max_ms={_maximumPressureWorkMilliseconds:0.###} overload_reported={_overloadObserved}{System.Environment.NewLine}" +
                $"pause_hash_frozen_camera_rotated={_pauseVerified} shared_pause_input_route={_pauseInputRouteVerified} attendee_pick_event={_attendeePickVerified} selected_attendee_retained={_selectionRetainedAfterLoad} shared_action_call_ms={_pressureInputLatencyMilliseconds:0.###} shared_action_responsive={_pressureInputVerified} os_event_latency_measured=false palette_assignment=ordinal_modulo_10 player_attendee_controls=false fixture_only=true" + System.Environment.NewLine;
            File.WriteAllText(Path.Combine(_foundationCaptureDirectory!, "verification-1280x720.txt"), report);
            GD.Print($"FOUNDATION_CAPTURE_COMPLETE passed={passed} tick={snapshot.CurrentTick}");
            _foundationCaptureDirectory = null; GetTree().Quit(passed ? 0 : 2);
        }
        static int failedCount(ServiceQueueSnapshot value) => value.Agents.Count(item => item.Action == ServiceQueueAgentAction.Failed);
    }

    private void CaptureFoundation(string stage)
    {
        var path = Path.Combine(_foundationCaptureDirectory!, $"foundation-{stage}-1280x720.png");
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"FOUNDATION_CAPTURE stage={stage} path={path} result={error}");
        _foundationCaptureStage++;
    }

    private void AdvanceQueuePresentation(double delta)
    {
        _navigationPresentedFrames++;
        _navigationTickDebt += delta * 80.0;
        var ticks = Math.Min((int)_navigationTickDebt, 16);
        for (var tick = 0; tick < ticks; tick++)
        {
            _session.AdvanceTicks(1); _queueReference!.Session.AdvanceTicks(1);
            if (_session.CurrentTick == 400)
            {
                _ = ServiceQueueFixture.SetOpen(_queueFixture!, false, 2);
                _ = ServiceQueueFixture.SetOpen(_queueReference!, false, 2);
                _queueClosed = true;
            }
            if (_session.CurrentTick == 430)
            {
                ApplyQueueReopenFixture(_queueFixture!); ApplyQueueReopenFixture(_queueReference!);
                _queueReopened = true;
            }
        }
        if (ticks > 0) _navigationTickDebt -= ticks;
        var snapshot = _session.CaptureSnapshot();
        foreach (var agent in snapshot.NavigationAgents) _attendeeVisuals[agent.Id].Position = ToWorld(agent);
        var queue = snapshot.ServiceQueues.Single();
        _hashLabel.Text = $"AUTONOMOUS SERVICE QUEUE  MEMBERS {queue.OrderedMembers.Count}  SALES {snapshot.Transactions.Count}\nTICK {_session.CurrentTick}  HASH {snapshot.AuthoritativeHash[..12]}";
        if (_navigationCaptureStage == 0 && _navigationPresentedFrames >= 12) CaptureQueue("travelling");
        else if (_navigationCaptureStage == 1 && _queueClosed && !_queueReopened && _session.CurrentTick >= 410) CaptureQueue("closed");
        else if (_navigationCaptureStage == 2 && _queueReopened && _session.CurrentTick >= 450) CaptureQueue("reopened-travelling");
        else if (_navigationCaptureStage == 3 && queue.ActiveOwnerId is not null && queue.OrderedMembers.All(id =>
            snapshot.NavigationAgents.Single(agent => agent.Id == id).Action == AgentNavigationAction.Arrived)) CaptureQueue("physical-queue");
        if (snapshot.Transactions.Count != 5 || queue.OrderedMembers.Count != 0) return;
        _queuePurchaseFrames++;
        if (_navigationCaptureStage == 4 && _queuePurchaseFrames >= 3) CaptureQueue("purchases-complete");
        var allDeparted = ServiceQueueFixture.AllAtExit(_queueFixture!);
        if (!allDeparted) return;
        _navigationArrivalFrames++;
        if (_navigationCaptureStage == 5 && _navigationArrivalFrames >= 3) CaptureQueue("departure-complete");
        if (_navigationArrivalFrames < 8) return;
        var reference = _queueReference!.Session.CaptureSnapshot();
        var buyersMatch = snapshot.Transactions.Select(item => item.BuyerId).SequenceEqual(_queueFixture!.AgentIds);
        var passed = snapshot.AuthoritativeHash == reference.AuthoritativeHash && snapshot.CurrentTick == reference.CurrentTick &&
            snapshot.Transactions.Count == 5 && buyersMatch && snapshot.FestivalFinances.Single().CashPennies == 1500 &&
            snapshot.OwnedStocks.Single().Quantity == 0 && queue.ActiveOwnerId is null && queue.Agents.All(item => item.ReservedSlotIndex is null) &&
            allDeparted && ServiceQueueFixture.AllAtExit(_queueReference!);
        var report = $"M0.08 exported-runtime verification passed={passed} resolution={GetWindow().Size}{System.Environment.NewLine}" +
            $"same_tick={snapshot.CurrentTick} rendered_hash={snapshot.AuthoritativeHash} headless_hash={reference.AuthoritativeHash} equivalent={snapshot.AuthoritativeHash == reference.AuthoritativeHash}{System.Environment.NewLine}" +
            $"closure_tick=400 reopened_clean=True transactions={snapshot.Transactions.Count} buyer_order={string.Join(',', snapshot.Transactions.Select(item => item.BuyerId.Value))}{System.Environment.NewLine}" +
            $"festival_cash_p={snapshot.FestivalFinances.Single().CashPennies} stock={snapshot.OwnedStocks.Single().Quantity} active_owner=none reservations_released={queue.Agents.All(item => item.ReservedSlotIndex is null)} all_departed={allDeparted}{System.Environment.NewLine}" +
            "input=attendee-ai-fixture player_navigation_controls=False player_queue_controls=False" + System.Environment.NewLine;
        File.WriteAllText(Path.Combine(_queueCaptureDirectory!, "verification-1280x720.txt"), report);
        GD.Print($"QUEUE_CAPTURE_COMPLETE passed={passed} tick={snapshot.CurrentTick}");
        _queueCaptureDirectory = null; GetTree().Quit(passed ? 0 : 2);
    }

    private static void ApplyQueueReopenFixture(ServiceQueueFixtureState fixture)
    {
        _ = ServiceQueueFixture.SetOpen(fixture, true, 3);
        for (var index = 0; index < fixture.AgentIds.Count; index++)
            _ = ServiceQueueFixture.Enqueue(fixture, fixture.AgentIds[index], fixture.Session.NextSubmissionSequence, (ulong)(4 + index));
    }

    private void CaptureQueue(string stage)
    {
        var path = Path.Combine(_queueCaptureDirectory!, $"queue-{stage}-1280x720.png");
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"QUEUE_CAPTURE stage={stage} path={path} result={error}");
        _navigationCaptureStage++;
    }

    private void AdvanceNavigationPresentation(double delta)
    {
        _navigationPresentedFrames++;
        // Wall time only schedules fixed authoritative ticks; it never enters simulation state.
        _navigationTickDebt += delta * 80.0;
        var ticks = Math.Min((int)_navigationTickDebt, 16);
        if (ticks > 0)
        {
            _presentationFrom = ToWorld(_session.CaptureSnapshot().NavigationAgents.Single());
            _session.AdvanceTicks(ticks);
            for (var tick = 0; tick < ticks; tick++) _navigationReference!.Session.AdvanceTicks(1);
            _navigationTickDebt -= ticks;
            _presentationTo = ToWorld(_session.CaptureSnapshot().NavigationAgents.Single());
        }
        _attendeeVisual.Position = _presentationFrom.Lerp(_presentationTo, (float)Math.Clamp(_navigationTickDebt, 0, 1));

        var agent = _session.CaptureSnapshot().NavigationAgents.Single();
        _hashLabel.Text = $"ATTENDEE AI  {agent.Action.ToString().ToUpperInvariant()}\nTICK {_session.CurrentTick}  HASH {_session.CaptureSnapshot().AuthoritativeHash[..12]}";
        if (_navigationCaptureStage == 0 && _navigationPresentedFrames >= 12) CaptureNavigation("start");
        else if (_navigationCaptureStage == 1 && _session.CurrentTick >= 1400) CaptureNavigation("mid");
        if (agent.Action != AgentNavigationAction.Arrived) return;
        _navigationArrivalFrames++;
        if (_navigationCaptureStage == 2 && _navigationArrivalFrames >= 2) CaptureNavigation("arrived");
        if (_navigationArrivalFrames < 8) return;

        var renderedHash = _session.CaptureSnapshot().AuthoritativeHash;
        var referenceHash = _navigationReference!.Session.CaptureSnapshot().AuthoritativeHash;
        var blockedCell = _session.TraversalGrid!.Overrides.Values.First(item => !item.IsWalkable).Cell;
        var before = (agent.XMillimetres, agent.ZMillimetres);
        var renderedBlocked = IssueBlockedTarget(_session, agent.Id, blockedCell, 3);
        var referenceBlocked = IssueBlockedTarget(_navigationReference.Session, agent.Id, blockedCell, 3);
        var blockedAgent = _session.CaptureSnapshot().NavigationAgents.Single();
        var sameTickEquivalent = _session.CurrentTick == _navigationReference.Session.CurrentTick && renderedHash == referenceHash;
        var noTeleport = before == (blockedAgent.XMillimetres, blockedAgent.ZMillimetres);
        var passed = sameTickEquivalent && renderedBlocked.IsAccepted && referenceBlocked.IsAccepted &&
            blockedAgent.Action == AgentNavigationAction.NoRoute && noTeleport;
        var report = $"M0.07 exported-runtime verification passed={passed} resolution={GetWindow().Size}{System.Environment.NewLine}" +
            $"same_tick={_session.CurrentTick} rendered_hash={renderedHash} headless_hash={referenceHash} equivalent={sameTickEquivalent}{System.Environment.NewLine}" +
            $"arrival_action={agent.Action} position_mm={before.Item1},{before.Item2}{System.Environment.NewLine}" +
            $"blocked_target={blockedCell.X},{blockedCell.Z} action={blockedAgent.Action} expanded={blockedAgent.LastSearchExpandedNodes} no_teleport={noTeleport}{System.Environment.NewLine}" +
            "presentation_interpolation=visual-only authoritative_source=integer-millimetre-read-state input=attendee-ai-fixture" + System.Environment.NewLine;
        File.WriteAllText(Path.Combine(_navigationCaptureDirectory!, "verification-1280x720.txt"), report);
        GD.Print($"NAVIGATION_CAPTURE_COMPLETE passed={passed} tick={_session.CurrentTick}");
        _navigationCaptureDirectory = null;
        GetTree().Quit(passed ? 0 : 2);
    }

    private void CaptureNavigation(string stage)
    {
        var path = Path.Combine(_navigationCaptureDirectory!, $"navigation-{stage}-1280x720.png");
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"NAVIGATION_CAPTURE stage={stage} path={path} result={error}");
        _navigationCaptureStage++;
    }

    private static CommandResult IssueBlockedTarget(GameSession session, EntityId agentId, GridCell cell, ulong commandId) =>
        session.Execute(new CommandEnvelope(new CommandId(commandId), session.CampaignId, session.Phase,
            session.CurrentTick, session.NextSubmissionSequence, agentId,
            new SetAgentDestinationCommand(cell, "fixture.blocked-target")));

    private static Vector3 ToWorld(NavigationAgentSnapshot agent) =>
        new(agent.XMillimetres / 1000f, 0.04f, agent.ZMillimetres / 1000f);

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
            var pickTarget = id == "farm.main-gate"
                ? _gateLeafCollider.ToGlobal(new Vector3(1.695f, 0.79f, 0.08f))
                : visual.GlobalPosition + new Vector3(0, 1, 0);
            Pick(_camera.UnprojectPosition(pickTarget));
            var resolved = _selected?.StableId;
            var correct = resolved == id;
            passed &= correct;
            var target = id == "farm.main-gate" ? "visible-open-leaf" : "visible-object";
            lines.Add($"pick target={target} expected={id} resolved={resolved ?? "none"} correct={correct}");
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

        var grassBounds = GetCombinedMeshBounds(_grassVisuals);
        var trackBounds = GetCombinedMeshBounds(_trackVisuals);
        var grassSupportsBoundary = grassBounds.Position.X <= -31.9f && grassBounds.End.X >= 31.9f &&
            grassBounds.Position.Z <= -31.9f && grassBounds.End.Z >= 31.9f;
        var trackReachesGate = trackBounds.Position.Z <= -31.9f && trackBounds.End.Z >= 31.9f &&
            trackBounds.Position.X <= 0 && trackBounds.End.X >= 0;
        passed &= grassSupportsBoundary && trackReachesGate;
        lines.Add($"grass_bounds_x={grassBounds.Position.X:0.###}..{grassBounds.End.X:0.###} grass_bounds_z={grassBounds.Position.Z:0.###}..{grassBounds.End.Z:0.###} supports_boundary={grassSupportsBoundary}");
        lines.Add($"track_bounds_z={trackBounds.Position.Z:0.###}..{trackBounds.End.Z:0.###} reaches_gate_z30={trackReachesGate}");

        _middleDragging = true;
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false });
        var middleReleaseCleared = !_middleDragging;
        passed &= middleReleaseCleared;
        lines.Add($"middle_release_before_gui_clears_drag={middleReleaseCleared}");

        var currentHash = _session.CaptureSnapshot().AuthoritativeHash;
        var hashUnchanged = currentHash == _pausedHash && _session.IsPaused;
        passed &= hashUnchanged;
        lines.Add($"paused={_session.IsPaused} hash_before={_pausedHash} hash_after={currentHash} unchanged={hashUnchanged}");
        lines.Insert(0, $"M0.06 exported-runtime verification passed={passed} resolution={GetWindow().Size}");
        return (passed, string.Join(System.Environment.NewLine, lines) + System.Environment.NewLine);
    }

    private static Aabb GetCombinedMeshBounds(IEnumerable<Node3D> roots)
    {
        var hasBounds = false;
        var combined = new Aabb();
        foreach (var root in roots)
        foreach (var child in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (child is not MeshInstance3D mesh || mesh.Mesh is null) continue;
            var worldBounds = mesh.GlobalTransform * mesh.GetAabb();
            combined = hasBounds ? combined.Merge(worldBounds) : worldBounds;
            hasBounds = true;
        }
        return combined;
    }
}
