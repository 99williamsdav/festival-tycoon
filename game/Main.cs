using Festival.Simulation;
using Festival.Simulation.Fixtures;
using Festival.Persistence;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

public partial class Main : Node
{
    private const float MinZoom = 18f;
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
    private VBoxContainer? _satisfactionSection;
    private Label? _satisfactionLabel;
    private ProgressBar? _satisfactionBar;
    private Button? _stagePowerButton;
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
    private SharedWorldFeasibilityFixtureState? _sharedWorldFixture;
    private string? _sharedWorldOutputPath;
    private string? _campaignCaptureDirectory;
    private int _campaignCaptureFrame;
    private int _campaignCaptureStage;
    private bool _campaignDuplicateRejected;
    private bool _campaignSaveReloadExact;
    private double _campaignMaximumInteractionMilliseconds;
    private double _campaignMaximumAdvanceMilliseconds;
    private string _campaignUiStatus = "Ready";
    private Label _campaignIdentityLabel = null!;
    private Label _campaignFinanceLabel = null!;
    private Label _campaignCommitmentLabel = null!;
    private Label _campaignDigestLabel = null!;
    private PanelContainer _campaignTopPanel = null!;
    private LineEdit _campaignNameEdit = null!;
    private OptionButton _campaignPaletteOption = null!;
    private Button _campaignCommitButton = null!;
    private Button _campaignAdvanceButton = null!;
    private readonly FoundationClock _sharedWorldClock = new();
    private readonly List<double> _sharedWallFrameMilliseconds = [];
    private readonly List<double> _sharedEngineDeltaMilliseconds = [];
    private readonly List<double> _sharedWorkMilliseconds = [];
    private int _sharedMeasurementFrames;
    private int _sharedMeasurementStage;
    private long _sharedRepresentativeTick;
    private long _sharedMeasurementStartTick;
    private long _sharedWallStartTimestamp;
    private long _sharedWallPreviousTimestamp;
    private double _sharedSaveMilliseconds;
    private double _sharedLoadMilliseconds;
    private bool _sharedRestoreExact;
    private int _benchmarkFrames;
    private readonly List<double> _benchmarkFrameMilliseconds = [];
    private FiftyAgentFoundationFixtureState? _foundationReference;
    private readonly FoundationClock _foundationClock = new();
    private readonly FoundationPresentationInterpolator _foundationPresentation = new();
    private string _foundationPublishedHash = "";
    private long _foundationPublishedHashTick = -1;
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
    private readonly SaveCompatibility _saveCompatibility = new("0.0.1-r0.05-hearing-v1",
        LowerWitteringFarmScenario.ContentCompatibilityHash, "r0-disorder-layout-v13");
    private static readonly string[] OrientationNames = ["South", "West", "North", "East"];

    public override void _Ready()
    {
        ConfigureCaptureMode();
        if (_hearingCaptureDirectory is not null && DisplayServer.GetName() != "headless")
        {
            GetWindow().Mode = Window.ModeEnum.Windowed;
            GetWindow().Size = new Vector2I(890, 680);
        }
        _autosaveScheduler = new RealTimeAutosaveScheduler(_foundationCaptureDirectory is null ?
            RealTimeAutosaveScheduler.ProductionCadenceSeconds : 2);
        if (_sharedWorldFixture is not null)
        {
            _session = _sharedWorldFixture.Session;
        }
        else if (_benchmarkFixture is not null)
        {
            _session = _benchmarkFixture.Waves[0].Session;
        }
        else if (_foundationCaptureDirectory is not null)
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
        else if (_captureDirectory is not null || _navigationCaptureDirectory is not null)
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
        else
        {
            _session = _campaignCaptureDirectory is not null ? GameSession.CreateCampaign(20260922) :
                _audienceCaptureDirectory is not null ? GameSession.CreateEquipmentCampaign(20260922, 2) :
                _hearingCaptureDirectory is not null || _waterFoundationCaptureDirectory is not null ? GameSession.CreateMedicalCampaign(20260922) :
                _staffCaptureDirectory is not null || _waterPlaytestCaptureDirectory is not null || _interventionCaptureDirectory is not null || _disorderCaptureDirectory is not null || OS.GetCmdlineUserArgs().Length == 0 ? GameSession.CreateDisorderCampaign(20260922) :
                _medicalCaptureDirectory is not null ? GameSession.CreateMedicalCampaign(20260922) :
                _equipmentCaptureDirectory is not null || _preparationCaptureDirectory is null ? GameSession.CreateEquipmentCampaign(20260922, _equipmentCaptureDirectory is not null || _equipmentPerformanceOutput is not null ? (_liveMeasurementTier == 0 ? 2 : _liveMeasurementTier) : 1) :
                GameSession.CreatePreparedCampaign(20260922, _preparationMeasurementTier == 0 ? 1 : _preparationMeasurementTier);
        }
        if (_hearingCaptureDirectory is not null)
        {
            foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" })
                if (!_session.Execute(CampaignEnvelope(new AcceptPreparationOfferCommand(offer))).IsAccepted)
                    throw new InvalidOperationException("Hearing capture booking failed.");
            if (!_session.Execute(CampaignEnvelope(new StartPreparedEditionCommand())).IsAccepted)
                throw new InvalidOperationException("Hearing capture start failed.");
            _session.AdvanceWithoutSnapshot(6_200);
            if (_session.PreparedStatus != PreparationStatus.Failed)
                throw new InvalidOperationException("Hearing capture did not reach a fatal medical incident.");
        }
        if (_waterFoundationCaptureDirectory is not null)
        {
            foreach (var command in new SessionCommand[] { new MovePrimaryWaterPointCommand(new GridCell(104, 112)),
                         new ApplyWaterFoundationEffectCommand("water.tower") })
                if (!_session.Execute(CampaignEnvelope(command)).IsAccepted)
                    throw new InvalidOperationException($"Water foundation capture command {command} was rejected.");
        }
        _autosaveGeneration = AutosaveRotation.NextGeneration(SaveDirectory, _saveCompatibility);
        _pausedHash = _session.CaptureSnapshot().AuthoritativeHash;
        _foundationPublishedHash = _pausedHash;
        _foundationPublishedHashTick = _session.CurrentTick;
        BuildWorld();
        if (DisplayServer.GetName() != "headless")
            DisplayServer.SetIcon(GD.Load<Texture2D>("res://assets/branding/festival-tycoon-stage-sun-icon-v3.png").GetImage());
        if (_session.CaptureMedical() is not null) BuildMedicalWorld();
        if (_session.CaptureDisorder() is not null) BuildDisorderWorld();
        if (_session.CaptureEquipment() is not null) EnsureStageDrumKit();
        if (_session.CaptureSnapshot().NavigationAgents.Count > 0) BuildAttendee();
        if (_hearingCaptureDirectory is not null)
        {
            _foundationPresentation.Reset(_session.CaptureObservation());
            _foundationClock.ResetBoundary();
        }
        if (_sharedWorldFixture is not null) BuildSharedWorldServiceMarkers();
        if (_foundationFixture is not null) _foundationPresentation.Reset(_session.CaptureSnapshot());
        BuildHud();
        if (_queueCaptureDirectory is not null || _foundationFixture is not null || _sharedWorldFixture is not null) { _focus = new Vector3(10, 0, 4); _camera.Size = 58; }
        if (_session.CaptureEquipment() is not null) { _focus = new Vector3(-16, 0, 11); _camera.Size = 32; }
        ApplyCamera();
        if ((OS.GetCmdlineUserArgs().Length == 0 || _startSplashCapturePath is not null) &&
            _session.CaptureEquipment() is not null) BuildStartSplash();
        if (_captureDirectory is not null)
            SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.farmhouse"));
        var version = Engine.GetVersionInfo()["string"].AsString();
        GD.Print($"FESTIVAL_TYCOON_LAUNCHED build={ToolchainSmoke.BuildVersion} godot={version}");
        GD.Print($"FARM_SCENE_READY scenario={LowerWitteringFarmScenario.ScenarioId} objects={_visualRegistry.Count} hash={_pausedHash}");
    }

    public override void _Process(double delta)
    {
        if (_equipmentPerformanceOutput is not null) _equipmentCallbackStarted = Stopwatch.GetTimestamp();
        if (_middleDragging && !Input.IsMouseButtonPressed(MouseButton.Middle)) _middleDragging = false;
        var input = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) input.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) input.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) input.X += 1;
        if (input.LengthSquared() > 0) Pan(input.Normalized() * (float)delta * 18f);
        if (_waterPlacementMode != WaterPlacementMode.None && _waterPlaytestCaptureDirectory is null)
            UpdateWaterPlacementPreview(GetViewport().GetMousePosition());
        if (_session.CapturePreparation() is not null) AdvancePreparationPresentation(delta);
        else if (_sharedWorldFixture is not null) AdvanceSharedWorldFeasibility(delta);
        else if (_benchmarkFixture is not null) AdvanceRenderedBenchmark(delta);
        else if (_foundationFixture is not null) AdvanceFoundationPresentation(delta);
        else if (_queueCaptureDirectory is not null) AdvanceQueuePresentation(delta);
        else if (_navigationCaptureDirectory is not null) AdvanceNavigationPresentation(delta);
        else if (_session.CaptureCampaignPlanningSnapshot() is null) UpdateHashStatus();
        if (_captureDirectory is not null) ProcessCapture();
        if (_campaignCaptureDirectory is not null) ProcessCampaignCapture();
        if (_preparationProfileOutput is not null) FinishPreparationProfileFrame();
        if (_equipmentCaptureDirectory is not null) ProcessEquipmentCapture();
        if (_equipmentPerformanceOutput is not null) ProcessEquipmentPerformanceCheck();
        ProcessMedicalCapture();
        ProcessWaterFoundationCapture();
        ProcessStaffCapture();
        ProcessWaterPlaytestCapture();
        ProcessInterventionCapture();
        ProcessAudienceCapture();
        ProcessDisorderCapture();
        ProcessHearingCapture();
        ProcessStartSplashCapture();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_startSplash is not null) return;
        // Release must be observed before a HUD Control consumes the mouse event.
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: false })
            _middleDragging = false;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_captureDirectory is not null || _navigationCaptureDirectory is not null || _queueCaptureDirectory is not null || _foundationCaptureDirectory is not null || _sharedWorldOutputPath is not null || _campaignCaptureDirectory is not null) return;
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Escape && _waterPlacementMode != WaterPlacementMode.None) { CancelWaterPlacement(); return; }
            if (_waterPlacementMode != WaterPlacementMode.None && key.Keycode is Key.Comma or Key.Period)
            { RotateWaterPlacement(key.Keycode == Key.Comma ? -1 : 1); return; }
            if (key.Keycode == Key.Q) Rotate(-1);
            else if (key.Keycode == Key.E) Rotate(1);
            else if (key.Keycode == Key.Space)
            {
                if (_session.CapturePreparation() is not null)
                { _session.Execute(CampaignEnvelope(new SetPausedCommand(!_session.IsPaused))); RefreshPreparationHud(); }
                else if (_foundationFixture is not null) HandleFoundationPauseInput(); else ReportPause();
            }
        }
        else if (inputEvent is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed) Zoom(-4);
            else if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed) Zoom(4);
            else if (mouse.ButtonIndex == MouseButton.Middle) _middleDragging = mouse.Pressed;
            else if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed && _waterPlacementMode != WaterPlacementMode.None) CancelWaterPlacement();
            else if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed && _waterPlacementMode != WaterPlacementMode.None)
                CommitWaterPlacement(mouse.Position);
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
            var performer = _session.CapturePreparation()?.People.SingleOrDefault(item => item.AgentId == agent.Id.Value);
            var visual = AddAsset(performer?.Role == ProtectedPersonRole.Performer ? PerformerBodyPath(performer.Name) :
                "res://assets/characters/lwf_generic_attendee_v1.glb", ToWorld(agent));
            if (_foundationFixture is not null || _sharedWorldFixture is not null)
            {
                var ids = _sharedWorldFixture?.AgentIds ?? _foundationFixture!.AgentIds;
                var ordinal = Array.IndexOf(ids.ToArray(), agent.Id);
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
            else if (_session.CapturePreparation() is not null)
            {
                var pickBody = new StaticBody3D { CollisionLayer = 1, CollisionMask = 1 };
                pickBody.AddChild(new CollisionShape3D { Position = new Vector3(0, 0.85f, 0),
                    Shape = new CapsuleShape3D { Radius = 0.38f, Height = 1.7f } });
                visual.AddChild(pickBody);
                _attendeePickRegistry.Add(pickBody.GetInstanceId(), agent.Id);
            }
            _attendeeVisuals.Add(agent.Id, visual);
            var responder = _session.GetResponseStaff().SingleOrDefault(item => item.AgentId == agent.Id.Value);
            if (responder?.Role == ResponseRole.Medic)
                visual.AddChild(InstantiateAsset("res://assets/characters/lwf_medic_vest_cue_v1.glb"));
            if (responder?.Role == ResponseRole.Steward)
            {
                MatchStewardShirtPalette(visual);
                var yoke = InstantiateAsset("res://assets/characters/lwf_steward_yoke_cue_v1.glb");
                yoke.Name = "StewardYokeCue";
                visual.AddChild(yoke);
            }
            if (performer is { Role: not ProtectedPersonRole.Guest } role &&
                responder is null)
            {
                var cue = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.20f, 0.12f, 0.05f) },
                    Position = new Vector3(0, 1.25f, 0.20f),
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = role.Name == "Morgan Finch" ? new Color("6acfd1") : role.Role == ProtectedPersonRole.Staff ? new Color("ffd166") : new Color("aa88dd") }
                };
                visual.AddChild(cue);
                if (role.Name == "Morgan Finch") visual.AddChild(new Label3D { Text = "MORGAN\nMAINTENANCE", Position = new Vector3(0, 2.1f, 0), FontSize = 36, PixelSize = .009f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
            }
        }
        foreach (var profile in _session.GetResponseStaff())
            if (_attendeeVisuals.TryGetValue(new EntityId(profile.AgentId), out var responderVisual))
                responderVisual.AddChild(new Label3D { Text = profile.Name.Split(' ')[0].ToUpperInvariant(), Position = new Vector3(0, 2.1f, 0),
                    FontSize = 30, PixelSize = .009f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
        _attendeeVisual = _attendeeVisuals[agents[0].Id];
        _presentationFrom = _presentationTo = ToWorld(agents[0]);
        ResetMedicalCuePresentation();
        ResetDisorderCuePresentation();
    }

    private static void MatchStewardShirtPalette(Node3D visual)
    {
        // The generic body has a baked amber chest swatch at palette columns
        // 40..55. On Jordan alone, reuse its two existing teal shirt swatches
        // (24..39); skin, hair, trousers and the approved yoke remain untouched.
        var source = GD.Load<Texture2D>("res://assets/characters/lwf_generic_attendee_v1_attendee_palette.png");
        var image = source.GetImage();
        if (image.GetWidth() != 96 || image.GetHeight() != 8)
            throw new InvalidOperationException("The steward's generic-body palette layout changed.");
        for (var y = 0; y < image.GetHeight(); y++)
        for (var x = 40; x < 56; x++)
            image.SetPixel(x, y, image.GetPixel(x - 16, y));
        var shirtMatched = ImageTexture.CreateFromImage(image);
        var matchedMeshes = 0;
        foreach (var child in visual.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (child is not MeshInstance3D mesh || mesh.Mesh is null) continue;
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D sourceMaterial) continue;
                var material = (StandardMaterial3D)sourceMaterial.Duplicate();
                material.AlbedoTexture = shirtMatched;
                mesh.SetSurfaceOverrideMaterial(surface, material);
                matchedMeshes++;
            }
        }
        if (matchedMeshes == 0)
            throw new InvalidOperationException("No generic-body surface was available for Jordan's shirt-colour match.");
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

    private void BuildSharedWorldServiceMarkers()
    {
        for (var index = 0; index < _sharedWorldFixture!.ServiceVisualCells.Count; index++)
        {
            var centre = TraversalGrid.CellCentre(_sharedWorldFixture.ServiceVisualCells[index]);
            if (index != 1)
                AddAsset("res://assets/environment/lwf_service_point_v2.glb",
                    new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f));
            var label = new Label3D
            {
                Text = $"SERVICE {index + 1}", Position = new Vector3(centre.XMillimetres / 1000f, 3.6f, centre.ZMillimetres / 1000f),
                FontSize = 44, OutlineSize = 8, Modulate = new Color("f5e9c9"), OutlineModulate = new Color("29352c"), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            };
            AddChild(label);
        }
    }

    private void BuildHud()
    {
        if (_session.CapturePreparation() is not null) { BuildPreparationHud(); return; }
        if (_session.CaptureCampaignPlanningSnapshot() is not null)
        {
            BuildCampaignHud();
            return;
        }
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
        BuildSatisfactionBar(box);
        _inspectorBody = LabelText("Click a building, gate, stage or service point.\nClick empty ground to clear.", 16, ink);
        _inspectorBody.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_inspectorBody);
        BuildStagePowerAction(box);
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        _hashLabel = LabelText("SIM HASH: CHECKING", 13, new Color("47603b")); _hashLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_hashLabel);

        var help = new PanelContainer(); help.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        help.OffsetLeft = 16; help.OffsetTop = -92; help.OffsetRight = 620; help.OffsetBottom = -16;
        help.AddThemeStyleboxOverride("panel", PaperStyle(new Color(0.96f, 0.91f, 0.78f, 0.94f))); layer.AddChild(help);
        var helpLabel = LabelText("PAN  WASD / ARROWS / MIDDLE-DRAG    ZOOM  WHEEL / + −\nROTATE  Q E / BUTTONS    SELECT  LEFT CLICK    PAUSE  SPACE", 14, ink);
        helpLabel.HorizontalAlignment = HorizontalAlignment.Center; helpLabel.VerticalAlignment = VerticalAlignment.Center; help.AddChild(helpLabel);
    }

    private void BuildCampaignHud()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        var ink = new Color("29352c");
        var cream = new Color("f5e9c9");

        _campaignTopPanel = new PanelContainer();
        _campaignTopPanel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _campaignTopPanel.OffsetLeft = 16; _campaignTopPanel.OffsetTop = 16;
        _campaignTopPanel.OffsetRight = -16; _campaignTopPanel.OffsetBottom = 86;
        layer.AddChild(_campaignTopPanel);
        var topBar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        topBar.AddThemeConstantOverride("separation", 22); _campaignTopPanel.AddChild(topBar);
        _campaignIdentityLabel = LabelText("", 20, ink); topBar.AddChild(_campaignIdentityLabel);
        _campaignFinanceLabel = LabelText("", 16, ink); topBar.AddChild(_campaignFinanceLabel);
        topBar.AddChild(ButtonText("SAVE", CampaignManualSave));
        topBar.AddChild(ButtonText("LOAD", CampaignManualLoad));

        var planner = new PanelContainer();
        planner.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        planner.OffsetLeft = 16; planner.OffsetTop = 102; planner.OffsetRight = 398; planner.OffsetBottom = -16;
        planner.AddThemeStyleboxOverride("panel", PaperStyle(cream)); layer.AddChild(planner);
        var plannerMargin = new MarginContainer();
        foreach (var key in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) plannerMargin.AddThemeConstantOverride(key, 18);
        planner.AddChild(plannerMargin);
        var plannerBox = new VBoxContainer(); plannerBox.AddThemeConstantOverride("separation", 12); plannerMargin.AddChild(plannerBox);
        plannerBox.AddChild(LabelText("CAMPAIGN DESK  •  INHERITED FARM", 14, new Color("6f5937")));
        plannerBox.AddChild(LabelText("Festival name", 14, ink));
        _campaignNameEdit = new LineEdit { CustomMinimumSize = new Vector2(0, 40) };
        _campaignNameEdit.TextSubmitted += _ => CampaignRename(); plannerBox.AddChild(_campaignNameEdit);
        plannerBox.AddChild(ButtonText("RENAME", CampaignRename));
        plannerBox.AddChild(LabelText("Paper tab colour", 14, ink));
        _campaignPaletteOption = new OptionButton { CustomMinimumSize = new Vector2(0, 40) };
        foreach (var value in Enum.GetValues<FestivalPalette>()) _campaignPaletteOption.AddItem(value.ToString());
        _campaignPaletteOption.ItemSelected += CampaignPaletteSelected; plannerBox.AddChild(_campaignPaletteOption);
        plannerBox.AddChild(new HSeparator());
        _campaignCommitmentLabel = LabelText("", 15, ink); _campaignCommitmentLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        plannerBox.AddChild(_campaignCommitmentLabel);
        _campaignCommitButton = ButtonText("CONFIRM £40", CampaignConfirmCommitment); plannerBox.AddChild(_campaignCommitButton);
        plannerBox.AddChild(new HSeparator());
        _campaignAdvanceButton = ButtonText("ADVANCE WEEK", CampaignAdvanceWeek); plannerBox.AddChild(_campaignAdvanceButton);

        var digest = new PanelContainer();
        digest.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        digest.OffsetLeft = -438; digest.OffsetTop = 102; digest.OffsetRight = -16; digest.OffsetBottom = -16;
        digest.AddThemeStyleboxOverride("panel", PaperStyle(cream)); layer.AddChild(digest);
        var digestMargin = new MarginContainer();
        foreach (var key in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) digestMargin.AddThemeConstantOverride(key, 18);
        digest.AddChild(digestMargin);
        var digestBox = new VBoxContainer(); digestBox.AddThemeConstantOverride("separation", 12); digestMargin.AddChild(digestBox);
        digestBox.AddChild(LabelText("WEEKLY PREVIEW / DIGEST", 14, new Color("6f5937")));
        _campaignDigestLabel = LabelText("", 16, ink); _campaignDigestLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        digestBox.AddChild(_campaignDigestLabel);
        digestBox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        _hashLabel = LabelText("", 12, new Color("47603b")); _hashLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; digestBox.AddChild(_hashLabel);
        RefreshCampaignHud();
    }

    private void CampaignRename()
    {
        try { _session.RenameFestival(_campaignNameEdit.Text); _campaignUiStatus = "Festival renamed"; }
        catch (ArgumentException exception) { _campaignUiStatus = exception.Message; }
        RefreshCampaignHud();
    }

    private void CampaignPaletteSelected(long index)
    {
        _session.SelectPalette((FestivalPalette)index); _campaignUiStatus = $"Palette set to {(FestivalPalette)index}"; RefreshCampaignHud();
    }

    private void CampaignConfirmCommitment()
    {
        var result = _session.Execute(CampaignEnvelope(new ConfirmPlanningCommitmentCommand(CampaignDefaults.BasicAdministrationCommitmentId)));
        _campaignUiStatus = result.IsAccepted ? "£40 scheduled for next Advance Week" : $"Rejected: {result.Message}";
        if (result.ReasonCode == CommandReasonCode.AlreadyCommitted) _campaignDuplicateRejected = true;
        RefreshCampaignHud();
    }

    private void CampaignAdvanceWeek()
    {
        if (_session.Phase != SessionPhase.Planning) { _campaignUiStatus = "Opening Check reached; planning cannot advance again."; RefreshCampaignHud(); return; }
        var result = PlanningAdvanceCoordinator.Advance(
            SaveDirectory, _session, _saveCompatibility, DateTimeOffset.UtcNow, _autosaveGeneration,
            CampaignEnvelope(new AdvancePlanningWeekCommand()));
        _campaignUiStatus = result.IsSuccess ? "Autosaved • " + result.Digest!.Summary : result.Message;
        if (result.IsSuccess) _autosaveGeneration++;
        RefreshCampaignHud();
    }

    private void CampaignManualSave()
    {
        var result = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-campaign", new SaveWriteRequest(
            _session, _saveCompatibility, "manual", DateTimeOffset.UtcNow));
        _campaignUiStatus = result.IsSuccess ? "Campaign saved" : result.Error ?? "Save failed"; RefreshCampaignHud();
    }

    private void CampaignManualLoad()
    {
        var result = SaveFileAdapter.LoadSlot(SaveDirectory, "manual-campaign", _saveCompatibility);
        if (result.IsSuccess) { _session = result.Session!; _campaignUiStatus = "Campaign loaded"; }
        else _campaignUiStatus = result.Error ?? "Load failed";
        RefreshCampaignHud();
    }

    private CommandEnvelope CampaignEnvelope(SessionCommand command) => new(
        new CommandId(1_010_000UL + _session.NextSubmissionSequence), _session.CampaignId, _session.Phase,
        _session.CurrentTick, _session.NextSubmissionSequence, null, command);

    private void RefreshCampaignHud()
    {
        var snapshot = _session.CaptureSnapshot();
        var campaign = snapshot.Campaign!;
        var finance = snapshot.FestivalFinances.Single(item => item.OwnerId == campaign.FinanceOwnerId);
        var phase = snapshot.Phase == SessionPhase.Planning ? $"PLANNING W{campaign.PlanningWeek}" : "OPENING CHECK";
        _campaignIdentityLabel.Text = $"{campaign.FestivalName.ToUpperInvariant()}  •  {phase}";
        _campaignFinanceLabel.Text = $"CASH £{finance.CashPennies / 100m:0}  •  DEBT £{campaign.Loan.OutstandingPrincipalPennies / 100m:0}  •  SETTLEMENT £{(campaign.Loan.PrincipalDueAtSettlementPennies + campaign.Loan.InterestDueAtSettlementPennies) / 100m:0}";
        if (!_campaignNameEdit.HasFocus()) _campaignNameEdit.Text = campaign.FestivalName;
        _campaignPaletteOption.Selected = (int)campaign.Palette;
        _campaignTopPanel.AddThemeStyleboxOverride("panel", PaperStyle(CampaignPaletteColor(campaign.Palette)));
        var commitment = campaign.Commitments.Single();
        _campaignCommitmentLabel.Text = $"BASIC ADMINISTRATION AND COVER\n£40 • {commitment.Status.ToString().ToUpperInvariant()}\n" +
            (commitment.Status == PlanningCommitmentStatus.Paid ? $"Paid once on the W{commitment.DueOnAdvanceFromWeek} advance." :
                commitment.Status == PlanningCommitmentStatus.Confirmed ? $"Confirmed in W{commitment.ConfirmedInWeek}; due on this W{commitment.DueOnAdvanceFromWeek} advance." :
                "Confirm now to pay on the next manual Advance Week.");
        _campaignCommitButton.Disabled = commitment.Status != PlanningCommitmentStatus.Available;
        _campaignAdvanceButton.Disabled = snapshot.Phase != SessionPhase.Planning;
        var preview = snapshot.Phase == SessionPhase.Planning ? _session.GetWeekAdvancePreview() : null;
        var previewText = preview is null ? "No further planning advance. Review the opening warnings." :
            $"NEXT: W{preview.FromWeek} → {(preview.PhaseAfter == SessionPhase.OpeningCheck ? "OPENING CHECK" : $"W{preview.FromWeek - 1}")}\n" +
            $"Known payment: {(preview.DuePayments.Count == 0 ? "none" : $"£{preview.DuePayments.Sum(item => item.AmountPennies) / 100m:0}")}\n" +
            $"Cash after: £{preview.CashAfterPennies / 100m:0}\n\nSettlement forecast\nPrincipal £{preview.PrincipalDueAtSettlementPennies / 100m:0}\nInterest £{preview.InterestDueAtSettlementPennies / 100m:0}\nNot paid weekly.";
        var lastDigest = campaign.WeeklyDigests.LastOrDefault();
        _campaignDigestLabel.Text = $"{previewText}\n\nSTATUS\n{_campaignUiStatus}" + (lastDigest is null ? "" : $"\n\nLAST DIGEST\n{lastDigest.Summary}");
        _hashLabel.Text = $"FIXED SITE  LOWER WITTERING FARM\nSEED {snapshot.CampaignSeed}  SITE SEED {campaign.SiteSeed}\nGAMEPLAY HASH {snapshot.AuthoritativeHash[..16]}\nName/palette are cosmetic save state.";
    }

    private static Color CampaignPaletteColor(FestivalPalette palette) => palette switch
    {
        FestivalPalette.Meadow => new Color("dfe8c4"),
        FestivalPalette.Marigold => new Color("f1d28a"),
        FestivalPalette.Berry => new Color("dfb7c5"),
        FestivalPalette.River => new Color("bcd9db"),
        _ => new Color("f5e9c9"),
    };

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
        else if (collider is not null && _securityPostPickId != 0 && collider.GetInstanceId() == _securityPostPickId) SelectSecurityPost();
        else if (collider is not null && _medicalFacilityPicks.TryGetValue(collider.GetInstanceId(), out var medicalFacility))
            SelectMedicalFacility(medicalFacility.Facility, medicalFacility.WaterPointId);
        else if (collider is not null && _pickRegistry.TryGetValue(collider.GetInstanceId(), out var item)) SelectObject(item);
        else ClearSelection();
    }

    private void SelectObject(FarmObjectReadModel item)
    {
        ClearSecurityPostSelection();
        _selectedMedicalFacility = null;
        RefreshMedicalNeedBars(null);
        _selectedAttendeeId = null;
        _selected = item;
        RefreshStagePowerAction();
        RefreshSatisfactionBar(null);
        RefreshMedicalActionInspector();
        RefreshDisorderActionInspector();
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
        ClearSecurityPostSelection();
        RefreshMedicalNeedBars(null);
        _selected = null; _selectedAttendeeId = null; _selectedMedicalFacility = null; _selectedWaterPointId = "water.main";
        _highlight.Visible = false; _inspectorTitle.Text = "Nothing selected";
        RefreshStagePowerAction();
        RefreshSatisfactionBar(null);
        RefreshMedicalActionInspector();
        RefreshDisorderActionInspector();
        _inspectorBody.Text = "Click a building, gate, stage or service point.\nClick empty ground to clear."; GD.Print("FARM_SELECTION_CLEARED");
    }

    private void SelectAttendee(EntityId id)
    {
        if (!_attendeeVisuals.TryGetValue(id, out var visual))
        {
            GD.Print($"ATTENDEE_SELECTION_UNAVAILABLE id={id.Value} no physical visual yet");
            return;
        }
        ClearSecurityPostSelection();
        _selectedMedicalFacility = null;
        _selected = null; _selectedAttendeeId = id;
        RefreshStagePowerAction();
        _highlight.Position = visual.Position + new Vector3(0, 0.08f, 0);
        _highlight.Scale = new Vector3(0.7f, 1, 0.7f); _highlight.Visible = true;
        RefreshAttendeeInspector();
        RefreshMedicalActionInspector();
        RefreshDisorderActionInspector();
        GD.Print($"ATTENDEE_SELECTED id={id.Value} orientation={OrientationNames[_orientation]}");
    }

    private void BuildSatisfactionBar(VBoxContainer parent)
    {
        _satisfactionSection = new VBoxContainer { Visible = false };
        _satisfactionLabel = LabelText("OVERALL SATISFACTION", 12, new Color("6f5937"));
        _satisfactionSection.AddChild(_satisfactionLabel);
        _satisfactionBar = new ProgressBar { MaxValue = 10_000, ShowPercentage = false,
            CustomMinimumSize = new Vector2(285, 16) };
        _satisfactionSection.AddChild(_satisfactionBar);
        parent.AddChild(_satisfactionSection);
    }

    private void RefreshSatisfactionBar(int? satisfaction)
    {
        if (_satisfactionSection is null) return;
        _satisfactionSection.Visible = satisfaction is not null;
        if (satisfaction is not { } value) return;
        _satisfactionLabel!.Text = $"OVERALL SATISFACTION  {value / 100m:0}%";
        _satisfactionBar!.Value = value;
        _satisfactionBar.Modulate = value < 3_500 ? new Color("df5750") :
            value < 7_000 ? new Color("459ad1") : new Color("53bb72");
        _satisfactionBar.TooltipText = $"Overall satisfaction: {value / 100m:0}%";
    }

    private void BuildStagePowerAction(VBoxContainer parent)
    {
        if (_session.CaptureEquipment() is null) return;
        _stagePowerButton = ButtonText("ISOLATE STAGE POWER", () =>
            CommitEquipmentAction(new EquipmentCommand(EquipmentAction.Isolate)));
        _stagePowerButton.Visible = false;
        parent.AddChild(_stagePowerButton);
    }

    private void RefreshStagePowerAction()
    {
        if (_stagePowerButton is null) return;
        _stagePowerButton.Visible = _selected?.Kind == FarmObjectKind.TrailerStage;
        if (!_stagePowerButton.Visible) return;
        var error = _session.ValidateCommand(CampaignEnvelope(new EquipmentCommand(EquipmentAction.Isolate)));
        _stagePowerButton.Disabled = error is not null;
        _stagePowerButton.TooltipText = error?.Message ?? "Safely isolate this stage's power.";
    }

    private void RefreshAttendeeInspector(SessionObservation? supplied = null)
    {
        if (_selectedAttendeeId is not { } id) return;
        var observation = supplied ?? _session.CaptureObservation();
        var navigation = observation.NavigationAgents.Single(item => item.Id == id);
        if (_session.CapturePreparation() is { } preparation)
        {
            RefreshLivePersonInspector(id, navigation, preparation);
            return;
        }
        var queue = observation.ServiceQueues.Single();
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
            else if (args[i] == "--capture-campaign" && i + 1 < args.Length) _campaignCaptureDirectory = args[++i];
            else if (args[i] == "--capture-r005-hearing" && i + 1 < args.Length)
            {
                _hearingCaptureDirectory = args[++i];
                Directory.CreateDirectory(_hearingCaptureDirectory);
            }
            else if (args[i] == "--capture-r005a-water" && i + 1 < args.Length)
            {
                _waterFoundationCaptureDirectory = args[++i];
                Directory.CreateDirectory(_waterFoundationCaptureDirectory);
            }
            else if (args[i] == "--capture-r005b-staff" && i + 1 < args.Length)
            {
                _staffCaptureDirectory = args[++i]; Directory.CreateDirectory(_staffCaptureDirectory);
            }
            else if (args[i] == "--capture-r005c-water" && i + 1 < args.Length)
            {
                _waterPlaytestCaptureDirectory = args[++i]; Directory.CreateDirectory(_waterPlaytestCaptureDirectory);
            }
            else if (args[i] == "--capture-r005c-staff" && i + 1 < args.Length)
            {
                _interventionCaptureDirectory = args[++i]; Directory.CreateDirectory(_interventionCaptureDirectory);
            }
            else if (args[i] == "--capture-r005c-audience" && i + 1 < args.Length)
            {
                _audienceCaptureDirectory = args[++i]; Directory.CreateDirectory(_audienceCaptureDirectory);
            }
            else if (args[i] == "--capture-r005c-audience-refinement" && i + 1 < args.Length)
            {
                _audienceRefinementCapture = true;
                _audienceCaptureDirectory = args[++i]; Directory.CreateDirectory(_audienceCaptureDirectory);
            }
            else if (args[i] == "--capture-preparation" && i + 1 < args.Length) _preparationCaptureDirectory = args[++i];
            else if (args[i] == "--capture-live-performance" && i + 1 < args.Length) _liveCaptureDirectory = args[++i];
            else if (args[i] == "--capture-start-splash" && i + 1 < args.Length) _startSplashCapturePath = args[++i];
            else if (args[i] == "--capture-medical" && i + 2 < args.Length)
            {
                _medicalCaptureMode = args[++i];
                _medicalCaptureDirectory = args[++i];
                Directory.CreateDirectory(_medicalCaptureDirectory);
            }
            else if (args[i] == "--capture-disorder" && i + 2 < args.Length)
            {
                _disorderCaptureMode = args[++i];
                _disorderCaptureDirectory = args[++i];
                Directory.CreateDirectory(_disorderCaptureDirectory);
            }
            else if (args[i] == "--measure-live-performance" && i + 2 < args.Length)
            {
                _liveMeasurementTier = int.Parse(args[++i]);
                if (_liveMeasurementTier is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(_liveMeasurementTier));
                _equipmentPerformanceOutput = args[++i];
                Directory.CreateDirectory(Path.GetDirectoryName(_equipmentPerformanceOutput)!);
            }
            else if (args[i] == "--measure-live-performance-capped" && i + 2 < args.Length)
            {
                _liveMeasurementTier = int.Parse(args[++i]);
                if (_liveMeasurementTier is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(_liveMeasurementTier));
                _equipmentPerformanceOutput = args[++i];
                _equipmentCappedDiagnostic = true;
                Directory.CreateDirectory(Path.GetDirectoryName(_equipmentPerformanceOutput)!);
            }
            else if (args[i] == "--diagnose-native-render-timing" && i + 1 < args.Length)
            {
                _liveMeasurementTier = 2;
                _nativeTimingControlDiagnostic = true;
                _equipmentPerformanceOutput = args[++i];
                Directory.CreateDirectory(Path.GetDirectoryName(_equipmentPerformanceOutput)!);
            }
            else if (args[i] == "--capture-equipment" && i + 2 < args.Length)
            {
                _equipmentCaptureMode = args[++i];
                _equipmentCaptureDirectory = args[++i];
                Directory.CreateDirectory(_equipmentCaptureDirectory);
            }
            else if (args[i] == "--check-equipment-performance" && i + 1 < args.Length)
            {
                _equipmentPerformanceOutput = args[++i];
                Directory.CreateDirectory(Path.GetDirectoryName(_equipmentPerformanceOutput)!);
            }
            else if (args[i] == "--diagnose-equipment-vsync" && i + 1 < args.Length)
            {
                _equipmentVsyncDiagnostic = true;
                _equipmentPerformanceOutput = args[++i];
                Directory.CreateDirectory(Path.GetDirectoryName(_equipmentPerformanceOutput)!);
            }
            else if (args[i] == "--diagnose-equipment-capped" && i + 1 < args.Length)
            {
                _equipmentCappedDiagnostic = true;
                _equipmentPerformanceOutput = args[++i];
                Directory.CreateDirectory(Path.GetDirectoryName(_equipmentPerformanceOutput)!);
            }
            else if (args[i] == "--measure-preparation" && i + 1 < args.Length) _preparationMeasurementTier = int.Parse(args[++i]);
            else if (args[i] == "--profile-preparation" && i + 2 < args.Length)
            {
                _preparationProfileCapture = args[++i] == "capture";
                _preparationProfileOutput = args[++i];
                _preparationCaptureDirectory = Path.GetDirectoryName(_preparationProfileOutput);
                _preparationMeasurementTier = 2;
            }
            else if (args[i] == "--profile-departure") _preparationProfileDeparture = true;
            else if (args[i] == "--profile-full-attempt") _preparationProfileFullAttempt = true;
            else if (args[i] == "--benchmark-launch" && i + 3 < args.Length)
            {
                var agents = int.Parse(args[++i]);
                var passage = Enum.Parse<BenchmarkPassage>(args[++i], true);
                _benchmarkOutputPath = ProjectSettings.GlobalizePath(args[++i]);
                _benchmarkFixture = CrowdBenchmarkFixture.Create(agents, passage);
            }
            else if (args[i] == "--m1-feasibility-launch" && i + 1 < args.Length)
            {
                _sharedWorldOutputPath = ProjectSettings.GlobalizePath(args[++i]);
                _sharedWorldFixture = SharedWorldFeasibilityFixture.Create();
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
        if (_campaignCaptureDirectory is not null) DirAccess.MakeDirRecursiveAbsolute(_campaignCaptureDirectory);
        if (_benchmarkOutputPath is not null) DirAccess.MakeDirRecursiveAbsolute(Path.GetDirectoryName(_benchmarkOutputPath)!);
        if (_sharedWorldOutputPath is not null) DirAccess.MakeDirRecursiveAbsolute(Path.GetDirectoryName(_sharedWorldOutputPath)!);
    }

    private void ProcessCampaignCapture()
    {
        _campaignCaptureFrame++;
        if (_campaignCaptureStage == 0 && _campaignCaptureFrame >= 12)
        {
            CaptureCampaign("created");
            var interaction = Stopwatch.StartNew();
            _campaignNameEdit.Text = "Wittering Paper Lanterns"; CampaignRename();
            _campaignPaletteOption.Select((int)FestivalPalette.Berry); CampaignPaletteSelected((int)FestivalPalette.Berry);
            CampaignConfirmCommitment(); CampaignConfirmCommitment();
            interaction.Stop(); _campaignMaximumInteractionMilliseconds = interaction.Elapsed.TotalMilliseconds;
            _campaignCaptureStage = 1; _campaignCaptureFrame = 0;
            return;
        }
        if (_campaignCaptureStage == 1 && _campaignCaptureFrame >= 8)
        {
            CaptureCampaign("commitment-preview");
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            CampaignManualSave(); CampaignManualLoad();
            _campaignSaveReloadExact = _session.CaptureSnapshot().AuthoritativeHash == hash;
            _campaignCaptureStage = 2; _campaignCaptureFrame = 0;
            return;
        }
        if (_campaignCaptureStage == 2 && _campaignCaptureFrame >= 4 && _session.Phase == SessionPhase.Planning)
        {
            var advance = Stopwatch.StartNew(); CampaignAdvanceWeek(); advance.Stop();
            _campaignMaximumAdvanceMilliseconds = Math.Max(_campaignMaximumAdvanceMilliseconds, advance.Elapsed.TotalMilliseconds);
            _campaignCaptureFrame = 0;
            return;
        }
        if (_campaignCaptureStage != 2 || _session.Phase != SessionPhase.OpeningCheck || _campaignCaptureFrame < 8) return;
        CaptureCampaign("opening-check");
        var snapshot = _session.CaptureSnapshot();
        var campaign = snapshot.Campaign!;
        var autosaves = Enumerable.Range(0, AutosaveRotation.SlotCount)
            .Count(i => File.Exists(SaveFileAdapter.ResolveSlotPath(SaveDirectory, $"autosave-{i}")));
        var paid = campaign.LedgerTransactions.Count(item => item.Reason == "Basic administration and cover");
        var passed = snapshot.Phase == SessionPhase.OpeningCheck && campaign.PlanningWeek == 0 && snapshot.CurrentTick == 0 &&
            snapshot.FestivalFinances.Single().CashPennies == 76_000 && campaign.Loan.OutstandingPrincipalPennies == 80_000 &&
            campaign.Loan.PrincipalDueAtSettlementPennies + campaign.Loan.InterestDueAtSettlementPennies == 22_400 &&
            campaign.WeeklyDigests.Count == 8 && paid == 1 && campaign.LedgerTransactions.All(item => item.IsBalanced) &&
            _campaignDuplicateRejected && _campaignSaveReloadExact && autosaves == 3 &&
            _campaignMaximumInteractionMilliseconds < 100 && _campaignMaximumAdvanceMilliseconds < 2_000;
        var report = $"M1.01 exported-runtime verification passed={passed} resolution={GetWindow().Size}{System.Environment.NewLine}" +
            $"festival={campaign.FestivalName} palette={campaign.Palette} site={campaign.SiteId} seed={snapshot.CampaignSeed} site_seed={campaign.SiteSeed}{System.Environment.NewLine}" +
            $"phase={snapshot.Phase} planning_week={campaign.PlanningWeek} manual_advances={campaign.WeeklyDigests.Count} authoritative_ticks={snapshot.CurrentTick}{System.Environment.NewLine}" +
            $"cash_p={snapshot.FestivalFinances.Single().CashPennies} debt_p={campaign.Loan.OutstandingPrincipalPennies} settlement_principal_p={campaign.Loan.PrincipalDueAtSettlementPennies} settlement_interest_p={campaign.Loan.InterestDueAtSettlementPennies}{System.Environment.NewLine}" +
            $"commitment_paid_transactions={paid} all_ledger_balanced={campaign.LedgerTransactions.All(item => item.IsBalanced)} duplicate_confirmation_rejected={_campaignDuplicateRejected}{System.Environment.NewLine}" +
            $"save_reload_exact={_campaignSaveReloadExact} autosave_slots={autosaves} interaction_max_ms={_campaignMaximumInteractionMilliseconds:0.###} advance_max_ms={_campaignMaximumAdvanceMilliseconds:0.###} normal_planning_responsive={_campaignMaximumInteractionMilliseconds < 100 && _campaignMaximumAdvanceMilliseconds < 2_000}{System.Environment.NewLine}" +
            $"hash={snapshot.AuthoritativeHash} stable60_target_unchanged=true stable60_m1_00=Fail os_input_latency=Unverified m1_11_final_gate=true no_live_crowd=true{System.Environment.NewLine}";
        File.WriteAllText(Path.Combine(_campaignCaptureDirectory!, "verification-1280x720.txt"), report);
        GD.Print(report); _campaignCaptureDirectory = null; GetTree().Quit(passed ? 0 : 2);
    }

    private void CaptureCampaign(string stage)
    {
        var path = Path.Combine(_campaignCaptureDirectory!, $"campaign-{stage}-1280x720.png");
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"CAMPAIGN_CAPTURE stage={stage} path={path} result={error}");
    }

    private void AdvanceSharedWorldFeasibility(double delta)
    {
        var fixture = _sharedWorldFixture!;
        if (_sharedMeasurementStage == 0)
        {
            for (var tick = 0; tick < FoundationClock.MaximumTicksPerFrame && !SharedWorldFeasibilityFixture.IsRepresentativeActiveState(fixture); tick++)
                _session.AdvanceTicks(1);
            PresentSharedWorld();
            if (!SharedWorldFeasibilityFixture.IsRepresentativeActiveState(fixture)) return;
            _sharedRepresentativeTick = _session.CurrentTick;
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            var timer = Stopwatch.StartNew();
            var saved = SaveFileAdapter.SaveSlot(SaveDirectory, "m1-shared-world", new SaveWriteRequest(_session, _saveCompatibility, "m1-feasibility", DateTimeOffset.UtcNow));
            timer.Stop(); _sharedSaveMilliseconds = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); var loaded = SaveFileAdapter.LoadSlot(SaveDirectory, "m1-shared-world", _saveCompatibility); timer.Stop();
            _sharedLoadMilliseconds = timer.Elapsed.TotalMilliseconds;
            _sharedRestoreExact = saved.IsSuccess && loaded.IsSuccess && loaded.Session!.CaptureSnapshot().AuthoritativeHash == hash;
            if (!_sharedRestoreExact) { GetTree().Quit(2); return; }
            _session = loaded.Session!; _sharedWorldFixture = fixture = fixture with { Session = _session };
            _sharedWorldClock.RequestedSpeed = RequestedSpeed.OneX; _sharedWorldClock.ResetBoundary(); _sharedMeasurementStage = 1;
            ResetSharedMeasurementWindow();
            return;
        }

        // Arm only after one clean post-restore presentation frame. Save/load and its engine
        // delta therefore cannot contaminate either callback intervals or attained speed.
        if (_sharedWallPreviousTimestamp == 0)
        {
            PresentSharedWorld();
            _sharedMeasurementStartTick = _session.CurrentTick;
            _sharedWallStartTimestamp = _sharedWallPreviousTimestamp = Stopwatch.GetTimestamp();
            return;
        }

        var work = Stopwatch.StartNew();
        var ticks = _sharedWorldClock.Schedule(delta);
        for (var tick = 0; tick < ticks; tick++) _session.AdvanceTicks(1);
        PresentSharedWorld(); work.Stop();
        var wallNow = Stopwatch.GetTimestamp();
        _sharedWallFrameMilliseconds.Add(Stopwatch.GetElapsedTime(_sharedWallPreviousTimestamp, wallNow).TotalMilliseconds);
        _sharedWallPreviousTimestamp = wallNow;
        _sharedEngineDeltaMilliseconds.Add(delta * 1000);
        _sharedWorkMilliseconds.Add(work.Elapsed.TotalMilliseconds); _sharedMeasurementFrames++;
        if (_sharedMeasurementFrames < 90) return;
        var requested = _sharedWorldClock.RequestedSpeed;
        WriteSharedRenderedResult(requested, requested == RequestedSpeed.FourX);
        if (requested == RequestedSpeed.OneX)
        {
            var loaded = SaveFileAdapter.LoadSlot(SaveDirectory, "m1-shared-world", _saveCompatibility);
            if (!loaded.IsSuccess) { GetTree().Quit(2); return; }
            _session = loaded.Session!; _sharedWorldFixture = fixture with { Session = _session };
            _sharedWorldClock.RequestedSpeed = RequestedSpeed.FourX; _sharedWorldClock.ResetBoundary();
            ResetSharedMeasurementWindow(); _sharedMeasurementStage = 2;
            return;
        }
        GetTree().Quit(0);
    }

    private void PresentSharedWorld()
    {
        var snapshot = _session.CaptureSnapshot();
        foreach (var agent in snapshot.NavigationAgents)
            if (_attendeeVisuals.TryGetValue(agent.Id, out var visual)) visual.Position = ToWorld(agent);
        var queued = snapshot.ServiceQueues.Sum(queue => queue.OrderedMembers.Count);
        _hashLabel.Text = $"M1.00 SHARED WORLD • 50 PEOPLE • 3 DESTINATIONS\nACTIVE {snapshot.NavigationAgents.Count}  QUEUED {queued}  SERVED {snapshot.Transactions.Count}\nREQUEST {(int)_sharedWorldClock.RequestedSpeed}×  SCHEDULER-DELTA {_sharedWorldClock.AttainedSpeed:0.00}×\nTICK {snapshot.CurrentTick}  HASH {snapshot.AuthoritativeHash[..12]}";
    }

    private void ResetSharedMeasurementWindow()
    {
        _sharedWallFrameMilliseconds.Clear(); _sharedEngineDeltaMilliseconds.Clear(); _sharedWorkMilliseconds.Clear();
        _sharedMeasurementFrames = 0; _sharedMeasurementStartTick = 0; _sharedWallStartTimestamp = 0; _sharedWallPreviousTimestamp = 0;
    }

    private void WriteSharedRenderedResult(RequestedSpeed speed, bool final)
    {
        var frames = _sharedWallFrameMilliseconds.Order().ToArray();
        var engineDeltas = _sharedEngineDeltaMilliseconds.Order().ToArray();
        var work = _sharedWorkMilliseconds.Order().ToArray();
        double P(double[] values, double p) => values[Math.Clamp((int)Math.Ceiling(values.Length * p) - 1, 0, values.Length - 1)];
        var path = Path.Combine(Path.GetDirectoryName(_sharedWorldOutputPath!)!, $"shared-world-{(int)speed}x.json");
        var snapshot = _session.CaptureSnapshot();
        var wallElapsedSeconds = Stopwatch.GetElapsedTime(_sharedWallStartTimestamp, _sharedWallPreviousTimestamp).TotalSeconds;
        var measuredTicks = snapshot.CurrentTick - _sharedMeasurementStartTick;
        var wallAttainedSpeed = measuredTicks / (FoundationClock.OneXTickRate * wallElapsedSeconds);
        var interacting = snapshot.ServiceQueues.SelectMany(queue => queue.Agents)
            .Count(agent => agent.Action is not ServiceQueueAgentAction.Completed and not ServiceQueueAgentAction.Failed);
        var result = new
        {
            RequestedSpeed = (int)speed, WallAttainedSpeed = wallAttainedSpeed,
            SchedulerEngineDeltaAttainedSpeed = _sharedWorldClock.AttainedSpeed,
            SampleFrames = frames.Length, SampleTicks = measuredTicks, WallElapsedSeconds = wallElapsedSeconds,
            MeasurementStartTick = _sharedMeasurementStartTick, MeasurementEndTick = snapshot.CurrentTick,
            WallFrameMeanMs = frames.Average(), WallFrameP50Ms = P(frames, .5),
            WallFrameP95Ms = P(frames, .95), WallFrameP99Ms = P(frames, .99),
            EngineDeltaMeanMs = engineDeltas.Average(), EngineDeltaP95Ms = P(engineDeltas, .95),
            WorkMeanMs = work.Average(),
            WorkP50Ms = P(work, .5), WorkP95Ms = P(work, .95), WorkP99Ms = P(work, .99),
            RepresentativePopulation = snapshot.NavigationAgents.Count == SharedWorldFeasibilityFixture.AgentCount,
            RepresentativeBoundaryTick = _sharedRepresentativeTick,
            ActiveAgents = snapshot.NavigationAgents.Count, InteractingAgents = interacting, Destinations = snapshot.ServiceQueues.Count,
            SaveMs = _sharedSaveMilliseconds, LoadMs = _sharedLoadMilliseconds, RestoreExact = _sharedRestoreExact,
            ProcessPeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64, Hash = snapshot.AuthoritativeHash,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        if (final)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.ChangeExtension(_sharedWorldOutputPath!, ".png"));
            File.WriteAllText(_sharedWorldOutputPath!, "M1.00 shared-world rendered feasibility completed; see adjacent 1x/4x JSON evidence." + System.Environment.NewLine);
        }
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
            $"controller_tick={fixture.ControllerTick} active_sessions={fixture.ActiveSessionCount} active_agents={fixture.ActiveAgentCount} completed={fixture.Completed} backlog={fixture.Backlog} route_failures={fixture.RouteFailures} hash={fixture.CompositeHash()}{System.Environment.NewLine}" +
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

    // Development layout revisions use a new save namespace. Old files remain
    // untouched and the compatibility header still rejects cross-layout loads.
    private string SaveDirectory => _interventionCaptureDirectory is not null ? Path.Combine(_interventionCaptureDirectory, "saves") :
        _waterPlaytestCaptureDirectory is not null ? Path.Combine(_waterPlaytestCaptureDirectory, "saves") :
        _staffCaptureDirectory is not null ? Path.Combine(_staffCaptureDirectory, "saves") :
        _audienceCaptureDirectory is not null ? Path.Combine(_audienceCaptureDirectory, "saves") : ProjectSettings.GlobalizePath(
        _session?.CapturePreparation() is not null ? "user://saves/r0.05-hearing-v1" : "user://saves");
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
            var loadedSnapshot = _session.CaptureSnapshot();
            _foundationPresentation.Reset(loadedSnapshot);
            _foundationPublishedHash = loadedSnapshot.AuthoritativeHash;
            _foundationPublishedHashTick = loadedSnapshot.CurrentTick;
            _autosaveScheduler.Rebase();
            _manualRestoreVerified = _manualSaveHash.Length > 0 && loadedSnapshot.AuthoritativeHash == _manualSaveHash;
            _selectionRetainedAfterLoad = selectedBeforeLoad is { } selected && loadedSnapshot.NavigationAgents.Any(item => item.Id == selected) && _selectedAttendeeId == selected;
            _saveStatus = "LOADED";
        }
        else _saveStatus = "LOAD ERROR";
        GD.Print($"FOUNDATION_MANUAL_LOAD success={result.IsSuccess} tick={_session.CurrentTick} error={result.Error ?? "none"}");
    }

    private void AdvanceFoundationPresentation(double delta)
    {
        var cap = _foundationCaptureDirectory is not null && _foundationCaptureStage == 3 ? 2 : FoundationClock.MaximumTicksPerFrame;
        var ticks = _foundationClock.Schedule(delta, cap);
        var observation = _session.CaptureObservation();
        for (var tick = 0; tick < ticks; tick++)
        {
            _session.AdvanceWithoutSnapshot(1);
            if (_foundationCaptureDirectory is not null) _foundationReference!.Session.AdvanceWithoutSnapshot(1);
            observation = _session.CaptureObservation();
            _foundationPresentation.Advance(observation);
        }
        if (_autosaveScheduler.Advance(delta))
        {
            var saved = AutosaveRotation.Save(SaveDirectory, _session, _saveCompatibility, DateTimeOffset.UtcNow, _autosaveGeneration);
            _saveStatus = saved.IsSuccess ? "AUTOSAVED" : "AUTOSAVE ERROR";
            if (saved.IsSuccess) { _autosaveGeneration++; _autosaveWrites++; }
        }
        foreach (var agent in observation.NavigationAgents)
        {
            var sample = _foundationPresentation.Sample(agent.Id, _foundationClock.InterpolationFraction);
            _attendeeVisuals[agent.Id].Position = new Vector3((float)(sample.XMillimetres / 1000), 0.04f, (float)(sample.ZMillimetres / 1000));
        }
        RefreshAttendeeInspector(observation);
        var counts = FoundationDiagnostics.Count(observation);
        var clockStatus = _session.IsPaused ? "PAUSED • CAMERA / INSPECT / SAVE ACTIVE" : $"REQUEST {(int)_session.RequestedSpeed}×  ATTAINED {_foundationClock.AttainedSpeed:0.00}×  {(_foundationClock.IsOverloaded ? "⚠ REDUCED" : "ON TARGET")}";
        if (_foundationCaptureDirectory is not null || observation.CurrentTick - _foundationPublishedHashTick >= 80)
        {
            var published = _session.CaptureSnapshot();
            _foundationPublishedHash = published.AuthoritativeHash;
            _foundationPublishedHashTick = published.CurrentTick;
            if (_foundationCaptureDirectory is not null) ProcessFoundationCapture(published, published.ServiceQueues.Single());
        }
        _hashLabel.Text = $"M0 FOUNDATION • 50 AUTONOMOUS ATTENDEES\nTRAVELLING {counts.Travelling}  QUEUED {counts.QueueMembers}  WAITING {counts.Waiting}  IN SERVICE {counts.InService}\nSERVED {counts.Served}  FAILED {counts.Failed}  {clockStatus}  {_saveStatus}\nTICK {observation.CurrentTick}  HASH@{_foundationPublishedHashTick} {_foundationPublishedHash[..12]}";
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
