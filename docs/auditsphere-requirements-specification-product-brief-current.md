# AuditSphere — Product Brief

**Status: CURRENT.** One-page product framing for agents and reviewers. The binding detail is the [STE Audit Management Tool specification v2.1](auditsphere-accounting-module-requirements-current.md); where this brief and an ADR differ from that specification, the ADR records the approved deviation.

## Problem

The firm's commercial audit platform charges per client file above a fixed tier (the specification cites a limit of 130 client files). The firm needs an in-house platform with no per-file penalty that runs the whole engagement lifecycle — sale, acceptance, planning, fieldwork, review, reporting, archive — plus the firm's own practice books, in strict alignment with ISA 210, 220/ISQC 1, 230, 320, 505, 570, 700 and 705.

## Users and their AuditSphere roles

| Specification persona | Who | Core decisions they own |
| --- | --- | --- |
| PREPARER | Audit Associate / Junior | Executes FSLI procedures, attaches digital and physical evidence, submits for review, logs time. |
| REVIEWER | Audit Senior / Manager | Reviews work, raises review notes (rework loop), sets sampling, calculates and rounds materiality, compiles the SRM. |
| APPROVER | Engagement Partner | Risk clearance (Key 2), engagement letter, Red-risk clearance, SRM sign-off, opinion, signature/seal, release, early lock. |
| CLIENT | Client coordinator / CFO / MD | PBC uploads and responses, receives invoices, holding letters and deliverables; upload rights freeze at release. |

AuditSphere authorizes each of these through explicit `RoleGrant` scope (`FIRM_WIDE`, `CLIENT`, `ENGAGEMENT`, `GROUP`); finance, administration and specialist roles exist beside them.

## In scope (five modules)

1. **Commercial and CRM** — leads, client group structure, role-routed contacts, quotation and five-chapter proposal, dual-key gate, engagement letter, 50 % advance invoice, receipt, client portal.
2. **Administration, governance and planning** — acceptance/continuance, five-folder workspace, staffing and capacity, TB-linked three-tier materiality, Green/Amber/Red routing, Partner planning sign-off.
3. **Fieldwork** — TB ingestion and FSLI mapping with memory, split P&L / balance-sheet dashboard, analytical review, workprogrammes with ad-hoc steps, sampling, hybrid evidence, going concern, three-tier review, SRM, confirmations and holding letter.
4. **Reporting and archive** — four-way opinion with modified-opinion basis, signed report, five-part bundle, balance invoice, portal freeze, 60-day (or Partner early) lock.
5. **Practice management** — charge-out rates, profitability, utilization, budget variance, firm ledger, firm TB/P&L, AR ageing.

Beyond the specification, the owner added: client accounting workspace and group consolidation (`Accounting/`), one SharePoint site per new client, and an optional authorized client-bookkeeping service (see `AGENTS.md` §1).

## Non-goals

- Autonomous audit conclusions or opinions; every professional judgement stays human.
- Certificate-backed digital signatures and eSignature providers (ADR-0005); Purview records integration.
- Live outbound WhatsApp or social channels — channels are captured as labels on leads; outbound mail goes through the consented Graph mail capability only.
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
| Archive compliance | File read-only no later than 60 days after report signature | `FileFreezeService` schedule and worker freeze; external SharePoint enforcement remains `BLOCKED_EXTERNAL` |
| Production readiness | External gates P1–P10 closed with named human evidence | `docs/execution/status.json` `externalGates` |
