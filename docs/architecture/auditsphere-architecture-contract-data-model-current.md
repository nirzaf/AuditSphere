# AuditSphere — Data Model Contract

**Status: CURRENT.** An index, not a copy. The executable truth is the EF Core model (`src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext*.cs`), the Domain classes (`src/AuditSphereOps.Domain/`) and the migrations (`src/AuditSphereOps.Infrastructure/Persistence/Migrations/`). Read the partial for your capability; do not read the model snapshot.

## 1. Storage conventions (binding)

| Rule | Detail |
| --- | --- |
| Engine | PostgreSQL 18.6 via Npgsql 10 / EF Core 10. One `AuditSphereDbContext`; `OnModelCreating` calls one `Configure<Module>(b)` per partial file. |
| Money | `decimal` in C#, `numeric(19,6)` in PostgreSQL, banker's rounding (`MidpointRounding.ToEven`) and `MoneyPolicy.Normalize` (R2R-ADR-03). Never `double`. API and Angular carry decimals as exact strings. |
| States | String constants in static classes (`PbcStates`, `RiskBands`, `FileFreezeStates`, …) backed by database check constraints. Adding a state means updating the constant class, the check constraint (migration) and every Angular decoder that enumerates it. |
| Append-only history | Enforced in the database, not only in C#: the migrations install many `BEFORE UPDATE/DELETE` triggers. Raw SQL for the larger trigger sets lives beside the migrations in `*Sql.cs` helpers (for example `ClientSalesInvoiceWorkflowSql.cs`). A change that needs to "edit" history must add a revision, amendment or reversal row instead. |
| Concurrency | Optimistic fences: commands carry `ExpectedRevision` / `ExpectedVersion` and refuse stale input; parent/child guards may use PostgreSQL `xmin` inside trigger SQL. This is how "row-level concurrency" (specification §4.3.1) is met. |
| Idempotency | Commands that can be retried by a browser after an unknown outcome persist a receipt keyed by a client-supplied request ID (`*Receipts` partials). Reconcile the receipt first; never blind-retry. |
| Isolation | Every row that belongs to a client carries `FirmId` and the client (and engagement where relevant); queries filter by the actor's `RoleGrant` scope in Application code. |
| Bytes | Uploaded and generated files are stored with SHA-256 identities; provider writes go through durable operations with exact-version read-back receipts. |

## 2. Model partials by specification module

| Specification module | `AuditSphereDbContext.<Module>.cs` partials | Domain files |
| --- | --- | --- |
| 1 Commercial and CRM | `Practice`, `Commercial`, `ClientContactCreations`, `ClientConversions`, `EngagementCreations` | `Practice/Crm.cs`, `Commercial.cs`, `ClientConversion.cs`, `ClientContactCreation.cs`, `Engagements/*.cs` |
| 2 Governance and planning | `AssessmentReceipts`, `Audit` (planning, materiality, risk bands, procedure definition and applicability history), `AuditPlanningReceipts`, `ResourcePlanning`, `ResourcePlanningReceipts`, `StaffingChanges`, `BudgetPreparations`, `BudgetApprovals` | `Acceptance/*.cs`, `Audit/MaterialityAndRiskBands.cs`, `Audit/StatutoryMilestones.cs`, `Practice/ResourcePlanning.cs`, `StaffingChange.cs`, `BudgetPreparation.cs`, `BudgetApproval.cs` |
| 3 Fieldwork | `Fieldwork`, `FieldworkConnections`, `ConfirmationBookedSources`, `FindingCommands`, `ReviewPointCommands`, `WorkpaperCommands`, `Reviews`, `Pbc`, `ClientPortal`, `Documents` | `Audit/Fieldwork.cs`, `FieldworkConnections.cs`, `AuditSamplingEngine.cs`, `AuditConfirmationClosure.cs`, `AuditConfirmationBookedSource.cs`, `Audit/*CommandReceipt.cs`, `Reviews/Reviews.cs`, `Reviews/ReviewPointCommandReceipt.cs`, `Documents/*.cs` |
| 3 (TB and accounting inputs) | `Accounting`, `NativeTrialBalanceSources`, `NativeGeneralLedgerSources`, `ClientAccounting`, `ClientAccountingProfileRevisions`, `ClientAccountingDimensionSchema`, `PeriodMaintenanceReceipts`, `FinancialStatements`, `AdjustmentBridge`, `AdjustmentJournalActions`, `AdjustmentPlanActions`, `AccountingAnalysisPreparations`, `AccountingCreationPreparations`, `AccountingEvidenceActions`, `ClientAccountRoles`, `ValuationPreparations` | `Accounting/*.cs` |
| 4 Reporting and archive | `Completion`, `AuditDeliverables`, `FileFreeze`, `ScopedEvidence` | `Completion/*.cs`, `Records/*.cs` |
| 5 Practice management | `FirmOperations`, `Practice` (time, billing, ledger), `TimeEntryChanges`, `BillingCommands`, `WorkflowTasks`, `WorkflowTemplates`, `WorkflowEvidence`, `WorkflowApplicability`, `WorkflowDependencyRevisions`, `WorkflowReminders` | `Practice/Time.cs`, `TimeEntryChangeReceipt.cs`, `Billing.cs`, `BillingCommandReceipt.cs`, `FirmLedger.cs`, `FirmOperations.cs`, `Workflow*.cs` |
| Owner addition: client bookkeeping | `ClientAccountingSchedules`, `ClientCounterparties`, `ClientManualSettlements`, `ClientOpenItemAllocations`, `ClientOperationalLedger`, `ClientOpeningSuccessors`, `ClientSalesInvoices`, `ClientSalesInvoiceWorkflow`, `ClientSalesCreditNotes`, `ClientPurchaseInvoices`, `ClientPurchaseCreditNotes` | `Accounting/Client*.cs` |
| Owner addition: consolidation | `Consolidation` | `Accounting/` consolidation and FX records |
| Platform | `Security`, `OversightAccess`, `Operations`, `OperatorCommands`, `Microsoft365`, `ClientSharePointSites` | `Security/Users.cs`, `Security/AdministratorOversightAccessEvent.cs`, `Completion/OperatorCommandReceipt.cs`, `Microsoft365/*.cs`, `Shared/Kernel.cs` |

Procedure execution adds `audit_procedure_command_receipts` (append-only actor/request receipts with definition, generation, result/review and evidence-digest identity) and the nullable `evidence_digest` column. The submitted evidence snapshot is stored inside the immutable `audit_procedure_results.structured_result_json` under the server-reserved versioned property `_auditEvidenceSnapshot`; direct result/review writes are not accepted. See [AuditProcedureCommandReceipt](../../src/AuditSphereOps.Domain/Audit/AuditProcedureCommandReceipt.cs) and `AuditProgramService.Evidence.cs`.

SharePoint-primary PBC storage adds `pbc_document_registrations` (append-only link from a received upload and its completed transfer to an exact-version `document_snapshots` row and its `document_references` row, guarded at commit), `pbc_storage_dispositions` (per-upload read source and local-copy state, moving only LOCAL_STAGED → SELECTED_SITE → back, or SELECTED_SITE → RELEASED) and append-only `pbc_storage_events`; `pbc_storage_command_receipts` retain each administrator command's request hash, review basis, preview and outcome, bound at commit to its exact storage event or registration. `pbc_upload_chunks.staged_path` history is never rewritten when a staged copy is released.

Use the [code map](auditsphere-architecture-code-map.md) to go from a capability to its Application services.

Conditional task applicability uses `workflow_task_applicability_proposals` and `workflow_task_applicability_decisions`, mapped by `AuditSphereDbContext.WorkflowApplicability.cs` from `Domain/Practice/WorkflowTaskApplicability.cs`. Scoped foreign keys bind the exact task binding, template step, independently accepted PBC receipt and actors. Sequential proposal/decision lineage, basis hashes, exact revision/generation pins and independent actors are retained; command request keys recover the actor's original outcome. The `WorkflowTaskApplicabilityEvidence` migration protects these records against UPDATE/DELETE/TRUNCATE and refuses downgrade when evidence exists. It changes neither operational task status nor professional procedure decisions and fabricates no legacy approvals.

`administrator_oversight_access_events`, mapped by `AuditSphereDbContext.OversightAccess.cs`, retains the exact firm/actor/session, registered operation, resource, request identity and `DISCLOSURE_AUTHORIZED` outcome. A scoped actor foreign key and unique firm/request identity accompany the `AdministratorOversightAccessEvidence` migration's append-only protections. No automatic expiry or disposal is implemented; retention and independent-history-reviewer policy must be accepted separately before the dependent rollout.

`technical_library_versions` binds `(firm_id, document_id)` to its document's `(firm_id, id)` through `fk_technical_library_version_document_scope`, configured in `AuditSphereDbContext.FirmOperations.cs`. The additive `TechnicalLibraryVersionFirmScope` migration validates existing references and refuses inconsistent history. It does not repair or delete versions. A failed upgrade must leave retained bytes, the prior migration and its original parent constraint intact; resolving invalid existing references requires an authorized data-review process.

## 3. Changing the schema

1. Edit the Domain class and the matching `Configure<Module>` partial only.
2. Add constraints and any trigger SQL in the same migration. Keep trigger SQL in a `*Sql.cs` helper when it is longer than a few statements.
3. Create the migration from the repository root (name it for the business change, PascalCase, as existing migrations do):
   ```bash
   dotnet ef migrations add <BusinessChangeName> --project src/AuditSphereOps.Infrastructure \
     --startup-project src/AuditSphereOps.Api
   ```
4. Confirm there is no pending model drift with the pinned EF tool and the matching built Infrastructure/API configuration; the CI workflow retains the full build and drift gate.
5. Never edit or delete an applied migration. Focused migration checks use an owned test database; Development or live migrations require the separately authorized operator deployment/recovery procedure. A model-drift check is not migration authority.
6. If the change alters a request or response shape, regenerate `contracts/auditsphere-openapi.json` (section 4) and update the Angular decoder.

## 4. API contract regeneration

`scripts/contracts/verify-openapi.sh` runs `tools/AuditSphereOps.OpenApiEmitter` against the real API host and diffs the result with `contracts/auditsphere-openapi.json`. To regenerate after an intentional change, run the emitter directly after a Release build and commit its output:

```bash
dotnet run --project tools/AuditSphereOps.OpenApiEmitter --no-build --configuration Release \
  -- contracts/auditsphere-openapi.json
```
