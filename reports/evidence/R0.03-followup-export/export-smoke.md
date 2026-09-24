# R0.03 follow-up Windows export smoke — 24 September 2026

- Reviewed implementation commit: `ff9caeba7cfee2aa488d538509687660db5ae768`.
- Exact fresh artifact: `artifacts/windows/r003-followup-approved-v1-2/FestivalTycoon-R0.03-followup.exe`, 123,260,064 bytes, SHA-256 `E7D62B1A5D63877AD45AD50510C293E3A58C36A8536B15AD1DD436F63631F908`.
- Adjacent `data_Festival.Game_windows_x86_64` contains 191 files. No earlier export was overwritten.
- Built `game/Festival.Game.csproj` with `-c ExportRelease --no-restore`: zero warnings/errors. The repository-pinned portable Godot 4.7.2 .NET editor, with its matching local Windows templates, exported to this isolated directory. Its process exited, `export.stderr.log` was empty, and the export log ended `[ DONE ] savepack` without `ERROR:` or `WARNING:` lines.
- A preliminary fresh directory `r003-followup-approved-v1` contains only failed-attempt logs. The system-wide Godot editor could not locate Windows templates under AppData and created no executable. It was not reused; the successful artifact above came from the repository-pinned editor and templates.

## Exact startup smoke

Launched that exact EXE with `Start-Process -Wait -WindowStyle Hidden`, arguments `--headless --quit-after 60`, from its own export directory. It ran from 22:38:23 to 22:38:25 local and exited 0. Godot's user log was updated at 22:38:25 with fresh markers:

```text
FESTIVAL_TYCOON_LAUNCHED build=0.0.1-m0.01 godot=4.7.2-stable (official)
FARM_SCENE_READY scenario=scenario.lower-wittering-farm objects=6 hash=ea682835558413263e2e06aae52527dfc751aff344a2eded585961419ef788d6
```

The startup marker retains the historical M0.01 label; R0.03 follow-up identity is the commit, artifact hash, and `0.0.1-r0.03-hot-medical-v6` save compatibility.

## Package resource and icon checks

The export log lists approved `lwf_free_water_point_v1.glb.import` and the stage-sun PNG. Searching the complete export log and adjacent data files found no water-v2 model/palette and no `background-crowd.wav`, `generator-explosion2.wav`, `exaggerated-female-scream.wav`, or `exaggerated-male-scream.mp3`. An independent ASCII filename search of the embedded EXE pack found `lwf_free_water_point_v1.glb` and none of those excluded names. The package checks cover filenames/resources, not an exhaustive decoded-content audit.

Windows `Icon.ExtractAssociatedIcon` returned a 32 × 32 [EXE icon image](executable-icon.png) visually matching the supplied stage-sun art. This verifies the executable's associated icon, not the runtime window's icon in an interactive session. The latter remains unverified.

This smoke verifies startup and the scoped resource/icon checks, not a human visual playtest, audible cues or sustained frame pacing. The four supplied sound recordings remain unlicensed candidates outside `game/`.
