# AuditSphere — Blazor Removal Readiness Gate

> **Decision: `NOT_READY`.** Commit `59387b54` physically removed `AuditSphereOps.Web`, Razor UI, and MudBlazor before the canonical removal gate passed. The rollback source and project entry have been restored from the parent snapshot. A local Development smoke verified the Razor home, Blazor runtime, MudBlazor/static assets, and circuit negotiation; production OIDC, route switching, rollback recovery, and canary remain unaccepted. Do not treat restoration or a local smoke as completed migration or retirement.

This gate follows the source inventory, route matrix, feature register, test register, and current execution evidence. Exact live counts and test results are maintained in [`docs/execution/status.json`](../execution/status.json), under `verification.angularBlazorMigrationBaselineAndFocusedRouteResourceVerification`.

## Gate checklist

| Condition | Status | Evidence / remaining work |
|---|---|---|
| Every production Blazor route accounted for | **PASS — route ownership only** | All discovered route templates are in [the route matrix](auditsphere-migration-blazor-angular-route-parity.md); the focused API route contract passes. This is not feature parity. |
| Every required feature and user action reviewed | **OPEN** | One source/action row is parity verified, 13 Administration artifacts are partially reviewed, 62 source/action rows are unanalyzed, and 28 supporting files remain unreviewed. The acceptance family remains partial. See the source-specific review records and `status.json`. |
| Every command and backend authority mapped to an equivalent API/Application contract | **OPEN** | Per-source command, validation, audit, recovery, and outcome crosswalk is incomplete. |
| Every authorization rule, role, and scope compared | **OPEN** | Existing server authorization tests do not establish full old-to-new parity for every source action. |
| Client portal, upload, download, Microsoft 365, and recovery behaviors reviewed end to end | **OPEN** | Focused Angular/API journeys exist; the complete per-source crosswalk and live external gates remain incomplete. |
| Keyboard, accessibility, responsive, and locale behavior accepted | **PARTIAL** | Automated DOM/focus/contrast/locale checks exist. A human screen-reader walkthrough and wider locale acceptance remain open. |
| Angular route protection and direct-route behavior reviewed | **PARTIAL** | Route guards and route ownership are tested; the full authorization and direct-link matrix remains open. API authorization remains authoritative. |
| CSRF, sessions, revocation, cross-client and cross-engagement isolation reviewed | **PARTIAL** | Existing security and isolation tests cover selected journeys; coverage is not yet reconciled against every Blazor command/query. |
| Unknown-result, concurrency, and idempotent recovery parity reviewed | **PARTIAL** | Focused recovery journeys exist; per-source command and failure-state coverage is incomplete. |
| Current Angular production build, .NET Release build, Angular unit suite, and PostgreSQL-backed full solution regression pass | **PASS — local** | The restored rollback source and current API/Angular tree passed the current Release build and full suite; exact project counts and source attribution are in `status.json`. This proves local regression only, not behavior parity for every removed assertion. |
| Current EF model drift is clean | **PASS — local** | `dotnet ef migrations has-pending-model-changes` reported no changes to the model since the last migration on the verified source snapshot. |
| No active runtime navigation points to legacy Blazor workbenches | **PASS — route contract** | `AngularRoutingContractTests` checks runtime links and native route ownership. |
| API, Application, Domain, Infrastructure, and Worker require no Web project reference | **PASS — project graph only** | The application project graph is separate from Web. This does not prove feature parity or an accepted cutover. |
| No required test or test fixture depends on Web | **PASS — project references only; behavior replacement OPEN** | `OwnedHost` starts the API process explicitly and the API.Tests/E2E.Tests project graph contains no Web reference. Nine legacy E2E suites removed by `8ea3ef01` still need assertion-by-assertion replacement mapping. The restored source is available to the discovery inventory. |
| Rollback exit criteria and production-like cutover are proven | **OPEN — local host smoke only** | `AuditSphereOps.Web` is again in the solution and builds. A built-in-browser Development smoke loaded `/`, served Blazor/MudBlazor and local assets successfully, and negotiated a Blazor circuit. The host is explicit opt-in outside Test. No production OIDC, route switch, rollback recovery, or canary has been exercised; local startup is not operational rollback acceptance. |
| Live Microsoft 365 acceptance is complete | **BLOCKED_EXTERNAL** | Live tenant, consent, and selected-resource checks remain external acceptance gates in `status.json`. |

## Required work before reopening the removal gate

1. Review each remaining source/action row against its Angular behavior, API contract, Application authority, role/scope, validation, audit evidence, empty/error/stale behavior, concurrency, recovery, accessibility, and route behavior. Preserve reviewed source from the Git snapshot and record evidence per source action.
2. Complete the API-only E2E migration audit. Replace the conditional Web-source scan with the committed, hash-checked inventory contract, and map every assertion in the nine suites deleted by `8ea3ef01` to a passing behavior-level Angular/API journey or an owner-approved retirement disposition.
3. Exercise and record the restored rollback host and production-like cutover/recovery, or complete all applicable readiness gates and obtain the separate owner decision before physical removal. The restored source alone does not satisfy this condition.
4. The current local Release build, Angular checks, complete solution suite, and EF model check have passed. Preserve their exact attribution in `status.json`; rerun the affected checks after any subsequent source changes and before acceptance.
5. Complete the production-like canary and rollback acceptance, human assistive-technology walkthrough, and applicable locale review.
6. Resolve or formally retain all external Microsoft acceptance gates; do not mark them passed from local simulation.
7. Re-run dependency, package, Razor, route-reference, CI, deployment, EF model, and documentation searches after the preceding work.

When every applicable row passes, publish a refreshed removal report and stop for the separate owner review required before accepting removal. This document is the stop gate; it does not authorize deletion.
