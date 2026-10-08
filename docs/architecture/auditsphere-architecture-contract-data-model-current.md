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
| 2 Governance and planning | `AssessmentReceipts`, `Audit` (planning, materiality, risk bands), `ResourcePlanning`, `ResourcePlanningReceipts`, `StaffingChanges`, `BudgetPreparations`, `BudgetApprovals` | `Acceptance/*.cs`, `Audit/MaterialityAndRiskBands.cs`, `Audit/StatutoryMilestones.cs`, `Practice/ResourcePlanning.cs`, `StaffingChange.cs`, `BudgetPreparation.cs`, `BudgetApproval.cs` |
| 3 Fieldwork | `Fieldwork`, `FieldworkConnections`, `Reviews`, `Pbc`, `ClientPortal`, `Documents` | `Audit/Fieldwork.cs`, `FieldworkConnections.cs`, `AuditSamplingEngine.cs`, `AuditConfirmationClosure.cs`, `Reviews/Reviews.cs`, `Documents/*.cs` |
| 3 (TB and accounting inputs) | `Accounting`, `ClientAccounting`, `FinancialStatements`, `AdjustmentBridge`, `AdjustmentJournalActions`, `AdjustmentPlanActions`, `AccountingAnalysisPreparations`, `AccountingCreationPreparations`, `AccountingEvidenceActions`, `ClientAccountRoles`, `ValuationPreparations` | `Accounting/*.cs` |
| 4 Reporting and archive | `Completion`, `AuditDeliverables`, `FileFreeze`, `ScopedEvidence` | `Completion/*.cs`, `Records/*.cs` |
| 5 Practice management | `FirmOperations`, `Practice` (time, billing, ledger) | `Practice/Time.cs`, `Billing.cs`, `FirmLedger.cs`, `FirmOperations.cs` |
| Owner addition: client bookkeeping | `ClientAccountingSchedules`, `ClientCounterparties`, `ClientManualSettlements`, `ClientOpenItemAllocations`, `ClientOperationalLedger`, `ClientSalesInvoices`, `ClientSalesInvoiceWorkflow`, `ClientSalesCreditNotes`, `ClientPurchaseInvoices`, `ClientPurchaseCreditNotes` | `Accounting/Client*.cs` |
| Owner addition: consolidation | `Consolidation` | `Accounting/` consolidation and FX records |
| Platform | `Security`, `Operations`, `Microsoft365`, `ClientSharePointSites` | `Security/Users.cs`, `Microsoft365/*.cs`, `Shared/Kernel.cs` |

Use the [code map](auditsphere-architecture-code-map.md) to go from a capability to its Application services.

## 3. Changing the schema

1. Edit the Domain class and the matching `Configure<Module>` partial only.
2. Add constraints and any trigger SQL in the same migration. Keep trigger SQL in a `*Sql.cs` helper when it is longer than a few statements.
3. Create the migration from the repository root (name it for the business change, PascalCase, as existing migrations do):
   ```bash
   dotnet ef migrations add <BusinessChangeName> --project src/AuditSphereOps.Infrastructure \
     --startup-project src/AuditSphereOps.Api
   ```
4. Confirm there is no remaining drift with the `has-pending-model-changes` command in `AGENTS.md` §3.
5. Never edit or delete an applied migration. Never apply migrations to Development or production from an agent session; deployment uses the approved procedure in the operator guide.
6. If the change alters a request or response shape, regenerate `contracts/auditsphere-openapi.json` (section 4) and update the Angular decoder.

## 4. API contract regeneration

`scripts/contracts/verify-openapi.sh` runs `tools/AuditSphereOps.OpenApiEmitter` against the real API host and diffs the result with `contracts/auditsphere-openapi.json`. To regenerate after an intentional change, run the emitter directly after a Release build and commit its output:

```bash
dotnet run --project tools/AuditSphereOps.OpenApiEmitter --no-build --configuration Release \
  -- contracts/auditsphere-openapi.json
```
