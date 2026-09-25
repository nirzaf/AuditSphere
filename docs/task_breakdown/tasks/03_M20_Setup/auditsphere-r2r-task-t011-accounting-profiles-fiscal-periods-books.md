---
id: "T011"
work_package: "R2R-03"
modules: [20]
status: "NOT_STARTED"
depends_on: ["T010"]
owner: ""
reviewer: ""
review_decision: ""
reviewed_commit: ""
evidence_ref: ""
approval_ref: ""
blocked_reason: ""
branch: ""
issue_pr: ""
updated_at: ""
---
# T011 — Build accounting profiles, fiscal periods and reporting books

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Create real accounting setup with explicit owner, basis, currency and historical revisions.

**Original work package:** `R2R-03` — M20 profiles, approved chart/taxonomy/dimension versions, periods/books and context activation
**Original package exit:** Complete valid/invalid context lifecycle with historical preservation and source handoff.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T010 — Build shared Blazor forms, state, dirty guards and conflict UI](../02_Shared_Foundation/auditsphere-r2r-task-t010-shared-blazor-forms-state-dirty-guards.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [20 Accounting Contract](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md)

## Sequential work

1. Reuse ClientAccountingProfile, ClientReportingPeriod and ClientReportingBook; map existing states before adding logical transitions.
2. Implement the owned requests with field, fiscal-calendar, ownership and revision checks; proposed DTO fields are defined in the module contract.
3. Add necessary composite FKs/indexes and revision preservation; quarantine ambiguous historical setup rather than backfilling guesses.
4. Extend profile/period/book panels and separate EditContexts. Declare close/readiness contracts but defer their complete implementation to T038–T040.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 20: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 20: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `CreateAccountingProfileCommand(ProfileDraftDto, Meta)` | `MutationReceiptDto` | Valid actual fiscal date, currency and client; reject duplicate profile; authorized creator. |
| `ReviseAccountingProfileCommand(ProfileId, ProfileDraftDto, Meta)` | `MutationReceiptDto` | Exact predecessor/revision; changed accounting basis requires reason and explicit impact preview. |
| `DefineReportingPeriodCommand(PeriodDraftDto, Meta)` | `MutationReceiptDto` | Inclusive dates, same-owner prior period, approved calendar/period kind, no ambiguous overlap. |
| `CreateReportingBookCommand(BookDraftDto, Meta)` | `MutationReceiptDto` | Period open, same basis/currency/client; unique code. |
| `GetAccountingWorkspaceQuery(ClientId, PageRequest)` | `AccountingWorkspaceDto` | Authorized profile summary, available periods/books/charts and capabilities; no sibling data. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 20: exact input/output DTO fields and supporting request rules](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Profile/period/book lifecycle and paged workspace DTO.
- Additive mapping/migration and integrated setup forms.

**Direct consumers unlocked by this task:**

- [T012 — Build chart and taxonomy revision authoring](auditsphere-r2r-task-t012-chart-taxonomy-revision-authoring.md)

- [Module 20: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 20: required component tree, state and forms](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Reject invalid dates, cross-client prior periods/books and duplicate owner-scoped codes.
- Used settings are revised, not overwritten.
- Empty setup has a clear permitted manual starting action.

**Source-named test families (proposed unless current source confirms them):**

- Module 20: `ReportingContextInvariantTests`, `ReportingContextPersistenceTests`, `AccountingContextEditorTests`, `R2R20AccountingJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

**Related original stories:** `VP-034`. [Full preserved wording and production interpretation](../../reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md).

**Related integration journeys:** [R2R-AT-02](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-02), [R2R-AT-27](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-27).

## Completion checklist

<!-- COMPLETION-CHECKLIST -->
- [ ] Current checkout, existing symbols and applicable approvals were inspected; scope conflicts are resolved or the task is BLOCKED.
- [ ] All hard dependencies are COMPLETED and their exact contracts/evidence were consumed.
- [ ] The task-specific work and every applicable invariant/owned request are implemented or proven already implemented; no placeholder outcome remains.
- [ ] Applicable migrations, validation, authorization, concurrency and source/history preservation checks have observed results.
- [ ] Required task-level tests pass with named expected/observed outcomes; future integration tests remain explicitly tracked instead of claimed complete.
- [ ] The independent reviewer accepted the exact reviewed commit and evidence; downstream owners received the handoff.
<!-- END-COMPLETION-CHECKLIST -->

## Evidence and handoff record

| Field | Value to record |
|---|---|
| Inspected baseline and reused symbols | Not recorded |
| Code commit / schema / deployed build if applicable | Not recorded |
| Requirement → assertion → command/run → observed result | Not recorded |
| Policy / scope approval reference | Not recorded |
| Known limitations / exact blocker | Not recorded |
| Exported contract / manifest / artifact references for consumers | Not recorded |
| Reviewer and acceptance decision | Not recorded |

**Tracking-only note:** filling these fields or running the status helper is not proof that tests ran, a professional approval, merge authorization or permission to perform a tenant operation.
