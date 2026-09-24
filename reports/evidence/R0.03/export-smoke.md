# R0.03 Windows export startup smoke — 24 September 2026

- Fresh, isolated artifact: `artifacts/windows/r003-playtest-2/FestivalTycoon-R0.03.exe`, 117,880,736 bytes, SHA-256 `767E89DB768A38DBB178CE5C82D70EE9167DE4A072AB72E85A1BAE37A832D600`.
- Adjacent `data_Festival.Game_windows_x86_64` folder contains 191 files, including the published game and simulation assemblies. No previous export was overwritten.
- Created with the repository-pinned Godot 4.7.2 .NET editor and `Windows Desktop` Release preset. Packing wrote the executable and .NET folder; the editor then remained idle and its exact process was stopped. Export-process exit is therefore **not** a clean success signal.
- Startup verification launched this exact executable with `Start-Process -Wait -WindowStyle Hidden`, arguments `--headless --quit-after 60`, from its export directory. Process started 21:46:41 local and ended 21:46:43 with exit code 0. Godot's per-user `Festival Tycoon/logs/godot.log` was updated at 21:46:43, and contained:

  ```text
  FESTIVAL_TYCOON_LAUNCHED build=0.0.1-m0.01 godot=4.7.2-stable (official)
  FARM_SCENE_READY scenario=scenario.lower-wittering-farm objects=6 hash=5d5b8584fcdfeb31a8be658205c2dd769a8f1fd38bd4d09485b451823312fa59
  ```

The marker name retains the M0.01 toolchain-smoke build label; R0.03 identity is the game save compatibility `0.0.1-r0.03-hot-medical-v5` plus this artifact path/hash. Earlier PowerShell direct `&` launches of GUI exports only captured the engine banner on stdout, so a missing stdout marker was not a valid failure conclusion. The earlier `r003-playtest` artifact is superseded. This verifies startup, not a human visual playtest or sustained frame pacing.
