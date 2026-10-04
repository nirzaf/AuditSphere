# AuditSphere — Blazor Removal Readiness Gate

> **Decision: `NOT_READY`. STOP before physical removal of `AuditSphereOps.Web`, Razor UI, or MudBlazor.** Route ownership and selected Angular journeys have local evidence, but complete source-action parity and removal readiness have not been demonstrated.

This gate follows the source inventory, route matrix, feature register, test register, and current execution evidence. Exact live counts and test results are maintained in [`docs/execution/status.json`](../execution/status.json), under `verification.angularBlazorMigrationBaselineAndFocusedRouteResourceVerification`.

## Gate checklist

| Condition | Status | Evidence / remaining work |
|---|---|---|
| Every production Blazor route accounted for | **PASS — route ownership only** | All discovered route templates are in [the route matrix](auditsphere-migration-blazor-angular-route-parity.md); the focused API route contract passes. This is not feature parity. |
| Every required feature and user action reviewed | **OPEN** | A curated review now records `AssessmentDecision.razor` as `PARTIAL`; its direct-deep-link journey, decision-specific revocation test, and lost-response browser recovery remain open. The other 75 route/action rows and supporting source files remain `NOT_ANALYZED`; feature-family parity is `PARTIAL`. |
| Every command and backend authority mapped to an equivalent API/Application contract | **OPEN** | Per-source command, validation, audit, recovery, and outcome crosswalk is incomplete. |
| Every authorization rule, role, and scope compared | **OPEN** | Existing server authorization tests do not establish full old-to-new parity for every source action. |
| Client portal, upload, download, Microsoft 365, and recovery behaviors reviewed end to end | **OPEN** | Focused Angular/API journeys exist; the complete per-source crosswalk and live external gates remain incomplete. |
| Keyboard, accessibility, responsive, and locale behavior accepted | **PARTIAL** | Automated DOM/focus/contrast/locale checks exist. A human screen-reader walkthrough and wider locale acceptance remain open. |
| Angular route protection and direct-route behavior reviewed | **PARTIAL** | Route guards and route ownership are tested; the full authorization and direct-link matrix remains open. API authorization remains authoritative. |
| CSRF, sessions, revocation, cross-client and cross-engagement isolation reviewed | **PARTIAL** | Existing security and isolation tests cover selected journeys; coverage is not yet reconciled against every Blazor command/query. |
| Unknown-result, concurrency, and idempotent recovery parity reviewed | **PARTIAL** | Focused recovery journeys exist; per-source command and failure-state coverage is incomplete. |
| Current Angular production build, .NET Release build, and focused tests pass | **PASS — current source snapshot** | Results and caveats are in `status.json`. The full solution suite was not rerun at the current source commit. |
| Current EF model drift is clean | **PASS — current source snapshot** | `dotnet ef migrations has-pending-model-changes` reported no pending changes. |
| No active runtime navigation points to legacy Blazor workbenches | **PASS — route contract** | `AngularRoutingContractTests` checks runtime links and native route ownership. |
| API, Application, Domain, Infrastructure, and Worker require no Web project reference | **PASS — project boundary** | The production project graph does not reference `AuditSphereOps.Web`; the API rejects legacy presentation outside `Test`. |
| No required test or test fixture depends on Web | **OPEN — known coupling** | API.Tests now references the API project directly and its 189-test suite passes without Web. A new route contract checks the committed route inventory, but the existing route test still scans Web source in this committed snapshot. E2E.Tests still references Web; `OwnedBlazorHost` retains legacy startup for legacy cases and `RouteRenderSmokeTests` asserts legacy markup. |
| Rollback exit criteria and production-like cutover are proven | **OPEN** | Local browser rendering is not a production canary, and the legacy host is Test-only. Document and exercise the approved operational rollback path. |
| Live Microsoft 365 acceptance is complete | **BLOCKED_EXTERNAL** | Live tenant, consent, and selected-resource checks remain external acceptance gates in `status.json`. |

## Required work before reopening the removal gate

1. Review each remaining source/action row against its Angular behavior, API contract, Application authority, role/scope, validation, audit evidence, empty/error/stale behavior, concurrency, and recovery. The assessment decision review is a worked example with three explicit open checks; mark parity only when evidence covers the behavior.
2. Finish splitting PostgreSQL fixture lifecycle and API-host startup from `OwnedBlazorHost`; API-only mode avoids launching Web for migrated Angular journeys, but the shared fixture still owns both concerns and E2E.Tests still references Web.
3. Retain the new committed-inventory route contract and decide whether to remove or keep the existing Web-source route parser; replace or explicitly retain the legacy route-render test with an owner-approved, behavior-appropriate decision.
4. Run the complete Release solution suite against a frozen current source/assets snapshot and record the exact source commit. Preserve the historical suite attribution separately.
5. Complete the production-like canary and rollback acceptance, human assistive-technology walkthrough, and applicable locale review.
6. Resolve or formally retain all external Microsoft acceptance gates; do not mark them passed from local simulation.
7. Re-run dependency, package, Razor, route-reference, CI, deployment, and documentation searches after the preceding work.

When every applicable row passes, publish a refreshed removal report and stop for the separate owner review required before deleting the project. This document is the stop gate; it does not authorize deletion.
