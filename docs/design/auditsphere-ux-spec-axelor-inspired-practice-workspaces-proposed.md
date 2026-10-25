# AuditSphere — Axelor-Inspired Practice UX Implementation Specification

**CRM, engagement planning, billing, and firm financial management**

| Attribute | Specification |
|---|---|
| Status | **PROPOSED — implementation contract for review; not a claim of implemented or verified functionality** |
| Prepared | 25 October 2026 |
| Target repository | `nirzaf/AuditSphere` |
| Inspected target commit | `89fa2f22706a630b7c9d923fc8e4878100dcb0e2` |
| Reference repository | `axelor/axelor-open-suite` |
| Inspected reference commit | `e7cbea53bae88d2540f59a70b09bf0f326f1022e` — master release 9.1.10 |
| Reference documentation | Official Axelor documentation pages identifying themselves as version 8.5; used for interaction patterns, not version parity |
| Architecture | Existing ASP.NET Core / C# / Angular Material-CDK / EF Core / PostgreSQL modular monolith and existing workers |
| Product boundary | Improvements within the existing Commercial/CRM, Governance/Planning, and Practice/Finance capabilities; no additional product module |
| Integration boundary | **No change to Microsoft Entra, SharePoint, email-provider behavior, or release/archive functionality** |
| Review evidence | Targeted source, contract, template, policy, and test-body inspection; no application execution, browser acceptance, database migration, or live-provider test |
| Deliverable | A code-grounded UX and implementation specification, not a fork or integration of Axelor |

## Contents

1. [Decision and success criteria](#s01)
2. [Evidence and current-state assessment](#s02)
3. [Non-regression contract](#s03)
4. [Axelor reference translation](#s04)
5. [Information architecture and shared interaction rules](#s05)
6. [CRM and commercial pipeline](#s06)
7. [Engagement planning and resources](#s07)
8. [Billing and collections](#s08)
9. [Financial reporting and practice analytics](#s09)
10. [Authorization, precision, and recovery](#s10)
11. [Backend, API, frontend, and database changes](#s11)
12. [Implementation stories](#s12)
13. [Acceptance scenarios](#s13)
14. [Verification strategy](#s14)
15. [Rollout and rollback](#s15)
16. [Deferred changes and decision boundaries](#s16)
17. [Agent execution contract](#s17)
18. [Source register and limitations](#s18)

<a id="s01"></a>
## 1. Decision and success criteria

### 1.1 Decision

Use Axelor as a reference for organizing business work: navigable pipelines, contextual record pages, multiple views over the same tasks, clearly separated planning measures, and financial drill-down. Implement the resulting designs natively in AuditSphere, using its existing components, domain services, permissions, and financial definitions.

**This is a presentation and read-discovery enhancement, not a business-process replacement.** New filters, read-only projections, grouped views, and clearer navigation are intentional UX changes. Existing mutations must retain their prerequisites, payload meaning, approval rules, transaction boundaries, idempotency, and side effects. Identical visual layout is not required; equivalent business behavior is.

Do not install Axelor, integrate with its database, reproduce its XML view engine, copy its Java/JavaScript/XML implementation, import its branding/screenshots, or introduce an ERP dependency. Reference behavior is not authority to change AuditSphere. Original implementation is the default; any proposed reuse of third-party code or assets requires a separate licensing decision.

### 1.2 Four bounded work areas

| Work area | Intended outcome | Existing owners |
|---|---|---|
| Commercial workspace | Understand a lead, related opportunities, proposal revisions, and the next permitted commercial action without conflating a sale with audit activation | Existing CRM, quotation/proposal, conversion, acceptance, and fee handoffs |
| Engagement planning | See work by engagement, phase, task group, owner, due date, and dependency; inspect staff capacity without creating another task engine | Existing tasks, workflow templates, resource planning, staffing, budgets, and time |
| Billing workspace | Find firm invoices, inspect balances, and navigate payments, allocations, terms, and review without shortening protected financial workflows | Existing billing accounts, invoices, fee agreements, receipts, credits, and command receipts |
| Finance and analytics | Distinguish operational economics from posted firm accounting, with reliable scope labels and source drill-down | Existing receivables, unbilled-time, firm books, ledger, and practice analytics queries |

### 1.3 Testable success criteria

The implementation is accepted only when all applicable criteria below are demonstrated on the reviewed release candidate:

- Every existing action and deep link within a changed screen has a recorded equivalent destination and unchanged server authority; no hidden loss of functionality.
- Filtered summaries either represent the complete authorized query cohort or clearly identify themselves as page-only/current-view observations. A loaded-page count must not look like a complete pipeline count.
- Every protected mutation still uses its existing command and recovery contract. A new card, table, board, or drawer cannot approve, post, allocate, activate, sign, or release on its own.
- Existing business totals and financial calculations are unchanged for the same fixtures. Any evidenced financial defect discovered during this work is isolated into a separately reviewed correction, not silently repaired through a presentation change.
- New views remain bounded, accessible, and usable after direct navigation, refresh, concurrent edits, permission revocation, and lost responses.
- SharePoint/Entra configuration, automated provisioning, staff synchronization, document custody, and provider traffic remain unaffected by the UX work.

No percentage-of-completion target or productivity claim is assigned before measuring the baseline. Navigation effort, task completion time, and report comprehension can be measured during pilot acceptance; they are not substitutes for correctness.

<a id="s02"></a>
## 2. Evidence and current-state assessment

### 2.1 Evidence vocabulary

**Source-observed** means executable code was inspected; it does not mean the behavior was run successfully. **Partial** means relevant foundations exist, but the proposed end-to-end presentation is not established. **Unverified** means this review did not establish the capability or its absence. **Proposed** identifies a new requirement in this document. A file or test body is never evidence of current runtime success by itself.

GitHub search can lag behind master. The pinned file reads in the source register control this review. Before implementation, compare the current working commit with the inspected commit and refresh only the affected evidence. Do not overwrite concurrent work on the assumption that this document describes the latest code forever.

### 2.2 Findings from inspected AuditSphere source

| Area | Observed current state | Disposition for this specification |
|---|---|---|
| Lead workbench | `leads.ts` has paged search, lead creation, canonical channel selection, qualification, and uncertain-request recovery. Its “Awaiting qualification” card counts `NEW` entries in `items`, while the adjacent search total uses the complete `total`. [AS04] | **Source-observed count-scope discrepancy.** Add a complete server count or explicitly label a page-only value. Do not remove recovery or qualification. |
| Lead query | `PracticeLeadQuery.PageAsync` authorizes firm-wide commercial access, applies name search, counts, pages, and rechecks authority. [AS05] | **Source-observed foundation.** Extend filters and aggregates without widening role scope or fetching all lead details into the browser. |
| Opportunities and history | `opportunities.ts` exposes opportunities under one lead, separate opportunity/history pagination, exact proposal revisions, and known gaps in legacy activity history. [AS06] | **Partial relative to cross-lead pipeline UX.** Reuse the detail workspace; a global pipeline read is proposed only after checking for an equivalent owner at implementation time. |
| CRM domain | `Crm.cs` defines canonical lead/opportunity/proposal states; `Opportunity` already has nullable `Probability`, `OwnerUserId`, and `NextAction`. [AS07] | **Entity-present; complete editing/units/validation unverified here.** Do not invent duplicate fields or introduce weighted forecasting in the core scope. |
| Task workspace | `work-queue.ts` already supports list/board/calendar, complete default-view counts, task groups, evidence/dependency state, cancellation/reopening affordances, and a separate procedure review inbox. [AS08] | **Source-observed foundation.** Improve hierarchy and context; do not build a replacement task board. |
| Resource planning | `resources.ts/html` already presents weekly capacity, unavailability, planned hours, approved actual/billable hours, skills, certificates, and reviewed planning commands. [AS09], [AS10] | **Source-observed foundation.** Improve filtering, inspection, and navigation; retain current scheduling semantics and review. |
| Templates | The code map and inspected `EngagementWorkflowPlanTests` show published revision pinning, schedule preview, independent review, request recovery, and dependency gates. [AS03], [AS21] | **Source and test-body evidence, not executed.** Present the existing lifecycle more clearly. |
| Invoice workspace | `invoice.ts` already separates totals, credits, allocations, receipts, payment terms, reversals, and allowed actions. It explicitly labels `SENT` as staff-recorded, without delivery evidence. [AS11] | **Source-observed foundation.** Reorganize into focused sections; preserve the separate facts and action safeguards. |
| Unbilled time | `FirmUnbilledTimeQuery` already classifies approved time and source reservations, with complete bounded subtotals and a notice that standard value is not recognized WIP, revenue, debt, or billing authorization. [AS13] | **Implemented in inspected source; runtime unverified.** Reuse it instead of proposing a new WIP engine. |
| Receivables | `FirmReceivablesAgingQuery` already uses posted firm invoices, approved terms, allocations, credits, and reviewed reversals at a UTC as-of cutoff. [AS14] | **Source-observed foundation.** Preserve its date and bucket policy and improve discovery/drill-down. |
| Firm finance entry | `/app/finance` currently hosts firm ledger operations, account/period setup, journals, and close/reopen flows. [AS15], [AS12] | **Source-observed route contract.** Do not replace it silently with an invoice dashboard. |
| Practice analytics | `analytics.ts` displays a wide economics table plus utilization, workload, review turnaround, client-wait, and phase/task summaries. [AS16] | **Source-observed presentation.** Group the information and expose metric bases without inventing new calculations. |
| Financial definitions | The current query computes realization as billed/standard value and `Profit` as billed minus recorded staff cost; contract contribution and firm ledger profit are separate. [AS17], [AS18] | **Source-observed semantics.** Improve labels, not formulas. |
| SharePoint storage work | The pinned latest commit adopts functionality-preserved storage scope and explicitly excludes manual site management, membership handover, worker retirement, and delta synchronization. [AS00] | **Outside this UX change.** Do not revive the earlier superseded design. |

The absence of a feature from an inspected slice is not proof of repository-wide absence. Stories begin by finding the existing owner, not by creating a similarly named parallel implementation.

<a id="s03"></a>
## 3. Non-regression contract

### 3.1 Business invariants that must remain unchanged

| Protected behavior | Mandatory preservation rule |
|---|---|
| Client acceptance | Commercial acceptance, professional risk approval, agreed terms, advance-payment allocation, and engagement activation remain separate records and gates. A pipeline “Won” label is not engagement activation. |
| Fee policy | Preserve approved 50/50 fee milestones, current agreement lineage, and unconditional fully paid/allocated advance eligibility. No new fee plan, discount rule, or payment waiver is introduced. |
| Invoice automation | Existing optional automation stays off unless already enabled by its own policy. It creates eligible drafts only; it never approves, posts, sends, or collects. [AS20] |
| Independent decisions | Preparers cannot acquire reviewer/Partner/FinanceReviewer powers through a new workspace. Staffed-role eligibility and current grants remain authoritative. |
| Task and lifecycle separation | Completing or moving an operational task never directly advances an audit lifecycle state, approves evidence, or signs a report. |
| Financial domain separation | Firm invoices and books, client bookkeeping, and group consolidation remain distinct. A “client” filter in firm finance does not switch to the client's own ledger. |
| Financial arithmetic | Preserve decimal precision, rounding, allocation rules, dates, report definitions, and null/unavailable handling. No financial arithmetic using JavaScript floating-point amounts. |
| Evidence | Preserve original bytes, exact revisions/hashes, accepted sources, immutable approvals, posted facts, source lineage, and corrections/reversals. |
| Recovery | Preserve request IDs, payload hashes, expected revisions, reviewed previews, receipts, and unknown-outcome recovery. No automatic mutation retries. |
| External effects | Opening/filtering a screen must not send mail, provision a site, grant access, post an invoice, or start cleanup. |

### 3.2 Microsoft 365 and document non-goals

No changes to sign-in identity binding, cookie/session behavior, Graph permissions, app registrations, tenant consent, certificate mounts, dedicated-site rollout boundaries, automated client-site creation, assigned-staff Full Control synchronization, folder templates, PBC upload/review semantics, document limits, existing storage disposition, or final archive authority.

No manual-site mode, delta worker, replacement storage provider, modified upload/download protocol, or retirement of the privileged `client-sites` worker. A redesigned page may link to an existing authorized document action; it must not implement a second provider path. SharePoint files and archive contents must remain untouched by UX tests unless an existing explicitly authorized synthetic provider test is being run.

### 3.3 Route, action, and data parity

Before changing a screen, create a small parity inventory identifying each current route, query parameter, button/action, role gate, validation, recovery step, upload/download, and related record link. Map it to its proposed location. Existing URLs and HTTP write contracts remain stable. Query additions must be optional and backward-compatible.

Moving a control into a tab is allowed. Removing an action because it appears rarely used is not. A hidden panel is not a security control: sensitive data must be excluded by the server when unauthorized. Keep old deep links useful after direct reload; use route aliases only when unavoidable and explicitly tested.

No automatic user, role, status, financial-record, or template migration is needed for layout improvements. Existing feature switches retain their current values. This document does not authorize production deployment, merges, changes to tenant grants, or destructive operations.

<a id="s04"></a>
## 4. Axelor reference translation

Axelor is evidence for interaction patterns, not for AuditSphere's domain rules. Its official docs and source contain navigable opportunity views, grouped project screens, task views, and contextual invoice sections. [AX01], [AX02], [AX03], [AX04], [AX05], [AX06]

| Reference pattern | AuditSphere adaptation | Deliberately not adopted |
|---|---|---|
| Opportunity grids organized by stage, customer, owner, and amount | A filterable, read-only pipeline over existing opportunities, with currency-separated totals and links to the exact proposal | Editable custom stages, arbitrary “Next stage,” automatic “Won,” or closing other opportunities |
| Customer-linked commercial record with history and quotations | One opportunity context joining existing proposal revisions, communication evidence, and conversion references | New contact/CRM database, generic activity engine, or automatic client merge |
| Project overview with contextual task/planning links | Engagement context header and navigation to existing tasks, resources, time, budgets, and gates | A new project entity, arbitrary nested engagements, or a generic project “Finish” command |
| Task tree, board, and calendar | Engagement → workflow phase → existing task group → existing task; preserve current alternate views | A second task engine, new dependency semantics, automatic critical-path scheduling, or guessed start/end dates |
| Planning views near actual work | Weekly capacity and allocation inspection beside approved actual/billable hours | Equipment/room booking, payroll, or automatic staffing/permission changes |
| Invoice header, lines, terms, and related records grouped clearly | Focused invoice sections with amount summary and separately authorized commands | Axelor's accounting treatment, invoice status names, tax defaults, subscription billing, or one-click financial approval |
| Business reporting near operational records | Read-only economics and firm-report workspaces with exact source drill-down | Importing ERP chart-of-accounts rules, recognizing WIP/revenue, or cross-currency totals without approved FX |

**Terminology:** Axelor's “partner” can mean a customer/third party. In AuditSphere, “Partner” is an audit-firm professional authority. Use **Client**, **Contact**, and **Engagement Partner** precisely; do not import ambiguous labels.

The two image-cache links supplied in the request could not be verified in this review. The design therefore relies on official documentation and inspected source rather than attributing features to an unverified screenshot. No screenshot or Axelor visual asset needs to ship with AuditSphere.

<a id="s05"></a>
## 5. Information architecture and shared interaction rules

### UX-COMMON-01 — Keep existing navigation destinations

Improve the current navigation within existing sections. Use the existing route catalogue and guards as the authority. A new child route is allowed only for a genuinely new read-only workspace with no suitable current route.

| Existing area | Intended organization | Route rule |
|---|---|---|
| Commercial | Leads; Pipeline; existing opportunity/proposal/quotation detail | Keep `/app/practice/leads` and its existing detail links. A pipeline child route is proposed, not observed. |
| Work | My Work; Team/exception views; existing template management | Keep `/app/work`, current query/deep-link handling, and existing template routes. |
| Engagement | Existing overview, workplan, planning/staffing, time/budget and professional gates | Add contextual links/tabs without replacing the engagement shell or its lifecycle projection. |
| Resources | Existing resource grid and editors | Keep the current resource route and 1–12-week contract. |
| Finance | Existing firm ledger; billing discovery; fee settlements; receivables; books | `/app/finance` remains the ledger entry. Do not silently repurpose it. |
| Practice analytics | Existing economics and operational performance | Preserve the current route, authorization, metric definitions, and default query behavior. |

`/app/...` is the Angular route notation used in the inspected components. The application is hosted beneath its established `/ui` route boundary; avoid inventing a second base URL. Update `app.routes.ts`, server SPA ownership, navigation, and direct-refresh tests together when an additive route is approved. [AS02]

### UX-COMMON-02 — Standard workspace anatomy

Use a consistent hierarchy, implemented with existing Angular Material/CDK and shared components:

| Region | Contents | Behavior |
|---|---|---|
| Context header | Title; client/engagement or firm context; period where applicable; source status | Context identifiers must agree with loaded data, not just route text. |
| Primary action | At most one visually primary creation/workflow action; secondary actions nearby | Server capability controls availability; no inferred finance/audit privilege. |
| Filter bar | Search, relevant status, owner, period/date and currency controls; clear/reset | Explicitly identify which results and summaries each filter affects. |
| Summary strip | Small set of actionable counts or amounts with basis/scope labels | Link to the same authorized underlying cohort; never page-derived global totals. |
| Main view | Table, existing board/calendar, grouped task list, or report | Bounded paging; stable IDs; clear sorting and partial-window labels. |
| Context detail | Selected record details and links to the authoritative full workspace | Optional drawer/inline panel; no duplicated command state or different business logic. |
| Action/recovery panel | Existing preview, exact input review, assent, pending result and receipt lookup | Remains visible until resolved; switching a tab cannot discard an uncertain command. |

Do not build a configurable dashboard framework. Extract a shared UI component only when two changed screens genuinely need the same behavior. Reuse `audit-page-header`, `audit-state`, `audit-command-message`, `audit-status`, `Api.resource`, and existing draft/recovery helpers. [AS02]

### UX-COMMON-03 — Density, responsiveness, and accessibility

Use existing spacing, typography, colors, borders, and focus tokens from `styles.scss`; no rebranding or new palette. Keep main task actions easy to scan, with progressive disclosure for long histories. Compact tables still need readable values and clearly associated headings.

At narrow widths, maintain a readable record summary and a deliberate horizontal-scroll region for genuinely tabular content. Do not truncate monetary values, hide a blocking state, or discard columns that establish scope. Provide a textual equivalent for any visual utilization bar or chart. Color never conveys status alone.

Tabs, filters, buttons, expanders, dialogs, and table regions must work with a keyboard. Preserve focus on refresh and return it to the initiating control after a closed detail panel, unless the user deliberately moved focus. Respect session invalidation and reduced motion. Use semantic table headings, accessible names, status announcements, and explicit empty/denied/stale states. Target the repository's existing accessibility standard; do not claim certification from an automated check alone.

### UX-COMMON-04 — Filter and summary integrity

A summary must declare: **what is counted**, **which records are included**, **currency where relevant**, **date/basis**, and **whether the count is complete**. Distinguish lead counts from opportunity counts and invoice totals from receipt availability.

For new projections, return rows and their matching aggregates from one consistent authorized observation where practical. Use a short read transaction/snapshot following established owner patterns; reauthorize after the read before returning sensitive data. A continuation token grants no authority and must be invalidated or rejected when its query context changes.

Preserve intentionally different current scopes. For example, the existing My Work overview describes default authorized queues that can overlap, while assignment filters affect its list. Keep that explanation; do not silently redefine those counts. A future unified filtered overview would be a separately reviewed read-contract change.

Do not persist business datasets, approval state, tokens, or reviewed assent in browser preferences. Navigation/filter state may use allowlisted query parameters. Avoid customer names, contact emails, document URLs, and free-text evidence in durable browser storage or telemetry.

<a id="s06"></a>
## 6. CRM and commercial pipeline

### CRM-01 — Lead workbench

Retain lead creation, qualification, canonical channels, and request recovery. Reorganize the screen into a compact search/list workspace with creation in an explicit panel or dialog; do not leave a large blank creation form as the only focal point once records exist.

Add server-supported optional filters for **lead status**, **owner**, and **source channel**, alongside current name search. A “My leads” shortcut is a filter for an already authorized commercial user; it does not authorize a user who lacks current firm-wide commercial access. Include Unassigned separately from a missing/disabled owner. Historical owners must not be silently replaced.

Recommended table columns: lead name, channel, primary contact, owner, lead status, creation date, and permitted next action. An optional last-recorded-activity field may use actual retained events; it must not be labeled “last contacted” unless a real contact event supports that statement. Preserve accessible paging and existing activity history.

Summary cards should initially be **Matching leads**, **New / awaiting qualification**, and **Qualified**, each counted over the same filtered cohort. Additional statuses belong in the filter rather than an excessive row of cards. The `NEW` summary must not be calculated from `items` unless labeled “on this page.” Update the backend projection and frontend decoder together. [AS04], [AS05]

No lead deduplication merge, additional communications connector, or hidden qualification automation is introduced. A suspected duplicate may be an informational link only if a safe existing query supports it; automatic merging is out of scope.

### CRM-02 — Opportunity pipeline

Provide a cross-lead pipeline discovery view if no equivalent exists at implementation time. It is a read model over the existing `Opportunity`, lead, and proposal owners, not a new pipeline table.

Use the existing canonical opportunity stages **DISCOVERY, PROPOSAL, NEGOTIATION, WON, LOST**. Present open stages prominently and completed/lost opportunities through explicit filters or collapsed sections. A column is a grouping, not an editable lifecycle field. Unknown/unsupported values must remain visible as a safe diagnostic state rather than being discarded or assigned to a guessed column.

Each card/row identifies opportunity, lead/client, service route, engagement period, responsible owner, expected fee and currency, stage, and its authoritative proposal reference where available. Multiple opportunities for one client stay distinct. Proposal revisions must not multiply the opportunity value or make one opportunity appear in several financial totals.

A table and board must use the same filter semantics and underlying authorization. Page or load each stage in bounded windows; a loaded slice cannot imply that the column is complete. Column count and currency totals come from the authorized full cohort, not the number of rendered cards. Stable sort and continuation context are required.

**Core pipeline interactions are read-only:** open opportunity; open exact proposal; navigate to the existing allowed commercial action. No drag-to-Won, generic `SetStage`, automatic acceptance, or new stage-transition endpoint. Existing commands remain the only way to change stage. A proposal's sent/accepted/superseded state is shown separately from opportunity stage and engagement readiness.

**Open expected value** is the unweighted sum of valid **expected opportunity fees**, grouped by currency, for the selected open-stage cohort. When viewing Won/Lost records, omit that open-value card or explicitly identify its separate open-record scope; do not imply it totals the closed rows. It is a planning estimate, not signed revenue or collectible debt. Do not add current proposal fees to the same total. Missing currency/value is separately counted and marked unavailable; it must not be treated as zero. The current nullable `Probability` and `NextAction` fields may be displayed only after their read contract and meaning are verified. Weighted forecasts, closing-date estimates, scoring, and editing those fields are not prerequisites for this core view. [AS07], [AX01], [AX05]

### CRM-03 — Opportunity and proposal workspace

Keep the existing lead/opportunity detail route as the record authority. Organize it around:

1. **Overview:** lead/client, service, entity, period, owner, opportunity stage, expected fee, and existing proposal references.
2. **Proposals:** exact revision, draft/review/sent/response status, fee/currency, supersession relationship, and permitted existing actions.
3. **History:** independently paged retained activity, recorded actors/times, response channel, and communication evidence where the user may read it.
4. **Conversion readiness:** a presentation of existing conversion/acceptance/activation records and their blockers, not a new decision engine.

Offer comparisons only between authorized, explicitly selected revisions. Show material differences in scope, period, currency, fee, deliverables, and timeline without overwriting the old offer. A newer proposal requires its own valid acceptance; it cannot inherit the response to an older hash.

Keep commercial and professional readiness distinct. Suggested display labels are **Commercial response recorded**, **Risk clearance current**, **Terms agreed**, **Advance allocated**, and **Engagement activation**. Populate only from the existing owner query; lack of permission to see a sensitive risk rationale must not expose it through a tooltip. Show a permitted high-level blocked state instead.

Preserve current request IDs, revision strings, saved pending requests, and recovery prompts. Do not create another opportunity/client after a timeout merely because the new panel looks empty. Retain `hasLegacyHistoryGap`; never manufacture older activity dates or actors. [AS06], [AS07]

### CRM-04 — Conversion handoff

A conversion summary may gather existing authoritative links into one screen, but actions still invoke their current owners in the same order. Creating a prospect/client shell, generating an engagement letter, recording paid advance, provisioning Microsoft resources, and activating an engagement remain separate facts.

The UX must show the existing next permitted action and responsible capability without trying to complete the whole chain automatically. A relationship manager can see a finance blocker only to the extent already allowed; they do not acquire a finance action. A Partner button does not imply finance reviewer authority. Financial/professional/provider failures must stay distinguishable.

Keep automatic client-site creation and all its original triggering conditions unchanged. Converting a lead must not create extra site requests, duplicate clients, or repeated email dispatch because two UI panels observe the same event.

<a id="s07"></a>
## 7. Engagement planning and resources

### PLAN-01 — Hierarchical view of existing work

Group existing work as **Client/engagement → workflow phase → task group → task**. Reuse `WorkflowTaskBinding.Phase` and `TaskGroupName`; unbound/manual tasks remain explicitly ungrouped rather than being assigned an invented phase. This is a view hierarchy, not a new parent-child model. [AS03], [AS08]

A group header may show loaded task count and complete server count when available. Where only a bounded page exists, say “tasks on this page” and expose continuation. Do not infer that a phase is complete from the tasks currently loaded. Required/Optional labels must continue to originate in the pinned published template; manual tasks stay unlabeled when their current contract has no requiredness.

A task row should expose title, owner, due date, persisted task state, unmet task prerequisites, unmet evidence prerequisites, current review/PBC condition, and existing next action. Preserve mapping/FSLI and template lineage in a details panel. Opening a blocked task should identify the permitted source link and current blocker; it must not offer a generic override.

Keep existing list/board/calendar toggles and queue meanings. A task may belong to overlapping queue summaries; do not sum those summaries into a false unique total. Preserve the separate submitted-procedure review inbox, including submissions that have no current task representation.

Do not introduce drag-and-drop completion, universal percentage progress, phase editing, a generic task state setter, or automatic completion of children. Cancelled tasks do not satisfy required predecessors; reopening follows the existing reason/revision/history command.

### PLAN-02 — Engagement context and navigation

Use the same engagement context when switching between tasks, planning, staffing, budgets, and time. Display client, service route, reporting period, responsible staffed person, and current derived phase. Separate **task progress** from **professional gate readiness**.

Provide contextual links rather than duplicated editors. Opening a task from a calendar, reminder, or dashboard must select the exact task even when it lies outside the first normal result page. Preserve current scoped deep-link recovery and stale/revoked-target behavior. A URL or browser filter is never authority to load another client's task.

Do not replace the seven display phases or eleven derived audit states with Axelor project statuses. Resource allocation phases (`PLANNING`, `FIELDWORK`, `COMPLETION`, `REPORTING`, `UNASSIGNED`) are a different classification; do not force a one-to-one mapping to workflow/audit phases.

### PLAN-03 — Resource capacity workspace

Retain the current weekly grid and its 1–12-week input bound. Improve inspection with filters for department, staff member, existing skill/certification fields, and over-allocation where current server data/authorization supports them. Clearly distinguish client-side filtering of a complete bounded grid from a new server-paged population; do not show incomplete filtered totals as complete firm capacity.

Each staff/week cell should expose **net capacity**, **planned allocation**, **all approved actual hours**, **approved billable hours**, and **recorded unavailability**. Do not combine planned and actual effort into one utilization bar. Keep over-allocation text visible alongside any visual indication. Zero capacity means the percentage is undefined; it is not zero utilization.

Selecting a cell should show existing allocations and unavailability with engagement links, phase, and exact minutes. The edit action opens the existing resource editor and reviewed planning flow. Nothing is saved through a hover, filter, card selection, or calendar movement.

Maintain Sunday–Saturday allocation weeks and Sunday–Thursday working days. Public holidays remain recorded unavailability, not a generated holiday service. Display date-only fields without timezone shifting; display operational instants with their current documented basis. Do not silently redefine UTC reports as Qatar-day reports.

Skills, certificate expiry, independent verification, staffing eligibility, and current role grants are different facts. An unexpired certification does not establish approved professional eligibility. A resource allocation does not create an engagement role or direct SharePoint access; existing staffing commands retain their current side effects. [AS09], [AS10]

### PLAN-04 — Templates, dependencies, and time handoff

Improve the existing template picker and plan preview with service applicability, published version, task groups, required/optional classification, proposed due dates, role assignments, dependencies, and evidence requirements already supported by the owner.

Keep preview and execution separate. Changing the selected template, staffing, due-date basis, or source references invalidates the review preview and clears assent. Instantiation uses the existing idempotent plan request and receipt. A second click or lost response cannot create another plan. Library publication must not rewrite adopted plans.

Do not add arbitrary recurrence, automatic annual roll-forward, or approval copying. Link to existing permitted annual/template behavior only. Existing dependency bounds, full-graph validation, and cycle checks remain unchanged. Do not imply that a visually hidden task group can be removed from the professional program.

Provide an existing **Record time** link from task/engagement context, carrying safe identifiers for server resolution. Preserve current approved mapping/FSLI lineage, billable classification, captured rates, time review, and correction behavior. Prefilled context is not permission and must be revalidated. Never record time automatically from screen-open duration or convert fixed-fee time into a new bill without the existing finance workflow. [AS03], [AS21]

<a id="s08"></a>
## 8. Billing and collections

### BILL-01 — Firm billing discovery workspace

Add or enhance a bounded invoice discovery view under the existing Finance area. First check the current route/query inventory; reuse an equivalent invoice list if present. The inspected `/app/finance` is a ledger workspace and must keep that meaning. A new billing child route is an additive read experience, not a replacement finance home. [AS12], [AS15]

Recommended filters: client, exact engagement where lineage supports it, invoice type, persisted invoice status, currency, invoice number, and a clearly labeled date basis. Use the existing invoice/fee/time source relationships. Where an invoice spans engagements, show **Multiple engagements** rather than picking the first. Unsupported or conflicting lineage is a visible data-quality condition, not a guessed link.

Recommended columns: invoice number, client, engagement/service context, type, currency, total, credited, net allocated, outstanding, approved due date, invoice state, and next permitted action. Settlement labels such as Partially allocated are derived displays; do not create new persisted invoice statuses.

Keep these fact dimensions separate:

| Dimension | Meaning | Must not imply |
|---|---|---|
| Invoice workflow state | Current persisted draft/review/approved/posted/sent/cancelled state, as supported by existing constants | Cash received, client delivery, or professional release |
| Settlement observation | Original total, issued credits, net applied receipts, and outstanding at the declared basis | A new posted state or permission to bypass allocation |
| Payment terms | Current independently approved due date and its evidence; pending revisions shown separately | A default credit policy or assumed NET 30 |
| Dispatch evidence | Existing staff-recorded or provider-observed facts from the exact invoice/communication owner | Delivery/read evidence when none exists |
| Fee milestone | Advance/balance agreement source and eligibility | Revenue recognition or automatic posting |

For a new summary, aggregate only records in its stated cohort and currency, with a consistent read observation. Show draft value separately from posted receivables. Do not use a “Paid” toggle, sum unlike currencies, or count draft invoices as overdue debt. Prefer existing receivables totals for receivables rather than introducing a competing formula.

Opening an invoice uses the existing detail route. Clicking a summary changes only the read filter. No bulk approve/post/send/credit/write-off or payment initiation is part of this work.

### BILL-02 — Invoice record composition

Reorganize the existing invoice page while keeping every current action and its behavior:

| Section | Required contents |
|---|---|
| Overview | Invoice identity, client billing context, currency, persisted status/revision, created/posted dates, total/credited/allocated/outstanding |
| Lines and sources | Existing line quantities/rates/totals, exact source links where authorized, and whole-invoice totals |
| Payments and allocations | Recorded receipts, their available balances, allocation history, and independently reviewed reversal history |
| Terms and credit notes | Approved contractual date, separate pending terms, evidence references, current credit notes, and their history/paging |
| Review and history | Existing authorized workflow actions, preparer/reviewer facts where exposed, and exact pending request/receipt recovery |

Use a concise, persistent amount strip, but keep current values and currency together. A large page may use tabs or anchored sections; do not destroy a pending action component on tab change without preserving its existing request reference and recovery state. A newly selected invoice must never inherit another invoice's draft, assent, or unresolved request.

Preserve the inspected `SENT` disclosure: it is a staff-recorded sent state without invoice dispatch/delivery/read evidence. Do not replace that notice with a successful email icon or a delivery timestamp. This UX scope adds no invoice mail sender. [AS11]

Default the record to reading, not to a large set of editable financial forms. Show action forms only after the corresponding permitted action is selected. Before submission, display the same exact target, amount/currency, reason, evidence, and consequences currently required. Keep independent approvals separate from preparation.

### BILL-03 — Receipts, allocations, and corrections

Preserve distinct operations for recording received cash, applying an existing receipt, issuing a credit note, proposing a terms revision, and requesting/reviewing an allocation reversal. UI grouping must not merge these into an unsafe “Settle invoice” command.

When selecting a receipt, show its currency, recorded amount, amount already applied, and currently available amount. When allocating, show the exact invoice and its current outstanding balance. Do not silently clamp the requested amount to a new balance after another actor changes it; reject/reload through the existing command response and require the user to review again.

Use the existing `BillingCommandWorkspace` request identity and receipt lookup for supported receipt/allocation/credit commands. Retrying with a new request ID after an uncertain response is prohibited. For actions whose current contract uses state verification rather than an immutable command receipt, keep that established mechanism and do not invent a false proof of exact execution.

An approved reversal unapplies an allocation under the current policy; it is not a bank refund. A credit note is not a cash receipt. Pending or rejected corrections do not change the displayed financial balance. The same user must not prepare and independently approve a protected revision when current rules prohibit it.

No automatic settlement from matching bank-reference text, no bank connection, and no payment initiation are added. Existing receipt and credit histories remain paged and discoverable. [AS03], [AS11], [AS22]

### BILL-04 — Fee milestones and unbilled time

Improve navigation from an invoice to the exact fee agreement/milestone or time-source reference. Keep the current advance and balance eligibility rules. For an accepted QAR 20,000 fee, the approved 50% advance requirement is QAR 10,000; merely displaying an invoice or recording an unallocated receipt does not satisfy it.

Default-off draft automation remains a distinct setting owned by the existing policy. Preserve the state explaining that an advance draft is pending because automation is disabled. The balance draft is not eligible merely because a draft report exists. Reuse the approved financial-package release/paid-advance rule and finance review/posting. [AS20]

Link to the existing unbilled-time report rather than adding a new WIP balance. Retain its buckets **UNALLOCATED, RESERVED, INVOICED, REVIEW_REQUIRED** and fee-basis distinctions. Preserve complete bounded subtotals, unavailable values, source-integrity failures, and the separation between fixed-fee references and time-based invoice sources.

Call the measure **Approved time — standard charge-out value** or the existing equivalent. Never label it recognized WIP, revenue, collectible balance, or automatic billing entitlement. Opening a row navigates to permitted existing time/billing details; it does not reserve or bill it. [AS13]

<a id="s09"></a>
## 9. Financial reporting and practice analytics

### FIN-01 — Receivables workspace

Retain the existing report and its UTC as-of-date meaning. Show the selected as-of date, date basis, bucket policy, currency, and whether the report is complete. Use the current query's actual bucket definitions; this document does not replace them.

Present currency-level totals above a client/engagement/invoice drill-down. Include undated invoices in an explicitly labeled category rather than inventing a due date. Retain current distinctions between approved payment terms and newer pending/rejected revisions. A future receipt or later approved reversal must not change a historical as-of balance.

Keep all posted firm invoice types eligible under the existing rule, not just advance/final invoices. Client-bookkeeping receivables never appear in this report. An ambiguous fee-source relationship remains a refused/diagnostic outcome; it is not resolved by taking the first matching engagement.

When adding display filters, distinguish a filtered subset from the complete returned report. If server filtering/paging is required for scale, preserve complete matching totals and the source formula. Do not remove current safety limits or show a partially computed report as complete. Match export scope to the visible report/filter contract; label any intentional distinction clearly. [AS14]

### FIN-02 — Firm books and ledger reporting

Retain current firm TB, monthly P&L, prior-year comparators, expense evidence, accounts, periods, and journal drill-down. Separate **report viewing** from **ledger setup and protected posting/close actions**, while leaving all existing actions reachable to their existing roles.

Show accounting month, currency, posted-data basis, and report identity where the current owner provides it. A missing prior-year accounting period is unavailable, not a zero comparator. Keep signed negative amounts and deficits visible. Present a monthly series and an accessible detail table from the same returned figures; do not recalculate totals in a chart.

A financial summary must drill to the same permitted ledger source cohort. If a user can read a summary but not a specific ledger workspace under existing rules, suppress the link or present a safe denial; do not grant new ledger access. A link from engagement economics to firm books is navigation, not an assertion that both reports have identical recognition/date bases.

No change to revenue recognition, advance/deferred-income posting, benefit accruals, partner withdrawal treatment, closed-period rules, accounting policy, FX, tax, or client/group books. A chart cannot establish a new accounting basis. [AS15], [AS18], [AS19]

### FIN-03 — Practice analytics composition

Organize the existing analytics screen into focused views rather than showing every metric in one 15-column table:

| View | Primary contents | Explicit basis notice |
|---|---|---|
| Operational delivery | Workload, task completion, phase task counts, review turnaround, client wait | Operational indicators do not establish audit quality, professional approval, or phase completion. |
| Capacity and time | Approved actual/billable/non-chargeable time and utilization | Distinguish planned allocation, available capacity, and actual billable utilization. |
| Engagement economics | Existing standard value, recorded staff cost, billed, allocated cash, realization, and billed-less-cost | Measures follow the existing query; they are not the firm's recognized accounting profit. |
| Contract comparison | Agreed fee, lifetime standard value, contract contribution | Contract/lifetime basis is separate from date-filtered operational time. |

A column preset may hide secondary columns visually but must leave their definitions and authorized detail accessible. Keep the current paging contract and its disclosure: engagement economics is a stable-ID page, while some operational summaries cover the whole selected period. Do not sum the page into a firm-wide headline.

Use existing components/native tables for the initial release. Simple visual bars are optional and must have text equivalents. A third-party chart or Gantt library is not required. Any graph must use already-authorized server figures, visibly handle unavailable data, and link to an equivalent table.

### FIN-04 — Metric dictionary and unchanged formulas

The following dictionary captures the inspected source semantics. The implementation must preserve the numeric output for the same fixtures and explain its basis in the interface. [AS17], [AS18], [AS19]

| Metric | Current basis to preserve | UX rule |
|---|---|---|
| Actual hours | Approved time, including non-billable/no-charge, in the selected work-date range | Show non-chargeable hours separately; never imply all actual hours are billable. |
| Standard value | Approved billable minutes × captured charge-out rate ÷ 60 | No-charge/non-billable time contributes no standard value. Missing billable rate/currency is unavailable. |
| Recorded staff cost | Approved time × applicable internal staff cost rate effective on work date | Missing/conflicting latest effective rates make cost-dependent results unavailable. Do not substitute charge-out rates. |
| Billed | Existing source-linked posted invoice-line basis, net of applicable credits | Retain the owner's selection/allocation logic. Do not call it recognized revenue. |
| Collected / applied cash | Existing receipt allocations net of approved reversals, attributed by the existing invoice-share rule | It is not automatically cash received during the screen's selected time-entry period. State the actual basis. |
| Billing realization | Billed ÷ standard value × 100, when denominator is positive and inputs are available | Label it **Billing realization**. Do not replace the numerator with contract fee. |
| Collection ratio | Collected ÷ billed × 100, when billed is positive | Do not fabricate 0% or 100% for undefined/missing cases. |
| Existing `Profit` DTO field | Billed − recorded actual staff cost | Display **Billed less recorded staff cost**; this is not firm accounting profit and is not a full overhead-inclusive margin. Keep payload compatibility. |
| Existing `MarginPercent` | (Billed − recorded actual staff cost) ÷ billed × 100 | Label **Billed-less-staff-cost margin** with the basis, not “net profit margin.” |
| Budget variance minutes | Approved budget minutes − approved actual minutes | Label **Budget hours remaining / overrun** with signed values; negative means overrun. Do not reverse the formula silently. |
| Contract contribution | Agreed contract fee − lifetime approved billable standard value in the contract currency | Keep separate from period billed-less-cost and firm ledger profit. Ambiguous agreement/rate/currency stays unavailable. |
| Actual utilization | Approved billable minutes ÷ capacity net of recorded unavailability | All approved hours are a separate figure; zero capacity is undefined. |
| Planned utilization | Planned allocation ÷ applicable net capacity | Do not present as actual performance. |
| Task workload | Non-cancelled tasks due in the selected period, grouped by current assignee | Task count is not hours/capacity. Open includes overdue; do not add them together. |
| Task completion | Current completed due tasks ÷ applicable non-cancelled due tasks | No-due tasks are outside this measure; task completion is not professional gate readiness. |
| Review turnaround | Existing exact result-revision submission-to-decision durations for decisions in the selected range | Include both review and changes-required decisions as currently defined; count excluded chronology/revision cases. |
| Client wait | Existing provider-accepted PBC send intervals and recorded client responses | Selected interval starts may finish later; reminders do not reset the clock; provider acceptance is not inbox delivery. |
| Receivables outstanding | Posted firm invoice balance at the existing UTC as-of cutoff, after eligible credits/net allocations | Keep historical cutoff, approved term timing, and existing bucket policy. |
| Firm accounting profit | Existing posted firm ledger income less expense for the selected accounting periods | Separate report/source from engagement economics. |

**Date warning:** The current analytics workspace combines work-date-filtered time, source-linked billing/allocations, current approved budgets, and lifetime contract measures. Do not add one universal “as of” or “this period cash” label over the whole screen. Inspect `SelectEconomicsInvoiceIdsAsync` and the surrounding owner query when adding drill-down so its cohort exactly matches the shown figure. Clarifying a label is in scope; changing a recognition or temporal rule is not.

**Currency warning:** The existing query may return `MIXED` or `UNAVAILABLE` rather than a financial value. Keep that distinction. No browser sum of QAR and USD, no implicit conversion, and no averaging of engagement percentages to create a firm ratio. A new aggregate ratio requires summing compatible numerators/denominators under an approved read definition, not averaging rows.

### FIN-05 — Drill-down contract

Every new drill-down carries the source measure's business context, supported filter, and date/basis description. Reauthorize at the destination. A valid summary does not imply permission to see private staff cost rates, unrelated clients, or individual performance records.

If a figure cannot be reproduced from a permitted destination with the same basis, show its definition without a misleading link until a matching read projection exists. Label the difference between complete totals, current-page data, current observations, and historical reports. Keep empty, unavailable, and access-denied states distinct.

<a id="s10"></a>
## 10. Authorization, precision, and recovery

### 10.1 Existing authority remains authoritative

This table is an implementation guide based on the inspected owners, not a new RBAC policy. The actual command/query remains controlling.

| Surface | Inspected access boundary / required preservation |
|---|---|
| Lead list and new pipeline | Firm-wide internal commercial roles used by `PracticeLeadQuery`; a “My” filter cannot broaden access. |
| Client/engagement detail | Current resource-scoped grants and the destination's own eligibility. A corporate relationship grants no sibling access. |
| My Work and procedure review inbox | Current task/engagement grants, review eligibility, staffing hierarchy, and source-specific checks. |
| Resource grid | Existing firm-wide Partner/Manager/Administrator read policy; editing uses its existing planning authority. |
| Practice analytics | Existing firm-wide Partner/Administrator/FinanceManager policy. Do not give all Managers or FinanceReviewers access by adding a dashboard link. |
| Firm ledger, receivables, unbilled time | Existing FinanceManager/FinanceReviewer query policies, with action-specific preparer/reviewer/poster checks. |
| Invoice actions | Current invoice-specific `can*` and server authorization, not a generic finance navigation permission. |
| Client portal | Remains unchanged; internal pipeline, resource, cost, and review content is not added to it. |

Use existing `AuthorizationDecision`, trusted actor/session resolution, scope filters, CSRF, and safe error conventions. Counts, totals, search suggestions, exports, drawers, cached responses, and alternate views require the same authorization discipline as detail pages. Never fetch all firm records into a lower-privilege browser and hide them locally.

Read models combining several owners must not use one permissive role union. Either return only the subpanels the actor is independently authorized to read or keep those panels behind their existing endpoints. A missing permission produces an honest unavailable/denied panel, not an error that invites role escalation.

### 10.2 Exact values and validation

Keep monetary amounts and existing Int64 revision/count contracts as exact strings where the API requires them. Reuse current bounded decoders. UI display formatting must not feed rounded values back into financial commands. Preserve negative amounts, zero, and unavailable separately.

Validate all new filter enums, identifiers, date ranges, sort choices, page sizes, and continuation context server-side. Use a fixed allowlist of sort fields; no arbitrary SQL/dynamic property sorting. Keep parameterized queries and indexed access where measured. Current resource/report limits remain until a separate measured change is approved.

### 10.3 Concurrent edits and unknown outcomes

A drawer, tab, or board selection must capture the current route, firm, user/session generation, resource ID, and relevant revision. Recheck ownership after each awaited operation. Discard stale responses and close only the dialogs owned by the invalidated workspace.

For a protected mutation, preserve the existing request identity, preview digest, assent, target, expected revision, and payload. A response timeout means unknown outcome, not automatic failure. Keep further conflicting actions blocked, recover the original receipt/state through the established path, and never silently generate a new key. A cancelled HTTP request does not prove that the server cancelled the command.

When an approved change alters a source revision, dependent UI previews must refresh or become stale. Existing receipts/history remain valid records of what actually happened; they must not be overwritten by a new optimistic UI state. No full-screen refactor may weaken this behavior merely to make component extraction easier.

### 10.4 Read consistency and side-effect isolation

New summary queries should share a consistent read snapshot with their own rows when feasible, end it promptly, and recheck current authorization before publication. Do not open long database transactions around browser interaction, provider I/O, or UI polling.

New UX reads must not enqueue external operations. Existing independently scheduled workers may continue their approved work; verification must distinguish that baseline activity from any new effect attributable to navigation or filtering. Reuse existing commands when a user explicitly requests a permitted operation, with the same exactly-scoped side effects. Correlate requests safely without logging financial payloads, contact data, access tokens, source documents, or private-cost values.

<a id="s11"></a>
## 11. Backend, API, frontend, and database changes

### 11.1 Smallest sufficient implementation

The default change is **composition of existing screens and queries**. Introduce an additive query only when a required read shape cannot be supplied correctly by an existing owner. No domain command rewrite, generic repository, mediator, dynamic form framework, second state store, or new queue is justified by this UX work.

The following names marked **proposed** are suggested implementation locations, not claims that those files/routes currently exist. Check the current tree before creating them.

| Area | Existing owners to extend/reuse | Proposed minimal addition only if needed |
|---|---|---|
| Lead summary/filter | `Application/Practice/PracticeLeadQuery.cs`; `Api/Ui/UiEndpoints.Commercial.cs`; `features/commercial/leads.ts` | Optional filter/count fields or a companion summary query with identical authorization/filter semantics |
| Cross-lead pipeline | `Domain/Practice/Crm.cs`; `PracticeCrmService` read/write owners; commercial endpoints; current opportunity UI | **Proposed:** `Application/Practice/CommercialPipelineQuery.cs`, a read-only endpoint, and `features/commercial/pipeline.ts` |
| Proposal/conversion composition | `features/commercial/opportunities.ts`, `proposal.ts`, `quotation.ts`; CRM and existing conversion/acceptance owners | Small presentation components within the same feature; no generic commercial command facade |
| Tasks | `WorkflowTaskQueueQuery`, `EngagementWorkflowTaskService`, existing workflow APIs; `work-queue.ts`, `work-queue-views.ts` | Grouped view helper/components using existing phase/group IDs and bounded data |
| Resources | Existing resource-planning services and APIs; `resources.ts/html/scss`, `resource-editor`, `planning-command-contracts` | Cell/detail presentation and safe optional filtering, without a new scheduling engine |
| Templates/time | `WorkflowTemplateService`, `EngagementWorkflowPlanService`, `PracticeTimeService`; current template/time screens | Preview/detail organization and contextual navigation only |
| Billing discovery | `BillingService`, `BillingInvoiceWorkspaceQuery`, `FeeAgreementService`, current finance endpoints | **Proposed:** `Application/Practice/BillingWorkbenchQuery.cs` and a new billing child view if no equivalent list exists |
| Invoice detail | `features/finance/invoice.ts`; existing billing/terms/reversal/receipt queries and commands | Focused presentational sections retaining one action/recovery owner |
| Unbilled/AR | `FirmUnbilledTimeQuery`, `FirmReceivablesAgingQuery`; current endpoints and report screens | Links, display filters, and aligned drill-down; no new recognition calculation |
| Firm books | `FirmFinanceQuery`, `LedgerService`, `FirmExpenseService` inside `FirmOperationsServices.cs`; `books.ts`, `ledger.ts` | Read/report composition and clearly separated action panels |
| Analytics | `PracticeAnalyticsQuery` in `FirmOperationsServices.cs`, `ContractContributionCalculator`; `analytics.ts` | Column groups, basis labels, matching drill-down; no changed numeric definition |
| Shared UI | `core/api.ts`, `decode.ts`, `drafts.ts`, `tab-drafts.ts`, `session.ts`, `ui.ts`, current style tokens | Reuse first; extract only genuinely repeated changed-screen presentation |

Use full repository prefixes from the source register when opening files. `Application/...` in this table means `src/AuditSphereOps.Application/...`; `features/...` means `src/AuditSphereOps.Ui/src/app/features/...`.

### 11.2 Proposed additive read contracts

These are design requirements, not existing API signatures. Names should be aligned to current conventions during the story inventory.

| Proposed read | Request scope | Required response properties |
|---|---|---|
| Lead summary/filter extension | Existing name search plus optional canonical status/channel/owner; bounded page | Existing fields retained; complete same-filter counts, explicitly scoped observation metadata if added |
| Commercial pipeline | Existing commercial authorization; optional lead/client/service/owner/stage/currency filters; bounded stage/page | Opportunity rows, stable source IDs, authoritative current proposal reference or ambiguity state, complete count and currency-separated expected-value totals |
| Billing workspace | Current finance/invoice authorization; approved client/engagement/status/type/currency/search/date filters | Invoice summaries with existing balance semantics; complete matching totals only where supported; capabilities and exact detail links; observation/date basis |
| Matching report drill-down | Existing permitted measure/context; validated filters/cursor | Rows that reproduce the specified source cohort, not a broad unrelated record search |

A useful new response envelope can include `observedAtUtc`, an allowlisted filter echo, `items`, bounded pagination, and `summaryScope`. That is a recommendation for new contracts only; do not force every existing endpoint to adopt a new envelope. Never introduce an authoritative dashboard snapshot table merely to return consistent reads.

A proposed `GET /api/ui/practice/commercial-pipeline` or `GET /api/ui/finance/billing-workspace` must be checked against the current route catalogue before adoption. Existing writes and download endpoints stay intact. Update OpenAPI, Angular decoders, API tests, and direct-refresh route ownership for each affected contract. A changed decoder must reject malformed or out-of-scope shapes rather than quietly coercing values.

### 11.3 Query rules

Apply authorization before aggregating; do not compute cross-tenant totals and filter them afterwards. Scope every relationship to firm/client/engagement as applicable. Use server-resolved relationships for invoice sources and proposal chains. Never use a client-provided `can*`, stage, status, or amount as authoritative.

Keep filtering/sorting/paging in the database for potentially large pipelines/invoice lists. Avoid one query per row or downloading every client's opportunities into the browser. Propose indexes only after inspecting actual generated SQL and representative plans. A consistent filter/summary read can be added without rewriting the established command architecture.

If a report's existing supported bound is exceeded, preserve its safe refusal and offer a supported narrowing path. Do not invent “all records” completeness from the first 10,000 rows. Avoid claims that a filter exists when the current endpoint cannot accept it; implement the validated optional filter or provide accurate instructions.

### 11.4 Database and migration policy

**No new business tables are required by the core specification.** Reuse existing leads, opportunities, proposals, tasks/bindings/groups, allocations, timesheets, invoices, receipts, source reservations, payment terms, ledger records, and report sources.

Do not add another `PipelineStage`, `Project`, `Task`, `Invoice`, `Customer`, `DashboardBalance`, or `WipLedger` table. Do not duplicate `Opportunity.Probability` or `NextAction`. Do not add expected-close dates or task start/end dates merely to make a forecast or Gantt graphic look populated.

Optional non-unique supporting indexes may be introduced only with demonstrated query need and reviewed upgrade/rollback impact. An index-only migration must not backfill or alter business records. Persistent user-preference tables are not needed for the initial UX; allowlisted route parameters and existing session-scoped presentation state are sufficient.

### 11.5 API compatibility and internal extraction

Mechanically extracting a large invoice/resource component is allowed only when its action state, session fences, validations, review/assent, and tests remain owned coherently. Do not mix unrelated form paradigms or create duplicate `CommandState` instances capable of submitting the same pending action independently.

Preserve legacy payload fields such as analytics `Profit` while improving their displayed labels. New fields should be additive; a rename that changes JSON contracts is not necessary for clearer wording. Do not normalize legacy nulls into invented values. Existing report IDs, source identities, and opaque cursors keep their meaning.


<a id="s12"></a>
## 12. Implementation stories

These are bounded work packages, not a promise of single-pass completion. Each story requires its affected source to be refreshed at implementation time. P0 marks correctness/parity foundations; P1 marks subsequent UX work. Dependencies define sequencing, not a demand for parallel agents. All stories inherit Sections 3 and 10.

| Story | Work package | Priority | Dependencies |
|---|---|---|---|
| [UX-00](#ux-00) | Pin the baseline and record parity | P0 | None |
| [UX-01](#ux-01) | Apply consistent workspace composition | P0 | UX-00 |
| [UX-02](#ux-02) | Correct lead summary scope and add bounded filters | P0 | UX-00, UX-01 |
| [UX-03](#ux-03) | Introduce a read-only opportunity pipeline | P1 | UX-00, UX-01, UX-02 |
| [UX-04](#ux-04) | Compose proposal history and conversion readiness | P1 | UX-00, UX-01, UX-03 |
| [UX-05](#ux-05) | Improve task hierarchy and alternate views | P1 | UX-00, UX-01 |
| [UX-06](#ux-06) | Refine weekly resource inspection | P1 | UX-00, UX-01, UX-05 |
| [UX-07](#ux-07) | Improve template preview and task-to-time handoff | P1 | UX-00, UX-01, UX-05 |
| [UX-08](#ux-08) | Compose billing discovery and invoice detail | P1 | UX-00, UX-01 |
| [UX-09](#ux-09) | Preserve financial action and recovery UX | P0 | UX-00, UX-01; coordinate with UX-08 |
| [UX-10](#ux-10) | Improve receivables and unbilled-time navigation | P1 | UX-00, UX-01, UX-08 |
| [UX-11](#ux-11) | Clarify firm books and ledger reporting | P1 | UX-00, UX-01, UX-08 |
| [UX-12](#ux-12) | Recompose analytics and explain existing metrics | P0 | UX-00, UX-01 |
| [UX-13](#ux-13) | Demonstrate integrated parity and release safely | P0 | UX-00 through UX-12 applicable scope |

<a id="ux-00"></a>
### UX-00 — Pin the baseline and record parity

**Priority / dependencies:** P0; None.

**Current evidence:** Repository instructions, Angular conventions, and the functional-parity storage boundary are source-observed. The runtime and rendered baseline have not been established by this review. [AS00], [AS01], [AS02], [AS03]

**Scope:** Inventory changed-screen routes, actions, roles, labels, query bases, and recovery flows at the actual implementation commit. Identify existing equivalent queries before creating files. Preserve concurrent storage work.

**Implementation:** Produce a compact action/route/metric parity matrix and affected-file list; no application/schema changes are required. Capture approved synthetic baseline outputs and relevant existing tests when the execution environment permits.

**Failure cases and non-goals:** Do not create a duplicate backlog for already completed features. Record unverified runtime behavior as unverified; isolate policy conflicts rather than inferring approval.

**Verification:** Use the inventory as the gate for subsequent stories. Existing route/action absence, financial-output differences, or unexplained permission changes block acceptance. Acceptance: UX-AC-01, UX-AC-02, UX-AC-03, UX-AC-04.

<a id="ux-01"></a>
### UX-01 — Apply consistent workspace composition

**Priority / dependencies:** P0; UX-00.

**Current evidence:** Shared page/state/status components, Material/CDK, design tokens, and command/draft helpers already exist. [AS02]

**Scope:** Improve headers, filter placement, readable summaries, section navigation, focus behavior, and loading/empty/denied/recovery states in the selected screens only.

**Implementation:** Extend existing feature templates/styles; extract small presentation helpers only when reused. Preserve existing route ownership, Api.resource, decoders, and component/session fences. No new UI framework or preference table.

**Failure cases and non-goals:** Keep pending financial/planning actions alive and unambiguous across tabs; do not persist assent. Do not load protected hidden panels for unauthorized users.

**Verification:** Targeted component/navigation checks plus keyboard and narrow-width verification. Record rendered evidence separately from static lint. Acceptance: UX-AC-05, UX-AC-06, UX-AC-07, UX-AC-08.

<a id="ux-02"></a>
### UX-02 — Correct lead summary scope and add bounded filters

**Priority / dependencies:** P0; UX-00, UX-01.

**Current evidence:** Lead search is paged; the qualification card uses loaded rows. The owner query requires firm-wide commercial authorization and rechecks it. [AS04], [AS05]

**Scope:** Add canonical status/channel/owner filters and complete matching lead counts. Retain current creation, qualification, channels, pagination, and recovery.

**Implementation:** Extend PracticeLeadQuery and its existing commercial endpoint/decoder; add fields compatibly. Update leads.ts. Reuse existing CRM tables; consider indexes only after measured plans.

**Failure cases and non-goals:** My leads is narrowing, not authorization. Count and rows must agree for the same query observation. Unassigned/legacy owners remain explicit.

**Verification:** Extend nearby lead query/API/UI tests. Demonstrate more than one page, empty search, all supported statuses, role denial, and unchanged qualification side effects. Acceptance: UX-AC-09, UX-AC-10, UX-AC-11, UX-AC-12.

<a id="ux-03"></a>
### UX-03 — Introduce a read-only opportunity pipeline

**Priority / dependencies:** P1; UX-00, UX-01, UX-02.

**Current evidence:** Canonical opportunity states and per-lead opportunity/history views exist; a cross-lead board is not established by the inspected slices. Probability and NextAction fields already exist. [AS06], [AS07]

**Scope:** Provide an authorized table/board grouped by existing states with owner/service/client/currency filters, bounded stage pages, and complete currency-separated expected-fee totals.

**Implementation:** Reuse the existing owner if found; otherwise add proposed CommercialPipelineQuery, an additive read endpoint, pipeline.ts, and route/decoder tests. No new opportunity table or mutation endpoint.

**Failure cases and non-goals:** No drag-to-Won, editable stages, forecasts, auto-conversion, or latest-proposal guess. An opportunity is counted once regardless of proposal revisions.

**Verification:** Verify row/summary coherence, multi-page columns, same-client multiple opportunities, multiple currencies, malformed cursors, and current commercial authorization. Acceptance: UX-AC-13, UX-AC-14, UX-AC-15, UX-AC-16.

<a id="ux-04"></a>
### UX-04 — Compose proposal history and conversion readiness

**Priority / dependencies:** P1; UX-00, UX-01, UX-03.

**Current evidence:** Current opportunity detail retains proposal revisions, activity paging, legacy gaps, and recoverable create intents. Existing acceptance and fee gates remain separate. [AS06], [AS07], [AS20]

**Scope:** Group overview, proposals, history, and existing readiness links. Make revision comparisons and current next actions easy to inspect without merging business decisions.

**Implementation:** Modify existing opportunities.ts/proposal.ts/quotation.ts and their already-owned queries as needed; any additional readiness response must redact restricted reasons. Keep current commands and response hashes.

**Failure cases and non-goals:** No repeated creation after lost responses, no inherited acceptance on superseding offers, no finance access through a Partner/commercial role, and no duplicate provisioning/mail effects.

**Verification:** Extend existing commercial/proposal journey assertions and include old direct links, revised offers, independent review, pending conversion recovery, and unchanged provider intent counts. Acceptance: UX-AC-17, UX-AC-18, UX-AC-19, UX-AC-20.

<a id="ux-05"></a>
### UX-05 — Improve task hierarchy and alternate views

**Priority / dependencies:** P1; UX-00, UX-01.

**Current evidence:** My Work already has list/board/calendar, task groups, overlapping default-view summaries, dependency/evidence state, and a separate review inbox. [AS08], [AS03]

**Scope:** Group existing tasks by engagement, phase, and TaskGroupName; improve context/details and safe navigation across the existing views.

**Implementation:** Extend work-queue.ts/work-queue-views.ts and existing workflow projections only where needed. Preserve WorkTask identity, source bindings, requiredness, bounds, and current feature switch.

**Failure cases and non-goals:** No new task model, completion semantics, audit-stage mutation, or hiding of review submissions without task representation. Bound group counts must disclose partial pages.

**Verification:** Reuse workflow queue/dependency/evidence tests and targeted UI tests. Verify identical task IDs across views, late-page deep links, blocked completion, cancellation/reopening, and review inbox coverage. Acceptance: UX-AC-21, UX-AC-22, UX-AC-23, UX-AC-24.

<a id="ux-06"></a>
### UX-06 — Refine weekly resource inspection

**Priority / dependencies:** P1; UX-00, UX-01, UX-05.

**Current evidence:** The existing weekly grid already separates capacity, availability, planned, approved actual/billable time, and reviewed planning requests. [AS09], [AS10]

**Scope:** Add clear staff/week detail, safe filtering, and contextual engagement links. Preserve the established grid bounds and editing/recovery pathway.

**Implementation:** Extend resources.ts/html/scss, resource-editor, and existing planning query contracts only as required. No new scheduling engine or derived staffing grants.

**Failure cases and non-goals:** Preserve Sunday-based weeks, date-only values, certificate verification labels, zero-capacity behavior, and over-allocation visibility. Cell selection must not save.

**Verification:** Focused grid/preview/receipt tests and keyboard inspection. Verify dates, net capacity, missing profiles, over-allocation, stale previews, and lost-response planning recovery. Acceptance: UX-AC-25, UX-AC-26, UX-AC-27, UX-AC-28.

<a id="ux-07"></a>
### UX-07 — Improve template preview and task-to-time handoff

**Priority / dependencies:** P1; UX-00, UX-01, UX-05.

**Current evidence:** Published templates, grouped steps, preview digests, schedule/staffing pins, dependency gates, and idempotent execution are present in source/test bodies. [AS03], [AS21]

**Scope:** Make applicability/version, groups, dependencies, proposed due dates, and missing assignments visible. Add contextual links to existing time capture.

**Implementation:** Reuse WorkflowTemplateService, EngagementWorkflowPlanService, existing workflow-template/plan UI, and PracticeTimeService routes. No new plan schema.

**Failure cases and non-goals:** Changed inputs clear assent; repeat execution returns the original receipt. Do not carry prior approvals into a new year or automatically bill fixed-fee time.

**Verification:** Extend PlanPinsPublishedTemplateAndSchedule_AndRecoversRetryWithoutDuplicateTasks and the nearest UI tests; verify published-version immutability and time-context scope. Acceptance: UX-AC-29, UX-AC-30, UX-AC-31, UX-AC-32.

<a id="ux-08"></a>
### UX-08 — Compose billing discovery and invoice detail

**Priority / dependencies:** P1; UX-00, UX-01.

**Current evidence:** Invoice detail already exposes separate balances, terms, receipts, credits, and role-specific actions. The current finance entry route is the firm ledger. [AS11], [AS12], [AS15]

**Scope:** Reuse or add a bounded firm-invoice discovery view; reorganize invoice reading into overview, lines/sources, payments, terms/credits, and review/history.

**Implementation:** Reuse BillingInvoiceWorkspaceQuery and current routes; proposed BillingWorkbenchQuery/new child route only if needed. Extract invoice presentation without duplicating command ownership.

**Failure cases and non-goals:** Keep /app/finance meaning, all current actions, exact balances, undated terms, and staff-recorded SENT disclosure. No bulk financial mutation or auto-email.

**Verification:** Verify existing detail links, complete-versus-page totals, scoped invoice lists, mixed currencies, all action locations, and the unchanged sent-evidence notice. Acceptance: UX-AC-33, UX-AC-34, UX-AC-35, UX-AC-36.

<a id="ux-09"></a>
### UX-09 — Preserve financial action and recovery UX

**Priority / dependencies:** P0; UX-00, UX-01; coordinate with UX-08.

**Current evidence:** Current commands distinguish receipt recording, allocation, credits, reviewed terms and reversals; exact request recovery and financial-role separation exist. [AS03], [AS11], [AS22]

**Scope:** Improve action selection and review summaries without changing the mutations. Surface current available receipts and immutable correction history.

**Implementation:** Keep BillingService/BillingCommandWorkspace/terms/reversal owners and endpoint payloads. Refactor only the affected UI form composition and recovery display.

**Failure cases and non-goals:** No new request ID on uncertain retry, silent amount clamping, self-review, bank-payment initiation, or mutation from tab selection. Pending corrections do not alter balances.

**Verification:** Reuse the inspected BillingWorkflow test and focused API/browser cases for concurrency, same-key replay, changed payload, revoked authority, independent terms/reversals, and exact balances. Acceptance: UX-AC-37, UX-AC-38, UX-AC-39, UX-AC-40.

<a id="ux-10"></a>
### UX-10 — Improve receivables and unbilled-time navigation

**Priority / dependencies:** P1; UX-00, UX-01, UX-08.

**Current evidence:** FirmReceivablesAgingQuery and FirmUnbilledTimeQuery already implement firm-only reporting, bounded complete subtotals, and explicit date/value bases. [AS13], [AS14]

**Scope:** Improve report filters/labels, currency/client grouping, existing exports, and invoice/time drill-down. Reuse current unbilled categories and notices.

**Implementation:** Extend existing report screens and validated optional read filters only. Preserve current formulas, report bounds, date cutoffs, source reservations, and endpoint authorization.

**Failure cases and non-goals:** Do not treat standard value as WIP/debt, add fixed-fee time to invoices automatically, combine client-ledger AR, or invent due dates.

**Verification:** Verify historical versus later transactions, undated terms, report bounds, multi-currency separation, source ambiguity, and report/export/drill-down alignment. Acceptance: UX-AC-41, UX-AC-42, UX-AC-43, UX-AC-44.

<a id="ux-11"></a>
### UX-11 — Clarify firm books and ledger reporting

**Priority / dependencies:** P1; UX-00, UX-01, UX-08.

**Current evidence:** Firm books and ledger owners expose accounting periods, journals, postings, reporting and protected close/reopen. Monthly comparison semantics are approved. [AS15], [AS18], [AS19]

**Scope:** Separate report reading from configuration/actions visually; improve monthly series, prior-year disclosure, source links, and accounting context.

**Implementation:** Reuse FirmFinanceQuery, LedgerService, FirmExpenseService, existing books.ts/ledger.ts and report contracts. No new GL, recognition, FX, or tax engine.

**Failure cases and non-goals:** Keep independent posting/close controls, original report IDs, exact signed values, missing-prior-period nulls, and permitted evidence links.

**Verification:** Extend nearby firm-book/ledger tests and report UI checks. Demonstrate report reconciliation, authorized drill-down, closed-period denial, and unchanged firm/client/group separation. Acceptance: UX-AC-45, UX-AC-46, UX-AC-47, UX-AC-48.

<a id="ux-12"></a>
### UX-12 — Recompose analytics and explain existing metrics

**Priority / dependencies:** P0; UX-00, UX-01.

**Current evidence:** Analytics already provides economics plus operational summaries. Realization is billed/standard, Profit is billed less recorded staff cost, and lifetime contract contribution is separate. [AS16], [AS17], [AS18], [AS19]

**Scope:** Create focused column groups/views, accurate labels, definition disclosure, and only source-matched permitted drill-downs.

**Implementation:** Keep PracticeAnalyticsQuery and existing JSON fields/formulas. Any new read metadata is additive. Reuse current CSS and tables; charts are optional, not a dependency.

**Failure cases and non-goals:** No page-to-firm sums, averaging ratios, invented FX, missing-as-zero, contract-fee substitution for realization, or period-cash claims unsupported by the source.

**Verification:** Snapshot numeric output by approved fixture, not visual markup. Verify formulas/bases, nulls, mixed currencies, pagination, private-cost permissions, and accessibility. Acceptance: UX-AC-49, UX-AC-50, UX-AC-51, UX-AC-52.

<a id="ux-13"></a>
### UX-13 — Demonstrate integrated parity and release safely

**Priority / dependencies:** P0; UX-00 through UX-12 applicable scope.

**Current evidence:** This review has no executed application, browser, CI, or live-provider acceptance results. Existing code/test presence is not release evidence.

**Scope:** Run coherent synthetic role-based journeys across the changed areas and verify the baseline parity matrix, same-source contracts, and rollback.

**Implementation:** Use current focused tests locally; full existing suites in CI/CD on the exact candidate. Update only the real execution ledger with observed results. No tenant or storage activation.

**Failure cases and non-goals:** A UX release must not change protected command results, dispatch/provisioning counts, approved data, permissions, or integration settings. Irreversible operations require owner authorization.

**Verification:** Archive candidate identity, test evidence, visual/keyboard review, metric comparison, provider non-effect checks, and additive UI rollback instructions. Record remaining blockers honestly. Acceptance: UX-AC-53, UX-AC-54, UX-AC-55, UX-AC-56.

<a id="s13"></a>
## 13. Acceptance scenarios

**All scenarios below are specified, not executed in this review.** They are observable outcomes, not a requirement to add one new test file per row. Reuse or extend existing high-signal tests and consolidate equivalent fixtures. Retain command/UI evidence, persisted results, source commit, environment and actual verdict.

### UX-00: Pin the baseline and record parity

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-01 | Baseline action coverage | Inventory every existing changed-screen action and deep link for each applicable role. | Each has an equivalent destination and unchanged owner/guard; no action disappears in the new layout. |
| UX-AC-02 | Updated source detection | Implement against a commit newer than the reviewed SHA. | Changed owners/contracts are re-read; concurrent SharePoint storage work is not overwritten or reinterpreted. |
| UX-AC-03 | Integration non-regression | Open and filter all new workspaces without explicit workflow commands. | No mail, site provisioning, permission sync, document cleanup, financial posting, or archive mutation is initiated. |
| UX-AC-04 | Evidence honesty | Inspect the completion record for this feature. | Source inspection, executed tests, browser review, CI and live-provider acceptance are reported separately; missing evidence is not a pass. |

### UX-01: Apply consistent workspace composition

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-05 | Direct link and refresh | Open an existing proposal, task, invoice and ledger URL directly, then refresh. | The same scoped resource and route meaning are preserved; no new base-path or SPA ownership failure. |
| UX-AC-06 | Keyboard and narrow layout | Use keyboard-only navigation and a narrow viewport through filters, rows, tabs and details. | Focus remains visible and intentional; complete amounts/context remain available; table scroll areas and dialog return focus work. |
| UX-AC-07 | Pending action on tab change | Start an action, lose its response, then switch tabs/details. | The original unresolved request remains recoverable, cannot be submitted by a second form, and does not transfer to another record. |
| UX-AC-08 | Permission invalidation | Disable/revoke the current identity while reads/dialogs are pending. | Late responses cannot repopulate protected content; owned dialogs/drafts clear under existing rules; the server still refuses unauthorized access. |

### UX-02: Correct lead summary scope and add bounded filters

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-09 | Lead count across pages | Create a permitted synthetic query with more than 25 leads and NEW records on later pages. | Matching and awaiting-qualification totals cover the same full cohort, or an explicitly page-only interim label is used. |
| UX-AC-10 | Filter/count coherence | Apply name, canonical status, channel and owner filters, including Unassigned. | Rows and complete counts use the same authorized filters; malformed values are rejected, not widened to an unrestricted query. |
| UX-AC-11 | My leads does not grant access | Attempt the new list/summary with only an insufficient client-scoped commercial grant. | Existing firm-wide eligibility still denies; no names, counts or amounts leak through summary endpoints. |
| UX-AC-12 | Existing creation/qualification recovery | Lose a lead-create response and recover; qualify using the original workflow. | No duplicate lead, changed channel taxonomy, removed validation, or bypassed existing qualification behavior. |

### UX-03: Introduce a read-only opportunity pipeline

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-13 | One opportunity, many proposals | Create multiple proposal revisions for one opportunity and another opportunity for the same client. | Each opportunity appears/counts once with its correct authoritative proposal relationship; one result does not close the other. |
| UX-AC-14 | No stage shortcut | Interact with board columns/cards and forge a stage in a request. | The board performs no direct stage mutation; only the existing legitimate domain commands can change stage or activate work. |
| UX-AC-15 | Pipeline currency and nulls | Mix QAR/USD opportunities and an unavailable currency/value. | Totals remain currency-separated; missing inputs are disclosed; no FX or weighted forecast is invented. |
| UX-AC-16 | Bounded complete board | Load a stage with more records than its visible page, change a filter, and reuse the old continuation. | Loaded-window labels are accurate; totals are complete for their cohort; stale/foreign continuation is reset or refused. |

### UX-04: Compose proposal history and conversion readiness

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-17 | Superseded offer | Accept a dispatched proposal, then create a new materially changed revision. | Old evidence remains; the new view cannot label the new offer accepted or reuse the earlier response hash. |
| UX-AC-18 | Conversion is not activation | Display a commercially won/converted lead with risk, terms or advance gates missing. | Readiness remains blocked under its actual owner; no UI shortcut creates professional approval or activation. |
| UX-AC-19 | Legacy history and source privacy | Open an old lead with missing historical events and a restricted risk record. | Known history is shown with its gap; no invented actors/times or confidential risk detail appears in commercial UI. |
| UX-AC-20 | Exact advance requirement | For a valid QAR 20,000 agreement, allocate QAR 9,999.99, then a valid total QAR 10,000. | The first amount does not unlock the payment gate; the full allocated advance can satisfy only that gate, with other prerequisites unchanged. |

### UX-05: Improve task hierarchy and alternate views

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-21 | Same records in alternate views | View identical filtered tasks in list, board, calendar and grouped presentation. | Same task/source IDs and authoritative states; no cloned task or new lifecycle interpretation. |
| UX-AC-22 | Blocked and cancelled predecessors | Attempt successor completion with unmet evidence or a cancelled required predecessor. | Existing server gate refuses; grouping/collapse does not hide or override the blocker. |
| UX-AC-23 | Unrepresented review submission | Submit a procedure result without a matching active task representation. | It remains discoverable in the separate review inbox and is not lost by task-only navigation. |
| UX-AC-24 | Late-page and revoked task link | Follow a reminder to a task beyond the initial page, then revoke access. | Exact scoped selection works while permitted and fails safely after revocation; no broad task fetch bypass. |

### UX-06: Refine weekly resource inspection

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-25 | Capacity calculation labels | Use net capacity 100 hours, planned allocation 110, approved billable 80 and approved no-charge 20. | Planned utilization shows 110% and over-allocation; actual billable utilization 80%; all approved actual hours 100. |
| UX-AC-26 | Dates and zero capacity | Inspect a Sunday allocation week, Friday/Saturday non-working days, recorded leave and a zero-capacity week. | Existing calendar rules stay unchanged; date-only values do not shift; zero-capacity percentage is unavailable. |
| UX-AC-27 | Selection versus mutation | Open staff/week details, filter by skill, and inspect an unexpired but separately unverified certificate. | No allocation/role/site-access write occurs; certificate expiry is not represented as professional verification. |
| UX-AC-28 | Planning preview conflict | Change staffing/allocation data after preview or lose the submit response. | Stale preview is rejected; renewed assent is required; original receipt recovery prevents duplicate planning effects. |

### UX-07: Improve template preview and task-to-time handoff

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-29 | Template revision pinning | Publish a new library revision after an engagement adopted the prior one. | Existing plan and historical decisions remain pinned; no silent adoption or approval copying. |
| UX-AC-30 | Concurrent plan execution | Submit the same reviewed plan request concurrently and recover a lost response. | One accepted plan outcome and original receipt; no duplicate tasks/dependencies. |
| UX-AC-31 | Applicability and dependencies | Select an incompatible/unpublished template or change dependencies after preview. | Existing applicability/review/generation checks refuse execution; no partial graph or cycle bypass. |
| UX-AC-32 | Contextual time and fixed fee | Open time capture from a task with pinned mapping under a fixed-fee engagement. | Current context/grants are revalidated; no screen-time recording, rate revaluation, or automatic extra time invoice. |

### UX-08: Compose billing discovery and invoice detail

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-33 | Finance entry preserved | Open /app/finance and its existing ledger action links after adding billing discovery. | Ledger meaning and protected account/period/journal actions remain; new discovery is an additive destination. |
| UX-AC-34 | Invoice states are separate | Inspect posted, staff-marked SENT, partially allocated, undated and pending-terms invoices. | Workflow, settlement, approved due-date and dispatch evidence are distinct; SENT is not delivery; no guessed terms. |
| UX-AC-35 | Billing totals and permissions | Page/filter a multi-currency invoice list and attempt foreign firm/client identifiers. | Totals have declared complete scope/currency; unauthorized records and summary information remain unavailable. |
| UX-AC-36 | Invoice layout parity | Compare pre/post-layout action inventory, line totals, histories and download links. | Every supported action remains reachable with unchanged payload/recovery; no source/balance/history is omitted. |

### UX-09: Preserve financial action and recovery UX

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-37 | Exact receipt and credit balance | Use the existing synthetic invoice fixture: total 220, issued credit 20, net allocation 200. | Outstanding is exactly zero; a further allocation is rejected and posted amounts remain immutable. |
| UX-AC-38 | Idempotency and unknown outcome | Repeat same-key/same-payload receipt/allocation/credit commands, then change the payload. | Original effect/receipt is reused; payload conflict is rejected; the new layout never generates a substitute key automatically. |
| UX-AC-39 | Concurrent and revoked financial action | Two actors attempt to apply the same available receipt; revoke one actor before commit. | No over-allocation or unauthorized publication; stale input is not silently clamped to a different amount. |
| UX-AC-40 | Independent corrections | Submit terms or an allocation reversal, attempt self-review, then use an eligible independent reviewer. | Self-review is refused; pending/rejected records do not alter balances/dates; approved changes follow existing effective-date rules. |

### UX-10: Improve receivables and unbilled-time navigation

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-41 | Historical receivables cutoff | Run an earlier as-of report before later receipts, credits, approved terms and reversals. | Only facts eligible at the existing UTC cutoff affect that report; later events do not rewrite its result. |
| UX-AC-42 | All firm invoice types only | Include advance, final, time and other posted firm invoices plus a client-bookkeeping invoice. | Firm report includes its eligible types and excludes client books; ambiguous milestone lineage remains a safe failure. |
| UX-AC-43 | Unbilled semantics | Inspect unallocated/reserved/invoiced/review-required time including fixed-fee references and missing rates. | Existing categories/notices remain; no recognized WIP/debt or auto-billing is inferred; unavailable value stays unavailable. |
| UX-AC-44 | Report bound and export | Exceed a supported report bound, then request a permitted bounded report and export/drill-down. | No silently truncated totals; the supported report/export cohort and date basis agree; narrowing instructions are actionable. |

### UX-11: Clarify firm books and ledger reporting

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-45 | Firm report reconciliation | Compare monthly ledger series, range totals and permitted underlying postings. | Values agree under the same source/report identity; engagement economics is not substituted for posted-ledger profit. |
| UX-AC-46 | Missing comparator and signed value | Use a negative current month and no corresponding prior-year period. | Negative value remains visible; prior comparator is unavailable, not zero or an invented percentage. |
| UX-AC-47 | Protected ledger operations | Try posting into a closed period or reviewing with an insufficient role from the reorganized page. | Existing period/role/independence controls still refuse; no convenience chart/drill-down changes posting state. |
| UX-AC-48 | Financial-domain isolation | Navigate firm reports from an engagement belonging to a group with client-bookkeeping records. | Only firm-book data enters firm totals; no consolidation elimination or client invoice posts into the firm ledger. |

### UX-12: Recompose analytics and explain existing metrics

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-49 | Distinct metric formulas | Use compatible synthetic inputs: billed 15,000; standard value 25,000; staff cost 10,000; contract fee 20,000; lifetime standard 25,000. | Billing realization 60%; billed less staff cost 5,000; contract contribution −5,000. None is mislabeled firm accounting profit. |
| UX-AC-50 | Missing/mixed metric input | Remove a billable rate or create conflicting latest staff costs/mixed currencies/zero denominator. | Affected values and ratios become unavailable under existing logic, not zero/default/implicit FX; unaffected permitted fields remain honest. |
| UX-AC-51 | Page versus whole-period summary | Load a later economics page while operational summaries cover the complete supported period. | Labels disclose both scopes; no page sum becomes a firm total; ratios are not averaged or relabeled as contract realization. |
| UX-AC-52 | Drill-down privacy and time basis | Inspect a collected figure/contract comparison and follow its permitted source links as different roles. | Links match source basis; no unsupported period-cash/as-of claim, private cost-rate leak, or access expansion. |

### UX-13: Demonstrate integrated parity and release safely

| ID | Scenario | Fixture / action | Required result |
|---|---|---|---|
| UX-AC-53 | Connected commercial-to-finance journey | Run a coherent synthetic lead/proposal/conversion/advance/engagement/invoice path with distinct eligible actors. | The UX improves navigation while preserving exact business outcomes, reviewer separation, source identities and one-time existing side effects. |
| UX-AC-54 | Connected planning-to-report journey | Adopt a template, allocate resources, record/review time, inspect unbilled value, billing and analytics. | Same task/time/source facts flow through existing owners; no lifecycle auto-advance, duplicate billing or altered metric definition. |
| UX-AC-55 | Release and rollback evidence | Run relevant tests and exact-candidate full CI, then rehearse returning to the previous UI with additive APIs. | Candidate identity and results are recorded; older routes/contracts still work; no business-data rollback or destructive schema reversal is needed. |
| UX-AC-56 | Microsoft integration unchanged | Compare configuration, authorized synthetic command traces and provider-intent counts before/after the same workflows. | Entra identity, site automation, staff sync, folder/document/archive behavior and optional automation flags are unchanged; no new Graph permissions or delta worker. |


<a id="s14"></a>
## 14. Verification strategy

### 14.1 What this review established

The review inspected selected current source files, Angular templates/decoders, existing API definitions, approved policy records, Axelor reference views, and two relevant test bodies. It identified concrete owners and several presentation/definition issues worth addressing. It did **not** execute either application, verify deployed screenshots, inspect production data, run tests, measure performance, change tenant resources, or certify accounting/audit compliance.

The earlier supplied image links were not available as dependable visual evidence. Official Axelor source and documentation are the reference basis. Documentation version 8.5 and repository version 9.1.10 are deliberately distinguished; no claim is made that every manual screenshot matches the pinned source.

### 14.2 Focused tests first

| Changed boundary | Minimum credible verification |
|---|---|
| Presentation-only layout | Targeted template/component test when interaction changes; keyboard/narrow-layout inspection; unchanged action parity inventory |
| Query/filter/aggregate | Existing PostgreSQL-backed owner tests covering positive data, complete-versus-page scope, wrong firm/role, filter validation, concurrent observation and bounds |
| API/decoder | Targeted API response/authorization test and Angular bounded-decoder test; regenerate/verify OpenAPI only when affected |
| Existing protected form extraction | Existing positive workflow plus denial, stale revision, lost response, same-request recovery, and session invalidation tests |
| New drill-down | Exact measure-to-row cohort comparison plus destination authorization and direct-refresh behavior |
| Optional supporting index | Representative query-plan evidence and migration upgrade/compatibility check; no data mutation |
| Release candidate | Existing full unfiltered suites in CI/CD on the reviewed commit; no inference of full acceptance from fast CI alone |

The inspected `BillingWorkflow_SeparatesFinanceRoles_BalancesReceiptAndCredit_AndProtectsPostedHistory` and `PlanPinsPublishedTemplateAndSchedule_AndRecoversRetryWithoutDuplicateTasks` are useful starting points. Their bodies were read, not run. [AS21], [AS22]

Other candidate existing suites identified by the code map include `PracticeCrmTests`, `CommercialWorkflowTests`, `EngagementWorkflowTaskTests`, `WorkflowTemplateTests`, `FirmOperationsTests`, and related Angular commercial, proposal, finance, planning and workflow journeys. Resolve the current files and exact method names before selecting a cohort; map presence alone is not proof of coverage.

Do not add redundant unit/API/browser copies of the same assertion, remove difficult tests, weaken independent-review checks, or alter expected values to match a changed implementation. Acceptance outcomes derive from the existing approved behavior, not from the convenience of a new layout.

### 14.3 Execution example — only when implementation is underway

For a changed billing behavior requiring the inspected regression, a focused existing test can be selected as follows from an appropriately configured repository checkout:

```bash
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~BillingTests.BillingWorkflow_SeparatesFinanceRoles_BalancesReceiptAndCredit_AndProtectsPostedHistory'
```

This command is guidance, **not a test result**. Follow the checkout's current `AGENTS.md`, its pinned toolchain, and dedicated disposable PostgreSQL test configuration. Do not run against production or reuse stale binaries. Prefer the existing bounded `scripts/agent-check` cohort when it covers the change. Full local suites are not the default.

### 14.4 Browser verification and evidence

Use the available built-in browser to exercise changed screens with approved synthetic identities. Validate actual populated states, not just page headings. Include one ordinary user, one scoped/denied user, a preparer/reviewer pair, and applicable finance roles without assigning additional privileges simply to make the test pass.

Inspect keyboard interaction, focus after cancellation/refresh, mobile-width tables, long names/amounts, loading/error/empty states, and preserved network/recovery behavior. Record which routes/data states were actually visited and any console/network issues. A passing unit test or compile does not establish rendered behavior.

A minimal evidence entry contains story/scenario ID, candidate commit, changed owner, test or browser action, fixture/environment, expected result, observed result, and limitation. Record volatile run data only in the repository's designated execution ledger. Do not turn this specification into a duplicate historical status report.

<a id="s15"></a>
## 15. Rollout and rollback

### 15.1 Ordered delivery

| Stage | Work | Exit condition |
|---|---|---|
| 1. Inventory | UX-00: current-source parity, action/route/metric baseline, conflict review | No unclassified protected action, route, metric definition, or integration boundary |
| 2. Low-risk composition | UX-01 and focused invoice/analytics label work without changed formulas | Existing commands/results preserved; layout and keyboard checks pass |
| 3. Commercial discovery | UX-02–04: lead aggregates/filters, pipeline, proposal context | Scoped complete counts and exact proposal/conversion lineage demonstrated |
| 4. Work planning | UX-05–07: groups, resources, template/time navigation | Existing task identities, review inbox, capacity, preview/receipt and lifecycle separation demonstrated |
| 5. Finance discovery/reporting | UX-08–12 as applicable, with UX-09 safeguards | Balances, historical reports, financial roles, recovery and metric bases match the baseline |
| 6. Integrated acceptance | UX-13 on one candidate | Required exact-source CI, connected synthetic journeys, browser review and rollback evidence recorded |

Do not activate an existing disabled workflow/automation feature to make a new screen look populated. Do not ship seeded fake operational totals. An unavailable feature must remain truthfully unavailable, not be replaced by fabricated success.

### 15.2 Release controls

Use the repository's current deployment process and permissions. Select a controlled internal pilot cohort through existing deployment controls; do not introduce a general feature-flag platform or permanently duplicate old/new workspaces. Preserve old URLs and command behavior during the rollout.

For new read shapes, deploy backward-compatible server support and decoder-compatible UI in the appropriate order. A failed summary call must not clear an unresolved protected command or cause an automatic retry. Existing details remain the business authority while new discovery views are accepted.

No data migration should be required beyond an explicitly reviewed optional supporting index. Do not reprice historical records, backfill fabricated history, change approved templates, recalculate posted balances, rename canonical statuses, or reset provider verification while deploying this UX.

### 15.3 Rollback

A normal rollback restores the prior UI/read code while leaving additive APIs and harmless index support compatible. It must not undo business transactions performed through existing commands. Retain pending request IDs and receipt recovery compatibility across the release window.

Before releasing a component extraction, confirm the previous supported client can still use existing endpoint contracts. If that is impossible, redesign the change additively rather than requiring destructive schema reversal. New read-only projections do not justify a business-data restore.

Any deployment, merge, critical deletion, or production permission change follows the owner's designated authorization. `AUDITSPHERE-MERGE-APPROVED` is the known project merge confirmation codeword; it does not grant unrelated production or tenant authority. No such operation is authorized by creation of this document.

<a id="s16"></a>
## 16. Deferred changes and decision boundaries

| Topic | Disposition |
|---|---|
| Weighted pipeline forecasts, success scoring, expected close dates | Deferred. Existing `Probability` is not proof of an accepted forecast policy or end-to-end editor. Do not add fields/formulas speculatively. |
| Direct board drag/drop of stages, tasks, or allocations | Excluded from the core scope. It must not bypass existing reviewed commands; keyboard-safe inspection gives most of the benefit with less risk. |
| New free-form task hierarchy, custom statuses, automatic progress percentages | Excluded. Use existing phase/group/task data and derived lifecycle. |
| Full Gantt/critical-path scheduling | Deferred. Do not invent task date spans, adopt a new library, or change due-date/dependency rules to draw bars. |
| CRM entity merging or duplicate remediation | Separate data-integrity story only when requested and grounded in existing rules. No automatic merge here. |
| Additional communications, reminders, email delivery or calendar integrations | Excluded. Existing channels and dispatch semantics are preserved. WhatsApp remains its current source label, not a new connector. |
| Fee model changes, subscription billing, tax defaults, discounts or new automatic financial actions | Excluded. Preserve approved terms, finance controls and draft-only automation. |
| Recognized WIP, revenue recognition, global margin or profitability policy | Separate finance-owned decision. Do not promote charge-out or billed-less-staff-cost values into accounting facts. |
| Individual staff rankings or wider private-cost visibility | Excluded. Existing authorization and approved reporting bases remain controlling. |
| SharePoint manual-site mode, delta sync, worker retirement or archive changes | Explicitly excluded; the current functional-parity storage boundary remains untouched. |
| Java/Axelor installation, ERP module import, source/UI asset copying | Excluded. Independently implement the applicable concepts in the current AuditSphere stack. |
| New product modules, brokers, databases, microservices or general dashboard builders | Excluded. This is a bounded enhancement of existing owners. |

Routine engineering choices that preserve the contract can proceed under the project's current workflow. An unresolved professional or finance policy blocks only its dependent enhancement, not unrelated layout work. Do not invent a policy to avoid recording a blocker.

### 16.1 Risks and mitigations

| Risk | Required mitigation |
|---|---|
| Duplicate feature implementation because a prior audit was stale | Refresh targeted source first; reuse current owners, task views, unbilled report and receipt APIs. |
| New summaries reveal restricted records | Authorize before aggregate computation and reauthorize before response; test negative roles and scope-swapped identifiers. |
| Board implies workflow authority | Read-only grouping; named existing actions only; keep commercial stage, task state and professional gate distinct. |
| Metric relabeling changes business meaning | Preserve current calculations; declare numerator, denominator, date basis, currency and null rules. |
| Refactored form loses recovery or assent fences | One coherent pending-command owner; targeted lost-response and identity-invalidation regressions. |
| Large reports/pipelines degrade the application | Bounded database reads, complete honest summaries, measured indexes and preserved limits; no full browser mirror. |
| UX work accidentally modifies Microsoft behavior | Isolate affected files; explicit provider non-effect tests; no changes to Microsoft credentials, workers, storage or archive owners. |

<a id="s17"></a>
## 17. Agent execution contract

The following bounded instruction can accompany an implementation assignment:

> Implement only the assigned UX story from this specification in the existing AuditSphere stack. Read the current `AGENTS.md`, affected code-map entries, approved policy owners, and actual source/tests before editing. Compare the current commit with the reviewed baseline and reuse capabilities already implemented. Preserve all existing business actions, authorization, maker/checker and professional gates, exact financial calculations, request/receipt recovery, routes, and Microsoft/SharePoint behavior. Axelor is a concept reference only; do not introduce its runtime, modules, source, or assets. Start with the smallest coherent presentation/read-query change. Add no business table or dependency without an evidenced need and approval. Run the smallest relevant existing tests, then record observed results and deferred CI honestly. Do not change test expectations or permissions to make the UX pass. Stop only the dependent work when a genuine policy decision or external acceptance is missing; continue independent in-scope work. Report changed files, preserved actions, new read shapes, verification, limitations, and rollback. Do not merge, deploy, delete production data, or change tenant permissions without the owner's required authorization.

For each completed story, the delivery note must answer: **What changed? Which existing behavior was preserved? Which files/queries/actions were touched? What was actually verified? What remains unverified? How is the UI/read change rolled back?**

Do not report the entire practice-management system as complete because these UX stories pass. They do not close unrelated audit methodology, financial policy, storage custody, real-tenant, accessibility certification, or production-release gates.

<a id="s18"></a>
## 18. Source register and limitations

### 18.1 AuditSphere source evidence

The links below pin the inspected snapshot. Line windows describe the portions read in this review; they do not assert that every related file or behavior was inspected. Short names elsewhere in the document resolve to these owners or explicitly proposed additions.

| ID | Source / inspected range | Why it matters |
|---|---|---|
| [AS00] | Commit `89fa2f22706a630b7c9d923fc8e4878100dcb0e2`, metadata and change summary | Current storage functional-parity boundary; previous manual/delta proposal superseded |
| [AS01] | `AGENTS.md`, lines 1–155 | Architecture, minimal-change discipline, safety, tests and evidence rules |
| [AS02] | `docs/architecture/auditsphere-angular-conventions-current.md`, lines 1–155 | Existing Angular composition, tokens, forms, decoders and recovery conventions |
| [AS03] | `docs/architecture/auditsphere-architecture-code-map.md`, lines 280–375 and 390–560 | Existing capability owners and test/route navigation; not runtime proof |
| [AS04] | `src/AuditSphereOps.Ui/src/app/features/commercial/leads.ts`, lines 1–230 | Current lead UX and page-scoped qualification card |
| [AS05] | `src/AuditSphereOps.Application/Practice/PracticeLeadQuery.cs`, complete returned file | Current list/filter/role boundary and response fields |
| [AS06] | `src/AuditSphereOps.Ui/src/app/features/commercial/opportunities.ts`, lines 1–220 | Opportunity detail, history and command intents |
| [AS07] | `src/AuditSphereOps.Domain/Practice/Crm.cs`, lines 1–200 | Existing canonical states, opportunity/proposal fields and evidence identities |
| [AS08] | `src/AuditSphereOps.Ui/src/app/features/practice/work-queue.ts`, lines 1–250 | Existing alternate views, summaries, groups, dependencies and review inbox |
| [AS09] | `src/AuditSphereOps.Ui/src/app/features/practice/resources.ts`, lines 1–150 | Grid/decoder contracts, capacity data, current role boundary and planning state |
| [AS10] | `src/AuditSphereOps.Ui/src/app/features/practice/resources.html`, lines 1–155 | Current resource layout, Sunday calendar, safeguards and certificate labels |
| [AS11] | `src/AuditSphereOps.Ui/src/app/features/finance/invoice.ts`, lines 1–225 | Invoice amounts, terms, receipts/reversals, permissions and sent-evidence disclosure |
| [AS12] | `src/AuditSphereOps.Api/Ui/UiEndpoints.Finance.cs`, lines 1–155 | Existing finance/unbilled/receivables routes and request contracts |
| [AS13] | `src/AuditSphereOps.Application/Practice/FirmUnbilledTimeQuery.cs`, lines 1–190 | Existing bounded unbilled-time semantics, buckets and source reservations |
| [AS14] | `src/AuditSphereOps.Application/Practice/FirmReceivablesAgingQuery.cs`, lines 1–150 | Historical cutoff, invoice scope, terms and receipt/credit/reversal basis |
| [AS15] | `src/AuditSphereOps.Ui/src/app/features/finance/ledger.ts`, lines 1–160 | Existing finance route meaning, setup and protected ledger actions |
| [AS16] | `src/AuditSphereOps.Ui/src/app/features/practice/analytics.ts`, lines 1–230 | Current wide table, page/summary scope and operational measures |
| [AS17] | `src/AuditSphereOps.Application/Practice/FirmOperationsServices.cs`, lines 130–445 | Current analytics DTOs, authorization, formulas, basis definitions and null behavior |
| [AS18] | Same `FirmOperationsServices.cs`, lines 460–570 | Firm reporting definitions and expense ownership/evidence boundaries |
| [AS19] | ADR-0018 practice reporting bases, complete returned document | Approved utilization, time-value, receivables and monthly-report policy |
| [AS20] | ADR-0007 fee-invoice automation, complete returned document | Draft-only automation, eligibility and independent finance roles |
| [AS21] | `tests/AuditSphereOps.Domain.Tests/EngagementWorkflowPlanTests.cs`, lines 1–115 | Existing template/schedule/idempotency/dependency test body; not executed |
| [AS22] | `tests/AuditSphereOps.Domain.Tests/BillingTests.cs`, lines 1–120 | Existing role, balance, idempotency and immutable-posting test body; not executed |

### 18.2 Axelor reference evidence

| ID | Reference | Use / limitation |
|---|---|---|
| [AX01] | Official **Opportunities** manual, version 8.5 | Pipeline organization and linked commercial records; its workflow rules are not adopted |
| [AX02] | Official **Project management** manual, version 8.5 | Contextual project navigation and alternative task views; not authority for new AuditSphere modules |
| [AX03] | Official **Create an invoice** manual, version 8.5 | Grouped invoice information; accounting/advance treatment explicitly not imported |
| [AX04] | Official **Leads** manual, version 8.5 | Lead discovery/organization reference; not a replacement for local qualification rules |
| [AX05] | `axelor-crm/src/main/resources/views/Opportunity.xml`, lines 1–190, pinned 9.1.10 source | Concrete grid/form arrangement; no XML/code copying |
| [AX06] | `axelor-project/src/main/resources/views/Project.xml`, lines 1–140, pinned 9.1.10 source | Project/tree/navigation patterns; no project-state engine copying |
| [AX07] | Axelor master commit metadata for `e7cbea53bae88d2540f59a70b09bf0f326f1022e` | Reference snapshot identity, not a claim that it remains the latest release |

### 18.3 Remaining limitations

There was no full repository audit, production dataset inspection, application execution, visual baseline measurement, or complete review of every financial owner. The implementation must inspect the exact current route catalogue, serializers, query helpers, current financial fixtures, and any unexamined UI actions before extending them. The code-grounded findings above should not be generalized into claims that unrelated features are missing or defective.

This document is complete as a proposed implementation specification. Acceptance of the software remains contingent on actual evidence for the applicable stories and scenarios, without changing the protected project functionality.

---

## Reference links

[AS00]: https://github.com/nirzaf/AuditSphere/commit/89fa2f22706a630b7c9d923fc8e4878100dcb0e2
[AS01]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/AGENTS.md#L1-L155
[AS02]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/docs/architecture/auditsphere-angular-conventions-current.md#L1-L155
[AS03]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/docs/architecture/auditsphere-architecture-code-map.md#L280-L560
[AS04]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Ui/src/app/features/commercial/leads.ts#L1-L230
[AS05]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Application/Practice/PracticeLeadQuery.cs
[AS06]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Ui/src/app/features/commercial/opportunities.ts#L1-L220
[AS07]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Domain/Practice/Crm.cs#L1-L200
[AS08]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Ui/src/app/features/practice/work-queue.ts#L1-L250
[AS09]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Ui/src/app/features/practice/resources.ts#L1-L150
[AS10]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Ui/src/app/features/practice/resources.html#L1-L155
[AS11]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Ui/src/app/features/finance/invoice.ts#L1-L225
[AS12]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Api/Ui/UiEndpoints.Finance.cs#L1-L155
[AS13]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Application/Practice/FirmUnbilledTimeQuery.cs#L1-L190
[AS14]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Application/Practice/FirmReceivablesAgingQuery.cs#L1-L150
[AS15]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Ui/src/app/features/finance/ledger.ts#L1-L160
[AS16]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Ui/src/app/features/practice/analytics.ts#L1-L230
[AS17]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Application/Practice/FirmOperationsServices.cs#L130-L445
[AS18]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/src/AuditSphereOps.Application/Practice/FirmOperationsServices.cs#L460-L570
[AS19]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/docs/architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md
[AS20]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/docs/architecture/adr/auditsphere-architecture-adr-0007-fee-invoice-automation-drafts-only-approved.md
[AS21]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/tests/AuditSphereOps.Domain.Tests/EngagementWorkflowPlanTests.cs#L1-L115
[AS22]: https://github.com/nirzaf/AuditSphere/blob/89fa2f22706a630b7c9d923fc8e4878100dcb0e2/tests/AuditSphereOps.Domain.Tests/BillingTests.cs#L1-L120
[AX01]: https://docs.axelor.com/aos/en/aos/crm/opportunity/
[AX02]: https://docs.axelor.com/aos/en/aos/project_manage/create_project1/
[AX03]: https://docs.axelor.com/aos/en/aos/invoicing/create_invoice/
[AX04]: https://docs.axelor.com/aos/en/aos/crm/lead/
[AX05]: https://github.com/axelor/axelor-open-suite/blob/e7cbea53bae88d2540f59a70b09bf0f326f1022e/axelor-crm/src/main/resources/views/Opportunity.xml#L1-L190
[AX06]: https://github.com/axelor/axelor-open-suite/blob/e7cbea53bae88d2540f59a70b09bf0f326f1022e/axelor-project/src/main/resources/views/Project.xml#L1-L140
[AX07]: https://github.com/axelor/axelor-open-suite/commit/e7cbea53bae88d2540f59a70b09bf0f326f1022e
