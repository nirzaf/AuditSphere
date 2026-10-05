# Angular migration source review — commercial workflow

**Status:** PARTIAL_REVIEWED
**Source reviewed against code:** `4aaffc31243bd524ebc48142af6b3f2c42c66d1a`
**Pinned discovery snapshot:** `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`

The six legacy source files below match their SHA-256 values in the pinned
source inventory. They remain in the Web rollback/reference host. A route or
component mapping alone does not establish behavior parity.

| Legacy source | SHA-256 |
|---|---|
| `src/AuditSphereOps.Web/Components/Commercial/FeeAgreementPanel.razor` | `dfccebd75f46b426f0bde2c0a31e5e07514149a8886114e30046edb7b378df65` |
| `src/AuditSphereOps.Web/Components/Commercial/ProposalWorkflowPanel.razor` | `48ce7b934d125e5a17dbd99f9985b156bfee527f2e2633a6c795ea24bcfec29f` |
| `src/AuditSphereOps.Web/Components/Commercial/QuotationWorkbench.razor` | `0c67a30b6460eebe193831b2b7220d2f3527cf144ccebfc68ab0295a9376a259` |
| `src/AuditSphereOps.Web/Components/Pages/CommercialSettings.razor` | `41fe4c7f57d95f08ed4fafe1927995d45e65d3725dab4b9938181125c0c5b4da` |
| `src/AuditSphereOps.Web/Components/Pages/Leads.razor` | `e467a74d007de30e672b9123489363e4e98a61054ee5ae0b2048d698efa8867c` |
| `src/AuditSphereOps.Web/Components/Pages/ProposalDetail.razor` | `952220975ed21b7af83b657ce2b307820bad300181b419b1469e8ebeaea55863` |

## Behavior and ownership mapping

| Legacy behavior | Angular, API and Application owner | Current evidence and remaining gap |
|---|---|---|
| Search, page, create and qualify commercial leads; create opportunities; revise and inspect proposal history. | Angular `features/commercial/leads.ts` and `proposal.ts`; API `UiEndpoints.Commercial.cs`; Application `PracticeLeadQuery` and `PracticeCrmService`. | `AngularCommercialJourneyTests` exercises lead and opportunity creation with lost-response recovery, proposal creation/revision and recovery. The full lead validation, role/scope, search-boundary, conflict and inaccessible-ID matrices remain open. |
| Require independent internal proposal review, record a sent state, and record a client response. | Angular `ProposalDetail` and its composed workflow; `UiEndpoints.Commercial.cs`; `PracticeCrmService`. | `AngularProposalWorkflowJourneyTests` verifies that an author cannot submit their own proposal for review, a distinct Partner reviews it, the Partner records sent status, and both acceptance and decline persist the reviewer, timestamps, response reason and opportunity state. `Mark as sent` records status only; it does not send email. These application-recorded responses do not prove an external client's identity or delivery. |
| Preview and save priced quotations, submit versions to the configured approval matrix, and produce quotation/letter/tender documents. | Angular `quotation.ts` and `documents.ts`; API `UiEndpoints.Quotation.cs` and `UiEndpoints.CommercialDocuments.cs`; Application `QuotationService`, `QuotationWorkspaceQuery`, `CommercialApprovalMatrix`, and `CommercialDocumentService`. | The commercial journey covers server preview/save/approval, persisted quotation terms, document generation, lost-response recovery, and document integrity. Full pricing-boundary, approval-rule, document-variant, download-failure and unknown-result matrices remain open. |
| Create a reviewed fee agreement, link it to an engagement, issue milestone invoices and record payments. | Angular `fee-agreement.ts`; API `UiEndpoints.FeeAgreement.cs`; Application `FeeAgreementWorkspaceQuery`, `FeeAgreementService`, and the existing billing review/post/receipt path. | The commercial journey creates an agreement after separately approved proposal state and verifies the 50/50 advance/balance milestones and finance-authority boundary. All invoice, payment, duplicate, stale-authority, and command-recovery branches are not covered here. |
| Version commercial letterhead/profile and approval rules; deactivate obsolete rules. | Angular `settings.ts`; API `UiEndpoints.CommercialSettings.cs`; Application `CommercialSettingsQuery` and `QuotationService`. | The commercial journey verifies saved profile/rule revisions, deactivation and guarded tab drafts. Complete field, validation, concurrent-edit and failure/recovery parity remains open. |
| Convert an accepted prospect through a separate reviewed conversion workflow. | Angular `client-conversion.ts` and guarded proposal route; API `UiEndpoints.ClientConversion.cs`; Application `ClientConversionWorkspace` and `PracticeCrmService.ConvertToClientDraftAsync`. | `AngularClientConversionJourneyTests` exercises both route ownership modes and reconciles a lost response without duplicate clients. A proposal acceptance only offers the separate conversion review; it does not itself create a client, engagement or portal access. The conversion and subsequent engagement-acceptance gates remain distinct. |

## Security and composition boundary

The API resolves the trusted actor and delegates business changes to
Application services; Angular does not access EF Core or call Microsoft Graph.
The focused proposal journey checks author/reviewer separation and persisted
review evidence. It does not establish all commercial role/scope, firm/client
isolation, guessed-ID, independent-review, or revoked-session cases across the
six legacy artifacts.

## Local verification

- The PostgreSQL-backed API-host Angular commercial and conversion cohort
  passed using `AngularCommercialJourneyTests` and
  `AngularClientConversionJourneyTests`.
- The new PostgreSQL-backed `AngularProposalWorkflowJourneyTests` passed for
  both acceptance and decline after a Release build of its dependent projects.
- The built-in browser rendered the Development Leads and Commercial Settings
  pages read-only. It displayed the empty lead state and the versioned
  commercial profile/approval settings; no business command was submitted.
- The full solution regression, Angular unit suite, production Angular build,
  and EF pending-model check were not run for this slice. The latest recorded
  full solution regression is a separate earlier checkpoint recorded in
  `docs/execution/status.json`.

All six source artifacts remain `PARTIAL`. Broader role/scope and isolation
assertions, exhaustive validation and failure/recovery paths, external
delivery/provider evidence, assistive-technology acceptance, production
canary/rollback, live Microsoft gates, and separate owner acceptance remain
open. This review does not authorize Blazor removal; retirement remains
`NOT_READY`.
