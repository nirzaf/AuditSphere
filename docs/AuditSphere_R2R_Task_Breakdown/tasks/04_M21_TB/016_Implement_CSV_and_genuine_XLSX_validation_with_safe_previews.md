---
id: "T016"
work_package: "R2R-04"
modules: [21]
status: "NOT_STARTED"
depends_on: ["T015"]
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
# T016 — Implement CSV and genuine XLSX validation with safe previews

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Original work-package order](../../reference/05_Execution_Coordination_and_Handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Validate staged TB sources using the blueprint’s explicit numeric, format and scope rules.

**Original work package:** `R2R-04` — M21 bounded TB CSV/XLSX receipt, profile/column mapping, staging, validation and acceptance
**Original package exit:** Real formats, parser attacks/limits, rejected replacement and sealed-membership tests.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T015 — Build bounded import receipts, chunks and column profiles](015_Build_bounded_import_receipts_chunks_and_column_profiles.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/03_Shared_Domain_Persistence_CQRS_Blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/04_Lifecycle_Lineage_and_Module_Ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/06_Contract_Clarifications_and_Resource_Limits.md)
- [21 Trial Balance and GL Contract](../../modules/21_Trial_Balance_and_GL_Contract.md)

## Sequential work

1. Reuse approved CSV/OpenXML parsers; verify genuine workbook structure rather than filename extension.
2. Stream/parse with bounded expansion, entry/shared-string limits and safe row-level diagnostics.
3. Normalize signed-net/debit-credit layouts; retain raw/normalized hashes, leading-zero codes and supported date conventions.
4. Display preview/errors and operation state without committing rejected sources or executing formulas/macros/external links.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 21: exact models, invariants and state transitions](../../modules/21_Trial_Balance_and_GL_Contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 21: exact mappings, keys, indexes and migration proposals](../../modules/21_Trial_Balance_and_GL_Contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `ValidateAccountingImportCommand(ImportFinalizeDto, Meta)` | `OperationTicketDto` | Complete staged upload/profile; enqueue streaming parse/validation, no source acceptance yet. |
| `GetImportIssuesQuery(SessionId, PageRequest)` | `PageDto<ImportIssueDto>` | Row keys/error codes safe; no arbitrary source-file path disclosure. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 21: exact input/output DTO fields and supporting request rules](../../modules/21_Trial_Balance_and_GL_Contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/01_Command_Query_Ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Typed validation report, paged issues and deterministic normalization.
- CSV/XLSX validation worker and browser preview.

**Direct consumers unlocked by this task:**

- [T017 — Seal and accept TB revisions with replacement protection](017_Seal_and_accept_TB_revisions_with_replacement_protection.md)

- [Module 21: exact producer/consumer boundaries](../../modules/21_Trial_Balance_and_GL_Contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 21: required component tree, state and forms](../../modules/21_Trial_Balance_and_GL_Contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Reject unbalanced totals, ambiguous duplicates, missing headers, unknown dimensions and excess precision/limits.
- Formula-dependent input, external workbook parts and renamed fake XLSX fail safely.
- Rejected input preserves the previous accepted source.

**Source-named test families (proposed unless current source confirms them):**

- Module 21: `AccountingImportContractTests`, `SourceRevisionIntegrityTests`, `CompletenessAndMappingTests`, `AccountingImportWizardTests`, `MappingEditorTests`, `R2R21SourceJourneys`. [Exact source cases](../../modules/21_Trial_Balance_and_GL_Contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-035-AC02 | Unbalanced totals, duplicate ambiguous accounts, missing headers, unknown dimensions, formula-dependent numeric cells and exceeded limits produce a non-committed error preview. |
| VP-035-AC04 | XLSX means an actual workbook format, not CSV renamed to .xlsx; source bytes remain in-session only and exported/imported formats are verified. |

**Related original stories:** `VP-035`. [Full preserved wording and production interpretation](../../reference/08_Original_52_Acceptance_Criteria.md).

**Related integration journeys:** [R2R-AT-07](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-07), [R2R-AT-08](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-08).

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
