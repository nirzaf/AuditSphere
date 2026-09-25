---
id: "T042"
work_package: "R2R-12"
modules: [26]
status: "NOT_STARTED"
depends_on: ["T041"]
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
# T042 — Pin and replace approved component packages

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Accept exact compatible component results as immutable group inputs.

**Original work package:** `R2R-12` — M26 approved group/perimeter and exact component acceptance
**Original package exit:** Control/membership/scoping/currentness; no implicit Partner grant; no invented component values.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T041 — Build controlled group perimeters and group authorization](auditsphere-r2r-task-t041-group-perimeters-authorization.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [26 Consolidation Contract](../../modules/auditsphere-r2r-module-26-consolidation-contract.md)

## Sequential work

1. Resolve actual approved Module 25 package bytes/results/manifest and eligible membership, never caller-supplied balances.
2. Validate period, basis, policy, taxonomy/mapping and currency compatibility; external-pack reuse remains within the approved existing contract.
3. Persist explicit component pins/replacements and stale downstream currentness without altering source packages.
4. Build readiness, mismatch and replacement comparison with group-safe projections.

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
| `PinConsolidationComponentCommand(ComponentPinDto, Meta)` | `MutationReceiptDto` | Read server-owned package/approval/hash; membership and compatibility current; no caller-invented values. |
| `ReplaceComponentPinCommand(OldPinRef, ComponentPinDto, Meta)` | `MutationReceiptDto` | Explicit reviewed replacement, same intended member/purpose; new scope/run basis, history preserved. |
| `GetComponentReadinessQuery(ScopeVersionRef)` | `ComponentReadinessDto` | Required members, eligible exact package choices and explicit mismatches; no “first two clients”. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 26: exact input/output DTO fields and supporting request rules](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Versioned approved component pins and component-set membership manifest.
- Component readiness/selection UI and replacement history.

**Direct consumers unlocked by this task:**

- [T043 — Implement rate policies and reproducible FX translation](../13_M26_FX/auditsphere-r2r-task-t043-rate-policies-reproducible-fx-translation.md)

- [Module 26: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 26: required component tree, state and forms](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Unreviewed, incompatible, stale or forged component input is denied.
- A later component replacement does not silently refresh an approved group run.
- Private component artifacts require separate authority.

**Source-named test families (proposed unless current source confirms them):**

- Module 26: `ConsolidationPerimeterTests`, `ConsolidationCurrencyAndEliminationTests`, `ConsolidationInputManifestTests`, `ConsolidationWorkspaceTests`, `R2R26GroupJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-043-AC03 | Adding a component does not expand the operator’s access to its unrelated engagements; narrow group access exposes only approved component projections. |
| VP-044-AC01 | Given eligible component packages, when selected, then exact revisions are pinned and a subsequent replacement produces a stale-component warning rather than silent refresh. |

**Related original stories:** `VP-043`, `VP-044`. [Full preserved wording and production interpretation](../../reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md).

**Related integration journeys:** [R2R-AT-22](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-22), [R2R-AT-23](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-23), [R2R-AT-25](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-25), [R2R-AT-26](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-26).

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
