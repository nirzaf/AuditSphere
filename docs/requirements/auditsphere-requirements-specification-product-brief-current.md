# AuditSphere — Product Brief

**Status: CURRENT.** One-page product framing for agents and reviewers. The binding detail is the [STE Audit Management Tool specification v2.2](auditsphere-accounting-module-requirements-current.md); where this brief and an ADR differ from that specification, the ADR records the approved deviation.

## Problem

The firm's commercial audit platform charges per client file above a fixed tier (the specification cites a limit of 130 client files). The firm needs an in-house platform with no per-file penalty that runs the whole engagement lifecycle — sale, acceptance, planning, fieldwork, review, reporting, archive — plus the firm's own practice books, in strict alignment with ISA 210, 220/ISQC 1, 230, 320, 505, 570, 700 and 705.

## Users and their AuditSphere roles

| Specification persona | Who | Core decisions they own |
| --- | --- | --- |
| PREPARER | Audit Associate / Junior | Executes FSLI procedures, attaches digital and physical evidence, submits for review, logs time. |
| REVIEWER | Audit Senior / Manager | Reviews work, raises review notes (rework loop), sets sampling, calculates and rounds materiality, compiles the SRM. |
| APPROVER | Engagement Partner | Risk clearance (Key 2), engagement letter, Red-risk clearance, SRM sign-off, opinion, signature/seal, release and early lock. ADR-0017 requires the currently staffed Engagement Partner for SRM clearance, opinion, report signing, signed-representation verification and bundle assembly. |
| CLIENT | Client coordinator / CFO / MD | PBC uploads and responses, receives invoices, holding letters and deliverables; upload rights freeze at release. |

AuditSphere authorizes each of these through explicit `RoleGrant` scope (`FIRM_WIDE`, `CLIENT`, `ENGAGEMENT`, `GROUP`); finance, administration and specialist roles exist beside them.

## In scope (five modules)

1. **Commercial and CRM** — leads, client group structure, role-routed contacts, quotation and five-chapter proposal, dual-key gate, engagement letter, approved 50/50 fee milestones, advance receipt and client portal. Optional invoice automation creates drafts only; the balance draft follows paid advance and release of the linked financial package, and the full allocated advance gates activation ([ADR-0007](../architecture/adr/auditsphere-architecture-adr-0007-fee-invoice-automation-drafts-only-approved.md), [ADR-0012](../architecture/adr/auditsphere-architecture-adr-0012-unconditional-advance-activation-hard-block-current.md)).
2. **Administration, governance and planning** — acceptance/continuance, five-folder workspace, Sunday–Thursday workweek, staffing and capacity, TB-linked three-tier materiality using mapped PBT without normalization, Green/Amber/Red routing, Partner planning sign-off ([ADR-0011](../architecture/adr/auditsphere-architecture-adr-0011-materiality-pbt-without-normalization-approved.md), [ADR-0017](../architecture/adr/auditsphere-architecture-adr-0017-ste-v22-delegated-requirement-decisions-approved.md)).
3. **Fieldwork** — TB ingestion and FSLI mapping with memory, split P&L / balance-sheet dashboard, analytical review, workprogrammes with ad-hoc steps, sampling, hybrid evidence, going concern, three-tier review, SRM, confirmations and holding letter.
4. **Reporting and archive** — four-way opinion with modified-opinion basis, final-report certificate-signing capability using a firm-held X.509 certificate, five-part bundle, balance-invoice draft only after the advance is paid and the linked financial package is released, portal freeze, 60-day (or authorized Partner early) local lock. The append-only release/archive store is authoritative; SharePoint is a secondary copy whose protection remains `BLOCKED_EXTERNAL` until exact hashes and grants are read back ([ADR-0009](../architecture/adr/auditsphere-architecture-adr-0009-archive-authority-release-store-approved.md), [ADR-0016](../architecture/adr/auditsphere-architecture-adr-0016-certificate-signed-final-report-approved.md)).
5. **Practice management** — charge-out rates and billable-time-based value/realization, utilization as billable minutes over capacity net of unavailability, budget variance, firm ledger, monthly comparative firm P&L and ageing of all posted firm invoices; rate aliases apply wherever rates are looked up ([ADR-0018](../architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md)).

Specification v2.2 adds a **workflow experience layer** over these modules: a seven-phase display of the existing canonical engagement states, enhanced Dual-Key Acceptance readiness cards, and task templates, queues, recurrence and safe automation (specification addendum and §§5.3, 6, 7). The phases are presentation only; the canonical states, gates and the three financial boundaries are unchanged. Delivery status is tracked in the [v2.2 delta](../execution/auditsphere-execution-tracker-ste-v21-gap-verification-current.md#v22-workflow-experience-delta).

Beyond the specification, the owner added: client accounting workspace and group consolidation (`Accounting/`), one SharePoint site per new client, and an optional authorized client-bookkeeping service (see `AGENTS.md` §2).

## Non-goals

- Autonomous audit conclusions or opinions; every professional judgement stays human.
- Workflow automation that approves, signs, posts, sends or releases anything, or that moves an engagement past a gate; the v2.2 task layer only creates tasks, reminders and drafts for a human to act on.
- External eSignature providers and Purview records integration remain out of scope (ADR-0005). Firm-held X.509 signing of the final report is approved under ADR-0016; the production requirement and legal acceptance remain separate decisions.
- WhatsApp is a lead-channel label only: nothing is ingested or sent through it, and proposals are dispatched by email (ADR-0017). Other live social channels remain out of scope.
- Emailed temporary passwords for the client portal (replaced by Microsoft sign-in, ADR-0003).
- Automatic invoice approval, posting, sending or payment execution (ADR-0007).
- Payroll execution, inventory, procurement operations, external tax filing.
- A second ERP, microservices, message brokers, Kubernetes, a Python backend or a React frontend.

## Success measures

| Measure | Target | How it is shown |
| --- | --- | --- |
| Licensing | No limit on clients, engagements or working papers | No licensing or per-file count gate. Caps such as `AccountingTaskOwnerQuery.MaxEngagements` bound a single query's result, not what the firm may hold (NFR policy §3). Not yet independently audited across every query. |
| Lifecycle integrity | Every stage in specification §5 reachable only through its gate | `EngagementLifecycleQuery` stage projection; gate refusals in Application commands |
| Professional control | Zero automated professional decisions | Partner/Manager commands require the deciding identity; ADR register |
| Archive compliance | Issued evidence remains verifiable in the authoritative release/archive store; local freeze is enforced by 60 days after report signature or earlier authorized Partner lock | `FileFreezeService` schedule and worker freeze; SharePoint is a secondary copy and remains `BLOCKED_EXTERNAL` until exact hashes and expected grants are read back (ADR-0009) |
| Production readiness | External gates P1–P10 closed with named human evidence | `docs/execution/status.json` `externalGates` |
