# Pop and Electronic music previews v1

Two original sample-free candidates only, awaiting human audition and exact preview approval. No integration or commit; existing folk/punk music, performer graphics and approved recorded crowd/incident sounds are untouched.

| Preview | Arrangement | Format | Measured integrated loudness / true peak |
| --- | --- | --- | --- |
| `pop_loop_v1.wav` | Bright piano-like major-key hook, syncopated chord stabs/clean bass and backbeat | 16 s, 120 BPM, eight 4/4 bars; mono 22,050 Hz 16-bit PCM WAV | −18.5 LUFS / −3.3 dBFS |
| `electronic_loop_v1.wav` | Minor-key additive synth arpeggio, pumping sustained pad, offbeat bass and four-on-the-floor kick | Same format and bar length | −18.5 LUFS / −5.3 dBFS |

These are deliberately synthetic prototype music, not recordings of real performers or final soundtrack approval. They share a tempo but contrast harmonic colour, instrument envelope, melodic structure and rhythm. Human listening must judge recognisability, taste, repetition and in-game balance.

## Source and checks

`generate_genre_previews.py` authors the note patterns and synthesizes tones/percussion from finite mathematical waveforms and deterministic pseudorandom noise (seed 20260926). No downloaded sample, soundfont, vocal, lyric, quoted song or external recording is used. NumPy is the only synthesis dependency. Existing generators are not imported or run, so rejected procedural crowd clips are not regenerated.

The renderer circularly mixes note tails across the bar boundary and applies a 4 ms boundary taper. Checks verify exactly 352,800 mono samples at 22,050 Hz, 16-bit PCM, finite samples and zero first/last sample. FFmpeg EBU R128 measured the table above after mastering trims. This is click protection, not a claim of human-approved looping or musical quality. `technical.json` records final WAV hashes and sample/RMS peak measurements.

## Existing comparison music

Approved temporary Folk: `C:/Projects/festival-tycoon/game/assets/audio/folk_loop_v1.wav`.
Approved temporary Punk: `C:/Projects/festival-tycoon/game/assets/audio/punk_loop_v1.wav`; use as the Rock genre placeholder remains a separate pending user/coordinator decision.

Builder agreed IDs Folk=0, Rock=1, Pop=2, Electronic=3 and the existing live/powered playback convention. One loop per genre may be reused across six acts; no six-track or act-specific score is implied. Preview approval is required before any new audio is copied into runtime/integrated or committed.
