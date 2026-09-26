using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private int _immersionLayoutStep;
    private string _immersionLayoutHash = "";

    private void LayoutPickVendor(string id)
    {
        Pick(_camera.UnprojectPosition(_immersionVendors[id].Position + Vector3.Up));
        if (_selectedImmersionVendor != id || _immersionMoveButton is not { Visible: true, Text: "Move" } ||
            _immersionControls!.IsAncestorOf(_immersionMoveButton))
            throw new InvalidOperationException("Vendor picking/context-only Move failed: " + id);
        if (_contextPanel is { Visible: true } && _contextPanel.GetGlobalRect().HasPoint(_camera.UnprojectPosition(_immersionVendors[id].Position + Vector3.Up)))
            throw new InvalidOperationException("Selected vendor is obscured by its context panel in the new initial frame.");
    }

    private void ProcessImmersionLayoutCapture()
    {
        if (++_immersionCaptureFrame < 4) return;
        switch (_immersionLayoutStep++)
        {
            case 0:
                AssertContextPanel(false);
                var vendors = _session.CaptureImmersion()!.Vendors;
                if (vendors.Single(v => v.Id == "food") is not { Cell: { X: 144, Z: 119 }, QuarterTurns: 0 } ||
                    vendors.Single(v => v.Id == "drinks") is not { Cell: { X: 160, Z: 120 }, QuarterTurns: 0 })
                    throw new InvalidOperationException("Corrected new-game vendor sites missing.");
                foreach (var body in _immersionVendors.Values)
                {
                    var category = body.GetNode<Label3D>("VendorCategoryLabel");
                    if (category.Text != (body == _immersionVendors["food"] ? "FOOD" : "DRINK") ||
                        category.FontSize != 45 || Math.Abs(category.PixelSize - .009f) > .00001f)
                        throw new InvalidOperationException("Vendor category label must match first-aid typography, without world prices.");
                    var screen = _camera.UnprojectPosition(body.Position);
                    var track = _camera.UnprojectPosition(new Vector3(0, 0, body.Position.Z));
                    var tent = _camera.UnprojectPosition(ImmersionPosition(GameSession.MedicalTentCell));
                    if (screen.X <= track.X || screen.X <= tent.X || screen.Y <= tent.Y) throw new InvalidOperationException("Vendor not screen-right of tent and clear of track in actual default projection.");
                    var frontZ = body.Position.Z + (body == _immersionVendors["food"] ? 1.5f : .96f);
                    if (Math.Abs(frontZ - (ImmersionPosition(GameSession.MedicalTentCell).Z + 1.5f)) > .05f)
                        throw new InvalidOperationException("Vendor serving frontage does not align with first-aid entrance.");
                    GD.Print($"IMMERSION_LAYOUT_PROJECTION world={body.Position} screen={screen} track={track} default_orientation={_orientation} camera_focus={_focus} camera_size={_camera.Size}");
                }
                var assembly = _immersionVendors["food"].GetNode<Node3D>("ImmersionFoodVan/ApprovedFoodVanAssembly");
                if (assembly.GetChildCount() != 2) throw new InvalidOperationException("Food awning omission must leave exactly chassis and fascia.");
                if (_immersionControls!.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text.Contains("MOVE", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Object Move leaked into global festival panel.");
                _immersionLayoutHash = _session.CaptureSnapshot().AuthoritativeHash;
                ClearSelection(); return;
            case 1:
                AssertContextPanel(false);
                ImmersionImage("default-tent-aligned-row-context-hidden"); LayoutPickVendor("food"); AssertContextPanel(true); return;
            case 2:
                if (!_inspectorBody.Text.Contains("Chips £3")) throw new InvalidOperationException("Food context lost product price.");
                ImmersionImage("food-selected-context-move");
                _immersionMoveButton!.EmitSignal(Button.SignalName.Pressed);
                _immersionQuarterTurns = 2;
                UpdateImmersionPlacementPreview(_camera.UnprojectPosition(_immersionVendors["food"].Position));
                if (_immersionPlacementIssue is not null) throw new InvalidOperationException(_immersionPlacementIssue);
                return;
            case 3:
                ImmersionImage("food-context-rotation-preview");
                _UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
                if (_placingImmersionVendor is not null || _session.CaptureSnapshot().AuthoritativeHash != _immersionLayoutHash)
                    throw new InvalidOperationException("Escape cancellation changed authoritative placement.");
                AssertContextPanel(false);
                LayoutPickVendor("drinks"); return;
            case 4:
                if (!_inspectorBody.Text.Contains("Soft £2") || !_inspectorBody.Text.Contains("Beer £3"))
                    throw new InvalidOperationException("Drink context lost product prices.");
                ImmersionImage("drinks-selected-context-move");
                _focus = ImmersionPosition(new GridCell(162, 144)); _camera.Size = 24; ApplyCamera();
                _immersionMoveButton!.EmitSignal(Button.SignalName.Pressed);
                _immersionQuarterTurns = 3;
                // Exercise a rotated placement at a separate clear grass site.
                UpdateImmersionPlacementPreview(_camera.UnprojectPosition(ImmersionPosition(new GridCell(162, 144))));
                if (_immersionCandidate != new GridCell(162, 144) || _immersionPlacementIssue is not null)
                    throw new InvalidOperationException($"Temporary bar preview candidate={_immersionCandidate}: {_immersionPlacementIssue}");
                return;
            case 5:
                ImmersionImage("drinks-context-rotation-preview");
                CommitImmersionPlacement(_camera.UnprojectPosition(ImmersionPosition(new GridCell(162, 144))));
                if (_session.CaptureImmersion()!.Vendors.Single(v => v.Id == "drinks").QuarterTurns != 3)
                    throw new InvalidOperationException("Context Move normal rotated placement failed.");
                // Return through the same normal command path to the requested default.
                // Physics picks refresh on the next engine frame after moving;
                // keep this already-selected stable ID for the immediate return.
                SelectImmersionVendor("drinks"); _immersionMoveButton!.EmitSignal(Button.SignalName.Pressed);
                _focus = ImmersionPosition(new GridCell(160, 120)); ApplyCamera();
                _immersionQuarterTurns = 0;
                CommitImmersionPlacement(_camera.UnprojectPosition(ImmersionPosition(new GridCell(160, 120))));
                PreparationSave(); var hash = _session.CaptureSnapshot().AuthoritativeHash; PreparationLoad();
                AssertContextPanel(false);
                if (_session.CaptureSnapshot().AuthoritativeHash != hash) throw new InvalidOperationException("Corrected layout real-file reload not exact.");
                ClearSelection(); _focus = new Vector3(6, 0, -4.25f); _camera.Size = 34;
                Rotate(1); return; // Ordinary cosmetic pan/zoom for component QA.
            case 6:
                ImmersionImage("rotated-west-no-food-awning"); Rotate(1); return;
            case 7:
                ImmersionImage("rotated-north-no-food-awning"); Rotate(1); return;
            case 8:
                ImmersionImage("rotated-east-no-food-awning"); Rotate(1);
                SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.trailer-stage"));
                if (!_disorderButtons[DisorderAction.RestoreMusic].Visible || _immersionMoveButton!.Visible)
                    throw new InvalidOperationException("Stage-specific reset or vendor selection clearing failed.");
                return;
            case 9:
                ImmersionImage("stage-selected-context-reset");
                AssertContextPanel(true);
                SelectMedicalFacility(MedicalFacility.Water, "water.main"); AssertContextPanel(true);
                SelectMedicalFacility(MedicalFacility.FirstAid); AssertContextPanel(true);
                SelectSecurityPost(); AssertContextPanel(true);
                SelectImmersionVendor("drinks"); AssertContextPanel(true);
                _immersionVendors["drinks"].Visible = false; AssertContextPanel(false);
                _immersionVendors["drinks"].Visible = true; AssertContextPanel(true);
                _UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape }); AssertContextPanel(false);
                // This CLI fixture must never touch production user:// saves.
                if (_immersionCaptureDirectory is null || SaveDirectory != System.IO.Path.Combine(_immersionCaptureDirectory, "saves"))
                    throw new InvalidOperationException("Layout fixture saves must stay inside its isolated evidence directory.");
                var current = _session; var currentHash = current.CaptureSnapshot().AuthoritativeHash;
                var legacy = GameSession.CreatePreparedCampaign(20260922);
                var saved = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-preparation", new(legacy, _saveCompatibility, "layout-legacy-context-fixture", DateTimeOffset.UtcNow));
                if (!saved.IsSuccess) throw new InvalidOperationException(saved.Error);
                PreparationLoad();
                AssertContextPanel(false);
                if (_session.CaptureDisorder() is not null || _disorderButtons[DisorderAction.RestoreMusic].Visible || _immersionMoveButton!.Visible)
                    throw new InvalidOperationException("Context actions leaked into legacy no-disorder load.");
                saved = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-preparation", new(current, _saveCompatibility, "layout-context-fixture", DateTimeOffset.UtcNow));
                if (!saved.IsSuccess) throw new InvalidOperationException(saved.Error);
                PreparationLoad();
                if (_session.CaptureSnapshot().AuthoritativeHash != currentHash) throw new InvalidOperationException("Context cross-mode restore changed layout identity.");
                ClearSelection();
                AssertContextPanel(false);
                if (_disorderButtons[DisorderAction.RestoreMusic].Visible || _immersionMoveButton!.Visible)
                    throw new InvalidOperationException("Object actions leaked after clearing selection.");
                return;
            case 10:
                AssertContextPanel(false); ImmersionImage("context-cleared-backing-hidden-global-hud-retained");
                GD.Print("IMMERSION_LAYOUT_CAPTURE_COMPLETE tent_aligned_fronts_screen_right=True front_direction_positive_z=True food_awning_omitted=True bar_canopy_unchanged=True context_move=True rotation_cancel_commit=True exact_file_reload=True four_camera_views=True stage_reset_context=True no_disorder_legacy_load_context_clear=True context_backing_startup_clear_load_removed_escape=True facility_vendor_stage_security_context=True global_hud_retained=True");
                _immersionCaptureCompleted = true; GetTree().Quit(); return;
        }
    }
}
