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
| T024 | Expose controlled unallocation from approved allocation history | IN_PROGRESS | [ClientOpenItemAllocationHistoryQuery.cs](../../src/AuditSphereOps.Application/Accounting/ClientOpenItemAllocationHistoryQuery.cs) |
| T025 | Resolve component replacement retention before exposing correction | IN_PROGRESS | Pending implementation |
| T026 | Surface draft-scope ownership relationships | IMPLEMENTED_VERIFICATION_PENDING | [ownership.ts](../../src/AuditSphereOps.Ui/src/app/features/consolidation/ownership.ts) |
| T027 | Add intercompany matching and independent approval | IN_PROGRESS | Pending implementation |
| T028 | Complete external component-pack intake, review and pinning | NOT_STARTED | Pending implementation |
| T029 | Preserve portal request drafts during a same-context refresh | IMPLEMENTED_VERIFICATION_PENDING | [request.ts](../../src/AuditSphereOps.Ui/src/app/features/portal/request.ts) |
| T030 | Let slow package-build status reads finish | IMPLEMENTED_VERIFICATION_PENDING | [financial-package-build.ts](../../src/AuditSphereOps.Ui/src/app/features/engagements/financial-package-build.ts) |
| T031 | Preserve consolidation pagination and bounded subquery errors | NOT_STARTED | Pending implementation |
| T032 | Provide bounded portal request and package history continuation | IMPLEMENTED_VERIFICATION_PENDING | [ClientPortalWorkspaceQuery.cs](../../src/AuditSphereOps.Application/Documents/ClientPortalWorkspaceQuery.cs) |
| T033 | Show statutory milestone preview before save | IMPLEMENTED_VERIFICATION_PENDING | [plan.ts](../../src/AuditSphereOps.Ui/src/app/features/audit/plan.ts) |
| T034 | Show the statement body of a selected pinned presentation | IMPLEMENTED_VERIFICATION_PENDING | [presentation.ts](../../src/AuditSphereOps.Ui/src/app/features/accounting/presentation.ts) |
| T035 | Document native client counterparty, account-role, operational-journal, allocation and settlement contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.ClientOpenItemAllocations.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.ClientOpenItemAllocations.cs) |
| T036 | Document native client sales, purchase and credit-note contracts | IMPLEMENTED_VERIFICATION_PENDING | [UiEndpoints.ClientPurchaseInvoices.cs](../../src/AuditSphereOps.Api/Ui/UiEndpoints.ClientPurchaseInvoices.cs) |
| T037 | Document client accounting setup, chart, currency, mapping and remeasurement contracts | IN_PROGRESS | Pending implementation |
| T038 | Document trial-balance/GL intake and source-evidence contracts | IN_PROGRESS | Pending implementation |
| T039 | Document adjustment-journal and adjustment-plan contracts | IN_PROGRESS | Pending implementation |
| T040 | Document financial package and statement report contracts | NOT_STARTED | Pending implementation |
| T041 | Document period-maintenance, reconciliation, evidence and valuation contracts | NOT_STARTED | Pending implementation |
| T042 | Document group consolidation contracts | NOT_STARTED | Pending implementation |
| T043 | Document firm ledger, billing and end-of-service contracts | NOT_STARTED | Pending implementation |
| T044 | Document commercial lead, quotation, fee-agreement and conversion contracts | NOT_STARTED | Pending implementation |
| T045 | Document client, acceptance, engagement, staffing and budget contracts | NOT_STARTED | Pending implementation |
| T046 | Document practice resource, time, workflow and portfolio contracts | NOT_STARTED | Pending implementation |
| T047 | Document audit planning, procedure, fieldwork and confirmation contracts | NOT_STARTED | Pending implementation |
| T048 | Document audit completion, records and deliverable contracts | NOT_STARTED | Pending implementation |
| T049 | Document client portal, PBC and portal journal-management contracts | NOT_STARTED | Pending implementation |
| T050 | Document administration, setup, Microsoft services and durable-operation contracts | NOT_STARTED | Pending implementation |
| T051 | Document session, search and browser authentication protocol contracts | NOT_STARTED | Pending implementation |

## Retention and acceptance decisions

Component corrections use a new scope version, retaining original scope, component and package evidence. The implementation handoff must guide users through the existing authorized scope and component submission workflows; no same-draft destructive replacement adapter is authorized by this tracker. Professional audit decisions and live-provider acceptance remain governed by their existing controls.
