# AuditSphereOps — Current State & Active Slice Handoff

**Status:** CURRENT

**Purpose:** Compact, authoritative handoff of the active implementation slice, recent verified changes, local environment state, and next actions.

**Authority:** Active execution handoff document. Volatile metrics (exact test counts, migration count, verified commit SHA, CI run IDs) belong exclusively to [`docs/execution/status.json`](status.json).

**Audience:** AI coding agents and human developers.

**Chronology:** Sections are newest first. An older section describes the state
when it was written; where a later section supersedes it, the later section is
current. The top-level `verifiedCommit` in `status.json` names the last commit on
which the full suite was run; per-slice records carry their own evidence.

> **Archive:** earlier slice sections, moved verbatim on 2026-10-08, are in [`auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md`](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). They are history, not current authority; the ledger is [`status.json`](status.json).

## Source-linked package build from the product (CP-11 accepted-source cash-flow subpath) — 2026-10-14

The engagement-scoped preparer screen lists approved mappings, finalized adjustment plans, reporting periods and validated comparative packages. It supports a paged accepted-ledger line picker and submits exact decimal strings to the durable `BuildFinancialPackage.v1` operation. Input discovery is scope-authorized and the server still validates the selected current/comparative package. The routes are `GET /api/ui/engagements/{id}/financial-package-inputs` and `POST /api/ui/accounting/packages/builds`.

The build screen now polls a bounded status receipt for the exact operation. Only its originator can read that receipt; it returns no payload or error detail, and the package identity appears only after confirmed completion. Terminal failures stop polling and remain visibly distinct from queued or running work.

The synthetic browser journey passed 1/1: it completed the worker build, showed the package link, denied another reviewer the originator-only receipt (403), denied preparer source review, and allowed independent approval of persisted GL links. Cash, equity, note and adjusted trial balance cross-cast at six decimals. Angular build passed with the existing `commercial/settings.scss` budget warning; specs passed 722/722 across 127 files. Release build passed with 0 warnings/errors; OpenAPI drift passed. Exact-head CI [38598712675](https://github.com/nirzaf/AuditSphere/actions/runs/38598712675) passed Docs, Angular, 141 Domain + 8 API checks and EF drift; its E2E cohort passed 12/15, including this journey. Two bundle failures repeat baseline run 38592122928; one legacy activation `canonical:false` case failed only here. See `status.json` under `targetedVerification.currentCashFlowPackageJourney.

This remains one CP-11 cash-flow build subpath, not completion of CP-11 or the attached story. The manual-evidenced path, equity/note source lineage, other required source/review paths and remaining CP-11 acceptance are still open. CP-12 is blocked on the approved indirect cash-flow policy reference and qualified interpretation of reviewed assessed-zero FX states; no accounting treatment is inferred here. Full Domain/API/E2E suites were not run locally; CI ran only the focused cohorts described above and is not green because of the listed browser failures.

The branch also includes the upstream repair for five Domain tests that failed on master (four package-review fixtures now seed the canonical validation population, and the FX test numbers cross-cast to adjusted cash). The recorded focused run predates this follow-up and is retained in [`status-archive-2026-10.json`](status-archive-2026-10.json); it is not final-head evidence.

## Accepted GL discovery for schedule preparation — 2026-10-14

The specialist preparation workspace now exposes a bounded view of the selected independently accepted GL for its exact reporting period. Application authorization precedes source discovery; period/currency/sealed identity are checked, source selection and generation are checked again after reading, and signed functional amounts remain exact decimal strings. Source and line identities are visible with paging. Period changes hide prior source content; a mismatched period is refused.

This is CP-11 source discovery, not persisted monetary allocation or independent schedule review. It does not satisfy final cash/equity source-lineage readiness. Indirect cash-flow roles, assessed-zero review and source-backed schedule bindings remain outstanding. The isolated synthetic built-in-browser fixture exercised paging, signed amounts and period clearing/refusal; no live tenant or development database was changed. Exact checks and omissions are in `status.json`.

## Consolidated practice/reporting intake and mandatory-rule readiness — 2026-10-14

The supplied completion story extends the reporting handoff using consolidation aliases over the existing board; it does not approve methodology or create another lifecycle. Its inherited reporting criteria were compared with the preceding source without changes. Current HEAD already contains the Qatar working-week analytics fixture and audit-submission fence corrections; neither was overwritten.

The existing canonical package rule population now has one shared completeness evaluator. Build, review, management presentation, staff queue, current-release checks and seal status/action refuse absent, failed or ambiguous mandatory results. Scope is exact and review diagnostics follow authorization. Draft rendering and stored historical evidence remain available. Focused tests include all mandatory omissions/failures and a complete-format package with no failed row but a missing result; CI includes these tests.

CP-17 remains partial: a versioned final profile, actual cash/equity source resolution, reviewed disclosures, exact formatted composition decisions and the full browser journey remain pending. No professional or external gate was approved and no persistent database or deployment changed. Exact candidate fingerprints, checks and omissions are in `status.json`.

## Supplementary schedule provenance in pinned reporting — 2026-10-14

The presentation source manifest now binds the persisted current/comparative cash-flow, FX and equity rows, signed amounts, scope/currency/date/classification fields, cash populations and opening/closing evidence. Pin creation, read and freshness checks use the same versioned computation; the legacy contract field name remains unchanged. Old pins need explicit repinning to satisfy the strengthened freshness check; stored historical artifacts remain available.

This is identity protection, not accepted-source resolution, allocation validation or independent professional review. The source-backed schedule editor and full reporting journey remain incomplete; professional gates remain closed. No migration, deployment or UI changes were made. The subsequent dependency correction pins the already-used PKCS library to its patched version; NuGet audit enforcement is retained. Build/check evidence is tracked separately. Focused evidence and omissions live in `status.json`. Uncommitted audit-procedure concurrency work remains separate.

## Trial balance-to-report intake and signed equity — 2026-10-14

The attached AS-FR-CLOSE-001 story is mapped in [the delta review](auditsphere-execution-tracker-trial-balance-report-current.md), using existing task and methodology authorities. Current/comparative equity checks now preserve signs rather than accepting opposite amounts by magnitude. Focused presentation and retained AUD-04 regression checks passed; exact evidence is `targetedVerification.trialBalanceReportSignedEquity` in [status.json](status.json).

This is not full reporting completion. T003's qualified methodology/independent-review evidence is absent; draft/source-lineage warnings and existing review order remain. Source bindings, indirect cash flow, reviewed notes, exact final composition/decisions and full browser/live acceptance remain open. Pending procedure submission-lock changes from the interrupted workflow task are retained separately and are not part of this reporting delta.

## Task/professional boundary — 2026-10-13

Completing every task in a published pinned plan still refuses activation without Partner acceptance and creates no professional approval, opinion, release or freeze evidence. Template/plan checks passed; see `targetedVerification.workflowTemplateProfessionalBoundary` in [status.json](status.json). TASK-01 remains partial. Operational `RequiredTask` is not a professional gate.

## Release bundle integrity — 2026-10-13

Assembly rechecks signing policy and matching certificate on new creation/replay, including later certificate-required policy. All stored artifact hashes, including the representation template, are checked. Web forwards host policy; downloads remain scope checked. Focused Domain, Angular/reference journeys and built-in browser checks passed: ZIP/certificate integrity, lineage, stale sources and bounded correspondence with injected appends. Evidence: `targetedVerification.releaseBundleSigningReplay`, `releaseBundleCorrespondence` and `releaseBundleRaceAndAngular` in [status.json](status.json). Further adverse cases, full release and operator rotation review remain open. No global policy or persistent database changed.

## Reviewed task dependency amendments — 2026-10-13

WF-08 now supports manager-reviewed required/withdrawn task edges as append-only amendments over the original pinned template graph. Both task revisions, exact engagement scope and a reason are bound to the actor-owned idempotency receipt. The complete effective graph is serialized and checked for cycles, including opposite-edge races; PostgreSQL also rejects raw cyclic inserts and history mutation. Queue readiness and both completion routes use the same effective graph. No professional approval or pinned template row changes.

The Angular ordering panel provides exact review and assent, receipt-only recovery for lost replies, explicit dropdown labels and session/scope clearing. The focused Domain/API/Angular/browser batch passed after correcting analyzer assertions and a label lookup issue. Built-in-browser inspection confirmed a reviewed withdrawal appends history and restores only the operational action. Exact counts, source hash, artifacts and limitations are under `targetedVerification.workflowTaskDependencyAmendments` in [status.json](status.json). No persistent database was migrated. Template-declared domain rules, other workflow gaps and full release/external/professional acceptance remain open.

## Live domain evidence prerequisites — 2026-10-13

WF-08 task prerequisites now reference current independently approved materiality, an exact accepted/closed PBC request or a current independently reviewed procedure. Satisfaction is evaluated live; no editable satisfaction flag exists. Scoped managers review revision-fenced changes with reasons and recover uncertain replies by reading their actor-owned receipt. Append-only policy history and exact source foreign keys are database-enforced; both workflow and legacy task completion honor required evidence. PBC waits feed the client-wait queue; stale procedure generations and open review notes remain blockers. Expired role grants no longer elevate task completion.

The focused .NET, Angular and owned browser batch passed after correcting synthetic fixture/assertion issues. Built-in-browser inspection also confirmed a reviewed withdrawal changes only operational policy and retains the unsatisfied professional source. Exact counts, source digest, artifacts and migration evidence are in `targetedVerification.workflowDomainEvidencePrerequisites` in [status.json](status.json). No persistent database was migrated. WF-08 remains partial: template-declared domain rules and safe manual task-edge amendments are next; broader workflow, release and external/professional acceptance remain open.

## Agent capability evidence and pilot comparison — 2026-10-13

The existing [capability runner](../testing/auditsphere-testing-guide-agent-capability-checks-current.md) maps acceptance to named checks, exposes synthetic actors/states and omitted evidence, parses sanitized assertion/request-ID context, and detects HTML/SCSS/new-file changes. Explicit browser execution builds the current UI first; reports cannot overwrite source or prior evidence. Matching-cohort comparisons retain missing measurements instead of inventing gains. Focused UI/browser, authorization/API and calculator pilots passed; source hashes, the resolved socket-denial attempt and limitations are under `targetedVerification.agentCapabilityRunner` in [status.json](status.json). Comparative productivity measurements, broader release checks and external/professional acceptance remain open.

## Reviewed annual template reference — 2026-10-13

Annual preparation suggests the current published revision of a prior plan’s template. The optional choice is retained in the exact creation preview and immutable receipt, then preselected in the new engagement’s task-plan screen only while current. An archived/superseded reference blocks new creation; the plan still requires separate staffing/schedule review and assent. Prior plans are unchanged. A CI-discovered assessment receipt timestamp mismatch is corrected by returning the persisted row on first publication. Evidence is under `targetedVerification.workflowAnnualTemplateReference` in [status.json](status.json). Nonstandard calendars, populated-mapping browser acceptance and wider workflow/release gates remain open.

## Annual checklist handoff — 2026-10-13

Annual receipts now open the exact new engagement in the existing client assessment. The query confirms client ownership, shows whether a fresh evaluation is needed, and retains parent-client authorization. Opening a continuance review still requires reviewed assent; delta answers start empty and the Partner decision editor fixes the exact engagement and service. Period/revision changes invalidate scoped decision previews under a shared engagement lock. Prior decisions and answers remain history; activation stays separate. Evidence is under `targetedVerification.workflowAnnualChecklistHandoff` in [status.json](status.json). Template preselection, populated-mapping browser acceptance, nonstandard calendars and broader acceptance remain open.

## Annual reference metadata — 2026-10-13

Annual preparation suggests active client contacts valid at the next-period start and approved mappings from the exact prior engagement and period. Accounting metadata requires source-engagement accounting read authority; RelationshipManagers receive a restricted state. References are bounded and never copy allocations, routing, access, consent or approvals. Evidence is under `targetedVerification.workflowAnnualReferenceMetadata` in [status.json](status.json). Fresh-checklist orchestration and broader acceptance remain open.

## Agent capability tooling and bounded analytics — 2026-10-12

The [capability runner](../testing/auditsphere-testing-guide-agent-capability-checks-current.md) connects three task packets to owning files, invariants, fixtures and focused checks. Socket denial is distinct from database outage; default preflight changes no configuration. Comparative productivity measurements remain pending.

Engagement economics now pages before loading financial evidence, with Angular and legacy navigation. Period-wide task and utilization denominators stay unchanged. The owned synthetic API/browser fixture verified forward/reset navigation; focused PostgreSQL assertions also cover queue assignment narrowing. Evidence and limitations are in `targetedVerification.workflowAnalyticsPaging` and `agentCapabilityRunner` in [status.json](status.json). Overall workflow and release acceptance remain partial.

## v2.2 unlinked procedure review inbox — 2026-10-12

The staff work queue now has a separate, read-only inbox for current submitted procedure results that have no operational task linked anywhere to that procedure. The query enforces current reviewer role grants, exact client/engagement scope, session freshness, reviewer independence and staffing hierarchy. Results already represented by a linked rework task stay in the task views instead, including after resubmission. The inbox links to the existing fieldwork workspace; that workspace and the review command remain authoritative for planning generation, risk basis, holds and final review eligibility. No task, review decision or migration is created by this query. `WorkflowExperience:Enabled` remains default-off.

The focused PostgreSQL regression passed 1/1 and covers an unlinked result, preparer denial, an existing task link and a linked resubmission. API and OpenAPI emitter Release builds passed with offline NuGet vulnerability-feed warnings; fresh OpenAPI generation and drift verification passed. Angular's direct compiler/template check passed. The Angular production build aborts with exit 134 immediately after “Building…” and no diagnostic; authenticated browser rendering was not verified. No full suite, persistent database migration, live provider or professional acceptance is claimed. Evidence is in `status.json` under `targetedVerification.workflowReviewInbox`.

## Annual workflow — period-bound commercial acceptance — 2026-10-12

The engagement lifecycle and Dual-Key readiness now resolve a proposal only when both its opportunity and proposal match the engagement's exact service route and financial period. An engagement-linked fee agreement remains authoritative, but its proposal must also match those exact facts. A prior-year accepted offer therefore cannot make a new-period engagement appear ready for advance billing. No schema change or migration was needed. The existing Partner risk-clearance model remains client-level and generation-bound; annual engagement creation and fresh year-specific continuance remain open work.

The focused PostgreSQL regression passed 1/1 for a new-period engagement facing a fully accepted prior-period offer; it remains in Proposal Generation. The test build completed with offline NuGet vulnerability-feed warnings. The regression is included in the focused CI filter, and commits are pushed to `master`; the GitHub run-list API was unreachable, so the CI outcome is unobserved. No UI was changed, no persistent database was migrated, and no full suite or live/professional acceptance is claimed.

## Under Review queue and regression CI coverage — 2026-10-12

WF-07 now derives the **Under review** queue state from a linked changes-required procedure result followed by the exact next submitted revision. An independent review decision clears the operational task from this view; completing the task does not alter audit-review authority. The focused PostgreSQL regression covers the full transition. The existing PostgreSQL CI filter now includes that regression. Commits are pushed to `master`; the GitHub run-list API could not be reached, so CI outcome is unobserved. `WorkflowExperience:Enabled` remains false by default, and no additional migration was introduced.

## v2.2 workflow analytics — client wait — 2026-10-12

Practice Analytics measures PBC wait from provider-accepted email to recorded client message, acknowledgement/resubmission or upload. Queued, failed, uncertain and reminder mail do not start/reset; provider acceptance is not inbox delivery. The period filters starts, responses are observed through query time, and terminal requests without response evidence are excluded.

Caps are 5,000 sends, 2,000 requests and 20,000 events; exceeding them yields unavailable, never partial totals. Acknowledgement/resubmission appends `CLIENT_ACTION`; migration remains unapplied.

Focused PostgreSQL 3/3 and Angular 5/5 passed; API/UI builds, EF and OpenAPI passed. Browser reached sign-in guard (local API unavailable). No full suite or live acceptance; WF-14 remains PARTIAL.

## v2.2 workflow activity — task event trace — 2026-10-12

WF-14 now persists append-only `Operational Task` activity for workflow-task creation, initial assignment, assignment, reassignment and completion. Each event links the exact existing `WorkTask` and `WorkflowTaskBinding`, scope, actor, optional assigned staff member, task revision and timestamp. Events are written in the same transaction as the successful task command; idempotent replays and refused/no-op commands do not create duplicate events. PostgreSQL rejects event updates and deletes and rejects inserts whose work task does not match the referenced binding. The timeline query reauthorizes the current engagement grant, caps date filters at 90 UTC days, uses keyset pagination and returns no task title, free-form note, risk evidence or audit conclusion from the event table.

The staff work queue now has a per-task activity panel. This increment covers only operational task events and labels them `Operational Task`. Template publication already has its own append-only decision records; template/reminder/automation events are not yet projected into a unified timeline. Reopened/rejected tasks and due-date changes have no activity coverage here. Client-wait metrics, other source labels, broader analytics pagination/performance evidence, and remaining WF-14 stories remain open. Additive migrations `20261012060111_WorkflowTaskActivity` and `20261012061421_WorkflowTaskActivityBindingGuard` have not been applied to a persistent database.

Verification on the working tree based on `445e1d99`: API Release build passed with 0 errors and two offline `NU1900` warnings; the focused PostgreSQL workflow-task activity regression passed 1/1, covering create/assign/reassign/complete, idempotent replay, timeline paging, foreign-scope and revoked-grant denials, database update/delete rejection, and mismatched task/binding insert rejection; the Angular activity contract spec passed 3/3; the development server compiled; EF model drift reported no pending changes; OpenAPI drift passed. The production Angular build aborted with exit 134 and no diagnostics. The built-in browser showed the staff sign-in guard because the local API/session was unavailable, so authenticated activity rendering was not observed. No full suite or persistent database migration was run. Exact evidence is in `status.json` under `targetedVerification.workflowTaskActivity`.

## v2.2 workflow analytics — phase task rollups — 2026-10-12

Practice Analytics now groups non-cancelled tasks due in the selected period by their current workflow-phase binding, with a separate unassigned-phase row. It reports due, completed, open, overdue and completion percentage for all seven phases. Completion requires both the completed task status and its persisted completion timestamp. The UI and metric definition state that these operational counts do not establish phase or audit-gate readiness. No migration was needed.

Verification on the working tree based on `5de433ff`: the Release solution build passed with 0 errors and six offline `NU1900` vulnerability-feed warnings; the focused PostgreSQL analytics regression passed 1/1, including bound and unbound task counts; the Angular analytics contract spec passed 4/4; EF model drift reported no pending changes; OpenAPI drift passed unchanged; and the Angular development server compiled. The built-in browser reached the staff sign-in guard because the local API/session was unavailable, so authenticated data rendering was not observed. The full test suite and production Angular build were not run. No persistent database migration, live-provider or professional acceptance was performed. Exact evidence is in `status.json` under `targetedVerification.workflowPhaseRollup`.

WF-14 remains partial: client wait, additional append-only activity sources, analytics pagination/performance acceptance and the remaining workflow stories are open. No persistent database was migrated.

## v2.2 workflow analytics — procedure review turnaround — 2026-10-12

Practice Analytics now shows average elapsed hours from submission of the exact procedure-result revision to each append-only review decision created in the selected date range. Both `REVIEWED` and `CHANGES_REQUIRED` decisions count. Revision mismatches and decisions timestamped before submission are excluded and counted; an empty valid sample is `n/a`. The query returns aggregate counts only and does not expose reviewer comments or names, change review state, or imply review quality or audit clearance. A `(FirmId, CreatedAt)` index supports the bounded review-period query; migration `20261012052354_WorkflowReviewAnalyticsIndex` is additive and has not been applied to a persistent database.

Verification on the working tree based on `690e1c11`: API Release build passed with 0 errors and two offline `NU1900` warnings; the focused PostgreSQL analytics regression passed 1/1, including both review outcomes, date filtering, invalid chronology and revision mismatch; the Angular analytics contract spec passed 4/4; EF model drift reported no pending changes; OpenAPI drift passed unchanged; and the Angular development server compiled. The built-in browser redirected the analytics route to the staff sign-in guard, so no authenticated data rendering was observed. The Angular production build aborted with exit 134 and no diagnostics. No full suite, live-provider or professional acceptance was run. Exact evidence is in `status.json` under `targetedVerification.workflowReviewTurnaround`.

The phase task rollups are recorded in the newer section above. WF-14 remains partial: client wait, additional append-only activity sources, analytics pagination/performance acceptance and the remaining workflow stories are open. No persistent database was migrated.

## v2.2 workflow analytics — task completion and workload — 2026-10-12

The existing bounded Practice Analytics view now reports completed applicable tasks and a separate completion percentage beside on-time, late and overdue counts. The denominator is non-cancelled tasks whose due dates fall within the selected date range; completion requires both `COMPLETED` status and a persisted completion timestamp. Tasks without due dates are excluded, and an empty denominator reports `n/a`. This is operational progress only; it does not satisfy an audit gate, change lifecycle state or produce accounting values.

Verification on the working tree based on `dee458d3`: Release solution build passed with 0 errors and six offline `NU1900` warnings; the targeted Angular analytics contract spec passed 2/2; the focused PostgreSQL analytics regression passed 1/1; OpenAPI drift passed with no contract change. No full suite or Angular production build was run for this slice. The run summary is in `status.json` under `targetedVerification.workflowExperiencePrH`.

The same firm-wide analytics query now groups non-cancelled tasks due in the selected period by staff assignee and shows due, completed, open and overdue counts. Unassigned tasks and assignments that do not resolve to a staff user have separate buckets. Staff names are returned only to the endpoint's existing firm-wide Partner, Administrator and FinanceManager audience. This measure is a task count, not capacity or effort; it does not change task state or audit readiness.

Verification of the workload slice based on `0afdaf9a`: the focused PostgreSQL analytics regression passed 1/1 after an initial sandbox socket denial and elevated rerun; the Angular analytics contract spec passed 3/3; the OpenAPI-emitter Release build passed with 0 errors and two offline `NU1900` warnings; its generated contract matched the committed file. The built-in browser opened the local preview but reached the staff sign-in guard, so the analytics page was not authenticated or visually verified. No full suite or production Angular build was run for this slice.

Per-phase task rollups, client wait, append-only workflow event coverage and broader analytics pagination/performance evidence remain open. Overall WF-14 and v2.2 acceptance remain PARTIAL.

## v2.2 final-release evidence projection — 2026-10-12

The engagement completion query now projects the latest release candidate together with its exact external checkpoint and persisted release row. The view reports candidate and target revisions, manifest SHA-256, checkpoint identity/state/verification time, whether the checkpoint revision and read-back digest match the candidate, and whether the persisted Release identity, checkpoint, target revision and manifest agree. It labels a missing issue `NOT_ISSUED`, a fully matching persisted issue `ISSUED`, and inconsistent state `RECONCILIATION_REQUIRED`. The Angular completion screen displays these distinctions and explicitly says that a candidate is not a release. This is read-only; it adds no release or approval command and no migration.

Verification on the working tree based on `87df1a24`: Release solution build passed with 0 errors and six offline `NU1900` vulnerability-feed warnings; the completion Angular contract spec passed 12/12; the focused PostgreSQL projection test passed 1/1; OpenAPI drift passed with no contract change. The Angular development server compiled the routes, but the built-in browser reached the staff sign-in guard because no local API session was available. The Angular production build aborted with exit 134 and no diagnostics. No full suite, persistent migration, signed-in browser workflow, live provider check or professional acceptance was run. Run results are recorded under `targetedVerification.workflowExperiencePrH` in `status.json`.

This only improves visibility of locally persisted release evidence. Issuance still requires the current approval and verified external checkpoint read-back; live delivery and SharePoint/provider acceptance remain external. Overall WF-13 and v2.2 acceptance remain partial.

## v2.2 Manager Review and rework follow-up — 2026-10-12

`ManagerReviewSummaryQuery` adds a read-only, scope-authorized summary over the existing fieldwork completion evaluation. It reports redacted blockers, procedure and evidence counts, unresolved threads, open confirmations, reconciliation/difference work, and whether the latest Summary Review Memorandum is current. `PartnerHandoffReady` requires both current completion readiness and a current SRM; this projection does not record a review decision, opinion, or lifecycle transition. `GET /api/ui/engagements/{id}/manager-review` serves the summary, and the Angular fieldwork page shows it only to actors who can review selections.

When `WorkflowExperience:Enabled` is true, an authorized `ChangesRequired` review creates one idempotently keyed `WorkTask` and workflow binding in the same transaction as the append-only review decision. The task is tied to the exact procedure/result revision and goes to the prior preparer only while that user remains eligible; otherwise it is unassigned for manager routing. No notification is sent. No migration was added for this follow-up.

The two focused PostgreSQL regression tests passed 2/2, including the returned-work blocker, stable redaction, linked task, prior-preparer assignment and duplicate-review refusal. The API Release build, Angular production build, EF model-drift check and OpenAPI contract drift check passed; the .NET build emitted two `NU1900` vulnerability-feed warnings because NuGet was unreachable. The built-in browser could not load the local fieldwork route because the local Angular server was no longer listening; the existing local tab reached only the staff sign-in guard. No authenticated browser journey or full suite was run. Exact commands and results are in `status.json` under `targetedVerification.workflowManagerReviewPrF`.

This is a partial workflow increment. The canonical lifecycle stage does not regress to Fieldwork on rework; the summary flag does not change lifecycle gates; procedure-level assignment/resubmission restrictions and task notifications remain open. Queue filters for rework and waiting-for-client, evidence dependency evaluators, Kanban/calendar views, and task-plan authoring remain open. The workflow feature stays default-off; no live or professional acceptance is claimed.

## v2.2 workflow experience, PRs D–F — 2026-10-12

WF-04 keeps `WorkTask` as the one operational task/status record and binds optional workflow metadata one-to-one. Existing `TimeEntry` lineage remains attached to the same task. The default-off API exposes scoped task create/read/assign/reassign/complete; creation uses a stable idempotency key and immutable digest, and revisioned actions reject stale requests. Checklist completion does not advance the canonical audit lifecycle.

WF-05 adds a firm-scoped template catalog with immutable revisions, dependency-cycle validation, independent review, publication/supersession and archive evidence. WF-06 adds a preview/execute/receipt path that pins the exact published revision, engagement generation, staffing snapshot and statutory schedule basis. Instantiation creates existing `WorkTask` records and bindings in one transaction. Concurrent same-request execution creates one plan; an ambiguous retry recovers its original receipt even after the schedule changes. A new published template version does not mutate a prior engagement plan. Plan receipts are append-only in PostgreSQL.

PR-F partially advances WF-07/08. `WorkflowTaskQueueQuery` returns My Tasks, Team, Overdue and Completed views scoped from current unexpired role grants, with stable keyset paging and task-owner, FSLI and mapping lineage. The Angular `My work` screen is at `/ui/app/work`. Plan execution now records append-only dependency edges only when they match the exact pinned template steps; task completion is refused until each `TASK_COMPLETED` predecessor is complete. These edges remain operational checklist controls and do not set audit review, lifecycle or approval states. The newer Manager Review follow-up above adds a read-only handoff summary and feature-flagged rework task creation. Waiting-for-client, audit-review and rework queue filters, domain-evidence dependency evaluators, Kanban/calendar views and task-plan authoring screens remain open.

Migrations `20261012000128_WorkflowTaskBindings`, `20261012003440_WorkflowTemplateCatalog`, `20261012004124_WorkflowTemplateOpenRevision`, `20261012010500_WorkflowTaskPlanReceipts` and `20261012015133_WorkflowTaskDependencies` are additive and were not applied to any persistent database. The focused queue/plan PostgreSQL cohort passed 3/3; the API-host OpenAPI contract test passed 1/1; the API Release build passed with two NU1900 vulnerability-feed warnings; EF model drift and OpenAPI drift passed. Angular development serving compiled successfully. The built-in browser loaded the app and showed Access unavailable / Sign in because no local API session was configured; no authenticated queue journey was run. Exact provenance is in `status.json` under `targetedVerification.workflowTaskQueuePrF`.

`WorkflowExperience:Enabled` remains false by default; migrations are not applied to a persistent database. The queue route is present, but local browser access stopped at the staff sign-in guard, and no authenticated queue command was exercised. WF-07/08 and WF-12 remain partial. WF-09/10 automations and PBC follow-up, WF-11 annual recurrence, WF-13/14 completion/archive cards and analytics, the lead-level journey, and WF-15 production acceptance remain open. No live tenant or professional acceptance is claimed.

## Exact pinned financial statement drafts — 2026-10-12

The FSR presentation slice now saves an immutable draft revision against the exact package inputs, published layout version/hash, comparative package/hash, note-source manifest, builder/renderer versions and display policy. Repeating the same pin request is idempotent. PDF, DOCX and XLSX bytes are stored as package artifacts and linked to that revision; repeat downloads reuse the stored bytes. A source change makes the pin stale: the historical rendered bytes remain retrievable, while an unrendered format cannot be created from the stale snapshot. The package seal manifest records pinned presentation identity and artifact hashes without changing the draft's `UNAPPROVED_DRAFT` status or treating it as an approved/issued statement. Cross-casts for current and comparative note amounts are checked against their own package balances.

Observed on this uncommitted working tree based on `00376915`: the focused PostgreSQL Domain regression passed 30/30; the Angular presentation spec passed 4/4; the API and OpenAPI emitter Release builds passed; EF model drift reported no pending changes; and the OpenAPI contract was regenerated. Angular production build passed using the SQLite cache backend, with the existing `commercial/settings.scss` budget warning. `status.json` records the focused commands and provenance. No full suite or signed-in browser workflow was run for this slice. No development or production database was migrated and no live financial data was changed.

This remains a partial, unapproved draft capability. The manifest preserves source identities, but preparer-entered references are not independently resolved to ledger evidence. Professional framework/display policy approval, the approved golden fixture, full note narratives and disclosure schedules, review and authorization, audited report assembly, signing/legal acceptance, release/archive and production acceptance remain open. Full cash-flow and equity completeness also remains gated by approved methodology and source lineage.

## Dedicated client SharePoint sites default rollout — 2026-10-12

`ClientSites:Enabled` now defaults to `true` in the API, worker and Hostinger Compose stack, with an explicit false override. The rollout still requires one administrator-approved UTC `ClientSites:ClientsCreatedAfter` boundary; an absent or invalid boundary never becomes the beginning of time. Historical clients before the boundary are not selected or migrated. The `client-sites` worker starts as an isolated group and reports blocked readiness when its configuration, certificate, tenant consent or rollout boundary is unavailable.

The API and `worker-pbc` receive the same cutoff. Repository resolution blocks shared-site folder creation for clients covered by the enabled or already-persisted cutoff until a dedicated site is verified, including while the dedicated-site worker is blocked and before it can persist the cutoff. Invalid/mismatched settings block dedicated-site discovery; a saved boundary remains authoritative, and conflicting saved values use the earliest recorded boundary for no-fallback enforcement while earlier clients retain their repositories. When the policy is enabled but neither a valid configured nor persisted boundary exists, workspace review/provisioning now also fails closed instead of routing an unclassified client to the shared site; setting an explicit cutoff or explicitly disabling the policy is required before shared-site provisioning. A verified dedicated site continues to serve its client. Automatic discovery remains idempotent per client, and the existing approved yearly engagement templates remain in use. Administration now shows the effective policy, cutoff, counts, tenant consent, worker heartbeat, site permission evidence, per-client site and membership states, durable operation diagnostics, and the existing Operations recovery route.

The heartbeat migration is part of the source tree and model snapshot. The latest grouped follow-up on 2026-10-12 passed the focused Domain cohort 37/37, workspace provisioning API tests 4/4, PostgreSQL-backed Playwright journey 1/1 and Angular workspace administration spec 4/4. The Release test builds emitted NU1900 warnings because `api.nuget.org` was unreachable; the tests themselves passed against isolated synthetic fixtures. These results are from an uncommitted working tree based on `00376915`, which also contains separate in-progress financial statement presentation edits; the full Domain, API and E2E suites were not run. Markdown health, filename and narrative-metrics gates passed. A read-only built-in Chrome check on 2026-10-12 loaded the acceptance tenant page and rendered the Dedicated client SharePoint sites panel, with no client-site rows. The page did not identify its deployed source commit, so this does not prove the current source revision is deployed. No development, acceptance or production migration was run; no real client site was provisioned and no staff membership reconciliation was performed. Exact commands and provenance are in [`status.json`](status.json). Live acceptance still requires identifying the deployed revision, applying the approved UTC cutoff consistently to API and worker configuration, and verifying a synthetic new-client site and staff membership reconciliation in the real tenant.

## Dynamic statement note references — 2026-10-11

Statement face-line note references now separate stable persisted note codes from display ordinals. The presentation assigns numbers from each code's first face-line position in the published layout, then uses the same number on each linked face line; reordering supplied note rows does not change numbering. Blank/oversized logical IDs and duplicate case-normalized (ID, face destination) pointers are refused. The presentation manifest builder version is v4. A failed `NOTE_CONTENT` check and warning state that the persisted data contains only cross-cast amounts, without ordered narrative, typed note tables or comparative note values. The output remains `UNAPPROVED_DRAFT`; no note content or accounting policy is invented.

Observed: the focused Domain and Angular presentation tests passed. The API Release build passed with NuGet vulnerability-feed warnings; Markdown health, filename, narrative-metric, UI inventory, status JSON and diff checks passed. The OpenAPI schema shape is unchanged by the note-number value update; its drift gate passed after the preceding presentation slice and was not rerun here. No migration or business-data change was made. FSR-008 remains PARTIAL: disclosure applicability, ordered note content, prior schedules, PPE/expense schedule completeness and seal-time pointer checks are open.

## Cross-statement readiness checks and export diagnostics — 2026-10-11

The statement presentation now cross-casts current and comparative equity rollforwards to mapped profit or loss, OCI and SFP balances. Cash-flow readiness requires operating, investing and financing amounts, validates current and comparative bridges independently, and carries the package's persisted `CASH_FLOW_RECONCILED` check, which also validates the selected closing-cash population. PDF, DOCX and XLSX all include the same per-check `PASS`/`FAIL` diagnostics in the presentation basis. Builder version is v3. Missing comparative schedules and incomplete source data remain explicitly failed or unavailable; output remains `UNAPPROVED_DRAFT`.

Observed: the focused Domain and Angular presentation tests passed; the API Release build passed with NuGet vulnerability-feed warnings; Markdown health, filename, narrative-metric, status JSON and `git diff --check` gates passed. The .NET test host initially could not bind its runner socket in the sandbox; the elevated retry ran and passed the focused suite. No full suite or PostgreSQL-backed integration test was run. A signed-in browser export journey was unavailable; the local UI had previously stopped at its unauthenticated access gate. No migration or business-data change was made. FSR remains PARTIAL; this slice does not provide source-record lineage, professional policy approval, complete notes/disclosures, final composition, sealing, signing acceptance or release.

## Cash-flow and equity schedules in statement presentation — 2026-10-11

Working-tree follow-up based on `3012b495`. The typed statement presentation now renders the persisted cash-flow movements and equity rollforward beside the SFP and profit-or-loss statements. PDF, Word and Excel exports use the same model. Cash bridge totals exclude noncash items and remain unavailable when an operating, investing, financing or FX input is missing; a supplied zero FX effect is distinct from an absent effect. Comparative schedule amounts remain unavailable when the comparative package has no matching rows. The builder refuses oversized supplementary schedules rather than silently truncating them.

The output remains `UNAPPROVED_DRAFT`. Readiness checks expose missing schedules, an unreconciled cash bridge, absent comparatives, preparer-entered references without source resolution, and equity not cross-cast to the SFP. This does not add source-record lineage, pin profit-transfer treatment, approve policy, or complete composition, sealing, signing or release. No migration or persisted business-data change was made.

Observed: `FinancialStatementPresentationTests` passed 16/16; the targeted Angular presentation spec passed 4/4; the API project build passed with two `NU1900` vulnerability-feed warnings; and the Angular production build passed with the existing `commercial/settings.scss` component-style budget warning. The full solution build was cancelled after Domain and Application compiled because Infrastructure compilation stopped making progress; it is not counted as passed. The built-in browser rendered the local Angular shell and its `Access unavailable` sign-in gate. Its `/api/ui/session` request was refused because no local API was running, so no authenticated package or export flow was browser-verified. This slice remains partial; the FSR backlog and professional acceptance gates remain open.

## Cash-flow movement evidence and adjusted cash cross-cast — 2026-10-11

The package build contract now requires each cash-flow movement to carry a reporting-period date, activity code, preparer-entered source reference and classification rationale. It also records the selected cash-account codes and separate opening/closing balance references. The metadata is persisted, included in the supplementary hash, surfaced in the scoped package workspace, and included in the canonical package artifact. New noncash operating adjustments are retained alongside noncash investing/financing items but excluded from the cash bridge. The selected adjusted cash-account population must exist in the adjusted source balances and cross-cast exactly to supplied closing cash; no balancing plug is created.

These references and classifications remain preparer inputs: the implementation does not resolve them to source-record identities or independently verify the underlying documents. There is no editor for this new schedule yet, and no approved framework-specific cash-flow classification policy. The increment is therefore PARTIAL. T030 still waits on T029 under the R2R task dependencies; it is not marked complete. It does not deliver the complete indirect method, population lineage, review workflow or professional acceptance described by FSR-005.

Observed on this working tree: Release solution build passed with zero errors and six `NU1900` warnings because the NuGet vulnerability feed was unavailable; Angular CI suite passed 634/634; `FinancialStatementPresentationTests` passed 15/15; Angular production build passed with the existing `commercial/settings.scss` budget warning (7.53 kB against 4.00 kB); EF reported no pending model changes; OpenAPI drift, Markdown, filename, narrative-metric, UI source-inventory and `git diff --check` gates passed. The new PostgreSQL-backed cash-flow cases were compiled but not run because PostgreSQL on `127.0.0.1:5433` did not respond. The built-in browser opened the local Angular package route and showed the expected unauthenticated access guard; no signed-in package data or cash-flow schedule was available for browser verification. This slice remains unapproved and does not close the attached statement backlog.

## A4-safe statement PDF and preview-pinned export — 2026-10-11

The statement PDF now allocates its four columns within the A4 printable width: 21 cm page width minus the configured 2 cm left and right margins, or 17 cm. Deep-row indentation is capped so the label column retains at least 4.5 cm for text. A long-label regression confirms the PDF paginates the wrapped row and every rendered page remains A4. The Angular export request now carries the exact published `layoutVersionId` shown in the preview. The PostgreSQL-backed regression also publishes a successor layout after preview and checks that an explicit export retains the reviewed version, but that integration case could not run locally.

Observed for this working tree based on `1f783baf`: Application Release build passed with one `NU1900` warning because the NuGet vulnerability feed was unreachable; `FinancialStatementPresentationTests` passed 15/15; the targeted Angular presentation spec passed 4/4; and the Angular production build passed with the existing commercial-settings stylesheet budget warning. The modified Domain test assembly compiled. Markdown, filename, narrative-metric, status JSON and diff checks passed. The PostgreSQL service on port 5433 was down; its documented local start attempt failed because a shared-memory block for that data directory was still in use, so it was left untouched. The local in-app browser did not reach the dev server route, and no signed-in/API workflow was assessed. The layout-lineage integration test and browser flow remain unverified. This is still an unapproved draft slice; it does not close the wider FSR backlog or professional, signing, composition, sealing or release gates.

## Typed financial-statement presentation draft — 2026-10-11

The current local slice implements the recommended first FSR presentation milestone. A scoped read and export now build a typed SFP and profit-or-loss presentation from persisted package lines and a published layout for the same client/framework. The server applies display signs, joins current and explicitly bound comparative package lines, links persisted note cross-casts, and blocks unbalanced or incompletely mapped statements. Exports produce deterministic PDF, DOCX and XLSX from that same draft model; Excel numeric cells remain numeric. The package's existing immutable artifact generation and stored bytes are unchanged.

The output is marked `UNAPPROVED_DRAFT`, and amounts use a display policy that still needs professional/design approval. The fixture uses synthetic balances matched to selected reference totals; it is not the NAQAHA ledger or an approved golden fixture. The focused tests and browser boundary evidence are recorded in [`status.json`](status.json). The local browser rendered the Angular shell's unauthenticated access guard; no API session or signed-in package data was available for a full screen journey.

FSR-001/T003 accounting-policy approval remains blocked on qualified owners. Cash-flow and equity statement matrices, complete disclosure policy and dynamic note numbering, full composition/sealing, audited report assembly, management and Partner acceptance, and production/legal sign-off remain open. This partial presentation slice does not complete the R2R task cards or the attached backlog.

## Isolated background mail worker active — 2026-10-11

The acceptance mail queues and pending discovery rows were checked before activation; no
unreviewed recipient was pending. The isolated mail profile was started with the previously
verified sender credential. Its broad parent credential mount was identified and replaced with
an outbound-mail-only read-only mount. The disabled PBC profile now also uses only its own
Selected Site mount; it was not started.

The clean VPS checkout was fast-forwarded to the reviewed Compose change and only the new mail
worker recreated. Its own key is readable and sibling credentials are absent. Logs show successful
queue polling without failures; API readiness and existing worker preservation passed. The
administrator Operations page is accessible and has no browser errors. This verifies idle worker
deployment, not an unexecuted business-mail workflow. Exact evidence is in [`status.json`](status.json).

SharePoint's separate exact-site grant remains unresolved; its Entra consent is present and the
runtime identity still cannot access the approved site. The exact grant receipt is requested
before another provider probe. No tenant-wide scope or site permission was added.

## Separate directory capabilities configured and verified — 2026-10-11

Following exact owner approval, the existing User Creation certificate was matched to its Entra
registration and staged privately on the VPS. Guest Invitation and Group Membership received
separate restricted VPS key pairs; only their public certificates were uploaded to their exact
single-permission app registrations. All identities authenticated for the configured tenant with
exactly their named application role, and their distinct app IDs were saved in the protected
server environment.

Only the API was recreated to enable the reviewed capabilities. Readiness passed and existing
workers were unchanged. AuditSphere persisted verified permission checks for User Creation and
Guest Invitation, plus a successful bounded read for Group Membership. Directory Reader and Mail
also verified successfully; browser console error checks were empty. No user was created, guest
invited or group membership modified. This completes capability setup and read/permission
verification, not full mutation-journey acceptance. Exact observations are in [`status.json`](status.json).

SharePoint remains separately blocked by the approved-target denial. The site's inspected General panel
confirmed the reviewed site identity; no grant or permission scope was changed. Further duplicate target probes remain paused pending exact grant
investigation, and the runtime Selected Site app ID stays unset.

## Restricted outbound mail verified and delivered — 2026-10-11

The local interactive authentication alternative failed because the Exchange authentication
library rejects this Mac's OS version. A temporary official Linux PowerShell container on the VPS
provided an active supported Exchange connection. The initial access checks allowed both the
approved sender and unrelated synthetic mailbox, proving a sender restriction was needed.

A mail-enabled security scope group containing only the approved sender and an exact-app
`RestrictAccess` policy were created. Exchange then allowed the sender and denied the unrelated
fixture. A structurally valid Graph authorization probe with no recipients also denied the
unrelated mailbox. The dedicated token contained only `Mail.Send`; the existing certificate was
reused and no new application credential was added.

The approved self-addressed synthetic message was accepted by Graph and Exchange's filtered
message trace confirmed delivery. The API-only recreation enabled the mail capability; readiness
passed, existing workers were unchanged, and fresh AuditSphere capability verification passed.
The reviewed local Mail metadata was saved and its immutable receipt reconciled; Records remains
unconfigured. The live mail worker was not started, so this is transport and configuration evidence,
not durable commercial/PBC mail workflow acceptance. Temporary verification containers and files
were removed. Exact timestamps, status and correlation evidence are in [`status.json`](status.json).

The prior authentication blocker is resolved by the supported Linux path. SharePoint's exact-site
denial and the disabled mutation capabilities remain separate blockers.

## Live VPS upgrade and directory consent verified — 2026-10-11

After exact owner approval, a fresh catalog-checked database backup was retained, the approved
migrations were applied to the synthetic acceptance database, and only the API was recreated.
Public readiness passed. Existing worker containers and the local development database were
unchanged. Exact image, migration, backup and observation evidence is in [`status.json`](status.json).

The dedicated Directory Reader now has both fixed Web callbacks and its own certificate. Only
its public certificate was uploaded to Entra; the private key remains on the VPS. Directory Reader
and Tenant Consent were enabled. The reviewed Microsoft consent and identity flow completed,
and AuditSphere persisted verified consent and a fresh successful bounded directory read.
Mail and directory mutations remain disabled. Consent verification does not promote the separate
resource revision or activate a workspace.

The deployed time screen renders FSLI attribution. End-of-service access correctly refuses an
administrator without a Finance or Partner grant. The commercial settings screen also renders the pricing
policy form and reports that no actual pricing policy has been configured. These are live screen observations, not a
record-specific business acceptance run. Selected SharePoint remains blocked by the missing
exact-site grant; neither the unrelated control nor the approved target allowed access.

## VPS upgrade rehearsal and selected-site access check — 2026-10-11

The previously authorized SSH access worked despite the expired Hostinger browser terminal.
The clean VPS checkout was fast-forwarded, the published API and migration images were pulled,
and a private custom-format PostgreSQL backup was catalog-checked and restored to a disposable
database. The pending migrations applied there, and the new API returned readiness HTTP 200
against that copy. The rehearsal container, database and temporary environment files were removed.
The live database and existing services were unchanged. Its upgrade awaits the specific migration
approval because the earlier deployment scope allowed API recreation without database migration.
Exact image, migration and backup evidence is in [`status.json`](status.json).

The installed Selected Site certificate matches the public certificate on the approved Entra app.
A live token contained exactly `Sites.Selected` for the configured tenant. Read-only Graph checks
returned HTTP 403 with `accessDenied` for both the approved synthetic site and its unrelated
control. The positive grant is missing; no disposable file write was attempted and no site grant
was changed. The server-side Selected Site app ID remains unset pending the user's exact-site grant.

Automatic approval review rejected selecting the Web callback platform on the separate Directory
Reader app because the earlier callback authorization named the Development app. Exact approval
for the Directory Reader callbacks, certificate and read-only capability enablement has been
requested. Its registration and runtime remain unchanged pending that response.

## Current-tree acceptance verification — 2026-10-11

The complete .NET and Angular suites, Release build, OpenAPI drift check, EF model-drift check,
documentation validators and source inventory now have current-tree evidence. Exact commands,
counts, durations, migration head and the verified code commit are recorded in [`status.json`](status.json).
The PostgreSQL-backed tests used only the isolated `auditsphere_tests` database. The previous
four failing browser journeys were diagnosed and fixed; the pricing-policy maker/checker and
end-of-service professional-gate journeys are now included in browser coverage.

The end-of-service page correctly blocks accrual preparation until a qualified accountant names
the treatment and a Partner confirms it. No firm treatment or chart accounts were supplied, so no
real accrual was prepared or posted. Certificate signing remains implemented and locally verified;
the firm certificate and legal-sufficiency decision are still outstanding. The GitHub Wiki's
Production Environment Settings page already documents the `ReportSigning` configuration, so no
Wiki edit was needed.

The Entra portal now confirms admin consent for `User.Read.All` on the dedicated Directory Reader
registration, alongside its delegated `User.Read` sign-in permission. This is registration-level
consent only: AuditSphere's tenant-connection page still shows no nonce-bound consent attempt, and
its consent action remains unavailable until the deployment-owned consent identity and fixed
callback are configured. Directory verification remains `NOT_VERIFIED` and the Directory Reader
runtime switch remains disabled. The selected SharePoint target fields remain an unsubmitted
browser-tab draft; the user will grant the exact site. Runtime Graph capabilities remain disabled,
and the selected-resource probe and workspace activation remain blocked on that grant and credential
setup. Do not mark those external states complete from local or Entra-registration evidence.
