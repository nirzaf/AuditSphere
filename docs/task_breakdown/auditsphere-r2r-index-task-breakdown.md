# STEAuditSphere — R2R Task Breakdown



## Single navigation index · Modules 20–26







**Start here.** Implement the numbered task files in the order below, subject to their explicit completion dependencies. This pack reorganizes `STEAuditSphere_R2R_Implementation_Blueprint_Modules_20-26.md`; it does not replace or broaden that blueprint.







**Pack version:** 1.0



**Prepared:** 24 September 2026



**Source document:** STE-R2R-ARCH-001, version 1.0, proposed for review



**Source repository baseline:** `master@ba1a3ec23335b40667b679e4f0cd5f66b9e723b9` — a historical reference, not a freshly verified head



**Target solution:** `nirzaf/AuditSphere` / `AuditSphereOps.slnx`







- **R2R Modules 20–26:** implementation contract for the accounting/reporting engine: context, TB/GL, adjustments, reconciliations, statements, packages and consolidation.



- **Audit workflow backlog:** complementary professional contract for the 20-section, 165-procedure, 28-story audit programme: planning, programme execution, populations, sampling, confirmations, fieldwork, findings, financial-statement audit review and completion.







These are separate layers and must not be merged into one module implementation. Audit consumes exact R2R source identities but does not create duplicate TB/GL, chart, period or package masters. Accounting review, package approval, journal management decisions and release gates are not substitutes for independent audit testing or audit acceptance. The current audit programme is single-entity; R2R Module 26 consolidation remains supported, while group-audit methodology is a future separate backlog. Payroll and tax stories in the audit backlog are workpaper testing only, never payroll execution or tax preparation/filing.







> [!IMPORTANT]



> ### Architectural Context & Invariants for AI Coding Agents



> - **Source Intent vs. Implemented Architecture:** Task cards preserve original blueprint requirement phrasing and request names (`XxxCommand`/`XxxQuery`). However, the implemented application layer uses **static capability services** returning `CommandResult`/`CommandResult<T>` in a single modular monolith (`AuditSphereOps`), **not** MediatR handlers or microservices (see [`auditsphere-r2r-reference-baseline-architecture-and-adrs.md` §2.3.1](reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md#section-2-3-1)).



> - **Current Architecture Wins:** `AGENTS.md` and `docs/architecture/auditsphere-architecture-current-architecture.md` are the supreme implementation authorities. Where a task card or reference suggests introducing MediatR, bUnit, or generic repository layers, the current modular monolith architecture wins.



> - **Inspect Current Repository Reality First:** Many features across tasks T001–T054 and fieldwork tasks are already implemented in `AuditSphereOps.Application` and `AuditSphereOps.Domain`. Always inspect existing capability files via `docs/architecture/auditsphere-architecture-code-map.md` and reuse existing types and methods before writing new code.







## Quick navigation







[How to use the pack](#how-to-use) · [Current tracking](#current-tracking) · [Status rules](#status-rules) · [Dependencies and parallel work](#dependencies) · [Complete task order](#task-order) · [Module map](#module-map) · [Source and coverage](#source-coverage) · [Agent instructions](#agent-instructions)







<a id="how-to-use"></a>



## 1. How to use the pack







1. Extract the complete ZIP, preserving folders and relative links. Open **`auditsphere-r2r-index-task-breakdown.md`** in an editor with Markdown navigation.



2. Start at **T001**. Obtain owner approval and inspect the actual current repository before changing implementation. None of the tasks is pre-approved by the act of generating this pack.



3. Open the next eligible task; read its hard dependencies, required shared/module references, exact owned requests and verification checks. Reuse existing functionality instead of creating duplicate models/services.



4. Record the owner, issue/branch, inspected and reviewed commit, evidence, blockers and independent reviewer in that task file. Work through its six architecture sections and completion checklist.



5. Mark a task COMPLETED only after its own checks and review pass. Record original criteria and integration journey outcomes separately; **task completion is not automatic module or production acceptance**.



6. Refresh this index and validate the pack after tracking updates. A standard-library-only helper is included; Markdown remains usable without it.







**Initial status:** all 75 cards are `NOT_STARTED`. This means they have not been evaluated or executed under this new breakdown. It does **not** mean that all the underlying application features are missing. Existing code must be inspected, reused and reverified against the task.







<a id="current-tracking"></a>



## 2. Current task tracking







<!-- BEGIN PROGRESS -->
| Tracking measure | Current value |
|---|---:|
| Numbered implementation tasks | 75 |
| Original work packages | 21 |
| COMPLETED | 0 |
| IN_REVIEW | 0 |
| IN_PROGRESS | 0 |
| BLOCKED | 0 |
| REOPENED | 0 |
| NOT_STARTED | 75 |

These are task-tracking totals, not a software-completion percentage or a transferred status from the source repository. Original criterion, fixture and integration acceptance are tracked separately.
<!-- END PROGRESS -->







### Next eligible task







<!-- BEGIN NEXT -->
- [T001](tasks/00_Baseline/auditsphere-r2r-task-t001-baseline-current-state-inventory.md) — Approve scope and inventory the current implementation
<!-- END NEXT -->







<a id="status-rules"></a>



## 3. Status authority, evidence and completion







The **front matter at the top of each task file is the single source of truth for its task status**. The tables in this index are generated summaries. Do not mark only an index checkbox or maintain competing completion lists.







| Status | Meaning | Can a dependent task begin? |



|---|---|---|



| `NOT_STARTED` | Not assessed/executed under this task breakdown. Existing implementation may already cover part or all of the work. | No |



| `IN_PROGRESS` | Named owner is implementing or verifying; hard prerequisites must already be completed. | No |



| `BLOCKED` | A concrete missing dependency, approval, failed check or upstream rework prevents advancement. Record the reason. | No |



| `IN_REVIEW` | Task work and local evidence are ready for independent review, but not yet accepted. | No |



| `COMPLETED` | Required task checks and handoff are accepted at the recorded commit with independent review. | Yes, subject to the consumer's other dependencies |



| `REOPENED` | Previously completed work requires reimplementation/reverification; historical evidence remains. | No |







Normal lifecycle: `NOT_STARTED → IN_PROGRESS → IN_REVIEW → COMPLETED`. Use `BLOCKED` when unable to proceed. A completed task reenters work through `REOPENED`, with a reason and impact assessment.







### What to record in a task







The metadata includes `owner`, `reviewer`, `review_decision`, `reviewed_commit`, `evidence_ref`, `approval_ref`, `blocked_reason`, `branch`, `issue_pr` and `updated_at`. The body has a six-item completion checklist and a fuller evidence/handoff table. Record actual values only. For foundation tasks, the reviewed commit can be the inspected baseline tied to the approval/evidence record; do not invent an implementation commit.







For COMPLETED, the helper requires: completed prerequisites; all six checklist entries checked; owner and distinct reviewer; `review_decision: "APPROVED"`; a full 40-character reviewed commit SHA; and an existing pack-relative evidence file or a recorded HTTP(S) evidence URL. T001 also requires the owner's blueprint/scope approval reference. These checks validate record structure, **not the truth of an approval or test outcome**.







Keep an evidence record with original requirement ID, test method/journey, expected and observed result, run command/ID, exact commit and schema, fixture, role/scope, relevant source/artifact identity and reviewer. A template is included below in this index.







### Optional local tracking helper







Python 3.10+ is sufficient; no packages, network access, GitHub token or database access are required. Run from the extracted pack root. The helper changes **only this extracted documentation pack**, not the application or repository configuration.







```bash



python tools/task_status.py validate



python tools/task_status.py next



python tools/task_status.py set T001 IN_PROGRESS --owner "Actual implementation owner"



```







After performing the task and recording real evidence, submit it for review:







```bash



python tools/task_status.py set T001 IN_REVIEW --owner "Actual implementation owner"



```







After review, complete the six checkboxes and metadata with real values, then use the helper. The following is a syntax template, not fabricated evidence; replace every placeholder before running:







```bash



python tools/task_status.py set T001 COMPLETED \



  --owner "<actual owner>" \



  --reviewer "<actual independent reviewer>" \



  --review-decision APPROVED \



  --commit "<full 40-character reviewed SHA>" \



  --evidence "<existing relative evidence file or evidence URL>" \



  --approval "<owner scope approval reference>"



```







A blocker or rework must be explicit:







```bash



python tools/task_status.py set T002 BLOCKED --owner "<actual owner>" --reason "Required architecture decision is pending"



# Only for a task that has already reached COMPLETED:



python tools/task_status.py set T019 REOPENED --reason "Mapping contract changed; dependent outputs need revalidation"



python tools/task_status.py refresh



python tools/task_status.py validate



```







Reopening completed work makes its active/reviewed/completed descendants `BLOCKED` and records why; untouched tasks remain `NOT_STARTED` and show waiting dependencies. Old evidence is preserved, not erased. When the upstream task is completed again, downstream owners must explicitly review/retest and progress their own cards; they are not automatically recompleted.







**Markdown-only mode:** edit the task front matter, checklist and evidence fields manually; update the corresponding index status/readiness and history row in the same change. Prefer `refresh`/`validate` to catch stale summaries and dependency mistakes. Coordinate all dependency or contract edits with the principal owner and update `tracking/pack_manifest.json` together.







Original acceptance ledgers are deliberately separate:







- [52 original acceptance criteria](coverage/auditsphere-r2r-tracker-acceptance-criteria.md): `NOT_RUN / PASS / FAIL / BLOCKED` with evidence.



- [30 integrated R2R journeys](coverage/auditsphere-r2r-tracker-integration-journeys.md): scenario-level observed results.



- [Eight golden fixtures](coverage/auditsphere-r2r-tracker-golden-fixtures.md): policy approval and numerical execution separately.



- [Audit workflow traceability](tracking/auditsphere-audit-tracker-workflow-traceability.md): complete mapping of 28 AS-AUD stories and 165 AWP procedures.







T051 and T054 cannot be marked complete through the helper until those ledgers show all required PASS results and fixture approvals. Passing the documentation validator does not run the application tests.







<a id="dependencies"></a>



## 4. Dependency rules and safe coordination







**Default route:** T001 through T075 (T001–T054 for foundational R2R accounting/reporting, followed by T055–T075 for integrated audit-workflow gap closure). A larger number never appears as a prerequisite of a smaller number. Every original work-package dependency is preserved transitively, and each task lists direct predecessors and consumers.







The sequence deliberately breaks module-level cycles:







- **Module 20 setup:** T011–T014 defines profiles/periods/charts/context. Its **close/amendment execution waits until T038–T040**, after statements and packages exist.



- **Module 21 mapping:** T019 owns the only mapping engine. It consumes Module 20 chart/taxonomy definitions and later supports AJE-only accounts without fabricating raw TB rows.



- **Module 25 entity package kernel:** T034–T037 completes before component pins in T042. **Group output later reuses it at T047**, so Module 25 is not blocked on Module 26.



- **Module 23 corrections:** a reconciliation links to Module 22 journal decisions. It must not create its own journal-approval mechanism or mutate source books.







### Bounded parallel opportunities







T030, T031 and T032 can be worked after T029 is complete; T033 waits for all three. After T037, the entity-close lane T038–T040 and group lane T041–T047 can progress separately; T048 waits for both.







These are dependency-safe opportunities, **not permission to edit shared files concurrently**. The coordinator assigns isolated UI/test work after the relevant contracts are frozen. Serialize migrations, DbContext snapshots, shared DTOs, source/currency policies and artifact schemas. Use numerical order whenever shared edits would conflict.







### Required handoff







A predecessor hands over exact contract/DTO definitions, reuse/new-symbol map, migrations, state transitions, dependency manifests, error outcomes and observed test results. The consumer verifies that handoff at the current repository revision. Do not copy business records into another module or infer authority from a producer's UI.







[One-owner command/query registry](coverage/auditsphere-r2r-tracker-command-query-ownership.md) · [Exact lifecycle and port contract](reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md) · [Status history](tracking/auditsphere-r2r-tracker-status-history.md)







<a id="task-order"></a>



## 5. Complete ordered task register







The following table is the only master navigation/status index. Each task opens its own smaller Markdown implementation card. Statuses are derived from task metadata.







<!-- BEGIN TASKS -->
<a id="wp-00"></a>
### R2R-00 — Pin current SHA; inventory existing models/services/migrations/UI/tests; reconcile ADRs and scope conflicts

**Original dependencies:** Owner review of this draft. **Task gate:** 0/3 COMPLETED.
**Original exit evidence:** Existing/new symbol ledger, policy approvals, preserved baseline tests and dependency licence decisions.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T001 — Approve scope and inventory the current implementation](tasks/00_Baseline/auditsphere-r2r-task-t001-baseline-current-state-inventory.md) | Owner/scope review | NOT_STARTED | READY FOR OWNER REVIEW | Unassigned |
| [ ] | [T002 — Approve architecture, contracts and transaction ownership](tasks/00_Baseline/auditsphere-r2r-task-t002-architecture-contracts-transaction-ownership.md) | [T001](tasks/00_Baseline/auditsphere-r2r-task-t001-baseline-current-state-inventory.md) | NOT_STARTED | WAITING: T001 | Unassigned |
| [ ] | [T003 — Approve accounting policies, resource bounds and golden fixtures](tasks/00_Baseline/auditsphere-r2r-task-t003-accounting-policies-resource-bounds-golden-fixtures.md) | [T002](tasks/00_Baseline/auditsphere-r2r-task-t002-architecture-contracts-transaction-ownership.md) | NOT_STARTED | WAITING: T002 | Unassigned |

<a id="wp-01"></a>
### R2R-01 — MediatR facade, async validation adapter, explicit DTO mapping, one transaction-owner registry, bUnit test project

**Original dependencies:** R2R-00. **Task gate:** 0/3 COMPLETED.
**Original exit evidence:** One representative existing command/query migrated without behavior change or nested transaction.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T004 — Introduce MediatR facades without changing transaction behavior](tasks/01_CQRS/auditsphere-r2r-task-t004-command-query-boundary.md) | [T003](tasks/00_Baseline/auditsphere-r2r-task-t003-accounting-policies-resource-bounds-golden-fixtures.md) | NOT_STARTED | WAITING: T003 | Unassigned |
| [ ] | [T005 — Implement asynchronous validation and explicit DTO mapping](tasks/01_CQRS/auditsphere-r2r-task-t005-asynchronous-validation-dto-mapping.md) | [T004](tasks/01_CQRS/auditsphere-r2r-task-t004-command-query-boundary.md) | NOT_STARTED | WAITING: T004 | Unassigned |
| [ ] | [T006 — Establish bUnit and the layered verification harness](tasks/01_CQRS/auditsphere-r2r-task-t006-bunit-verification-harness.md) | [T005](tasks/01_CQRS/auditsphere-r2r-task-t005-asynchronous-validation-dto-mapping.md) | NOT_STARTED | WAITING: T005 | Unassigned |

<a id="wp-02"></a>
### R2R-02 — Scope/currentness/manifest/idempotency contracts and shared EditForm/state/dirty/conflict components

**Original dependencies:** R2R-01. **Task gate:** 0/4 COMPLETED.
**Original exit evidence:** Direct-handler denial, concurrency, same-document stale-route and atomic invalidation tests.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T007 — Enforce reporting scope, current actor and shared identities](tasks/02_Shared_Foundation/auditsphere-r2r-task-t007-reporting-scope-actor-shared-identities.md) | [T006](tasks/01_CQRS/auditsphere-r2r-task-t006-bunit-verification-harness.md) | NOT_STARTED | WAITING: T006 | Unassigned |
| [ ] | [T008 — Implement exact dependency manifests and atomic stale propagation](tasks/02_Shared_Foundation/auditsphere-r2r-task-t008-dependency-manifests-atomic-stale-propagation.md) | [T007](tasks/02_Shared_Foundation/auditsphere-r2r-task-t007-reporting-scope-actor-shared-identities.md) | NOT_STARTED | WAITING: T007 | Unassigned |
| [ ] | [T009 — Implement idempotency, concurrency and persistence discipline](tasks/02_Shared_Foundation/auditsphere-r2r-task-t009-idempotency-concurrency-persistence-discipline.md) | [T008](tasks/02_Shared_Foundation/auditsphere-r2r-task-t008-dependency-manifests-atomic-stale-propagation.md) | NOT_STARTED | WAITING: T008 | Unassigned |
| [ ] | [T010 — Build shared Blazor forms, state, dirty guards and conflict UI](tasks/02_Shared_Foundation/auditsphere-r2r-task-t010-shared-blazor-forms-state-dirty-guards.md) | [T009](tasks/02_Shared_Foundation/auditsphere-r2r-task-t009-idempotency-concurrency-persistence-discipline.md) | NOT_STARTED | WAITING: T009 | Unassigned |

<a id="wp-03"></a>
### R2R-03 — M20 profiles, approved chart/taxonomy/dimension versions, periods/books and context activation

**Original dependencies:** R2R-02. **Task gate:** 0/4 COMPLETED.
**Original exit evidence:** Complete valid/invalid context lifecycle with historical preservation and source handoff.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T011 — Build accounting profiles, fiscal periods and reporting books](tasks/03_M20_Setup/auditsphere-r2r-task-t011-accounting-profiles-fiscal-periods-books.md) | [T010](tasks/02_Shared_Foundation/auditsphere-r2r-task-t010-shared-blazor-forms-state-dirty-guards.md) | NOT_STARTED | WAITING: T010 | Unassigned |
| [ ] | [T012 — Build chart and taxonomy revision authoring](tasks/03_M20_Setup/auditsphere-r2r-task-t012-chart-taxonomy-revision-authoring.md) | [T011](tasks/03_M20_Setup/auditsphere-r2r-task-t011-accounting-profiles-fiscal-periods-books.md) | NOT_STARTED | WAITING: T011 | Unassigned |
| [ ] | [T013 — Build multidimensional accounting schema revisions](tasks/03_M20_Setup/auditsphere-r2r-task-t013-multidimensional-accounting-schema-revisions.md) | [T012](tasks/03_M20_Setup/auditsphere-r2r-task-t012-chart-taxonomy-revision-authoring.md) | NOT_STARTED | WAITING: T012 | Unassigned |
| [ ] | [T014 — Activate exact reporting contexts and verify Module 20 setup](tasks/03_M20_Setup/auditsphere-r2r-task-t014-reporting-contexts-module-20-setup.md) | [T013](tasks/03_M20_Setup/auditsphere-r2r-task-t013-multidimensional-accounting-schema-revisions.md) | NOT_STARTED | WAITING: T013 | Unassigned |

<a id="wp-04"></a>
### R2R-04 — M21 bounded TB CSV/XLSX receipt, profile/column mapping, staging, validation and acceptance

**Original dependencies:** R2R-03. **Task gate:** 0/3 COMPLETED.
**Original exit evidence:** Real formats, parser attacks/limits, rejected replacement and sealed-membership tests.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T015 — Build bounded import receipts, chunks and column profiles](tasks/04_M21_TB/auditsphere-r2r-task-t015-import-receipts-chunks-column-profiles.md) | [T014](tasks/03_M20_Setup/auditsphere-r2r-task-t014-reporting-contexts-module-20-setup.md) | NOT_STARTED | WAITING: T014 | Unassigned |
| [ ] | [T016 — Implement CSV and genuine XLSX validation with safe previews](tasks/04_M21_TB/auditsphere-r2r-task-t016-csv-xlsx-validation-safe-previews.md) | [T015](tasks/04_M21_TB/auditsphere-r2r-task-t015-import-receipts-chunks-column-profiles.md) | NOT_STARTED | WAITING: T015 | Unassigned |
| [ ] | [T017 — Seal and accept TB revisions with replacement protection](tasks/04_M21_TB/auditsphere-r2r-task-t017-seal-accept-tb-revisions.md) | [T016](tasks/04_M21_TB/auditsphere-r2r-task-t016-csv-xlsx-validation-safe-previews.md) | NOT_STARTED | WAITING: T016 | Unassigned |

<a id="wp-05"></a>
### R2R-05 — M21 resumable GL intake, opening/completeness, mapping/splits, paged drill-down and exports

**Original dependencies:** R2R-04. **Task gate:** 0/3 COMPLETED.
**Original exit evidence:** Opening+movement=closing; partial batches cannot pass; approved mapping/source lineage.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T018 — Build resumable GL intake and journal drill-down](tasks/05_M21_GL_Mapping/auditsphere-r2r-task-t018-resumable-gl-intake-journal-drilldown.md) | [T017](tasks/04_M21_TB/auditsphere-r2r-task-t017-seal-accept-tb-revisions.md) | NOT_STARTED | WAITING: T017 | Unassigned |
| [ ] | [T019 — Build versioned account mappings, manual splits and review](tasks/05_M21_GL_Mapping/auditsphere-r2r-task-t019-versioned-account-mappings-splits-review.md) | [T018](tasks/05_M21_GL_Mapping/auditsphere-r2r-task-t018-resumable-gl-intake-journal-drilldown.md) | NOT_STARTED | WAITING: T018 | Unassigned |
| [ ] | [T020 — Build opening completeness proofs and scoped source exports](tasks/05_M21_GL_Mapping/auditsphere-r2r-task-t020-opening-completeness-proofs-scoped-exports.md) | [T019](tasks/05_M21_GL_Mapping/auditsphere-r2r-task-t019-versioned-account-mappings-splits-review.md) | NOT_STARTED | WAITING: T019 | Unassigned |

<a id="wp-06"></a>
### R2R-06 — M22 journals, independent technical/management decisions, reflection and adjusted snapshots

**Original dependencies:** R2R-05. **Task gate:** 0/3 COMPLETED.
**Original exit evidence:** Accepted/rejected/partial/reflected/unknown and replacement-base tests; no double application.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T021 — Build arbitrary-line adjustment drafts and submission](tasks/06_M22_Adjustments/auditsphere-r2r-task-t021-arbitrary-line-adjustment-drafts-submission.md) | [T020](tasks/05_M21_GL_Mapping/auditsphere-r2r-task-t020-opening-completeness-proofs-scoped-exports.md) | NOT_STARTED | WAITING: T020 | Unassigned |
| [ ] | [T022 — Implement adjustment decisions, revisions and reversals](tasks/06_M22_Adjustments/auditsphere-r2r-task-t022-adjustment-decisions-revisions-reversals.md) | [T021](tasks/06_M22_Adjustments/auditsphere-r2r-task-t021-arbitrary-line-adjustment-drafts-submission.md) | NOT_STARTED | WAITING: T021 | Unassigned |
| [ ] | [T023 — Build reflection treatment, sealed plans and adjusted TB snapshots](tasks/06_M22_Adjustments/auditsphere-r2r-task-t023-reflection-treatment-adjusted-tb-snapshots.md) | [T022](tasks/06_M22_Adjustments/auditsphere-r2r-task-t022-adjustment-decisions-revisions-reversals.md) | NOT_STARTED | WAITING: T022 | Unassigned |

<a id="wp-07"></a>
### R2R-07 — M23 bank/subledger source schedules, typed items, correction links, proof/review/rework

**Original dependencies:** R2R-06. **Task gate:** 0/3 COMPLETED.
**Original exit evidence:** Raw and adjusted-basis proofs agree without duplicate corrections; zero unexplained residual.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T024 — Build reconciliation schedules and typed source items](tasks/07_M23_Reconciliations/auditsphere-r2r-task-t024-reconciliation-schedules-typed-source-items.md) | [T023](tasks/06_M22_Adjustments/auditsphere-r2r-task-t023-reflection-treatment-adjusted-tb-snapshots.md) | NOT_STARTED | WAITING: T023 | Unassigned |
| [ ] | [T025 — Implement reconciliation proof and correction-journal handoff](tasks/07_M23_Reconciliations/auditsphere-r2r-task-t025-reconciliation-proof-correction-journals.md) | [T024](tasks/07_M23_Reconciliations/auditsphere-r2r-task-t024-reconciliation-schedules-typed-source-items.md) | NOT_STARTED | WAITING: T024 | Unassigned |
| [ ] | [T026 — Implement reconciliation review, rework and export](tasks/07_M23_Reconciliations/auditsphere-r2r-task-t026-reconciliation-review-rework-export.md) | [T025](tasks/07_M23_Reconciliations/auditsphere-r2r-task-t025-reconciliation-proof-correction-journals.md) | NOT_STARTED | WAITING: T025 | Unassigned |

<a id="wp-08"></a>
### R2R-08 — M24 versioned layout, current/prior balances, line drill-down and statement-set snapshots

**Original dependencies:** R2R-07. **Task gate:** 0/3 COMPLETED.
**Original exit evidence:** Approved mapping → exact current/prior lines; no parent/child double count; missing prior visible.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T027 — Build bounded statement-layout definitions and publication](tasks/08_M24_Statements/auditsphere-r2r-task-t027-statement-layout-definitions-publication.md) | [T026](tasks/07_M23_Reconciliations/auditsphere-r2r-task-t026-reconciliation-review-rework-export.md) | NOT_STARTED | WAITING: T026 | Unassigned |
| [ ] | [T028 — Implement comparative-basis selection and historical differences](tasks/08_M24_Statements/auditsphere-r2r-task-t028-comparative-basis-selection-differences.md) | [T027](tasks/08_M24_Statements/auditsphere-r2r-task-t027-statement-layout-definitions-publication.md) | NOT_STARTED | WAITING: T027 | Unassigned |
| [ ] | [T029 — Build canonical statement sets and exact line drill-down](tasks/08_M24_Statements/auditsphere-r2r-task-t029-canonical-statement-sets-line-drilldown.md) | [T028](tasks/08_M24_Statements/auditsphere-r2r-task-t028-comparative-basis-selection-differences.md) | NOT_STARTED | WAITING: T028 | Unassigned |

<a id="wp-09"></a>
### R2R-09 — M24 cash/equity schedules, notes, policy-edition rules, restatements and review

**Original dependencies:** R2R-08. **Task gate:** 0/4 COMPLETED.
**Original exit evidence:** Complete supported statement set including required disclosures; subsequent-event/treatment decisions recorded.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T030 — Build source-backed cash-flow schedules](tasks/09_M24_Supplements/auditsphere-r2r-task-t030-source-backed-cash-flow-schedules.md) | [T029](tasks/08_M24_Statements/auditsphere-r2r-task-t029-canonical-statement-sets-line-drilldown.md) | NOT_STARTED | WAITING: T029 | Unassigned |
| [ ] | [T031 — Build equity-movement schedules](tasks/09_M24_Supplements/auditsphere-r2r-task-t031-equity-movement-schedules.md) | [T029](tasks/08_M24_Statements/auditsphere-r2r-task-t029-canonical-statement-sets-line-drilldown.md) | NOT_STARTED | WAITING: T029 | Unassigned |
| [ ] | [T032 — Build disclosure notes and reviewed applicability](tasks/09_M24_Supplements/auditsphere-r2r-task-t032-disclosure-notes-reviewed-applicability.md) | [T029](tasks/08_M24_Statements/auditsphere-r2r-task-t029-canonical-statement-sets-line-drilldown.md) | NOT_STARTED | WAITING: T029 | Unassigned |
| [ ] | [T033 — Complete statement review and restatement lifecycle](tasks/09_M24_Supplements/auditsphere-r2r-task-t033-statement-review-restatement-lifecycle.md) | [T030](tasks/09_M24_Supplements/auditsphere-r2r-task-t030-source-backed-cash-flow-schedules.md), [T031](tasks/09_M24_Supplements/auditsphere-r2r-task-t031-equity-movement-schedules.md), [T032](tasks/09_M24_Supplements/auditsphere-r2r-task-t032-disclosure-notes-reviewed-applicability.md) | NOT_STARTED | WAITING: T030, T031, T032 | Unassigned |

<a id="wp-10"></a>
### R2R-10 — M25 composition, renderer reuse, genuine outputs, validation, sealing and exact decisions

**Original dependencies:** R2R-09. **Task gate:** 0/4 COMPLETED.
**Original exit evidence:** All expected artifacts parse and reconcile; partial generation/tamper/source races fail closed.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T034 — Persist financial-package composition and revisions](tasks/10_M25_Packages/auditsphere-r2r-task-t034-financial-package-composition-revisions.md) | [T033](tasks/09_M24_Supplements/auditsphere-r2r-task-t033-statement-review-restatement-lifecycle.md) | NOT_STARTED | WAITING: T033 | Unassigned |
| [ ] | [T035 — Render genuine XLSX, DOCX and PDF with durable operations](tasks/10_M25_Packages/auditsphere-r2r-task-t035-render-xlsx-docx-pdf-durable-operations.md) | [T034](tasks/10_M25_Packages/auditsphere-r2r-task-t034-financial-package-composition-revisions.md) | NOT_STARTED | WAITING: T034 | Unassigned |
| [ ] | [T036 — Validate artifact sets and atomically seal packages](tasks/10_M25_Packages/auditsphere-r2r-task-t036-validate-artifact-sets-seal-packages.md) | [T035](tasks/10_M25_Packages/auditsphere-r2r-task-t035-render-xlsx-docx-pdf-durable-operations.md) | NOT_STARTED | WAITING: T035 | Unassigned |
| [ ] | [T037 — Implement exact-package decisions, downloads and release readiness](tasks/10_M25_Packages/auditsphere-r2r-task-t037-package-decisions-downloads-release-readiness.md) | [T036](tasks/10_M25_Packages/auditsphere-r2r-task-t036-validate-artifact-sets-seal-packages.md) | NOT_STARTED | WAITING: T036 | Unassigned |

<a id="wp-11"></a>
### R2R-11 — M20 close/amendment and entity package→review→release/archive boundary integration

**Original dependencies:** R2R-10. **Task gate:** 0/3 COMPLETED.
**Original exit evidence:** End-to-end entity lifecycle, fresh amendment and prior-period opening bridge without historical mutation.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T038 — Implement close readiness and controlled period close](tasks/11_Entity_Close/auditsphere-r2r-task-t038-close-readiness-controlled-period-close.md) | [T037](tasks/10_M25_Packages/auditsphere-r2r-task-t037-package-decisions-downloads-release-readiness.md) | NOT_STARTED | WAITING: T037 | Unassigned |
| [ ] | [T039 — Implement period amendments and controlled opening roll-forward](tasks/11_Entity_Close/auditsphere-r2r-task-t039-period-amendments-opening-rollforward.md) | [T038](tasks/11_Entity_Close/auditsphere-r2r-task-t038-close-readiness-controlled-period-close.md) | NOT_STARTED | WAITING: T038 | Unassigned |
| [ ] | [T040 — Integrate entity package, release and logical archive handoffs](tasks/11_Entity_Close/auditsphere-r2r-task-t040-entity-package-release-logical-archive.md) | [T039](tasks/11_Entity_Close/auditsphere-r2r-task-t039-period-amendments-opening-rollforward.md) | NOT_STARTED | WAITING: T039 | Unassigned |

<a id="wp-12"></a>
### R2R-12 — M26 approved group/perimeter and exact component acceptance

**Original dependencies:** R2R-10. **Task gate:** 0/2 COMPLETED.
**Original exit evidence:** Control/membership/scoping/currentness; no implicit Partner grant; no invented component values.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T041 — Build controlled group perimeters and group authorization](tasks/12_M26_Perimeter/auditsphere-r2r-task-t041-group-perimeters-authorization.md) | [T037](tasks/10_M25_Packages/auditsphere-r2r-task-t037-package-decisions-downloads-release-readiness.md) | NOT_STARTED | WAITING: T037 | Unassigned |
| [ ] | [T042 — Pin and replace approved component packages](tasks/12_M26_Perimeter/auditsphere-r2r-task-t042-pin-replace-component-packages.md) | [T041](tasks/12_M26_Perimeter/auditsphere-r2r-task-t041-group-perimeters-authorization.md) | NOT_STARTED | WAITING: T041 | Unassigned |

<a id="wp-13"></a>
### R2R-13 — M26 versioned rates/policy, translation bridges and manual intercompany review

**Original dependencies:** R2R-12. **Task gate:** 0/2 COMPLETED.
**Original exit evidence:** Same-currency and defined FX profiles; missing-rate/unsupported-method denial.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T043 — Implement rate policies and reproducible FX translation](tasks/13_M26_FX/auditsphere-r2r-task-t043-rate-policies-reproducible-fx-translation.md) | [T042](tasks/12_M26_Perimeter/auditsphere-r2r-task-t042-pin-replace-component-packages.md) | NOT_STARTED | WAITING: T042 | Unassigned |
| [ ] | [T044 — Implement manual intercompany pair review and exceptions](tasks/13_M26_FX/auditsphere-r2r-task-t044-manual-intercompany-pair-review-exceptions.md) | [T043](tasks/13_M26_FX/auditsphere-r2r-task-t043-rate-policies-reproducible-fx-translation.md) | NOT_STARTED | WAITING: T043 | Unassigned |

<a id="wp-14"></a>
### R2R-14 — M26 elimination journals, deterministic group runs, independent review and group packages

**Original dependencies:** R2R-13. **Task gate:** 0/3 COMPLETED.
**Original exit evidence:** Investment/equity and intercompany golden results, source nonmutation, group-only artifact scope.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T045 — Build balanced elimination journals and independent review](tasks/14_M26_Output/auditsphere-r2r-task-t045-balanced-elimination-journals-review.md) | [T044](tasks/13_M26_FX/auditsphere-r2r-task-t044-manual-intercompany-pair-review-exceptions.md) | NOT_STARTED | WAITING: T044 | Unassigned |
| [ ] | [T046 — Build deterministic consolidation results and independent review](tasks/14_M26_Output/auditsphere-r2r-task-t046-deterministic-consolidation-results-review.md) | [T045](tasks/14_M26_Output/auditsphere-r2r-task-t045-balanced-elimination-journals-review.md) | NOT_STARTED | WAITING: T045 | Unassigned |
| [ ] | [T047 — Assemble reviewed group packages through the shared artifact kernel](tasks/14_M26_Output/auditsphere-r2r-task-t047-assemble-group-packages-artifact-kernel.md) | [T046](tasks/14_M26_Output/auditsphere-r2r-task-t046-deterministic-consolidation-results-review.md) | NOT_STARTED | WAITING: T046 | Unassigned |

<a id="wp-15"></a>
### R2R-15 — Cross-module replay, historical/currentness, permission revocation, concurrency, fault and recovery acceptance

**Original dependencies:** R2R-11, R2R-14. **Task gate:** 0/4 COMPLETED.
**Original exit evidence:** All approved criterion/test mappings executed on one release candidate with zero unresolved blocking failures.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T048 — Verify cross-module authority, replay and race conditions](tasks/15_Acceptance/auditsphere-r2r-task-t048-cross-module-authority-replay-race-conditions.md) | [T040](tasks/11_Entity_Close/auditsphere-r2r-task-t040-entity-package-release-logical-archive.md), [T047](tasks/14_M26_Output/auditsphere-r2r-task-t047-assemble-group-packages-artifact-kernel.md) | NOT_STARTED | WAITING: T040, T047 | Unassigned |
| [ ] | [T049 — Execute complete entity and group accounting acceptance journeys](tasks/15_Acceptance/auditsphere-r2r-task-t049-entity-group-accounting-acceptance-journeys.md) | [T048](tasks/15_Acceptance/auditsphere-r2r-task-t048-cross-module-authority-replay-race-conditions.md) | NOT_STARTED | WAITING: T048 | Unassigned |
| [ ] | [T050 — Verify schema upgrades, restore, bounded performance and output safety](tasks/15_Acceptance/auditsphere-r2r-task-t050-schema-upgrades-restore-bounded-performance.md) | [T049](tasks/15_Acceptance/auditsphere-r2r-task-t049-entity-group-accounting-acceptance-journeys.md) | NOT_STARTED | WAITING: T049 | Unassigned |
| [ ] | [T051 — Reconcile every requirement and freeze the verified release candidate](tasks/15_Acceptance/auditsphere-r2r-task-t051-freeze-verified-release-candidate.md) | [T050](tasks/15_Acceptance/auditsphere-r2r-task-t050-schema-upgrades-restore-bounded-performance.md) | NOT_STARTED | WAITING: T050 | Unassigned |

<a id="wp-16"></a>
### R2R-16 — Staged deployment/migration rehearsal, operator training and production acceptance

**Original dependencies:** R2R-15. **Task gate:** 0/3 COMPLETED.
**Original exit evidence:** Approved environment, backups/restore, configuration and live Microsoft evidence where the enabled workflow requires it.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T052 — Rehearse staged deployment and migrations](tasks/16_Deployment/auditsphere-r2r-task-t052-rehearse-staged-deployment-migrations.md) | [T051](tasks/15_Acceptance/auditsphere-r2r-task-t051-freeze-verified-release-candidate.md) | NOT_STARTED | WAITING: T051 | Unassigned |
| [ ] | [T053 — Verify enabled Microsoft dependencies and complete operator handover](tasks/16_Deployment/auditsphere-r2r-task-t053-verify-microsoft-dependencies-operator-handover.md) | [T052](tasks/16_Deployment/auditsphere-r2r-task-t052-rehearse-staged-deployment-migrations.md) | NOT_STARTED | WAITING: T052 | Unassigned |
| [ ] | [T054 — Obtain final Record-to-Report production acceptance and record the R2R handover](tasks/16_Deployment/auditsphere-r2r-task-t054-production-acceptance-handover.md) | [T053](tasks/16_Deployment/auditsphere-r2r-task-t053-verify-microsoft-dependencies-operator-handover.md) | NOT_STARTED | WAITING: T053 | Unassigned |

<a id="wp-17"></a>
### AUD-17 — Audit program library, engagement tailoring, lead schedules, sampling and confirmations foundation

**Original dependencies:** R2R-00. **Task gate:** 0/5 COMPLETED.
**Original exit evidence:** Published 20-section library, tailored engagement procedures, reconciled lead schedules and auditable sampling/confirmation records.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T055 — Publish versioned audit-program library](tasks/17_Audit_Foundation/auditsphere-audit-task-t055-audit-program-library.md) | [T003](tasks/00_Baseline/auditsphere-r2r-task-t003-accounting-policies-resource-bounds-golden-fixtures.md) | NOT_STARTED | WAITING: T003 | Unassigned |
| [ ] | [T056 — Instantiate and tailor engagement audit programs](tasks/17_Audit_Foundation/auditsphere-audit-task-t056-engagement-audit-programs.md) | [T014](tasks/03_M20_Setup/auditsphere-r2r-task-t014-reporting-contexts-module-20-setup.md), [T055](tasks/17_Audit_Foundation/auditsphere-audit-task-t055-audit-program-library.md) | NOT_STARTED | WAITING: T014, T055 | Unassigned |
| [ ] | [T057 — Reconcile audit lead schedules and source populations](tasks/17_Audit_Foundation/auditsphere-audit-task-t057-lead-schedules-source-populations.md) | [T020](tasks/05_M21_GL_Mapping/auditsphere-r2r-task-t020-opening-completeness-proofs-scoped-exports.md), [T056](tasks/17_Audit_Foundation/auditsphere-audit-task-t056-engagement-audit-programs.md) | NOT_STARTED | WAITING: T020, T056 | Unassigned |
| [ ] | [T058 — Implement audit sampling, item testing and subsequent matching](tasks/17_Audit_Foundation/auditsphere-audit-task-t058-sampling-item-testing.md) | [T057](tasks/17_Audit_Foundation/auditsphere-audit-task-t057-lead-schedules-source-populations.md) | NOT_STARTED | WAITING: T057 | Unassigned |
| [ ] | [T059 — Manage audit confirmations and alternative procedures](tasks/17_Audit_Foundation/auditsphere-audit-task-t059-confirmations-alternative-procedures.md) | [T058](tasks/17_Audit_Foundation/auditsphere-audit-task-t058-sampling-item-testing.md) | NOT_STARTED | WAITING: T058 | Unassigned |

<a id="wp-18"></a>
### AUD-18 — Audit planning, materiality, Cash & Bank, Receivables, Inventory, Revenue and Payables fieldwork

**Original dependencies:** R2R-00. **Task gate:** 0/6 COMPLETED.
**Original exit evidence:** Approved materiality/risk, complete Cash, Receivables, Inventory, Revenue and Payables audit workpapers.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T060 — Implement audit planning, materiality and risk assessment](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | [T056](tasks/17_Audit_Foundation/auditsphere-audit-task-t056-engagement-audit-programs.md), [T057](tasks/17_Audit_Foundation/auditsphere-audit-task-t057-lead-schedules-source-populations.md) | NOT_STARTED | WAITING: T056, T057 | Unassigned |
| [ ] | [T061 — Implement cash and bank audit workpapers](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t061-cash-bank-workpapers.md) | [T026](tasks/07_M23_Reconciliations/auditsphere-r2r-task-t026-reconciliation-review-rework-export.md), [T059](tasks/17_Audit_Foundation/auditsphere-audit-task-t059-confirmations-alternative-procedures.md), [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T026, T059, T060 | Unassigned |
| [ ] | [T062 — Implement trade receivables and allowance audit workpapers](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t062-receivables-allowance-workpapers.md) | [T058](tasks/17_Audit_Foundation/auditsphere-audit-task-t058-sampling-item-testing.md), [T059](tasks/17_Audit_Foundation/auditsphere-audit-task-t059-confirmations-alternative-procedures.md), [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T058, T059, T060 | Unassigned |
| [ ] | [T063 — Implement inventory count, costing and nrv audit workpapers](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t063-inventory-count-costing-nrv.md) | [T058](tasks/17_Audit_Foundation/auditsphere-audit-task-t058-sampling-item-testing.md), [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T058, T060 | Unassigned |
| [ ] | [T064 — Implement revenue testing and cut-off audit workpapers](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t064-revenue-testing-cutoff.md) | [T058](tasks/17_Audit_Foundation/auditsphere-audit-task-t058-sampling-item-testing.md), [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T058, T060 | Unassigned |
| [ ] | [T065 — Implement purchases, payables and unrecorded liabilities audit workpapers](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t065-payables-unrecorded-liabilities.md) | [T058](tasks/17_Audit_Foundation/auditsphere-audit-task-t058-sampling-item-testing.md), [T059](tasks/17_Audit_Foundation/auditsphere-audit-task-t059-confirmations-alternative-procedures.md), [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T058, T059, T060 | Unassigned |

<a id="wp-19"></a>
### AUD-19 — Fixed Assets, Expenses, Payroll, Loans, Related Parties, Tax and client journal fraud testing

**Original dependencies:** R2R-00. **Task gate:** 0/7 COMPLETED.
**Original exit evidence:** Complete audit workpapers for remaining balance-sheet and P&L areas, plus ISA 240 journal-entry testing.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T066 — Implement fixed assets audit workpapers](tasks/19_Audit_Fieldwork_Extended/auditsphere-audit-task-t066-fixed-assets-workpapers.md) | [T058](tasks/17_Audit_Foundation/auditsphere-audit-task-t058-sampling-item-testing.md), [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T058, T060 | Unassigned |
| [ ] | [T067 — Implement expenses vouching and cut-off audit workpapers](tasks/19_Audit_Fieldwork_Extended/auditsphere-audit-task-t067-expenses-vouching-cutoff.md) | [T058](tasks/17_Audit_Foundation/auditsphere-audit-task-t058-sampling-item-testing.md), [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md), [T066](tasks/19_Audit_Fieldwork_Extended/auditsphere-audit-task-t066-fixed-assets-workpapers.md) | NOT_STARTED | WAITING: T058, T060, T066 | Unassigned |
| [ ] | [T068 — Implement payroll audit workpapers](tasks/19_Audit_Fieldwork_Extended/auditsphere-audit-task-t068-payroll-workpapers.md) | [T058](tasks/17_Audit_Foundation/auditsphere-audit-task-t058-sampling-item-testing.md), [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T058, T060 | Unassigned |
| [ ] | [T069 — Implement loans, borrowings and covenant audit workpapers](tasks/19_Audit_Fieldwork_Extended/auditsphere-audit-task-t069-loans-borrowings-covenants.md) | [T059](tasks/17_Audit_Foundation/auditsphere-audit-task-t059-confirmations-alternative-procedures.md), [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T059, T060 | Unassigned |
| [ ] | [T070 — Implement related parties audit workpapers](tasks/19_Audit_Fieldwork_Extended/auditsphere-audit-task-t070-related-parties-workpapers.md) | [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T060 | Unassigned |
| [ ] | [T071 — Implement tax and statutory liabilities audit workpapers](tasks/19_Audit_Fieldwork_Extended/auditsphere-audit-task-t071-tax-statutory-liabilities.md) | [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T060 | Unassigned |
| [ ] | [T072 — Implement journal entries and fraud risk audit testing](tasks/19_Audit_Fieldwork_Extended/auditsphere-audit-task-t072-journal-entries-fraud-risk.md) | [T020](tasks/05_M21_GL_Mapping/auditsphere-r2r-task-t020-opening-completeness-proofs-scoped-exports.md), [T058](tasks/17_Audit_Foundation/auditsphere-audit-task-t058-sampling-item-testing.md), [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T020, T058, T060 | Unassigned |

<a id="wp-20"></a>
### AUD-20 — Substantive analytics, Going Concern assessment and Subsequent Events review

**Original dependencies:** R2R-00. **Task gate:** 0/3 COMPLETED.
**Original exit evidence:** Documented ratio/variance analysis, evaluated going-concern forecast and reviewed subsequent events through report date.

| Done | Task file | Direct dependencies | Status | Readiness | Owner |
|---|---|---|---|---|---|
| [ ] | [T073 — Implement substantive and final analytical review](tasks/20_Audit_Completion/auditsphere-audit-task-t073-substantive-final-analytical-review.md) | [T029](tasks/08_M24_Statements/auditsphere-r2r-task-t029-canonical-statement-sets-line-drilldown.md), [T060](tasks/18_Audit_Fieldwork_Core/auditsphere-audit-task-t060-opening-balance-verification.md) | NOT_STARTED | WAITING: T029, T060 | Unassigned |
| [ ] | [T074 — Implement going concern audit evaluation](tasks/20_Audit_Completion/auditsphere-audit-task-t074-going-concern-evaluation.md) | [T069](tasks/19_Audit_Fieldwork_Extended/auditsphere-audit-task-t069-loans-borrowings-covenants.md), [T073](tasks/20_Audit_Completion/auditsphere-audit-task-t073-substantive-final-analytical-review.md) | NOT_STARTED | WAITING: T069, T073 | Unassigned |
| [ ] | [T075 — Implement subsequent events audit review](tasks/20_Audit_Completion/auditsphere-audit-task-t075-subsequent-events-review.md) | [T073](tasks/20_Audit_Completion/auditsphere-audit-task-t073-substantive-final-analytical-review.md), [T074](tasks/20_Audit_Completion/auditsphere-audit-task-t074-going-concern-evaluation.md) | NOT_STARTED | WAITING: T073, T074 | Unassigned |
<!-- END TASKS -->







<a id="module-map"></a>



## 6. Module-to-task map







| Module / responsibility | Implementation tasks | Original work packages | Full six-part source specification |



|---|---|---|---|



| Shared foundation | T001–T010 | R2R-00–02 | [Architecture](reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md), [shared contracts](reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md) |



| 20 — Accounting | T011–T014; close/amendment T038–T040 | R2R-03, R2R-11 | [Accounting](modules/auditsphere-r2r-module-20-accounting-workspace-contract.md) |



| 21 — Trial Balance & GL | T015–T020 | R2R-04–05 | [TB, GL and mappings](modules/auditsphere-r2r-module-21-trial-balance-gl-contract.md) |



| 22 — Adjustments & Journals | T021–T023 | R2R-06 | [Adjustments](modules/auditsphere-r2r-module-22-adjustments-journals-contract.md) |



| 23 — Reconciliations | T024–T026 | R2R-07 | [Reconciliations](modules/auditsphere-r2r-module-23-reconciliations-specialist-schedules-contract.md) |



| 24 — Financial Statements | T027–T033 | R2R-08–09 | [Statements](modules/auditsphere-r2r-module-24-financial-statements-contract.md) |



| 25 — Financial Packages | T034–T037; handoff T040; group reuse T047 | R2R-10–11, R2R-14 | [Packages](modules/auditsphere-r2r-module-25-financial-packages-contract.md) |



| 26 — Consolidation | T041–T047 | R2R-12–14 | [Consolidation](modules/auditsphere-r2r-module-26-consolidation-contract.md) |



| Integrated acceptance | T048–T051 | R2R-15 | [Fixtures and journeys](reference/auditsphere-r2r-reference-golden-fixtures-and-integration-journeys.md) |



| Deployment and handover | T052–T054 | R2R-16 | [Readiness and handoff](reference/auditsphere-r2r-reference-execution-coordination-and-handover.md) |



| Audit Foundation | T055–T059 | AUD-17 | [Audit Traceability](tracking/auditsphere-audit-tracker-workflow-traceability.md) |



| Audit Fieldwork (Core) | T060–T065 | AUD-18 | [Audit Traceability](tracking/auditsphere-audit-tracker-workflow-traceability.md) |



| Audit Fieldwork (Extended) | T066–T072 | AUD-19 | [Audit Traceability](tracking/auditsphere-audit-tracker-workflow-traceability.md) |



| Audit Completion & Review | T073–T075 | AUD-20 | [Audit Traceability](tracking/auditsphere-audit-tracker-workflow-traceability.md) |







Module completion requires its implementation tasks **and** applicable integrated criteria/consumer tests, not simply the last local task in a row. External identity, document, engagement, review, release and archive modules remain existing dependencies, not extra modules to rebuild under this request.







<a id="source-coverage"></a>



## 7. Complete source and coverage references







| File | Purpose |



|---|---|



| [Unchanged original blueprint](source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md) | Byte-for-byte source of this breakdown; retained for conflict resolution and historical provenance |



| [Original audit workflow user stories](source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md) | Full 28-story, 165-procedure, 20-section audit workflow source document |



| [Scope/evidence boundaries](reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md) | Included/excluded functionality and meaning of EXISTING/EXTEND/NEW/DECISION |



| [Baseline, architecture and ten ADRs](reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md) | Reuse plan, architecture transition and approvals |



| [Research and source register](reference/auditsphere-r2r-reference-standards-and-source-register.md) | Original standards, product and engineering citations; not newly researched |



| [Shared domain, persistence, CQRS and Blazor](reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md) | Common IDs, precision, migrations, DTOs, transaction and form rules |



| [Lifecycle, invalidation and ports](reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md) | Cross-module coordination and owner/consumer boundaries |



| [Original execution and handover rules](reference/auditsphere-r2r-reference-execution-coordination-and-handover.md) | All 17 original work packages, agent protocol and final gates |



| [Clarifications and resource bounds](reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md) | Exact shorthand meaning and proposed limits requiring approval |



| [Fixtures, journeys and evidence standard](reference/auditsphere-r2r-reference-golden-fixtures-and-integration-journeys.md) | Original synthetic numerical fixtures, 30 failure/lifecycle journeys and verification layers |



| [Original 52 acceptance criteria](reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md) | Exact wording plus the blueprint's production interpretation |



| [111 command/query owners](coverage/auditsphere-r2r-tracker-command-query-ownership.md) | Unique task ownership for every explicit request contract |



| [Criterion result ledger](coverage/auditsphere-r2r-tracker-acceptance-criteria.md) | Editable criterion-by-criterion acceptance results |



| [Integration journey result ledger](coverage/auditsphere-r2r-tracker-integration-journeys.md) | Editable full-journey evidence |



| [Golden fixture approval/results](coverage/auditsphere-r2r-tracker-golden-fixtures.md) | Separate professional approval and numerical proof |



| [Audit workflow clarifications](reference/auditsphere-r2r-reference-audit-workflow-consolidation-clarifications.md) | Architectural, boundary, and product-scope determinations for audit consolidation |



| [Audit workflow traceability ledger](tracking/auditsphere-audit-tracker-workflow-traceability.md) | Traceability ledger mapping 28 AS-AUD stories, 258 criteria and 165 AWP procedures to canonical tasks |



| [Status history](tracking/auditsphere-r2r-tracker-status-history.md) | Append-only documentation history for task changes |



| [Pack manifest](tracking/pack_manifest.json) | Source hash, task paths, dependency graph and original coverage inventories |



| [Local status helper](tools/task_status.py) | Optional local status transitions, index refresh and structural checks |



| [Helper self-tests](tools/test_task_status.py) | Twelve checks run against disposable copies of this pack; not application tests |



| [Pack validation record](tracking/auditsphere-r2r-report-pack-validation.md) | Structural checks performed on the delivered ZIP contents |







The source appendices retain prototype-specific wording where present. Apply the source's stated production interpretations; do not silently rewrite an original criterion to claim it passed. Unresolved matters (for example a separately enumerated taxonomy command or production output labelling) require the named architecture/policy decision, not an invented contract.







### Evidence record template







Create a real evidence record under `tracking/` or use the project's approved evidence store. Do not prefill a successful result.







```text



Task / original requirement / R2R journey:



Owner and independent reviewer:



Current source commit and schema:



Policy / scope approval reference:



Fixture version, actor and scope:



Source / result / artifact references:



Command / test method / browser scenario:



Expected outcome:



Observed outcome and run reference:



Failure / retry / concurrency / rework assertions:



Known limitation or blocker:



Consumer handoff:



Reviewer decision and timestamp:



```







<a id="agent-instructions"></a>



## 8. Instructions for a coding agent







Open this index and choose the next approved dependency-ready task. Read its module and shared references before coding. Inspect the actual repository head and map EXISTING/EXTEND/NEW/DECISION symbols. Do not rebuild working functionality, introduce duplicate accounting state, rename wire values, broaden scope, infer accounting policy or create an endpoint because its proposed name looks plausible.







Implement the smallest coherent vertical slice, including its domain rules, actual persistence delta, owned request/validator contracts, consumer manifests, existing Blazor route and required tests. A task with no public request ownership consumes the existing contract instead of defining a competing one. Stop and mark BLOCKED on a concrete unresolved policy/contract contradiction.







Update the task and exact evidence, request independent review, then refresh the index. Preserve old evidence if reopening a dependency. Never change expected financial answers to match faulty output. Task status cannot authorize a merge, deployment or Microsoft tenant operation; preserve the project's separate authorization process.







**Completion of this documentation pack:** source decomposition, numbering, links and dependencies can be validated locally. **Completion of STEAuditSphere:** requires the actual approved code, professional decisions, tests, migrations, provider evidence and final handover. Those are not performed by generating or validating this ZIP.
