# Development tier starts — copied Windows EXE review

**PASS.** Independently verified and ran the actual delivered Windows executable on 14 October 2026. No actionable finding in this bounded export review. The source review remains in `development-tier-starts-independent-review.md`.

## Identity and delivery

- Source commit: `e4f4b69f81b1c85ce8dff0682df4997f02608ebc` (verified HEAD).
- Executable: `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/playtest-development-tiers-2026-10-14/FestivalTycoon.exe`.
- Size: 198,669,424 bytes. SHA-256: `440EFA0E1703B8CE5A9BB7AD8991E907F2880D534121CFE3864C6D7DA43B5740`.
- Independently hashed all 194 delivered files and all 194 export-source files against `exported-build-manifest.json`: no mismatches. The delivery has exactly 194 files, including the adjacent runtime data directory; no ZIP is required.
- All 13 owned source checkpoint hashes match, including the generated diagnostic harness UID. The original twelve reviewed source files remain unchanged.

## Independent execution

Launched the exact delivered executable using `Start-Process`, a hidden window, and arguments `--fixed-fps 30 --quit-after 220 --log-file C:/Projects/festival-tycoon/reports/evidence/development-tier-starts/independent-exported/native.log -- --capture-development-tiers C:/Projects/festival-tycoon/reports/evidence/development-tier-starts/independent-exported --capture-size 1280x720`.

Observed process path matched the delivered executable; PID 16808 completed within 30 seconds with exit code **0**. The harness itself also reported that exact executable path. Its JSON reports **37 checks passed, zero failures**, and the native log reaches `DEVELOPMENT_TIER_EVIDENCE_COMPLETE`. The isolated save-directory check and actual manual-save round trip passed. Normal user campaign saves were not used by the diagnostic host.

Reviewed all eight PNGs produced by this independent run. They show the replaced footer, two development tiers, retained Steward picker, Heavy Rain reveal, normal perk draft, correct Tier 2 budget (£353.35 carry plus £750 receipts = £1,103.35), both stage programmes and actual learned act/relationship indicators, then the fresh Tier 1 Heatwave reveal. No new scoped visual defect is evident at 1280×720. Reset, fresh identity, reveal/menu resume, and management-host restoration assertions all passed.

Independent evidence is retained under `reports/evidence/development-tier-starts/independent-exported/`, including `process-proof.json`, `hash-verification.json`, `development-tier-evidence.json`, `native.log`, eight screenshots and isolated diagnostic saves. Also inspected the builder's exported process proof and completed native log, which corroborate the same delivered executable and 37 passing checks.

## Log qualifications and limits

The export log contains four `Condition "f.is_null()" is true. Continuing.` messages and the recorded exporter exit code is 0. The builder's copied-EXE runtime log is clean. The independent sandboxed runtime emitted one shader-cache creation error at `user://`; it continued with rendering, all eight captures, all 37 checks and exit code 0. This cache-access limitation is recorded rather than describing the independent log as entirely error-free; it did not prevent the reviewed functionality.

This is a bounded native scripted smoke and visual review of the actual copied payload, backed by the earlier simulation tests. It is not a manual full festival playthrough or a claim of coverage beyond the commissioned menu/carryover/save/reset scope.
