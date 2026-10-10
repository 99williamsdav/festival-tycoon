# Festival Tycoon

A small-scale festival management game set on a working farm in Lower Wittering. You plan a festival on the farm field, book the bands, hire the crew, lay out the site, then watch the day play out with every guest, band member and member of staff simulated individually. They queue, drink, eat, dance, get too hot, get stuck in portaloos, argue, collapse and go home. Built in Godot 4.7.2 (.NET) with a deterministic C# simulation underneath.

**Status (10 October 2026):** a playable prototype of the survival campaign's first two tiers. You can play Tier 1, carry your money and debt forward with **Next festival**, and play a busier two-stage Tier 2. Heavy rain, Tier 2's weather challenge, is in progress. Builds go out as Windows and macOS zips for playtesting.

## What's in the game

**The campaign**
- **Survival tiers.** Tier 1 is 25 guests at £10 a head in a heatwave. Tier 2 is 50 guests at £15 a head with a second stage.
- **Clearing a tier.** Finish a festival with no death or Council shutdown, then press **Next festival** on the results paper.
- **Carry-over.** Your cash and the starter loan's debt carry forward, along with reputation, scene credibility, Council favour and band relationships.
- **Failure.** A failed festival goes to a Council hearing, then a retry from that tier's opening state.
- **Perks.** A draft of perks at the start of each festival.

**Planning the festival**
- **Build mode** on the farm field, from one per-tier limits table: water taps, portaloos, bars, food vans, first aid, steward posts, bins and the "Under My Umbrella" marquee for shade. The default layout follows the layout used in playtests.
- **Booking.** Book from a seeded shortlist of 53 catalogue acts. Availability depends on reputation and per-genre scene credibility.
- **Staff.** Hire sound engineers, medics, stewards and maintenance from a graded staff market with personality traits. Tier 1 caps staff at one grade above standard.
- **Food traders.** Each van picks a trader, who pays you a pitch fee: Chip Off The Old Block (chips), Pizza the Action (pizza), or, from Tier 2, Korma Chameleon (curry).
- **Money.** Supplies, stock and rig hire, and a budget with an overdraft. A box office briefing opens each festival.

**The day**
- **Two stages from Tier 2.** The Trailer Stage, plus the Pond Stage by the farm pond on a half-offset timetable. Each stage has its own sound engineer. The two generators pool into one power supply. Guests watch whichever act they most want to see.
- **Guests.** They choose what to do by need, money and taste: drinks, food (only when peckish), water, toilets, rest and dancing to the set.
- **Queues and services.** Queues grow into free ground. Bars and vans keep separate per-stall accounts.
- **Heat and medical.** Heat, thirst and intoxication, with medical collapses, a medic response and rest spots by first aid or under the marquee.
- **Disorder.** Arguments and fights, stewards and interventions.
- **Hazards.** Litter and bins (with wasps when they overflow), stuck portaloo doors, broken taps, puddles and mud.
- **Cows.** A pasture gate that can break and let cows loose to chew cables. Stewards can herd them back.
- **Power.** Generator strain and faults from the power budget.
- **Dav's Lav-Sucker.** Call his tanker to empty full toilets. Guests call it "Dirty Henry".
- **Mood.** A live crowd mood with trend and breakdown, speech-bubble chatter, moments and field notes.

**After the day**
- The results paper has three tabs:
  - **Newspaper**, the public review;
  - **Accounts**, per-stall sales, pitch fees and the cash chart;
  - **Performances**, every set with its peak crowd, hiccups, attendee quotes and how it moved your relationship with the band.
- **Band relationships.** These run from −100 to +100. They move with how each gig went and change the act's fee from −50% to +100%. Bands who liked playing for you are always offered again.

**Not built yet:** heavy rain (in progress), Tier 3 and beyond, loan settlement and bailiffs, same-cuisine competing vans, random relationship events and a classic freeplay mode. See [briefs/FUTURE_IDEAS.md](briefs/FUTURE_IDEAS.md).

## Where to read more

- **[briefs/DECISIONS.md](briefs/DECISIONS.md):** the dated log of every approved decision and what was built. This is the most up-to-date record of the design.
- **[ROGUELIKE_DESIGN.md](ROGUELIKE_DESIGN.md):** the survival campaign's intent: tiers, failure and retry, fair counterplay.
- **[DESIGN_PHILOSOPHIES.md](DESIGN_PHILOSOPHIES.md):** the design principles the project follows.
- **[CURRENT_DESIGN.md](CURRENT_DESIGN.md):** a routing snapshot dated 29 September 2026. It predates most of the above, so check DECISIONS for anything later.
- **[assets/source/DESIGN_INDEX.md](assets/source/DESIGN_INDEX.md):** approved art and its source packages.
- **[briefs/BACKLOG.md](briefs/BACKLOG.md)** and **[briefs/FUTURE_IDEAS.md](briefs/FUTURE_IDEAS.md):** review backlog and parked ideas.
- **[docs/WORKING-AGREEMENTS.md](docs/WORKING-AGREEMENTS.md):** how design, art and code changes are agreed.

The older annual-tycoon design and the M0/M1 roadmap documents (`GAME_DESIGN_SPEC.md`, `IMPLEMENTATION_PLAN.md`, `briefs/M0.*`, `briefs/M1`) are kept for reference only.

## Repository layout

| Path | What it is |
| --- | --- |
| `src/Festival.Simulation` | The deterministic game simulation: a plain `net8.0` library with no Godot reference. Fixed 80 ticks per second (80 ticks is one festival minute). |
| `src/Festival.Persistence` | Save files (gzip JSON) and the save/load coordinators. |
| `src/Festival.ContentAdapter` | Read-only projections the presentation uses (e.g. steward cleanup poses). |
| `src/Festival.Runner` | Headless scenarios and fixtures. |
| `tests/Festival.Tests` | MSTest suite for the simulation, persistence and content. It runs without Godot. |
| `game/` | The Godot 4.7.2 .NET project: world, crowd bodies, HUD (`game/Hud`), audio. |
| `assets/source/` | Art source packages from the designer: Blender scripts, concept boards, verification renders, integration notes. Production files are installed into `game/assets/` (with copies in `assets/runtime/`). |
| `briefs/` | Decisions, task briefs and results. |
| `tools/` | Build, test, run and export scripts. |
| `eng/toolchain.json` | Pinned Godot and template versions with SHA-256 hashes. |

## Prerequisites

- Windows x64, with PowerShell 7 or Windows PowerShell 5.1.
- .NET SDK **8.0.417** x64. `global.json` rejects other SDK versions, so builds can't silently change toolchains.
- Network access for the first bootstrap and NuGet restore.
- About 2 GB free for the repository-local Godot editor and export templates cache.

Godot does not need to be installed globally. The bootstrap downloads the official Godot .NET 4.7.2 editor and matching export templates, checks their hashes against `eng/toolchain.json`, and runs Godot in self-contained mode under `.tools/`.

## Building, testing and running

Run from the repository root:

```powershell
.\tools\bootstrap-toolchain.ps1   # once: fetch the pinned Godot editor and templates
.\tools\build.ps1                 # build everything (Debug by default)
.\tools\test.ps1                  # fast test suite; add -Slow for whole-day soak tests and probes
.\tools\run-game.ps1              # launch the game from source
.\tools\export-windows.ps1        # -> artifacts\windows\FestivalTycoon.exe (+ its data_ folder)
.\tools\export-macos.ps1          # -> artifacts\macos\FestivalTycoon.zip (universal, ad-hoc signed)
```

- **Windows exports** come as two parts: the exe and the `data_Festival.Game_windows_x86_64` folder beside it, which holds the C# assemblies. Ship both together, e.g. zipped into one folder.
- **The macOS zip** is unnotarised, so on first launch use right-click → Open.
- **Headless check:** `.\tools\run-game.ps1 -Headless -QuitAfter 2` runs a startup check without graphics.
- **Debug builds** are the default for build, test and run, because Godot's development launcher loads Debug assemblies.

**Playtest launch flags:**
- `--start-tier 2` opens a Tier 2 festival as if Tier 1 had just been completed.
- `--pond-stage-trial` tries the Pond Stage at Tier 1. The title screen also has a **Try the Pond Stage** button for this.

To check the build from a clean state, close Godot and run `.\tools\clean-generated.ps1`, then build, test and export. The clean script removes only an explicit allowlist (`artifacts/`, `game/.godot/` and the known projects' `bin/` and `obj/`), and keeps the downloaded toolchain.

## Engineering conventions

- **Determinism.** Each step of the simulation is deterministic, so saves replay identically.
  - No wall-clock time, no `string.GetHashCode`, and no unordered dictionary or hash-set iteration feeding results.
  - Random rolls go through the keyed RNG helpers.
  - Restore validation re-derives everything it can, and `CanonicalStateHasher` hashes the authoritative state.
- **Save versioning.** Any change to what a save holds, or to how saved state plays out, bumps the save id (`r0-build-vNN` in `game/Main.cs`). Saves live in a per-version folder, so old saves are simply not offered. New optional fields use `JsonIgnore(WhenWritingNull/WhenWritingDefault)` so unrelated hashes stay stable.
- **Single-stage behaviour pin.** `SingleStageBehaviourTests` pins a digest of a Tier 1 day. Re-pin it only deliberately, in its own commit, when an approved change is meant to alter Tier 1 play.
- **Art workflow.** New graphics are designed concept-first: the designer delivers a concept board, the user picks, then production assets arrive with an `INTEGRATION.md`. Music puns are encouraged wherever things get names.
- **Known failing test.** `PerpendicularAndDiagonalCrossingsDischarge` covers the unused `TrafficProof` prototype and has failed since it was added. Everything else in the fast suite is expected to pass.

## Toolchain decision (September 2026)

Godot .NET 4.7.2 stable with matching .NET export templates, .NET SDK 8.0.417 and MSTest.Sdk 4.4.0. Godot 4.7.2 was the latest stable maintenance release; Godot's C# support needs the .NET-enabled editor and .NET 8 or later. Web export isn't available for Godot C# projects, so the targets are Windows and macOS.

References: [Godot 4.7.2 downloads](https://godotengine.org/download/archive/4.7.2-stable/), [C# basics](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html), [command-line export](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html), [MSTest SDK](https://www.nuget.org/packages/MSTest.Sdk/4.4.0).
