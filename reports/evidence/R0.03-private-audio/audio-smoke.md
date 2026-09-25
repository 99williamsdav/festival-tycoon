# R0.03 local-private incident audio smoke — 25 September 2026

Scope: private local playback only. The user recalls selecting Creative Commons/free-use recordings but will check exact sources later. No licence, attribution or redistribution claim is made. The four source filenames and SHA-256 values are recorded in [provenance](../../../assets/source/audio/crowd/PROVENANCE.md); game-local copies matched byte-for-byte.

Targeted review accepted the local-private audio wiring in commit `ef73e4a46276709f1bcde9eec92d9d515275e819` with no code findings. Human audition and source/licence checks remain separate gates.

## Implementation and verification

- All four recordings were copied to git-ignored `game/assets/audio/crowd/`. The project `Outdoor Stage` bus owns their mute and volume routing. The ambient player uses a low starting gain, camera-focus distance attenuation and restarts its long clip after completion. Generator and witness cues use separate players. The cue cursor consumes authoritative death evidence once and resets on load, including while muted.
- Equipment `equipment:death` emits generator explosion followed by the female off-camera witness sample. Medical `medical:death` emits the male off-camera witness sample. The voices are not assigned to victims. Updated cursor tests assert both deterministic variants and no replay on load.
- `dotnet build FestivalTycoon.sln --no-restore`: passed, zero warnings/errors. Focused direct-DLL medical/live/preparation/equipment/farm/audio-cursor tests: **43/43 passed**.
- Pinned Godot 4.7.2 `--headless --path game --import`: exited 0 and imported all four streams. It logged a non-fatal Unicode parsing warning while reading embedded WAV metadata; source identity and codec audition still need human review.
- Local `--headless --path game --quit-after 60`: exited 0 with fresh `FESTIVAL_TYCOON_LAUNCHED`, `FARM_SCENE_READY`, and `INCIDENT_AUDIO_READY ambient=True explosion=True female=True male=True mode=private-playtest` markers.
- Local `--capture-medical escalate` (rendered): exited 0 with `Terminal casualties=1`, one `INCIDENT_AUDIO_AMBIENT_START` and one `INCIDENT_AUDIO cue=DeathScream voice=Male tick=6000`. [Outcome frame](medical-escalate/outcome.png). This proves loaded streams and cue selection; it is not an audible-quality test.
- The existing rendered equipment escalation fixture hit its 110-second wall timeout before the terminal tick, at 5,471 ticks and 0.620× attained speed in this Debug/capture run. [Result](equipment-escalate/result.json) has `passed=false`, `Stage=Warning`, `roundTrip=true`; this is **not** evidence of actual explosion playback. The focused equipment cursor test reaches the authoritative terminal event and asserts ordered explosion/scream, once only and silent after restore.

## Private-resource containment

- `.gitignore` excludes the raw source WAV/MP3 files and the game-local playback folder from routine commits.
- The Windows export preset excludes `assets/audio/crowd/*`. A local-only PCK filter probe in ignored `artifacts/windows/r003-private-audio-filter-check/` completed with empty stderr. Neither its export log nor its embedded filename table contained any of the four private audio filenames. No playable export or public package was made for this trial.
- This containment was checked for the current Godot 4.7.2 preset. It is not a substitute for rechecking every future export before distribution.

Human listening is still needed for mix/loudness, whether the background file actually contains suitable crowd ambience, abrupt loop boundaries, and the musical context. Its embedded metadata names an unrelated copyrighted programme. Exact source/licence links, attribution terms and redistribution permission remain open gates.
