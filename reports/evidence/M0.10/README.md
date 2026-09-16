# M0.10 evidence manifest

All JSON files are direct `Festival.Runner --benchmark` output. Timing is machine-dependent; authoritative hashes and completion accounting are deterministic.

- `headless-50-wide-1x.json` and `headless-50-wide-4x.json`: current 200-warm-up / 300-measurement paired run.
- `headless-200-wide-4x.json`: current requested-4× run. The paired 1× result predates the shortened measurement window but reaches the same final tick and hash.
- `headless-500-wide-4x.json`: current explicit requested-4× run; it completes correctly but misses real-time, 4× and tick-cost targets.
- Other 50/200/500 JSON files: earlier, longer 600-warm-up / 1,200-measurement runs retained as valid measurements.
- `headless-1200-wide-1x.json`: deliberately bounded 4+12 tick pre-arrival diagnostic. It is not comparable with the standard window and is not a performance pass.
- `rendered-1200-wide-timeout.txt`: exported launch timeout; no rendered frame was obtained.
- `rendered-50-wide-1280x720.txt` and `.png`: required exported 50-agent early-frame launch capture.
- `rendered-500-wide-1280x720.txt` and `.png`: successful highest-feasible exported early-frame launch capture. The 500 image was inspected; it shows the farm/HUD disclosure before attendees enter the visible farm, so it is not a steady-state crowd capture.

Primary commands:

```powershell
.\tools\run-crowd-benchmark.ps1 -Agents 500 -Passage wide -Speed 4
.\tools\run-rendered-benchmark.ps1 -Agents 500 -Passage wide -Resolution 1280x720 -EvidenceDirectory reports\evidence\M0.10
.\tools\run-deterministic-fixture.ps1
.\tools\run-save-roundtrip.ps1
.\tools\run-foundation-check.ps1
.\tools\test.ps1
```

The original 1,200 full-fidelity target is preserved as a failure. See `briefs/results/M0.10.md` for interpretation and `reports/M0_ACCEPTANCE.md` for the M0 gate.
