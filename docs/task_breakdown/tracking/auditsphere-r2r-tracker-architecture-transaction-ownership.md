# T002 architecture and transaction ownership working ledger

**State:** IN_PROGRESS. This is inspected implementation evidence, not an approved T002 handoff. [T001's accepted inventory](auditsphere-r2r-tracker-current-baseline-inventory.md) is the source baseline; `docs/architecture/auditsphere-architecture-current-architecture.md` and `docs/architecture/auditsphere-architecture-code-map.md` govern implementation. The preserved [reference ADRs](../reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md#section-2-4) supply requirements to reconcile, not an instruction to replace the current stack.

| Decision | Current implementation position | Remaining decision or proof |
|---|---|---|
| ADR-01: module ownership | Reuse the five-project modular monolith and existing accounting/consolidation services; the seven-module R2R blueprint is an approved scope baseline. | Map each proposed symbol to an existing owner or a task-owned addition. |
| ADR-02: application and component tests | Static Application services and PostgreSQL-backed xUnit plus Playwright are the current architecture. `MediatR` and `bUnit` are not installed, so no package version or MediatR licence is being approved by this task. | Preserve one transaction owner per writing operation; adding either package would need its own reviewed architecture and licence decision. |
| ADR-05: dependency currentness | Existing firm/client generation fences and exact source identities remain the starting point. | Define the proposed dependency manifest and stale propagation under its owning tasks. |
| ADR-06: source/artifact storage | Reuse existing source and artifact stores, retaining exact bytes and SHA-256 identities. | Verify each new handoff's retention and access boundary. |
| ADR-07: package presentation | Financial calculation belongs to Module 24; assembly/rendering belongs to Module 25. | Pin their shared DTO and manifest identity before new implementation. |
| ADR-08: release/records | Purview and eSignature providers are excluded by owner instruction; exact uploaded evidence and human release/records gates stay in scope. | No provider acceptance can be inferred. |
| ADR-10: roles and independence | Group creation now derives the creator's group role from an active firm-wide Manager, Partner, or Administrator grant; it no longer grants Partner to every creator. | Audit other group-role paths and person-based professional decisions under their owning tasks. |

## Transaction owners inspected so far

| Writing operation | Current owner | Transaction boundary | Follow-up |
|---|---|---|---|
| `ConsolidationService.CreateGroupAsync` | `ConsolidationService.Groups` | One EF `SaveChangesAsync` persists group and creator grant atomically; no facade transaction. | Decide whether later firm-role revocation must also revoke an independently stored group grant, and fence concurrent changes accordingly. |
| `ConsolidationService.AddMembershipAsync` | `ConsolidationService.Groups` | Service opens one database transaction, locks group row, writes membership and revision, commits. | Verify every early return rolls back through disposal. |
| `FinancialStatementService` package operations | `FinancialStatementService.Package` | Existing code conditionally opens a transaction only if `CurrentTransaction` is null. | Trace callers before declaring the exact package operation ownership settled; no nested wrapper should be added. |
| General-ledger writes | `ClientAccountingService.GeneralLedger` | An inspected mutation path opens an explicit service transaction. | Map the preserved request names to concrete methods; inspect durable worker stages separately. |

The [111 preserved command/query names](../coverage/auditsphere-r2r-tracker-command-query-ownership.md) are task ownership, not evidence that 111 handler classes or transaction boundaries exist. Mapping each writing request to an implemented static service method or a named `NEW/DECISION` item remains open. Shared DTO/port ownership, migration serialization and capability-to-role mapping likewise remain incomplete; T002 must not be marked complete on this working ledger alone.

## Shared contract candidates and serialized files

| Candidate from the approved blueprint | Current classification | Proposed owner; disposition still needed |
|---|---|---|
| `ReportingContextRevision` and scoped reference/evidence identities | `NEW/DECISION`; existing client, engagement, period and revision fields are not yet proven equivalent | T007 shared-identity task, coordinated through T002; do not duplicate current client records. |
| `MoneyAmount` | `NEW/DECISION`; current calculators use `decimal` and `MoneyPolicy` | T003 policy task with Domain owner; decide whether a type adds an invariant before introducing it. |
| `DependencyManifest` and `Currentness` | `NEW/DECISION`; existing generations, hashes and manifest fields cover only identified portions | T007 shared contract, with lineage consumers owned by their module task. |
| `ApprovalDecision` | `NEW/DECISION`; existing capability, mapping, package and review decisions are distinct records | T007 cross-module identity proposal; professional decisions stay with their specific module and human reviewer. |
| Public DTOs and module ports | No bulk shared contract introduced by T002 | Owning task proposes the smallest DTO/port; coordinator serializes edits to shared public contracts. |
| DbContext configuration, model snapshot and migrations | One existing EF Core context; no T002 model change | Coordinator serializes changes and checks for model drift; individual feature task supplies schema/backfill evidence. |

The transaction-owner rule for future writing operations is the named Application command/service or durable worker stage that performs the write. A caller may invoke it but may not add a second transaction around a service-owned transaction. Read-only queries do not own a write transaction. These are implementation rules for the remaining request mapping, not proof that all preserved requests already have implementations.

## Current capability-to-role mapping inspected

| Boundary | Active roles required in the current Application authorization code | Additional condition |
|---|---|---|
| Client accounting setup, analysis, statements and consolidation preparation | `AccountingPreparer`, `AccountingReviewer`, `Manager`, `Partner`, or `Administrator` | Client operations require a covering client/engagement `RoleGrant`; firm configuration requires a firm-wide grant; group operations require an active `GroupAccessGrant`. |
| Review actions in those capabilities | `AccountingReviewer`, `Manager`, `Partner`, or `Administrator` | Matching scope and action-specific independence/currentness gates still apply. A role alone is not approval evidence. |
| Legacy adjustment-journal preparation | `AccountingPreparer`, `Staff`, `Partner`, or `Manager` | Explicit dataset client/engagement scope. This differs from the broader current-service preparer array and needs per-operation reconciliation before a unified role policy is claimed. |
| Legacy adjustment-journal review | `AccountingReviewer`, `Partner`, or `Manager` | Explicit journal client/engagement scope and review restrictions. |
| Group creation | Firm-wide `Manager`, `Partner`, or `Administrator` | Creator receives the matching active role as a group grant. Existing group grants are independently stored; the effect of later firm-role revocation on those grants remains a policy decision. |

These are observations from `AuthorizationDecision`, `ClientAccountingService.Authorization`, `AccountingAnalysisService.Authorization`, `FinancialStatementService.Authorization`, `ConsolidationService.Authorization`, and `AdjustmentJournalService`. They do not replace each command's exact authorization predicate. The T002 handoff still needs an approved disposition for the role-array variation and explicit person-based professional acceptance mapping.
