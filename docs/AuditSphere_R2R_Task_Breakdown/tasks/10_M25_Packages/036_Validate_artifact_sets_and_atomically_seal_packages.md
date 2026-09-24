---
id: "T036"
work_package: "R2R-10"
modules: [25]
status: "NOT_STARTED"
depends_on: ["T035"]
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
# T036 — Validate artifact sets and atomically seal packages

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Original work-package order](../../reference/05_Execution_Coordination_and_Handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Publish an immutable complete artifact set only after validating exact content and bytes.

**Original work package:** `R2R-10` — M25 composition, renderer reuse, genuine outputs, validation, sealing and exact decisions
**Original package exit:** All expected artifacts parse and reconcile; partial generation/tamper/source races fail closed.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T035 — Render genuine XLSX, DOCX and PDF with durable operations](035_Render_genuine_XLSX_DOCX_and_PDF_with_durable_operations.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/03_Shared_Domain_Persistence_CQRS_Blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/04_Lifecycle_Lineage_and_Module_Ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/06_Contract_Clarifications_and_Resource_Limits.md)
- [25 Financial Packages Contract](../../modules/25_Financial_Packages_Contract.md)

## Sequential work

1. Use separate pre-render content and post-render artifact manifests to avoid self-referential file hashes.
2. Read stored bytes on the server, verify digests/MIME/round-trip content and required-format completeness.
3. Recheck source/membership/currentness and publish the immutable seal atomically under the owned transaction.
4. Expose exact manifests and block stale/tampered/partially persisted render attempts.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 25: exact models, invariants and state transitions](../../modules/25_Financial_Packages_Contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 25: exact mappings, keys, indexes and migration proposals](../../modules/25_Financial_Packages_Contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `ValidatePackageArtifactsCommand(PackageDefinitionRef, RenderAttemptId, Meta)` | `ValidationReportDto` | Server-read exact bytes, MIME/format, round-trip values, digests and required-file completeness. |
| `SealFinancialPackageCommand(PackageDefinitionRef, RenderAttemptId, ExpectedManifestDigest, Meta)` | `MutationReceiptDto` | Recompute all dependencies and byte manifest; all required formats valid; one atomic immutable seal. |
| `GetPackageManifestQuery(PackageRef)` | `PackageManifestDto` | Content and artifact manifests, lineage and historical/current state. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 25: exact input/output DTO fields and supporting request rules](../../modules/25_Financial_Packages_Contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/01_Command_Query_Ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Validated artifact manifest and immutable package seal.
- Integrity and race-condition evidence.

**Direct consumers unlocked by this task:**

- [T037 — Implement exact-package decisions, downloads and release readiness](037_Implement_exact_package_decisions_downloads_and_release_readiness.md)

- [Module 25: exact producer/consumer boundaries](../../modules/25_Financial_Packages_Contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 25: required component tree, state and forms](../../modules/25_Financial_Packages_Contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Missing required DOCX/PDF cannot pass after XLSX succeeds.
- Source changed during render blocks publication.
- Tampered bytes are rejected rather than updating the stored hash to match them.

**Source-named test families (proposed unless current source confirms them):**

- Module 25: `PackageManifestTests`, `FinancialArtifactRoundTripTests`, `PackagePublicationIntegrityTests`, `PackageAssemblyComponentTests`, `R2R25PackageJourneys`. [Exact source cases](../../modules/25_Financial_Packages_Contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-042-AC02 | Export generation failure or unsupported content blocks that output and reports the reason; no renamed CSV, empty PDF or fake success is accepted. |

**Related original stories:** `VP-042`. [Full preserved wording and production interpretation](../../reference/08_Original_52_Acceptance_Criteria.md).

**Related integration journeys:** [R2R-AT-18](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-18), [R2R-AT-19](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-19), [R2R-AT-20](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-20).

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
