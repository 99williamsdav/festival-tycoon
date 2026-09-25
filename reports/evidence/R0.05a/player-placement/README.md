# R0.05a player-placement correction — rendered evidence

The OpenGL Compatibility fixture uses a labelled, deterministic layout: moved original tap `(104,112)`, additional taps `(70,120)` and `(101,130)`, and the fixed approved tower. This is visual/interaction evidence, not a human pacing playtest.

- `tower-selected.png`: the approved fixed tower remains separately pickable.
- `valid-site-preview.png`: the first extra tap has a green snapped footprint, 20 queue/overflow markers and a visible `VALID • 70,120` preparation summary, before that tap is placed.
- `placed-tap-selected.png`: a player-positioned standpipe is picked by its saved identity, `water.extra-1`.
- `occupied-site-preview.png`: moving the original tap onto an occupied site shows a red 3.5 m footprint, 20 red queue/overflow markers and an explicit overlap reason in the preparation summary.
- `two-taps-live.png`: after opening, a two-guest fixture has separate drinkers at moved `water.main` and `water.extra-1`; all three taps remain in the layout.
- `same-id-layout-b.ftsave` and `same-id-relocated.png`: the fixture saves and loads layout B with the same `water.extra-1` ID at `(110,130)` instead of layout A's `(70,120)`. The existing visual and collision pick move; a ray at the old cell does not select that tap, a ray at the new cell does, and the new-site inspector is rendered.

Run: `Godot_v4.7.2-stable_mono_win64_console.exe --path game --rendering-driver opengl3 -- --capture-r005a-water reports/evidence/R0.05a/player-placement`. Exit 0; `FESTIVAL_TYCOON_LAUNCHED`, `FARM_SCENE_READY`, picks, valid and invalid previews, two owners, same-ID relocated visual/pick and old-layout stale-selection clearing appeared. No `ERROR:` lines. The fixture moves the original through the simulation command before rendering, previews the first valid extra site, then installs both extras through the same simulation commands; it does not claim to have clicked the preparation button. Command persistence, mixed legacy-command rejection and retry are covered by `WaterFoundationsTests`.

The isolated Release export is `artifacts/windows/r005a-player-placement/FestivalTycoon-R0.05a.exe`; its updated managed `Festival.Game.dll` SHA-256 is `385E6582265E518B9E8180C986734699169FFA5963B9F1297449870EEFB30CF8`. Launching the export with `--headless --quit-after 60` exited 0 and wrote `FESTIVAL_TYCOON_LAUNCHED` and `FARM_SCENE_READY` to Godot's application log (`%APPDATA%/Godot/app_userdata/Festival Tycoon/logs/godot.log`, 25 September 2026, 21:57:17). The shell captured only the engine header; the application log is the marker evidence. The default playtest executable was not overwritten.
