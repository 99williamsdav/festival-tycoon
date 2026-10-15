using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Opt-in native smoke: real menu handlers and normal commands, with all three save hosts isolated.
public partial class Main
{
    private string? _architectureEvidenceOutput;
    private int _architectureFrame;
    private readonly List<object> _architectureChecks = [];
    private readonly List<object> _architectureMovement = [];
    private string _architectureParkedHash = "";
    private void ArchitectureCheck(string name, bool passed)
    {
        _architectureChecks.Add(new { name, passed });
        if (!passed) throw new InvalidOperationException("Architecture native evidence failed: " + name);
    }
    private void ArchitecturePress(string name) => FindChildren(name, "Button", true, false).OfType<Button>()
        .First(b => b.IsVisibleInTree()).EmitSignal(Button.SignalName.Pressed);
    private void ArchitecturePicture(string name) => GetViewport().GetTexture().GetImage()
        .SavePng(Path.Combine(_architectureEvidenceOutput!, name + ".png"));
    private void ArchitectureSend(SessionCommand command) => ArchitectureCheck("command-" + command.GetType().Name, _host.Submit(command).IsAccepted);
    private void ArchitectureSave(string name)
    {
        var hash = _session.CaptureSnapshot().AuthoritativeHash;
        var save = SaveFileAdapter.SaveSlot(_host.SaveDirectory, name, new(_session, _saveCompatibility, "native-smoke", DateTimeOffset.UtcNow));
        var loaded = SaveFileAdapter.LoadSlot(_host.SaveDirectory, name, _saveCompatibility);
        ArchitectureCheck(name + "-file-roundtrip", save.IsSuccess && loaded.IsSuccess && loaded.Session!.Identity == _session.Identity
            && loaded.Session.CaptureSnapshot().AuthoritativeHash == hash);
    }
    private void ArchitectureReady()
    {
        var perk = _session.CapturePerks()!;
        ArchitectureSend(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
        ArchitectureSend(new UseDefaultBuildLayoutCommand());
        var acts = _session.GetFestivalActs().Where(a => _session.ActStandingOf(a) == ActStanding.Available)
            .OrderBy(_session.ActFee).ThenBy(a => a.Id, StringComparer.Ordinal).Select(a => a.Id).ToArray();
        ArchitectureSend(new SetProgrammeCommand(acts[..3]));
        if (_session.Stages.Count > 1) ArchitectureSend(new SetProgrammeCommand(acts[3..6]) { StageId = _session.Stages[1].Id });
        ArchitectureSend(new SetPreparationStockCommand(40, 32));
        var crew = _session.GetStaffCandidates().GroupBy(c => c.Role).Select(g => g.OrderBy(c => c.Traits.Length > 0)
            .ThenBy(c => c.Grade > 0).ThenBy(c => Math.Abs(c.Grade)).ThenBy(c => c.Id, StringComparer.Ordinal).First()).ToArray();
        foreach (var person in crew) ArchitectureSend(new AcceptPreparationOfferCommand(person.Id));
        if (_session.Stages.Count > 1)
        {
            var extra = _session.GetStaffCandidates().Where(c => c.Role == StaffRole.Sound && !crew.Any(p => p.Id == c.Id))
                .OrderBy(c => c.Traits.Length > 0).ThenBy(c => c.Grade > 0).ThenBy(c => Math.Abs(c.Grade)).ThenBy(c => c.Id, StringComparer.Ordinal).First();
            ArchitectureSend(new AcceptPreparationOfferCommand(extra.ExtraOfferId));
        }
        ArchitectureCheck("normal-plan-ready", _session.GetPreparationStartBlockers().Count == 0);
        ArchitectureSend(new StartPreparedEditionCommand());
        _hudWorkspaceOpen = _buildDrawerOpen = false; _boxOfficeSeenAttempt = 1;
        _session.AdvanceWithoutSnapshot(8000); _foundationPresentation.Reset(_session.CaptureObservation());
        BuildAttendee(); ArchitectureSend(new SetPausedCommand(true)); RefreshPreparationHud();
    }
    private void ArchitectureWalk(string name)
    {
        var id = new EntityId(_session.PlayerStewardId!.Value);
        var before = _session.CaptureObservation().NavigationAgents.First(a => a.Id == id);
        ArchitectureCheck(name + "-running-manual-control", _session.PreparedStatus == PreparationStatus.Running && _shiftActive
            && _session.CaptureStewardShift()!.Autopilot is null);
        ArchitectureSend(new SetPausedCommand(false));
        // A fresh autopilot can stop beside a wall or person. Steer normally, without assuming east is clear.
        foreach (var (x, z) in new[] { (1000, 0), (-1000, 0), (0, -1000), (0, 1000), (707, -707), (-707, -707), (707, 707), (-707, 707) })
        {
            var start = _session.CaptureObservation().NavigationAgents.First(a => a.Id == id); var tick = _session.CurrentTick;
            var facing = StewardShiftRules.FacingTowards(x, z);
            ArchitectureSend(new SetPlayerMoveCommand(x, z, false, facing)); _session.AdvanceWithoutSnapshot(80);
            ArchitectureSend(new SetPlayerMoveCommand(0, 0, false, facing));
            var end = _session.CaptureObservation().NavigationAgents.First(a => a.Id == id);
            _architectureMovement.Add(new { name, seed = _session.CampaignSeed, directionX = x, directionZ = z,
                startTick = tick, endTick = _session.CurrentTick, beforeX = start.XMillimetres, beforeZ = start.ZMillimetres,
                afterX = end.XMillimetres, afterZ = end.ZMillimetres });
            ArchitectureCheck(name + "-manual-tick-step", _session.CurrentTick == tick + 80 && _session.PreparedStatus == PreparationStatus.Running);
            if (start.XMillimetres != end.XMillimetres || start.ZMillimetres != end.ZMillimetres) break;
        }
        ArchitectureSend(new SetPausedCommand(true));
        var after = _session.CaptureObservation().NavigationAgents.First(a => a.Id == id);
        ArchitectureCheck(name + "-manual-controller-moved", before.XMillimetres != after.XMillimetres || before.ZMillimetres != after.ZMillimetres);
        var cell = TraversalGrid.WorldToCell(after.XMillimetres, after.ZMillimetres);
        ArchitectureCheck(name + "-manual-position-on-open-field", _session.TraversalGrid!.Get(cell).IsWalkable && StewardShiftRules.InField(cell));
        _foundationPresentation.Reset(_session.CaptureObservation()); ArchitectureSave(name);
    }
    private void ProcessArchitectureEvidence()
    {
        try { CaptureArchitectureEvidence(); }
        catch (Exception error)
        {
            Directory.CreateDirectory(_architectureEvidenceOutput!);
            File.WriteAllText(Path.Combine(_architectureEvidenceOutput!, "failure.txt"), error.ToString());
            File.WriteAllText(Path.Combine(_architectureEvidenceOutput!, "failure-checks.json"), JsonSerializer.Serialize(_architectureChecks));
            File.WriteAllText(Path.Combine(_architectureEvidenceOutput!, "movement.json"), JsonSerializer.Serialize(_architectureMovement));
            File.WriteAllText(Path.Combine(_architectureEvidenceOutput!, "failure-state.json"), JsonSerializer.Serialize(_session.CapturePersistenceSnapshot()));
            GD.PushError(error.ToString()); GetTree().Quit(2);
        }
    }
    private void CaptureArchitectureEvidence()
    {
        var frame = _architectureFrame++;
        if (frame == 0)
        {
            Directory.CreateDirectory(_architectureEvidenceOutput!); _challengeFlowEnabled = true; CloseChallengeReveal(); BuildStartSplash();
            ArchitectureCheck("management-save-isolated", _host.SaveDirectory == Path.Combine(_architectureEvidenceOutput!, "management-saves"));
            ArchitectureCheck("survival-and-classic-rulesets-implemented", Enum.GetValues<CampaignRuleset>().SequenceEqual(new[] { CampaignRuleset.Survival,CampaignRuleset.Classic }));
        }
        if (frame == 8) { ArchitecturePicture("01-existing-control-menu"); ArchitecturePress("DevelopmentTiers"); }
        if (frame == 16)
        {
            ArchitecturePress("DevelopmentTier2");
            ArchitectureCheck("recorded-management-tier-two", _session.Identity == CampaignIdentity.Management && _session.CurrentFestival.Tier == 2
                && _session.CapturePersistentCampaignState()!.CashPennies == 110335 && _session.Stages.Select(s => s.Id).SequenceEqual(new[] { "stage.main", "stage.barn" }));
        }
        if (frame == 24) { ArchitecturePicture("02-existing-rain-challenge"); ArchitecturePress("PrepareFestival"); }
        if (frame == 32) { ArchitectureReady(); ArchitectureSave("management-tier-two"); _rig.Frame(new(0, 3.5f, -23), 36); }
        if (frame == 48) { ArchitecturePicture("03-management-tier-two-live"); _architectureParkedHash = _session.CaptureSnapshot().AuthoritativeHash.ToString(); BuildStartSplash(); }
        if (frame == 56) ArchitecturePress("FrontOfHouse");
        if (frame == 64)
        {
            ArchitectureCheck("all-rounder-identity-and-isolated-save", _session.Identity == CampaignIdentity.HandsOn
                && _host.SaveDirectory == Path.Combine(_architectureEvidenceOutput!, "hands-on-saves"));
            ArchitecturePress("PrepareFestival");
        }
        if (frame == 72) { ArchitectureReady(); ProcessHandsOnHint(); }
        if (frame == 80)
        {
            ArchitecturePicture("04-all-rounder-management");
            ArchitectureCheck("all-rounder-manager-autopilot", !HandsOnFirstPerson && _session.CaptureStewardShift()!.Autopilot is not null && _session.HasManagementCapability);
            ToggleHandsOnView();
            ArchitectureCheck("all-rounder-first-person-manual", HandsOnFirstPerson && EyeViewActive && _session.CaptureStewardShift()!.Autopilot is null);
            SetShiftMenu(null); ArchitectureWalk("all-rounder-manual");
        }
        if (frame == 96)
        {
            ArchitecturePicture("05-all-rounder-first-person"); LeaveHandsOnFirstPerson();
            ArchitectureCheck("all-rounder-returned-autopilot", !HandsOnFirstPerson && _session.CaptureStewardShift()!.Autopilot is not null);
            BuildStartSplash();
        }
        if (frame == 104)
        {
            ArchitecturePress("EnterFestival");
            ArchitectureCheck("parked-management-resumed-after-all-rounder", _session.Identity == CampaignIdentity.Management
                && _architectureParkedHash == _session.CaptureSnapshot().AuthoritativeHash.ToString()); BuildStartSplash();
        }
        if (frame == 112) ArchitecturePress("StewardShift");
        if (frame == 120) ArchitecturePress("ShiftTier1");
        if (frame == 128)
        {
            ArchitectureCheck("steward-only-tier-one-identity-and-save", _session.Identity == CampaignIdentity.StewardOnly && !_session.HasManagementCapability
                && _session.CurrentFestival.Tier == 1 && _shiftActive && _host.SaveDirectory == Path.Combine(_architectureEvidenceOutput!, "steward-saves"));
            ArchitectureWalk("steward-tier-one");
        }
        if (frame == 144) { ArchitecturePicture("06-steward-only-tier-one"); ReturnFromStewardShift(); }
        if (frame == 152) ArchitecturePress("StewardShift");
        if (frame == 160) ArchitecturePress("ShiftTier2");
        if (frame == 168)
        {
            ArchitectureCheck("steward-only-tier-two-allocation", _session.Identity == CampaignIdentity.StewardOnly && _session.CurrentFestival.Tier == 2
                && _session.IsRainyFestival && _session.Stages.Select(s => s.Id).SequenceEqual(new[] { "stage.main", "stage.barn" }));
            ArchitectureWalk("steward-tier-two");
        }
        if (frame == 184) { ArchitecturePicture("07-steward-only-tier-two"); ReturnFromStewardShift(); }
        if (frame == 192)
        {
            ArchitecturePress("EnterFestival");
            ArchitectureCheck("parked-management-resumed-after-steward", _session.Identity == CampaignIdentity.Management
                && _architectureParkedHash == _session.CaptureSnapshot().AuthoritativeHash.ToString() && !_shiftActive);
            ArchitectureSave("resumed-management");
        }
        if (frame == 208)
        {
            ArchitectureCheck("resumed-management-hides-steward-view-cues", _managerCue?.Visible != true && _firstPersonCue?.Visible != true
                && _youBadge?.Visible != true && _youTag?.Visible != true);
            ArchitecturePicture("08-resumed-management");
            File.WriteAllText(Path.Combine(_architectureEvidenceOutput!, "architecture-evidence.json"), JsonSerializer.Serialize(new
            {
                passed = true, executable = OS.GetExecutablePath(), checks = _architectureChecks, movement = _architectureMovement,
                caveat = "Scripted actual native menu handlers, normal preparation, view switching, bounded ordinary manual steering (up to eight 80-tick attempts) and real isolated save files; no manual full-day playthrough or performance claim."
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("SURVIVAL_ARCHITECTURE_EVIDENCE_COMPLETE"); GetTree().Quit(); return;
        }
        if (!StartMenu.IsOpen && !_challengeReveal.IsOpen && _session.CaptureObservation().NavigationAgents.Count > 0)
        {
            ProcessDayCycle(1.0 / 30); ProcessRain(); ProcessPondStage(1.0 / 30); ProcessBarnStage(1.0 / 30);
            AdvancePreparationPresentation(1.0 / 30); SyncStageNowPlaying();
            if (_shiftActive) ProcessStewardShiftView();
        }
        ProcessHandsOnHint();
    }
}
