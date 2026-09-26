# AuditSphere — MudBlazor UI Conventions & Migration Status

**Status:** CURRENT (slice 2 — MudBlazor content migration for all inventoried routes)

## 1. Selected version

- MudBlazor **9.10.0** (released 2026-09-13), centrally pinned in
  `Directory.Packages.props`, referenced only from
  `src/AuditSphereOps.Web/AuditSphereOps.Web.csproj`.
- Targets `net8.0`; compatible with this repo's `net10.0` (verified by
  Release build + runtime E2E). No Domain/Application/Infrastructure
  reference (enforced by `ArchitectureGuardTests`).
- Lockfile `src/AuditSphereOps.Web/packages.lock.json` regenerated via
  `dotnet restore --use-lock-file`.

## 2. Composition

- `Program.cs`: `builder.Services.AddMudServices()` (`MudBlazor.Services`
  namespace) within existing Interactive Server boundaries. No render-mode
  change.
- `Components/App.razor`: MudBlazor CSS/JS (`_content/MudBlazor/...`)
  after Blazor assets; existing `pbc-upload.js` and `draft-state.js`
  ordering preserved.
- `Components/Layout/MainLayout.razor`: `MudThemeProvider` (AuditSphere
  theme) + `MudPopoverProvider` + `MudDialogProvider` +
  `MudSnackbarProvider`; responsive `MudDrawer`; skip-link preserved.
- `Components/_Imports.razor`: `MudBlazor`, `Shared`, `Theme` namespaces.

## 3. Theme & shared components (presentation only)

- `Components/Theme/AuditSphereTheme.cs`: primary #075985, secondary
  #0E7490, surface/background aligned with existing `app.css`; compact
  radius; enterprise typography.
- `Components/Shared/StatusChip.razor`: color + icon so status is never
  color-only.
- `Components/Shared/PageHeader.razor`: native `h1` (keeps
  `FocusOnNavigate` + Playwright `GetByRole(Heading)` selectors stable).
- `Components/Shared/LoadingState.razor`: loading / error(h2) / empty /
  ready states. Accepts `IsLoading`/`LoadingMessage` (page call sites) and
  `Loading`/`LoadingText` aliases, and captures unmatched attributes so a
  presentation-only call site can never break rendering.
- `Components/Shared/ConfirmDialog.razor`: consequence + record identity,
  optional required reason, duplicate-submit guard, server error slot,
  no premature success.
- `Components/Shared/ScopeBanner.razor`: informational scope note.

## 4. Migration pattern applied to every route

- `<section class="card">` → `<MudPaper Class="pa-4 my-4" Elevation="1">`.
  When the section was labelled, `role="region"` is added explicitly so the
  implicit `section`→`region` mapping (and Playwright
  `GetByRole(AriaRole.Region)`) is preserved.
- `<section class="notice[ success| error| warning]">` → `<MudAlert>` with the
  matching `Severity`, keeping the original `role` attribute
  (`status`/`alert`) and native `h1`/`h2` headings for stable selectors.
- `<button class="button">` / `<button class="btn-secondary">` /
  `<a class="button">` → `<MudButton>` (`Color`/`Variant`/`OnClick`/
  `Disabled`/`Href`/`ButtonType`); form submit buttons keep
  `ButtonType="ButtonType.Submit"` inside their original `<form>`.
- `<a class="back-link">` → `<MudLink Class="back-link mb-2 d-inline-block">`.
- `<span class="status">` → `<StatusChip>` (text + colour + icon).
- `<p role="status">Loading …</p>` → `<LoadingState>` (progress + status text).
- `<table>` (single header row, one `@foreach` body, no `tfoot`, no
  `colspan`/`rowspan`) → `<MudTable Items="…">` with `<HeaderContent>` /
  `<RowTemplate>`; the row variable is bound to the MudTable `context`.
  No pager is added, so every row that existed before still renders in the
  DOM.
- Metric strips → `<MudGrid>` + `<MudItem>` + `<MudPaper>`.
- Native `h1`/`h2`, `id`, `aria-labelledby`, `role`, `data-draft-scope`,
  `data-draft-field`, `data-optional`, form `id`s and `.command-result`
  status text are preserved verbatim; `draft-state.js` and `pbc-upload.js`
  keep working because their hooks are untouched.


## 5. Per-route completion (47 inventoried routes)

- **Content-migrated**: every inventoried route now renders through the
  MudBlazor shell and MudBlazor content primitives (`PageHeader`,
  `MudPaper`, `MudAlert`, `MudTable`, `MudButton`, `MudLink`, `StatusChip`,
  `LoadingState`, `MudGrid`). `/app`, `/app/overview` (`Portfolio`) and
  `/app/practice/leads` (`Leads`) additionally use `MudTextField` form
  controls, a `MudTable` pager and the `ConfirmDialog`/`ISnackbar`
  qualification flow; server validation (`PracticeCrmService`) remains
  authoritative and duplicate submission is still guarded by `_busy`.
- Routes covered: `/`, `/app`, `/app/overview`, `/app/practice/leads`,
  `/app/practice/time`, `/app/practice/proposals/{Id:guid}`,
  `/app/practice/invoices/{InvoiceId:guid}`, `/app/finance`,
  `/app/operations`, `/app/administration`,
  `/app/administration/microsoft365`, `/app/clients/{ClientId:guid}`,
  `/app/engagements/{EngagementId:guid}`,
  `/app/engagements/{EngagementId:guid}/pbc`,
  `/app/engagements/{EngagementId:guid}/audit-plan`,
  `/app/engagements/{EngagementId:guid}/audit-fieldwork`,
  `/app/engagements/{EngagementId:guid}/completion`,
  `/app/accounting`, `/app/accounting/evidence`,
  `/app/accounting/periods/{PeriodId:guid}`,
  `/app/accounting/packages/{PackageId:guid}`, `/app/accounting/reviews`,
  `/app/accounting/rollforward`, `/app/accounting/restatements`,
  `/app/accounting/remeasurement`, `/app/accounting/mappings`,
  `/app/accounting/mappings/{MappingId:guid}`, `/app/accounting/journals`,
  `/app/accounting/journals/{JournalId:guid}`,
  `/app/accounting/differences`, `/app/consolidation`,
  `/app/consolidation/advanced/{ScopeId:guid}`,
  `/app/completion/{Id:guid}`,
  `/app/audit/library`, `/app/audit/plans/{Id:guid}`,
  `/app/audit/populations/{Id:guid}`, `/app/audit/workpapers/{Id:guid}`,
  `/app/assessments/{Id:guid}`, `/app/assessments/{Id:guid}/decision`,
  `/app/clients/{ClientId:guid}/assessment`, `/app/findings/{Id:guid}`,
  `/app/reviews/{Id:guid}`, `/app/records/archives/{Id:guid}`,
  `/app/releases/{CandidateId:guid}`, `/portal`,
  `/portal/requests/{RequestId:guid}`,
  `/portal/accounting/packages/{PackageId:guid}`, `/setup/microsoft365`,
  `/auth/access-not-assigned`.
- **Deliberate exceptions, each with an observed reason:**
  1. **6 tables stay native `<table>`** (inside `MudPaper`): totals rows
     (`<tfoot>`) in `Journals`, `InvoiceDetail`, `FinancialPackage`,
     `Consolidation` and row-spanning status rows (`colspan`) in
     `FinancialPackage` and `PbcRequests` have no `MudTable` equivalent.
  2. **8 browser-draft boundary elements stay native
     `<section class="card">`** (`PracticeTime` task scope,
     `AccountingWorkspace` context selector, `AdvancedConsolidationWorkflow`
     schedule scope, `PbcRequests` new request, `ClientPbcRequest` upload and
     reply, `FinancialPackageReviews` selection, `CurrencyRemeasurement`
     workpaper). `draft-state.js` discovers draft boundaries by
     `[data-draft-scope]`/`.card` element and filters nested candidates by
     DOM ancestry; swapping that element for a MudPaper `<div>` changes the
     boundary/element identity that the script and Blazor's DOM patching
     depend on, so these elements keep their original markup.
  3. **`/app/accounting/remeasurement` content is not migrated.** Its
     browser-draft autosave/restore-across-reload journey
     (`CurrencyRemeasurementWorkbenchRestrictsContextAndRestoresBrowserDraft`)
     is sensitive to the additional first-render work introduced by the
     MudBlazor shell: with the shell applied and the page untouched at HEAD,
     the same journey failed once and then passed on a rerun. The page is
     therefore left at its HEAD markup until that sensitivity is resolved;
     no behaviour or selector was changed.



## 6. Guardrails observed

- No DB migration, no business-rule/authorization change, no competing UI
  library, no unrelated refactor. MudBlazor stays isolated to
  `AuditSphereOps.Web`.
- E2E heading/text/role selectors preserved; `draft-state.js` and
  `pbc-upload.js` are byte-identical to `master`.
- Two E2E failures found during this slice were fixed in the UI layer, not in
  the tests:
  - `ProspectTimeBillingAndManualFirmCloseKeepTheirBoundaries` asserted the
    contiguous text `Status: SENT`; the inline `StatusChip` split that text
    across elements, so the invoice, journal and proposal eyebrow lines keep
    their original plain-text form.
  - `ProspectTimeBillingAndManualFirmCloseKeepTheirBoundaries` also located
    the time form by `GetByRole(AriaRole.Region, "Record time draft")`; every
    labelled card now carries an explicit `role="region"` so the implicit
    `section`→`region` mapping is preserved.
- Verification of this slice (local PostgreSQL 18.6 on `127.0.0.1:5433`):
  - `dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj
    --no-restore --configuration Release` — 0 warnings, 0 errors.
  - `dotnet ef migrations has-pending-model-changes` — no changes since the
    last migration.
  - `dotnet test tests/AuditSphereOps.Domain.Tests` — 338/338 passed, 0 skipped
    (includes the three `ArchitectureGuardTests`).
  - `dotnet test tests/AuditSphereOps.Api.Tests` — 6/6 passed, 0 skipped.
  - `dotnet test tests/AuditSphereOps.E2E.Tests` — 60/60 passed, 0 skipped.
- No hosted CI run, tenant operation or production effect is claimed for this
  slice; the change is uncommitted working-tree state.

