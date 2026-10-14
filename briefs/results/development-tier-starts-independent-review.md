# Development tier starts — independent source review

**PASS.** No actionable findings in the authorized source scope. Reviewed independently on 14 October 2026 against baseline `d40cad64559b28b39b23ceea3e1b0fa42d798df0` and the twelve owned source files pinned in `reports/evidence/development-tier-starts/checkpoint.json`. All twelve source SHA-256 values matched. This is a source/native-editor review; the copied Windows EXE requires the planned separate addendum.

The review follows `docs/WORKING-AGREEMENTS.md` and accepts the coordinator and direct user's approval of the recorded two-star survival clear. A higher-star result is not an acceptance requirement. Unrelated character imports, lockfile, outfit and old UID files are excluded.

## Implementation and provenance

- `game/Hud/StartMenu.cs` replaces the trial footer with a native two-option selector and Back navigation, matching the existing Steward Only interaction. There is no Tier 3 option. Focus and footer restoration are explicit.
- `game/Main.Splash.cs:23` creates Tier 1 through the ordinary fresh-entropy campaign factory. Tier 2 calls `GameSession.CreateRecordedTierTwoStart`, restores the management host, resets the existing views, closes the menu and invokes the existing challenge reveal.
- `src/Festival.Simulation/RecordedDevelopmentStart.cs:13` restores the complete embedded snapshot, checks the normal restore result, verifies Tier 1 and normal next-festival eligibility, and calls canonical `CreateNextFestival`. It does not simulate a festival per click or supply independent cash, standing or rewards. The unchanged restore path verifies the reconstructed authoritative hash (`GameSession.cs:602`).
- The ordinary-command recorder and reproduction test establish actual playable provenance: seed 20260922, normal challenge acknowledgement, first offered perk, default layout, three available acts, purchased stock and paid crew, then ordinary advancement. The recorder evidence reports a byte-identical snapshot with SHA-256 `1802C92ADA0692E67553B0EC15E7374F9B1513952F895ACC8D31A07DE7D5567D`. The independent test reproduced both the completed result and next-tier authoritative hash without private mutations or grants.
- The books reconcile: £477.75 income less £262 expenses, £57.60 sold stock cost and £4.80 write-off = £153.35 net result. Adding the original £200 loan gives £353.35 closing cash; Tier 2 adds only £750 tickets to open at £1,103.35. £200 principal remains owed. No duplicate £450 starting funds or Tier 1 ticket grant occurs.
- Canonical carryover preserves reputation 14, scene credibility `[10,0,10,10,0,0]`, three seen acts and their learned talent, actual relationship history and values (Daisy −1, DJ +10, Harmonium +1), and Favour 1. No invented equipment, water-share use, popularity shift or general knowledge grant appears.
- Tier 2 begins ordinary Heavy Rain preparation, with fresh bookings, staff, stock, layout and perk draft; previous layout remains available to copy. Trailer/Pond allocation and the existing extra medic slot follow canonical tier logic. No progression/economy rule or Barn allocation change is present.
- Normal save namespace and host transitions remain in use. The opt-in diagnostic harness isolates its saves beneath the evidence output directory. Existing management resume, retry, Steward Only and All-Rounder code paths remain intact; native evidence exercises management host restoration and state-preserving menu return.

## Validation and visual review

Independently executed:

`dotnet test tests/Festival.Tests/Festival.Tests.csproj --no-build -- --filter FullyQualifiedName~RecordedDevelopmentStartTests --results-directory reports/evidence/development-tier-starts/independent-tests`

**6 passed, 0 failed**, 17.8 seconds. Covers eligibility/canonical transition equality, financial reconciliation, learned/relationship carryover and resets, normal rain/unlocks/affordable opening, Council retry, and ordinary-command reference reproduction. An initial unsupported `--filter-class` invocation exited before running tests; the corrected invocation above passed.

Also inspected supplied evidence: clean native build (zero warnings/errors); initial combined 24-case run with 22 passes and two sandbox save-access failures; successful two-case approved-access rerun; four further existing save/reset and Steward/All-Rounder cases passing in `mode-regressions.txt`. This represents 28 distinct passing cases across the reported runs, not 28 independently rerun cases.

Reviewed all eight native source screenshots and the 37 passing scripted checks in `source/development-tier-evidence.json`. The menu and development picker are legible and unclipped at the captured 1280×720 size; the existing Steward selector remains available. Heavy Rain appears before the perk draft, Tier 1 displays Heatwave, and the Tier 2 box office correctly shows its tier, £353.35 carried cash, £750 receipts and £1,103.35 total. The programme image shows both stages and the actual learned talent/relationship indicators. No scoped visual defect found.

Evidence is scripted native UI coverage and simulation tests, not a manual full playthrough. No copied release executable has been assessed by this review yet.
