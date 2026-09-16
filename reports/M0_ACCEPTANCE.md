# M0 foundation acceptance

Overall outcome: **Conditional / performance gate failed at promised ceiling**. The deterministic, financial, save, camera and 50-agent foundations pass. The original 1,200 full-fidelity performance target does not.

| IMPLEMENTATION_PLAN §3 requirement | Status | Evidence |
|---|---|---|
| Documented clean build/export, no editor-only setup | Pass | [M0.01](../briefs/results/M0.01.md), [M0.10](../briefs/results/M0.10.md) |
| Same seed + commands produce same checksum | Pass | [M0.02](../briefs/results/M0.02.md); M0.10 paired hashes |
| Halfway save resumes to same final checksum | Pass | [M0.05](../briefs/results/M0.05.md), [M0.09](../briefs/results/M0.09.md) |
| Pause freezes authoritative progress; camera remains usable | Pass | [M0.09](../briefs/results/M0.09.md) |
| 1×/4× identical at identical simulation ticks | Pass | M0.09 regression plus M0.10 50/200 paired hashes |
| Wallet transfer conserves money | Pass | [M0.04](../briefs/results/M0.04.md) |
| Queue reservation/lifecycle survives save/load | Pass | [M0.08](../briefs/results/M0.08.md), [M0.09](../briefs/results/M0.09.md) |
| Camera rotates 90°; selection stable through four views | Pass | [M0.06](../briefs/results/M0.06.md) and camera-relative corrective tests |
| Static grid, deterministic pathing, separation, no fence crossing | Pass at accepted fixtures | [M0.07](../briefs/results/M0.07.md), [M0.09](../briefs/results/M0.09.md) |
| 50 rendered attendees gate→physical queue→service | Pass | [M0.09](../briefs/results/M0.09.md) |
| Multiple benchmark destinations and narrow/wide variants | Pass | [M0.10](../briefs/results/M0.10.md) |
| 50/200/500/1,200 performance measured | Pass as measurement obligation | M0.10; 1,200 explicitly diagnostic/incomplete |
| Intended 1,200-agent scale plausible at target | **Fail** | 500 already averages 115.6 ms/tick; 1,200 initialization/warm-up structurally excessive |
| 1,200 at ≥30 rendered FPS at 1× | **Fail** | Exported launch timed out after 217 seconds without producing a frame |
| 4× useful acceleration without skipped ticks | Pass at 50; **Fail at 200/500** | 200 attained 3.25×; explicit 500 requested-4× run attained 0.73×, while preserving hashes |
| Simulation CPU under 8 ms average at ceiling | **Fail** | 200/500 mean and high percentiles exceed target |
| Peak process memory under 2 GB | Pass on measured machine | Approximately 53–63 MB headless working set |
| No duplicated reservations; completed scenarios reconcile sales | Pass through 500 | All completed agents produced one sale; zero final reservations/failures/recovery |
| No Godot references in simulation library | Pass | Project boundary/build evidence |
| Valid versioned save header and migration behavior | Pass | [M0.05](../briefs/results/M0.05.md), M0.09 presence-aware migration |
| Low-poly readability / atmosphere | Partially verified | Four-view/screenshots pass technical inspection; subjective polish remains playtest work |

Decision carried forward: target 200 full-fidelity attendees near term, keep 500 as an optimization stress fixture, and retain 1,200 as an eventual perceived crowd ambition requiring a separately approved architecture/optimization brief. This recommendation does not rewrite the original gate result.
