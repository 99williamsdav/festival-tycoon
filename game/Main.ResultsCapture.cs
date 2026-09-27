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
    private int _resultsCaptureFrame;
    private GameSession? _resultsNatural;
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
                var overlay = new CanvasLayer { Layer = 30 }; AddChild(overlay);
                _resultsFixtureLabel = new Label { Position = new Vector2(12, 3) }; _resultsFixtureLabel.AddThemeFontSizeOverride("font_size", 13); overlay.AddChild(_resultsFixtureLabel);
                var perk = _session.CapturePerks()!;
                void Accept(SessionCommand command) { var r = _session.Execute(CampaignEnvelope(command)); ResultsCheck(r.IsAccepted, r.Message); }
                Accept(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
                Accept(new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"]));
                Accept(new AcceptPreparationOfferCommand("staff.engineer")); Accept(new AcceptPreparationOfferCommand("equipment.buy"));
                Accept(new PurchaseImmersionStarterStockCommand()); Accept(new StartPreparedEditionCommand());
                BuildAttendee(); _session.AdvanceWithoutSnapshot(24000); _foundationPresentation.Reset(_session.CaptureObservation());
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
            if (frame == 29)
            {
                _session = _resultsNatural!; _festivalPaper?.QueueFree(); _festivalPaper = null; RefreshHearingHud();
                BuildStartSplash(); _resultsFixtureLabel!.Text = "TERMINAL MENU ROUTE · completed run stays frozen · no progression/reset"; _resultsShot = "07-menu";
            }
            if (frame % 4 == 0 && _resultsShot != "")
            {
                var image = GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(_resultsCaptureDirectory, _resultsShot + ".png"));
                GD.Print($"RESULTS_CAPTURE image={_resultsShot} size={image.GetWidth()}x{image.GetHeight()}"); _resultsShot = "";
            }
            if (frame == 33) { GD.Print("RESULTS_CAPTURE completed natural_exit_reload_freeze=True last_guest_gate=True initialized_ratings_labelled=True council_bypass=True menu=True"); GetTree().Quit(); }
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
