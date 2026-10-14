using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Opt-in native UI evidence. Its normal host writes only under the requested evidence directory.
public partial class Main
{
    private string? _developmentTierEvidenceOutput;
    private int _developmentTierEvidenceFrame;
    private readonly List<object> _developmentTierChecks = [];
    private string _developmentTierInitialHash = "", _developmentTierFreshHash = "";
    private string _developmentTierPendingPerks = "";
    private ulong _developmentTierFirstSeed;
    private void TierCheck(string name, bool passed)
    {
        _developmentTierChecks.Add(new { name, passed });
        if (!passed) throw new InvalidOperationException("Development tier evidence failed: " + name);
    }
    private Button TierButton(string name) => FindChildren(name, "Button", true, false).OfType<Button>().First(b => b.IsVisibleInTree());
    private void TierPress(string name) => TierButton(name).EmitSignal(Button.SignalName.Pressed);
    private void TierPicture(string name) => GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_developmentTierEvidenceOutput!, name + ".png"));
    private void TierSend(SessionCommand command)
    {
        var result = _host.Submit(command);
        TierCheck("command-" + command.GetType().Name, result.IsAccepted);
    }
    private void ProcessDevelopmentTierEvidence()
    {
        try { CaptureDevelopmentTierEvidence(); }
        catch (Exception error)
        {
            Directory.CreateDirectory(_developmentTierEvidenceOutput!);
            File.WriteAllText(Path.Combine(_developmentTierEvidenceOutput!, "failure.txt"), error.ToString());
            GD.PushError(error.ToString()); GetTree().Quit(2);
        }
    }
    private void CaptureDevelopmentTierEvidence()
    {
        var frame = _developmentTierEvidenceFrame++;
        if (frame == 0)
        {
            Directory.CreateDirectory(_developmentTierEvidenceOutput!);
            _challengeFlowEnabled = true;
            CloseChallengeReveal(); BuildStartSplash();
            _developmentTierInitialHash = _session.CaptureSnapshot().AuthoritativeHash.ToString();
            TierCheck("isolated-save-directory", _host.SaveDirectory == Path.Combine(_developmentTierEvidenceOutput!, "diagnostic-saves"));
        }
        if (frame == 8)
        {
            TierPicture("01-menu");
            TierCheck("trial-link-replaced", !FindChildren("TryPondStage", "Button", true, false).Any());
            TierPress("DevelopmentTiers");
        }
        if (frame == 16)
        {
            TierPicture("02-development-picker");
            TierCheck("tier-picker-focus", TierButton("DevelopmentTier1").HasFocus());
            TierCheck("only-two-development-tiers", !FindChildren("DevelopmentTier3", "Button", true, false).Any());
            TierCheck("menu-did-not-mutate-session", _developmentTierInitialHash == _session.CaptureSnapshot().AuthoritativeHash.ToString());
            TierPress("DevelopmentBack");
        }
        if (frame == 24)
        {
            TierCheck("back-restores-footer-focus", TierButton("DevelopmentTiers").HasFocus());
            TierPress("StewardShift");
        }
        if (frame == 32)
        {
            TierCheck("steward-picker-retained", TierButton("ShiftTier1").HasFocus() && TierButton("ShiftTier2").IsVisibleInTree());
            TierPicture("03-steward-picker"); TierPress("ShiftBack");
        }
        if (frame == 40) TierPress("DevelopmentTiers");
        if (frame == 48)
        {
            TierPress("DevelopmentTier2");
            _developmentTierFreshHash = _session.CaptureSnapshot().AuthoritativeHash.ToString();
            var p = _session.CapturePreparation()!;
            TierCheck("actual-carried-cash-and-debt", p.CarriedIn is { CashPennies: 35_335, DebtPennies: 20_000 } && p.OpeningCashPennies == 110_335);
            TierCheck("same-as-canonical-recorded-transition", _developmentTierFreshHash == GameSession.CreateRecordedTierTwoStart().CaptureSnapshot().AuthoritativeHash.ToString());
            TierCheck("knowledge-and-actual-relationships", p.SeenActs.Length == 3 && p.ActRelationships!.Select(r => r.Value).SequenceEqual(new[] { -1, 10, 1 }));
            TierCheck("rain-and-normal-trailer-pond-allocation", _session.IsRainyFestival && _session.Stages.Select(s => s.Id).SequenceEqual(new[] { "stage.main", "stage.pond" }));
            TierCheck("fresh-preparation-before-draft", p.Status == PreparationStatus.Preparing && p.BuildPlacements.Length == 0 && _session.CapturePerks()!.Pending);
            _developmentTierPendingPerks = JsonSerializer.Serialize(_session.CapturePerks());
        }
        if (frame == 56)
        {
            TierPicture("04-heavy-rain-reveal");
            TierCheck("heavy-rain-art-before-perks", _challengeReveal.IsOpen && _challengeReveal.Artwork!.Texture.ResourcePath.EndsWith("heavy-rain-a.png", StringComparison.Ordinal)
                && !_session.CapturePreparation()!.ChallengeAcknowledged);
            TierCheck("modal-has-keyboard-focus-and-hides-hud", _challengeReveal.Continue!.HasFocus() && _challengeHiddenLayers.All(l => !l.Layer.Visible));
            TierCheck("reveal-did-not-change-authority", _developmentTierFreshHash == _session.CaptureSnapshot().AuthoritativeHash.ToString());
            TierPress("ChallengeMenu");
        }
        if (frame == 64)
        {
            TierCheck("reveal-menu-keeps-state", StartMenu.IsOpen && !_challengeReveal.IsOpen && _developmentTierFreshHash == _session.CaptureSnapshot().AuthoritativeHash.ToString());
            TierPress("EnterFestival");
        }
        if (frame == 72)
        {
            TierCheck("management-resumes-pending-reveal", _challengeReveal.IsOpen && _developmentTierFreshHash == _session.CaptureSnapshot().AuthoritativeHash.ToString());
            TierPress("PrepareFestival");
        }
        if (frame == 80)
        {
            TierPicture("05-preparation-draft");
            TierCheck("continue-acknowledges-with-same-perk-draw", !_challengeReveal.IsOpen && _session.CapturePreparation()!.ChallengeAcknowledged
                && _developmentTierPendingPerks == JsonSerializer.Serialize(_session.CapturePerks()) && Perks.Panel!.IsVisibleInTree());
            var perk = _session.CapturePerks()!;
            TierSend(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
            TierSend(new UseDefaultBuildLayoutCommand());
            var acts = _session.GetFestivalActs().Where(a => _session.ActStandingOf(a) == ActStanding.Available)
                .OrderBy(_session.ActFee).ThenBy(a => a.Id, StringComparer.Ordinal).Select(a => a.Id).ToArray();
            TierSend(new SetProgrammeCommand(acts[..3]));
            TierSend(new SetProgrammeCommand(acts[3..6]) { StageId = FestivalStages.PondId });
            TierSend(new SetPreparationStockCommand(40, 32));
            var crew = _session.GetStaffCandidates().GroupBy(c => c.Role).Select(g => g.OrderBy(c => c.Traits.Length > 0)
                .ThenBy(c => c.Grade > 0).ThenBy(c => Math.Abs(c.Grade)).ThenBy(c => c.Id, StringComparer.Ordinal).First()).ToArray();
            foreach (var person in crew) TierSend(new AcceptPreparationOfferCommand(person.Id));
            var secondSound = _session.GetStaffCandidates().Where(c => c.Role == StaffRole.Sound && !crew.Any(p => p.Id == c.Id))
                .OrderBy(c => c.Traits.Length > 0).ThenBy(c => c.Grade > 0).ThenBy(c => Math.Abs(c.Grade)).ThenBy(c => c.Id, StringComparer.Ordinal).First();
            TierSend(new AcceptPreparationOfferCommand(secondSound.ExtraOfferId));
            TierCheck("ordinary-plan-affordable-and-ready", _session.GetPreparationStartBlockers().Count == 0 && _session.PreparationPlanCost < 110_335);
            RefreshPreparationHud();
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            var save = _host.SaveManual(); TierCheck("manual-save", save.IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(_host.SaveDirectory, SessionHost.ManualSlot, _saveCompatibility);
            TierCheck("actual-save-file-roundtrip", loaded.IsSuccess && loaded.Session!.CaptureSnapshot().AuthoritativeHash == hash);
        }
        if (frame == 88)
        {
            TierPicture("06-tier-two-box-office");
            TierCheck("box-office-tier-label", FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.IsVisibleInTree() && l.Text == "ADMIT ONE · TIER 2"));
            FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.IsVisibleInTree() && b.Text == "Start planning").EmitSignal(Button.SignalName.Pressed);
        }
        if (frame == 96)
        {
            TierPicture("07-two-stage-plan");
            BuildStartSplash();
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            TierPress("EnterFestival");
            TierCheck("ordinary-menu-resume-preserves-plan", !_challengeReveal.IsOpen && hash == _session.CaptureSnapshot().AuthoritativeHash);
            BuildStartSplash();
        }
        if (frame == 104) TierPress("DevelopmentTiers");
        if (frame == 112)
        {
            TierPress("DevelopmentTier2");
            TierCheck("tier-two-restart-resets-all-edits", _developmentTierFreshHash == _session.CaptureSnapshot().AuthoritativeHash.ToString() && _challengeReveal.IsOpen);
            CloseChallengeReveal(); BuildStartSplash();
        }
        if (frame == 120) TierPress("DevelopmentTiers");
        if (frame == 128)
        {
            TierPress("DevelopmentTier1");
            var p = _session.CapturePreparation()!;
            _developmentTierFirstSeed = _session.CampaignSeed;
            TierCheck("tier-one-is-a-real-fresh-campaign", p.Tier == 1 && p.CarriedIn is null && p.OpeningCashPennies == 45_000
                && p.SeenActs.Length == 0 && p.ActRelationships is null && p.Reputation == 0 && !_session.PondStageOpen && _challengeReveal.IsOpen);
        }
        if (frame == 136)
        {
            TierPicture("08-fresh-tier-one-reveal");
            TierCheck("tier-one-heatwave-art", _challengeReveal.Artwork!.Texture.ResourcePath.EndsWith("heatwave-b.png", StringComparison.Ordinal));
            CloseChallengeReveal(); BuildStartSplash();
        }
        if (frame == 144) TierPress("DevelopmentTiers");
        if (frame == 152)
        {
            TierPress("DevelopmentTier1");
            TierCheck("fresh-tier-one-has-new-identity", _session.CampaignSeed != _developmentTierFirstSeed);
            // Exercise the existing parked-host return without opening either user-owned mode save directory.
            CloseChallengeReveal(); ParkCampaignHost();
            var classic = _campaignHost;
            _host = new SessionHost(GameSession.CreateHandsOnCampaign(20260922), Path.Combine(_developmentTierEvidenceOutput!, "diagnostic-hands-on"), _saveCompatibility);
            StartDevelopmentTier(2);
            TierCheck("development-restores-management-host", ReferenceEquals(classic, _host) && _campaignHost is null && !_session.IsHandsOn && !_shiftActive);
        }
        if (frame == 160)
        {
            File.WriteAllText(Path.Combine(_developmentTierEvidenceOutput!, "development-tier-evidence.json"), JsonSerializer.Serialize(new
            {
                passed = true, executable = OS.GetExecutablePath(), checks = _developmentTierChecks,
                openingCashPennies = 110_335, referenceSeed = 20260922, referenceStars = 2,
                caveat = "Scripted native buttons and normal preparation/save flow. No manual playthrough; Council failure/retry and ordinary progression exercised by simulation regressions."
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("DEVELOPMENT_TIER_EVIDENCE_COMPLETE"); GetTree().Quit();
        }
    }
}
