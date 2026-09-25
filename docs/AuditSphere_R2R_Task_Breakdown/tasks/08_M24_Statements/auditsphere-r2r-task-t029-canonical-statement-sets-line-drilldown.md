---
id: "T029"
work_package: "R2R-08"
modules: [24]
status: "NOT_STARTED"
depends_on: ["T028"]
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
# T029 — Build canonical statement sets and exact line drill-down

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Calculate and persist the canonical accounting result consumed by package rendering.

**Original work package:** `R2R-08` — M24 versioned layout, current/prior balances, line drill-down and statement-set snapshots
**Original package exit:** Approved mapping → exact current/prior lines; no parent/child double count; missing prior visible.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T028 — Implement comparative-basis selection and historical differences](auditsphere-r2r-task-t028-comparative-basis-selection-differences.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [24 Financial Statements Contract](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md)

## Sequential work

1. Combine exact context, accepted sources, effective adjustment snapshot, mapping, layout and comparative pins.
2. Persist per-line contributions and validation snapshots; supplement references may be incomplete until T030–T033.
3. Provide BS/P&L/equity/cash-flow views only to the extent of supported supplied inputs; explicit missing-support gates remain.
4. Implement exact source-line drill-down and source-change invalidation without rendering from mutable live balances.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 24: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 24: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `BuildStatementSetCommand(StatementBuildInputDto, Meta)` | `OperationTicketDto` | Complete approved inputs; capture membership revisions; worker uses immutable inputs and fenced publication. |
| `GetStatementWorkspaceQuery(Context)` | `StatementWorkspaceDto` | Selected source, mapping/layout/supplementary readiness and eligible comparative options. |
| `GetStatementSetQuery(StatementSetRef)` | `StatementSetDto` | Typed lines, current/prior values, OCI/equity/cash/notes, validations and manifest. |
| `GetStatementLineageQuery(StatementSetRef, LineId, PageRequest)` | `PageDto<StatementContributionDto>` | Source accounts/journal contributions/mapping fractions plus schedule references; no hidden source access. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 24: exact input/output DTO fields and supporting request rules](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Canonical statement-set snapshot and line contributions.
- Readiness-aware statement workspace for supplemental completion.

**Direct consumers unlocked by this task:**

- [T030 — Build source-backed cash-flow schedules](../09_M24_Supplements/auditsphere-r2r-task-t030-source-backed-cash-flow-schedules.md)
- [T031 — Build equity-movement schedules](../09_M24_Supplements/auditsphere-r2r-task-t031-equity-movement-schedules.md)
- [T032 — Build disclosure notes and reviewed applicability](../09_M24_Supplements/auditsphere-r2r-task-t032-disclosure-notes-reviewed-applicability.md)

- [Module 24: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 24: required component tree, state and forms](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- GOLD-R2R-01 gives adjusted assets 17,900 and profit 3,900.
- Parent/leaf and retained-earnings/period-result double counting is prevented.
- A partial statement set cannot pass final review or package readiness before required schedules/notes exist.

**Source-named test families (proposed unless current source confirms them):**

- Module 24: `StatementLayoutAndPolicyTests`, `CashEquityComparativeTests`, `StatementRevisionLineageTests`, `StatementDesignerTests`, `DisclosureEditorTests`, `R2R24StatementJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-037-AC04 | Every generated line exposes its mapping/source references; no unrecognized account is silently assigned a zero balance or miscellaneous category. |
| VP-040-AC01 | Given valid mapped current/prior periods, when statements are built, then each column and subtotal reconciles to its selected source; a missing prior period shows unavailable, not zero. |
| VP-040-AC02 | Assets, liabilities/equity and period-result movements reconcile in the supported fixture; invalid totals display blocking validation. |
| VP-040-AC04 | The preview provides complete visible structure, editing and drill-down for the supported demonstration; unsupported calculations never render invented balanced figures. |

**Related original stories:** `VP-040`. [Full preserved wording and production interpretation](../../reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md).

**Related integration journeys:** [R2R-AT-01](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-01), [R2R-AT-10](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-10), [R2R-AT-15](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-15), [R2R-AT-17](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-17).

**Golden fixtures:** [GOLD-R2R-01](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-01). Fixture use requires the original policy approval; the numbers are synthetic QA values.

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
