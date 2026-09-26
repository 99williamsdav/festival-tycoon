using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private string? _timetableCaptureDirectory;
    private int _timetableCaptureFrame;

    private void TimetableAudioAssert(string path, bool playing)
    {
        AdvanceLivePerformancePresentation(0);
        if (_stageMusic!.Playing != playing || playing && _stageMusic.Stream?.ResourcePath != path)
            throw new InvalidOperationException($"Audio mismatch: expected {path} playing={playing}.");
        GD.Print($"TIMETABLE_AUDIO_CHECK stream={_stageMusic.Stream?.ResourcePath} playing={_stageMusic.Playing} expected={playing}");
    }

    private void TimetableRockAndReloadCheck()
    {
        // Presentation diagnostic: a separately, normally prepared first-slot Rock
        // session is swapped while both snapshots are Live. This is not evidence
        // of physical timetable progression; that remains the ordinary run below.
        var original = _session;
        var hash = original.CaptureSnapshot().AuthoritativeHash;
        var rock = GameSession.CreateTimetableCampaign(20260922);
        void Send(SessionCommand command)
        {
            var result = rock.Execute(new(new CommandId(rock.NextSubmissionSequence + 1), rock.CampaignId,
                rock.Phase, rock.CurrentTick, rock.NextSubmissionSequence, null, command));
            if (!result.IsAccepted) throw new InvalidOperationException($"Rock audio fixture rejected {command}.");
        }
        Send(new SetProgrammeCommand(["act.barnstorm-circuit", "act.neon-postcards", "act.field-frequency"]));
        foreach (var id in new[] { "staff.steward", "equipment.buy" }) Send(new AcceptPreparationOfferCommand(id));
        Send(new StartPreparedEditionCommand());
        rock.AdvanceWithoutSnapshot(1600);
        var medical = rock.CaptureMedical()!;
        Send(new StaffInterventionCommand(medical.AtRiskGuestId, medical.MedicId, StaffInterventionAction.GuideToRest));
        rock.AdvanceWithoutSnapshot(1800);
        Send(new SetPausedCommand(true));
        if (rock.CaptureLivePerformance()?.Stage != LiveSetStage.Live)
            throw new InvalidOperationException("Rock fixture did not physically reach Live.");
        _session = rock;
        TimetableAudioAssert("res://assets/audio/rock_loop_v2.wav", true);
        GD.Print($"TIMETABLE_ROCK duration={_stageMusic!.Stream.GetLength():0.000000} same_live_phase=True");
        _session = original;
        TimetableAudioAssert("res://assets/audio/folk_loop_v1.wav", true);
        PreparationSave();
        var isolated = GameSession.Restore(original.CapturePersistenceSnapshot());
        if (!isolated.IsSuccess) throw new InvalidOperationException(isolated.Error);
        _session = isolated.Session!;
        StaffCaptureSend(new EquipmentCommand(EquipmentAction.Isolate));
        TimetableAudioAssert("", false);
        PreparationLoad();
        TimetableAudioAssert("res://assets/audio/folk_loop_v1.wav", true);
        if (_session.CaptureSnapshot().AuthoritativeHash != hash || _lastReactionSequence != _session.CaptureLivePerformance()!.ReactionSequence ||
            _crowdCheer!.Playing || _bandEntryApplause!.Playing)
            throw new InvalidOperationException("Live reload changed hash or replayed a crowd reaction.");
        GD.Print("TIMETABLE_AUDIO_RELOAD exact_hash=True no_replayed_reaction=True power_cut_silence=True");
        SelectAttendee(new EntityId(_session.CaptureLivePerformance()!.Listeners[0].AgentId));
    }

    private void TimetableImage(string name) => GetViewport().GetTexture().GetImage()
        .SavePng(Path.Combine(_timetableCaptureDirectory!, name + ".png"));

    private void TimetableState(string name)
    {
        var programme = _session.CaptureProgramme()!;
        var p = _session.CapturePreparation()!;
        var live = _session.CaptureLivePerformance();
        var protectedPerformers = p.People.Where(person => person.Role == ProtectedPersonRole.Performer).Select(person => person.AgentId).ToArray();
        if (p.Status == PreparationStatus.Failed || protectedPerformers.Length != 9 || p.People.Length > 50 ||
            _session.CaptureMedical()!.Needs.Count(need => protectedPerformers.Contains(need.AgentId)) != 9)
            throw new InvalidOperationException("Timetable demonstration lost protected performers or exceeded its scope.");
        if (name is "first-set" or "second-set" or "third-set" &&
            (live?.Stage != LiveSetStage.Live || live.Performers.Count(person => person.OnStage) != 3))
            throw new InvalidOperationException($"{name} did not physically start within its fixed window.");
        GD.Print($"TIMETABLE_STATE name={name} tick={_session.CurrentTick} slot={programme.CurrentSlot} phase={live?.Stage} ready={live?.Performers.Count(person => person.OnStage)} people={p.People.Length} performers={protectedPerformers.Length} admitted={p.People.Count(person => person.Admitted)} departed={p.People.Count(person => person.Departed)} status={programme.Status} hash={_session.CaptureSnapshot().AuthoritativeHash}");
        if (live is not null)
            foreach (var performer in live.Performers)
            {
                var nav = _session.CaptureObservation().NavigationAgents.Single(agent => agent.Id.Value == performer.AgentId);
                GD.Print($"TIMETABLE_BAND person={performer.AgentId} physical={nav.XMillimetres},{nav.ZMillimetres} action={nav.Action} intent={nav.IntentId} instrument={performer.InstrumentAttached}");
            }
    }

    private void TimetableAdvanceTo(long relativeTick)
    {
        var target = _session.CapturePreparation()!.StartedTick + relativeTick;
        if (_session.CurrentTick < target)
        {
            StaffCaptureSend(new SetPausedCommand(false));
            while (_session.CurrentTick < target && _session.PreparedStatus != PreparationStatus.Failed)
            {
                // Demonstration operator responds to visible warnings with the ordinary
                // baseline medic; no needs/position/deadline injection or remote aid.
                var medical = _session.CaptureMedical()!;
                foreach (var need in medical.Needs.Where(need => need.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical ||
                    need.AgentId == medical.AtRiskGuestId && medical.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical))
                {
                    var command = new MedicalCommand(need.AgentId, MedicalAction.DispatchMedic, medical.MedicId);
                    if (_session.ValidateCommand(CampaignEnvelope(command)) is null)
                    {
                        StaffCaptureSend(command);
                        GD.Print($"TIMETABLE_OPERATOR ordinary_medic_dispatch=True patient={need.AgentId} tick={_session.CurrentTick}");
                        break;
                    }
                }
                _session.AdvanceWithoutSnapshot((int)Math.Min(80, target - _session.CurrentTick));
            }
            if (_session.PreparedStatus == PreparationStatus.Failed)
                throw new InvalidOperationException($"Ordinary timetable counter run failed at {_session.CurrentTick}; {_session.CaptureLifecycleSnapshot()!.Casualties.Last().PersonId}");
            StaffCaptureSend(new SetPausedCommand(true));
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary(); RefreshPreparationHud();
        }
        TimetableState("checkpoint");
    }

    private void ProcessTimetableCapture()
    {
        if (_timetableCaptureDirectory is null) return;
        try { ProcessTimetableCaptureCore(); }
        catch (Exception exception)
        {
            GD.PushError($"TIMETABLE_CAPTURE_FAILED {exception.Message}");
            _timetableCaptureDirectory = null;
            GetTree().Quit(1);
        }
    }

    private void ProcessTimetableCaptureCore()
    {
        if (_timetableCaptureDirectory is null) return;
        _timetableCaptureFrame++;
        if (_timetableCaptureFrame == 4)
        {
            _programmeDraft = ["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"];
            RefreshProgrammeControls(); _programmeBook!.EmitSignal(Button.SignalName.Pressed);
            foreach (var offer in new[] { "staff.steward", "equipment.buy" }) _offerButtons[offer].EmitSignal(Button.SignalName.Pressed);
            // Exercise the ordinary load handler across both saved modes in this
            // isolated fixture directory; never overwrite player saves.
            var bookedSession = _session;
            var bookedHash = _session.CaptureSnapshot().AuthoritativeHash;
            var legacy = GameSession.CreateDisorderCampaign(20260922);
            var legacySave = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-preparation", new SaveWriteRequest(legacy, _saveCompatibility, "legacy-mode-fixture", DateTimeOffset.UtcNow));
            if (!legacySave.IsSuccess) throw new InvalidOperationException(legacySave.Error);
            PreparationLoad();
            if (_session.CaptureProgramme() is not null || _programmeControls!.Visible || !_offerButtons.ContainsKey("act.folk") || !_offerButtons.ContainsKey("act.punk"))
                throw new InvalidOperationException("Legacy load retained programme UI or lost single-set offers.");
            var programmeSave = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-preparation", new SaveWriteRequest(bookedSession, _saveCompatibility, "programme-mode-fixture", DateTimeOffset.UtcNow));
            if (!programmeSave.IsSuccess) throw new InvalidOperationException(programmeSave.Error);
            PreparationLoad();
            if (_session.CaptureSnapshot().AuthoritativeHash != bookedHash || !_programmeControls.Visible || _offerButtons.ContainsKey("act.folk"))
                throw new InvalidOperationException("Programme load changed booking or retained legacy offers.");
            GD.Print("TIMETABLE_UI legacy_and_programme_load=True exact_booked_hash=True");
            _preparationMessage = "ONE-DAY DEMO • ordinary programme booking, paid once; three distinct protected bands.";
            RefreshPreparationHud();
            _focus = new Vector3(-10, 0, 11); _camera.Size = 29; ApplyCamera();
            TimetableState("booked");
        }
        if (_timetableCaptureFrame == 6)
        {
            TimetableImage("three-acts-booked-order-and-price");
            PreparationStart();
            if (_session.PreparedStatus != PreparationStatus.Running)
                throw new InvalidOperationException($"Programme start failed: {_preparationMessage}");
            if (_session.CaptureEquipment() is not { LoadPercent: 80 })
                throw new InvalidOperationException("Normal Hot scenario must retain the inherited safe generator baseline.");
            StaffCaptureAdvance(1200);
            SelectAttendee(new EntityId(_session.CaptureProgramme()!.Performers[2].AgentId));
            TimetableState("scheduled-first-start");
        }
        if (_timetableCaptureFrame == 7)
        {
            TimetableImage("scheduled-start-performer-readiness-and-role-title");
            StaffCaptureAdvance(400);
            var medical = _session.CaptureMedical()!;
            StaffCaptureSend(new StaffInterventionCommand(medical.AtRiskGuestId, medical.MedicId, StaffInterventionAction.GuideToRest));
            TimetableAdvanceTo(2000);
            SelectAttendee(new EntityId(_session.CaptureLivePerformance()!.Listeners[0].AgentId));
            TimetableState("first-set");
        }
        if (_timetableCaptureFrame == 8)
        {
            TimetableImage("first-folk-set-current-upcoming-needs");
            TimetableAudioAssert("res://assets/audio/folk_loop_v1.wav", true);
            TimetableRockAndReloadCheck();
            TimetableAdvanceTo(7360);
            TimetableAudioAssert("", false);
            TimetableState("first-changeover");
            var saved = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-changeover", new SaveWriteRequest(_session, _saveCompatibility, "timetable-changeover", DateTimeOffset.UtcNow));
            var loaded = SaveFileAdapter.LoadSlot(SaveDirectory, "manual-changeover", _saveCompatibility);
            if (!saved.IsSuccess || !loaded.IsSuccess || loaded.Session!.CaptureSnapshot().AuthoritativeHash != _session.CaptureSnapshot().AuthoritativeHash)
                throw new InvalidOperationException("Timetable changeover save did not round-trip.");
            GD.Print("TIMETABLE_SAVE changeover_exact=True");
            var endHash = _session.CaptureSnapshot().AuthoritativeHash;
            var endPlays = _setEndApplausePlayCount;
            PreparationSave(); PreparationLoad();
            AdvanceLivePerformancePresentation(0);
            if (_session.CaptureSnapshot().AuthoritativeHash != endHash || _setEndApplausePlayCount != endPlays || _setEndApplause!.Playing)
                throw new InvalidOperationException("Ended-set reload changed state or replayed applause.");
            GD.Print("SET_END_APPLAUSE_RELOAD exact_hash=True replay=False");
        }
        if (_timetableCaptureFrame == 10)
        {
            TimetableImage("first-band-physical-exit-changeover-silence");
            TimetableAdvanceTo(10200); TimetableState("second-set");
        }
        if (_timetableCaptureFrame == 12)
        {
            TimetableAudioAssert("res://assets/audio/pop_loop_v1.wav", true);
            TimetableImage("second-pop-band-on-marks");
            TimetableAdvanceTo(15360); TimetableState("second-changeover");
        }
        if (_timetableCaptureFrame == 14)
        {
            TimetableAudioAssert("", false);
            TimetableImage("second-band-physical-exit-changeover");
            TimetableAdvanceTo(18200); TimetableState("third-set");
        }
        if (_timetableCaptureFrame == 16)
        {
            TimetableAudioAssert("res://assets/audio/electronic_loop_v1.wav", true);
            TimetableImage("third-electronic-band-on-marks");
            TimetableAdvanceTo(23600); TimetableState("wind-down");
        }
        if (_timetableCaptureFrame == 18)
        {
            TimetableAudioAssert("", false);
            TimetableImage("all-nine-protected-performers-remain-on-farm");
            TimetableAdvanceTo(24001);
            StaffCaptureSend(new SetPausedCommand(false));
            for (var ticks = 0; ticks < 9600 && _session.PreparedStatus != PreparationStatus.Finished; ticks++)
                _session.AdvanceWithoutSnapshot(1);
            if (_session.PreparedStatus != PreparationStatus.Finished)
                throw new InvalidOperationException("Physical final departure did not complete in the demonstration bound.");
            // Finished editions reject further pause commands. Hold only presentation
            // work, without mutating settled authoritative state or bypassing departure.
            _preparationSaveBlocked = true;
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary(); RefreshPreparationHud();
            TimetableState("physical-final-departure");
        }
        if (_timetableCaptureFrame == 20)
        {
            if (_session.PreparedStatus != PreparationStatus.Finished || _session.CapturePreparation()!.People.Any(person => !person.Departed))
                throw new InvalidOperationException("Not every protected person completed physical festival departure.");
            TimetableImage("festival-finished-physical-departure");
            GD.Print("TIMETABLE_CAPTURE_COMPLETE three_sets=True protected_performers=9 physical_departure=True existing_graphics=True");
            GetTree().Quit();
        }
    }
}
