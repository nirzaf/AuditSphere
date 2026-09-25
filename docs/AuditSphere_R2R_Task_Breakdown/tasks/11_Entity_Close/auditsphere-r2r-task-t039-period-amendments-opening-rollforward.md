---
id: "T039"
work_package: "R2R-11"
modules: [20, 21, 24, 25]
status: "NOT_STARTED"
depends_on: ["T038"]
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
# T039 — Implement period amendments and controlled opening roll-forward

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Preserve closed-period output while opening a reasoned successor workflow and exact next-period bridge.

**Original work package:** `R2R-11` — M20 close/amendment and entity package→review→release/archive boundary integration
**Original package exit:** End-to-end entity lifecycle, fresh amendment and prior-period opening bridge without historical mutation.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T038 — Implement close readiness and controlled period close](auditsphere-r2r-task-t038-close-readiness-controlled-period-close.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [20 Accounting Contract](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md)
- [21 Trial Balance and GL Contract](../../modules/auditsphere-r2r-module-21-trial-balance-gl-contract.md)
- [24 Financial Statements Contract](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md)
- [25 Financial Packages Contract](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md)

## Sequential work

1. Reuse existing period amendment/restatement/opening bridge and RollForwardPeriodAsync service contracts.
2. Require closed predecessor, original package identity, explicit dates/basis/currency and authorized amendment reason.
3. Carry forward only approved permitted opening/configuration facts; never inherit completed tasks, evidence or current approvals.
4. Create fresh output/review applicability for amendments and expose predecessor/next-period links.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 20: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#domain)
- [Module 21: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-21-trial-balance-gl-contract.md#domain)
- [Module 24: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#domain)
- [Module 25: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 20: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#persistence)
- [Module 21: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-21-trial-balance-gl-contract.md#persistence)
- [Module 24: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#persistence)
- [Module 25: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `OpenPeriodAmendmentCommand(PeriodId, OriginalPackage: Ref, DecisionInputDto, Meta)` | `MutationReceiptDto` | Closed period only; new revision, reason, independent authority; original output retained. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 20: exact input/output DTO fields and supporting request rules](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Period amendment and existing-service roll-forward integration.
- Opening source lineage and historical-preservation UI.

**Direct consumers unlocked by this task:**

- [T040 — Integrate entity package, release and logical archive handoffs](auditsphere-r2r-task-t040-entity-package-release-logical-archive.md)

- [Module 20: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#lineage)
- [Module 21: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-21-trial-balance-gl-contract.md#lineage)
- [Module 24: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#lineage)
- [Module 25: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 20: required component tree, state and forms](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#blazor)
- [Module 21: required component tree, state and forms](../../modules/auditsphere-r2r-module-21-trial-balance-gl-contract.md#blazor)
- [Module 24: required component tree, state and forms](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#blazor)
- [Module 25: required component tree, state and forms](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Original approved package and source rows remain unchanged.
- Missing or ambiguous opening sources are not defaulted to zero.
- This task does not invent an unlisted second roll-forward CQRS contract.

**Source-named test families (proposed unless current source confirms them):**

- Module 20: `ReportingContextInvariantTests`, `ReportingContextPersistenceTests`, `AccountingContextEditorTests`, `R2R20AccountingJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-20-accounting-workspace-contract.md#verification).
- Module 21: `AccountingImportContractTests`, `SourceRevisionIntegrityTests`, `CompletenessAndMappingTests`, `AccountingImportWizardTests`, `MappingEditorTests`, `R2R21SourceJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-21-trial-balance-gl-contract.md#verification).
- Module 24: `StatementLayoutAndPolicyTests`, `CashEquityComparativeTests`, `StatementRevisionLineageTests`, `StatementDesignerTests`, `DisclosureEditorTests`, `R2R24StatementJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#verification).
- Module 25: `PackageManifestTests`, `FinancialArtifactRoundTripTests`, `PackagePublicationIntegrityTests`, `PackageAssemblyComponentTests`, `R2R25PackageJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

**Related original stories:** `VP-034`, `VP-036`, `VP-040`. [Full preserved wording and production interpretation](../../reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md).

**Related integration journeys:** [R2R-AT-12](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-12), [R2R-AT-15](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-15), [R2R-AT-16](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-16), [R2R-AT-21](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-21), [R2R-AT-26](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-26).

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
