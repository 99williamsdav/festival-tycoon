# M0 foundation acceptance

Overall outcome: **Fail at the promised crowd/performance ceiling**. Functional, deterministic, financial, save and camera foundations pass; representative rendered performance and ceiling memory remain unverified where stated.

| IMPLEMENTATION_PLAN §3 requirement | Status | Evidence |
|---|---|---|
| Documented clean build/export, no editor-only setup | Pass | [M0.01](../briefs/results/M0.01.md), [M0.10](../briefs/results/M0.10.md) |
| Same seed + commands produce same checksum | Pass | M0.02 and M0.10 repeated hashes |
| Halfway save resumes to same final checksum | Pass | M0.05/M0.09 plus repaired regression run |
| Pause freezes authoritative progress; camera remains usable | Pass | M0.09 |
| 1×/4× identical at identical simulation ticks | Pass | M0.09 authoritative regression |
| Wallet transfer conserves money | Pass | M0.04 |
| Queue reservation/lifecycle survives save/load | Pass | M0.08/M0.09 |
| Camera rotates 90°; selection remains stable | Pass | M0.06/M0.09 |
| Static grid, deterministic pathing, separation, no fence crossing in accepted fixtures | Pass | M0.07/M0.09 |
| 50 rendered attendees gate→physical queue→service | Pass | M0.09 exported foundation evidence |
| Multiple benchmark destinations and narrow/wide variants exist | Pass | M0.10 fixture/tests; sessions are isolated |
| Same benchmark configuration has stable authoritative output | Pass | M0.10 tests and retained hashes |
| Wide fixture improves controlled completion without changing demand/stock | Pass | Deterministic fixture only; width can also shorten individual routes |
| Completed scenarios reconcile sales/reservations without despawn | Pass | 50/200/500 functional completion evidence |
| 50/200 representative headless measurement | Pass | All sessions/agents active at both sample boundaries |
| 500 representative headless measurement | Pass | Legacy 600+1200 active-population window, units corrected |
| 1,200 representative measurement | Unverified | Only bounded pre-arrival diagnostic; normal warm-up structurally excessive |
| Intended 1,200 full-fidelity scale plausible at target | Fail | 200/500 unpaced capacity already below game 1×; 1,200 produced no exported frame |
| 1,200 at ≥30 rendered FPS at 1× | Fail | Export launch timed out after 217 seconds without a frame |
| Requested/attained representative rendered 1×/4× below ceiling | Unverified | Existing 50/500 captures are early/pre-arrival; headless loop is unpaced |
| 4× useful acceleration at the crowd ceiling without skipped ticks | Fail | Even 200 unpaced compute capacity is only 0.245–0.270× game 1× |
| Simulation CPU under 8 ms average per rendered frame at ceiling | Unverified | No representative ceiling frame sample; this target is not per tick |
| Peak process memory under 2 GB at ceiling | Unverified | True process peak passes at 50/200 only; old 500/1,200 samples are invalid for this gate |
| No Godot references in simulation library | Pass | Project boundary/build evidence |
| Valid versioned save header and migration behavior | Pass | M0.05/M0.09 |
| Low-poly readability / atmosphere | Unverified | Existing visuals are technical captures; subjective stakeholder/playtest gate remains |

Current supported recommendation: retain the accepted 50-attendee foundation as the evidenced baseline. Use 200 and 500 only as optimization/stress fixtures until a representative rendered scheduler benchmark proves otherwise. Preserve 1,200 as a future perceived-crowd goal requiring separately approved optimization/LOD work.
