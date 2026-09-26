# Rock loop v2 — faster review-only preview

Responds to the request to speed up v1 and make the groove rockier. Regenerated at **115 BPM**, not time-stretched or pitch-shifted. Eight complete 4/4 bars give **16.695646 s** after sample rounding (368,139 samples), rather than forcing a partial bar into 16 seconds. Original v1 is preserved separately.

The saturated guitar/power-note riff and bass remain central. Extra restrained palm-muted pickups, more driving kick syncopation and a stronger backbeat increase momentum without adopting Punk's frantic 180 BPM strumming. No Pop piano hook or Electronic synth arpeggio is added. Instruments are deliberate synthesized prototype approximations, not live recordings.

## Source and validation

`generate_rock_preview_v2.py` is the editable deterministic source, using original note patterns, noise-excited feedback strings, saturation, simple smoothing, additive bass/kick and generated drum noise. No samples, soundfonts, external tracks, vocals, downloads or Mureka access. Fixed seed 2026092601.

Mono **22,050 Hz, 16-bit PCM WAV**. Circular tail mixing and a 4 ms boundary taper protect the repeat seam. Format/sample-count/finite-value checks and zero first/last sample passed. FFmpeg EBU R128 measured **−18.5 LUFS integrated**, **−2.8 dBFS true peak**, **1.2 LU loudness range**; no clipping. Human audition must still judge the seam and musical quality.

Exact WAV SHA-256: `e366a5e04987a4a01a0ecfaaadf2a19b9f781417320f6f7962e843ee47c05b40`. `technical.json` records final numerical measurements. Local preview only, awaiting exact approval. No repository/runtime copies, integration, commits, crowd edits or gameplay changes.
