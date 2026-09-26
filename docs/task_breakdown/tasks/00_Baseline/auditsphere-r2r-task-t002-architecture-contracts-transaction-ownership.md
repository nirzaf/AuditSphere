---
id: "T002"
work_package: "R2R-00"
modules: []
status: "IN_REVIEW"
depends_on: ["T001"]
owner: "Codex implementation coordinator"
reviewer: ""
review_decision: ""
reviewed_commit: ""
evidence_ref: "tracking/auditsphere-r2r-tracker-architecture-transaction-ownership.md"
approval_ref: ""
blocked_reason: ""
branch: "master"
issue_pr: ""
updated_at: "2026-09-26T18:08:35+00:00"
---
# T002 — Approve architecture, contracts and transaction ownership

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Freeze the shared architecture decisions and ownership rules agents must use.

**Original work package:** `R2R-00` — Pin current SHA; inventory existing models/services/migrations/UI/tests; reconcile ADRs and scope conflicts
**Original package exit:** Existing/new symbol ledger, policy approvals, preserved baseline tests and dependency licence decisions.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T001 — Approve scope and inventory the current implementation](auditsphere-r2r-task-t001-baseline-current-state-inventory.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [01 Baseline Architecture and ADRs](../../reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md)
- [02 Standards and Source Register](../../reference/auditsphere-r2r-reference-standards-and-source-register.md)

## Sequential work

1. Record the architecture positions in R2R-ADR-01, 02, 05, 06, 07, 08 and 10 from the blueprint.
2. Inspect current transactions and designate one owner per command: compatibility service or migrated unit of work.
3. Approve compatible pinned MediatR and bUnit dependencies and the MediatR licensing decision; retain unrelated package versions.
4. Define the DTO/port ledger, migration serialization owner and capability-to-role mapping. Inspect group-creation authority rather than inheriting implicit Partner promotion.

**Current-architecture disposition proposed for review:** The [T002 decision ledger](../../tracking/auditsphere-r2r-tracker-architecture-transaction-ownership.md) retains static Application services and the existing xUnit/Playwright harness. MediatR and bUnit are absent and are not introduced or licensed by this task; a later package change would require its own review. The [111-request registry](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md) assigns each preserved request to one task, while the ledger assigns capability transaction boundaries and requires each owning task to prove the concrete method. No public command/query is added by T002.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

This foundation/coordination task changes only the shared contracts expressly described in its work steps. It does not independently create a new business aggregate.

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

## 3. Application & CQRS Contracts (`.Application`)

No new public command/query is assigned to this task. Configure, verify or connect the contracts of the owning tasks. Any additional request name requires a recorded contract decision rather than an agent-generated assumption.

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Approved architecture and dependency/licence records.
- Transaction-owner registry and shared-contract ownership ledger.

**Direct consumers unlocked by this task:**

- [T003 — Approve accounting policies, resource bounds and golden fixtures](auditsphere-r2r-task-t003-accounting-policies-resource-bounds-golden-fixtures.md)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- A facade cannot wrap a service-owned transaction in a second transaction.
- Module 24 owns accounting presentation; Module 25 owns composition/rendering.
- No duplicate R2R database, client/entity model, message broker or frontend is proposed.

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

## Completion checklist

<!-- COMPLETION-CHECKLIST -->
- [x] Current checkout, existing symbols and applicable approvals were inspected; scope conflicts are resolved or the task is BLOCKED.
- [x] All hard dependencies are COMPLETED and their exact contracts/evidence were consumed.
- [x] The task-specific work and every applicable invariant/owned request are implemented or proven already implemented; no placeholder outcome remains.
- [x] Applicable migrations, validation, authorization, concurrency and source/history preservation checks have observed results.
- [x] Required task-level tests pass with named expected/observed outcomes; future integration tests remain explicitly tracked instead of claimed complete.
- [ ] The independent reviewer accepted the exact reviewed commit and evidence; downstream owners received the handoff.
<!-- END-COMPLETION-CHECKLIST -->

## Evidence and handoff record

| Field | Value to record |
|---|---|
| Inspected baseline and reused symbols | [Architecture, transaction, shared-contract and role review ledger](../../tracking/auditsphere-r2r-tracker-architecture-transaction-ownership.md); accepted T001 inventory consumed. |
| Code commit / schema / deployed build if applicable | Group-authority source `11b3f3779866a34146980da9e9b942e10846adc6`; no schema or deployment change. Final review-ledger commit to be pinned at handoff. |
| Requirement → assertion → command/run → observed result | ADR-10 no implicit Partner promotion → PostgreSQL `CreateGroup_GrantsCreatorOnlyTheirFirmWideRole` passed 1/1; Release web build passed with 0 warnings/errors; full Release suite passed 418/418 (339 Domain, 7 API, 72 E2E); EF model check found no pending changes; task-pack validation and 21/21 helper self-tests passed. Exact observations are in `docs/execution/status.json`. |
| Policy / scope approval reference | Repository owner accepted T001 seven-module scope and exact inventory commit `042611a70afdc97c006bf9b75ba58037cc0654a3`; T002 architectural disposition is proposed for independent review, not yet approved. |
| Known limitations / exact blocker | Preserved request names are not all current code classes. Each downstream task must prove its concrete service, transaction, authorization, source fence and tests; task-owned shared types and legacy role variation remain for their owning tasks. Live tenant and production gates remain external. Independent T002 acceptance is pending. |
| Exported contract / manifest / artifact references for consumers | [T002 review ledger](../../tracking/auditsphere-r2r-tracker-architecture-transaction-ownership.md) and [111-request task ownership registry](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md). |
| Reviewer and acceptance decision | Pending independent exact-commit review. |

**Tracking-only note:** filling these fields or running the status helper is not proof that tests ran, a professional approval, merge authorization or permission to perform a tenant operation.
