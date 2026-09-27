# Non-blocking backlog

Small follow-ups that should not hold up the current brief. Review this list during a quiet period; promote an item into a brief when it has clear scope and acceptance criteria. Completed items should be marked done, not silently dropped.

| ID | Follow-up | Notes |
| --- | --- | --- |
| B-001 | Improve the farmhouse shadow | Visual polish; previously deferred. |
| B-002 | Refine crowd placement and movement around a dense stage audience | Keep the current organic dispersal, but improve approach paths, crowding tolerance, and excited fans' ability to get near the front. |
| B-003 | Calibrate crowd audio | Tune cheering/booing loudness and timing against crowd size and enthusiasm; fill remaining gaps with suitable licensed recordings. |
| B-004 | Revisit occasional frame hitches under larger crowds | Accepted for now; measure again as simulation complexity grows. |
| B-005 | Revisit person-to-person crossing and crush behaviour | Needs deliberate crowd-interaction design before implementation. |
| B-006 | Revisit attendee cue readability under dense crowds | R0.03 added person-anchored distress and decision cues; assess whether they remain legible without becoming noisy as scenarios grow. |
| B-007 | Fix the known traffic-kernel crossing test failure | `TrafficKernelProofTests.PerpendicularAndDiagonalCrossingsDischarge(True)` is the one remaining failure in the otherwise passing full suite as of R0.03. |
| B-008 | Done: face idle staff outward from their posts | R0.05h implements Riley/Jordan and hired peers' derived idle outward facing; actual motion/role jobs/intervention/collapse keep precedence, return verified. [Evidence](results/R0.05h.md); human feel unverified. |
| B-009 | Present festival time as a realistic day clock, coupled to future day/night lighting | User-requested follow-up, recorded 26 September 2026. Translate the abstract elapsed festival clock into a believable day span; midday–11pm is an example, not an approved final schedule. Define the time mapping and lighting relationship before implementation. Backlog only; current timing and HUD remain unchanged. |
| B-010 | Done: omit unused generic service point from normal presentation | R0.05h authorized removal of normal mesh/pick only, retaining immutable data/assets and explicit interaction/navigation/service diagnostics; final actual fixture raypick passed. No live commercial-service change. [Evidence](results/R0.05h.md). |
| B-011 | Done: align small barn doors with big barn | Latest27September user instruction supersedes26September inward wording: both door fronts+Z, unchanged centres/big yaw/assets; narrow rotated footprint and routes verified. Old development files preserved, fresh compatible saves required. [Evidence](results/R0.05h.md). |

Backlog items are not approval gates. If a follow-up starts affecting safety, core gameplay, or a brief's acceptance criteria, raise it for prioritisation rather than leaving it here.
