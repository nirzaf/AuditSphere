# AuditSphereOps — Current State & Active Slice Handoff







**Status:** CURRENT



**Purpose:** Compact, authoritative handoff of the active implementation slice, recent verified changes, local environment state, and next actions.



**Authority:** Active execution handoff document. Volatile metrics (exact test counts, migration count, verified commit SHA, CI run IDs) belong exclusively to [`docs/execution/status.json`](status.json).



**Audience:** AI coding agents and human developers.







---

## Client financial-package access refresh

The management-review page now reloads on package route changes and offers an
explicit refresh. Each load clears the prior package, actor, decision fields and
result before resolving current identity and querying the exact package scope;
an older asynchronous route read cannot repopulate the new view. A browser
regression covers grant revocation followed by read-only refresh, alongside
the existing route-isolation and denied-decision journeys. This is a bounded
AS-PAR-002 repair, not complete client-portal or whole-application acceptance.
Exact verification and source identity are recorded in `status.json`.

## Client PBC request access refresh

The client request page now clears its request, transfer receipts, conversation,
and action results before an explicit refresh resolves the current identity and
reauthorizes the exact assigned request. Denied refresh also removes browser-saved
reply and upload drafts. A browser journey exercises same-document navigation to
another recipient's request, return to the assigned request, then role-grant
revocation and refresh without a document reload. This is a bounded AS-PAR-002
read and revocation repair; the whole-application authorization audit remains open.
Exact verification and source identity are recorded in `status.json`.

## Release candidate access refresh

The release workbench clears its candidate, checkpoint, attestation, signature
projection and release key before a route change or explicit refresh rechecks the
current actor and exact candidate scope. A late read from an older route cannot
restore its prior evidence. Scope/session denial on issue reloads the protected
view. The release page describes provider evidence without claiming Purview
acceptance, which is outside the approved product scope. A browser regression
covers a same-document route change and a revoked staff grant. Exact verification
results are in `status.json`; this is a bounded AS-PAR-002 authorization repair,
not complete whole-application acceptance.

## P1 local identity session binding

OIDC sign-in now requires a mapped, enabled local `(tenantId, objectId)` identity and
records its current local session epoch in the protected authentication ticket. The
development sign-in records the same epoch. Current-actor resolution rejects a ticket
whose epoch no longer matches the local user, so an old cookie cannot acquire the new
epoch after role revocation. The browser regression verifies that a refreshed journal
and review queue clear protected data when that happens. This corrects the earlier
review-ledger inference that incrementing the database epoch alone invalidated an
open circuit; the ticket needed to retain its sign-in epoch.

This is local P1 hardening, not live Entra acceptance. Approved runtime credentials,
tenant authorization, identity fixtures and observed live sign-in behavior remain
external prerequisites under issue #13. Downstream live-provider issues remain gated
by that acceptance. Exact checks and source identity are in `status.json`.

The owner kept the Purview and eSignature provider exclusions. The dedicated
provider issues #17–#19 were closed as not planned, and the recovery, operations,
and final-acceptance issues retain their in-scope work without those provider
prerequisites. The issue closures do not establish live records or signing acceptance.







## Period workbench access refresh

The restatement and roll-forward selection loaders clear prior projections and resolve
current actor/grant state before rebuilding scoped choices and history. Revoked client
access and disabled users clear client names, periods, package choices, draft values and
review actions. An engagement-only grant cannot restore client-level period access.
Scope/session-denied command results also clear the protected view. A stale source
package or restatement lineage now remains a visible service error when a fresh
client-scoped authorization check confirms the session is still valid.

`PeriodWorkbenchScopeJourneyTests` exercises the two screens with revoked grants and
disabled identities in an existing browser document and verifies no period/restatement
mutation. This is a bounded AS-PAR-002 AC02–AC03 / AC-24 repair; the whole-application
query, export, command and revocation audit remains open. Verification results and the
source baseline are recorded in `status.json`. Deployment guidance is unaffected.

The pending-work handoff links every story family and preserves optional professional
profile gates and root product exclusions. Follow specification §46 for independent
review and explicit merge authorization before advancing acceptance.


## 1. Active Implementation Scope







- **Active Work Package:** Audit workflow gap-closure, client accounting implementation, and repository-wide documentation & AI navigability refactoring.



- **Implementation Status:** `IN_PROGRESS`



- **Acceptance Status:** `LOCAL_VERIFIED`



- **Scope Boundary:** Import-first preparation, audit, and consolidation workspace (not an operational client ERP). Excludes client sales/purchase/inventory operations, payroll execution, and payment initiation. Purview and eSignature provider integrations are excluded from product scope: exact uploaded signed-document evidence, SHA-256 identities, human decisions, and release manifests are preserved without claiming provider acceptance.







---







## 2. Recent Completed Slices (Handoff Summary)







### 2.0 MudBlazor UI Content Migration (commit `e66bc49`)

- **All inventoried routes migrated:** every AuditSphereOps route renders its content
  through MudBlazor 9.10.0 primitives (`PageHeader`, `MudPaper`, `MudAlert`, `MudTable`,
  `MudButton`, `MudLink`, `StatusChip`, `LoadingState`, `MudGrid`) inside the existing
  Blazor Interactive Server boundaries. No authorization, revision-fencing or
  persistence behaviour changed.
- **Contracts preserved:** native `h1`/`h2`, `id`, `aria-labelledby`, `role`,
  `data-draft-*`, form `id`s and `.command-result` status text are retained;
  `draft-state.js` and `pbc-upload.js` are byte-identical to `master`.
- **Two E2E regressions found and fixed in the UI layer:** the contiguous
  `Status: SENT` invoice text, and the `GetByRole(AriaRole.Region)` lookup of the
  time form (labelled cards now carry an explicit `role="region"`).
- **Documented exceptions** in
  [`docs/auditsphere-ui-mudblazor-conventions-migration-current.md`](../auditsphere-ui-mudblazor-conventions-migration-current.md):
  6 native tables (`tfoot`/`colspan`) and the native browser-draft boundary
  elements. `/app/accounting/remeasurement` was left at its master markup in
  this slice because its browser-draft reload journey was sensitive to the
  MudBlazor render path; slice 3 migrated it (see 2.1).
- **State:** committed and pushed as `e66bc49`. No tenant operation or
  production effect.

### 2.1 MudBlazor Form Controls, Navigation & CSS (slice 3)

- **Lockfile health fixed:** the MudBlazor commit left
  `tests/AuditSphereOps.Api.Tests/packages.lock.json` and
  `tests/AuditSphereOps.E2E.Tests/packages.lock.json` stale (`NU1004` in locked
  mode). Both were regenerated with `dotnet restore --force-evaluate` on the
  repository-pinned SDK; locked-mode restore now passes with 0 errors and no
  unrelated package changed.
- **`CurrencyRemeasurement.razor` migrated:** MudBlazor shell (`PageHeader`,
  `MudLink`, `LoadingState`, `MudAlert`, `MudPaper`, `MudTable`, `MudButton`,
  `StatusChip`) with `MudTextField`/`MudNumericField` line fields whose inner
  inputs carry the forwarded `data-draft-field` attributes. The draft boundary
  section, its five selects and the date input stay native because
  `draft-state.js` reads GUID/bool/ISO values from `control.value` and the E2E
  journey asserts those exact values after reload. The draft-restore journey
  `CurrencyRemeasurementWorkbenchRestrictsContextAndRestoresBrowserDraft` passed
  9/9 explicit runs plus the full-suite runs (previously flaky).
- **Form controls migrated on 15 pages** with typed components
  (`MudTextField`, `MudNumericField<T>`, `MudSelect`, `MudCheckBox`); no native
  `<button>` remains anywhere. Two selects reverted back to native after the
  full suite exposed hard contracts (Playwright `SelectOptionAsync` on the
  package-review Stage select; `Locator("#decision-outcome")` visibility on the
  assessment decision page; raw-value `InputValueAsync` on the M365 capability
  selects) — documented in the migration document and in inline comments.
- **Accounting navigation standardized:** `AccountingNavigation.razor` and the
  `Consolidation`/`AccountingRecords` page tab rows now use a `MudPaper` bar of
  `aria-current`-carrying links (`.accounting-nav`); the broad `/app/accounting`
  prefix bug is fixed so only the correct entry is active.
- **CSS consolidated:** `.accounting-tabs`, `.button`, `.card-grid` and
  `button:disabled` removed; `.accounting-nav` rules added; no second CSS
  component framework. Shared components reviewed; MudBlazor remains Web-only
  (`ArchitectureGuardTests`).
- **State:** full Release suite green (counts in `status.json`), no EF model
  drift, no schema migration, tenant operation or production effect.

### 2.2 MudBlazor Migration Completion (slice 4)

- **Last two select exceptions closed at full assertion strength:** the
  `FinancialPackage` Stage select is `MudSelect`, driven by a new
  `SelectMudOptionAsync` journey helper (open the labelled select, click the
  exact option) choosing the same value with unchanged downstream assertions;
  the `AssessmentDecision` `#decision-outcome` id moved to the visible MudSelect
  wrapper `div`, so the partner visibility and unauthorized-manager absence
  checks needed no test change. The Microsoft 365 capability selects stay
  native permanently (raw-value `InputValueAsync` contract).
- **`draft-state.js` guard:** `<input type="hidden">` composite-widget internals
  (MudSelect combobox mirrors) are excluded from draft discovery and
  collection; visible draft-field semantics unchanged; `pbc-upload.js`
  byte-identical.
- **Route-render guarantee:** `RouteRenderSmokeTests` visits every
  parameterless inventoried route (19 staff routes plus the client portal)
  asserting heading render with zero page errors; detail routes keep their
  seeded journeys.
- **State:** Domain 338/338, Api 6/6, E2E 61/61, no EF model drift (counts in
  `status.json`); commit `dc43bc0` pushed; no tenant operation or production
  effect.

### 2.3 MudBlazor Migration Final Reconciliation (slice 5)

- **Verified counts recorded:** 42 page components declaring 49 routes,
  zero native `<button>`, 39 intentional native controls across 9 pages, and
  5 native tables (previously recorded as "47 routes / 6 tables" — corrected).
  See `docs/auditsphere-ui-mudblazor-conventions-migration-current.md` §8.
- **Last cleanup:** StatusChip now maps `POSTED`/`RECONCILED`/`ACCEPTED` to
  success and `RESUBMITTED` to info; the dead `.field-label`/`.context-bar`
  CSS rules were removed. `MainLayout`, `AuditSphereTheme`, `PageHeader`,
  `LoadingState`, `ScopeBanner` and `ConfirmDialog` reviewed and unchanged.
- **State:** Domain 338/338, Api 6/6, E2E 61/61, no EF model drift; commit
  `3d449fb` pushed; no tenant operation or production effect.


### 2.1 Documentation Standardization & AI Navigability Refactor



- **Standardized Filenames:** All non-root Markdown files standardized to `auditsphere-<area>-<document-type>-<subject>[-<id>][-<status>].md` with globally unique basenames. Root `README.md` and `AGENTS.md` preserved as conventional exceptions.



- **Capability-Focused Partials:** Services (`ConsolidationService`, `ClientAccountingService`, `AccountingAnalysisService`, `AuditFieldworkService`, `FinancialStatementService`), `AuditSphereDbContext`, and `ClientAccountingTests` decomposed into focused capability partial files with unchanged static APIs and test semantics.



- **Architectural Reference Guards:** `ArchitectureGuardTests.cs` enforces project reference layering (Domain references no outer project, Application references Domain only, Infrastructure references Application and Domain, never a host).



- **Documentation Policy & Automated Guards:** Established `docs/architecture/auditsphere-architecture-document-naming-policy.md`, `scripts/docs/validate-markdown-filenames.py`, and `MarkdownNamingGuardTests.cs`.



- **R2R Source Hashing Preserved:** `HISTORICAL_SOURCE` banners added and excluded from hash verification, preserving exact SHA-256 requirement hashes for R2R modules 20–26 and Audit workflow sources.







### 2.2 Re-Review Repairs & Currency Translation Completion (RR-01–RR-09, F01–F08)



- **Stable Reserve Line Identity (RR-01):** Derived cumulative translation reserve line IDs from immutable translation snapshots (`TranslationIdentities.CumulativeTranslationReserveLineId`), making replay, approval, and readback stable.



- **Fail-Closed FX Rates (RR-02, RR-03):** Removed missing-rate aggregate fallbacks; missing rate purposes return typed `gate.blocked` errors. Same functional and presentation currency translates as identity rate 1 without consuming observations.



- **Exact Line Rate Identity (RR-04):** Manifest rows carry their own line's consumed rate purpose and rate; classification resolves through recognized declared sections or approved mappings.



- **IAS 21 Rate Bridge (F01–F03):** `LineTranslationCalculator` computes translation reserve from equity and P&L rate differences with zero residual signed sum (`GOLD-R2R-07`), persisting `CTA_RESERVE` in consolidation balances.



- **Task Scope Reconciliation (RR-08, F06):** Reconciled T054 as R2R-only handover, moving audit handover `AS-AUD-028-AC10` to T075 with declared audit acceptance inputs.







---







## 3. Observed Local Verification State







All verifications are run against local PostgreSQL 18.6 on port 5433:







| Check | Expected Result | Authority Pointer |



|---|---|---|



| **Solution Build (Release)** | `0 Warning(s), 0 Error(s)` | `dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj --configuration Release` |



| **PostgreSQL Test Suites** | All tests pass, 0 skips (Domain, Api, E2E Playwright) | Consult [`docs/execution/status.json`](status.json) for authoritative counts |



| **EF Model Drift Check** | No pending model changes | `dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web` |



| **R2R Task Pack Status** | `valid: true`, 0 errors, 75 tasks | `python3 docs/task_breakdown/tools/task_status.py validate` |



| **Task Pack Self-Tests** | 21/21 passed | `python3 docs/task_breakdown/tools/test_task_status.py` |



| **Markdown Filename Guard** | 100% compliant, 0 collisions | `python3 scripts/docs/validate-markdown-filenames.py` |



| **Git Diff Cleanliness** | 0 whitespace or formatting errors | `git diff --check` |







---







## 4. Known Blockers & External Boundaries







The following external gates require live cloud infrastructure or partner sign-off and cannot be closed in local development:







- **P1 (Entra OIDC Authentication):** `BLOCKED_EXTERNAL` — requires live Azure AD tenant app registration and client secret mount.



- **P2 (Selected-Resource SharePoint/Graph):** `BLOCKED_EXTERNAL` — requires live Microsoft 365 tenant with selected-resource application scopes.



- **P3 (External Release Checkpoint Store):** `BLOCKED_EXTERNAL` — requires remote cryptographic immutable storage.



- **P7 (Cross-Store Recovery):** `BLOCKED_EXTERNAL` — requires multi-region cloud backup infrastructure and tested RPO/RTO.



- **P8 (Production Secrets & Observability):** `BLOCKED_EXTERNAL` — requires production Key Vault and OpenTelemetry collector.



- **P9 (Independent Merge Review):** `BLOCKED_EXTERNAL` — requires partner sign-off.



- **P10 (Real-Tenant Acceptance §47):** `BLOCKED_EXTERNAL` — requires customer-authorized tenant execution.







---







## 5. What the Next Agent Should Know







1. **Read Order:** Always read [`AGENTS.md`](../../AGENTS.md) and [`docs/auditsphere-docs-index.md`](../auditsphere-docs-index.md) before performing work.



2. **Architecture Authority:** [`docs/architecture/auditsphere-architecture-current-architecture.md`](../architecture/auditsphere-architecture-current-architecture.md) defines the implemented architecture. Do not introduce MediatR, microservices, or new layers.



3. **Capability File Map:** Consult [`docs/architecture/auditsphere-architecture-code-map.md`](../architecture/auditsphere-architecture-code-map.md) to locate relevant partial classes, domain records, and test fixtures.



4. **Volatile Facts Rule:** Never add live test counts, migration counts, or commit hashes to narrative documentation. Record them only in [`docs/execution/status.json`](status.json).



5. **Next Work Packages:** Consult [`docs/execution/auditsphere-execution-pending-tasks.md`](auditsphere-execution-pending-tasks.md) for open local work:



   - Continue the AC-01–AC-28 and AS-PAR-002 route/parameter revocation review.



   - Formalize G16 period-end open-item methodology approval.



   - Maintain documentation health validation.







---







## 6. Standard Verification Commands







```bash



# 1. Build solution in Release



dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj --no-restore --configuration Release







# 2. Check for EF Core model drift



dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web







# 3. Validate R2R task pack & pinned source hashes



python3 docs/task_breakdown/tools/task_status.py validate







# 4. Run task status self-tests



python3 docs/task_breakdown/tools/test_task_status.py







# 5. Validate markdown naming policy



python3 scripts/docs/validate-markdown-filenames.py







# 6. Run domain and architecture guard tests



dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --filter "FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~MarkdownNamingGuardTests"



```
