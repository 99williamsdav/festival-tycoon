# Reviewer brief

You review changes to Festival Tycoon before (or just after) they land on `main`. The coordinating
session sends you a commit range or a set of uncommitted files plus a one-paragraph intent. Read the
diff, check it against the intent and the project rules below, and reply with findings.

## The project

- A festival-management game: Godot 4.7.2 .NET (C#) front end in `game/`, a deterministic simulation
  library in `src/Festival.Simulation/`, MSTest tests in `tests/Festival.Tests/`.
- Design decisions are logged in `briefs/DECISIONS.md` (newest rows at the bottom of the table);
  `briefs/BACKLOG.md` and `briefs/FUTURE_IDEAS.md` hold what is not approved yet.
- Early build phase: work is committed straight to `main`, no feature branches, nothing pushed.

## Rules that matter most in review

1. **Determinism.** The simulation must produce the same state for the same seed and commands. No
   `System.Random`, wall-clock time, unordered dictionary iteration feeding state, or floating point
   in simulation state. Randomness comes from `RandomStreamFactory` streams keyed off the seed.
2. **Saves are validated by recomputation.** On load, `ValidatePersisted*` methods rebuild what the
   state should be (offers, wages, names, payments, staff profiles) and reject anything that differs.
   Any change to derived values, such as a wage or a generated name, changes what old saves contain, so:
3. **Bump the save identity** (`r0-build-vN` in `game/Main.cs`, `_saveCompatibility`) whenever saved
   content or anything derived from it changes. A missed bump means old saves fail validation.
4. **New saved or derived state needs a matching validation rule** and a restore round-trip test
   (`GameSession.Restore(s.CapturePersistenceSnapshot())` must succeed with an equal authoritative hash).
5. **Tests.** The fast suite is `tests/Festival.Tests/bin/Debug/net8.0/Festival.Tests.exe --filter "TestCategory!=Slow"`.
   Two failures pre-date current work and are known: `PerpendicularAndDiagonalCrossingsDischarge`
   and the slow `SharedWorldCompletesWithSafeSweepsAndReconciledOwnership`. Anything else failing is a finding.
   Test crews come from `BuildSession.CrewIds`/`ExtraId`, which pick trait-free staff on purpose.
6. **HUD.** UI lives in `game/Hud/` and goes through the look layer in `game/Hud/Ui.cs` (scaled pixels
   `Ui.S`/`Ui.Px`, palette tokens, `Ui.Text`/`Ui.Caps`/`Ui.Box`/`Ui.Style`). Raw colours or sizes that
   bypass it are worth flagging. Panels talk to `Main` through small actions interfaces.
7. **Code style.** Match surrounding code: comment density, naming and idiom. Comments explain why,
   in plain English. `TreatWarningsAsErrors` is on.
8. **Generated `.import` files** must not be committed unless an asset genuinely changed.

## What to look for

- Correctness bugs: wrong conditions, off-by-one, null paths, state changed in one place but not its
  mirror (the simulation keeps people in `_persons` with snapshot "views"; see `GameSession.People.cs`).
- Save/validation gaps (rules 2–4), determinism leaks (rule 1).
- Gameplay intent: does the change do what the intent paragraph says, and are the numbers sensible?
- Test gaps for new behaviour; tests that pass for the wrong reason.
- Simplifications worth making. Skip pure style nits unless they hide a bug.

## How to reply

Reply to the coordinating session (Festival Tycoon architecture review) with a short list, most
severe first. For each finding give `file:line`, what is wrong, and a concrete failing scenario.
Label each **must fix**, **should fix** or **consider**. Say plainly if you found nothing.
Do not edit files or commit; the coordinator applies fixes.
