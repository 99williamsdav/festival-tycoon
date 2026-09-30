# Festival Tycoon playthrough showcase

The video and previews are local, ignored artifacts in `artifacts/showcase-r0.05ag/`; they are not committed to the repository. This tracked note preserves their provenance for a fresh checkout.

Deliverable: `festival-tycoon-playthrough-showcase.mp4` (77.5 seconds, 1280×720, 30 fps, H.264/AAC). `thumbnail.jpg` and `contact-final.png` are preview images.

Source game: R0.05ag at commit `850cb3a`. Godot 4.7.2 Mono Movie Maker rendered `showcase-native.avi` directly from the game with the opt-in `--capture-showcase` route. It contains no desktop or other-app recording. The route issues normal Build campaign commands for perk, default facilities, programme, steward, equipment, stock and Start. It then shows naturally moving arrivals and the live first set. The camera moves to the food/bar and toilet, and the session advances to its natural finished result: 20 admitted guests, 4 stars, 66.0% satisfaction, recorded sales and reconciled closing cash. No success or accounting values were fabricated. Simulation time is advanced between selected scenes; the film's opening card says this is a staged in-engine playthrough with selected time jumps.

The edit uses the game's existing mosaic logo, the native captured game audio and the approved in-game `folk_loop_v1.wav` at low underscore volume. There are no downloaded audio/video assets. The edit's four brief title cards separate the intentional time jumps and keep the game UI unobscured.

| Film time | Content |
| --- | --- |
| 00:00–00:02.5 | Title and staging disclosure |
| 00:02.5–00:23.5 | Perk, facilities, three-act programme, steward, equipment and stock |
| 00:23.5–00:35.5 | Gate opens; real staggered arrivals |
| 00:35.5–00:54.5 | First live set, crowd, food van and bar |
| 00:54.5–00:58.5 | Toilet/service view |
| 00:58.5–01:17.5 | Natural newspaper, itemized Accounts and cash reconciliation |

The native capture logged all scene boundaries and `capture complete` after 2,280 frames. FFprobe confirmed 77.5-second H.264 1280×720/30 video and stereo 48 kHz AAC audio. FFmpeg decoded the complete MP4 with zero errors. The final contact sheet and representative frames at the stage, services, newspaper, accounts and closing cash were inspected. Godot emitted an ObjectDB/resource-in-use warning only during exit cleanup, after the movie was finalized; the captured scenes completed without an error.
