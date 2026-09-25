---
id: "T040"
work_package: "R2R-11"
modules: [20, 25]
status: "NOT_STARTED"
depends_on: ["T039"]
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
# T040 — Integrate entity package, release and logical archive handoffs

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Original work-package order](../../reference/05_Execution_Coordination_and_Handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Prove the complete entity lifecycle through the existing release/archive owners.

**Original work package:** `R2R-11` — M20 close/amendment and entity package→review→release/archive boundary integration
**Original package exit:** End-to-end entity lifecycle, fresh amendment and prior-period opening bridge without historical mutation.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T039 — Implement period amendments and controlled opening roll-forward](039_Implement_period_amendments_and_controlled_opening_roll_forward.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/03_Shared_Domain_Persistence_CQRS_Blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/04_Lifecycle_Lineage_and_Module_Ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/06_Contract_Clarifications_and_Resource_Limits.md)
- [20 Accounting Contract](../../modules/20_Accounting_Contract.md)
- [25 Financial Packages Contract](../../modules/25_Financial_Packages_Contract.md)

## Sequential work

1. Connect package readiness and stage decisions to the existing completion owner without copying its database or issuing from Module 25.
2. Preserve exact sealed artifact identity through freeze, release and logical archive handoff.
3. Apply the approved no-Purview/no-eSignature scope change narrowly; retain human review, manifests, idempotency and historical evidence.
4. Execute entity end-to-end close/amend/release/archive fixtures and record consumer contract tests.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 20: exact models, invariants and state transitions](../../modules/20_Accounting_Contract.md#domain)
- [Module 25: exact models, invariants and state transitions](../../modules/25_Financial_Packages_Contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 20: exact mappings, keys, indexes and migration proposals](../../modules/20_Accounting_Contract.md#persistence)
- [Module 25: exact mappings, keys, indexes and migration proposals](../../modules/25_Financial_Packages_Contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

No new public command/query is assigned to this task. Configure, verify or connect the contracts of the owning tasks. Any additional request name requires a recorded contract decision rather than an agent-generated assumption.

Full one-owner registry: [111 command/query contracts](../../coverage/01_Command_Query_Ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Entity release/archive interoperability evidence.
- Resolved entity R2R lifecycle with fresh amendment/review history.

**Direct consumers unlocked by this task:**

- [T048 — Verify cross-module authority, replay and race conditions](../15_Acceptance/048_Verify_cross_module_authority_replay_and_race_conditions.md)

- [Module 20: exact producer/consumer boundaries](../../modules/20_Accounting_Contract.md#lineage)
- [Module 25: exact producer/consumer boundaries](../../modules/25_Financial_Packages_Contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 20: required component tree, state and forms](../../modules/20_Accounting_Contract.md#blazor)
- [Module 25: required component tree, state and forms](../../modules/25_Financial_Packages_Contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Archive receives reviewed released bytes, not regenerated balances.
- No forbidden provider or automatic professional decision is introduced.
- Entity lifecycle completes without enabling consolidation.

**Source-named test families (proposed unless current source confirms them):**

- Module 20: `ReportingContextInvariantTests`, `ReportingContextPersistenceTests`, `AccountingContextEditorTests`, `R2R20AccountingJourneys`. [Exact source cases](../../modules/20_Accounting_Contract.md#verification).
- Module 25: `PackageManifestTests`, `FinancialArtifactRoundTripTests`, `PackagePublicationIntegrityTests`, `PackageAssemblyComponentTests`, `R2R25PackageJourneys`. [Exact source cases](../../modules/25_Financial_Packages_Contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

**Related original stories:** `VP-034`, `VP-042`. [Full preserved wording and production interpretation](../../reference/08_Original_52_Acceptance_Criteria.md).

**Related integration journeys:** [R2R-AT-01](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-01), [R2R-AT-21](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-21), [R2R-AT-26](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-26), [R2R-AT-30](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-30).

**Golden fixtures:** [GOLD-R2R-01](../../coverage/04_Golden_Fixture_Tracking.md#gold-r2r-01), [GOLD-R2R-03](../../coverage/04_Golden_Fixture_Tracking.md#gold-r2r-03), [GOLD-R2R-04](../../coverage/04_Golden_Fixture_Tracking.md#gold-r2r-04), [GOLD-R2R-05](../../coverage/04_Golden_Fixture_Tracking.md#gold-r2r-05), [GOLD-R2R-08](../../coverage/04_Golden_Fixture_Tracking.md#gold-r2r-08). Fixture use requires the original policy approval; the numbers are synthetic QA values.

### Audit completion review, representation letter and release gates
- Perform and record engagement partner second review of all completed audit sections and workpapers (`AWP-20-01`).
- Enforce Engagement Quality Review (EQR) sign-off where mandated by firm policy or engagement risk profile (`AWP-20-02`).
- Require and verify uploaded signed management representations letter covering required ISA 580 matters (`AWP-20-05`).
- Formulate and record the auditor's professional opinion (unmodified, qualified, adverse, disclaimer) (`AWP-20-07`).
- Enforce audit report dating rule: report date must not precede the date on which all necessary audit evidence was obtained (`AWP-20-09`).
- Execute final audit report release workflow and atomically lock the engagement archive file (`AWP-20-10`).

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-027 | Primary |
| AS-AUD-027-AC01 | Covered |
| AS-AUD-027-AC02 | Covered |
| AS-AUD-027-AC05 | Covered |
| AS-AUD-027-AC07 | Covered |
| AS-AUD-027-AC09 | Covered |
| AS-AUD-027-AC10 | Covered |
| AS-AUD-027-AC11 | Covered |
| AS-AUD-027-AC12 | Covered |
| AS-AUD-027-AC13 | Covered |
| AS-AUD-027-AC14 | Covered (Blocked external) |
| AWP-20-01 | Covered |
| AWP-20-02 | Covered |
| AWP-20-05 | Covered |
| AWP-20-07 | Covered |
| AWP-20-09 | Covered |
| AWP-20-10 | Covered |

Source procedures (preserved wording):

- `AWP-20-01` Ensure all audit sections are completed and cross-referenced.
- `AWP-20-02` Ensure all review points have been cleared.
- `AWP-20-05` Complete going-concern and subsequent-event procedures.
- `AWP-20-07` Obtain signed management representation letter.
- `AWP-20-09` Complete senior/manager/partner review.
- `AWP-20-10` Finalize the auditor's report and signed financial statements.

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
