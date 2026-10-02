using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Festival.Game;

// Opt-in runtime audio check. Stages programme routes without writing a player save.
public partial class Main
{
    private string? _genreAudioVerificationOutput;
    private async void VerifyGenreAudio()
    {
        var checks = new List<object>();
        try
        {
            void Send(SessionCommand command)
            {
                var result = _host.Submit(command);
                if (!result.IsAccepted) throw new InvalidOperationException($"Audio fixture {command}: {result.Message}");
            }
            void Require(bool condition, string description)
            { if (!condition) throw new InvalidOperationException(description); }
            void Stage(string field, object? value) => typeof(GameSession)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_session, value);
            async Task WaitAudio() => await ToSignal(GetTree().CreateTimer(.12), SceneTreeTimer.SignalName.Timeout);
            var perks = _session.CapturePerks()!;
            Send(new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand[0]));
            Send(new UseDefaultBuildLayoutCommand());
            Send(new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
            foreach (var role in _session.GetStaffCandidates().GroupBy(c => c.Role))
                Send(new AcceptPreparationOfferCommand(role.OrderBy(c => c.Traits.Length).ThenBy(c => c.Grade > 0).First().Id));
            Send(new StartPreparedEditionCommand());
            var programme = _session.CaptureProgramme()!; var preparation = _session.CapturePreparation()!;
            var live = _session.CaptureLivePerformance()! with { Stage = LiveSetStage.Live,
                Performers = [], Listeners = [], LastReaction = "none" };
            Audio.EnsureStage(); Audio.ToggleMute(); // Test actual playback clocks without audible fixture music.
            var music = Audio.Players.First()!;
            var expectedFiles = new[] { "folk_loop_v1.wav", "indie_loop_v1.wav", "pop_loop_v1.wav",
                "electronic_loop_v1.wav", "punk_loop_v1.wav", "metal_loop_v1.wav", "punk_loop_v1.wav", "folk_loop_v1.wav" };
            for (var route = 0; route < expectedFiles.Length; route++)
            {
                var legacy = route >= FestivalGenre.Count;
                if (legacy)
                {
                    Stage("_programme", null);
                    Stage("_preparation", preparation with { AcceptedOffers = route == 6
                        ? preparation.AcceptedOffers.Append("act.punk").ToArray()
                        : preparation.AcceptedOffers.Where(id => id != "act.punk").ToArray() });
                }
                else
                {
                    var act = ActCatalogue.All.First(a => a.Genre == route);
                    Stage("_programme", programme with { ActIds = [act.Id, programme.ActIds[1], programme.ActIds[2]], CurrentSlot = 0 });
                }
                Audio.Reset(_session); SyncPresentationPause();
                Audio.AdvanceStage(_session, live, 100, Vector3.Zero, 0);
                var expected = "res://assets/audio/" + expectedFiles[route];
                Require(music.Stream?.ResourcePath == expected && music.Playing, $"Route {route} did not play {expected}");
                await WaitAudio();
                Require(music.GetPlaybackPosition() > .02, $"Route {route} playback clock did not advance");
                Send(new SetPausedCommand(true)); SyncPresentationPause();
                var paused = music.GetPlaybackPosition();
                await WaitAudio();
                Require(music.StreamPaused && Math.Abs(music.GetPlaybackPosition() - paused) < .015,
                    $"Route {route} moved while paused");
                Send(new SetPausedCommand(false)); SyncPresentationPause();
                await WaitAudio();
                Require(!music.StreamPaused && music.GetPlaybackPosition() > paused + .02, $"Route {route} did not resume");
                var length = music.Stream!.GetLength();
                var forwardLoop = false;
                if (route is FestivalGenre.Indie or FestivalGenre.Metal)
                {
                    Require(music.Stream is AudioStreamWav { LoopMode: AudioStreamWav.LoopModeEnum.Forward }, $"Route {route} import is not looped");
                    music.Seek((float)length - .04f); await WaitAudio();
                    forwardLoop = music.Playing && music.GetPlaybackPosition() < 1;
                    Require(forwardLoop, $"Route {route} did not wrap its loop boundary");
                }
                Audio.AdvanceStage(_session, live, 0, Vector3.Zero, 0);
                Require(!music.Playing, $"Route {route} played without power");
                Audio.AdvanceStage(_session, live, 100, Vector3.Zero, 0);
                Require(music.Playing, $"Route {route} did not restart when power returned");
                Audio.AdvanceStage(_session, live with { Stage = LiveSetStage.Finished }, 100, Vector3.Zero, 0);
                Require(!music.Playing, $"Route {route} played through set finish");
                Audio.AdvanceStage(_session, live with { Stage = LiveSetStage.BeforeSet }, 100, Vector3.Zero, 0);
                Require(!music.Playing, $"Route {route} played during changeover");
                checks.Add(new { route = legacy ? route == 6 ? "legacy-punk" : "legacy-folk" : FestivalGenre.Name(route),
                    expected, length, loaded = true, playbackAdvanced = true, pauseStable = true, resumed = true,
                    forwardLoop, powerStopped = true, powerRestored = true, changeoverSilent = true, passed = true });
            }
            Audio.Reset(_session);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_genreAudioVerificationOutput!))!);
            File.WriteAllText(_genreAudioVerificationOutput!, JsonSerializer.Serialize(new { diagnostic = "staged-genre-audio",
                debug = OS.IsDebugBuild(), checks, passed = true,
                caveat = "Runtime route/import/playback check on a muted bus; diagnostic stages programme selection. No player save, manual listening or full festival playtest." },
                new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("GENRE_AUDIO_VERIFIED routes=8 passed=true"); GetTree().Quit();
        }
        catch (Exception error)
        { GD.PushError($"GENRE_AUDIO_VERIFICATION_FAILED {error}"); GetTree().Quit(2); }
    }
}
