# AuditSphere — Blazor / Angular Route Parity Matrix

> Route-ownership matrix only. The entries below identify the declared Angular route, client-side guard and intended server authority; they do not prove action, validation, authorization, accessibility, failure, or recovery parity.

| Capability | Blazor route | Angular route | Route guard | Intended backend authority | Route ownership |
|---|---|---|---|---|---|
| Landing & Portfolio | `/` | `/ui/app` | None (Redirect) | TrustedActorResolver / Session | MAPPED |
| Portfolio Workbench | `/app` | `/ui/app` | staffGuard | Portfolio / PracticeClientService | MAPPED |
| Portfolio Overview Alias | `/app/overview` | `/ui/app/overview (-> app)` | None (Redirect) | SPA Redirect | MAPPED |
| Practice Leads CRM | `/app/practice/leads` | `/ui/app/practice/leads` | staffGuard | Practice Lead Command / RoleGrant | MAPPED |
| Lead Opportunities | `-` | `/ui/app/practice/leads/:id` | staffGuard | Practice Lead Opportunity / RoleGrant | MAPPED |
| Commercial Settings | `/app/practice/commercial-settings` | `/ui/app/practice/commercial-settings` | staffGuard, unsavedChangesGuard | Commercial Settings Service | MAPPED |
| Practice Proposals | `/app/practice/proposals/{Id:guid}` | `/ui/app/practice/proposals/:id` | staffGuard, unsavedChangesGuard | Practice Commercial / Proposal Service | MAPPED |
| Proposal Conversion | `-` | `/ui/app/practice/proposals/:id/client-conversion` | staffGuard, unsavedChangesGuard | Practice Client Conversion Service | MAPPED |
| Client Profile | `/app/clients/{ClientId:guid}` | `/ui/app/clients/:id` | staffGuard | Client RoleGrant / PracticeClientService | MAPPED |
| Client Contact Creation | `-` | `/ui/app/clients/:id/contacts/new` | staffGuard, unsavedChangesGuard | Client Contact Command / RoleGrant | MAPPED |
| Blocked Engagement Creation | `-` | `/ui/app/clients/:id/engagements/new` | staffGuard, unsavedChangesGuard | Engagement Creation Service | MAPPED |
| Client Acceptance Checklist | `/app/clients/{ClientId:guid}/assessment` | `/ui/app/clients/:id/assessment` | staffGuard, unsavedChangesGuard | Client Acceptance Service | MAPPED |
| Acceptance Assessment Detail | `/app/assessments/{Id:guid}` | `/ui/app/assessments/:id` | staffGuard | Client Acceptance Service | MAPPED |
| Acceptance Assessment Decision | `/app/assessments/{Id:guid}/decision` | `/ui/app/assessments/:id/decision` | staffGuard | Partner Acceptance Clearance Authority | MAPPED |
| Engagement Workspace | `/app/engagements/{EngagementId:guid}` | `/ui/app/engagements/:id` | staffGuard, engagementNavigationGuard | Engagement Scope RoleGrant | MAPPED |
| Engagement Activation Review | `-` | `/ui/app/engagements/:id/activation` | staffGuard, unsavedChangesGuard | Partner Activation Authority | MAPPED |
| Trial Balance Intake | `/app/engagements/{EngagementId:guid}/tb-intake` | `/ui/app/engagements/:id/tb-intake` | staffGuard, unsavedChangesGuard | Trial Balance Intake Service | MAPPED |
| General Ledger Workspace | `-` | `/ui/app/engagements/:id/general-ledger` | staffGuard | Client Accounting GL Service | MAPPED |
| General Ledger Upload | `-` | `/ui/app/engagements/:id/general-ledger/upload` | staffGuard, unsavedChangesGuard | Client Accounting GL Intake Service | MAPPED |
| Statement Drill-down | `/app/engagements/{EngagementId:guid}/statements` | `/ui/app/engagements/:id/statements` | staffGuard | Financial Statement Drilldown Service | MAPPED |
| Audit Plan & Strategy | `/app/engagements/{EngagementId:guid}/audit-plan` | `/ui/app/engagements/:id/audit-plan` | staffGuard | Audit Planning Service | MAPPED |
| Audit Plan Detail | `/app/audit/plans/{Id:guid}` | `/ui/app/audit/plans/:id` | staffGuard | Audit Planning Service | MAPPED |
| Audit Fieldwork | `/app/engagements/{EngagementId:guid}/audit-fieldwork` | `/ui/app/engagements/:id/audit-fieldwork` | staffGuard | Audit Fieldwork Service | MAPPED |
| Workpaper Editor | `/app/audit/workpapers/{Id:guid}` | `/ui/app/audit/workpapers/:id` | staffGuard, unsavedChangesGuard | Audit Workpaper Service | MAPPED |
| Confirmations Tracking | `-` | `/ui/app/engagements/:id/confirmations` | staffGuard, unsavedChangesGuard | Audit Confirmation Service | MAPPED |
| Audit Finding Detail | `/app/findings/{Id:guid}` | `/ui/app/findings/:id` | staffGuard | Audit Fieldwork Service | MAPPED |
| Audit Population & Sampling | `/app/audit/populations/{Id:guid}` | `/ui/app/audit/populations/:id` | staffGuard | Audit Sampling Engine | MAPPED |
| Audit Program Library | `/app/audit/library` | `/ui/app/audit/library` | staffGuard | Audit Program Library Service | MAPPED |
| Review Point Detail | `/app/reviews/{Id:guid}` | `/ui/app/reviews/:id` | staffGuard | Engagement Review Service | MAPPED |
| PBC Request Management | `/app/engagements/{EngagementId:guid}/pbc` | `/ui/app/engagements/:id/pbc` | staffGuard | PBC Transfer Service / Scoped Documents | MAPPED |
| Engagement Completion | `/app/engagements/{EngagementId:guid}/completion` | `/ui/app/engagements/:id/completion` | staffGuard | Audit Deliverables / Clearance Authority | MAPPED |
| Completion Detail | `/app/completion/{Id:guid}` | `/ui/app/completion/:id` | staffGuard | Audit Deliverables / Clearance Authority | MAPPED |
| Release Candidate | `/app/releases/{CandidateId:guid}` | `/ui/app/releases/:id` | staffGuard | Release Candidate Service / Safe Seal | MAPPED |
| Records Archive | `/app/records/archives/{Id:guid}` | `/ui/app/records/archives/:id` | staffGuard | Records Archive Service | MAPPED |
| Accounting Workspace | `/app/accounting` | `/ui/app/accounting` | staffGuard | Client Accounting Authority | MAPPED |
| Accounting Evidence Queue | `/app/accounting/evidence` | `/ui/app/accounting/evidence` | staffGuard | Accounting Evidence Service | MAPPED |
| Evidence Review Actions | `-` | `/ui/app/accounting/evidence/:kind/:id/actions` | staffGuard, unsavedChangesGuard | Accounting Evidence Review Authority | MAPPED |
| Evidence Detail | `-` | `/ui/app/accounting/evidence/:kind/:id` | staffGuard | Accounting Evidence Service | MAPPED |
| Reconciliation Review | `-` | `/ui/app/accounting/reconciliations/:id` | staffGuard | Accounting Reconciliation Service | MAPPED |
| Valuation Preparation | `-` | `/ui/app/accounting/reconciliations/:id/prepare/:kind` | staffGuard, unsavedChangesGuard | Valuation Preparation Engine | MAPPED |
| Source Acceptance | `-` | `/ui/app/accounting/sources/:id/acceptance` | staffGuard, unsavedChangesGuard | Source Intake Authority | MAPPED |
| GL Completeness Verification | `-` | `/ui/app/accounting/gl-sources/:id/completeness` | staffGuard, unsavedChangesGuard | GL Completeness Engine | MAPPED |
| GL Source Acceptance | `-` | `/ui/app/accounting/gl-sources/:id/acceptance` | staffGuard, unsavedChangesGuard | GL Intake Authority | MAPPED |
| Journal Preparation | `-` | `/ui/app/accounting/sources/:id/journal-draft` | staffGuard, unsavedChangesGuard | Client Accounting Journal Service | MAPPED |
| Adjustment Plan Create | `-` | `/ui/app/accounting/sources/:id/adjustment-plan` | staffGuard, unsavedChangesGuard | Adjustment Plan Service | MAPPED |
| Adjustment Plan Finalize | `-` | `/ui/app/accounting/adjustment-plans/:id/finalize` | staffGuard, unsavedChangesGuard | Adjustment Plan Finalization Authority | MAPPED |
| Adjustment Plan Review | `-` | `/ui/app/accounting/adjustment-plans/:id` | staffGuard | Adjustment Plan Service | MAPPED |
| Adjustment Plan Queue | `-` | `/ui/app/accounting/adjustment-plans` | staffGuard | Adjustment Plan Service | MAPPED |
| Chart & Taxonomy Mappings | `/app/accounting/mappings` | `/ui/app/accounting/mappings` | staffGuard | Client Accounting Mapping Service | MAPPED |
| Mapping Detail | `/app/accounting/mappings/{MappingId:guid}` | `/ui/app/accounting/mappings/:id` | staffGuard | Client Accounting Mapping Service | MAPPED |
| Mapping Draft Version | `-` | `/ui/app/accounting/mappings/:id/edit` | staffGuard, unsavedChangesGuard | Mapping Draft Engine | MAPPED |
| Mapping Approval Review | `-` | `/ui/app/accounting/mappings/:id/approval` | staffGuard, unsavedChangesGuard | Mapping Independent Review Authority | MAPPED |
| Adjustment Journals Queue | `/app/accounting/journals` | `/ui/app/accounting/journals` | staffGuard | Client Accounting Journal Service | MAPPED |
| Adjustment Journal Detail | `/app/accounting/journals/{JournalId:guid}` | `/ui/app/accounting/journals/:id` | staffGuard, unsavedChangesGuard | Client Accounting Journal Service | MAPPED |
| Journal Management Evidence | `-` | `/ui/app/accounting/journals/:id/management` | staffGuard, unsavedChangesGuard | Journal Management Evidence Service | MAPPED |
| Audit Differences | `/app/accounting/differences` | `/ui/app/accounting/differences` | staffGuard | Client Accounting Difference Calculator | MAPPED |
| Accounting Period Detail | `/app/accounting/periods/{PeriodId:guid}` | `/ui/app/accounting/periods/:id` | staffGuard | Accounting Period Service | MAPPED |
| Period Roll-forward | `/app/accounting/rollforward` | `/ui/app/accounting/rollforward` | staffGuard | Period Roll-forward Calculator | MAPPED |
| Period Restatements | `/app/accounting/restatements` | `/ui/app/accounting/restatements` | staffGuard | Period Restatement Calculator | MAPPED |
| Financial Packages Queue | `/app/accounting/reviews` | `/ui/app/accounting/reviews` | staffGuard | Financial Package Service | MAPPED |
| Financial Package Detail | `/app/accounting/packages/{PackageId:guid}` | `/ui/app/accounting/packages/:id` | staffGuard | Financial Package Service | MAPPED |
| FX Rates & Policies | `-` | `/ui/app/accounting/currency-configuration` | staffGuard, unsavedChangesGuard | Currency Configuration Service | MAPPED |
| Currency Remeasurement | `/app/accounting/remeasurement` | `/ui/app/accounting/remeasurement` | staffGuard, unsavedChangesGuard | Currency Remeasurement Engine | MAPPED |
| Group Consolidation Overview | `/app/consolidation` | `/ui/app/consolidation` | staffGuard | Consolidation Service / Group Scope | MAPPED |
| Consolidation Scope Workspace | `-` | `/ui/app/consolidation/scopes/:id` | staffGuard | Consolidation Service | MAPPED |
| Advanced Consolidation | `/app/consolidation/advanced/{ScopeId:guid}` | `/ui/app/consolidation/advanced/:id` | staffGuard | Advanced Consolidation Engine | MAPPED |
| Analytical Review Prep | `-` | `/ui/app/engagements/:id/analysis/new` | staffGuard, unsavedChangesGuard | Analytical Review Service | MAPPED |
| Reconciliation Prep | `-` | `/ui/app/engagements/:id/reconciliation/new` | staffGuard, unsavedChangesGuard | Reconciliation Service | MAPPED |
| Specialist Schedule Prep | `-` | `/ui/app/engagements/:id/specialists/new` | staffGuard, unsavedChangesGuard | Specialist Schedule Service | MAPPED |
| Resource Planning | `/app/practice/resources` | `/ui/app/practice/resources` | staffGuard, unsavedChangesGuard | Practice Resource Planning Service | MAPPED |
| Practice Time & Tasks | `/app/practice/time` | `/ui/app/practice/time` | staffGuard | Practice Time Service | MAPPED |
| Practice Analytics | `/app/practice/analytics` | `/ui/app/practice/analytics` | staffGuard | Practice Analytics Service | MAPPED |
| Firm Financial Ledger | `/app/finance` | `/ui/app/finance` | staffGuard | Firm Ledger / Billing Service | MAPPED |
| Firm Books | `/app/finance/books` | `/ui/app/finance/books` | staffGuard | Firm Financial Ledger Service | MAPPED |
| Practice Invoice Detail | `/app/practice/invoices/{InvoiceId:guid}` | `/ui/app/practice/invoices/:id` | staffGuard | Practice Billing Service | MAPPED |
| Technical Library | `/app/library` | `/ui/app/library` | staffGuard | Technical Library Service | MAPPED |
| Technical Library Document | `/app/library/{DocumentId:guid}` | `/ui/app/library/:id` | staffGuard | Technical Library Service | MAPPED |
| Firm Administration | `/app/administration` | `/ui/app/administration` | staffGuard | Tenant Administration Service | MAPPED |
| User Access Management | `-` | `/ui/app/administration/users` | staffGuard | Security RoleGrant Service | MAPPED |
| Project Progress | `/app/administration/project-progress` | `/ui/app/administration/project-progress` | staffGuard | Project Progress Tracker | MAPPED |
| Tenant Connection Settings | `/app/administration/microsoft365/tenant-connection` | `/ui/app/administration/microsoft365/tenant-connection` | staffGuard, unsavedChangesGuard | Tenant Administration Service / BLOCKED_EXTERNAL for live Graph | MAPPED |
| M365 Setup Alias | `/app/administration/microsoft365` | `/ui/app/administration/microsoft365 (-> tenant-connection)` | None (Redirect) | SPA Redirect | MAPPED |
| M365 Bootstrap Setup | `/setup/microsoft365` | `/ui/setup/microsoft365` | Bootstrap / Admin guard | Bootstrap Setup Service | MAPPED |
| Operations Dashboard | `/app/operations` | `/ui/app/operations` | staffGuard | Durable Operation Coordinator | MAPPED |
| Client Portal Home | `/portal` | `/ui/portal` | clientGuard | Client Portal Inbox / ScopeGrant | MAPPED |
| Client Portal PBC Request | `/portal/requests/{RequestId:guid}` | `/ui/portal/requests/:id` | clientGuard | Client Portal Transfer Service | MAPPED |
| Client Financial Review | `/portal/accounting/packages/{PackageId:guid}` | `/ui/portal/accounting/packages/:id` | clientGuard | Client Portal Financial Package Service | MAPPED |
| Client Journal Response | `-` | `/ui/portal/accounting/journals/:id` | clientGuard, unsavedChangesGuard | Client Portal Journal Response Service | MAPPED |
| Access Refusal / Not Assigned | `/auth/access-not-assigned` | `Served via ASP.NET Core /auth/sign-in redirect flow` | Authentication | Security / TrustedActorResolver | MAPPED |

## Route Parity Analysis Summary

- **Observed source routes:** Exact current totals are maintained in `docs/execution/status.json`. `/` and `/auth/access-not-assigned` remain server-owned; each other discovered workspace template has explicit native Angular/API route ownership.
- **Automated route evidence:** `AngularRoutingContractTests` checks that the API deep-link catalogue matches Angular route definitions, that Angular runtime links do not point to legacy workbenches, and that each non-server-owned legacy workspace route has explicit native ownership. The current focused run passed; exact run evidence is in `docs/execution/status.json`.
- **Additional Angular routes:** The catalogue also contains native task routes with no direct legacy `@page` counterpart. These are Angular-only additions, not evidence that a larger feature was parity-reviewed.
- **Guard boundary:** `staffGuard`, `clientGuard`, and `unsavedChangesGuard` are client-side route behavior. API/Application authorization remains authoritative and must be verified per command/query; a route guard alone does not establish access control.
- **Canonical deep links:** The API maps the explicit route catalogue and supports canonical `/app`/`/portal` ownership. This proves route resolution only. It does not establish equivalent screen actions, role-specific content, or production behavior.

Every row's `MAPPED` label refers only to route ownership in the declared route catalogues. Feature and test parity remain `PARTIAL` until the behavior-level review is complete; see [feature parity](blazor-feature-parity.md), [test parity](blazor-test-parity.md), and [removal readiness](blazor-removal-readiness.md).
