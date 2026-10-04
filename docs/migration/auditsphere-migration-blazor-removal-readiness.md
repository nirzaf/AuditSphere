# AuditSphere — Blazor Removal Readiness Gate

> **Decision: `NOT_READY`.** Commit `59387b54` physically removed `AuditSphereOps.Web`, Razor UI, and MudBlazor before the canonical removal gate passed. The deletion is present on `master`, but it is not accepted as successful migration or retirement. Restore/retain the rollback host until the behavioral, operational, and external gates below pass and the owner makes the separate removal decision.

This gate follows the source inventory, route matrix, feature register, test register, and current execution evidence. Exact live counts and test results are maintained in [`docs/execution/status.json`](../execution/status.json), under `verification.angularBlazorMigrationBaselineAndFocusedRouteResourceVerification`.

## Gate checklist

| Condition | Status | Evidence / remaining work |
|---|---|---|
| Every production Blazor route accounted for | **PASS — route ownership only** | All discovered route templates are in [the route matrix](auditsphere-migration-blazor-angular-route-parity.md); the focused API route contract passes. This is not feature parity. |
| Every required feature and user action reviewed | **OPEN** | The source-action register still has 75 unreviewed action rows and 28 unreviewed supporting files; the acceptance family remains partial. Focused tests for `AssessmentDecision.razor` are recorded separately and do not close the remaining rows. |
| Every command and backend authority mapped to an equivalent API/Application contract | **OPEN** | Per-source command, validation, audit, recovery, and outcome crosswalk is incomplete. |
| Every authorization rule, role, and scope compared | **OPEN** | Existing server authorization tests do not establish full old-to-new parity for every source action. |
| Client portal, upload, download, Microsoft 365, and recovery behaviors reviewed end to end | **OPEN** | Focused Angular/API journeys exist; the complete per-source crosswalk and live external gates remain incomplete. |
| Keyboard, accessibility, responsive, and locale behavior accepted | **PARTIAL** | Automated DOM/focus/contrast/locale checks exist. A human screen-reader walkthrough and wider locale acceptance remain open. |
| Angular route protection and direct-route behavior reviewed | **PARTIAL** | Route guards and route ownership are tested; the full authorization and direct-link matrix remains open. API authorization remains authoritative. |
| CSRF, sessions, revocation, cross-client and cross-engagement isolation reviewed | **PARTIAL** | Existing security and isolation tests cover selected journeys; coverage is not yet reconciled against every Blazor command/query. |
| Unknown-result, concurrency, and idempotent recovery parity reviewed | **PARTIAL** | Focused recovery journeys exist; per-source command and failure-state coverage is incomplete. |
| Current Angular production build, .NET Release build, and focused tests pass | **OPEN — current head not fully verified** | Prior passing results are attributed to their exact source commits in `status.json`. A full solution regression has not passed against the post-removal source tree. |
| Current EF model drift is clean | **HISTORICAL** | The last recorded no-drift check predates physical Web removal. Re-run the model check against a frozen current source snapshot before acceptance. |
| No active runtime navigation points to legacy Blazor workbenches | **PASS — route contract** | `AngularRoutingContractTests` checks runtime links and native route ownership. |
| API, Application, Domain, Infrastructure, and Worker require no Web project reference | **PASS — project graph only** | The application project graph is separate from Web. This does not prove feature parity or an accepted cutover. |
| No required test or test fixture depends on Web | **OPEN — migration in progress** | The E2E host/test conversion is in an unverified working-tree change. `AngularRoutingContractTests` still reads the removed Web source tree, while legacy render coverage was deleted without a current full regression proving equivalent replacement. |
| Rollback exit criteria and production-like cutover are proven | **OPEN — rollback host absent** | The current tree has no Blazor rollback host. Local Angular rendering is not a production canary; restore or retain an approved rollback path and exercise cutover/recovery before accepting removal. |
| Live Microsoft 365 acceptance is complete | **BLOCKED_EXTERNAL** | Live tenant, consent, and selected-resource checks remain external acceptance gates in `status.json`. |

## Required work before reopening the removal gate

1. Review each remaining source/action row against its Angular behavior, API contract, Application authority, role/scope, validation, audit evidence, empty/error/stale behavior, concurrency, recovery, accessibility, and route behavior. Preserve reviewed source from the Git snapshot and record evidence per source action.
2. Finish and verify the API-only E2E migration. Remove the Web-source scan from the route test by using the committed, hash-checked source inventory; retain behavior-level replacement tests for the deleted legacy journeys.
3. Restore/retain the rollback host, or complete all applicable readiness gates and obtain the separate owner decision before physical removal. The current deletion does not satisfy this condition.
4. Run the complete Release solution suite and Angular build against a frozen current source/assets snapshot; record the exact source commit and preserve historical results separately.
5. Complete the production-like canary and rollback acceptance, human assistive-technology walkthrough, and applicable locale review.
6. Resolve or formally retain all external Microsoft acceptance gates; do not mark them passed from local simulation.
7. Re-run dependency, package, Razor, route-reference, CI, deployment, EF model, and documentation searches after the preceding work.

When every applicable row passes, publish a refreshed removal report and stop for the separate owner review required before accepting removal. This document is the stop gate; it does not authorize deletion.
