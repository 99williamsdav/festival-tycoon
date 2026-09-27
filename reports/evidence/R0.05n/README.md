# R0.05n evidence index

27September2026. Fresh Sol-medium implementation and narrow P2 repair packet. Fresh Astra-low review found one shared-footer line-limit issue; same reviewer cleared the frozen repair with no remaining actionable findings. Isolated export/runtime passed. See `briefs/results/R0.05n.md` for product contract and actual save identity. User did not request a manual final-app walkthrough; none is claimed.

## Final pre-review commands

From `C:\Projects\festival-tycoon`:

```powershell
dotnet build tests/Festival.Tests/Festival.Tests.csproj --no-restore -v:q
dotnet build game/Festival.Game.csproj --configuration Debug --no-restore -v:q
dotnet tests/Festival.Tests/bin/Debug/net8.0/Festival.Tests.dll --filter 'FullyQualifiedName~LineupBooking|FullyQualifiedName~FestivalProgrammeTests|FullyQualifiedName~PreparationPlanTests|FullyQualifiedName~PreparationTests|FullyQualifiedName~SaveRoundTripTests|FullyQualifiedName~PerkTests|FullyQualifiedName~FestivalResultsTests|FullyQualifiedName~PreparationReadinessTests|FullyQualifiedName~FinanceFeedbackTests' --output Detailed --progress off --timeout 180s
```

Build logs `tests-build-final.log`/`game-build-final.log`: both exit0, zero warnings/errors. `proportional-final.log`:92/92 passed, zero failed/skipped,1m52.102s. `focused-dev2.log`:7/7 passed,6.748s, **overlaps** proportional set.

Pinned `C:\Projects\festival-tycoon\.tools\godot\4.7.2\editor\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe`:

```text
--path C:/Projects/festival-tycoon/game -- --capture-r005n-booking C:/Projects/festival-tycoon/reports/evidence/R0.05n/final-dev-1280x720 --capture-size 1280x720
--path C:/Projects/festival-tycoon/game -- --capture-r005n-booking C:/Projects/festival-tycoon/reports/evidence/R0.05n/final-dev-1920x1080 --capture-size 1920x1080
```

Each hidden native process exited0, empty stderr,16PNG, `BOOKING_CAPTURE_COMPLETE scripted_native_handlers=True ordinary_OS_drag=False`, same authoritative hash `65f039c0ed617392c8f52f14e6d6656c04f1f6a0c84042e08576176d0f52e05a`. `final-dev-*.stdout.log` records actual viewport bounds/minima per preparation screenshot. Every shot asserts Start/reason including Paid at Start/all three slots fully enclosed and footer above bottom bar. Script drives native ForceDrag plus overridden drag/drop callbacks and injected Enter/Space, not ordinary OS input. Images cover empty, one/full, preview/replace/swap, invalid/cancel, save-failure rollback/load/remove, keyboard placement, ready cost and live lock.

Earlier dev1/3/5–8 errors and PNGs are retained. Dev1 used GetDragData outside engine drag state; dev2 fixed harness state but footer lay below viewport; dev3 keyboard fixture failed; dev5–8 documented stale initial panel size before deferred minimum-size propagation. The targeted layout refresh passes viewport rectangles, with no assertion suppression. The pre-repair final720 empty readiness second line clipped after “before”; reviewer additionally found the two-line limit persisted on other tabs, hiding later blockers. Tooltips do not satisfy complete-text visibility. Those images are pre-repair, not final acceptance.

Narrow review repair logs/images: `review-diagnostic-1280x720` and `review-repair1-1280x720` retain failed cross-tab attempt (Godot `MaxLinesVisible=0` produced0/2 lines on Overview). `review-repair2-1280x720` and `review-repair2-1920x1080`: PID24404/58620 exit0/empty stderr,19PNG each, including `00-readiness-{programme,overview,staff}.png`. Full all-three-blocker sentence and actual rendered2/2 lines, reason/Start viewport bounds, cost, every-slot bounds and original native scripted interaction sequence pass at both sizes. Authoritative final hash `ebfbe7d7b38c077879b3fa4e4d2252eb54abe28dd9718ad404c2d9c39e97bae8`. `game-build-review-repair2.log` clean; `focused-review-repair2.log`7/7. Same-reviewer recheck clear.

Isolated export `export.stdout.log`/`.stderr.log`: pinned Godot hidden PID16264 exit0/empty stderr. `artifacts/windows/r005n-booking-final/FestivalTycoon-Booking.exe` SHA256 `DDAD7437114DE9754F8B46EFD54B59AAC419150FB38032735793DCEAE6D3F88E`, adjacent `data_Festival.Game_windows_x86_64` required. Packaged game DLL SHA256 `FAF5845DE52E011232431577D5404C614048512BFF7A7ACEBA7F1FD2B472684B` matches `game/.godot/mono/temp/bin/ExportRelease/win-x64/Festival.Game.dll`;187 declared unique dependency basenames present,0 missing. `exported-final-{1280x720,1920x1080}` and matching stdout/stderr: EXE PIDs12236/44968 exit0, empty stderr,19PNG each, startup/scene/completion markers and reviewed final hash all match. Scripted native handler checks, not ordinary OS input or manual playtest.

No human playtest, ordinary OS drag, full weekend, FPS, natural rare-incident or exported-runtime claim in this pre-review packet.
