# AuditSphere — Domain Glossary

**Status: CURRENT.** One meaning per term. Where the STE specification and the code use different words for the same thing, both appear and the code identifier is the one to use in new code. Code values are string constants in `src/AuditSphereOps.Domain/` unless a path says otherwise.

## Roles and scope

| Term | Meaning | Code |
| --- | --- | --- |
| RoleGrant | An explicit grant of an application role at one scope. The only source of AuthorizationDecision; Microsoft group membership never grants AuditSphere access. | `Domain/Security/Users.cs`, `Application/Security/` |
| Scope | Breadth of a grant: `FIRM_WIDE`, `CLIENT`, `ENGAGEMENT`, or `GROUP` (consolidation group, stored as a group grant). An engagement grant never reaches a sibling engagement. | `RoleAssignmentService` |
| Staffing level | Engagement staffing tier: `ENGAGEMENT_PARTNER`, `AUDIT_MANAGER`, `SENIOR_AUDITOR`, `STAFF_ASSOCIATE`. Specification APPROVER / REVIEWER / PREPARER map onto these. | `StaffingLevels` (`Practice/ResourcePlanning.cs`) |
| Charge-out role | Rate-card role name: Engagement Partner, Audit Manager, Audit Supervisor (alias Senior), Audit Associate (alias Junior). Unknown names get no rate. | `SteChargeOutRateBaseline` |
| Session epoch | Counter on a user's session; revocation advances it and every protected read/command rechecks it. | `Application/Security/` |

## Commercial and onboarding

| Term | Meaning | Code |
| --- | --- | --- |
| Lead channel | How contact started: Phone, WhatsApp, Email, Web Form, Referral, In-person. A label only; no channel integration. | `Practice/Crm.cs` |
| Client relationship | Group structure link: `PARENT`, `SUBSIDIARY`, `AFFILIATE`. | `ClientRelationshipKinds` |
| Contact routing | Which contact receives which document class (proposals/letters/reports, invoices/receipts, PBC requests, completion). | `PracticeCrmService.Routing.cs` |
| Quotation / Brief quotation | Versioned 1–2 page price offer from approved rate cards. | `QuotationService`, `QuotationCalculator` |
| Proposal / Tender | Comprehensive five-chapter proposal (firm profile, team CVs, credentials, methodology, fees and timeline). | `CommercialDocumentService.Tender.cs` |
| Dual-key gate | Key 1 = recorded client commercial acceptance; Key 2 = current unconditional Partner risk clearance for the same service. Both are required before an engagement letter. | `AcceptanceDecisionService`, `CommercialDocumentService` |
| Engagement letter | ISA 210 terms. Template chosen by service route: statutory audit, internal audit, or agreed-upon procedures (ISRS 4400). Unsupported routes fail closed. | `EngagementLetterTemplates.cs` |
| Fee agreement / milestones | The reviewed agreement and its 50 % advance and 50 % balance milestones, created with the letter. | `FeeAgreementService`, `FeeAgreementWorkspaceQuery` |
| Advance invoice preparation state | Why the advance draft invoice exists or does not yet (including "pending — automation disabled"). | `AdvanceInvoicePreparationStates` |
| Client conversion | The record that turns a lead into a client and captures portal intent. | `Practice/ClientConversion.cs` |

## Planning and materiality

| Term | Meaning | Code |
| --- | --- | --- |
| Benchmark | Base for planning materiality: `REVENUE` (0.5–2 %), `PROFIT_BEFORE_TAX` (5–10 %), `TOTAL_ASSETS` (0.5–1 %), `NET_ASSETS` (1–2 %). PBT is mapped balances excluding tax, **not normalized**. | `MaterialityBenchmarks`, `MaterialityCalculator.RateRanges` |
| PM | Planning materiality = benchmark × rate. | `MaterialityCalculator` |
| TE | Tolerable error / performance materiality = PM × 50–75 %. | `PerformanceRange` |
| SAD threshold | Summary of Audit Differences trivial cutoff = PM × 3–5 %. | `TrivialRange` |
| Practical rounding | Manager-only adjustment of PM/TE/SAD within ±5 % of computed values; append-only decision creating a new effective draft that the Partner then approves. | `MaterialityPracticalRounding`, `MaterialityEngineService` |
| Risk band | `GREEN` balance below TE; `AMBER` between TE and PM with low inherent risk; `RED` above PM, critical estimate or high inherent risk (mandatory Manager execution and Partner review). | `RiskBands`, `ProcedureRiskBandEvaluator` |
| Five-folder workspace | `01_Administration & Planning` … `05_Final Signed Archive`, provisioned on acceptance at `/Client/Year/01–05`. | `EngagementWorkspaceProvisioningHandler` |

## Fieldwork and review

| Term | Meaning | Code |
| --- | --- | --- |
| FSLI | Financial statement line item; the unit the split dashboard, workprogrammes, risk bands and modified opinions attach to. | Mapping and statement services |
| Audit area | Workprogramme area code: `CASH_BANK`, `RECEIVABLES`, `INVENTORY`, `REVENUE`, `PAYABLES`, `FIXED_ASSETS`, `EXPENSES`, `PAYROLL`, `LOANS`, `EQUITY`, … | `AuditAreaCodes` (`Audit/Fieldwork.cs`) |
| AR test | Analytical review of one FSLI (multi-period variance, ratios), including the ISA 570 going-concern horizon for audit engagements. | Analytical review services |
| Ad-hoc step | Practitioner-added procedure row in an active workprogramme; revisions count toward completion. | `FieldworkConnections` |
| Physical evidence index | Binder reference such as `X-1, Box 3, Shelf B`, with movement history and two-way procedure links. | `FieldworkConnections` |
| Under Rework | Specification term for a returned item. Code uses `CHANGES_REQUIRED` and anchored review notes; open notes block approval. | `AuditAreaAssessmentStatuses`, review notes |
| SRM | Summary Review Memorandum; generated in the final applicable workprogramme review transaction, bound to a digest of reviewed facts. | `DeliverableKinds.SummaryReviewMemorandum` |
| Critical confirmation | A Bank/AR/AP/Inventory/Legal confirmation flagged critical; while it lacks a returned and evaluated response, the report is held and a holding letter generated. | `AuditDeliverableService`, `HoldingLetterDispatch` |
| Holding letter | "Pending confirmation" letter queued to the client-management contact. Queued is not delivered; missing routing blocks dispatch. One dispatch per outstanding blocker set. | `DeliverableKinds.HoldingLetter` |

## PBC portal

| Specification badge | Code state (`PbcStates`) |
| --- | --- |
| Pending Upload | `SENT`, `ACKNOWLEDGED`, `PARTIALLY_RECEIVED` |
| Under Review | `RECEIVED`, `UNDER_REVIEW`, `RESUBMITTED` |
| Approved | `ACCEPTED`, `CLOSED` |
| Rejected / Re-upload Required | `CLARIFICATION_REQUIRED` (reason mandatory and shown to the client) |

This badge mapping is inferred from the state names and the portal behaviour described in the STE coverage table; confirm it against the portal badge component before relying on it in a UI change. `DRAFT` requests are never visible to the client. Upload transport states (`PbcUploadStates`) are separate: `STARTED`, `CHUNKING`, `STAGED`, `RECEIVED`, `FAILED`, `EXPIRED`.

## Completion and archive

| Term | Meaning | Code |
| --- | --- | --- |
| Opinion type | `UNMODIFIED` (specification "Clean / Unqualified"), `QUALIFIED`, `ADVERSE`, `DISCLAIMER`. Modified types require an FSLI from the approved taxonomy and a basis paragraph. | `AuditOpinionTypes` |
| Deliverable kind | `SUMMARY_REVIEW_MEMORANDUM`, `AUDIT_FINDINGS_REPORT`, `MANAGEMENT_LETTER`, `INDEPENDENT_AUDITORS_REPORT`, `HOLDING_LETTER`, `REPRESENTATION_LETTER`. | `DeliverableKinds` |
| Five-part bundle | Immutable ZIP: signed report and certified statements, management letter, verified signed LOR, correspondence trail, posted balance invoice; per-file hashes plus manifest. | `AuditDeliverableService.Bundle.cs` |
| Visual credential | Registered PNG signature or versioned firm-seal PNG. Not a certificate-backed signature. | `RegisterSignatureAsync`, `RegisterFirmSealAsync` |
| File freeze | `SCHEDULED` (60 days after report signature) → `FROZEN`; `AMENDMENT_OPEN` while a second Partner-approved amendment runs. | `FileFreezeStates` |
| Early compliance lock | Partner lock before day 60, bound to a reviewed archive-readiness digest and countdown revision. | `FileFreezeService.RequestEarlyComplianceLockAsync` |
| External read-only | Whether SharePoint read-only was `NOT_REQUESTED`, `REQUESTED` or `OBSERVED`. Local freeze does not imply it. | `ExternalReadOnlyStates` |

## Lifecycle

The specification §5 states are a **derived projection** (`CanonicalEngagementStages` in `Application/Acceptance/EngagementLifecycleQuery.cs`, endpoint `GET /api/ui/engagements/{id}/lifecycle`), not a stored status column: `LEAD_INGESTION` → `PROPOSAL_GENERATION` → `DUAL_KEY_PENDING` → `ADVANCE_BILLING` → `PORTAL_ACTIVE_PLANNING` → `FIELDWORK_EXECUTION` → `MANAGERIAL_REVIEW` → `PARTNER_APPROVAL` → `DELIVERABLE_RELEASE` → `COMPLIANCE_COUNTDOWN` → `ARCHIVED_READ_ONLY`. A stage advances only when the underlying gates' records exist (ADR-0004).

## Status vocabulary used in docs

`CURRENT`, `APPROVED`, `PROPOSED`, `HISTORICAL_SOURCE`, `SUPERSEDED` (document authority); `PASS_LOCAL_SLICE`, `LOCAL_VERIFIED`, `PARTIAL`, `READY_FOR_REVIEW`, `BLOCKED_EXTERNAL`, `OUT_OF_SCOPE` (evidence and gate state, recorded only in `docs/execution/status.json`).
