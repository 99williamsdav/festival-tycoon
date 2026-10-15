# Forecast mosaics — 15 October 2026

Candidate source/native integration of four user-approved Designer pieces. The full handoff (`REVIEW-NOTES.txt`), manifest, exact masters, prompts, gallery/context renders and references are preserved under `assets/source/ui/forecast-weather-mosaic-suite-v1/`. Their pre-approval status is retained as provenance; IMPLEMENTATION.md records the subsequent user approval.

Pleasant → pleasant-fine.png; Mild → mild.png; Cloudy → cloudy.png; Showery → showery-mixed.png. The four runtime PNGs are byte-identical to the supplied masters and manifest hashes. All are 1536×1024 (3:2). Native forecast continues to use its original 216×144 KeepAspectCentered slot (324×216 at 1080p).

Heatwave and HeavyRain temporarily use the unchanged existing challenge masters heatwave-b.png and heavy-rain-a.png. The new matching forecast replacements are not approved and are not integrated. Survival challenge art is untouched. No regeneration, weather/RNG/text/schedule/time-format changes, gameplay changes or automation.

Builder: clean game build; source/native 720p replay passes all 262 checks, exit 0 and empty runtime stderr. Twelve added assertions verify condition-specific loaded resource paths, 1536×1024 dimensions, slot dimensions and aspect-fit for all six conditions. Existing forecast/menu/save/live/shared-time checks remain. Evidence: reports/evidence/forecast-mosaic-suite/builder-native/. Asset import warnings are retained separately in import logs; runtime success does not erase them. Fresh focused independent review remains required.

This is separate from the already committed single-bar display change and ongoing cow-herding movement work. No Windows export was performed: the existing accepted r2 EXE is not updated by this source/native integration. The herd pacing implementation has not yet changed gameplay source at this checkpoint.

## Final six-image approval and validation

The user/coordinator approved the full suite. Final handoff is approved-six-runtime/HANDOFF.txt with manifest-final.json. Heatwave now resolves forecast/heatwave-forecast-v2.png and HeavyRain resolves forecast/heavy-rain-forecast-v3.png. Historical pending statements above describe the earlier checkpoint only and are superseded. All six runtime files are byte-identical to approved masters. Survival challenge images remain unchanged.

Final builder build is clean; final-six-builder-native passes262/262 checks, actual exit0, empty runtime stderr. Final-six-checkpoint.json pins84 files including protected gameplay/Survival sources. The first-four evidence remains preserved separately. No export has been made for this image-only checkpoint.
