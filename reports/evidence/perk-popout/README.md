# Compact perks/lineup UI evidence

Current [result/commands/identities/limits](../../../briefs/results/R0.05i-popout-lineup.md). No manual desktop QA. Final frozen source independently reviewed; final exported artifacts are the current proof, earlier attempts retained.

- [tests-focused.log](tests-focused.log):29/29,0failed/skipped,21.156s.
- [game-build-final-root.log](game-build-final-root.log):exit0,0warnings/errors,0.72s.
- [export-final.log](export-final.log):isolated Windows export exit0.
- [720 final stdout](final-1280x720/stdout.log), [1080 final stdout](final-1920x1080/stdout.log):12PNG each,empty stderr,process exit0,startup/completion/allgates,identical final/screenshot12 hashbeb7336189b0d23dce5491fce1ed6198b4040a826c9bd03f82f57d5de808a90d. Saved fixtures are isolated diagnostics, not normal saves to import.

| Frames | Demonstration |
|---|---|
| 01–04 | Empty/collapsed/open/×/reopened |
| 05–08 | Five owned/collapsed/open/scroll fifth/reopened with selected object |
| 09 | Immediate partial lineup, actual newest autosave hash, explicit manual reload |
| 10 | Injected failed-save rejection with coherent visible prior lineup |
| 11 | Full immediate lineup after duplicate rejection |
| 12 | Start displayed choices exactly once; paused deterministic boundary |

Representative links: [720 open](final-1280x720/06-five-open.png), [720 fifth](final-1280x720/07-five-bottom-accessible.png), [1080 selected context](final-1920x1080/08-five-reopened-selection.png), [empty red slots](final-1280x720/09-immediate-partial-lineup.png), [Start](final-1920x1080/12-start-displayed-lineup-once.png).

Development1/2 failed layout assertions; development3 completed before paused final boundary; development4 both sizes pass final frozen source. All retained without deletion. Full-five/empty setup, viewport input/Pressed signals and save-failure injection are labelled diagnostics. No OS-input, manual feel, FPS, ordinary full-five progression or full-weekend claim. Existing K save identity unchanged; old files preserved.
