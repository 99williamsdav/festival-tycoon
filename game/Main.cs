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

public partial class Main : Node, IHudHost
{
    private readonly Dictionary<ulong, FarmObjectReadModel> _pickRegistry = [];
    private readonly Dictionary<ulong, EntityId> _attendeePickRegistry = [];
    private readonly Dictionary<string, Node3D> _visualRegistry = new(StringComparer.Ordinal);
    private FarmObjectReadModel? _selected;
    private EntityId? _selectedAttendeeId;
    private MeshInstance3D _highlight = null!;
    private Label _inspectorTitle = null!;
    private Label _inspectorBody = null!;
    /// <summary>A person's standout traits, just under their name; hidden for anything else.</summary>
    private Label? _inspectorTraits;
    private VBoxContainer? _satisfactionSection;
    private Label? _satisfactionLabel;
    private ProgressBar? _satisfactionBar;
    private Button? _stagePowerButton;
    // The host owns the session, its clock and its saves; views only read through it.
    private SessionHost _host = null!;
    private CameraRig _rig = null!;
    private CrowdBodies? _bodiesView;
    private CrowdBodies Bodies => _bodiesView ??= new(this, () => _session, PerformerPresentationRole, id => _performerInstruments.TryGetValue(id, out var kit) && !kit.HasMeta("NoArms"));
    private GameSession _session => _host.Session;
    private Node3D _gateLeafCollider = null!;
    private readonly Dictionary<EntityId, Node3D> _attendeeVisuals = [];
    private readonly FoundationPresentationInterpolator _foundationPresentation = new();
    /// <summary>The tier --start-tier opens on; never set in normal play.</summary>
    private int? _startTier;
    /// <summary>--pond-stage-trial: a campaign that runs the Pond Stage, at Tier 1 or the --start-tier; never set in normal play.</summary>
    private bool _pondStageTrialFlag;
    private SaveCompatibility _saveCompatibility => new("0.0.1-r0-build-v52", LowerWitteringFarmScenario.ContentCompatibilityHash, "r0-build-v52");

    public override void _Ready()
    {
        ConfigureCommandLine();
        _host = new SessionHost(_preparationProfileOutput is not null ? Festival.Simulation.Fixtures.BuildScaleFixture.Create(20260922, _profileGuests) :
            OS.GetCmdlineUserArgs().Length == 0 ? CreateFreshBuildCampaign(out _) :
            _startTier is not null || _pondStageTrialFlag ? GameSession.CreateDevelopmentFestival(20260922, _startTier ?? 1, _pondStageTrialFlag) :
            _litterEvidenceOutput is not null || _genreAudioVerificationOutput is not null ? GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established) : GameSession.CreateBuildCampaign(20260922), SaveDirectory, _saveCompatibility);
        BuildWorld();
        if (DisplayServer.GetName() != "headless")
            DisplayServer.SetIcon(GD.Load<Texture2D>("res://assets/branding/festival-tycoon-stage-sun-icon-v3.png").GetImage());
        if (_session.CaptureMedical() is not null) BuildMedicalWorld();
        SyncImmersionWorld();
        if (_session.CaptureDisorder() is not null) BuildDisorderWorld();
        if (_session.CaptureEquipment() is not null) EnsureStageDrumKit();
        if (_session.CaptureSnapshot().NavigationAgents.Count > 0) BuildAttendee();
        BuildHud();
        // Frame both east-of-track vendors with the stage rather than a stage-only close-up.
        if (_session.CaptureImmersion() is not null) _rig.Frame(new Vector3(4, 0, 8), 50);
        else if (_session.CaptureEquipment() is not null) _rig.Frame(new Vector3(-16, 0, 11), 32);
        else _rig.Frame(Vector3.Zero, 62);
        ResetFinanceFeedback();
        if (OS.GetCmdlineUserArgs().Length == 0 && _session.CaptureEquipment() is not null) BuildStartSplash();
        var version = Engine.GetVersionInfo()["string"].AsString();
        GD.Print($"FESTIVAL_TYCOON_LAUNCHED build={ToolchainSmoke.BuildVersion} godot={version}");
        GD.Print($"FARM_SCENE_READY scenario={LowerWitteringFarmScenario.ScenarioId} objects={_visualRegistry.Count} hash={_session.CaptureSnapshot().AuthoritativeHash}");
        if (OS.GetCmdlineUserArgs().Length == 0)
            GD.Print($"INITIAL_BUILD_CAMPAIGN id={_session.CampaignId.Value} seed={_session.CampaignSeed}");
        if (_genreAudioVerificationOutput is not null) VerifyGenreAudio();
    }

    public override void _Process(double delta)
    {
        if (_genreAudioVerificationOutput is not null) return;
        if (!EyeViewActive) _rig.Process(delta);
        ProcessEyeView(delta);
        ProcessDayCycle(delta);
        ProcessGround(delta);
        ProcessGateSign(delta);
        ProcessStageSet(delta);
        ProcessPondStage(delta);
        ProcessMoments(delta);
        ProcessFieldNotes(delta);
        ProcessGardenGate(delta);
        ProcessCows(delta);
        ProcessLavSucker(delta);
        ProcessMoods(delta);
        ProcessChatter(delta);
        ProcessGenerator(delta);
        ProcessVanSteam();
        ProcessLitterEvidence();
        ProcessCleanupEvidence();
        ProcessSurroundEvidence();
        AdvancePreparationPresentation(delta);
        if (_pondEvidenceOutput is null) ProcessPond(delta);
        ProcessPondEvidence();
        ProcessGrassEvidence();
        ProcessTreeEvidence();
        SeparateSpeech();
        FinalizeCleanupEvidenceFrame();
        RefreshContextPanelVisibility();
        AdvanceFinanceFeedback(delta);
        _urgentAlertDisplay.Advance(delta); RenderUrgentAlerts();
        if (_preparationProfileOutput is not null) { ProcessBuildProfileSetup(); FinishPreparationProfileFrame(); }
        UpdateHoverFeedback(GetViewport().GetMousePosition());
        ProcessBuildDebugOverlay();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (StartMenu.IsOpen) return;
        _rig.ObserveRelease(inputEvent);
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        // The field guide is modal: Esc closes it and nothing else reaches the field.
        if (_fieldGuide is not null)
        {
            if (inputEvent is InputEventKey { Pressed: true, Keycode: Key.Escape }) CloseFieldGuide();
            GetViewport().SetInputAsHandled(); return;
        }
        if (ResultsPaper.IsOpen || StartMenu.IsOpen) return;
        if (Perks.Panel?.Visible == true && inputEvent is InputEventMouseButton perkMouse && Perks.Panel.GetGlobalRect().HasPoint(perkMouse.Position))
        { GetViewport().SetInputAsHandled(); return; }
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (_buildGhostKind is not null && key.Keycode == Key.Escape) { CancelBuildPlacement(); RefreshHudWorkspace(); return; }
            if (_buildGhostKind is not null && key.Keycode is Key.Comma or Key.Period)
            { RotateBuildGhost(key.Keycode == Key.Comma ? -1 : 1); return; }
            if(key.Keycode==Key.Escape && Perks.ConfirmationPending){Perks.CancelConfirmation();return;}
            if (key.Keycode == Key.Escape && EyeViewActive) { ExitEyeView(); return; }
            if (key.Keycode == Key.Escape) { ClearSelection(); return; }
            if (EyeViewActive) { if (key.Keycode == Key.Space) { _host.Submit(new SetPausedCommand(!_session.IsPaused)); RefreshPreparationHud(); } return; }
            if (key.Keycode is Key.Key1 or Key.Key2 or Key.Key3 && _session.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing)
            { SetDaySpeed(key.Keycode == Key.Key1 ? RequestedSpeed.OneX : key.Keycode == Key.Key2 ? RequestedSpeed.TwoX : RequestedSpeed.FourX); return; }
            if (key.Keycode == Key.Q) _rig.Rotate(-1);
            else if (key.Keycode == Key.E) _rig.Rotate(1);
            else if (key.Keycode == Key.Space)
            {
                _host.Submit(new SetPausedCommand(!_session.IsPaused));
                RefreshPreparationHud();
            }
        }
        else if (inputEvent is InputEventMouseButton mouse)
        {
            if (WorldInputOccluded(mouse.Position)) return;
            if (_buildGhostKind is not null && mouse.Pressed && mouse.ButtonIndex == MouseButton.Right)
            { CancelBuildPlacement(); RefreshHudWorkspace(); return; }
            if (_buildGhostKind is not null && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            { CommitBuildPlacement(mouse.Position); return; }
            if (EyeViewActive) return;
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Right && OpenBuildContextMenu(mouse.Position)) return;
            if (_rig.HandleButton(mouse)) return;
            if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed) Pick(mouse.Position, mouse.DoubleClick);
        }
        else if (inputEvent is InputEventMouseMotion motion)
        {
            if (EyeViewActive) return;
            _rig.HandleMotion(motion);
            if (_buildGhostKind is not null) UpdateBuildGhost(motion.Position);
        }
        // Trackpads (a Mac has no middle button): two fingers drag the view, a pinch zooms it.
        else if (inputEvent is InputEventPanGesture pan && !EyeViewActive && !WorldInputOccluded(pan.Position)) _rig.HandlePanGesture(pan);
        else if (inputEvent is InputEventMagnifyGesture pinch && !EyeViewActive && !WorldInputOccluded(pinch.Position)) _rig.HandlePinch(pinch);
    }


    private void BuildWorld()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("8fc4dc"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("d9e7c2"),
            AmbientLightEnergy = 0.72f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-54, -32, 0), LightColor = new Color("fff1c5"),
            LightEnergy = 1.25f, ShadowEnabled = true,
        };
        AddChild(sun);
        BuildGrass();
        BuildFarmSurround();
        BuildTrack();
        BuildHedgeBoundary();
        BuildGateApron();
        BuildFarmBeauty();
        BuildDayCycle(environment, sun);
        foreach (var item in LowerWitteringFarmScenario.CreateReadModel().Objects)
            if (item.Kind != FarmObjectKind.ServicePoint) AddFarmObject(item);
        BuildBackstage();
        BuildPasture();
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
        BuildHoverFeedback();
        _rig = new CameraRig(this);
    }

    private void BuildAttendee()
    {
        var agents = _session.CaptureSnapshot().NavigationAgents;
        foreach (var agent in agents)
        {
            var performer = _session.CapturePreparation()?.People.SingleOrDefault(item => item.AgentId == agent.Id.Value);
            var visual = performer?.Role == ProtectedPersonRole.Guest
                ? Bodies.AddGuest(agent.Id, ToWorld(agent))
                : performer is { Role: ProtectedPersonRole.Staff or ProtectedPersonRole.Performer }
                    ? Bodies.AddRole(performer, ToWorld(agent))
                    : AddAsset("res://assets/characters/lwf_generic_attendee_v1.glb", ToWorld(agent));
            if (performer?.Role == ProtectedPersonRole.Guest && _session.GuestWaitingForRelease(agent.Id.Value))
                visual.Hide();
            {
                var pickBody = new StaticBody3D { CollisionLayer = 1, CollisionMask = 1 };
                pickBody.AddChild(new CollisionShape3D { Position = new Vector3(0, 0.85f, 0),
                    Shape = new CapsuleShape3D { Radius = 0.38f, Height = 1.7f } });
                visual.AddChild(pickBody);
                _attendeePickRegistry.Add(pickBody.GetInstanceId(), agent.Id);
                if (performer?.Role == ProtectedPersonRole.Guest && _session.GuestWaitingForRelease(agent.Id.Value))
                    pickBody.CollisionLayer = 0;
            }
            _attendeeVisuals.Add(agent.Id, visual);
        }
        foreach (var profile in _session.GetResponseStaff())
            if (_attendeeVisuals.TryGetValue(new EntityId(profile.AgentId), out var responderVisual))
                responderVisual.AddChild(WorldText.Speech(new Label3D { Text = profile.Name.Split(' ')[0].ToUpperInvariant(), Position = new Vector3(0, 2.1f, 0),
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled }, 28));
        ResetMedicalCuePresentation();
        ResetDisorderCuePresentation();
        _urgentAlertDisplay.Reset(); _urgentAlertActions.Clear(); _hudAlertKey = "uninitialized";
    }


    private void BuildGrass()
    {
        for (var x = -4; x < 4; x++)
        for (var z = -4; z < 4; z++)
        {
            // Imported bounds are local X 0..8, Z -8..0. The +8 Z origin offset
            // therefore covers world -32..32 instead of leaving the north edge bare.
            ApplyGroundMaterial(AddAsset("res://assets/environment/lwf_field_grass_tile_8m_v1.glb", new Vector3(x * 8, 0, (z + 1) * 8)));
        }
    }

    private void BuildTrack()
    {
        for (var z = -3; z <= 4; z++)
        {
            var track = AddAsset("res://assets/environment/lwf_vehicle_track_straight_8x4m_v1.glb", new Vector3(0, 0.052f, z * 8));
            track.RotationDegrees = new Vector3(0, 90, 0);
        }
    }

    private void BuildHedgeBoundary()
    {
        // Four 8 m hedgerow runs in turn, offset per side, so no stretch of the boundary repeats its neighbour.
        string Hedge(int run) => $"res://assets/environment/lwf_hedge_straight_8m_{"abcd"[run % 4]}_v1.glb";
        for (var i = -4; i < 4; i++)
        {
            RegisterBreezeHedge(AddAsset(Hedge(i + 4), new Vector3(i * 8, 0, -32)));
            if (i is not -1 and not 0) RegisterBreezeHedge(AddAsset(Hedge(i + 5), new Vector3(i * 8, 0, 32)));
            // The west run beside the farmhouse is cut for the bands' garden gate (Main.Backstage).
            if (i != 0) { var left = AddAsset(Hedge(i + 6), new Vector3(-32, 0, i * 8)); left.RotationDegrees = new Vector3(0, 90, 0); RegisterBreezeHedge(left); }
            // The east run beside the pasture is cut for the field gate (Main.Cows).
            if (i == 0) continue;
            var right = AddAsset(Hedge(i + 7), new Vector3(32, 0, i * 8)); right.RotationDegrees = new Vector3(0, 90, 0);
            RegisterBreezeHedge(right);
        }
        RegisterBreezeHedge(AddAsset("res://assets/environment/lwf_hedge_straight_4m_a_v1.glb", new Vector3(-8, 0, 32)));
        RegisterBreezeHedge(AddAsset("res://assets/environment/lwf_hedge_straight_4m_a_v1.glb", new Vector3(4, 0, 32)));
        AddAsset("res://assets/environment/lwf_hedge_gate_end_v1.glb", new Vector3(-3, 0, 32));
        var northRight = AddAsset("res://assets/environment/lwf_hedge_gate_end_v1.glb", new Vector3(3, 0, 32));
        northRight.RotationDegrees = new Vector3(0, 180, 0);
    }

    /// <summary>
    /// The odd tree and the farm pond: an oak in the west hedge, a field maple behind the small barn, an old apple by
    /// the farmhouse, and a pond in the south-east corner. Their trunks and the pond are blocked in the terrain.
    /// </summary>
    private void BuildFarmBeauty()
    {
        foreach (var (asset, at) in new[]
                 {
                     ("lwf_tree_oak_v2", new Vector3(-32, 0, 9)), ("lwf_tree_field_maple_v1", new Vector3(29.5f, 0, -29.5f)),
                     ("lwf_tree_old_apple_v2", new Vector3(-29.2f, 0, -6.6f)),
                 })
        {
            var tree = AddAsset($"res://assets/environment/{asset}.glb", at);
            RegisterTreeEvidence(tree, asset, at);
            if (tree.FindChild("Crown", true, false) is Node3D crown) RegisterBreezeHedge(crown, .2f);
        }
        BuildPondWorld();
    }

    private void AddFarmObject(FarmObjectReadModel item)
    {
        var (path, size) = item.Kind switch
        {
            FarmObjectKind.Farmhouse => ("res://assets/environment/lwf_farmhouse_v1.glb", new Vector3(12.64f, 9.93f, 10.29f)),
            FarmObjectKind.SmallBarn => ("res://assets/environment/lwf_barn_v2.glb", new Vector3(14.65f, 7.1f, 10.06f)),
            FarmObjectKind.LargeBarn => ("res://assets/environment/lwf_large_barn_v1.glb", new Vector3(20.93f, 11.14f, 13.42f)),
            // Turned round: the drawbar at the south end, the band stairs coming off the north end into backstage.
            FarmObjectKind.TrailerStage => ("res://assets/environment/lwf_trailer_stage_v3.glb", new Vector3(9.91f, 2.2f, 4.92f)),
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

    // Keep managed PackedScene wrappers alive across repeated roster/load rebuilds.
    // Each call still creates a separate instance; approved asset bytes are unchanged.
    private static readonly Dictionary<string, PackedScene> AssetScenes = new(StringComparer.Ordinal);
    internal static Node3D InstantiateAsset(string path)
    {
        if (!AssetScenes.TryGetValue(path, out var packed))
        {
            packed = GD.Load<PackedScene>(path) ?? throw new InvalidOperationException($"Missing farm asset: {path}");
            AssetScenes.Add(path, packed);
        }
        return packed.Instantiate<Node3D>();
    }


    private void BuildHud() => BuildPreparationHud();

    private CommandEnvelope CampaignEnvelope(SessionCommand command) => _host.Envelope(command);








    private void Pick(Vector2 screenPosition, bool doubleClick = false)
    {
        if (WorldInputOccluded(screenPosition)) return;
        _selectedImmersionVendor = null;
        _selectedMarqueeId = null;
        _selectedToilet = false;
        var collider = ResolveWorldHit(screenPosition);
        if (collider is not null && _attendeePickRegistry.TryGetValue(collider.GetInstanceId(), out var attendeeId)) SelectAttendee(attendeeId);
        else if (collider is not null && TrySelectCow(collider)) { }
        else if (collider is not null && collider.GetInstanceId() == _generatorPickId) SelectGenerator();
        else if (collider is not null && TrySelectPond(collider)) { }
        else if (collider is not null && _immersionVendorPicks.TryGetValue(collider.GetInstanceId(), out var vendorId)) SelectImmersionVendor(vendorId);
        // A double-click on an occupied toilet picks whoever is inside, as there's nothing else of them to click.
        else if (collider is not null && _toiletPickOwners.TryGetValue(collider.GetInstanceId(), out var toiletId))
        {
            if (doubleClick && ToiletOccupant(toiletId) is { } occupant) SelectAttendee(new EntityId(occupant));
            else SelectToilet(toiletId);
        }
        else if (collider is not null && _binPickOwners.TryGetValue(collider.GetInstanceId(), out var binId)) SelectBin(binId);
        else if (collider is not null && _marqueePickOwners.TryGetValue(collider.GetInstanceId(), out var marqueeId)) SelectMarquee(marqueeId);
        else if (collider is not null && _securityPostPickId != 0 && collider.GetInstanceId() == _securityPostPickId) SelectSecurityPost();
        else if (collider is not null && _medicalFacilityPicks.TryGetValue(collider.GetInstanceId(), out var medicalFacility))
            SelectMedicalFacility(medicalFacility.Facility, medicalFacility.WaterPointId);
        else if (collider is not null && _pickRegistry.TryGetValue(collider.GetInstanceId(), out var item)) SelectObject(item);
        else ClearSelection();
    }

    private void SelectObject(FarmObjectReadModel item)
    {
        _selectedBinId = null; _selectedMarqueeId = null;
        if (!_visualRegistry.TryGetValue(item.StableId, out var objectVisual)) { ClearSelection(); return; }
        _selectedImmersionVendor = null;
        _selectedToilet = false;
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
        _highlight.Position = objectVisual.Position + new Vector3(0, 0.08f, 0);
        _highlight.Scale = new Vector3(radius, 1, radius); _highlight.Visible = true;
        _inspectorTitle.Text = item.DisplayName;
        var permanence = item.IsPermanent ? "Permanent • Immovable" : "Inherited • Fixed for this blockout";
        _inspectorBody.Text = $"ID  {item.StableId}\nTYPE  {DisplayKind(item.Kind)}\nSTATE  {item.State}\nSITE  {item.XMetres:0.#} m, {item.ZMetres:0.#} m\n{permanence}";
        if (Top.IsBuilt && !_hudDevelopment) _inspectorBody.Text = $"{DisplayKind(item.Kind)} · {item.State}\n{permanence}";
        GD.Print($"FARM_SELECTED id={item.StableId} orientation={_rig.OrientationName}");
    }

    private void ClearSelection()
    {
        _selectedBinId = null; _selectedMarqueeId = null;
        RefreshImmersionNeedBars(null);
        _selectedImmersionVendor = null;
        _selectedToilet = false;
        ClearSecurityPostSelection();
        RefreshMedicalNeedBars(null);
        _selected = null; _selectedAttendeeId = null; _selectedMedicalFacility = null; _selectedWaterPointId = "water.main"; _selectedPond = null;
        _selectedCowId = null; if (_herdCowButton is not null) _herdCowButton.Visible = false;
        _highlight.Visible = false; _inspectorTitle.Text = "Nothing selected";
        RefreshStagePowerAction();
        RefreshSatisfactionBar(null);
        RefreshMedicalActionInspector();
        RefreshDisorderActionInspector();
        _inspectorBody.Text = "Click a building, gate, stage or service point.\nClick empty ground to clear."; GD.Print("FARM_SELECTION_CLEARED");
    }

    private void SelectAttendee(EntityId id)
    {
        _selectedBinId = null; _selectedMarqueeId = null;
        if (_session.CapturePreparation()?.People.Any(person => person.AgentId == id.Value && person.Departed) == true) return;
        if (!_attendeeVisuals.TryGetValue(id, out var visual))
        {
            GD.Print($"ATTENDEE_SELECTION_UNAVAILABLE id={id.Value} no physical visual yet");
            return;
        }
        _selectedImmersionVendor = null;
        _selectedToilet = false;
        ClearSecurityPostSelection();
        _selectedMedicalFacility = null;
        _selected = null; _selectedAttendeeId = id;
        RefreshStagePowerAction();
        _highlight.Position = visual.Position + new Vector3(0, 0.08f, 0);
        _highlight.Scale = new Vector3(0.7f, 1, 0.7f); _highlight.Visible = true;
        RefreshAttendeeInspector();
        RefreshMedicalActionInspector();
        RefreshDisorderActionInspector();
        GD.Print($"ATTENDEE_SELECTED id={id.Value} orientation={_rig.OrientationName}");
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
        BuildPondStageAction(parent);
        _stagePowerButton = ButtonText("Shut off stage supply", () =>
            CommitEquipmentAction(new EquipmentCommand(EquipmentAction.Isolate)));
        _stagePowerButton.Visible = false;
        parent.AddChild(_stagePowerButton);
        BuildPowerSwitches(parent);
    }

    private void RefreshStagePowerAction()
    {
        RefreshPondCutButton();
        if (_stagePowerButton is null) return;
        // Only once the festival is running: before then there's no supply to shut off.
        _stagePowerButton.Visible = (_selected?.Kind == FarmObjectKind.TrailerStage || _selectedGenerator) &&
            _session.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing;
        if (!_stagePowerButton.Visible) return;
        var error = _session.ValidateCommand(CampaignEnvelope(new EquipmentCommand(EquipmentAction.Isolate)));
        _stagePowerButton.Disabled = error is not null;
        _stagePowerButton.TooltipText = _session.PowerBudgetActive
            ? "Cuts the stage off the generator: the music stops, but the rig's draw comes off at once.\n" + (error?.Message ?? "Cut the stage's power.")
            : "This generator currently supplies the trailer stage only. Isolation removes its modeled load, stops overload escalation and interrupts stage music.\n" + (error?.Message ?? "Isolate the trailer-stage circuit.");
        _stagePowerButton.Text = _session.PowerBudgetActive ? "Cut stage power" : "Shut off stage supply";
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
    }

    private static string DisplayKind(FarmObjectKind kind) => kind switch
    {
        FarmObjectKind.SmallBarn => "Small barn", FarmObjectKind.LargeBarn => "Large barn",
        FarmObjectKind.TrailerStage => "Trailer stage", FarmObjectKind.ServicePoint => "Generic service point", _ => kind.ToString(),
    };



    private void ConfigureCommandLine()
    {
        var args = OS.GetCmdlineUserArgs();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--profile-build" && i + 1 < args.Length)
            {
                // --profile-build <output.json> [guests] [seconds]
                _preparationProfileOutput = args[++i];
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out var guests)) { _profileGuests = guests; i++; }
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out var seconds)) { _profileSeconds = seconds; i++; }
            }
            // --start-tier <n>: playtests and scripted tours only; opens that tier as if the ones below were just completed.
            else if (args[i] == "--start-tier" && i + 1 < args.Length && int.TryParse(args[++i], out var tier)) _startTier = Math.Clamp(tier, 1, GameSession.HighestTier);
            else if (args[i] == "--pond-stage-trial") _pondStageTrialFlag = true;
            else if (args[i] == "--capture-litter" && i + 1 < args.Length) _litterEvidenceOutput = args[++i];
            else if (args[i] == "--capture-surround" && i + 1 < args.Length) _surroundEvidenceOutput = args[++i];
            else if (args[i] == "--capture-pond" && i + 1 < args.Length) _pondEvidenceOutput = args[++i];
            else if (args[i] == "--capture-willow" && i + 1 < args.Length) { _pondEvidenceOutput = args[++i]; _willowEvidence = true; }
            else if (args[i] == "--capture-grass" && i + 1 < args.Length) _grassEvidenceOutput = args[++i];
            else if (args[i] == "--capture-trees" && i + 1 < args.Length) _treeEvidenceOutput = args[++i];
            else if (args[i] == "--verify-genre-audio" && i + 1 < args.Length) _genreAudioVerificationOutput = args[++i];
            else if (args[i] == "--capture-steward-cleanup" && i + 1 < args.Length)
                _litterEvidenceOutput = _cleanupEvidenceOutput = args[++i];
            else if (args[i] == "--capture-size" && i + 1 < args.Length)
            {
                var size = args[++i].Split('x');
                if (size.Length == 2 && int.TryParse(size[0], out var width) && int.TryParse(size[1], out var height))
                {
                    GetWindow().Mode = Window.ModeEnum.Windowed;
                    GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
                    GetWindow().ContentScaleSize = Vector2I.Zero;
                    GetWindow().Size = new Vector2I(width, height);
                }
            }
        }
    }









    // Development layout revisions use a new save namespace. Old files remain
    // untouched and the compatibility header still rejects cross-layout loads.
    private string SaveDirectory =>
        ProjectSettings.GlobalizePath("user://saves/r0-build-v52");











    private static Vector3 ToWorld(NavigationAgentSnapshot agent) =>
        new(agent.XMillimetres / 1000f, 0.04f, agent.ZMillimetres / 1000f);

    SessionHost IHudHost.Host => _host;
    Viewport IHudHost.Viewport => GetViewport();
    string IHudHost.Message { get => _preparationMessage; set => _preparationMessage = value; }
    void IHudHost.RefreshHud() => RefreshPreparationHud();
    void IHudHost.Commit(SessionCommand command) => CommitEquipmentAction(command);
}
