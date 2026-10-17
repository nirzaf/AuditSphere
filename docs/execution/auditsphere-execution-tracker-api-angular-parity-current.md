# AuditSphere — API and Angular Parity Implementation Tracker

**Status:** CURRENT

This tracker covers the attached bidirectional API/Angular parity specification. Its task identifiers are independent of the R2R task board and AS-AWP-EXEC tasks. Source identity, immutable-SHA checks and test results belong to [status.json](status.json), under `apiAngularParity`.

`IMPLEMENTED_VERIFICATION_PENDING` means the named code exists; it does not assert functional acceptance. `IN_PROGRESS` means a remaining correction or active implementation is underway. Final acceptance still requires the specified behavior and relevant dynamic evidence.

| Task | Requirement | Implementation | Current code evidence |
| --- | --- | --- | --- |
| T001 | Require the privileged role and firm-wide scope on the same eligible grant | IMPLEMENTED_VERIFICATION_PENDING | [AuthorizationDecision.cs](../../src/AuditSphereOps.Application/Security/AuthorizationDecision.cs) |
| T002 | Correct emitted decimal and nullable error schemas | IMPLEMENTED_VERIFICATION_PENDING | [ApiContract.cs](../../src/AuditSphereOps.Api/Contracts/ApiContract.cs) |
| T003 | Use actual consolidation lifecycle literals in UI action gates | IMPLEMENTED_VERIFICATION_PENDING | [scope-workspace.ts](../../src/AuditSphereOps.Ui/src/app/features/consolidation/scope-workspace.ts) |
| T004 | Preserve consolidation journal precision and ownership units | IMPLEMENTED_VERIFICATION_PENDING | [scope-workspace.ts](../../src/AuditSphereOps.Ui/src/app/features/consolidation/scope-workspace.ts) |
| T005 | Reject invalid nonblank audit exception amounts without losing supported precision | IMPLEMENTED_VERIFICATION_PENDING | [fieldwork.ts](../../src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts) |
| T006 | Bind allocation assent to the exact displayed and submitted manifest | IMPLEMENTED_VERIFICATION_PENDING | [open-item-allocations.ts](../../src/AuditSphereOps.Ui/src/app/features/accounting/open-item-allocations.ts) |
| T007 | Reopen and revise purchase invoice drafts and returned invoices | IMPLEMENTED_VERIFICATION_PENDING | [purchase-invoice-workflow.ts](../../src/AuditSphereOps.Ui/src/app/features/accounting/purchase-invoice-workflow.ts) |
| T008 | Expose accounting group creation with existing firm authority | IMPLEMENTED_VERIFICATION_PENDING | [overview.ts](../../src/AuditSphereOps.Ui/src/app/features/consolidation/overview.ts) |
| T009 | Expose effective-dated accounting group membership | IMPLEMENTED_VERIFICATION_PENDING | [group-membership.ts](../../src/AuditSphereOps.Ui/src/app/features/consolidation/group-membership.ts) |
| T010 | Wire first/new consolidation scope creation | IMPLEMENTED_VERIFICATION_PENDING | [ConsolidationScopeCreationQuery.cs](../../src/AuditSphereOps.Application/Accounting/ConsolidationScopeCreationQuery.cs) |
| T011 | Add contact routing assignment and revocation to the client workspace | IMPLEMENTED_VERIFICATION_PENDING | [client-relationships.ts](../../src/AuditSphereOps.Ui/src/app/features/clients/client-relationships.ts) |
| T012 | Add client relationship creation and revocation | IMPLEMENTED_VERIFICATION_PENDING | [PracticeCrmService.Relationships.cs](../../src/AuditSphereOps.Application/Practice/PracticeCrmService.Relationships.cs) |
| T013 | Expose current Manager workprogramme approval | IMPLEMENTED_VERIFICATION_PENDING | [completion.ts](../../src/AuditSphereOps.Ui/src/app/features/audit/completion.ts) |
| T014 | Wire controlled procedure wording tailoring | IMPLEMENTED_VERIFICATION_PENDING | [fieldwork.ts](../../src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts) |
| T015 | Open an editable WORKING workpaper from its procedure | IMPLEMENTED_VERIFICATION_PENDING | [plan.ts](../../src/AuditSphereOps.Ui/src/app/features/audit/plan.ts) |
| T016 | Add manual engagement workflow-task creation | IMPLEMENTED_VERIFICATION_PENDING | [workflow-task-create.ts](../../src/AuditSphereOps.Ui/src/app/features/engagements/workflow-task-create.ts) |
| T017 | Assign and reassign existing workflow tasks | IMPLEMENTED_VERIFICATION_PENDING | [work-queue.ts](../../src/AuditSphereOps.Ui/src/app/features/practice/work-queue.ts) |
| T018 | Recover uncertain sales credit-note submissions and reviews | IMPLEMENTED_VERIFICATION_PENDING | [sales-invoice-workflow.ts](../../src/AuditSphereOps.Ui/src/app/features/accounting/sales-invoice-workflow.ts) |
| T019 | Retain and recover allocation submission/review outcomes | IMPLEMENTED_VERIFICATION_PENDING | [ClientOpenItemAllocationWorkflow.cs](../../src/AuditSphereOps.Application/Accounting/ClientOpenItemAllocationWorkflow.cs) |
| T020 | Recover purchase-invoice draft, submit and return outcomes | IMPLEMENTED_VERIFICATION_PENDING | [ClientPurchaseInvoiceWorkflow.cs](../../src/AuditSphereOps.Application/Accounting/ClientPurchaseInvoiceWorkflow.cs) |
| T021 | Recover purchase-credit submission and return outcomes | IMPLEMENTED_VERIFICATION_PENDING | [ClientPurchaseCreditNoteWorkflow.cs](../../src/AuditSphereOps.Application/Accounting/ClientPurchaseCreditNoteWorkflow.cs) |
| T022 | Make manual-settlement draft recovery survive edits and reloads | IMPLEMENTED_VERIFICATION_PENDING | [ClientOperationalLedgerWorkspace.Settlements.cs](../../src/AuditSphereOps.Application/Accounting/ClientOperationalLedgerWorkspace.Settlements.cs) |
| T023 | Paginate supplier selection in both purchase workflows | IMPLEMENTED_VERIFICATION_PENDING | [purchase-invoice-workflow.ts](../../src/AuditSphereOps.Ui/src/app/features/accounting/purchase-invoice-workflow.ts) |
| T024 | Expose controlled unallocation from approved allocation history | IMPLEMENTED_VERIFICATION_PENDING | [ClientOpenItemAllocationHistoryQuery.cs](../../src/AuditSphereOps.Application/Accounting/ClientOpenItemAllocationHistoryQuery.cs) |
| T025 | Resolve component replacement retention before exposing correction | IN_PROGRESS | [overview.ts](../../src/AuditSphereOps.Ui/src/app/features/consolidation/overview.ts) · [component-correction.ts](../../src/AuditSphereOps.Ui/src/app/features/consolidation/component-correction.ts) |
| T026 | Surface draft-scope ownership relationships | IMPLEMENTED_VERIFICATION_PENDING | [ownership.ts](../../src/AuditSphereOps.Ui/src/app/features/consolidation/ownership.ts) |
| T027 | Add intercompany matching and independent approval | IN_PROGRESS | [ConsolidationService.Intercompany.cs](../../src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Intercompany.cs) |
| T028 | Complete external component-pack intake, review and pinning | IN_PROGRESS | [ConsolidationExternalComponentPackQuery.cs](../../src/AuditSphereOps.Application/Accounting/ConsolidationExternalComponentPackQuery.cs) |
| T029 | Preserve portal request drafts during a same-context refresh | IMPLEMENTED_VERIFICATION_PENDING | [request.ts](../../src/AuditSphereOps.Ui/src/app/features/portal/request.ts) |
| T030 | Let slow package-build status reads finish | IMPLEMENTED_VERIFICATION_PENDING | [financial-package-build.ts](../../src/AuditSphereOps.Ui/src/app/features/engagements/financial-package-build.ts) |
| T031 | Preserve consolidation pagination and bounded subquery errors | IMPLEMENTED_VERIFICATION_PENDING | [ConsolidationScopeWorkspaceQuery.cs](../../src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationScopeWorkspaceQuery.cs) |
| T032 | Provide bounded portal request and package history continuation | IMPLEMENTED_VERIFICATION_PENDING | [ClientPortalWorkspaceQuery.cs](../../src/AuditSphereOps.Application/Documents/ClientPortalWorkspaceQuery.cs) |
| T033 | Show statutory milestone preview before save | IMPLEMENTED_VERIFICATION_PENDING | [plan.ts](../../src/AuditSphereOps.Ui/src/app/features/audit/plan.ts) |
| T034 | Show the statement body of a selected pinned presentation | IMPLEMENTED_VERIFICATION_PENDING | [presentation.ts](../../src/AuditSphereOps.Ui/src/app/features/accounting/presentation.ts) |
| T035 | Document native client counterparty, account-role, operational-journal, allocation and settlement contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.ClientOpenItemAllocations.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.ClientOpenItemAllocations.cs) |
| T036 | Document native client sales, purchase and credit-note contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.ClientPurchaseInvoices.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.ClientPurchaseInvoices.cs) |
| T037 | Document client accounting setup, chart, currency, mapping and remeasurement contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.Accounting.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.Accounting.cs) |
| T038 | Document trial-balance/GL intake and source-evidence contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.Intake.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.Intake.cs) |
| T039 | Document adjustment-journal and adjustment-plan contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.AdjustmentPlans.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.AdjustmentPlans.cs) |
| T040 | Document financial package and statement report contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.Packages.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.Packages.cs) |
| T041 | Document period-maintenance, reconciliation, evidence and valuation contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.PeriodMaintenance.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.PeriodMaintenance.cs) |
| T042 | Document group consolidation contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.Consolidation.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.Consolidation.cs) |
| T043 | Document firm ledger, billing and end-of-service contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.Finance.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.Finance.cs) |
| T044 | Document commercial lead, quotation, fee-agreement and conversion contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.Commercial.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.Commercial.cs) |
| T045 | Document client, acceptance, engagement, staffing and budget contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.cs) |
| T046 | Document practice resource, time, workflow and portfolio contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.WorkflowTasks.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.WorkflowTasks.cs) |
| T047 | Document audit planning, procedure, fieldwork and confirmation contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.Fieldwork.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs) |
| T048 | Document audit completion, records and deliverable contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.Completion.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.Completion.cs) |
| T049 | Document client portal, PBC and portal journal-management contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.Portal.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.Portal.cs) |
| T050 | Document administration, setup, Microsoft services and durable-operation contracts | IN_PROGRESS | [UiEndpoints.Administration.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.Administration.cs) |
| T051 | Document session, search and browser authentication protocol contracts | IMPLEMENTED_VERIFICATION_PENDING | [ApiHost.AuthenticationEndpoints.cs](../../src/AuditSphereOps.Api/ApiHost.AuthenticationEndpoints.cs) |

## Retention and acceptance decisions

Component corrections use a new scope version, retaining original scope, component and package evidence. The presentation handoff re-reads the authorized source scope and exact component/package identities, then accepts scope/group query identities only when the authorized overview contains that pair. Stale, unknown and wrong-group sources have no correction link. The scope workspace opens this retained-source handoff. Correction creation must retain the source period from the authorized scope options; missing or different periods are refused. No same-draft replacement adapter is authorized. Professional audit decisions and live-provider acceptance remain governed by their existing controls.

## Bounded component and readiness continuation

The scope workspace now pages components and readiness independently, alongside the existing package, journal, run and exception histories. Component correction resolves the exact authorized component independently of the displayed page. Readiness refusal retains its error state and has no successful page metadata. Scope/group revision checks fence the paged reads. The former large-readiness unavailable fallback is replaced by accessible bounded pages. Focused checks and their limits are recorded under `apiAngularParity.localVerification.pagingSlice` in [status.json](status.json); full acceptance remains unverified.
