using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

public partial class Main
{
    private string? _guestArrivalCaptureDirectory;
    private int _guestArrivalCaptureStep;
    private double _guestArrivalCaptureWait;
    private int _guestArrivalMidCount;

    private void GuestArrivalCaptureImage(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        PlaytestRequire(image.GetWidth() == GetWindow().Size.X && image.GetHeight() == GetWindow().Size.Y, "arrival native resolution");
        PlaytestRequire(image.SavePng(Path.Combine(_guestArrivalCaptureDirectory!, name + ".png")) == Error.Ok, "arrival screenshot");
    }

    private void GuestArrivalCaptureCheck()
    {
        var people = _session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).ToArray();
        PlaytestRequire(_session.OnSiteAttendeeCount == people.Count(p => p.Admitted && !p.Departed), "guest-only actual admission count");
        foreach (var person in people)
        {
            var pending = _session.GuestWaitingForRelease(person.AgentId);
            var body = _attendeeVisuals[new(person.AgentId)];
            var pick = body.GetChildren().OfType<StaticBody3D>().Single();
            PlaytestRequire(body.Visible == !pending && (pick.CollisionLayer != 0) == !pending,
                "pending guests hidden and unpickable; released guests visible and pickable");
            if (pending) PlaytestRequire(!person.Admitted, "pending guest is not admitted");
        }
    }

    private void ProcessGuestArrivalCapture(double delta)
    {
        if (_guestArrivalCaptureDirectory is null) return;
        _guestArrivalCaptureWait += delta;
        if (_guestArrivalCaptureWait < .25) return;
        _guestArrivalCaptureWait = 0;
        try
        {
            GD.Print($"GUEST_ARRIVAL_CAPTURE_STEP step={_guestArrivalCaptureStep} tick={_session.CurrentTick}");
            switch (_guestArrivalCaptureStep++)
            {
                case 0:
                    var perk = _session.CapturePerks()!;
                    CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
                    CommitEquipmentAction(new UseDefaultBuildLayoutCommand());
                    CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
                    foreach (var offer in new[] { "staff.steward", "maintenance.worker", "equipment.buy" })
                        CommitEquipmentAction(new AcceptPreparationOfferCommand(offer));
                    PreparationStart();
                    PlaytestRequire(_session.PreparedStatus == PreparationStatus.Running, "guest capture started");
                    _hudWorkspaceOpen = false; _buildDrawerOpen = false; _hudProgrammeOpen = false; ClearSelection();
                    _focus = new(4, 0, 8); _camera.Size = 50; ApplyCamera();
                    break;
                case 1:
                    AdvancePreparationPresentation(0); RefreshPreparationHud(); GuestArrivalCaptureCheck();
                    PlaytestRequire(_session.OnSiteAttendeeCount == 0, "zero admitted at start");
                    break;
                case 2:
                    GuestArrivalCaptureImage("01-start-gate");
                    _session.AdvanceWithoutSnapshot(2900);
                    _foundationPresentation.Reset(_session.CaptureObservation());
                    break;
                case 3:
                    AdvancePreparationPresentation(0); RefreshPreparationHud(); GuestArrivalCaptureCheck();
                    _guestArrivalMidCount = _session.OnSiteAttendeeCount;
                    PlaytestRequire(_guestArrivalMidCount > 0 && _guestArrivalMidCount < 20, "mixed arrivals at mid-window");
                    PlaytestRequire(_session.CapturePreparation()!.People.Any(p => p.Role == ProtectedPersonRole.Guest && _session.GuestWaitingForRelease(p.AgentId)), "pending guests remain midway");
                    break;
                case 4:
                    GuestArrivalCaptureImage("02-staggered-arrivals");
                    _session.AdvanceWithoutSnapshot(1500);
                    _foundationPresentation.Reset(_session.CaptureObservation());
                    break;
                case 5:
                    AdvancePreparationPresentation(0); RefreshPreparationHud(); GuestArrivalCaptureCheck();
                    PlaytestRequire(_session.OnSiteAttendeeCount > _guestArrivalMidCount, "later arrivals increase attendance");
                    PlaytestRequire(_session.CurrentTick < GameSession.FestivalSlotStarts[0], "all release visualized before first band");
                    PlaytestRequire(!_session.CapturePreparation()!.People.Any(p => p.Role == ProtectedPersonRole.Guest && _session.GuestWaitingForRelease(p.AgentId)), "all guests released by 60 seconds");
                    break;
                case 6:
                    GuestArrivalCaptureImage("03-before-first-band");
                    var guests = _session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).ToArray();
                    File.WriteAllText(Path.Combine(_guestArrivalCaptureDirectory, "result.json"), JsonSerializer.Serialize(new {
                        passed = true, seed = _session.CampaignSeed, resolution = GetWindow().Size.ToString(),
                        finalTick = _session.CurrentTick, firstBandTick = GameSession.FestivalSlotStarts[0],
                        admittedAtMidWindow = _guestArrivalMidCount, admittedAtFinal = _session.OnSiteAttendeeCount,
                        releasedAtFinal = guests.Count(p => !_session.GuestWaitingForRelease(p.AgentId)),
                        bound = guests.Length, physicalArrival = true, screenshots = 3
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    GD.Print("GUEST_ARRIVAL_CAPTURE_COMPLETE " + _guestArrivalCaptureDirectory);
                    _guestArrivalCaptureDirectory = null; GetTree().Quit(); break;
            }
        }
        catch (Exception error)
        {
            GD.PushError("GUEST_ARRIVAL_CAPTURE_FAILED " + error);
            _guestArrivalCaptureDirectory = null; GetTree().Quit(2);
        }
    }
}
