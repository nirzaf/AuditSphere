---
id: "T032"
work_package: "R2R-09"
modules: [24]
status: "NOT_STARTED"
depends_on: ["T029"]
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
# T032 — Build disclosure notes and reviewed applicability

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Original work-package order](../../reference/05_Execution_Coordination_and_Handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Complete editable required notes without replacing professional judgement or exposing internal comments.

**Original work package:** `R2R-09` — M24 cash/equity schedules, notes, policy-edition rules, restatements and review
**Original package exit:** Complete supported statement set including required disclosures; subsequent-event/treatment decisions recorded.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T029 — Build canonical statement sets and exact line drill-down](../08_M24_Statements/029_Build_canonical_statement_sets_and_exact_line_drill_down.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/03_Shared_Domain_Persistence_CQRS_Blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/04_Lifecycle_Lineage_and_Module_Ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/06_Contract_Clarifications_and_Resource_Limits.md)
- [02 Standards and Source Register](../../reference/02_Standards_and_Source_Register.md)
- [24 Financial Statements Contract](../../modules/24_Financial_Statements_Contract.md)

## Sequential work

1. Define required note-set membership, structured note tables and bounded content with policy/template references.
2. Persist edits as revisions; blank is not Not Applicable and Not Applicable needs a reason and reviewer decision.
3. Link note/statement amounts and separate client-visible output from internal review content.
4. Build note editor/applicability/review forms and invalidate current output when the required note set or approved note changes.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 24: exact models, invariants and state transitions](../../modules/24_Financial_Statements_Contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 24: exact mappings, keys, indexes and migration proposals](../../modules/24_Financial_Statements_Contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `SaveDisclosureNoteCommand(DisclosureDraftDto, Meta)` | `MutationReceiptDto` | Applicability/content/rationale, typed table bounds, safe markup and valid same-context line/evidence refs. |
| `ReviewDisclosureNoteCommand(NoteRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Required content/evidence, exact version, independent reviewer; NoApplicable is evidenced not a blank bypass. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 24: exact input/output DTO fields and supporting request rules](../../modules/24_Financial_Statements_Contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/01_Command_Query_Ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Versioned disclosure catalogue/content and independent decisions.
- Required-note readiness and safe client-output projection.

**Direct consumers unlocked by this task:**

- [T033 — Complete statement review and restatement lifecycle](033_Complete_statement_review_and_restatement_lifecycle.md)

- [Module 24: exact producer/consumer boundaries](../../modules/24_Financial_Statements_Contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 24: required component tree, state and forms](../../modules/24_Financial_Statements_Contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Adding/removing a required note changes the membership manifest even with unchanged balances.
- Blank notes cannot silently pass as approved exemptions.
- Client output omits internal review commentary.

**Source-named test families (proposed unless current source confirms them):**

- Module 24: `StatementLayoutAndPolicyTests`, `CashEquityComparativeTests`, `StatementRevisionLineageTests`, `StatementDesignerTests`, `DisclosureEditorTests`, `R2R24StatementJourneys`. [Exact source cases](../../modules/24_Financial_Statements_Contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-041-AC02 | Not-applicable notes need a reason and reviewer decision; a blank note is not an approved exemption. |
| VP-041-AC04 | Client previews expose only deliberately shared note content; internal reviewer comments remain internal and no professional conclusion is autogenerated. |

**Related original stories:** `VP-041`. [Full preserved wording and production interpretation](../../reference/08_Original_52_Acceptance_Criteria.md).

**Related integration journeys:** [R2R-AT-04](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-04), [R2R-AT-17](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-17).

### Audit disclosure checklist and notes review requirements
- Complete and evaluate the financial statement disclosure checklist for the applicable reporting framework (IFRS / local GAAP) (`AWP-18-04`).
- Verify that critical accounting estimates, judgments, and key sources of estimation uncertainty are adequately disclosed (`AWP-18-06`).
- Review related party disclosures for completeness against the audit related-party register and findings (`AWP-18-08`).
- Verify that all required notes, segment reporting, and supplementary schedules are included without omission (`AWP-18-09`).

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-025 | Primary |
| AS-AUD-025-AC05 | Covered |
| AS-AUD-025-AC06 | Covered |
| AS-AUD-025-AC08 | Covered |
| AS-AUD-025-AC09 | Covered |
| AS-AUD-025-AC10 | Covered |
| AS-AUD-025-AC11 | Covered |
| AWP-18-04 | Covered |
| AWP-18-06 | Covered |
| AWP-18-08 | Covered |
| AWP-18-09 | Covered |

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
