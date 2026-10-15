# Classic construction independent review

Reviewed baseline: HEAD `7e969c62`, uncommitted Classic construction slice and bounded repairs on 15 October 2026. This review covers the requested simulation/UI files, the related StaffInterventions repair, and `ClassicConstructionTests.cs`. It does not approve the subsequent development-capacity/three-stage commission.

Disposition after re-review: **construction code slice passes independent review; no remaining blocking findings within this scope.** Native UI proof and combined development handoff remain outstanding. This is not a completed playtest/export handoff.

## Findings resolved

- Historical null layouts/null entries are now rejected before `ClassicClosingCashBound` accesses them. Both malformed-history cases pass.
- GuideToRest acceptance and continuation consider all first-aid rest centres. Multi-site `RestSpotFor` filters candidates by actual path reachability; single-site selection retains its prior behavior. The additional-site fixture exercises destination selection, walking and cooling.
- Food traders render four van pickers per page, and expanded placement lists render twelve rows per kind per page. Pages clamp when the list shrinks; page state participates in refresh keys. The catalogue still derives candidates from actual placements plus one next van, without enumerating the 4096 safety bound.
- Existing optional paid extra-medic hiring is covered: exactly one worker is added by the contract and assigned to the second physical post. Additional buildings alone do not create staff.

## Verification

Independently executed after Builder released the build lane:

`dotnet test tests/Festival.Tests/Festival.Tests.csproj --no-build --no-restore -- --filter 'FullyQualifiedName~ClassicConstructionTests|FullyQualifiedName~StaffInterventionTests'`

Result: **17 passed, 0 failed, 0 skipped**, 37.716 seconds, exit code 0. Evidence: `artifacts/classic-construction-independent-tests.txt`. Source remained frozen during the run.

Also inspected:

- `artifacts/classic-construction-repair-tests.txt`: Builder's repaired focused suite, 17/17 passed.
- `artifacts/classic-construction-repair-game-build.txt`: repaired game build, zero warnings/errors.
- `artifacts/classic-construction-regression.txt` and `artifacts/classic-construction-regression-details.txt`: earlier broad suite, **46/46 passed**. The actual recorded filter includes ClassicConstruction, ClassicCampaign, ClassicBookingIntegration, BuildLayout, ResponsePost, RestSpot and ClassicSurvivalParity tests. The detailed log explicitly reports `SurvivalStateHashAndEventsRemainExactlyAsBeforeClassic` active before the final passing result.
- `ClassicSurvivalParityTests.cs` has no current diff. Its unchanged baseline compares full authoritative Survival state, events and RNG and exercises normal management/hands-on and Steward flows. This broad parity run preceded the bounded repairs; it was not independently rerun after repairs. The follow-up independent run covers the construction and affected StaffIntervention groups; single-site rest behavior and boolean reachability remain equivalent by source inspection.

## Limits and residual observations

- No native Godot interaction, screenshot validation, export or high-count development stress run was performed here. Pagination layout/usability and additional-post presentation still need the planned combined native evidence.
- The all-rest-spots-occupied fallback remains the primary centre, matching legacy overflow behavior. Candidate filtering now handles normal multi-site free-spot selection; this review does not claim disconnected-site overflow behavior has separately been exercised.
- Additional-post tests use labelled location/need fixtures for the guest journey, not an uninterrupted naturally generated festival scenario.
- Physical overlap, clear terrain and required-access checks remain in command/save validation. Additional post footprints are blocked at start and validated on live restore. Valid Survival cuisine selections remain uniqueness-validated.

No implementation files changed by reviewer. No unrelated palette imports, package lock changes, UIDs or saves altered. No commit or external chat message performed. New development capacity, three-stage setup, stress execution and export remain outside this review.
