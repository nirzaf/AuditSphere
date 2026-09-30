# AuditSphere — MudBlazor UI Conventions & Migration Status

**Status:** CURRENT — MudBlazor control migration complete; prototype-inspired visual modernization in progress

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
  ordering preserved. `enterprise-ui.css` loads after MudBlazor CSS for the
  presentation layer; it does not import the prototype stylesheet.
- `Components/Layout/MainLayout.razor`: `MudThemeProvider` (AuditSphere
  theme) + `MudPopoverProvider` + `MudDialogProvider` +
  `MudSnackbarProvider`; responsive `MudDrawer`; skip-link preserved. Route
  layout selection keeps `/portal` in `ClientLayout` and public/auth/setup
  routes in `PublicLayout`, without the staff navigation.
- `Components/_Imports.razor`: `MudBlazor`, `Shared`, `Theme` namespaces.

## 3. Theme & shared components (presentation only)

The group consolidation landing route now uses read-only counts of authorized
groups, scope versions and approved component pins. Its dense perimeter,
component, FX, advanced-schedule, intercompany and elimination registers
remain source-backed and scroll within their panels at wider widths, with
mobile cell labels at narrow widths. The approved-report area links to the
existing guarded advanced workflow. The advanced consolidation detail route now has scoped record context,
semantic perimeter status, read-only schedule/execution counts, compact group
facts and responsive JSON editors. The native draft controls remain in the
same draft boundary and their reload recovery is checked in a browser journey.
Schedule and execution registers use white panels, contained tables, mobile
cell labels and explicit empty states. These counts are not approval or
release-readiness signals, and no prototype-only method or business action
was added.

The staff PBC request inbox now uses the shared scoped record header, links
and count cards. Its native request form reflows from two columns to one,
while the upload table scrolls within its panel on narrow screens. Counts
describe only authorized loaded requests and transfer intents; a received
upload is not an evidence-suitability or completion decision. The existing
draft autosave scope, upload state transitions, client isolation and download
authorization remain unchanged.

Workpaper, finding and review-point details now use the shared record context
and white panels, with scoped status and navigation links. Workpaper revisions
and frozen-submission counts and finding state are read-only projections from
authorized rows. An open significant review point displays its existing
completion blocker. The native workpaper textareas retain their server draft
autosave contract; no prototype-only upload, assignment or review action was
copied. Mobile history cells have labels, and long record text wraps.

The audit population detail route now uses the same record toolbar, semantic
status chip, scoped count cards and white panels. A compact facts grid preserves
the complete receipt, extraction, exclusion and monetary context. The selection
table has mobile labels and a contained horizontal scroll area; empty selection
and evidence states use informational alerts. The counts repeat only authorized
population and item-test-review rows, with no sampling-sufficiency or audit
conclusion claim. No import, selection or review action was copied from the
prototype because this detail route has no such command surface.

Audit planning and completion now use the shared scoped metric, white-panel
and record-navigation styles. Planning forms retain every real command and
field while using two columns on wide screens and one on mobile. Completion
gate rows have mobile labels and long representation references wrap. The
completion route also reauthorizes and clears prior data on an in-place
engagement parameter change; this is a scope-safety repair, not a new
professional or release decision. Counts repeat authorized loaded rows and
do not indicate readiness to issue.

The audit program library and engagement fieldwork pages now use the compact
workspace hierarchy, scoped metrics and white panels. Fieldwork's section
selector initially limits the visible register to the first adopted source
section while retaining an All sections option; it does not modify persisted
procedure status or authorization. Library and fieldwork tables have mobile
cell labels, and long source hashes and version labels reflow within panels.

Accounting period detail, roll-forward and restatement routes now use the
shared workbench heading and panel styles. The two maintenance forms adapt
from three columns to one at narrow widths, and their history tables expose
mobile cell labels. Counts repeat scoped rows already loaded by each page;
they do not change period state or review authority.

Mapping and adjustment-journal detail pages now use the established record
header, metric and panel styles. Their links return to the matching scoped
queue. Mapping MudTables have `DataLabel` values for narrow-screen stacked
cells and scroll containers for wide content; the accounting commands,
authorization and evidence boundaries are unchanged.

The administrator project-progress route now includes every main application
area. Modules 20–26 and the four published audit task phases show measured
completion and pending counts in a four-state segmented bar; areas outside
that task pack show a neutral untracked bar and no
percentage. This does not derive implementation completion from live business
records or bypass task-card review gates.
Mapped module cards and the overall summary also show the completed-card
percentage calculated from their published task-card denominator. Audit and
shared-foundation summaries use the same calculation; a module with no mapped
cards never receives a fabricated percentage.
An administrator can filter the task-card lists by completed, active or in
review, pending or reopened, and blocked status. The filter leaves every
module bar and its full-source counts unchanged.

The accounting mapping, journal, difference and package-review queues now
show compact counts from their already scoped rows. Their local route links,
exact detail links, native review-selection checkboxes and draft fields remain
unchanged. Empty queues use informational alerts rather than color-only text;
linked-journal and selected-for-preview counts do not imply approval or
posting. Responsive captures cover the queue routes before and after this
presentation pass, with keyboard focus on the accounting queue links.

The accounting workspace and evidence queue now use the shared heading,
metric and white-panel rhythm. The workspace completion bar is a native
`<progress>` element derived only from its authorized visible period workflow
rows; it is not a financial-close, review or production-readiness claim. The
context selector remains native for the `draft-state.js` control-value
contract. The shared staff drawer reopens when a mobile viewport returns to
desktop; it still uses MudBlazor's responsive breakpoint and user toggle.
The prototype's richer accounting forms and lifecycle actions require the
existing real routes or backend contracts and were not copied into these
landing views.

The practice/firm-finance route family uses authorized collection counts in
compact metric cards and scoped white panels. Native task/time `<form>`
boundaries, browser-draft attributes and input values remain intact; their
layout uses responsive grid classes so paired fields stack at narrow widths.
The Leads `MudCard` and firm-ledger `MudPaper` surfaces share the palette and
panel rhythm. The prototype's extra commercial and billing fields are not
displayed without corresponding backend contracts.

The administrator Operations route now uses the same heading, scoped white
panels and compact metrics. Its operating mode and state counts repeat only
the authorized latest-operations projection already loaded by the page; the
bounded counts do not claim worker health or all-time completion. The existing
redacted ledger and guarded recovery actions remain unchanged. The table keeps
mobile cell labels and wraps long classification codes. Matching synthetic
before/after captures and a six-width browser journey cover this visual pass.

The release-candidate and records-archive detail routes now share the record
heading and white-panel layout. Release keeps its exact gate evidence and key
input, adding only a warning derived from its existing preflight evidence checks.
Archive shows authorized manifest/version/hold context without asserting
external protection; long hashes and the entry table reflow on narrow screens.
An unknown or out-of-scope archive has one unavailable message. Prototype-only
remote archive, signing and handover controls remain absent.

The client assessment detail and partner decision routes now use the same
record headings and white panels. Assessment shows a scoped response progress
bar only when question templates exist, alongside exact specialist-clearance
counts and current decision status. The clearance table has mobile labels, and
unknown and out-of-scope assessment IDs share one unavailable state. The partner
decision form keeps its immutable command and required-field validation. The
staff shell now gives the main workspace its full available width at mobile
breakpoints rather than reserving the closed drawer's width.

The protected Microsoft 365 setup route now shows a seven-step bar derived
only from the saved draft's existing progress query. Its count omits historical
consent evidence until independent consent verification succeeds, and the UI
labels it as a local checklist rather than live provider readiness. The
advanced native selects, bootstrap proof, saved-draft controls and selected-
resource checks retain their current behavior. Optional labels no longer
repeat the same qualifier in this form, and versioned-template rows have
mobile table labels.

The administrator tenant-connection route now uses the shared white-panel
hierarchy and a mobile-stacked tenant summary. Its capability table retains
every provider state and shows labeled cells on small screens; the directory
search fields reflow without changing the bounded provider query or exact
identity-binding action. A read-only bar counts fresh, verified checks among
enabled Microsoft permission capabilities. Sign-in configuration and the
selected SharePoint site stay outside that count because their evidence and
verification paths are separate. Consent return, verified consent, directory
read, local role assignment and production readiness remain distinct states.

The firm-administration workbench now uses the shared compact heading,
responsive white overview cards and evidence-based setup progress list. The
role-binding form has a two-column desktop layout without changing its exact
identity fields or server checks. Wide user, grant, capability, permission
and history registers scroll within their panels on desktop; mobile tables
retain labeled rows. All existing administration tabs, role actions, Microsoft
operations and immutable history remain available. The prototype's demo
personas, reset action and simulated identity lifecycle were not copied.

The administrator-only project task-progress route uses accessible
four-state progress bars for reviewed task-card counts across Modules 20–26,
each of the four audit task phases, their audit aggregate and cross-module
foundations. Its labels and expanded
task lists remain readable without color. The published task manifest and
front-matter files are copied into the Web output outside `wwwroot`; the page
checks current firm-administration authority before loading them. The tracker
is a read-only implementation ledger, not a live client workflow or software
readiness percentage.

- `Components/Theme/AuditSphereTheme.cs`: primary #2B6CB0, contrast-safe dark
  teal #0B6B65 for interactive text and #38B2AC for decorative accents,
  navy #0F172A drawer, white top bar and surfaces, #F8FAFC page
  background; compact enterprise typography. `enterprise-ui.css` holds the
  layout, responsive and shared-surface rules. The prototype remains read-only.
- `Components/Shared/StatusChip.razor`: color + icon so status is never
  color-only. The variant map covers the Domain workflow vocabulary observed
  at the call sites (`DRAFT`/`SUBMITTED`/`RESUBMITTED` → info,
  `APPROVED`/`ACTIVE`/`POSTED`/`RECONCILED`/`ACCEPTED`/`ISSUED`/… → success,
  `REJECTED`/… → error, `LOCKED`/`STALE`/… → warning); any future status
  falls back to a neutral chip with text and icon, never color alone.
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


## 5. Per-route completion (42 page components, 49 inventoried routes)

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
  `PeriodRestatements` (9), `FinancialPackage` (5 of 5),
  `ClientDetail` (4), `AssessmentDecision` (4 of 4),
  `ClientFinancialPackage` (3), `Finding` (2), `AuditProgramLibraryPage` (2),
  `Operations` (1), `Release` (1), `AuditFieldwork` (1). Each page also
  carries an inline HTML comment documenting any retained native element and
  why.
- **Deliberate exceptions, each with a technical reason:**
  1. **5 tables stay native `<table>`** (inside `MudPaper`) — reconciled count
     against the current tree: totals rows (`<tfoot>`) in `Journals`,
     `InvoiceDetail` and `Consolidation`, and row-spanning rows (`colspan`,
     e.g. the empty-state row spanning five columns in `FinancialPackage` and
     the status rows in `Consolidation`/`PbcRequests`) have no `MudTable`
     equivalent; `PbcRequests` and `FinancialPackage` also keep their
     `sr-only` table captions through the native element.
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
  5. **`Microsoft365Setup` is the only native-`<select>` page outside draft
     boundaries**: its journey asserts raw option values with
     `page.Locator("#mail-state").InputValueAsync()` equal to
     `NOT_CONFIGURED`. A `MudSelect` combobox holds display text, not the
     stored value, so converting would weaken a raw-value assertion into a
     display-text assertion. This is a permanent, contract-based exception.
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
- **Slice 4 completion of the former select exceptions:** the
  `FinancialPackage` "Stage" select and the `AssessmentDecision`
  `#decision-outcome` select are now `MudSelect`. The package-review journey
  selects the stage through the MudBlazor popover (`SelectMudOptionAsync`:
  open the labelled select, click the exact option) — the same value is
  chosen and every downstream assertion is unchanged. The decision journey
  needed no change: `#decision-outcome` moved to the visible MudSelect
  wrapper `div`, so the visibility/absence assertions keep their original
  strength for the partner and unauthorized-manager cases.



## 6. Guardrails observed

- No DB migration, no business-rule/authorization change, no competing UI
  library, no unrelated refactor. MudBlazor stays isolated to
  `AuditSphereOps.Web`.
- E2E heading/text/role selectors preserved; `pbc-upload.js` is byte-identical
  to `master`. `draft-state.js` gained one defensive change in slice 4:
  composite-widget internals (`<input type="hidden">`, e.g. a `MudSelect`
  combobox mirror) are excluded from draft discovery and collection, because a
  hidden input never carries user draft data and auto-enrolling one would
  pollute the stored draft JSON. Visible draft-field semantics are unchanged.
- Slice 4 route-render guarantee: `RouteRenderSmokeTests` signs in a dedicated
  firm-wide staff identity and visits every parameterless inventoried route
  (19 staff routes plus the client portal), asserting each page's heading
  renders through the MudBlazor shell with no unhandled page error. Detail
  routes with route parameters keep their dedicated seeded journeys.
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
(no references remain), `button:disabled` (MudBlazor owns disabled
buttons), and — in the final reconciliation — `.field-label` and
`.context-bar` (zero references in markup, code and scripts; note that
`.required-label`/`.optional-label` stay because `draft-state.js` applies
them at runtime). No second CSS component framework was introduced; reusable visual
tokens are shared by `Components/Theme/AuditSphereTheme.cs` and the
presentation-only `wwwroot/enterprise-ui.css` layer.

## 8. Final reconciliation (verified against the current tree)

- **42 page components** under `src/AuditSphereOps.Web/Components/Pages`
  declaring **49 `@page` routes** (six pages carry a second/third route alias:
  `AccountingRecords` x3, `AssessmentDetail`, `AuditPlan`, `Completion`,
  `Microsoft365Setup`, `Portfolio` x2). All routes live in `Pages/`.
- **Zero native `<button>` elements** remain; every action renders through
  `MudButton`/`MudIconButton`.
- **39 native `input`/`select`/`textarea` controls remain across 9 pages**, each
  an intentional exception with an inline comment and a documented reason:
  `draft-state.js` boundaries (PracticeTime 10, PbcRequests 6,
  ClientPbcRequest 6, FinancialPackageReviews 2, AdvancedConsolidationWorkflow 2,
  AccountingWorkspace 1), `draft-state.js` value contracts plus E2E value pins
  (CurrencyRemeasurement 5 selects + 1 ISO date input), raw-value
  `InputValueAsync()` contracts (Microsoft365Setup 4), and server-managed
  autosave interop (Workpaper 2).
- **5 native `<table>` elements remain** (`Journals`, `InvoiceDetail`,
  `FinancialPackage`, `Consolidation`, `PbcRequests`), each kept for `<tfoot>`
  totals, `colspan` row structures or `sr-only` captions that `MudTable` cannot
  express.
- **MudBlazor is referenced only by
  `src/AuditSphereOps.Web/AuditSphereOps.Web.csproj`** (centrally versioned
  9.10.0 in `Directory.Packages.props`); a source scan finds no reference in
  Domain, Application, Infrastructure, Worker or the test projects, and
  `ArchitectureGuardTests` enforces the project-reference direction.
- Every page's primary UI system is MudBlazor (route-selected staff/client/public
  layout +
  `MudThemeProvider`, headings via `PageHeader`, notices via `MudAlert`,
  containers via `MudPaper`, tables via `MudTable` where semantic, controls via
  MudBlazor inputs, statuses via `StatusChip`, loading via `LoadingState`).

## 9. Prototype-inspired visual modernization

Visual reference: `auditsphere-visual-prototype` source checkout at
`dade5e02c1a36596fd6596878e99a25c78f955ab`, with CSS loaded in its
`roles.css` → `styles.css` → `src/host.css` → `src/enterprise.css` order.
The target checkout started this visual slice at
`71fe1d4a806199287e02cbff131b2b6b42aa7c89` on `master`.
The base prototype shell declares a 232px sidebar and 73px top bar; its
earlier `roles.css` overrides the sidebar to 242px at desktop size. The real
app uses a 232px staff drawer, 73px desktop top bar and the owner's requested
navy/blue/teal palette. This is a visual reference, not a behavior source.

The initial implemented slice changes `MainLayout`, `ClientLayout`,
`PublicLayout`, `AuditSphereTheme`, `PageHeader`, `StatusChip`, the shared
CSS layer and the Portfolio presentation. `Routes.razor` selects a client-safe
layout for `/portal` routes and a navigation-free public layout for `/`,
`/auth/*` and `/setup/*`; `/app/administration/microsoft365` still uses
the staff layout. Existing accounting navigation and Portfolio data queries,
scope checks, CSV export, route links and error states remain intact.

| Affordance | Classification | Current disposition |
| --- | --- | --- |
| Navy navigation, compact headings, panels, metrics and table styling | `PRESENTATION_ONLY` | Shared CSS/theme and Portfolio pilot implemented; remaining page families need visual review. |
| Portfolio refresh, scoped search/export, release/package links | `WIRE_EXISTING` | Kept connected to existing Blazor actions and scoped PostgreSQL data. |
| Prototype global record search | `WIRE_EXISTING` + new Application query | Staff search over clients, engagements, PBC requests, leads, invoices and pages with per-hit reauthorization. |
| Prototype consolidated work queues and lifecycle widgets | `BACKEND_GAP` | No real equivalent exposed by the current shell; do not add decorative controls or local state. |
| SharePoint-backed workspace effects before exact capability verification | `EXTERNAL_BLOCKED` | Existing Portfolio warning remains visible. |
| Persona switching, scenario loading, simulated notifications and browser-local business store | `OUT_OF_SCOPE` | Never copied from the prototype. |

Next visual passes need to cover full staff page families and client detail
views, with authorized seeded browser evidence for route-specific controls,
long content, dialogs, stale/error states and keyboard operation. This section
does not claim parity or whole-application acceptance.

| Prototype visual family | Real route family | Visual pass |
| --- | --- | --- |
| Practice overview and dashboard | `/app`, `/app/overview` | Portfolio pilot complete; route-specific polish remains. |
| Practice, client and engagement work | `/app/practice/*`, `/app/clients/*`, `/app/engagements/*` | Visual pass on leads, time, client detail, engagement detail and proposal detail (breadcrumb, record toolbar, source-backed counts, shared panels). |
| Economics and billing | `/app/finance`, invoice details and practice time | Visual pass on firm finance, time and invoice detail (numeric table alignment moved from inline styles to shared classes). |
| Accounting and group workbenches | `/app/accounting/*`, `/app/consolidation/*` | Visual pass on every current accounting and group route, including package detail and currency remeasurement; remeasurement native selects no longer widen the page. |
| Audit, review, completion and records | `/app/audit/*`, `/app/assessments/*`, `/app/reviews/*`, `/app/findings/*`, `/app/completion/*`, `/app/releases/*`, `/app/records/*` | Partial: selected audit and detail routes, assessment, release and archive passed; other routes remain. |
| Firm administration and Microsoft 365 | `/app/administration*`, `/setup/microsoft365` | Partial: overview and tab workbench, task progress, protected setup and tenant-connection presentation passed; dialogs and remaining state/contrast acceptance still require a full route-family audit. |
| Client secure portal | `/portal*` | Client-safe shell and the three current portal views have a local visual pass; deeper state and keyboard coverage remains. |

Staff navigation follows the reference grouping (Practice & CRM, Economics & billing, Accounting workbench,
Audit & assurance, Administration) with only real destinations; the audit program library and currency
remeasurement gained entries. Root links (`/app`, `/app/accounting`, `/app/administration`) match exactly, so a
descendant never shows two active entries, and detail routes mark their owning section with a quieter
`audit-nav-section-current` cue (invoices → Finance, proposals → Leads, client/engagement/assessment → Portfolio,
audit detail routes → Audit & assurance, package detail → Package reviews). This is presentation only and never an
authorization grant. `ResponsiveShellSweepTests` (AS-UI-RESPONSIVE-SWEEP-01) checks every parameterless staff route
and ten seeded detail routes at 320/390/760/1024/1440/1920px for page-level overflow, a single exact-active entry
and the owning section.

| Story | Disposition in this checkout |
| --- | --- |
| UX-001 route inventory | Route families above; `RouteRenderSmokeTests` and the responsive sweep enumerate the parameterless routes. |
| UX-002/003 palette, shell | Implemented; grouped navigation, exact root matching and detail-route sections added. |
| UX-004/005 context, headers | Breadcrumb + `PageHeader` eyebrow on every staff detail route; scope clearing is covered by the AS-PAR-002 route-change suites. |
| UX-006–UX-028 page families | Visual pass on every current staff route family and the three portal views; see rows above. |
| UX-029 global search and tours | Scoped staff search implemented (see story checklist); guided tours remain out of scope. |
| UX-030 widths | Automated 320–1920px reflow sweep; 200%/400% zoom and manual keyboard walkthrough remain manual review items. |
| UX-031 acceptance | Local evidence only; independent visual review and before/after reference captures remain open. |

### 9.1 Route inventory (generated from `@page` declarations)

51 route declarations exist in this checkout. `READY_FOR_REVIEW` means a local visual pass with preserved
functional tests; no route is `ACCEPTED` until independent visual review. Evidence names the E2E test class that
exercises the route's behavior; it is not a visual-parity claim.

| Route | Component | Audience | Classification | UX status | Evidence (E2E class) | Responsive sweep |
| --- | --- | --- | --- | --- | --- | --- |
| `/app/accounting/differences` | `AccountingRecords` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | RouteRenderSmoke width matrix; FinancialArtifact tabs | Yes |
| `/app/accounting/evidence` | `AccountingEvidence` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | FinancialArtifact; ClientScope queue | Yes |
| `/app/accounting/journals/{JournalId:guid}` | `Journals` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | FinancialArtifact journal route | Yes |
| `/app/accounting/journals` | `AccountingRecords` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | RouteRenderSmoke width matrix; FinancialArtifact tabs | Yes |
| `/app/accounting/mappings/{MappingId:guid}` | `Mapping` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | ClientScope mapping route | Yes |
| `/app/accounting/mappings` | `AccountingRecords` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | RouteRenderSmoke width matrix; FinancialArtifact tabs | Yes |
| `/app/accounting/packages/{PackageId:guid}` | `FinancialPackage` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | FinancialArtifact package; SiblingClientIsolation | Yes |
| `/app/accounting/periods/{PeriodId:guid}` | `AccountingPeriod` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | PeriodWorkbenchScope | Yes |
| `/app/accounting/remeasurement` | `CurrencyRemeasurement` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | FinancialArtifact draft restore | Yes |
| `/app/accounting/restatements` | `PeriodRestatements` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | RouteRenderSmoke | Yes |
| `/app/accounting/reviews` | `FinancialPackageReviews` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | RouteRenderSmoke width matrix | Yes |
| `/app/accounting/rollforward` | `PeriodRollforward` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | ClientScope roll-forward | Yes |
| `/app/accounting` | `AccountingWorkspace` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | ClientScope workspace | Yes |
| `/app/administration/microsoft365/tenant-connection` | `TenantConnection` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | TenantAdministrationJourney | Yes |
| `/app/administration/microsoft365` (alias) | `Microsoft365Setup` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | M365SetupJourney | Page-specific or not yet |
| `/app/administration/project-progress` | `ProjectProgress` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | RouteRenderSmoke | Yes |
| `/app/administration` | `Administration` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | TenantAdministrationJourney; ClientScope admin | Yes |
| `/app/assessments/{Id:guid}/decision` | `AssessmentDecision` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | SiblingClientIsolation assessment; ClientScope decision | Page-specific or not yet |
| `/app/assessments/{Id:guid}` | `AssessmentDetail` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | SiblingClientIsolation assessment; ClientScope assess | Page-specific or not yet |
| `/app/audit/library` | `AuditProgramLibraryPage` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | AuditProgramLibraryScope | Yes |
| `/app/audit/plans/{Id:guid}` | `AuditPlan` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | AuditAndRelease plan | Page-specific or not yet |
| `/app/audit/populations/{Id:guid}` | `AuditPopulation` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | AuditAndRelease population | Page-specific or not yet |
| `/app/audit/workpapers/{Id:guid}` | `Workpaper` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | AuditAndRelease workpaper | Page-specific or not yet |
| `/app/clients/{ClientId:guid}/assessment` | `AssessmentDetail` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | SiblingClientIsolation assessment; ClientScope assess | Page-specific or not yet |
| `/app/clients/{ClientId:guid}` | `ClientDetail` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | ClientScope client profile | Yes |
| `/app/completion/{Id:guid}` | `Completion` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | AuditAndRelease completion | Page-specific or not yet |
| `/app/consolidation/advanced/{ScopeId:guid}` | `AdvancedConsolidationWorkflow` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | ClientScope advanced (widths, draft, focus) | Page-specific or not yet |
| `/app/consolidation` | `Consolidation` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | SiblingClientIsolation group; ClientScope advanced | Yes |
| `/app/engagements/{EngagementId:guid}/audit-fieldwork` | `AuditFieldwork` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | AuditAndRelease fieldwork | Yes |
| `/app/engagements/{EngagementId:guid}/statements` | `StatementDrillDown` | Staff (scoped) | WIRE_EXISTING | READY_FOR_REVIEW | FieldworkConnectionsJourney; FieldworkConnections (Domain) | Not yet |
| `/app/engagements/{EngagementId:guid}/tb-intake` | `TrialBalanceIntake` | Staff (scoped, accounting preparer) | WIRE_EXISTING | READY_FOR_REVIEW | FieldworkConnectionsJourney; FieldworkConnections (Domain) | Not yet |
| `/app/engagements/{EngagementId:guid}/audit-plan` | `AuditPlan` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | AuditAndRelease plan | Yes |
| `/app/engagements/{EngagementId:guid}/completion` | `Completion` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | AuditAndRelease completion | Yes |
| `/app/engagements/{EngagementId:guid}/pbc` | `PbcRequests` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | PbcUploadJourney; ClientScope PBC | Yes |
| `/app/engagements/{EngagementId:guid}` | `EngagementDetail` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | ClientScope engagement | Yes |
| `/app/finance` | `Finance` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | PracticeBillingLedger; ClientScope firm read | Yes |
| `/app/findings/{Id:guid}` | `Finding` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | AuditAndRelease finding | Page-specific or not yet |
| `/app/operations` | `Operations` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | ClientScope operations revoke | Yes |
| `/app/overview` (alias) | `Portfolio` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | RouteRenderSmoke; ClientScope portfolio export | Page-specific or not yet |
| `/app/practice/invoices/{InvoiceId:guid}` | `InvoiceDetail` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | PracticeBillingLedger | Page-specific or not yet |
| `/app/practice/leads` | `Leads` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | PracticeBillingLedger | Yes |
| `/app/practice/commercial-settings` | `CommercialSettings` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | CommercialJourney; CommercialWorkflow (Domain) | Not yet |
| `/app/practice/resources` | `ResourcePlanning` | Staff (Partner/Manager/Administrator) | WIRE_EXISTING | READY_FOR_REVIEW | PlanningAndResourcesJourney; PlanningResourcesAndMateriality (Domain) | Not yet |
| `/app/practice/analytics` | `PracticeAnalytics` | Staff (Partner/Administrator/FinanceManager, firm-wide) | WIRE_EXISTING | READY_FOR_REVIEW | FirmOperationsJourney; FirmOperations (Domain) | Not yet |
| `/app/finance/books` | `FirmBooks` | Staff (finance roles) | WIRE_EXISTING | READY_FOR_REVIEW | FirmOperationsJourney; FirmOperations (Domain) | Not yet |
| `/app/library`, `/app/library/{DocumentId:guid}` | `TechnicalLibrary` | Staff (audience-scoped) | WIRE_EXISTING | READY_FOR_REVIEW | FirmOperationsJourney; FirmOperations (Domain) | Not yet |
| `/app/practice/proposals/{Id:guid}` | `ProposalDetail` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | PracticeBillingLedger | Page-specific or not yet |
| `/app/practice/time` | `PracticeTime` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | ClientScope time scope | Yes |
| `/app/records/archives/{Id:guid}` | `RecordsArchive` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | ClientScope archive route | Page-specific or not yet |
| `/app/releases/{CandidateId:guid}` | `Release` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | AuditAndRelease release | Page-specific or not yet |
| `/app/reviews/{Id:guid}` | `ReviewPoint` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | ClientScope review point | Page-specific or not yet |
| `/app` | `Portfolio` | Staff (scoped) | PRESENTATION_ONLY + WIRE_EXISTING | READY_FOR_REVIEW | RouteRenderSmoke; ClientScope portfolio export | Yes |
| `/auth/access-not-assigned` | `AccessNotAssigned` | Public/auth (public layout) | PRESENTATION_ONLY | READY_FOR_REVIEW | RouteRenderSmoke | Page-specific or not yet |
| `/portal/accounting/packages/{PackageId:guid}` | `ClientFinancialPackage` | Client (portal layout) | PRESENTATION_ONLY | READY_FOR_REVIEW | FinancialArtifact client package | Page-specific or not yet |
| `/portal/requests/{RequestId:guid}` | `ClientPbcRequest` | Client (portal layout) | PRESENTATION_ONLY | READY_FOR_REVIEW | PbcUploadJourney; ClientScope client PBC | Page-specific or not yet |
| `/portal` | `ClientPortal` | Client (portal layout) | PRESENTATION_ONLY | READY_FOR_REVIEW | RouteRenderSmoke portal widths; SiblingClientIsolation portal | Page-specific or not yet |
| `/setup/microsoft365` | `Microsoft365Setup` | Public/auth (public layout) | PRESENTATION_ONLY | READY_FOR_REVIEW | M365SetupJourney | Page-specific or not yet |
| `/` | `Home` | Public/auth (public layout) | PRESENTATION_ONLY | READY_FOR_REVIEW | RouteRenderSmoke | Page-specific or not yet |

### 9.2 Story checklist

| Story | Status | Notes |
| --- | --- | --- |
| UX-001 Baseline inventory | READY_FOR_REVIEW | Generated route inventory above; reference captures of the prototype remain manual. |
| UX-002 Tokens/typography | READY_FOR_REVIEW | Theme + `enterprise-ui.css`; the remote Roboto font link was removed (system fonts only). |
| UX-003 Staff shell | READY_FOR_REVIEW | Grouped navigation, exact root matching, owning-section cue; responsive sweep. |
| UX-004 Context/navigation | READY_FOR_REVIEW | Breadcrumbs on detail routes; same-document clearing covered by AS-PAR-002 suites. |
| UX-005 Page headers/actions | READY_FOR_REVIEW | `PageHeader` eyebrow/description on every staff route. |
| UX-006–UX-011 Registers, detail, lifecycle, rework, forms | READY_FOR_REVIEW | Shared panels/registers; native draft contracts preserved. |
| UX-012 Dialogs/confirmations | READY_FOR_REVIEW | `ConfirmDialog` closes once per confirm (no double submit) and now shows an adjacent message when a required reason is missing; the revoke-access journey asserts nothing is revoked in that case. Focus trap/restore relies on MudBlazor and still needs a manual keyboard review. |
| UX-013 Feedback/reconnect | READY_FOR_REVIEW | `App.razor` hosts a custom `components-reconnect-modal` panel (styled in `enterprise-ui.css`, retry/reload in `reconnect-state.js`) stating that an action started before the drop is unconfirmed until its result shows; AS-UI-RECONNECT-STATE-01 drops the live circuit socket and asserts the panel appears and clears. |
| UX-014–UX-028 Page families | READY_FOR_REVIEW | Every route above has a visual pass. |
| UX-029 Bounded search/help | READY_FOR_REVIEW | `GlobalSearchQuery` + `GlobalSearch.razor`: clients, engagements, PBC requests, leads, invoices and pages, each hit re-authorized with its destination route's decision; coverage statement always shown; `/` shortcut only outside editable controls and dialogs; stale responses dropped and results cleared on navigation. Documents, evidence and emails remain out of scope; contextual module help is not added. |
| UX-030 Responsive/accessibility | IN_PROGRESS | Automated 320–1920px sweep passes; 200%/400% zoom, contrast audit and manual keyboard walkthrough remain. |
| UX-031 Visual/functional acceptance | IN_PROGRESS | Local evidence only; reference captures and independent review remain. |

### 9.3 Intentional differences

No persona switcher, simulated superuser, scenario loader, fake notifications, browser-local business store,
seeded charts, portal payments/signing/proposal acceptance, or document/email search. Staff search covers only record types it can re-authorize. Target-native routes and
raw-value native controls (draft and upload contracts) are retained. Provider status stays truthful
(`EXTERNAL_BLOCKED` where unverified). Narrow layouts stack content and scroll wide tables locally.

This map groups actual route prefixes, including parameterized detail routes;
it does not create links to prototype-only modules or treat prototype widgets
as implemented capabilities.

The client portal visual pass applies the navy page header to the list,
request-detail and financial-package views, uses compact white panels and a
timeline treatment, and stacks definition-list details on narrow screens.
The request file input is constrained to its card at 320px without changing
its native `FileList` API, `data-draft-scope` boundary, `data-draft-field`
attributes or upload transport. The client package decision is shown with
the shared semantic `StatusChip`; recording the decision is still the same
server-side command. The portal's duplicated scope note was removed because
the same restriction remains in its page description. No prototype-only
invoices, payments, proposal responses or local document registry were added.
