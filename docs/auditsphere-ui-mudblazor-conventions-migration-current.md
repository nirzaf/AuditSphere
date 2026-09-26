# AuditSphere — MudBlazor UI Conventions & Migration Status

**Status:** CURRENT (slice 3 — form controls, accounting navigation and CSS consolidation)

## 1. Selected version

- MudBlazor **9.10.0** (released 2026-09-13), centrally pinned in
  `Directory.Packages.props`, referenced only from
  `src/AuditSphereOps.Web/AuditSphereOps.Web.csproj`.
- Targets `net8.0`; compatible with this repo's `net10.0` (verified by
  Release build + runtime E2E). No Domain/Application/Infrastructure
  reference (enforced by `ArchitectureGuardTests` and a source scan).
- Lockfiles: `src/AuditSphereOps.Web/packages.lock.json` regenerated when the
  package was introduced; the transitive `MudBlazor` entries for the two test
  projects that reference the Web project
  (`tests/AuditSphereOps.Api.Tests/packages.lock.json`,
  `tests/AuditSphereOps.E2E.Tests/packages.lock.json`) were regenerated in
  slice 3 with `dotnet restore AuditSphereOps.slnx --force-evaluate` after the
  initial commit left them stale (`NU1004` in locked mode). No unrelated
  package versions changed.

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

## 4b. Form-control pattern (slice 3)

Remaining native controls were migrated with the matching MudBlazor type
rather than a universal text field:

```text
string input        -> MudTextField T="string" (@bind-Value, MaxLength, Immediate for
                       the previous @bind:event="oninput" call sites)
password / email/url-> MudTextField with InputType="InputType.Password|Email|Url"
decimal/int number  -> MudNumericField<T> (Step, Min, Max; Min is only set where the
                       original input set min — prior carrying amounts may be credits)
textarea            -> MudTextField Lines="n" (MudInput renders a real <textarea>)
select (string)     -> MudSelect T="string" + MudSelectItem (Value preserves the exact
                       stored value string, including Guid "D" strings and constants)
checkbox            -> MudCheckBox T="bool"
action button       -> MudButton (already Mud everywhere; no native <button> remains)
```

Attribute forwarding is the load-bearing mechanic: `MudTextField`/`MudNumericField`
splatted (`UserAttributes`) attributes render on the **inner `<input>`/`<textarea>`**
element, and `MudSelect` forwards them via `GetInputUserAttributes()` to the select
input. This keeps `id=` (label `for` association, `page.Locator("#id")` fills),
`required`/`title`/`maxlength`/`pattern`/`data-optional`/`data-draft-field`/
`data-draft-skip`/`autocomplete` on the element the browser and `draft-state.js`
actually read. Playwright `FillAsync`, `InputValueAsync` and draft save/restore
therefore keep working unchanged.

`MudSelect` has three hard contract limits, observed in slice 3, that decide where a
native `<select>` stays: (a) Playwright `SelectOptionAsync` only works on a native
`<select>`; (b) the `MudSelect` combobox input renders `type="hidden"`, so a
`Locator("#id")` **visibility** assertion fails; (c) the combobox input's value is
display text, so raw-value assertions such as `InputValueAsync() == "NOT_CONFIGURED"`
break. Selects under any of these contracts stay native; everything else converts.

## 4c. Accounting secondary navigation (slice 3)

- `Components/Layout/AccountingNavigation.razor`: the custom `.accounting-tabs`
  anchor row is now a `MudPaper` bar (`Elevation="0"`) of links carrying
  `class="accounting-nav-link"`, `title` and `aria-current="page"` on the active
  entry. Native `<a>` elements are kept (instead of `MudLink`) because `MudLink`
  has no `Title` parameter (MudBlazor analyzer `MUD0002`) and route navigation is
  link semantics, not tab-panel semantics; keyboard navigation is native anchor
  focus. The broad-prefix bug is fixed: `/app/accounting` is active on
  `/app/accounting` alone, while section entries (journals, mappings, …) stay
  active on their detail routes via `target + "/"` prefix matching.
- The two page-level tab rows using the same classes were converted to the same
  pattern: `Consolidation.razor` (in-page anchor links) and
  `AccountingRecords.razor` (mode links with `aria-current`).
- `.accounting-tabs` CSS was removed; `.accounting-nav`/`.accounting-nav-link`
  styles replace it (see §6b).


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
- **Form controls migrated (slice 3):** every remaining native `<input>`,
  `<textarea>` and `<button>` outside the exceptions below now renders through
  MudBlazor. Pages migrated in slice 3: `AuditPlan` (24 controls),
  `CurrencyRemeasurement` (10 of 16; see exception 3), `PeriodRollforward`
  (12), `Microsoft365Setup` (8 of 12; see exception 4), `Administration` (11),
  `PeriodRestatements` (9), `FinancialPackage` (4 of 5; see exception 5),
  `ClientDetail` (4), `AssessmentDecision` (3 of 4; see exception 5),
  `ClientFinancialPackage` (3), `Finding` (2), `AuditProgramLibraryPage` (2),
  `Operations` (1), `Release` (1), `AuditFieldwork` (1). Each page also
  carries an inline HTML comment documenting any retained native element and
  why.
- **Deliberate exceptions, each with a technical reason:**
  1. **6 tables stay native `<table>`** (inside `MudPaper`): totals rows
     (`<tfoot>`) in `Journals`, `InvoiceDetail`, `FinancialPackage`,
     `Consolidation` and row-spanning status rows (`colspan`) in
     `FinancialPackage` and `PbcRequests` have no `MudTable` equivalent.
  2. **Browser-draft boundary elements stay native** (`PracticeTime` both
     forms, `AccountingWorkspace` context selector,
     `AdvancedConsolidationWorkflow` schedule scope, `PbcRequests` new
     request, `ClientPbcRequest` upload and reply,
     `FinancialPackageReviews` selection, `CurrencyRemeasurement` prepare
     boundary). `draft-state.js` discovers boundaries by
     `[data-draft-scope]`/`.card`/`form:not(.card)` and collects draft fields
     by reading `control.value`/`checked` from
     `[data-draft-field]` `input`/`select`/`textarea` nodes, restoring by
     writing those values and dispatching `change`/`input` events. Swapping
     boundary elements or value-bearing controls for composites that do not
     expose the stored value on a real control changes what the script saves
     and restores, so these elements keep their original markup. Every such
     spot carries an inline HTML comment with this reason.
  3. **`CurrencyRemeasurement` keeps 5 native `<select>` and 1 native
     `<input type="date">`** inside its draft boundary: the draft stores raw
     GUID/bool/ISO strings read from `.value` (a `MudSelect` input mirror
     holds display text), the E2E journey asserts the GUID via
     `GetByLabel(...).InputValueAsync()` and `page.Locator("select").First`,
     and the date field's draft round-trip is the ISO `yyyy-MM-dd` string.
     Its text and numeric line fields are MudBlazor controls with forwarded
     `data-draft-field` attributes.
  4. **`Microsoft365Setup` keeps 4 native `<select>`**: the M365 journey
     asserts raw option values with
     `page.Locator("#mail-state").InputValueAsync()` equal to
     `NOT_CONFIGURED`; a `MudSelect` exposes display text instead. Its text,
     password, URL inputs and manifest textarea are MudBlazor controls.
  5. **Two selects stay native under interaction contracts:**
     `FinancialPackage` "Stage" (the package-review journey drives it with
     Playwright `SelectOptionAsync`) and `AssessmentDecision`
     `#decision-outcome` (the decision journey asserts
     `Locator("#decision-outcome")` visibility; a `MudSelect` combobox input
     renders hidden). Both carry inline comments. Their sibling controls are
     MudBlazor.
  6. **`Workpaper` keeps 2 native `<textarea>`** (`data-draft-skip` +
     `value`/`@oninput`) because they feed the server-side working-draft
     autosave interop rather than Blazor binding.
  7. **`PbcRequests` keeps the per-request reply `<textarea>`s** (dynamic
     per-request ids with `value`/`@oninput`, avoiding row re-render churn)
     in addition to its draft-boundary section.
  8. **`ClientPbcRequest` keeps the `<input type="file">`** (browser
     `FileList` API; `pbc-upload.js` also writes the readonly file name,
     content-type and byte-count fields by id) plus its draft-boundary
     SHA-256 field and reply box.



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
- Verification of slices 2+3 (local PostgreSQL 18.6 on `127.0.0.1:5433`):
  - `dotnet restore AuditSphereOps.slnx --force-evaluate` then
    `dotnet restore AuditSphereOps.slnx --locked-mode` — locked restore passes
    with 0 errors after regenerating the Api/E2E test-project lockfiles.
  - `dotnet build AuditSphereOps.slnx --no-restore --configuration Release` —
    0 warnings, 0 errors.
  - `dotnet ef migrations has-pending-model-changes` — no changes since the
    last migration.
  - `dotnet test AuditSphereOps.slnx` (Domain + Api, Release) — Domain 338/338,
    Api 6/6, 0 skipped (includes the three `ArchitectureGuardTests`).
  - `dotnet test tests/AuditSphereOps.E2E.Tests` — 60/60 passed, 0 skipped.
  - Draft-restore journey
    `CurrencyRemeasurementWorkbenchRestrictsContextAndRestoresBrowserDraft`:
    9/9 explicit runs plus the full-suite runs, 0 failures (it previously
    failed intermittently with the MudBlazor shell applied).
- No tenant operation or production effect. MudBlazor remains Web-only.

## 7. CSS consolidation (slice 3)

`wwwroot/app.css` keeps only shell/layout styles (topbar, sidebar, content,
hero), shared semantic text styles (`.muted`, `.scope-note`, `.details`,
`.field-help`, `.sr-only`, focus-visible outlines), styles still referenced by
retained native elements (`table`/`th`/`td`, `input`/`select`/`textarea`,
`.form-grid`, `fieldset`, `.draft-status`, `.table-wrap`), styles applied to
MudBlazor components as surface tokens (`.btn-primary`/`.btn-secondary`/
`.btn-small` on `MudButton`, `.notice` inside `MudAlert`), and the new
`.accounting-nav`/`.accounting-nav-link` rules. Removed as obsolete:
`.accounting-tabs` (replaced by `.accounting-nav`), `.button`
(native-button styling; no references remain), `.card-grid`
(no references remain), and `button:disabled` (MudBlazor owns disabled
buttons). No second CSS component framework was introduced; reusable visual
tokens remain in `Components/Theme/AuditSphereTheme.cs`.
