# R0.03 approved water-v3 Windows export smoke — 25 September 2026

- Reviewed v3 integration commit: `34fd0d7329ed5eb5583fc105cdd6b59460b21fbb`. Designer asset commit: `d7733c3d675a90d492fc021402dce1c01cd1123b`.
- Exact fresh isolated EXE: `artifacts/windows/r003-water-v3-approved-1/FestivalTycoon-R0.03-v3.exe`, 123,308,920 bytes, SHA-256 `DC4E52BA16765BD266660AC31059B0943C0C69CC6BC744F36A2AFA0E6D725CAC`. Its adjacent .NET data folder contains 191 files. No existing export was overwritten and no push was made.
- `dotnet build game/Festival.Game.csproj -c ExportRelease --no-restore`: passed, zero warnings/errors. The repository-pinned portable Godot 4.7.2 .NET editor and matching templates completed the standard `Windows Desktop` Release export. `export.stderr.log` was empty; `export.stdout.log` ended `[ DONE ] savepack` without error/warning markers.
- Launched that exact EXE with `Start-Process -Wait -WindowStyle Hidden`, arguments `--headless --quit-after 60`, from its own directory. It ran 05:56:44–05:56:46 local, exited 0, and refreshed Godot's user log with:

  ```text
  FESTIVAL_TYCOON_LAUNCHED build=0.0.1-m0.01 godot=4.7.2-stable (official)
  FARM_SCENE_READY scenario=scenario.lower-wittering-farm objects=6 hash=ea682835558413263e2e06aae52527dfc751aff344a2eded585961419ef788d6
  INCIDENT_AUDIO_READY ambient=False explosion=False female=False male=False mode=private-playtest
  ```

The marker retains the historical M0.01 launch label; the EXE path/hash and `0.0.1-r0.03-hot-medical-v6` save compatibility identify this build. The audio readiness line confirms this standard export has no optional private streams, even though local editor runs load them.

## Resource and icon checks

The export log lists `res://assets/environment/lwf_free_water_point_v3.glb.import` and its palette sidecar. An independent ASCII filename scan of the embedded EXE found `lwf_free_water_point_v3.glb`; historical approved v1 is also present but no longer referenced by gameplay. The full export log, embedded filename scan and adjacent data-folder check found no water-v2 model/palette and none of the four private `background-crowd.wav`, `generator-explosion2.wav`, `exaggerated-female-scream.wav` or `exaggerated-male-scream.mp3` files. These are scoped filename/resource checks, not a complete decoded-content audit.

Windows `Icon.ExtractAssociatedIcon` produced a 32 × 32 [EXE icon](executable-icon.png) visually matching the supplied stage-sun art. This verifies the associated executable icon, not the interactive window icon.

This is startup/package verification, not an exported human visual playtest, audible cue audition or sustained frame-pacing result. The authored v3 mesh sign/tap are small at the default wide camera; the floating `DRINKING WATER` cue is readable in the [editor capture](prevent-aligned/water-queue.png). Private audio source/licence and human mix checks remain open.
