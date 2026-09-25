# User-supplied incident audio — Freesound sources verified

These four files were placed locally under `assets/source/audio/crowd/` on
24 September 2026. On 25 September the user stated that they deliberately
selected Creative Commons/free-use recordings; their Freesound sources were
subsequently checked as documented below.
Byte-identical copies live under `game/assets/audio/crowd/` for runtime use.
The source duplicates remain git-ignored; the runtime copies are tracked and
included in the Windows export. The original filenames and hashes are
preserved below.

| Exact file | Metadata inspection | SHA-256 |
| --- | --- | --- |
| `background-crowd.wav` | 96.863 s; 48 kHz stereo 24-bit PCM. Embedded tags name Roger & Sarah Bansemer, “Painting and Travel,” episode 305 “Cape Porpoise,” with a 2023 Roger Bansemer copyright tag. | `FF53034DE6C278A5F85F6F8FB537056C2EBE966C971517B2DA84341B713B40DE` |
| `generator-explosion2.wav` | 3.869 s; 44.1 kHz mono 16-bit PCM; LMMS/libsndfile encoder tag only. | `0194830340241F3C1D051AD689CC54A5E7F8DE03C53EA29A4FFD76C2F357FD13` |
| `exaggerated-female-scream.wav` | 1.542 s; 48 kHz mono 32-bit float PCM; Adobe Audition 2017 creation tags only. | `A5DF02C3EE56D1B69BE1F6BB6060288558BB9E1C8CC0CAFC8C822A18E9546312` |
| `exaggerated-male-scream.mp3` | 1.140 s; 44.1 kHz mono MP3; no useful attribution/license tags. | `F0824FDA6DC98DD3CF78E55C1654E4D9EF1BCCCF45C5419EA3D0B3E515F469F1` |

On 25 September 2026 the user supplied the original Freesound filenames. Each
ID resolves to a page displaying the CC0 public-domain dedication, and each
page's duration, sample rate, channel count and format match the corresponding
local file. The source claims below supersede the earlier unknown-license gate;
the separate human audition and final mix check still remain. The ambient
file's Bansemer copyright metadata is consistent with the Freesound uploader,
though the page does not explain the unrelated programme tags.

| Local file | Original Freesound filename | Creator | Source and displayed licence |
| --- | --- | --- | --- |
| `background-crowd.wav` | `686975__bansemer__crowd.wav` | Bansemer | https://freesound.org/people/Bansemer/sounds/686975/ — CC0 |
| `generator-explosion2.wav` | `482993__v-ktor__large-explosion-1.wav` | V-ktor | https://freesound.org/people/V-ktor/sounds/482993/ — CC0 |
| `exaggerated-female-scream.wav` | `400183__tomattka__girl-screaming_01.wav` | tomattka | https://freesound.org/people/tomattka/sounds/400183/ — CC0 |
| `exaggerated-male-scream.mp3` | `339309__kalibrk__jirkascream.mp3` | Kalibrk | https://freesound.org/people/Kalibrk/sounds/339309/ — CC0 |

These are source-page and file-metadata matches, not a byte-for-byte match
against a fresh Freesound download. Freesound displays CC0, which permits
commercial use without mandatory attribution, but the source credits are
retained here. The current presentation routes the clips through the shared
`Outdoor Stage` bus and mute control, with conservative per-cue gain and
distance attenuation. A human mix/listening check is still pending.

## Local audition (PowerShell)

Run the Godot project directly or use a newly exported Windows EXE. Older
R0.03 exports intentionally contain none of these supplied clips:

```powershell
& 'C:\Projects\festival-tycoon\.tools\godot\4.7.2\editor\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe' --path 'C:\Projects\festival-tycoon\game'
```

Dismiss the splash, book the folk act and steward, buy equipment, then choose
`START FIXED ROSTER`. Listen for quiet crowd ambience during the running show;
`MUTE AUDIO`/`UNMUTE AUDIO` controls the shared bus. In the Hot scenario, leaving
Guest 20 without water, rest or medic intervention reaches the medical witness
cue after the visible warning. This is a fictional fixture, not a clinical model.
The generator explosion path is covered by the cue test, but its rendered Debug
capture timed out before death; live timing and the actual mix need a separate
human audition.
