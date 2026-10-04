# AuditSphere — Blazor / Angular Test Parity Register

> **Status: PARTIAL.** Test names that look similar do not prove replacement coverage. Each candidate mapping must be checked for the host it starts, the user identity/scope it uses, the action/outcome asserted, and the strength of the replacement assertion.

Exact run counts, commits, logs, and external-gate evidence belong in [`docs/execution/status.json`](../execution/status.json). This file records what the current tests prove and the remaining Blazor coupling.

## Current focused evidence

| Evidence | What it proves | What it does not prove | Result |
|---|---|---|---|
| `AngularRoutingContractTests` | API deep-link declarations match Angular route declarations; runtime Angular links avoid legacy workbench paths; non-server-owned legacy workspace paths have a native owner | Screen actions, form parity, backend authorization for each capability, error/recovery parity | Route ownership only |
| `AngularResourcePlanningJourneyTests` | Resource planning page is exercised through the API host in canonical and `/ui` modes; a reviewed allocation rejects a stale concurrent change and recovery reloads persisted state | Full staffing, certification, availability, budget, role/scope and all failure-state parity | Focused slice only |
| API-only Angular E2E fixture mode | Angular/API journeys can use the shared owned PostgreSQL fixture without launching a legacy Web process; a representative journey asserts no Web host log was created | Removal of the Web project reference, legacy route smoke, or parity of remaining Blazor-only tests | Full E2E result recorded in `status.json` |
| API.Tests standalone host | The API test project references `AuditSphereOps.Api` directly; its complete PostgreSQL-backed suite runs through `StandaloneApiApplicationFactory` with no Web project reference | Removal of Web from E2E.Tests/solution, legacy smoke replacement, or behavior parity for every Razor action | Full API suite result recorded in `status.json` |
| Angular CI component/unit suite | Current Angular component and contract tests pass | Blazor-to-Angular equivalence for every Razor artifact | Component coverage only |
| Release solution build and EF model check | Current projects compile and the EF model has no pending migration | Whole-solution behavioral regression or UI parity | Build/model evidence only |
| Built-in browser visit to `/app/practice/resources` on the API host | The canonical Angular resource screen renders with an authenticated Development session and shows the live resource grid/forms | Production behavior, every form command, accessibility with a human screen reader, or a comparison against the Blazor screen | Read-only visual inspection only |
| Most recent complete whole-solution suite in `status.json` | The recorded suite passed at its recorded source commit | Current-head pass: the current master commit is newer than that whole-suite checkpoint | Historical checkpoint only |

Current exact results and attribution are recorded under `angularBlazorMigrationBaselineAndFocusedRouteResourceVerification` in `status.json`.

## Blazor test coupling that blocks project removal

- `AuditSphereOps.Api.Tests.csproj` now references the API project directly; the full API test suite passed without Web. `AuditSphereOps.E2E.Tests.csproj` still references `AuditSphereOps.Web.csproj`.
- `OwnedBlazorHost.StartAsync` retains legacy startup by default for existing Blazor cases, and now has an explicit API-only mode. Migrated Angular journeys that use the API host opt out of the legacy Web processes. The shared fixture still combines database seeding and host lifecycle, and the E2E project still references Web; split those concerns and migrate or retire remaining legacy-only journeys before project removal.
- `RouteRenderSmokeTests` explicitly exercises the MudBlazor host and asserts legacy page markup. It is not an Angular replacement and needs equivalent Angular/API coverage or an owner-approved retirement decision.
- Existing test crosswalk rows are candidate relationships until the host and assertions have been checked. A shared test file or reused fixture is not itself a replacement test.

## Acceptance rule

For each migrated behavior, link the source action to its API contract/Application authority and to a test that actually uses the API/Angular host. Confirm positive and negative authorization, exact scope isolation, validation, audit result, concurrency/stale behavior, retry/idempotency, upload/download integrity, and user-visible recovery as applicable. Use `PARITY_VERIFIED` only when those assertions cover the relevant Blazor behavior; do not infer it from route, build, or unit-test success.
