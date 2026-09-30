using Festival.ContentAdapter;
using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private bool _attendeePoseCapture;
    private int _poseStep;
    private int _poseFrame;
    private string? _poseImage;
    private readonly HashSet<string> _poseActual = [];
    private EntityId _poseOwner;
    private MedicalSnapshot? _poseMedical;
    private DisorderSnapshot? _poseDisorder;
    private string _poseGuardHash = "";
    private Node3D? _poseShowcase;
    private Label? _poseDisclaimer;
    private int _poseShowcaseIndex;
    private readonly Dictionary<ulong, string> _poseRetryVariants = [];
    private readonly Dictionary<ulong, string> _poseRetryColours = [];
    private int _paletteCaptureIndex;
    private static readonly string[] PoseShowcaseStates = ["relaxed", "drink_hold", "food_hold", "drinking", "drinking", "eating"];
    private static readonly ImmersionProduct[] PoseShowcaseProducts = [ImmersionProduct.SoftDrink, ImmersionProduct.Beer, ImmersionProduct.Chips, ImmersionProduct.Beer, ImmersionProduct.SoftDrink, ImmersionProduct.Chips];
    /// <summary>The capture's subject: the first admitted guest.</summary>
    private ulong PoseGuestId() => _session.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Guest && person.Admitted).AgentId;

    private static void PoseAssert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("ATTENDEE_POSE_ASSERT " + message); }
    private static void PoseSetField(GameSession session, string field, object value) =>
        SetMember(typeof(GameSession), field, BindingFlags.NonPublic | BindingFlags.Instance, session, value);

    private void ProcessAttendeePoseCapture()
    {
        if (++_poseFrame < 4) return;
        if (_poseImage is { } image)
        {
            AttendeePoseImage(image); _poseImage = null;
            return;
        }
        if (_poseStep == 0)
        {
            PoseAssert(SaveDirectory == System.IO.Path.Combine(_attendeePoseCaptureDirectory!, "saves"), "Capture save isolation missing.");
            var perk = _session.CapturePerks()!;
            CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand.First(id => id != "thirsty-crowd")));
            CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"]));
            foreach (var offer in new[] { "staff.steward", "equipment.buy" }) PreparationAccept(offer);
            CommitEquipmentAction(new UseDefaultBuildLayoutCommand());
            CommitEquipmentAction(new SetPreparationStockCommand(40, 40, 32)); PreparationStart();
            PoseAssert(_session.PreparedStatus == PreparationStatus.Running, _preparationMessage);
            TimetableAdvanceTo(1600);
            var medical = _session.CaptureMedical()!;
            StaffCaptureSend(new StaffInterventionCommand(PoseGuestId(), medical.MedicId, StaffInterventionAction.GuideToRest));
            TimetableAdvanceTo(2000);
            _poseStep = 1;
            return;
        }
        if (_poseStep == 1)
        {
            var state = _session.CaptureImmersion()!;
            foreach (var person in state.People.Where(p => p.Held is not null && _session.ImmersionConsumptionEligible(p.AgentId)))
            {
                var id = new EntityId(person.AgentId);
                var root = _attendeeVisuals[id]; if (!root.HasMeta("GuestPoseVariant")) continue;
                var pose = AttendeePose.State(person.Held, true, true);
                var key = root.GetMeta("GuestPoseVariant").AsString() + "-" + pose + "-" + ImmersionProductKey(person.Held!.Product);
                if (_poseActual.Contains(key)) continue;
                _poseActual.Add(key); _poseOwner = id;
                SelectAttendee(id); _focus = root.Position; _camera.Size = 12; ApplyCamera();
                AdvancePreparationPresentation(0);
                PoseAssert(root.GetNodeOrNull<Node3D>("GuestBody") is not null && _immersionHeldVisuals.TryGetValue(id, out var prop) && prop.GetParent() == root, "Actual paid item/body root missing.");
                var hash = _session.CaptureSnapshot().AuthoritativeHash;
                var file = root.GetMeta("GuestPoseFile").AsString();
                var colourKey = GuestColourKey(root); AssertGuestPalette(root);
                var originalSession = _session;
                PreparationSave();
                var saved = SaveFileAdapter.LoadSlot(SaveDirectory, "manual-preparation", _saveCompatibility);
                PoseAssert(System.IO.File.Exists(SaveFileAdapter.ResolveSlotPath(SaveDirectory, "manual-preparation")) && saved.IsSuccess && saved.Session!.CaptureSnapshot().AuthoritativeHash == hash, "Actual isolated save file missing or invalid.");
                PreparationLoad(); AdvancePreparationPresentation(0);
                PoseAssert(!ReferenceEquals(originalSession, _session) && hash == _session.CaptureSnapshot().AuthoritativeHash && _attendeeVisuals[id].GetMeta("GuestPoseFile").AsString() == file, "Saved partial pose did not reconstruct exactly in a new session.");
                var loaded = _attendeeVisuals[id];
                PoseAssert(GuestColourKey(loaded) == colourKey, "File reload changed deterministic guest colours."); AssertGuestPalette(loaded);
                PoseAssert(_session.IsPaused, "Actual pose diagnostic was not paused.");
                _session.AdvanceWithoutSnapshot(80); AdvanceImmersionPresentation(0);
                PoseAssert(_session.CaptureSnapshot().AuthoritativeHash == hash && loaded.GetMeta("GuestPoseFile").AsString() == file, "Paused actual pose or ingestion progressed.");
                PoseAssert(loaded.GetChildren().OfType<StaticBody3D>().Count() == 1 && loaded.GetChildren().OfType<Node3D>().Count(n => n.Name == "GuestBody") == 1, "Duplicate capsule/body after reload.");
                SelectAttendee(id);
                GD.Print($"ATTENDEE_POSE_ACTUAL key={key} owner={id.Value} transaction={person.Held.TransactionId} consumed={person.Held.ConsumedTicks} hash={hash} reload=True root_identity=True scale=1");
                _poseImage = "actual-" + key;
                return;
            }
            var required = new[] { "drinking-beer", "drinking-soft", "eating-chips" };
            if (required.All(suffix => _poseActual.Any(k => k.EndsWith(suffix, StringComparison.Ordinal))) &&
                _poseActual.Any(k => k.StartsWith("male-", StringComparison.Ordinal)) && _poseActual.Any(k => k.StartsWith("female-", StringComparison.Ordinal)))
            {
                _poseMedical = _session.CaptureMedical()!; _poseDisorder = _session.CaptureDisorder()!;
                var owner = _poseOwner.Value;
                PoseSetField(_session, "MedicalView", _poseMedical with { Needs = _poseMedical.Needs.Select(n => n.AgentId == owner ? n with { Stage = MedicalStage.Collapsed, Intent = MedicalIntent.Collapsed } : n).ToArray() });
                _poseGuardHash = _session.CaptureSnapshot().AuthoritativeHash;
                AdvancePreparationPresentation(0);
                var root = _attendeeVisuals[_poseOwner];
                PoseAssert(Math.Abs(root.Rotation.X - Mathf.Pi / 2) < .01 && !_immersionHeldVisuals.ContainsKey(_poseOwner) && root.GetMeta("GuestPoseState").AsString() == "relaxed", "Collapse precedence or prop suspension broken.");
                var layer = new CanvasLayer { Layer = 19 }; AddChild(layer);
                _poseDisclaimer = LabelText("INITIALIZED COLLAPSE / FIGHT FIXTURE\nNo natural incident claim", 18, new Color("fff0bd"));
                _poseDisclaimer.Position = new Vector2(500, 125); layer.AddChild(_poseDisclaimer);
                _focus = root.Position; _camera.Size = 12; ApplyCamera();
                _poseImage = "fixture-collapsed-retained-item"; _poseStep = 2; return;
            }
            PoseAssert(_session.CurrentTick < 20000 && _session.PreparedStatus == PreparationStatus.Running, "Actual purchase/pose proof exceeded bounded live route.");
            TimetableAdvanceTo(_session.CurrentTick + 32); return;
        }
        if (_poseStep == 2)
        {
            PoseAssert(_session.CaptureSnapshot().AuthoritativeHash == _poseGuardHash, "Collapse presentation changed gameplay.");
            var collapsedRoot = _attendeeVisuals[_poseOwner];
            var collapsedPoint = _camera.UnprojectPosition(collapsedRoot.ToGlobal(new Vector3(0, .85f, 0)));
            var collapsedHit = ResolveWorldHit(collapsedPoint);
            PoseAssert(collapsedHit is not null && _attendeePickRegistry.TryGetValue(collapsedHit.GetInstanceId(), out var collapsedPicked) && collapsedPicked == _poseOwner, "Collapsed guest capsule identity no longer picks.");
            PoseSetField(_session, "MedicalView", _poseMedical!);
            var opponent = _poseDisorder!.People.First(p => p.AgentId != _poseOwner.Value).AgentId;
            PoseSetField(_session, "DisorderView", _poseDisorder with { People = _poseDisorder.People.Select(p => p.AgentId == _poseOwner.Value ? p with { Stage = DisorderStage.Fight, StageTick = _session.CurrentTick, OpponentId = opponent } : p).ToArray() });
            _poseGuardHash = _session.CaptureSnapshot().AuthoritativeHash; AdvancePreparationPresentation(0);
            PoseAssert(!_immersionHeldVisuals.ContainsKey(_poseOwner) && _attendeeVisuals[_poseOwner].GetMeta("GuestPoseState").AsString() == "relaxed", "Fight hands precedence broken.");
            _poseImage = "fixture-fighting-retained-item"; _poseStep = 3; return;
        }
        if (_poseStep == 3)
        {
            PoseAssert(_session.CaptureSnapshot().AuthoritativeHash == _poseGuardHash, "Fight presentation changed gameplay.");
            PoseSetField(_session, "DisorderView", _poseDisorder!); AdvancePreparationPresentation(0);
            PoseAssert(_immersionHeldVisuals.ContainsKey(_poseOwner), "Retained prop did not resume after hands interruption.");
            var root = _attendeeVisuals[_poseOwner];
            ClearSelection(); _focus = root.Position; _camera.Size = 12; ApplyCamera();
            var point = _camera.UnprojectPosition(root.ToGlobal(new Vector3(0, .85f, 0)));
            var hit = ResolveWorldHit(point);
            PoseAssert(hit is not null && _attendeePickRegistry.TryGetValue(hit.GetInstanceId(), out var picked) && picked == _poseOwner, "Resumed guest capsule no longer picks stable identity.");
            Pick(point); PoseAssert(_selectedAttendeeId == _poseOwner, "Resumed guest selection identity changed.");
            _poseDisclaimer!.Text = "RETAINED ITEM RESUMED AFTER FIXTURE\nActual purchase and saved progress preserved";
            _poseImage = "actual-item-resumed-and-selected"; _poseGuardHash = _session.CaptureSnapshot().AuthoritativeHash;
            GD.Print("ATTENDEE_POSE_INTERRUPTION collapse=True collapsed_picking=True fight=True retained=True resume=True picked=True cosmetic_hash=True");
            _poseStep = 4; return;
        }
        if (_poseStep == 4)
        {
            ClearSelection(); _poseDisclaimer!.Text = "PRESENTATION-ONLY POSE SHOWCASE\nBoth approved adult bodies · no inventory or gameplay mutation";
            if (_poseShowcase is not null) { _poseShowcase.Visible = false; _poseShowcase.QueueFree(); _poseShowcase = null; }
            if (_poseShowcaseIndex >= 30)
            {
                PoseAssert(_session.CaptureSnapshot().AuthoritativeHash == _poseGuardHash, "Showcase changed gameplay hash.");
                foreach (var person in _session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest))
                {
                    _poseRetryVariants.Add(person.AgentId, _attendeeVisuals[new(person.AgentId)].GetMeta("GuestPoseVariant").AsString());
                    _poseRetryColours.Add(person.AgentId, GuestColourKey(_attendeeVisuals[new(person.AgentId)]));
                }
                // CLI-only rare medical fixture uses the existing next-tick fatal
                // transition and ordinary Council Favour command, not a new rule.
                var medical = _session.CaptureMedical()!; var tick = _session.CurrentTick;
                var collapse = tick - GameSession.MedicalDeathDelayTicks;
                var warning = collapse - GameSession.MedicalCollapseDelayTicks;
                var victim = PoseGuestId();
                PoseSetField(_session, "MedicalView", medical with {
                    Needs = medical.Needs.Select(n => n.AgentId == victim ? n with { Stage = MedicalStage.Critical, Intent = MedicalIntent.Collapsed, Thirst = 10000, HeatExposure = 10000, WarningTick = warning, CollapseTick = collapse, CriticalTick = collapse + GameSession.MedicalCriticalDelayTicks } : n).ToArray() });
                StaffCaptureSend(new SetPausedCommand(false)); _session.AdvanceWithoutSnapshot(1);
                PoseAssert(_session.PreparedStatus == PreparationStatus.Failed, "Initialized past-deadline medical fixture did not take existing death transition.");
                _poseDisclaimer!.Text = "INITIALIZED PAST-DEADLINE MEDICAL FIXTURE\nOrdinary hearing and Favour retry reconstruction check";
                RefreshPreparationHud(); _poseImage = "fixture-retry-hearing"; _poseStep = 5; return;
            }
            var close = _poseShowcaseIndex >= 24;
            var index = close ? _poseShowcaseIndex - 24 : _poseShowcaseIndex / 4;
            var rotation = close ? 0 : _poseShowcaseIndex % 4;
            var state = PoseShowcaseStates[index]; var product = PoseShowcaseProducts[index];
            _poseShowcase = new Node3D(); AddChild(_poseShowcase);
            foreach (var variant in new[] { "male", "female" })
            {
                var root = AddGuestPoseRoot(new EntityId(ulong.MaxValue), new Vector3(variant == "male" ? -1.5f : 1.5f, .04f, 0));
                RemoveChild(root); _poseShowcase.AddChild(root); root.SetMeta("GuestPoseVariant", variant); SetGuestBodyPose(root, state, product);
                AssertGuestPalette(root);
                root.AddChild(new Label3D { Text = variant.ToUpperInvariant() + " · FIXTURE", Position = new Vector3(0, 2.2f, 0), FontSize = 24, PixelSize = .006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
                if (state != "relaxed")
                {
                    var prop = InstantiateAsset(product == ImmersionProduct.Chips ? "res://assets/props/lwf_chips_tray_v1.glb" : product == ImmersionProduct.Beer ? "res://assets/props/lwf_beer_cup_v2.glb" : "res://assets/props/lwf_soft_drink_cup_v1.glb");
                    root.AddChild(prop); ApplyGuestPropAnchor(root, prop, ImmersionProductKey(product));
                    PoseAssert(prop.Scale == Vector3.One, "Showcase prop scaled.");
                }
            }
            _orientation = rotation; _focus = new Vector3(0, .8f, 0); _camera.Size = close ? 12 : 32; ApplyCamera();
            _poseImage = $"showcase-{state}-{ImmersionProductKey(product)}-{(close ? "contact" : "view" + rotation)}";
            _poseShowcaseIndex++; return;
        }
        if (_poseStep == 5)
        {
            ConfirmHearing(new SpendCouncilFavourCommand()); ApplyHearingDecision();
            PoseAssert(_session.PreparedStatus == PreparationStatus.Preparing && _attendeeVisuals.Count == 0 && _immersionHeldVisuals.Count == 0 && _attendeePickRegistry.Count == 0, "Retry retained departed roots, props or pickers: " + _preparationMessage);
            var perk = _session.CapturePerks()!;
            CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand.First(id => id != "thirsty-crowd")));
            CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"]));
            PreparationAccept("staff.steward"); CommitEquipmentAction(new SetPreparationStockCommand(40, 40, 32)); PreparationStart();
            PoseAssert(_session.PreparedStatus == PreparationStatus.Running, "Retry start failed: " + _preparationMessage);
            StaffCaptureSend(new SetPausedCommand(true)); AdvancePreparationPresentation(0);
            foreach (var (id, variant) in _poseRetryVariants)
            {
                var root = _attendeeVisuals[new(id)];
                PoseAssert(root.GetMeta("GuestPoseVariant").AsString() == variant && root.GetMeta("GuestPoseState").AsString() == "relaxed" && root.GetChildren().OfType<StaticBody3D>().Count() == 1, "Retry guest identity/body/capsule changed.");
                PoseAssert(GuestColourKey(root) == _poseRetryColours[id], "Retry changed existing guest colours."); AssertGuestPalette(root);
            }
            var hash = _session.CaptureSnapshot().AuthoritativeHash; var before = _session;
            PreparationSave(); var saved = SaveFileAdapter.LoadSlot(SaveDirectory, "manual-preparation", _saveCompatibility);
            PoseAssert(saved.IsSuccess && saved.Session!.CaptureSnapshot().AuthoritativeHash == hash, "Retry actual save failed: " + saved.Error);
            PreparationLoad(); AdvancePreparationPresentation(0);
            PoseAssert(!ReferenceEquals(before, _session) && _session.CaptureSnapshot().AuthoritativeHash == hash && _immersionHeldVisuals.Count == 0, "Retry file reload/body cleanup changed state: " + _preparationMessage);
            _poseDisclaimer!.Text = "ORDINARY FAVOUR RETRY / FILE RELOAD\nGuest variants preserved · no stale held props or pickers";
            _focus = new Vector3(-6, 0, 13); _camera.Size = 44; ApplyCamera(); _poseImage = "retry-reloaded-clean-roots";
            GD.Print($"ATTENDEE_POSE_RETRY same_variants=True same_colours=True fresh_roots=True cleared_props=True cleared_pickers=True actual_file_reload=True new_session=True hash={hash}");
            _poseStep = 6; return;
        }
        if (_poseStep == 6)
        {
            if (_poseShowcase is not null) { _poseShowcase.Visible = false; _poseShowcase.QueueFree(); _poseShowcase = null; }
            ClearSelection();
            _poseDisclaimer!.Text = "PRESENTATION-ONLY DESIGNER PALETTE SHOWCASE\nProtected skin / eyes / shoes / mouth unchanged · no gameplay mutation";
            if (_paletteCaptureIndex == 0)
            {
                _poseGuardHash = _session.CaptureSnapshot().AuthoritativeHash;
                _poseShowcase = new Node3D(); AddChild(_poseShowcase);
                for (var clothing = 0; clothing < 4; clothing++) for (var hair = 0; hair < 4; hair++)
                    foreach (var variant in new[] { "male", "female" })
                        AddPaletteShowcaseActor(variant, clothing, hair, new Vector3(clothing * 3.2f - 4.8f + (variant == "male" ? -.65f : .65f), .04f, hair * 3.2f + 3.2f));
                _orientation = 0; _focus = new Vector3(0, 0, 8); _camera.Size = 32; ApplyCamera();
                _poseImage = "palette-all16-both-bodies-gameplay32m"; _paletteCaptureIndex++; return;
            }
            if (_paletteCaptureIndex < 3)
            {
                var hair = _paletteCaptureIndex;
                _poseShowcase = new Node3D(); AddChild(_poseShowcase);
                AddPaletteShowcaseActor("male", 1, hair, new(-1.5f, .04f, 0));
                AddPaletteShowcaseActor("female", 2, hair, new(1.5f, .04f, 0));
                _orientation = 0; _focus = Vector3.Zero; _camera.Size = 8; ApplyCamera();
                _poseImage = "palette-hair-" + GuestPaletteContract.HairIds[hair] + "-close8m"; _paletteCaptureIndex++; return;
            }
            foreach (var person in _session.CapturePreparation()!.People.Where(p => p.Role != ProtectedPersonRole.Guest))
                PoseAssert(!_attendeeVisuals[new(person.AgentId)].HasMeta("GuestClothing"), "Palette application reached legacy staff/performer.");
            PoseAssert(_session.CaptureSnapshot().AuthoritativeHash == _poseGuardHash && _guestPaletteMaterials.Values.All(set => set.Length == 16), "Palette showcase changed gameplay or exceeded cache bound.");
            GD.Print($"ATTENDEE_PALETTE_CHECK_COMPLETE combinations=16 both_bodies=True protected_bytes=True alpha=True original_resources=True nonsampling_material_flags=True nearest_clamp=True cached_across_poses=True compatible_bases={_guestPaletteMaterials.Count} materials={_guestPaletteMaterials.Count * 16} same_colours_reload_retry=True legacy_staff_performer=True hash={_poseGuardHash}");
            GD.Print($"ATTENDEE_POSE_CAPTURE_COMPLETE actual_keys={_poseActual.Count} showcase_images=30 palette_images=3 both_variants=True all_six_visuals=True four_rotations=True reload=True interruption=True picking=True retry=True showcase_hash={_poseGuardHash} manual_QA=False independent_review=False");
            _attendeePoseCaptureCompleted = true; GetTree().Quit();
        }
    }

    private void AddPaletteShowcaseActor(string variant, int clothing, int hair, Vector3 position)
    {
        var root = AddGuestPoseRoot(new(ulong.MaxValue), position);
        RemoveChild(root); _poseShowcase!.AddChild(root);
        root.SetMeta("GuestPoseVariant", variant); root.SetMeta("GuestClothing", clothing); root.SetMeta("GuestHair", hair);
        SetGuestBodyPose(root, "relaxed", null);
        ApplyGuestPalette(root, root.GetNode<Node3D>("GuestBody")); AssertGuestPalette(root);
        root.AddChild(new Label3D { Text = GuestPaletteContract.ClothingIds[clothing] + "\n" + GuestPaletteContract.HairIds[hair], Position = new(0, 2.2f, 0), FontSize = 20, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
    }
}
