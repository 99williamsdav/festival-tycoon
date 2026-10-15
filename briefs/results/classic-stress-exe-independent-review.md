# Classic development stress copied-EXE independent review

**ACCEPTED for the exact copied release payload and scoped development-test playtest handoff. No open blocking findings.** Performance limitations below remain part of this acceptance; this is not a smooth-frame-rate or continuous-real-time claim.

15 October 2026. Independent reviewer separate from the implementation writer. Reviewed the working agreements, architecture background, accepted construction/core/UI slices and final export checkpoint. No implementation edits, builds, imports, exports, fixture recording or baseline changes were performed. All reviewer writes are this report and isolated evidence under `reports/evidence/classic-stress/independent-exe/`.

## Exact payload and preservation

Launched `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-classic-stress-2026-10-15/FestivalTycoon.exe` directly, from its delivery directory, without `--path` or fixed-FPS flags. Executable SHA-256: `664AF1F7FE486F6FC4EFA00D8E20CE4E9F46831C8BFFFA585068D28FF9A95F24`. Checkpoint SHA-256: `09872F36C99FC8552B9E444A57B098798BE90407E0B346813F7800D6C551C2B8`. Accepted construction `d54224f7`, core `7778dcac`, UI/harness `bc2863d0`.

Independently verified every one of 194 payload files (length and SHA-256), 2,062 source pins and 428 preservation pins before and after execution: zero mismatches both times. The latter include prior delivered payloads, older EXEs, unrelated work and immutable fixtures. Before execution the directory contained exactly the 194 runtime files. Afterward the builder's separate `README.txt` was the sole additional file; it is outside the runtime manifest. No pins were replaced. See `pins-before.json` and `pins-after.json`.

## Independent runtime evidence

Each suite ran sequentially through hidden-window `Start-Process -Wait -PassThru`, with native cache/log access and separate evidence/save directories. All application flags followed `--`, all captures used 1280×720. Each execution record retains EXE, arguments, process ID, start/end timestamps and exit status; stdout/stderr are preserved. All six exited zero with empty stderr.

| Suite | Passing checks | PNGs |
| --- | ---: | ---: |
| Development stress | 61 | 19 key images + 420 clip frames |
| Classic tickets | 100 | 12 |
| Ordinary Classic loop | 109 | 9 |
| Classic weather | 262 | 22 |
| Survival architecture | 63 | 8 |
| Save warning/recovery | 24 | 4 |

All 619 check entries passed. The stress process ran 19:49:04–19:54:19 local time. Its report identifies the actual copied executable. It verifies diagnostic options initially off, nine paid contracts, 100 attendees, grant provenance, three engineers, separate normal/development hosts and byte-identical ordinary save files; Pond slot-nine release/refund/cancellation and restored local keyboard focus; Barn-only and Pond-only physical coverage and own-generator cutoff at active index zero; real additional response geometry, removal cleanup and readiness/camera nonintersection; paid full-roster start, all three stages live, completion, nine results, restored saves and Festival 3 cash carry without another grant.

Independently decompressed the actual completed save without rewriting it. At tick 46,517 it contains 133 admitted and 133 departed people, nine performance records and zero NoRoute agents. All nine facilities have zero queue, overflow and owner claims, and all immersion vendor claims are null. `completed-save-audit.json` records these counts and save SHA-256 `A158DDEECE0ED585BC04020E265B49EE0BE1DD8E978FCD9C9FE63001E57A0042`.

Visually inspected all 19 original stress key PNGs plus clip frames 0010, 0140 and 0280. The corrected development menu is visible; provenance and default-off options fit; three-stage forecast, engineer cards and bookings are readable; the Pond dialog identifies its headliner and £400/40-ticket refund. Camera controls sit to the left of the checklist and the generator warning is unobscured. Additional unstaffed posts and paged placed-object/trader lists are visible. Results show 100 punters, nine sets and reputation 40→47. The final image now shows the Festival 3 forecast. Live frames show the physical stages and explicitly label the diagnostic 100 attendees + 27 performers + 6 staff = 133 people. No blocking visual defect in these reviewed stress images. The broader regression suites are scripted assertion evidence, not a claim that every regression screenshot received visual inspection.

Encoded the actual copied-EXE 420 PNG frames into `independent-exe/classic-development.mp4` using libx264/yuv420p. Both encoder and probe exited zero; ffprobe confirms H.264, 1280×720, 20 fps, 420 frames and exactly 21 seconds. This is scripted gameplay footage, not a real-time performance demonstration. All source frames remain local. `evidence-pins.json` hashes 646 evidence files, including all frames, key PNGs, logs, saves, summaries and the MP4.

## Performance and persistence limits

Independently inspected both builder-run 90-second copied-release profiles and `performance.md`. They used the same hardware, camera, audio, resolution and default timing. Baseline has 37 people/one stage and heavy showers; stress has 133 people/three stages and cloudy dry weather, with differing tick ranges. This is a bounded typical-scene comparison, not controlled roster scaling.

Baseline/stress wall-frame p95 is 521.423/527.494 ms; attained wall speed is 0.538×/0.641×. Engine delta totals are 48.626/57.930 seconds over approximately 90 wall seconds. Simulation plus observation p95 is 9.423/28.345 ms; presentation/audio/HUD 20.692/34.430 ms; viewport GPU 21.327/27.741 ms. These counters can overlap and do not explain the much larger wall stalls. No engine/driver/OS cause is established. Both real cadence saves restore successfully beyond tick 8000, with no save error. The failed earlier debug pilot remains retained and is not counted as cadence proof.

After native execution, independently inspected and ran the builder's evidence-only offline save benchmark against the exact copied release Simulation/Persistence assemblies. Baseline cadence state has 37 people; the independently captured stress live state has 133. Each completed three warmup plus ten measured production `AutosaveRotation.SaveCaptured` writes, with all 13 exact authoritative-hash restorations successful. Measured nearest-rank p95/maximum is 433.4268 ms baseline and 761.5320 ms stress. With ten samples p95 equals maximum. These include production envelope, compression, write-through flush, temporary/prior validation and slot replacement, but exclude foreground capture, worker scheduling and concurrent game/render work. They are offline persistence durations, not frame-blocking times. Inputs were read-only. Results and logs are under `save-write-baseline/` and `save-write-stress/`.

The prior independent core acceptance establishes broader subset, deterministic and immutable Survival parity coverage; this executable gate does not rerun the full unit suite. Scripted captures and these short profiles are not a manual full-day playthrough or a guarantee for every randomized site. Subject to those stated boundaries, the final executable gate is passed. The native/build/test/export lane is released.
