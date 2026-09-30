using Festival.Simulation;
using Godot;
using System;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private string? _toiletCaptureDirectory;
    private int _toiletCaptureStep;
    private int _toiletCaptureTicks;
    private string _toiletPreparationHash = "";
    private bool _toiletOccupiedReloaded;

    private void ProcessToiletCapture()
    {
        if (++_immersionCaptureFrame < 4) return;
        _immersionCaptureFrame = 0;
        switch (_toiletCaptureStep)
        {
            case 0:
                if (_toiletBody is null || _toiletDoorPivot is null || _toiletFreeIndicator is not { Visible: true } ||
                    _toiletOccupiedIndicator is not { Visible: false } || _session.CaptureToilet() is not { FullPercent: 0 })
                    throw new InvalidOperationException("Approved default free toilet or door/indicator hierarchy missing.");
                _focus = _toiletBody.Position; _camera.Size = 20; ApplyCamera();
                ClearSelection(); _hudWorkspaceOpen = false; RefreshHudWorkspace();
                _toiletCaptureStep = 1; return;
            case 1:
                ImmersionImage("01-owned-free-default");
                Pick(_camera.UnprojectPosition(_toiletBody!.Position + new Vector3(0, 2.2f, 0)));
                if (!_selectedToilet || _toiletMoveButton is not { Visible: true, Text: "Move" } ||
                    !_inspectorBody.Text.Contains("0% full", StringComparison.Ordinal))
                    throw new InvalidOperationException("Physical toilet pick, context Move or capacity text missing.");
                _toiletCaptureStep = 2; return;
            case 2:
                ImmersionImage("02-picked-context-capacity-move");
                _toiletPreparationHash = _session.CaptureSnapshot().AuthoritativeHash;
                _toiletMoveButton!.EmitSignal(Button.SignalName.Pressed);
                RotateToiletPlacement(1);
                _toiletCaptureStep = 3; return;
            case 3:
                ImmersionImage("03-rotated-placement-preview");
                _UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
                if (_movingToilet || _session.CaptureSnapshot().AuthoritativeHash != _toiletPreparationHash)
                    throw new InvalidOperationException("Toilet placement cancellation changed authoritative state.");
                PreparationSave(); var hash = _session.CaptureSnapshot().AuthoritativeHash; PreparationLoad();
                if (_session.CaptureSnapshot().AuthoritativeHash != hash)
                    throw new InvalidOperationException("Current-version toilet preparation file reload changed authoritative hash.");
                ClearSelection();
                CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"]));
                PreparationAccept("staff.steward"); PreparationAccept("equipment.buy");
                PreparationStart();
                if (_session.PreparedStatus != PreparationStatus.Running)
                    throw new InvalidOperationException("Toilet capture could not start a normal campaign: " + _preparationMessage);
                // Labelled diagnostic drive only: put two ordinary admitted guests
                // near the visit threshold before the unrelated incident deadline.
                var ids = _session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest)
                    .Skip(3).Take(2).Select(p => p.AgentId).ToHashSet();
                var immersion = _session.CaptureImmersion()!;
                typeof(GameSession).GetProperty("ImmersionView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                    immersion with { People = immersion.People.Select(p => ids.Contains(p.AgentId) ?
                        p with { ToiletNeed = 9_000 } : p).ToArray() });
                var medical = _session.CaptureMedical()!;
                typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                    medical with { Needs = medical.Needs.Select(n => ids.Contains(n.AgentId) ? n with { Thirst = 0 } : n).ToArray() });
                _toiletCaptureStep = 4; return;
            case 4:
                _session.AdvanceWithoutSnapshot(160); _toiletCaptureTicks += 160;
                _foundationPresentation.Reset(_session.CaptureObservation());
                if (_session.CaptureToilet() is { OwnerId: { } owner, DoorOpen: false } &&
                    _session.CaptureImmersion()!.People.Single(p => p.AgentId == owner).ToiletStage == ToiletVisitStage.Using)
                {
                    RefreshPreparationHud();
                    if (_toiletFreeIndicator!.Visible || !_toiletOccupiedIndicator!.Visible || _toiletDoorPivot!.RotationDegrees.Y != 0)
                        throw new InvalidOperationException("Occupied service must have one red indicator and closed door.");
                    _focus = _toiletBody!.Position; _camera.Size = 8; _orientation = 2; ApplyCamera();
                    _toiletCaptureStep = 5;
                }
                else if (_toiletCaptureTicks >= 21_000 || _session.PreparedStatus != PreparationStatus.Running)
                    throw new InvalidOperationException("No ordinary physical toilet occupancy before festival closing.");
                return;
            case 5:
                ImmersionImage("04-occupied-door-closed");
                var before = _session.CaptureSnapshot().AuthoritativeHash;
                PreparationSave(); PreparationLoad();
                if (_session.CaptureSnapshot().AuthoritativeHash != before)
                    throw new InvalidOperationException("Current-version occupied toilet file reload changed authoritative hash.");
                _toiletOccupiedReloaded = true;
                _toiletCaptureStep = 6; return;
            case 6:
                _session.AdvanceWithoutSnapshot(80); _toiletCaptureTicks += 80;
                _foundationPresentation.Reset(_session.CaptureObservation());
                if (_session.CaptureToilet() is { WeeCount: > 0 } or { PooCount: > 0 } && _session.CaptureToilet()!.OwnerId is null)
                { RefreshPreparationHud(); _toiletCaptureStep = 7; }
                else if (_toiletCaptureTicks >= 23_000)
                    throw new InvalidOperationException("Occupied visitor did not finish and physically leave.");
                return;
            case 7:
                if (!_toiletFreeIndicator!.Visible || _toiletOccupiedIndicator!.Visible || _toiletDoorPivot!.RotationDegrees.Y != 0)
                    throw new InvalidOperationException("Post-visit free indicator or closed door missing.");
                SelectToilet();
                if (!_inspectorBody.Text.Contains("% full", StringComparison.Ordinal))
                    throw new InvalidOperationException("Clicked capacity text missing after the first visit.");
                _toiletCaptureStep = 8; return;
            case 8:
                AssertContextPanel(true);
                ImmersionImage("05-after-visit-counts-capacity");
                var toilet = _session.CaptureToilet()!;
                GD.Print($"TOILET_CAPTURE_COMPLETE size={GetWindow().Size} people={_session.CapturePreparation()!.People.Length} ticks={_toiletCaptureTicks} wees={toilet.WeeCount} poos={toilet.PooCount} litres={toilet.UsedMillilitres / 1000m:0.0} percent={toilet.FullPercent} occupied_reload={_toiletOccupiedReloaded} prepared_reload=True physical_pick=True exclusive_door=True");
                _immersionCaptureCompleted = true; GetTree().Quit(); return;
        }
    }
}
