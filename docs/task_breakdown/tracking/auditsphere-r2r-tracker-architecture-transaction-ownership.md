# T002 architecture and transaction ownership review ledger

**State:** IN_REVIEW. The repository owner approved the architectural direction at exact commit `779480c613b49f0e6ee1a818864ef48b5b8bd607` in the current Codex task on 2026-09-26. The explicit open items below still prevent a completed T002 handoff. [T001's accepted inventory](auditsphere-r2r-tracker-current-baseline-inventory.md) is the source baseline; `docs/architecture/auditsphere-architecture-current-architecture.md` and `docs/architecture/auditsphere-architecture-code-map.md` govern implementation. The preserved [reference ADRs](../reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md#section-2-4) supply requirements to reconcile, not an instruction to replace the current stack.

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
| `ConsolidationService.CreateGroupAsync` | `ConsolidationService.Groups` | One service transaction locks the firm safety row shared with role revocation, rechecks firm-wide authority, then persists group and creator grant atomically. No facade transaction. | Decide whether later firm-role revocation must also revoke an independently stored group grant; that policy is separate from preventing creation against a concurrent revocation. |
| `ConsolidationService.AddMembershipAsync` | `ConsolidationService.Groups` | Service opens one database transaction, locks group row, writes membership and revision, commits. Early returns before commit dispose the transaction. | A later membership task still must prove concurrent revocation and perimeter behavior. |
| `FinancialStatementService.BuildFinancialPackageAsync` called directly | `FinancialStatementService.Package` | Service opens and commits its own transaction when `CurrentTransaction` is null; it rechecks mapping version, generation and finalized-plan hash after firm/client locks. | Direct path needs its task-level authorization and concurrency proof. |
| `FinancialPackageBuildHandler.PublishAsync` called by `PostgresOperationStore.CompleteAsync` | `PostgresOperationStore.CompleteAsync` | Store opens the outer publication transaction before invoking the handler; `BuildFinancialPackageAsync` sees `CurrentTransaction`, does not open or commit a nested one, and its package write plus operation completion commit together. | Preserve this conditional contract; a new facade must not wrap either boundary. |
| `FinancialStatementService.EnqueueFinancialPackageBuildAsync` and `EnqueueFinancialPackageRenderAsync` | `FinancialStatementService.Package` | Each method owns a short enqueue transaction and commits only when `IOperationStore.EnqueueAsync` succeeds; worker publication occurs later under `PostgresOperationStore.CompleteAsync`. | Durable task must prove retry, cancellation and stale-source dispositions. |
| General-ledger writes | `ClientAccountingService.GeneralLedger` | An inspected mutation path opens an explicit service transaction. | Map the preserved request names to concrete methods; inspect durable worker stages separately. |

The [111 preserved command/query names](../coverage/auditsphere-r2r-tracker-command-query-ownership.md) are task ownership, not evidence that 111 handler classes or transaction boundaries exist. Mapping each writing request to an implemented static service method or a named `NEW/DECISION` item remains open. Shared DTO/port ownership, migration serialization and capability-to-role mapping likewise remain incomplete; T002 must not be marked complete on this working ledger alone.

## Request-to-method mappings inspected

| Preserved request | Present method and write owner | Disposition for owning task |
|---|---|---|
| `CreateConsolidationGroupCommand` (T041) | `ConsolidationService.CreateGroupAsync` is the current group-create method and owns its firm-guard transaction. | `EXTEND`: T041 must prove the full proposed perimeter and group authorization contract; T002 proves only current group creation and revocation serialization. |
| `RequestPackageRenderingCommand` (T035) | `FinancialStatementService.EnqueueFinancialPackageRenderAsync` owns the enqueue transaction; `PostgresOperationStore.CompleteAsync` owns later durable publication. | `EXTEND/DECISION`: T035 must reconcile its proposed Module 25 rendering contract with the existing package render operation; the method name alone is not full artifact-set equivalence. |
| `CreatePackageDefinitionCommand` (T034) | `BuildFinancialPackageAsync` builds a financial result from mapping and adjustment inputs; no current method has been proven to author the proposed independent Module 25 package-definition revision. | `NEW/DECISION`: do not mislabel a calculated Module 24 package as the Module 25 composition definition. |

These mappings identify a candidate method and the current write boundary, then explicitly leave broader contract equivalence with the owning task. They do not turn every preserved request into a current implementation.

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
| Group perimeter approval | Active group `AccountingReviewer`, `Manager`, `Partner`, or `Administrator` | `ApproveScopeAsync` refuses `CreatedByUserId == actor.UserId` before accepting the exact draft scope version; capability and current group-revision gates remain required. |

These are observations from `AuthorizationDecision`, `ClientAccountingService.Authorization`, `AccountingAnalysisService.Authorization`, `FinancialStatementService.Authorization`, `ConsolidationService.Authorization`, and `AdjustmentJournalService`. They do not replace each command's exact authorization predicate.

**Proposed role-variation disposition for review:** Preserve the narrower `AdjustmentJournalService` preparer/reviewer arrays at their existing endpoints. Do not infer that its `Staff` preparation role applies to the broader accounting services, or that `Administrator` automatically gains legacy journal-review authority. Each owning task must reconcile its exact command with the current `AuthorizationDecision` scope check and record any deliberate role expansion as a separate policy change. A successful role check is permission to attempt an action, not a professional acceptance decision.

**Proposed person-bound decision rule for review:** Existing consolidation scope, journal, component, intercompany, run and schedule review methods record `actor.UserId` in their approval/review fields. The perimeter path now rejects its creator before approval. The owning task must verify the current actor identity and independent reviewer constraint for each other action, persist the person and exact reviewed revision, and reject self-review where required. A generic role grant, historical owner approval, or generated calculation cannot be substituted for that person's decision. The exact predicates and missing independent-review cases remain task-level proof obligations; this paragraph is not a claim that all review paths are compliant.

## Contract ownership rule for the remaining R2R tasks

The [request registry](../coverage/auditsphere-r2r-tracker-command-query-ownership.md) names exactly one task for every preserved request. The table below assigns its existing Application capability boundary; an owning task must record the concrete method and transaction boundary before claiming that request implemented. A `NEW/DECISION` request does not acquire a transaction merely by appearing in the registry.

| Task range | Existing capability boundary to extend or prove equivalent | Write owner rule |
|---|---|---|
| T011–T014, Module 20 setup | `ClientAccountingService` partials for profiles, periods, books, charts and taxonomy | Named setup method owns its write; T014 reporting-context additions require an explicit identity decision first. |
| T015–T017, Module 21 TB | `TrialBalanceImportService`, `TrialBalanceValidationHandler`, `AccountingSourceAcceptanceService` | Intake/validation stage owns its durable step; source acceptance owns publication. No page-level or facade transaction. |
| T018–T020, Module 21 GL and mapping | `ClientAccountingService.GeneralLedger`, `GeneralLedgerCompletenessHandler`, `FinancialStatementService.Mapping` | GL intake/completeness and mapping publication each own their distinct persisted boundary. |
| T021–T023, Module 22 adjustments | `AdjustmentJournalService`, `AdjustmentPlanService` | Journal action or sealed-plan builder owns its own write; cross-service calls must not nest transactions. |
| T024–T026, Module 23 reconciliations | `AccountingAnalysisService.Reconciliation` | Named reconciliation action owns its write and exact source revision fence. |
| T027–T033, Module 24 statements | `StatementLayoutService`, `FinancialStatementService`, `ClientAccountingService.Restatements` | Layout publication, statement calculation/review and restatement each have one named write owner; Module 24 alone owns financial calculation. |
| T034–T037, Module 25 packages | `FinancialPackageBuildHandler`, `FinancialPackageRenderHandler`, `PackageSealService`, `FinancialPackageReviewService` | Durable build/render stage or seal/review command owns the short publication transaction; renderers are not a second financial engine. |
| T038–T040, entity close and release | `ClientAccountingService.Periods`, `.Restatements`, release/records Application services | Close/amendment command owns local state; external release and records effects retain separate fenced operations. |
| T041–T047, Module 26 group reporting | `ConsolidationService`, `CurrencyTranslationService`, shared package assembly boundary | Perimeter, FX, journal and run commands own their own group write; group assembly consumes approved component identities without editing client books. |
| T048–T054, acceptance/deployment | No new business aggregate assigned by the test/deployment tasks | Verify earlier owners; deployment migrations remain coordinator-serialized and live provider acceptance requires observed external evidence. |

This is a capability-level ownership assignment for future work, not a 111-row assertion that current methods already implement all preserved contracts. The owning task's evidence must identify its exact method, authorization scope, transaction and source-revision fence. The coordinator controls shared DTOs, DbContext model/snapshot and migration merges.
