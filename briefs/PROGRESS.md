# Foundation progress

Implementation began 7 September 2026. Update rows only when work actually occurs; detailed evidence belongs in a linked results file as described in [README.md](README.md).

| Brief | Status | Implementation / evidence | Open checks |
|---|---|---|---|
| M0.01 | Accepted | [Toolchain, projects, tests, Windows export and launch evidence](results/M0.01.md) | None |
| M0.02 | Accepted | [Deterministic clock, IDs, commands, PRNG streams, hashing and independent review](results/M0.02.md) | None |
| M0.03 | Accepted | [Versioned content schema, validation, canonical hash, fixtures, tests and independent review](results/M0.03.md) | None |
| M0.04 | Accepted | [Atomic purchase, balanced ledger, stock accounting, idempotency tests and independent review](results/M0.04.md) | None |
| M0.05 | Accepted | [Versioned gzip saves, complete DTO mapping, checksum, migration gate, atomic replacement, backup, recovery tests and independent review](results/M0.05.md) | None |
| M0.06 | Accepted | [Fixed Lower Wittering Farm scene/read model, approved art integration, stable-ID picking, four-view camera, persistent HUD, Windows screenshots, automated/runtime evidence and independent review](results/M0.06.md) | None |
| M0.07 | Accepted | [Static deterministic traversal, continuous AI retargeting, optimal terrain-cost A*, persistence/hash coverage, around-barn demonstration and review-repair evidence](results/M0.07.md) | None |
| M0.08 | Accepted | [Autonomous physical queue, hardened service/exit ownership, duplicate future-exit rejection, invalid-front-intent cleanup, timed atomic purchases, 66 passing tests and departure-complete exported evidence](results/M0.08.md) | None |
| M0.09 | Accepted + corrective follow-up | [Arrival-based queueing/rejoin lifecycle, presence-aware save migration, connected counter layout, view-relative controls, deterministic walking-speed variation, 79 passing tests and exported stress evidence](results/M0.09.md) | None |
| M0.10 | Accepted — performance gate failed at ceiling | [Unit-correct 50/200 representative results, 500 functional stress result, bounded 1,200 structural-fail evidence, strict M0 acceptance matrix and independent review](results/M0.10.md) | Representative rendered speed/frame targets and ceiling memory remain unverified; 200/500 are functionally correct but below real-time, and 1,200 remains a structural Fail |
| M1.00 | Accepted measurement — hard gate failed; temporary M1.01 progression exception recorded | [One-world 50-person fixture, deterministic/save/collision proof and representative monotonic-wall-clock measurements](results/M1.00.md) | Mean exceeds 60 FPS, but stable 60 FPS fails at p50/p95/p99; interactive save/report input latency is Unverified. Targets are unchanged and M1.11 remains the final gate |

For each completed task, record changed files, exact verification commands, observed results, reviewer/QA identity and limitations in its results file. Do not replace failures or unavailable checks with an unqualified completion claim.
