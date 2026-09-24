---
id: "T041"
work_package: "R2R-12"
modules: [26]
status: "NOT_STARTED"
depends_on: ["T037"]
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
# T041 — Build controlled group perimeters and group authorization

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Original work-package order](../../reference/05_Execution_Coordination_and_Handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Create independent group scope and approved effective membership without implicit professional authority.

**Original work package:** `R2R-12` — M26 approved group/perimeter and exact component acceptance
**Original package exit:** Control/membership/scoping/currentness; no implicit Partner grant; no invented component values.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T037 — Implement exact-package decisions, downloads and release readiness](../10_M25_Packages/037_Implement_exact_package_decisions_downloads_and_release_readiness.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/03_Shared_Domain_Persistence_CQRS_Blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/04_Lifecycle_Lineage_and_Module_Ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/06_Contract_Clarifications_and_Resource_Limits.md)
- [02 Standards and Source Register](../../reference/02_Standards_and_Source_Register.md)
- [26 Consolidation Contract](../../modules/26_Consolidation_Contract.md)

## Sequential work

1. Reuse group/membership/grant models and explicitly reconcile creator-grant behavior.
2. Bind control evidence, ownership/economic interest, effective dates, reporting period and supported method.
3. Reject cycles, duplicate economic inclusion and unsupported profiles rather than falling back to full consolidation.
4. Build perimeter/workspace and independent review with group-specific dirty-context protection.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 26: exact models, invariants and state transitions](../../modules/26_Consolidation_Contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 26: exact mappings, keys, indexes and migration proposals](../../modules/26_Consolidation_Contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `CreateConsolidationGroupCommand(GroupDraftDto, Meta)` | `MutationReceiptDto` | Explicit group-creation authority; approved method/profile; no automatic professional-role promotion. |
| `SavePerimeterDraftCommand(PerimeterDraftDto, Meta)` | `MutationReceiptDto` | Same-firm entities, valid dates/control evidence, no cycles/duplicates/unsupported ownership. |
| `ReviewConsolidationPerimeterCommand(PerimeterRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Independent allowed reviewer, supported exact scope/method and compatible periods. |
| `GetConsolidationWorkspaceQuery(GroupId, ScopeVersionId?)` | `ConsolidationWorkspaceDto` | Perimeter/components/method/rates/journals/readiness and allowed actions. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 26: exact input/output DTO fields and supporting request rules](../../modules/26_Consolidation_Contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/01_Command_Query_Ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Approved group scope/perimeter revision and permission boundaries.
- Group configuration/review UI.

**Direct consumers unlocked by this task:**

- [T042 — Pin and replace approved component packages](042_Pin_and_replace_approved_component_packages.md)

- [Module 26: exact producer/consumer boundaries](../../modules/26_Consolidation_Contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 26: required component tree, state and forms](../../modules/26_Consolidation_Contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Control is not inferred solely from an ownership percentage.
- Group creation does not grant Partner authority.
- Adding a component does not grant access to its unrelated engagements/files.

**Source-named test families (proposed unless current source confirms them):**

- Module 26: `ConsolidationPerimeterTests`, `ConsolidationCurrencyAndEliminationTests`, `ConsolidationInputManifestTests`, `ConsolidationWorkspaceTests`, `R2R26GroupJourneys`. [Exact source cases](../../modules/26_Consolidation_Contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-043-AC01 | Given a valid group/perimeter, when saved, then the group has its own scope, revision and component links and no source client balances are changed. |
| VP-043-AC02 | Duplicate components, cycles, invalid ownership percentages and incompatible period/entity assignments are rejected. |
| VP-043-AC04 | The selected calculation profile and limitations are visible; unsupported ownership/accounting methods cannot silently fall back to full consolidation. |

**Related original stories:** `VP-043`. [Full preserved wording and production interpretation](../../reference/08_Original_52_Acceptance_Criteria.md).

**Related integration journeys:** [R2R-AT-02](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-02), [R2R-AT-22](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-22), [R2R-AT-23](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-23), [R2R-AT-26](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-26).

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
