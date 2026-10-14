using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Explicit opt-in smoke, isolated from player saves. Injects a real adapter failure before atomic replacement.
public partial class Main
{
    private string? _saveWarningEvidenceOutput;
    private bool _saveWarningInjectFailure;
    private int _saveWarningFrame;
    private byte[]? _saveWarningEarlier;
    private void SaveWarningClick(string name)
    {
        var button = FindChildren(name, "Button", true, false).OfType<Button>().First(b => b.IsVisibleInTree());
        var point = button.GetGlobalRect().GetCenter();
        ArchitectureCheck(name + "-pointer-inside-viewport", !button.Disabled && GetViewport().GetVisibleRect().HasPoint(point));
        Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point });
        Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left,
            Pressed = true, ButtonMask = MouseButtonMask.Left });
        Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false });
    }
    private string SaveWarningManualPath => SaveFileAdapter.ResolveSlotPath(_host.SaveDirectory, SessionHost.ManualSlot);
    private void ProcessSaveWarningEvidence()
    {
        try
        {
            var frame = _saveWarningFrame++;
            if (frame == 0) { Directory.CreateDirectory(_saveWarningEvidenceOutput!); _challengeFlowEnabled = true; CloseChallengeReveal(); BuildStartSplash(); }
            if (frame == 8) ArchitecturePress("DevelopmentTiers");
            if (frame == 16) ArchitecturePress("DevelopmentTier2");
            if (frame == 24) ArchitecturePress("PrepareFestival");
            if (frame == 32)
            {
                var perk = _session.CapturePerks()!;
                ArchitectureSend(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
                _boxOfficeSeenAttempt = _session.CapturePreparation()!.Attempt;
                ArchitectureSend(new UseDefaultBuildLayoutCommand());
                var acts = _session.GetFestivalActs().Where(a => _session.ActStandingOf(a) == ActStanding.Available)
                    .OrderBy(_session.ActFee).ThenBy(a => a.Id, StringComparer.Ordinal).Select(a => a.Id).ToArray();
                ArchitectureSend(new SetProgrammeCommand(acts[..3]));
                ArchitectureSend(new SetProgrammeCommand(acts[3..6]) { StageId = _session.Stages[1].Id });
                ArchitectureSend(new SetPreparationStockCommand(40, 32));
                var crew = _session.GetStaffCandidates().GroupBy(c => c.Role).Select(g => g.OrderBy(c => c.Traits.Length > 0)
                    .ThenBy(c => c.Grade > 0).ThenBy(c => Math.Abs(c.Grade)).ThenBy(c => c.Id, StringComparer.Ordinal).First()).ToArray();
                foreach (var person in crew) ArchitectureSend(new AcceptPreparationOfferCommand(person.Id));
                var extra = _session.GetStaffCandidates().Where(c => c.Role == StaffRole.Sound && !crew.Any(p => p.Id == c.Id))
                    .OrderBy(c => c.Traits.Length > 0).ThenBy(c => c.Grade > 0).ThenBy(c => Math.Abs(c.Grade)).ThenBy(c => c.Id, StringComparer.Ordinal).First();
                ArchitectureSend(new AcceptPreparationOfferCommand(extra.ExtraOfferId));
                ArchitectureCheck("paid-normal-plan-ready", _session.GetPreparationStartBlockers().Count == 0);
                ArchitectureCheck("earlier-manual-save-written", _host.SaveManual().IsSuccess);
                _saveWarningEarlier = File.ReadAllBytes(SaveWarningManualPath);
                _saveWarningInjectFailure = true;
                PreparationSave();
                ArchitectureCheck("warning-shown-after-real-save-failure", _host.SaveWarningVisible && _cadenceErrorPanel!.Visible);
                ArchitectureCheck("earlier-slot-preserved", _saveWarningEarlier.SequenceEqual(File.ReadAllBytes(SaveWarningManualPath)));
            }
            if (frame == 40) ArchitecturePicture("01-save-failure-dismissible");
            if (frame == 48)
            {
                SaveWarningClick("DismissSaveWarning");
            }
            if (frame == 52)
            {
                ArchitectureCheck("x-dismisses-to-discreet-retry", !_host.SaveWarningVisible && !_cadenceErrorPanel!.Visible && _cadenceRetryStatus!.Visible);
                SelectHudTab("Programme");
                var barn = FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text.StartsWith("Barn Stage ·", StringComparison.Ordinal) && b.IsVisibleInTree());
                barn.EmitSignal(Button.SignalName.Pressed);
                ArchitectureCheck("stage-selector-usable", barn.ButtonPressed);
            }
            if (frame == 56) ArchitecturePicture("02-dismissed-stage-selection");
            if (frame == 64)
            {
                PreparationStart();
                ArchitectureCheck("start-continues-through-save-failure", _session.PreparedStatus == PreparationStatus.Running && _host.SaveError is not null);
                var tick = _session.CurrentTick;
                ArchitectureCheck("live-ticks-continue", _host.Advance(0.1, _ => { }) == 8 && _session.CurrentTick == tick + 8);
                ArchitectureSend(new SetPausedCommand(true));
                SyncSaveStatus();
                ArchitectureCheck("repeated-start-failure-stays-dismissed", !_host.SaveWarningVisible && !_cadenceErrorPanel!.Visible);
                ArchitectureCheck("old-manual-still-loads", SaveFileAdapter.LoadSlot(_host.SaveDirectory, SessionHost.ManualSlot, _saveCompatibility).IsSuccess);
            }
            if (frame == 72) ArchitecturePicture("03-live-despite-failed-save");
            if (frame == 80)
            {
                _saveWarningInjectFailure = false;
                SaveWarningClick("RetryDismissedSave");
            }
            if (frame == 84)
            {
                var saved = AutosaveRotation.LoadNewestValid(_host.SaveDirectory, _saveCompatibility);
                ArchitectureCheck("retry-recovers-exact-current-session", saved.IsSuccess && saved.Session!.CaptureSnapshot().AuthoritativeHash == _session.CaptureSnapshot().AuthoritativeHash);
                ArchitectureCheck("successful-retry-clears-status", _host.SaveError is null && !_cadenceErrorLayer!.Visible);
            }
            if (frame == 88) ArchitecturePicture("04-recovered-save");
            if (frame == 96)
            {
                File.WriteAllText(Path.Combine(_saveWarningEvidenceOutput!, "checks.json"), JsonSerializer.Serialize(_architectureChecks, new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("SAVE_WARNING_NATIVE_PASS checks=" + _architectureChecks.Count); GetTree().Quit();
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(_saveWarningEvidenceOutput!, "failure.txt"), error.ToString());
            File.WriteAllText(Path.Combine(_saveWarningEvidenceOutput!, "checks.json"), JsonSerializer.Serialize(_architectureChecks));
            GD.PushError(error.ToString()); GetTree().Quit(2);
        }
    }
}
