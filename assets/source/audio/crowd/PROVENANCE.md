# User-supplied incident audio — provenance gate

These four files were placed locally under `assets/source/audio/crowd/` on
24 September 2026. They remain untracked and are **not** copied into `game/`,
committed or included in any export. The R0.03 presentation code checks for
optional `res://assets/audio/crowd/` resources, but plays no supplied recording
until rights and exact candidate identity are confirmed.

| Exact file | Metadata inspection | SHA-256 | Required before integration |
| --- | --- | --- | --- |
| `background-crowd.wav` | 96.863 s; 48 kHz stereo 24-bit PCM. Embedded tags name Roger & Sarah Bansemer, “Painting and Travel,” episode 305 “Cape Porpoise,” with a 2023 Roger Bansemer copyright tag. | `FF53034DE6C278A5F85F6F8FB537056C2EBE966C971517B2DA84341B713B40DE` | Confirm this is actually the intended ambient crowd recording; provide its source, recording owner and game-loop/redistribution permission. |
| `generator-explosion2.wav` | 3.869 s; 44.1 kHz mono 16-bit PCM; LMMS/libsndfile encoder tag only. | `0194830340241F3C1D051AD689CC54A5E7F8DE03C53EA29A4FFD76C2F357FD13` | Creator/source and licence or written game/redistribution permission. |
| `exaggerated-female-scream.wav` | 1.542 s; 48 kHz mono 32-bit float PCM; Adobe Audition 2017 creation tags only. | `A5DF02C3EE56D1B69BE1F6BB6060288558BB9E1C8CC0CAFC8C822A18E9546312` | Performer/recording owner, source and licence or written game/redistribution permission. |
| `exaggerated-male-scream.mp3` | 1.140 s; 44.1 kHz mono MP3; no useful attribution/license tags. | `F0824FDA6DC98DD3CF78E55C1654E4D9EF1BCCCF45C5419EA3D0B3E515F469F1` | Performer/recording owner, source and licence or written game/redistribution permission. |

Metadata is not proof of licence. The user has been asked for source links and
usage terms for all four files. The first recording's unrelated copyright tags
make both content identity and rights particularly important to confirm.
