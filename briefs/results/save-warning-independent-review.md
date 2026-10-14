# Save-warning independent review

Verdict: PASS for the scoped source/native save-warning change after bounded repair. No remaining actionable findings in this scope. This is not approval of unfinished Classic work or an exported executable.

Reviewed against HEAD 0055f7414954db6f2e1960fd7628b6df19874a43: SessionHost.cs, SessionHostTests.cs, Main.SaveCadence.cs, Main.Preparation.cs, Main.Results.cs, Main.cs diagnostic wiring, Main.SaveWarningEvidence.cs, the implementation report and native evidence. Bounded re-review covered Main.SaveCadence.cs and Main.SaveWarningEvidence.cs only. No Classic implementation was reviewed or changed.

## Resolved finding

The first review raised P2: the dismissed retry HBox rendered as an unreadable dark block over the budget/live footer. The repair uses a themed, explicitly sized panel and nonwrapping label above the footer. I inspected both supplied native-pointer-repair and independently generated review-pointer-repair preparation/live screenshots. “Save failed” and “Retry save” are now legible; the strip leaves the budget, bottom navigation, stage selectors, Start festival and live controls clear. It slightly overlays the lower-left programme card area but does not cover that card's Remove control. The scoped finding is resolved.

The diagnostic now sends actual Godot mouse-motion and button events to X and Retry, asserts their centers are inside the viewport, and checks effects on later frames. This removes the earlier direct-signal limitation for those two controls.

## Independent evidence and accepted behavior

Ran the six unchanged SessionHost tests: 6 PASS, 0 failed, exit 0 (4.677 seconds). Used workspace TEMP/TMP and approved filesystem execution with --no-restore, -m:1, UseSharedCompilation=false, -nr:false and BuildProjectReferences=false. This preserved the compiled pre-Classic simulation reference. Tests were not needlessly repeated for the UI-only repair.

Initial independent native diagnostic: reports/evidence/save-warning/review-native, 22 checks PASS, exit 0, four screenshots. Final independent native diagnostic after repair: reports/evidence/save-warning/review-pointer-repair, 24 checks PASS, exit 0, four screenshots. The restricted native runs emitted a shader-cache creation warning which did not prevent checks or screenshots.

The host preserves accepted milestone actions on write failure, retains rejected-command rollback, keeps dismissal across repeated manual/milestone/background failures, clears the episode after recovery, and verifies prior valid manual bytes plus loadability after injected failure before replacement. Continued festival start/live tick and exact recovery hash assertions pass. The main warning has an X and no full-screen input blocker. Results navigation now continues after a failed checkpoint, as requested. No deletion, false save-success branch, or permission bypass was added in the reviewed diff.

## Limits

Pointer input was exercised for X and compact Retry at 1280×720; stage selection and start remain direct handler/signal checks. No interactive resize or exhaustive results/menu path run was performed. Ordinary project settings use canvas_items stretch; diagnostic capture disables stretch. The native runs use retained pre-Classic simulation binaries. No complete build of unfinished Classic sources, EXE export, or combined handoff was performed or approved. Only this report and isolated review evidence were written; no implementation, imports, locks or UIDs were changed by the reviewer.
