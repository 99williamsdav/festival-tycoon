# Barn Stage integration

The approved split barn replaces the large barn's presentation at its existing origin, yaw and scale. The measured interior and two front doorways are traversable; walls and deck edges remain solid. The separate reused riser faces west, lengthways down the hall, with its access module rotated about the original shared origin. Existing Duck/Pond assets remain unchanged.

Normal play includes the physical interior, permanent rain cover, kit, lighting, sparse building pick targets and occupancy/selection reveal. Normal programmes still use Trailer alone or Trailer plus Pond. **Barn music is verified only in the explicitly labelled controlled fixture**, which borrows the existing second-slot timings, roster, paid crew and generator allocation under a distinct `stage.barn` identity. No Barn campaign unlock, capacity, pricing, budget or third programme has been assigned. Diagnostic sessions reject campaign persistence and never open player saves. Normal saves use the new v92 geometry compatibility namespace; older files remain untouched.

## Visibility and geometry

Nine roof/wall/door/truss surfaces use the shared .30 alpha, .25-second reveal, one-second empty hold and .5-second recovery helper. Barn retains backface culling, per-instance materials and full shadow-only twins; marquee defaults remain unchanged. Floors, framing, signs, people and kit remain opaque. Physical guests, performers and staff reveal the shell, including a lone early performer; selection also reveals an empty barn. Occupancy does not change collision or rain cover. Ground/track overlays are clipped at the approved flush floor rather than modifying the delivered asset.

Navigation uses conservative .5 m cells with wall/capsule margins and the measured two front passages. Listening points avoid the front cross-aisle, band stairs and track. There are 60 geometric listening destinations, not an assigned economic capacity. All three band roles use the real stair route, deck height and west-facing presentation. Physical Barn deck heights also apply to staff and hands-on players without activating a programme.

Independent review identified a routed capsule/handrail overlap on the right stair column. The repair uses one left stair column and continuous segment guards for ordinary navigation, separation offsets and manual player movement. The high right rail is X=7.515..7.565; accepted roots remain below X=7.10 through its expanded contact zone. A .38 m capsule therefore clears it, while the west root limit keeps feet on tread tops. Segments are rejected, never clamped or teleported. Nine band waiting places now use the west flank of the stage, avoiding the narrow east-wall lane that stalled the second act.

A crowded bidirectional test also exposed a secondary-door jam when its conservative corridor had only one column. Three grid columns now fit the measured 2 m opening; continuous frame margins bound yielding roots to the safe part of the doorway. The real movement test completes all four opposing entry/exit routes with 58 listening positions occupied and their destinations reserved.

The approved Designer package is retained under `assets/source/environment/barn-stage-v1`, including original reference snapshots, source Blend, runtime, contracts, measured geometry and native import verification. Required provenance hashes were checked before integration; the runtime asset is copied unchanged. No Blender regeneration or new art approval gate was introduced.

## Verification

Final source build has zero warnings/errors. Recorded targeted results (overlapping runs, not additive unique counts):

- Eight Barn tests: both passages/interior/deck/waiting routes, permanent coverage including normal save restore, unchanged normal allocation and diagnostic save guard, actual three-person arrival/climb/play, all-role occupancy, real steward traversal to an indoor fight, actual rain exposure update, and independent generator interruption.
- Latest clearance batch: 17/17 Barn, pinned single-stage and build-route checks passed after tightening the exit/wall margins and backstage waiting locations.
- Scoped regressions: 67/69 initially passed; the two disk save/replay cases were blocked by temporary-directory sandbox access and both passed with the required filesystem permission. No simulation repair or repinning was needed.
- 42 focused Barn/fight/staff cases passed before the final eighth Barn power case was added; subsequent eight-case Barn runs passed. Final physical-deck checks: 18/18 Barn/steward stage cases and 12/12 actual hands-on autopilot cases passed.
- Review repair batch: 74/76 shared movement/fight/pinned cases passed initially. One failure was the exposed single-column exit jam; the other was an older navigation assertion expecting the now-open interior to remain blocked. The assertion now verifies the open hall and solid rear wall while retaining the north-to-south detour requirement. A subsequent 15/15 Barn/rear-wall/pinned batch passed. Final twelve Barn tests additionally exercise first-act descent/second-act punctual play, crowded/reserved opposing doorway walks, continuous stair/door segments and actual manual player climb/lateral limits.

The opt-in native harness advances the production simulation and records 450 frames at logical 30 fps (15 seconds), spanning tick 8000 through 9200 and the real first set at tick 9000. A labelled elapsed interval then lets actual listeners arrive. It checks west-facing bodies, .9 m deck height, exact active-band pause and menu-over-paused-host poses, nine fade surfaces/culling/shadow twins, per-instance isolation and shared nearest-hit occupant picking.

The camera inspection holds authority and captures all four actual azimuths at sizes 18 and 62 under day, late light and an explicit rain input override. Focus Y=3.5 is an inspection view; size 18 can crop roof edges and size 62 makes people small. Slate/stone layers retain visible tint accumulation, especially from rear views; opaque frames and the neighbouring small barn can occlude some people. Picking requires an exact visible person hit in each view, not every person unobscured. No natural campaign rain-balance, exhaustive crowd or performance-speedup claim is made.

Separate occupancy/selection inspections explicitly reposition actors with authority held; those shots are not movement evidence. The diagnostic paces capture-free frames against wall time so the existing one-second hold is measured honestly under `--fixed-fps`; normal production fade timing remains unchanged.
Re-entry is checked after an actual empty recovery has begun and must return every fade group to .30.

## Checkpoint and handoff status

Source/native verification is complete: **54/54 native checks passed**, with 24 camera/weather images, six occupancy images and a verified 450-frame, 30 fps, 15-second movie. Actual first-set start is tick 9000. Native logs contain no warnings/errors. The fresh independent **GPT-6 Astra MEDIUM source review passed**, including its own 12/12 Barn test rerun and all 130 checkpoint hashes (80 source, 38 native evidence, 12 verification files). Both original review findings were repaired and independently accepted. See `barn-stage-independent-review.md`; accepted checkpoint SHA-256 is `CA87AACB0DD43E84C1F38A5165267E058FAA8E6CE4CE27B8984F0C0DA7B809E1`.

The final recovery diagnostic observes a live tween transition and verifies first alpha rise occurs after at least 1000 wall milliseconds. This replaced a fixed-frame assumption that failed when renderer stalls correctly let the wall-clock hold expire earlier. Production timing was not changed. Failed/provisional development captures remain under ignored `artifacts` and are excluded from final evidence. The preparation review accepted preparation only.

Actual exported-build acceptance remains pending. No accepted EXE handoff is claimed until its new addendum passes.

The final Windows delivery will contain the EXE and entire adjacent data directory, with no ZIP. The bounded exported-build addendum will refer to the copied delivered EXE and its actual capture, not a source-only run.
