# Test suite speed, 16 October 2026

Machine: i7-12700H laptop (6 performance and 8 efficiency cores, 20 threads), 32 GB. MSTest.Sdk 4.4.0 runner, 956 tests.
Raw runner output (every test's duration, from `--show-slowest-tests`) is kept beside this file.

## What was slow

The suite already ran in parallel: `[assembly: Parallelize(Workers = 0, Scope = MethodLevel)]`, so 20 workers. Two
settings made that parallelism mostly waiting:

- **The workstation GC.** Twenty threads, each simulating festival days and allocating heavily, kept stopping for
  collections. `ClassicSurvivalParityTests` takes 65 s on its own in Release, but 18-23 minutes inside a full run.
- **Debug.** Tests ran with the JIT optimiser off. The shipped game runs optimised (ExportRelease).

A third problem showed under load: background saves (`SessionHost`'s cadence save, the preparation draft pipeline) use
`Task.Run`, and with every pool thread held by a test worker they waited for the pool to grow. Two `SessionHostTests`
timed out at 30 s in a Debug run with the server GC, and the draft-pipeline tests took over two minutes each.

## Before and after (wall clock)

| Set | Before: Debug, workstation GC | After: Release, server GC |
| --- | --- | --- |
| Fast suite (untagged tests) | 14 min 16 s (the old fast set of 896 tests; measured afterwards with the thread-pool fix and shared perk days already in, so the real figure was higher) | 2 min 08 s (874 tests) |
| Everyday (`.\tools\test.ps1`) | did not exist | 2 min 23 s (885 tests) |
| Full, parallel (`-Full`) | 33 min 28 s | 7 min 25 s; 12 min 30 s with another session's full Debug run alongside |
| Full, serial (1 worker) | not run | 42 min 43 s (some contention from other sessions) |

How the full run's 26 minutes split, each measured as a full parallel run:

| Configuration | Full suite |
| --- | --- |
| Debug, workstation GC (before) | 33 min 28 s |
| Debug, server GC | 22 min 03 s (and the two `SessionHostTests` timeouts) |
| Release, workstation GC | 23 min 12 s |
| Release, server GC | 8 min 30 s |
| Release, server GC, thread-pool headroom, shared perk days (after) | 7 min 25 s |

Other sessions were running tests during several of these runs. The "before" run had one other test process (the
parity tests, in the main checkout) for most of its length.

## Determinism

The serial run (1 worker), and two parallel full runs, ran the same 956 tests with the same single failure, the known
`PerpendicularAndDiagonalCrossingsDischarge (True)`. Every hash- and digest-pinned test passed in all three: the two
Survival parity guards, `SingleStageBehaviourTests`, the save round trips. Debug and Release gave the same outcomes too:
on 5e57ebb9 both failed the same three tests at the same observation, and both parity guards pass in Release on
5942a6ca.

Shared static state checked for parallel safety:

- `GameSession._lastLayoutFit` (BuildLayout.cs): read and written under `LayoutFitLock`, keyed by everything the fit
  depends on.
- `Stalls.Stages` (stage bounds): a dictionary under `lock`, keyed by stage id. The stages are fixed static
  definitions, so an id always has the same bounds.
- `ClassicDevelopment` and `GroundCover` `Lazy<>` caches: thread-safe by default, and read-only once built.
- `PersistenceTiming.Observer` is set only by the game.
- Test-side statics: the parity tests' event dictionaries belong to one-test classes. `MultiVendorTests.Day()` locks.
  `BuildRouteTests.Run` and `PondStageTests.PondDay` are `Lazy<>`.
- Files: every test writes to a fresh `Path.GetTempPath()` folder named with a GUID. Writes to fixed paths (parity
  baselines, probe and evidence folders) happen only when their record or output environment variable is set.
- No test sets environment variables or changes the current directory.

Nothing needed `[DoNotParallelize]`, and no simulation code changed.

## Slowest 30 tests

Before: one parallel full run, Debug with the workstation GC, on 5e57ebb9. Durations include waiting on the GC and on
other tests, and both parity guards failed on stale baselines at that commit. "Slow now" is the category after this
change.

| # | Test | Class | Slow now | Duration |
|---|---|---|---|---|
| 1 | SurvivalStateHashAndEventsRemainExactlyAsBeforeClassic | ClassicSurvivalParityTests | yes | 1393.2 s |
| 2 | HundredAttendeesAndThreeStagesCompleteAndCarryThroughASecondPaidFestival | ClassicDevelopmentTests | yes | 998.7 s |
| 3 | ProbeRainyTierTwoDays | RainProbe | yes | 963.0 s |
| 4 | ProbeTierOneWithAndWithoutAMarquee | MarqueeProbe | yes | 759.1 s |
| 5 | APondStageTrialTransitionsToBarnAtTierTwo | NextFestivalTests | yes | 699.2 s |
| 6 | ProbeThreeTrialDays | PondStageProbeTests | yes | 680.0 s |
| 7 | TierTwoPlaysToTheEndAndItsAccountsShowTheCarriedCashAndDebt | NextFestivalTests | yes | 592.5 s |
| 8 | ProbeWholeDays | TierTwoProbe | yes | 529.8 s |
| 9 | SurvivalFatalConsequencesAndHashesRemainExactlyAsBeforeClassic | ClassicSurvivalIncidentParityTests | yes | 522.6 s |
| 10 | ProbeTierTwoWithOneAndTwoOfEachStall | MultiVendorProbe | yes | 494.7 s |
| 11 | ADayWithTwoVansAndTwoBarsRestoresMidDayAndPlaysOnTheSame | MultiVendorTests | yes | 471.0 s |
| 12 | ProbeDrinkCollapsesAtTierOneAndTwo | DrinkProbe | yes | 440.8 s |
| 13 | FourPlayedFestivalsReconcileActualTicketsBandFeesAndCashCarry (20260922) | ClassicBookingIntegrationTests | yes | 427.1 s |
| 14 | FatalEquipmentAndDisorderConsequencesKeepTheirPreExtractionStateAndOrder | IncidentConsequenceParityTests | yes | 416.9 s |
| 15 | ExistingSurvivalBehaviorMatchesThePreRefactorReference | SurvivalArchitectureParityTests | yes | 415.8 s |
| 16 | FourPaidFestivalsCarryActualHoldingsRelationshipsAndResetOnlyTheDay | ClassicCampaignTests | yes | 414.7 s |
| 17 | EachVanKeepsItsOwnAccountAndTheBarsReconcile | MultiVendorTests | yes | 412.3 s |
| 18 | AGuestWeighsBothVansAndBothBarsByWalkQueueAndValue | MultiVendorTests | yes | 411.7 s |
| 19 | TheCrowdSplitsBetweenTheVansAndBothBarsSell | MultiVendorTests | yes | 411.4 s |
| 20 | FourPlayedFestivalsReconcileActualTicketsBandFeesAndCashCarry (14) | ClassicBookingIntegrationTests | yes | 377.5 s |
| 21 | MoreBeerIsSoldForMoreAndTheDayStillRestores | BeerFestivalTests | yes | 351.0 s |
| 22 | FriendlyQueuesCheerUpTheWaitingAndKeepTheirPatience | PerkEffectsTests | no | 349.5 s |
| 23 | SharedWorldCompletesWithSafeSweepsAndReconciledOwnership | SharedWorldFeasibilityTests | yes | 316.0 s |
| 24 | FourStallsOnTheBasicPaNeverStrainThePoolOnATierTwoDay | PooledPowerTests | yes | 252.5 s |
| 25 | ColaFiendsBuySoftDrinksInsteadOfQueuingForTheTap | PerkEffectsTests | no | 245.3 s |
| 26 | OwnBottlesMeanFewerTripsToTheTapButSlowerFills | PerkEffectsTests | no | 243.2 s |
| 27 | AlcoholicsBuyMoreBeerAndGetDrunker | PerkEffectsTests | no | 240.3 s |
| 28 | ThePlayerClimbsEachStagesStepsButNotItsEdgeAndNoGuestEverGetsUp | StewardShiftStageTests | no | 234.1 s |
| 29 | RobotWorkersNeverStopForThemselves | PerkEffectsTests | no | 233.5 s |
| 30 | APondDayPlaysItsFullLastSetAfterTheTrailerHasFinishedThenCloses | DayLengthTests | no | 222.0 s |

After: the serial Release run (1 worker, server GC), close to each test's own cost. Some probes and perk tests use
`AsParallel` internally.

| # | Test | Class | Slow now | Duration |
|---|---|---|---|---|
| 1 | ProbeRainyTierTwoDays | RainProbe | yes | 334.0 s |
| 2 | ProbeWholeDays | TierTwoProbe | yes | 139.7 s |
| 3 | SharedWorldCompletesWithSafeSweepsAndReconciledOwnership | SharedWorldFeasibilityTests | yes | 116.4 s |
| 4 | ExistingSurvivalBehaviorMatchesThePreRefactorReference | SurvivalArchitectureParityTests | yes | 79.0 s |
| 5 | SurvivalStateHashAndEventsRemainExactlyAsBeforeClassic | ClassicSurvivalParityTests | yes | 77.1 s |
| 6 | ProbeDrinkCollapsesAtTierOneAndTwo | DrinkProbe | yes | 55.2 s |
| 7 | HundredAttendeesAndThreeStagesCompleteAndCarryThroughASecondPaidFestival | ClassicDevelopmentTests | yes | 53.9 s |
| 8 | ProbeTierOneWithAndWithoutAMarquee | MarqueeProbe | yes | 44.6 s |
| 9 | ProbeTierTwoWithOneAndTwoOfEachStall | MultiVendorProbe | yes | 43.9 s |
| 10 | APondStageTrialTransitionsToBarnAtTierTwo | NextFestivalTests | yes | 41.1 s |
| 11 | ProbeThreeTrialDays | PondStageProbeTests | yes | 36.2 s |
| 12 | TierTwoPlaysToTheEndAndItsAccountsShowTheCarriedCashAndDebt | NextFestivalTests | yes | 34.3 s |
| 13 | APonchoBuyerStaysDryAndPonchosAreInTheAccounts | RainTests | yes | 30.3 s |
| 14 | AStrawJobEndsAsTheCrowdGoesHome | RainTests | yes | 25.5 s |
| 15 | MapTheGroundAtClosing | RainProbe | yes | 24.8 s |
| 16 | FatalEquipmentAndDisorderConsequencesKeepTheirPreExtractionStateAndOrder | IncidentConsequenceParityTests | yes | 24.2 s |
| 17 | SurvivalFatalConsequencesAndHashesRemainExactlyAsBeforeClassic | ClassicSurvivalIncidentParityTests | yes | 24.0 s |
| 18 | StrawAndMattingResistChurning | RainTests | yes | 23.4 s |
| 19 | MoreBeerIsSoldForMoreAndTheDayStillRestores | BeerFestivalTests | yes | 22.8 s |
| 20 | RainWetsTheGroundAndBusyGroundChurnsToMud | RainTests | no | 21.7 s |
| 21 | StewardsAndTheMedicStayUntilTheLastGuestIsOut ("management-only") | WalkOutTests | yes | 20.8 s |
| 22 | FourPaidFestivalsCarryActualHoldingsRelationshipsAndResetOnlyTheDay | ClassicCampaignTests | yes | 20.0 s |
| 23 | ShelterIsFilledNoFurtherThanItHoldsRoomFor | RainTests | no | 19.9 s |
| 24 | FourPlayedFestivalsReconcileActualTicketsBandFeesAndCashCarry (14) | ClassicBookingIntegrationTests | yes | 19.4 s |
| 25 | FourPlayedFestivalsReconcileActualTicketsBandFeesAndCashCarry (20260922) | ClassicBookingIntegrationTests | yes | 19.0 s |
| 26 | AGuestWeighsBothVansAndBothBarsByWalkQueueAndValue | MultiVendorTests | yes | 18.4 s |
| 27 | FailedAndAbandonedCustomersClearCounterAndFinishAtDistinctExits | ServiceQueueTests | yes | 18.3 s |
| 28 | ProbeToiletQueueBalanceAtTierOne | ToiletBalanceProbe | yes | 17.2 s |
| 29 | ThePlayerClimbsEachStagesStepsButNotItsEdgeAndNoGuestEverGetsUp | StewardShiftStageTests | no | 16.5 s |
| 30 | InsufficientCashAndStockoutReleaseOwnersWithoutDoubleCharging | ServiceQueueTests | no | 16.1 s |

## Time by class (serial Release run, top 40)

| Class | Tests | Total |
|---|---|---|
| RainProbe | 2 | 358.8 s |
| RainTests | 17 | 174.3 s |
| TierTwoProbe | 1 | 139.7 s |
| SharedWorldFeasibilityTests | 6 | 122.2 s |
| WalkOutTests | 14 | 99.5 s |
| ServiceQueueTests | 18 | 99.3 s |
| NextFestivalTests | 14 | 83.9 s |
| SurvivalArchitectureParityTests | 1 | 79.0 s |
| ClassicSurvivalParityTests | 1 | 77.1 s |
| ClassicCampaignTests | 11 | 63.3 s |
| ClassicDevelopmentTests | 20 | 60.0 s |
| ClassicBookingIntegrationTests | 15 | 58.8 s |
| DrinkProbe | 1 | 55.2 s |
| PreparationTests | 10 | 53.4 s |
| FoundationSceneTests | 12 | 48.8 s |
| MarqueeProbe | 1 | 44.6 s |
| MultiVendorProbe | 1 | 43.9 s |
| BandRelationshipTests | 27 | 42.1 s |
| StewardShiftStageTests | 10 | 38.3 s |
| FacilityFaultTests | 13 | 36.9 s |
| PondStageProbeTests | 1 | 36.2 s |
| StewardShiftActionTests | 23 | 35.0 s |
| HandsOnAutopilotTests | 12 | 28.8 s |
| FestivalResultsTests | 18 | 28.7 s |
| IncidentConsequenceParityTests | 1 | 24.2 s |
| MultiVendorTests | 11 | 24.2 s |
| ClassicSurvivalIncidentParityTests | 1 | 24.0 s |
| PooledPowerTests | 9 | 23.8 s |
| TimetableLateBandTests | 8 | 23.3 s |
| BeerFestivalTests | 2 | 22.9 s |
| PerkEffectsTests | 5 | 22.4 s |
| StaffAutonomyTests | 20 | 22.1 s |
| StewardShiftTests | 12 | 19.4 s |
| GroundTests | 11 | 19.2 s |
| BuildRouteTests | 7 | 18.5 s |
| ToiletBalanceProbe | 1 | 17.2 s |
| BarnTractorTests | 15 | 17.0 s |
| PowerBudgetTests | 6 | 16.1 s |
| FestivalProgrammeTests | 6 | 14.9 s |
| RecordedDevelopmentStartTests | 6 | 13.9 s |

## Changes

- `tests/Festival.Tests/Festival.Tests.csproj`: `ServerGarbageCollection`. This is the test runner only; the game's GC
  is unchanged.
- `tests/Festival.Tests/AssemblyInfo.cs`: an `[AssemblyInitialize]` raises the thread pool's minimum to 4 × cores, so
  background saves get a thread beside every worker.
- `tools/test.ps1`: builds and runs Release by default. It runs the exe directly and offers `-Full` (alias `-Slow`),
  `-Fast`, `-Filter` and `-Timings`.
- Categories. 21 heavy, previously untagged tests are now `Slow`: the four Survival and incident parity guards,
  multi-festival Classic runs, Tier 2 days, the MultiVendor day and three rain days. Eleven tests are tagged
  `Everyday`, and the everyday run adds them back:
  - `NextFestivalTests.TierTwoPlaysToTheEnd…`: a Tier 2 full day, with a mid-day restore through the save file.
  - The six `BuildRouteTests`: one shared Tier 1 default edition, with a mid-edition save.
  - The walk-out halfway save.
  - The natural-departure file reload.
  - The paid-performer mid-set save.
  - `PersonModelTests`' full-festival restore.
- Shared fixture: `PerkEffectsTests.Days(perk)` is now played once per perk per run. Before, all five tests replayed the no-perk
  days, 16 of the 40 seeded days they played. No assertion changed.

Not changed:

- The four parity guards' code, baselines and record switches; they only gained the `Slow` category.
- `ClassicSurvivalParityTests` and `SurvivalArchitectureParityTests` simulate the same sessions with different
  observations, so one capture could feed both. Each is now about 65-80 s on its own, so sharing would save about a
  minute of CPU in the full run and change neither the critical path nor Codex's guard code. It was left alone.
- The probes (`TestCategory("Probe")`) assert nothing; they report. They are about a quarter of the full run's CPU
  (`ProbeRainyTierTwoDays` alone is 5.5 minutes serial), but they stay in `-Full`. Excluding them would be
  `-Filter "TestCategory!=Probe"`.
