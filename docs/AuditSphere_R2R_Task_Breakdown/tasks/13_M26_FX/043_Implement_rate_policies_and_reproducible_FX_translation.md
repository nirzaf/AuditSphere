---
id: "T043"
work_package: "R2R-13"
modules: [26]
status: "NOT_STARTED"
depends_on: ["T042"]
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
# T043 — Implement rate policies and reproducible FX translation

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Original work-package order](../../reference/05_Execution_Coordination_and_Handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Translate approved component results with explicit rate direction, date, purpose and reserve lineage.

**Original work package:** `R2R-13` — M26 versioned rates/policy, translation bridges and manual intercompany review
**Original package exit:** Same-currency and defined FX profiles; missing-rate/unsupported-method denial.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T042 — Pin and replace approved component packages](../12_M26_Perimeter/042_Pin_and_replace_approved_component_packages.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/03_Shared_Domain_Persistence_CQRS_Blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/04_Lifecycle_Lineage_and_Module_Ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/06_Contract_Clarifications_and_Resource_Limits.md)
- [02 Standards and Source Register](../../reference/02_Standards_and_Source_Register.md)
- [26 Consolidation Contract](../../modules/26_Consolidation_Contract.md)

## Sequential work

1. Reuse rate sets/policies and distinguish transaction remeasurement from foreign-operation translation.
2. Implement positive bounded rate entry/review and complete historical/representative-average/closing coverage for the enabled profile.
3. Calculate an exact immutable translation bridge and separately explain CTA and rounding.
4. Build rate/policy editors and original/rate/translated/bridge views; no online FX feed.

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
| `SaveExchangeRateSetCommand(RateSetDraftDto, Meta)` | `MutationReceiptDto` | Positive bounded rates, correct pairs/types/dates, no ambiguous duplicates or implied direction. |
| `ReviewExchangeRateSetCommand(RateSetRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Independent review, source evidence, complete required date/type coverage. |
| `PublishTranslationPolicyCommand(TranslationPolicyDraftDto, DecisionInputDto, Meta)` | `MutationReceiptDto` | Approved method and golden fixtures; historical/average/closing rules complete. |
| `TranslateComponentCommand(ComponentPinRef, RateSetRef, TranslationPolicyRef, Meta)` | `OperationTicketDto` | Required current rates and compatible method; deterministic translation and reserve bridge. |
| `GetTranslationBridgeQuery(TranslationRef, PageRequest)` | `PageDto<TranslationLineDto>` | Original, rate/purpose, translated value, historical/CTA/rounding lineage. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 26: exact input/output DTO fields and supporting request rules](../../modules/26_Consolidation_Contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/01_Command_Query_Ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Reviewed rates/policy and translated component snapshot.
- FX bridge and method-limit UI.

**Direct consumers unlocked by this task:**

- [T044 — Implement manual intercompany pair review and exceptions](044_Implement_manual_intercompany_pair_review_and_exceptions.md)

- [Module 26: exact producer/consumer boundaries](../../modules/26_Consolidation_Contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 26: required component tree, state and forms](../../modules/26_Consolidation_Contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- GOLD-R2R-07 yields cash 400 and translation reserve -48 with signed total zero.
- Missing rates never become one; wrong direction and unsupported methods block.
- A universal closing rate is not applied to all equity/income lines.

**Source-named test families (proposed unless current source confirms them):**

- Module 26: `ConsolidationPerimeterTests`, `ConsolidationCurrencyAndEliminationTests`, `ConsolidationInputManifestTests`, `ConsolidationWorkspaceTests`, `R2R26GroupJourneys`. [Exact source cases](../../modules/26_Consolidation_Contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-044-AC02 | Missing rates, incompatible basis/period or unreviewed component packages block group output; missing amounts never default to zero. |
| VP-044-AC03 | The fixture’s translated values and rounding reconcile to published test expectations; every rate and translation difference is traceable. |
| VP-044-AC04 | An unapproved/unsupported translation rule shows a limitation and no fabricated consolidation result; component client packages remain unchanged. |

**Related original stories:** `VP-044`. [Full preserved wording and production interpretation](../../reference/08_Original_52_Acceptance_Criteria.md).

**Related integration journeys:** [R2R-AT-23](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-23), [R2R-AT-25](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-25).

**Golden fixtures:** [GOLD-R2R-07](../../coverage/04_Golden_Fixture_Tracking.md#gold-r2r-07). Fixture use requires the original policy approval; the numbers are synthetic QA values.

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
