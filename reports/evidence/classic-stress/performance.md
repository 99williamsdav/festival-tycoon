# Classic development release performance observations

Actual copied release EXE 664AF1F7FE486F6FC4EFA00D8E20CE4E9F46831C8BFFFA585068D28FF9A95F24, source bc2863d0, measured 15 October 2026. Both runs exit 0 with empty stderr and a restored real cadence autosave newer than tick8000. This is a bounded comparison, not a smooth-performance or real-time guarantee.

Hardware: Intel Core i7-12700H (14 cores / 20 logical processors), about 31.7 GiB visible RAM, Windows 11 Home build 26200. Godot 4.7.2 release, GL Compatibility on NVIDIA RTX 3050 Ti Laptop GPU, driver 32.0.16.1060, 1280×720, existing enabled VSync and 58 fps cap. Same camera Frame (4,0,0), distance 62; live audio unmuted. Hardware details: hardware.json. The scripted harness calls ordinary production simulation, presentation, stage/audio, HUD and rotating save methods; input/hover processing is omitted. No screenshot loop runs during measurement.

| Scene | Attendees | Performers | Staff | Stages | Wall seconds | Delivered engine seconds | Attained wall speed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| baseline | 25 | 9 | 3 | 1 | 90.168 | 48.626 | 0.538× |
| stress | 100 | 27 | 6 | 3 | 90.137 | 57.930 | 0.641× |

Timing values below are milliseconds, shown as p95 / maximum. Three-second warmup excluded. Render counters use Godot viewport CPU plus frame-setup CPU, and viewport GPU; these may overlap CPU/script work and must not be added to wall time.

| Work | Normal (37 people) | Stress (133 people) |
| --- | ---: | ---: |
| Wall frame | 521.4233 / 764.7926 | 527.4940 / 766.5074 |
| Simulation plus observations | 9.4233 / 21.3241 | 28.3447 / 33.8572 |
| Presentation, audio and HUD | 20.6923 / 32.7558 | 34.4299 / 57.3603 |
| Autosave scheduling/snapshot boundary | 0.0011 / 4.0226 | 0.0014 / 3.8813 |
| Viewport render CPU plus setup | 2.5270 / 10.9070 | 4.5010 / 15.0380 |
| Viewport render GPU | 21.3270 / 26.3440 | 27.7410 / 34.7890 |
| Simulation: activity-choice | 0.6794 / 9.7762 | 1.9641 / 8.5723 |
| Simulation: consumption-and-vendor-queues | 0.1595 / 6.5629 | 0.4065 / 7.2001 |
| Simulation: friendly-water-queues | 0.0004 / 0.0138 | 0.0003 / 0.0133 |
| Simulation: owned-service-queues | 0.0015 / 0.0123 | 0.0012 / 0.0261 |

All scope calls fit inside the 65,536-sample observation bound, so these per-scope p95 values cover every measured call (normal 3,879; stress 4,624; read exact counts in each profile). Consumption/vendor timing is inclusive; scope totals are not independent frame costs. The timing observer is opt-in, unsaved/unhashed, and the rebuilt neutrality test passed 1/1 after bounded p95 sampling was added.

Normal cadence restored tick 10402; stress cadence restored tick 10401; both SaveError=null. Background write/restore correctness was verified. The scheduling column measures the foreground snapshot/scheduling boundary, not the entire background file-write latency. Peak process working sets: normal 647.8 MiB; stress 702.7 MiB.

The large wall-frame stalls occur in both scenes (p95 about 0.52 seconds and max about 0.77 seconds), exceeding the separately observed script and render work. Godot delivered considerably less delta time than elapsed wall time. This evidence does not assign a cause to the remaining engine/driver/OS scheduling delay and does not support a claim of smooth 58 fps or continuous 1× play. No speculative gameplay or renderer change was made. Wider foreground/manual profiling remains a limitation.

Retained pilot: the earlier 45-second debug baseline ended with no new 30-engine-second cadence save and failed its assertion (exit 2). Its valid tick 0 autosave is not counted as cadence proof. Final release comparisons above use 90-second windows and pass the unchanged newer-than-8000 save assertion. Pilot profile/failure/logs remain under profile-baseline/ and artifacts/classic-stress-profile-baseline-*.txt. Final profiles and execution records are builder-exe-profile-baseline/ and builder-exe-profile-stress/.


The normal profile is the actual first Classic festival (seed 20260922, heavy showers); the development profile is the actual second-festival milestone (same campaign seed, cloudy and dry). Both start from tick 8000 before warmup, but subsequent tick ranges differ because delivered engine deltas differ. Weather and phase coverage are therefore covariates: this compares these two real scenes and does not isolate the cost of roster or stage count. No scaling ratio or cause for the shared wall stalls is claimed.

Separate offline persistence benchmark: the reviewer ran the copied release Simulation/Persistence assemblies through the production AutosaveRotation.SaveCaptured path, with three warmup writes followed by ten measured rotation writes per scene. Normal 37-person captured state: p95/max 433.4268 ms. Stress 133-person captured state: p95/max 761.5320 ms. All 13 writes per scene saved and restored the exact authoritative hash. This includes envelope construction, gzip/write-through flush, temporary/prior validation and slot replacement; it excludes foreground capture, concurrent game/render work and worker scheduling. With ten samples, nearest-rank p95 equals the maximum. These are background-workload measurements, not frame-blocking durations. Script and exact input/assembly hashes, samples and limitations are retained in run-save-write-benchmark.ps1 and independent-exe/save-write-{baseline,stress}/summary.json.
