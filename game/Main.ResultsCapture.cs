using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private string? _resultsCaptureDirectory;
    private bool _resultsNewGameCapture;
    private int _resultsCaptureFrame;
    private GameSession? _resultsNatural;
    private byte[]? _resultsTerminalSaveBytes;
    private ulong _resultsTerminalCampaignId;
    private ulong _resultsFirstNewCampaignId;
    private string _resultsShot = "";
    private Label? _resultsFixtureLabel;
    private void ResultsCheck(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void ResultsSetPreparation(PreparationSnapshot p) => typeof(GameSession).GetField("_preparation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_session, p);
    private void ResultsShot(string name, string label)
    {
        AdvancePreparationPresentation(0);
        _festivalPaper?.QueueFree(); _festivalPaper = null; RefreshPreparationHud();
        _resultsFixtureLabel!.Text = label; _resultsShot = name;
    }
    private void ProcessResultsCapture()
    {
        if (_resultsCaptureDirectory is null) return;
        try
        {
            var frame = ++_resultsCaptureFrame;
            if (frame == 1)
            {
                var unfinished = _session;
                var unfinishedHash = _session.CaptureSnapshot().AuthoritativeHash;
                BuildStartSplash();
                ((_startSplash!.FindChild("EnterFestival", true, false) as Button) ?? throw new InvalidOperationException("Initial Enter missing")).EmitSignal(Button.SignalName.Pressed);
                ResultsCheck(ReferenceEquals(unfinished, _session) && _session.CaptureSnapshot().AuthoritativeHash == unfinishedHash && _startSplash is null,
                    "Unfinished splash entry unexpectedly reset campaign");
                var overlay = new CanvasLayer { Layer = 30 }; AddChild(overlay);
                _resultsFixtureLabel = new Label { Position = new Vector2(12, 3) }; _resultsFixtureLabel.AddThemeFontSizeOverride("font_size", 13); overlay.AddChild(_resultsFixtureLabel);
                var perk = _session.CapturePerks()!;
                void Accept(SessionCommand command) { var r = _session.Execute(CampaignEnvelope(command)); ResultsCheck(r.IsAccepted, r.Message); }
                Accept(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
                Accept(new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"]));
                Accept(new AcceptPreparationOfferCommand("staff.engineer")); Accept(new AcceptPreparationOfferCommand("equipment.buy"));
                Accept(new PurchaseImmersionStarterStockCommand()); Accept(new StartPreparedEditionCommand());
                BuildAttendee(); _session.AdvanceWithoutSnapshot(_session.PreparedEditionDurationTicks); _foundationPresentation.Reset(_session.CaptureObservation());
                ResultsCheck(_session.PreparedStatus == PreparationStatus.Departing && _session.CompletedFestivalResult is null, "Natural closing must keep live world without report");
                PreparationSave(); PreparationLoad(); ResultsCheck(_session.PreparedStatus == PreparationStatus.Departing, "Actual departing file reload failed");
                ResultsShot("01-natural-departing-reloaded", "NATURAL TIMETABLE · accelerated deterministic ticks · actual file reload · guests still leaving");
            }
            if (frame == 5)
            {
                _session.AdvanceWithoutSnapshot(15000); _foundationPresentation.Reset(_session.CaptureObservation());
                ResultsCheck(_session.PreparedStatus == PreparationStatus.Finished && _session.CompletedFestivalResult is not null, "Natural departure did not complete");
                var hash = _session.CaptureSnapshot().AuthoritativeHash; _session.AdvanceWithoutSnapshot(100); ResultsCheck(hash == _session.CaptureSnapshot().AuthoritativeHash, "Finished scheduler progressed");
                PreparationSave(); PreparationLoad(); _resultsNatural = _session;
                ResultsShot("02-natural-complete-reloaded", "NATURAL SAFE COMPLETION · physical protected-person exits · actual terminal file reload");
                GD.Print($"RESULTS_NATURAL hash={hash} count={_session.CompletedFestivalResult!.GuestCount} stars={_session.CompletedFestivalResult.Stars} frozen=True reload=True");
            }
            if (frame == 2)
            {
                var image = GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(_resultsCaptureDirectory, "00-natural-closing.png"));
            }
            if (frame == 3)
            {
                var ticks = 0;
                while (_session.CapturePreparation()!.People.Count(p => p.Role == ProtectedPersonRole.Guest && !p.Departed) > 1 && ticks++ < 15000) _session.AdvanceWithoutSnapshot(1);
                ResultsCheck(_session.CapturePreparation()!.People.Count(p => p.Role == ProtectedPersonRole.Guest && !p.Departed) == 1 && _session.CompletedFestivalResult is null, "Last guest must gate report");
                _foundationPresentation.Reset(_session.CaptureObservation());
                ResultsShot("01-natural-last-guest", "NATURAL LAST GUEST · report gated on actual physical exit · staff/medic tail retained");
            }
            if (frame == 6)
            {
                ResultsCheck(_attendeePickRegistry.Count == 0 && _attendeeVisuals.Values.All(visual => !visual.Visible), "Departed people retained render or picker eligibility after reload");
                GD.Print("RESULTS_DEPARTED visuals_hidden=True picker_registry_empty=True historical_people_retained=True");
                var image = GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(_resultsCaptureDirectory, "02-natural-complete-reloaded.png"));
            }
            if (frame == 7)
            {
                _resultsScroll!.ScrollVertical = 100000; _resultsShot = "02b-natural-facts-scroll";
            }
            if (frame is 9 or 13 or 17)
            {
                _session = GameSession.Restore(_resultsNatural!.CapturePersistenceSnapshot()).Session!;
                var satisfaction = frame == 9 ? 1000 : frame == 13 ? 5000 : 9000;
                var p = _session.CapturePreparation()!;
                var guests = p.People.Count(person => person.Role == ProtectedPersonRole.Guest && person.Admitted && person.Departed);
                ResultsSetPreparation(p with { People = p.People.Select(person => person.Role == ProtectedPersonRole.Guest ? person with { Satisfaction = satisfaction } : person).ToArray(),
                    Result = p.Result! with { SatisfactionTotal = (long)satisfaction * guests } });
                ResultsShot($"0{(frame == 9 ? 3 : frame == 13 ? 4 : 5)}-initialized-{(frame == 9 ? 1 : frame == 13 ? 3 : 5)}star", "INITIALIZED RATING FIXTURE · satisfaction changed only for threshold/layout diagnostic · actual accounts retained");
            }
            if (frame == 21)
            {
                var p = _session.CapturePreparation()!;
                ResultsSetPreparation(p with { Result = p.Result! with { GuestCount = 0, SatisfactionTotal = 0, BeersFinished = null, Fights = null, MedicalCollapses = null } });
                ResultsShot("06-initialized-unrated-missing", "INITIALIZED PRESENTATION FIXTURE · no guests / legacy metrics unavailable · not a saved gameplay outcome");
            }
            if (frame == 25)
            {
                _session = GameSession.Restore(_resultsNatural!.CapturePersistenceSnapshot()).Session!;
                var p = _session.CapturePreparation()!; var id = _session.CaptureMedical()!.AtRiskGuestId;
                ResultsSetPreparation(p with { Status = PreparationStatus.Departing, Result = null, People = p.People.Select(person => person.AgentId == id ? person with { Departed = false } : person).ToArray() });
                typeof(GameSession).GetMethod("ApplyMedicalDeath", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(_session, [id, _session.CurrentTick - 4000, _session.CurrentTick - 2400, _session.CurrentTick - 1600]);
                ResultsCheck(_session.CompletedFestivalResult is null && _session.PreparedStatus == PreparationStatus.Failed, "Council must bypass paper");
                ResultsShot("07-initialized-council-bypass", "INITIALIZED FATAL MEDICAL FIXTURE · Council bypass · no success report");
            }
            if (frame == 29 && !_resultsNewGameCapture)
            {
                _session = _resultsNatural!; _festivalPaper?.QueueFree(); _festivalPaper = null; RefreshHearingHud();
                BuildStartSplash(); _resultsFixtureLabel!.Text = "TERMINAL MENU ROUTE · completed run stays frozen · no progression/reset"; _resultsShot = "07-menu";
            }
            if (frame == 29 && _resultsNewGameCapture)
            {
                _session = _resultsNatural!; _festivalPaper?.QueueFree(); _festivalPaper = null; RefreshPreparationHud();
                _resultsTerminalCampaignId = _session.CampaignId.Value;
                _resultsTerminalSaveBytes = File.ReadAllBytes(SaveFileAdapter.ResolveSlotPath(SaveDirectory, "manual-preparation"));
                var terminalHash = _session.CaptureSnapshot().AuthoritativeHash;
                ((_festivalPaper!.FindChild("ReturnToMenu", true, false) as Button) ?? throw new InvalidOperationException("Return button missing")).EmitSignal(Button.SignalName.Pressed);
                ResultsCheck(_newCampaignOnEnter && _startSplash is not null && _session.CaptureSnapshot().AuthoritativeHash == terminalHash,
                    "Return must preserve frozen session and show pending fresh-start menu");
                RefreshPreparationHud(); ResultsCheck(_festivalPaper is null, "Newspaper reopened behind menu");
                ResultsCheck(_resultsTerminalSaveBytes.SequenceEqual(File.ReadAllBytes(SaveFileAdapter.ResolveSlotPath(SaveDirectory, "manual-preparation"))), "Return changed terminal save bytes");
                _resultsFixtureLabel!.Text = "TERMINAL MENU ROUTE · completed run preserved until Enter · no paper behind splash"; _resultsShot = "07-menu";
            }
            if (frame == 33 && _resultsNewGameCapture)
            {
                ((_startSplash!.FindChild("EnterFestival", true, false) as Button) ?? throw new InvalidOperationException("Enter button missing")).EmitSignal(Button.SignalName.Pressed);
                _resultsFirstNewCampaignId = _session.CampaignId.Value;
                ResultsCheck(!_newCampaignOnEnter && _startSplash is null && _festivalPaper is null && _resultsFirstNewCampaignId != _resultsTerminalCampaignId,
                    "Enter did not create distinct fresh campaign");
                ResultsCheck(_session.CurrentTick == 0 && _session.PreparedStatus == PreparationStatus.Preparing && _session.CompletedFestivalResult is null &&
                    _session.CapturePerks() is { Pending: true, Equipped.Length: 0 } && _session.CapturePreparationPlan() is { Committed: false },
                    "Fresh campaign skipped draft/preparation or retained terminal state");
                ResultsCheck(_session.CaptureSnapshot().AuthoritativeHash == GameSession.CreateBookingCampaign(_session.CampaignSeed).CaptureSnapshot().AuthoritativeHash,
                    "Fresh session differs from complete new-campaign factory (funds, favours, draft, people or other state retained)");
                ResultsCheck(_resultsTerminalSaveBytes!.SequenceEqual(File.ReadAllBytes(SaveFileAdapter.ResolveSlotPath(SaveDirectory, "manual-preparation"))), "Enter changed terminal save bytes");
                _resultsFixtureLabel!.Text = "NEW CAMPAIGN · distinct identity · fresh perk draft and unpaid preparation · no newspaper";
                _resultsShot = "08-fresh-preparation";
                GD.Print($"RESULTS_NEW_CAMPAIGN terminal={_resultsTerminalCampaignId} fresh={_resultsFirstNewCampaignId} save_unchanged=True");
            }
            if (frame == 35 && _resultsNewGameCapture)
            {
                var viewport = GetViewport().GetVisibleRect();
                var panel = _perkPanel!.GetGlobalRect();
                bool Fits(Rect2 rect) => rect.Position.X >= 0 && rect.Position.Y >= 0 &&
                    rect.End.X <= viewport.End.X && rect.End.Y <= viewport.End.Y &&
                    rect.Position.X >= panel.Position.X && rect.Position.Y >= panel.Position.Y &&
                    rect.End.X <= panel.End.X && rect.End.Y <= panel.End.Y;
                ResultsCheck(_perkPanel.Visible && Fits(panel) && panel.Size.Y >= viewport.Size.Y - 125,
                    "Fresh draft panel retained completed-session owned-strip layout");
                foreach (var id in _session.CapturePerks()!.Hand)
                {
                    var card = _perkBody!.FindChild("DraftPerkCard_" + id, true, false) as Control;
                    var choose = _perkBody.FindChild("ChooseDraftPerk_" + id, true, false) as Button;
                    ResultsCheck(card is not null && choose is not null && Fits(card.GetGlobalRect()) && Fits(choose.GetGlobalRect()) &&
                        choose.IsVisibleInTree(), "Fresh draft card or Choose action is clipped: " + id);
                }
                ResultsCheck(_perkScroll!.ScrollVertical == 0, "Fresh draft actions require an unexpected scroll");
                GD.Print("RESULTS_NEW_CAMPAIGN draft_cards_actions_fit=True");
            }
            if (frame == 37 && _resultsNewGameCapture)
            {
                var firstChoice = _session.CapturePerks()!.Hand[0];
                ((_perkBody!.FindChild("ChooseDraftPerk_" + firstChoice, true, false) as Button) ??
                    throw new InvalidOperationException("Fresh draft Choose action missing")).EmitSignal(Button.SignalName.Pressed);
                ResultsCheck(_session.CapturePerks() is { Pending: false, Equipped.Length: 1 },
                    "Fresh draft Choose action did not commit the choice");
            }
            if (frame == 39 && _resultsNewGameCapture)
            {
                var viewport = GetViewport().GetVisibleRect();
                bool Fits(Rect2 rect) => rect.Position.X >= 0 && rect.Position.Y >= 0 &&
                    rect.End.X <= viewport.End.X && rect.End.Y <= viewport.End.Y;
                ResultsCheck(_hudTabs!.CurrentTab == 1 && _hudWorkspace!.IsVisibleInTree() && _bookingLane!.IsVisibleInTree() &&
                    _preparationStart.IsVisibleInTree() && _hudStartReason!.IsVisibleInTree() &&
                    _hudStartReason.Text.Contains('£') && Fits(_hudWorkspace.GetGlobalRect()) &&
                    Fits(_preparationStart.GetGlobalRect()) && Fits(_hudStartReason.GetGlobalRect()),
                    "Choosing a fresh perk did not reveal usable in-viewport Programme/cost/Start controls");
                _resultsFixtureLabel!.Text = "FRESH PROGRAMME · selected perk committed · cost and Start in viewport";
                _resultsShot = "09-fresh-programme";
                GD.Print("RESULTS_NEW_CAMPAIGN programme_cost_start_fit=True");
            }
            if (frame == 43 && _resultsNewGameCapture)
            {
                // An incompatible adapter read is pure; a compatible explicit
                // completed-file load remains available from the menu slot.
                var fresh = _session;
                var freshHash = fresh.CaptureSnapshot().AuthoritativeHash;
                var incompatible = SaveFileAdapter.LoadSlot(SaveDirectory, "manual-preparation",
                    new SaveCompatibility("r0.05p-incompatible-fixture", "wrong-content", "wrong-rules"));
                ResultsCheck(!incompatible.IsSuccess && ReferenceEquals(fresh, _session) &&
                    _session.CaptureSnapshot().AuthoritativeHash == freshHash && _festivalPaper is null,
                    "Incompatible completed-save read changed the fresh campaign");
                PreparationLoad();
                ResultsCheck(_session.CompletedFestivalResult is not null && _festivalPaper is not null,
                    "Compatible explicit completed-save load did not restore newspaper");
                // A second actual menu/enter cycle must not reuse the first fresh identity.
                ((_festivalPaper!.FindChild("ReturnToMenu", true, false) as Button) ?? throw new InvalidOperationException("Second Return missing")).EmitSignal(Button.SignalName.Pressed);
                ((_startSplash!.FindChild("EnterFestival", true, false) as Button) ?? throw new InvalidOperationException("Second Enter missing")).EmitSignal(Button.SignalName.Pressed);
                ResultsCheck(_session.CampaignId.Value != _resultsTerminalCampaignId && _session.CampaignId.Value != _resultsFirstNewCampaignId &&
                    _session.CurrentTick == 0 && _session.CompletedFestivalResult is null && _festivalPaper is null,
                    "Repeated completed-menu cycle did not create a fresh campaign");
                ResultsCheck(_resultsTerminalSaveBytes!.SequenceEqual(File.ReadAllBytes(SaveFileAdapter.ResolveSlotPath(SaveDirectory, "manual-preparation"))), "Second cycle changed terminal save bytes");
                GD.Print("RESULTS_NEW_CAMPAIGN repeated=True incompatible_read_unchanged=True explicit_terminal_load=True terminal_save_unchanged=True");
            }
            if (frame % 4 == 0 && _resultsShot != "")
            {
                var image = GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(_resultsCaptureDirectory, _resultsShot + ".png"));
                GD.Print($"RESULTS_CAPTURE image={_resultsShot} size={image.GetWidth()}x{image.GetHeight()}"); _resultsShot = "";
            }
            if (frame == 33 && !_resultsNewGameCapture) { GD.Print("RESULTS_CAPTURE completed natural_exit_reload_freeze=True last_guest_gate=True initialized_ratings_labelled=True council_bypass=True menu=True"); GetTree().Quit(); }
            if (frame == 47 && _resultsNewGameCapture) { GD.Print("RESULTS_CAPTURE completed natural_exit_reload_freeze=True last_guest_gate=True initialized_ratings_labelled=True council_bypass=True new_campaign=True"); GetTree().Quit(); }
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
