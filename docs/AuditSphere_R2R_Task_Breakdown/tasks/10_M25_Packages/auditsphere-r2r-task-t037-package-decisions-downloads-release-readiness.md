---
id: "T037"
work_package: "R2R-10"
modules: [25]
status: "NOT_STARTED"
depends_on: ["T036"]
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
# T037 — Implement exact-package decisions, downloads and release readiness

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Complete package review and safe handoff without conflating sealing with approval or release.

**Original work package:** `R2R-10` — M25 composition, renderer reuse, genuine outputs, validation, sealing and exact decisions
**Original package exit:** All expected artifacts parse and reconcile; partial generation/tamper/source races fail closed.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T036 — Validate artifact sets and atomically seal packages](auditsphere-r2r-task-t036-validate-artifact-sets-seal-packages.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [25 Financial Packages Contract](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md)

## Sequential work

1. Record accounting/management/partner/group stages required by the approved profile against the complete exact artifact set.
2. Apply person-based independence, scope, evidence and currentness to each decision and queue count.
3. Reauthorize and verify bytes before download; no client-selected filesystem paths or broader source access.
4. Return the existing completion/release owner a readiness manifest and named blockers without issuing a release.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 25: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 25: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `RecordFinancialPackageDecisionCommand(PackageReviewInputDto, Meta)` | `MutationReceiptDto` | Exact current sealed content, correct separate stage/person/scope, current evidence; return/reject reason. |
| `GetPackageReviewQueueQuery(StageFilter, ScopeFilter, PageRequest)` | `PageDto<PackageReviewRowDto>` | Current actionable stages only; historical decisions shown distinctly; scope before counts. |
| `GetPackageArtifactQuery(ArtifactReadRequestDto)` | `AuthorizedArtifactReadDto` | Reauthorize and verify digest before download; no client-chosen filesystem path. |
| `GetPackageReleaseReadinessQuery(PackageRef)` | `ReadinessDto` | M37-compatible exact package/artifacts/decisions with unresolved blockers; no release side effect. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 25: exact input/output DTO fields and supporting request rules](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Applicable package decisions, permitted downloads and M37 readiness port.
- Complete Module 25 entity package contract.

**Direct consumers unlocked by this task:**

- [T038 — Implement close readiness and controlled period close](../11_Entity_Close/auditsphere-r2r-task-t038-close-readiness-controlled-period-close.md)
- [T041 — Build controlled group perimeters and group authorization](../12_M26_Perimeter/auditsphere-r2r-task-t041-group-perimeters-authorization.md)

- [Module 25: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 25: required component tree, state and forms](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Returned/replaced packages need current reviewed revisions.
- Downloads return the exact reviewed bytes and deny revoked/sibling access.
- Accounting-only management acknowledgement does not invent audit approval, eSignature or release.

**Source-named test families (proposed unless current source confirms them):**

- Module 25: `PackageManifestTests`, `FinancialArtifactRoundTripTests`, `PackagePublicationIntegrityTests`, `PackageAssemblyComponentTests`, `R2R25PackageJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-25-financial-packages-contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-042-AC03 | When package content changes, prior artifacts and decisions remain historical and a new artifact revision must be reviewed. |
| VP-042-AC04 | External sharing remains explicit and scope-bound; internal workpapers/comments are excluded from management/client outputs by default. |

**Related original stories:** `VP-042`. [Full preserved wording and production interpretation](../../reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md).

**Related integration journeys:** [R2R-AT-04](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-04), [R2R-AT-05](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-05), [R2R-AT-20](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-20), [R2R-AT-22](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-22), [R2R-AT-30](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-30).

### Audit package readiness and draft report linkage
- Verify client director/management approval and signed financial statement evidence before release readiness (`AWP-20-06`).
- Link and validate the draft independent auditor's report (ISA 700/705/706) against the financial package artifacts (`AWP-20-08`).

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-027 | Primary |
| AS-AUD-027-AC06 | Covered |
| AS-AUD-027-AC08 | Covered |
| AWP-20-06 | Covered |
| AWP-20-08 | Covered |

Source procedures (preserved wording):

- `AWP-20-06` Complete financial statement disclosure checklist.
- `AWP-20-08` Perform final analytical review.

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
