# User-supplied incident audio — local-private-playtest gate

These four files were placed locally under `assets/source/audio/crowd/` on
24 September 2026. On 25 September the user stated that they deliberately
selected Creative Commons/free-use recordings and authorized **local private
playtesting only**, while reserving exact source/licence checks for later.
Byte-identical copies now live under git-ignored `game/assets/audio/crowd/`
for that local run. The original filenames and hashes are preserved. The
Windows export preset excludes that folder; a local PCK filter check confirmed
that no clip was packed. Neither source nor runtime audio is committed, pushed
or cleared for public distribution.

| Exact file | Metadata inspection | SHA-256 | Required before production/distribution |
| --- | --- | --- | --- |
| `background-crowd.wav` | 96.863 s; 48 kHz stereo 24-bit PCM. Embedded tags name Roger & Sarah Bansemer, “Painting and Travel,” episode 305 “Cape Porpoise,” with a 2023 Roger Bansemer copyright tag. | `FF53034DE6C278A5F85F6F8FB537056C2EBE966C971517B2DA84341B713B40DE` | Confirm this is actually the intended ambient crowd recording; provide its source, recording owner and game-loop/redistribution permission. |
| `generator-explosion2.wav` | 3.869 s; 44.1 kHz mono 16-bit PCM; LMMS/libsndfile encoder tag only. | `0194830340241F3C1D051AD689CC54A5E7F8DE03C53EA29A4FFD76C2F357FD13` | Creator/source and licence or written game/redistribution permission. |
| `exaggerated-female-scream.wav` | 1.542 s; 48 kHz mono 32-bit float PCM; Adobe Audition 2017 creation tags only. | `A5DF02C3EE56D1B69BE1F6BB6060288558BB9E1C8CC0CAFC8C822A18E9546312` | Performer/recording owner, source and licence or written game/redistribution permission. |
| `exaggerated-male-scream.mp3` | 1.140 s; 44.1 kHz mono MP3; no useful attribution/license tags. | `F0824FDA6DC98DD3CF78E55C1654E4D9EF1BCCCF45C5419EA3D0B3E515F469F1` | Performer/recording owner, source and licence or written game/redistribution permission. |

Metadata and the user's recollection are not proof of a licence. Exact source
links, licence names/versions, attribution requirements and redistribution
terms remain to be checked for all four files. The ambient recording's unrelated
programme copyright tags make its content identity especially important to
confirm. Its audible content and loudness also need a human audition before any
public package or mix acceptance. The current presentation routes these clips
through the shared `Outdoor Stage` bus and mute control, with conservative
per-cue gain and distance attenuation.

## Local audition (PowerShell)

Run the Godot project directly, **not** the approved `r003-followup-approved-v1-2` EXE (that export intentionally contains no supplied clips):

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
human audition. Keep the copied clips and any capture private.
