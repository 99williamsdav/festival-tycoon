# Original Indie and Metal placeholders

Producer supplied and authorized these basic live-set tracks on 2 October 2026. This copy preserves the original `generate.py` and `technical.json` unchanged from `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/audio_prototypes/new-genres-v1`.

Run `generate.py` with Python and NumPy to reproduce sibling WAVs and technical metadata. It synthesizes all instruments without external samples or artist imitation. No vocals or crowd are baked in. Frozen WAVs are preserved in `assets/runtime/audio/`; matching Godot copies are in `game/assets/audio/`.

Indie: 112 BPM, sixteen bars, 34.286 seconds; clean jangly strums, picked hook, bass and backbeat. Metal: 148 BPM, sixteen bars, 25.946 seconds; distorted power chords, palm-muted gallops, double kick and a later lead part. Both are stereo, 22,050 Hz, 16-bit PCM. These are temporary synthetic placeholders; no realistic recorded instrumentation or manual listening QA is claimed.

The source has circular tails and a short endpoint taper. Godot imports use whole-file forward looping, with trimming and normalization disabled. Older genre import behavior is preserved. Runtime loop wrapping, pause/resume and changeovers are checked separately from the composition/source metadata.
