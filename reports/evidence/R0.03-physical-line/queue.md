# R0.03 physical-arrival water line — 25 September 2026

Status: implemented and builder-verified; independent review and human exported-game playtest pending. This is a focused R0.03 queue correction, not R0.04.

Walking toward free water now has `SeekWater` intent and a dynamic approach destination, but **no queue ordinal or reservation**. The authoritative queue contains only people who reached the current entrance/tail (within 650 mm); simultaneous arrivals take places in stable person-ID order. When a place is taken or released, remaining walkers retarget toward the current tail. Admitted members advance one place at a time toward the tap; one owner begins drinking only after physical arrival at slot zero. The ten slots form a continuous, slightly offset single line, at least 1 m apart, avoiding the trailer's blocked footprint. Queue abandonment, ownership, performer eligibility and save/restore remain in the same medical state. Snapshot version 4 and save compatibility v8 deliberately reject old pre-reserved v7 queue semantics.

The [default-camera line capture](line-final/water-line.png) was visually inspected against the earlier user-provided image of four people loosely clustered around the standpipe. The fixture guided six additional guests and waited for five *physically admitted* members, reaching five at tick 1,141; the captured frame has seven admitted members in one tail-to-tap line and the HUD reads `Water queue 7`. It is a labelled fixture, not a claim that the untuned normal scenario always sustains seven. The approved v3 water GLB and its sign/runtime overlay were not edited; the designer's replacement sign candidate remains outside this change.

Verification:

- `dotnet build FestivalTycoon.sln --no-restore`: passed, zero warnings/errors.
- Direct built-DLL filter for medical, live performance, preparation, equipment, farm and incident-audio cursor: **54/54 passed**. New tests prove a faster walker starting farther away overtakes a slower earlier departure *before* queue admission, same-tick person-ID tie-breaking, five-person line advancement to the next physical tap owner, abandonment before obtaining a place, and save/restore during approach and service. Existing performer drink/return and counterfactual tests pass.
- `git diff --check`: no whitespace errors; only Windows line-ending conversion notices.
- Pinned Godot 4.7.2 `--capture-medical line`: exit 0, `water-line=7`. Repeated `escalate`/`prevent` fixtures exited 0: untreated `Collapsed` then `Terminal`/one casualty; medic route `Treating` then `Treated`/zero casualties. Both facility pick rays selected correctly.
- `dotnet build game/Festival.Game.csproj -c ExportRelease --no-restore`: passed, zero warnings/errors. Fresh `Windows Desktop` Release export exited 0 and ended `[ DONE ] savepack`.
- Final playable export: `artifacts/windows/r003-physical-line-1/FestivalTycoon-R0.03.exe` with adjacent .NET data folder. EXE 127,215,768 bytes, SHA-256 `76677E10C3414BE13F6C280853DC2FE3B0ED2A767AC7F12EAF2E9772B30A7334`. `Festival.Game.dll` SHA-256 `BC645521303838E80674F5C6CEA4D16E1F8BC5621EC017D908951C4FEE382A0B`; `Festival.Simulation.dll` SHA-256 `41ABD78DA7B66BBB9867B93C97152C7D8E3E9435838BFFB20BB4D05C660FFEF4`. Waited hidden headless startup exited 0 with fresh launch, scene-ready and integrated-audio markers.

The rejected first-aid v1 package is still untracked and untouched. No asset approval, R0.04 work, push, broader pacing result or human exported visual-playtest claim is made here.
