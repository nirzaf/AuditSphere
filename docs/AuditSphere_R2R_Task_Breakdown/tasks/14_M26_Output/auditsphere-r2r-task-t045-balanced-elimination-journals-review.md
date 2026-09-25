---
id: "T045"
work_package: "R2R-14"
modules: [26]
status: "NOT_STARTED"
depends_on: ["T044"]
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
# T045 — Build balanced elimination journals and independent review

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Record group-only adjustments once without changing component or firm ledgers.

**Original work package:** `R2R-14` — M26 elimination journals, deterministic group runs, independent review and group packages
**Original package exit:** Investment/equity and intercompany golden results, source nonmutation, group-only artifact scope.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T044 — Implement manual intercompany pair review and exceptions](../13_M26_FX/auditsphere-r2r-task-t044-manual-intercompany-pair-review-exceptions.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [26 Consolidation Contract](../../modules/auditsphere-r2r-module-26-consolidation-contract.md)

## Sequential work

1. Reuse group-journal/match structures; bind exact scope, component/rate dependencies and evidence.
2. Implement arbitrary valid debit/credit lines, allowed elimination kinds and unique source consumption.
3. Provide draft/submit/return/review/amend behavior with person-based independence.
4. Cover investment/equity as well as intercompany receivable/payable effects for the supported wholly-owned fixture.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 26: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 26: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `SaveGroupJournalCommand(GroupJournalDraftDto, Meta)` | `MutationReceiptDto` | Valid distinct lines/accounts/currency and source-consumption identity; draft totals visible. |
| `SubmitGroupJournalCommand(JournalId, Meta)` | `MutationReceiptDto` | Exact balance, evidence, current input set and no duplicate elimination. |
| `ReviewGroupJournalCommand(JournalRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Independent reviewer and live input/currentness checks; return reason mandatory. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 26: exact input/output DTO fields and supporting request rules](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Reviewed exact group journals/eliminations and consumption identity.
- Elimination register/editor and revision history.

**Direct consumers unlocked by this task:**

- [T046 — Build deterministic consolidation results and independent review](auditsphere-r2r-task-t046-deterministic-consolidation-results-review.md)

- [Module 26: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 26: required component tree, state and forms](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Unbalanced/mixed-context/duplicate source inclusion is rejected.
- GOLD-R2R-06 removes IC 1,000 and investment/equity 2,000 exactly once.
- Component source TB/package and firm ledger remain unchanged.

**Source-named test families (proposed unless current source confirms them):**

- Module 26: `ConsolidationPerimeterTests`, `ConsolidationCurrencyAndEliminationTests`, `ConsolidationInputManifestTests`, `ConsolidationWorkspaceTests`, `R2R26GroupJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-045-AC01 | Given a balanced supported elimination, when independently approved, then it affects group output once and neither component book/package is modified. |
| VP-045-AC02 | Unbalanced lines, unsupported counterparties, mixed contexts and duplicate source inclusion are rejected. |

**Related original stories:** `VP-045`. [Full preserved wording and production interpretation](../../reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md).

**Related integration journeys:** [R2R-AT-04](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-04), [R2R-AT-05](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-05), [R2R-AT-24](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-24), [R2R-AT-25](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-25).

**Golden fixtures:** [GOLD-R2R-06](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-06). Fixture use requires the original policy approval; the numbers are synthetic QA values.

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
