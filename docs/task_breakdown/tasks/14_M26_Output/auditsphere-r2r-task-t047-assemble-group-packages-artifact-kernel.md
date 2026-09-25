---
id: "T047"
work_package: "R2R-14"
modules: [25, 26]
status: "NOT_STARTED"
depends_on: ["T046"]
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
# T047 — Assemble reviewed group packages through the shared artifact kernel

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Complete group reporting using Module 25’s artifact/review contract rather than a second renderer.

**Original work package:** `R2R-14` — M26 elimination journals, deterministic group runs, independent review and group packages
**Original package exit:** Investment/equity and intercompany golden results, source nonmutation, group-only artifact scope.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T046 — Build deterministic consolidation results and independent review](auditsphere-r2r-task-t046-deterministic-consolidation-results-review.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [25 Financial Packages Contract](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md)
- [26 Consolidation Contract](../../modules/auditsphere-r2r-module-26-consolidation-contract.md)

## Sequential work

1. Implement the group branch of package scope with the exact approved GroupResultRef.
2. Reuse composition/render/validation/sealing/read APIs, without recomputing components or borrowing a client scope.
3. Bind group decisions to exact perimeter/component/rate/elimination/artifact manifests.
4. Execute same-currency and approved FX group output, downloads and release/archive contract handoff.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 25: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#domain)
- [Module 26: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 25: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#persistence)
- [Module 26: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `PrepareGroupPackageCommand(ApprovedRunRef, PackageDefinitionDraftDto, Meta)` | `MutationReceiptDto` | Group branch of M25 contract only; no recalculation of component sources or borrowed client scope. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 26: exact input/output DTO fields and supporting request rules](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Exact reviewed group package and source/FX/elimination lineage.
- Module 26 end-to-end acceptance and shared-renderer regression evidence.

**Direct consumers unlocked by this task:**

- [T048 — Verify cross-module authority, replay and race conditions](../15_Acceptance/auditsphere-r2r-task-t048-cross-module-authority-replay-race-conditions.md)

- [Module 25: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#lineage)
- [Module 26: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 25: required component tree, state and forms](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#blazor)
- [Module 26: required component tree, state and forms](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Group output files reconcile to reviewed run columns.
- Unauthorized component/source details do not leak through group export.
- No mutual entity-close/group-close dependency is introduced.

**Source-named test families (proposed unless current source confirms them):**

- Module 25: `PackageManifestTests`, `FinancialArtifactRoundTripTests`, `PackagePublicationIntegrityTests`, `PackageAssemblyComponentTests`, `R2R25PackageJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#verification).
- Module 26: `ConsolidationPerimeterTests`, `ConsolidationCurrencyAndEliminationTests`, `ConsolidationInputManifestTests`, `ConsolidationWorkspaceTests`, `R2R26GroupJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-046-AC04 | Exported group demo artifacts preserve these references and exclude unrelated client information; no group action posts into component or firm ledgers. |

**Related original stories:** `VP-044`, `VP-046`. [Full preserved wording and production interpretation](../../reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md).

**Related integration journeys:** [R2R-AT-22](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-22), [R2R-AT-23](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-23), [R2R-AT-25](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-25), [R2R-AT-26](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-26), [R2R-AT-30](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-30).

**Golden fixtures:** [GOLD-R2R-06](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-06), [GOLD-R2R-07](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-07). Fixture use requires the original policy approval; the numbers are synthetic QA values.

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
