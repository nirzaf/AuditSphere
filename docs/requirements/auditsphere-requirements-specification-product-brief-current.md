# AuditSphere — Product Brief

| Attribute | Value |
| --- | --- |
| Revision | 2.3 — proposed for adoption |
| Prepared | 24 October 2026 |
| Scope | Five existing modules; seven display phases; eleven derived audit lifecycle states |
| Implementation status | Unverified in this document review |
| Purpose | Executive scope, objectives, users, delivery priorities and acceptance boundaries |

> **Authority:** This is a review-derived revision candidate. A filename ending in `current` is the requested naming convention; it does not establish approval or implementation. The six repository counterparts were not inspected. Source-reported approved ADRs and executed contracts remain controlling until an authorized change is adopted. Proposed changes and D01–D10 decisions do not silently override them.

The [complete proposed specification](auditsphere-requirements-specification-complete-practice-management-proposed.md) governs the proposed design across this package. The [preserved v2.2 source](auditsphere-requirements-source-ste-audit-management-specification-v22.md) supplies historical requirements and approved source overlays. Companion documents use the same control identifiers and do not add product modules.

## Product objective

Provide an integrated practice-management system for an audit and accounting firm: convert a client mandate into an authorized engagement, prepare and review financial evidence, issue the correct professional deliverables, preserve the issued record, and manage the practice's own work and finances.

The revised design prioritizes a complete, traceable end-to-end workflow within the existing five modules. It retains the modular monolith, ASP.NET Core, Angular, EF Core, PostgreSQL, Microsoft identity, and the approved SharePoint integration boundary. Exact dependencies must be confirmed against the implementation; this package recommends no framework replacement.

## Business outcomes

- Know which engagements are ready, blocked, waiting for the client, under review or overdue, with the responsible staffed person and next permitted action.
- Prevent activation without the current dual-key acceptance, agreed terms and fully paid/allocated advance.
- Reproduce financial statements from the exact approved source, mapping, adjustments and reporting policy.
- Preserve evidence of preparer work, independent review, Partner decisions, management representations and required quality review.
- Release the correct immutable document bundle to authorized recipients and distinguish delivery from archive completion.
- Reconcile practice time, fees, cash, receivables and firm books without mixing client or group transactions.

## Users and decision authority

| User/capability | Core responsibility | Boundary |
| --- | --- | --- |
| PREPARER | Perform assigned work, record evidence and submit current results. | Cannot approve their own work or act as Partner. |
| REVIEWER | Review evidence, raise/clear review points within authority, support planning and SRM. | Required independence and seniority still apply. |
| APPROVER / staffed Engagement Partner | Acceptance, planning, significant-matter clearance, opinion, signing and final release decisions. | Requires current scope and the action's prerequisites. |
| CLIENT | Respond to authorized requests, provide evidence and access permitted issued output. | No internal risk assessments, reviewer notes or unrelated entity/period access. |
| Existing finance capability | Firm invoices, receipts, allocation, credits, posting and financial controls. | Financial authority does not grant professional audit sign-off. |
| Existing tenant/technical operations capability | Approved provisioning, configuration and operational recovery. | Cannot manufacture a human approval or bypass a professional gate. |
| Required independent quality-review capability | Complete the applicable independent review before report dating/signing. | Eligibility and applicability require the firm's approved methodology. |

## Five existing modules

| Existing module | Business responsibility | Owns | Boundary |
| --- | --- | --- | --- |
| **M1 Commercial & CRM Pipeline** | Lead through agreed terms | Client/contact profile, group relationships, proposals, dispatched revisions, commercial acceptance, Engagement Letter and commercial fee terms | Risk clearance comes from M2; invoices, cash and firm postings belong to M5. |
| **M2 Administration, Governance & Planning** | Eligibility, resources and plan | Acceptance/continuance, independence, assignments, calendar, service applicability, approved templates, directory provisioning requests, materiality and risk planning | Administrator access does not confer Partner authority. Planning consumes an accepted accounting dataset from M3. |
| **M3 Technical Execution & Audit Fieldwork** | Accounting preparation and audit evidence | TB/GL intake, native client accounting already in scope, mapping, adjustments, reconciliation, consolidation preparation, procedures, PBC, sampling, confirmations, reviews and SRM | Firm books and client books remain separate; consolidation never posts into component books. |
| **M4 Reporting & Final Deliverables** | Reviewed output through archive | Entity/group financial statements, notes, report composition, opinion, signed representations verification, signing, five-part bundle, release manifests, read-only freeze and inspection exports | Consumes M3 snapshots and M5 fee-note records. It cannot auto-approve financial or professional decisions. |
| **M5 Practice Analytics & Internal Bookkeeping** | Firm operations and financial control | Time, capacity analytics, rates, budgets, realization, fee milestones, firm invoices/credit notes/receipts/allocations, expenses, firm ledger, TB, P&L and AR ageing | Client sales/purchase invoices are never firm revenue. Charge-out value is not payroll cost. |

## Existing service routes

| Route already named in the sources | Required interpretation | Completion boundary |
| --- | --- | --- |
| External/statutory audit | Use all applicable audit gates and the existing eleven-state derived lifecycle. | Audited financial statements, audit report, required bundle, archive and professional review. |
| Imported accounting preparation | External books remain source of record. Reviewed adjustments and financial preparation do not imply external posting or an audit opinion. | Reconciled financial package under the selected service mandate and reporting framework. |
| Authorized native client bookkeeping | Activate only for an explicitly authorized book and period, after reviewed opening balances/cutover and applicable independence clearance. | Posted client ledger, reconciliations, period controls and statements. Optional ancillary capabilities do not block the core path. |
| Group consolidation | Group engagement consumes approved component snapshots and authorized external packs. | Group-only adjustments and reviewed consolidated financial output; component ledgers unchanged. |
| Agreed-upon procedures and internal audit | Separate the templates and mandates. ISRS 4400 relates to AUP; it is not a blanket standard for internal audit. Approve applicability before activation. | No automatic ISA opinion or assurance claim. D03 defines missing route details without adding product modules. |

## Seven-phase user experience

1. **Client onboarding:** lead, proposal, two acceptance keys, agreed terms, advance and authorized portal activation.
2. **Audit planning:** staffing, current source/mapping, materiality, risk routing, workprogram tailoring and Partner approval.
3. **Audit fieldwork:** source-linked procedures, samples or full-population testing where appropriate, PBC, evidence, confirmations and submitted results.
4. **Manager review:** independent review, returned work, cleared blocking points, current SRM and completion evidence.
5. **Partner sign-off:** current conclusions, required EQR, management-approved statements, verified signed representations and report signing.
6. **Report and delivery:** complete five-part bundle, valid fee note, exact manifest, authorized release and independently recorded delivery outcome.
7. **Archive and close:** assembly deadline, successful local freeze, independently verified secondary copy, retention and controlled corrections.

These are display phases over the eleven existing derived audit states. Task completion never sets the lifecycle directly. The bookkeeping/AUP/internal-audit routes require approved service-specific applicability and must not manufacture an audit opinion to traverse this audit workflow.

## Retained business rules

- Two current independent acceptance keys: authorized client acceptance of the exact dispatched offer, and unconditional clearance by the staffed Engagement Partner.
- Approved 50/50 fee milestones; full paid and allocated advance before activation; no configuration bypass.
- Default-off invoice automation creates drafts only. Finance approval, posting, dispatch and collection remain distinct.
- Microsoft identity for portal sign-in; no emailed or stored temporary passwords.
- One SharePoint site per client and the approved assigned-staff Full Control boundary; direct external rights remain separately accountable.
- Sunday–Thursday firm week, Friday/Saturday weekend, recorded unavailability and the established allocation-week convention.
- Domain-controlled professional gates, current approvals and exact versioned evidence.
- Authoritative append-only issued/archive evidence, with secondary SharePoint verification tracked separately.
- Client accounting, component books, group-only journals and the firm's own ledger remain isolated financial domains.

## Principal improvements in proposed revision 2.3

| Improvement | Resulting behavior | Controls |
| --- | --- | --- |
| Correct completion/signing order | Verify management representations and statement authorization before final report dating/signing. | [M4-03](auditsphere-requirements-specification-complete-practice-management-proposed.md#M4-03) |
| Precise financial chain | Source, mapping, adjustments, comparatives, movement schedules, statements and notes reconcile reproducibly. | [M3-01](auditsphere-requirements-specification-complete-practice-management-proposed.md#M3-01) · [M3-02](auditsphere-requirements-specification-complete-practice-management-proposed.md#M3-02) · [M4-01](auditsphere-requirements-specification-complete-practice-management-proposed.md#M4-01) |
| Separate package and invoice events | Resolve the balance-draft/final-bundle circular prerequisite through an adopted D01 decision. | [M4-06](auditsphere-requirements-specification-complete-practice-management-proposed.md#M4-06) · [D01](auditsphere-requirements-specification-complete-practice-management-proposed.md#decision-01) |
| Approval freshness | Changed inputs make only affected pending decisions stale; issued history remains preserved. | [CTRL-01](auditsphere-requirements-specification-complete-practice-management-proposed.md#CTRL-01) |
| Complete source-workprogram traceability | Preserve all 165 source procedures with phase-appropriate completion and review. | [rev-workprograms](auditsphere-requirements-specification-complete-practice-management-proposed.md#rev-workprograms) |
| Recoverable external operations | Reauthorize workers, reconcile unknown outcomes and prevent duplicate accepted local effects. | [ARCH-06](auditsphere-requirements-specification-complete-practice-management-proposed.md#ARCH-06) · [SEC-02](auditsphere-requirements-specification-complete-practice-management-proposed.md#SEC-02) |
| Clear archive guarantees | Distinguish deadline, committed freeze, secondary-copy observation and retention. | [M4-08](auditsphere-requirements-specification-complete-practice-management-proposed.md#M4-08) |
| Accurate practice metrics | Separate charge-out value, actual cost, billing, cash, utilization and firm accounting profit. | [M5-02](auditsphere-requirements-specification-complete-practice-management-proposed.md#M5-02) |

## Boundaries

Existing audit work on payroll, inventory, tax and loans does not introduce operational payroll, inventory, procurement, payment initiation, lending or external tax-filing products. WhatsApp remains a lead-source label. No new top-level module, framework migration, generic workflow engine or extra database is authorized by this revision. External eSignature services and Purview remain outside the approved scope.

## Acceptance and delivery priorities

1. Reconcile the current repository with the proposed contract and resolve owner decisions that block the next slice.
2. Verify authority, financial ownership, source modes, transactional invariants, concurrency and approval freshness.
3. Demonstrate one entity from accepted accounting data through financial statements, professional completion, signing and final release.
4. Verify the already-scoped native-bookkeeping and group-consolidation paths.
5. Demonstrate provider recovery, archive/correction behavior, genuine output formats, operational targets and coordinated restore on the same release candidate.

The [54 acceptance scenarios](auditsphere-requirements-specification-complete-practice-management-proposed.md#rev-acceptance) define observable outcomes; they have not been executed against the application. The [operating targets](auditsphere-requirements-specification-complete-practice-management-proposed.md#OPS-02) are proposed values for owner adoption and measurement. No completion percentage, measured capacity or production-readiness claim is made.

## Decisions and document set

The [D01–D10 decision register](auditsphere-requirements-specification-complete-practice-management-proposed.md#rev-decisions) identifies the professional, finance, tenant/security and operational owners. It covers package/billing sequencing, archive/retention, service profiles, materiality boundaries, confirmations, signing assurance, finance metrics, external access/operating targets, required quality review and supported consolidation cases.

| File | Purpose |
| --- | --- |
| [auditsphere-accounting-module-requirements-current.md](auditsphere-accounting-module-requirements-current.md) | All five modules: functional requirements, financial ownership, lifecycle and related controls. |
| [auditsphere-m365-onboarding-user-stories.md](auditsphere-m365-onboarding-user-stories.md) | Proposed Microsoft 365 onboarding and access/recovery stories within existing modules. |
| [auditsphere-requirements-catalog-domain-glossary-current.md](auditsphere-requirements-catalog-domain-glossary-current.md) | Domain definitions, canonical states, control catalogue, acceptance links and source-workflow index. |
| [auditsphere-requirements-source-ste-audit-management-specification-v22.md](auditsphere-requirements-source-ste-audit-management-specification-v22.md) | Complete supplied v2.2 source body preserved unchanged beneath an archival wrapper. |
| [auditsphere-requirements-specification-complete-practice-management-proposed.md](auditsphere-requirements-specification-complete-practice-management-proposed.md) | Full proposed v2.3 requirements and architecture, 165 procedures, 54 scenarios and decision register. |
| [auditsphere-requirements-specification-product-brief-current.md](auditsphere-requirements-specification-product-brief-current.md) | Product objectives, users, boundaries, priorities and package reading guide. |
