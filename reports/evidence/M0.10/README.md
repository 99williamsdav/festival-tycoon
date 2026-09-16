# M0.10 evidence manifest

All JSON files are direct `Festival.Runner --benchmark` output. Timing is machine-dependent; authoritative hashes and completion accounting are deterministic. The runner is unpaced: current files report `UnpacedGameSpeedCapacity = 1000 / (80 × mean tick ms)`, not attained requested speed.

- `headless-50-{wide,narrow}-1x.json` and `headless-200-{wide,narrow}-1x.json`: repaired 600-warm-up / 300-measurement runs. Each records all eight sessions and all declared agents active at both sample boundaries plus true process peak working set.
- `representative-500-wide-corrected-summary.json`: unit-corrected summary of the earlier representative 600+1200 window. `legacy-units-headless-500-wide-1x.json` is retained as its immutable raw source; its old attained-speed and working-set fields are invalid.
- `legacy-unpaced-*-requested-4x.json`: superseded unpaced runs, renamed to prevent accidental use. The requested label did not alter scheduling; retain these only as functional/hash evidence, never requested/attained speed evidence.
- `diagnostic-prearrival-1200-wide.json`: deliberately bounded 4+12 tick pre-arrival diagnostic. Its legacy attained-speed field is invalid; the file is not comparable with the standard window or a performance pass.
- `rendered-1200-wide-timeout.txt`: exported launch timeout; no rendered frame was obtained.
- `rendered-50-wide-1280x720.txt` and `.png`: required exported 50-agent early-frame launch capture.
- `rendered-500-wide-1280x720.txt` and `.png`: successful highest-feasible exported early-frame launch capture. The 500 image was inspected; it shows the farm/HUD disclosure before attendees enter the visible farm, so it is not a steady-state crowd capture.

Primary commands:

```powershell
.\tools\run-crowd-benchmark.ps1 -Agents 200 -Passage wide -Speed 1
.\tools\run-rendered-benchmark.ps1 -Agents 50 -Passage wide -TimeoutSeconds 300 -EvidenceDirectory reports\evidence\M0.10
.\tools\run-deterministic-fixture.ps1
.\tools\run-save-roundtrip.ps1
.\tools\run-foundation-check.ps1
.\tools\test.ps1
```

The original 1,200 full-fidelity target is preserved as a failure. See `briefs/results/M0.10.md` for interpretation and `reports/M0_ACCEPTANCE.md` for the M0 gate.
