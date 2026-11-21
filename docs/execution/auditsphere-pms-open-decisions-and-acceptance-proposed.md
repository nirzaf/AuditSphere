# AuditSphere PMS open decisions and acceptance

**Status: PROPOSED · Decision aid only · Reviewed 2026-11-21**

This document recommends safe defaults and records evidence gaps. It is not an approved requirement, professional decision, records schedule, accounting policy, named appointment, or release authorization. No safeguard is relaxed by this proposal.

## Evidence and authority

The trigger is the attached `AUDITSPHERE-PMS-COMPLETION-GAPS.md` (SHA-256 `8c98b25323d49a39b1169920b9a9273f93009e9f40d6a433d0a2a9b899c78c0f`). The repository [requirements index](../requirements/auditsphere-requirements-index-current.md) classifies the revision 2.3 product/accounting material and complete PMS specification as proposed. Use the [documentation index](../auditsphere-docs-index.md) and [AGENTS.md](../../AGENTS.md) authority order; this brief does not promote those sources.

Relevant approved decisions already constrain implementation:

- [ADR-0004](../architecture/adr/auditsphere-architecture-adr-0004-engagement-lifecycle-derived-projection-approved.md): lifecycle stage is derived from gate records; there is no writable stage setter.
- [ADR-0009](../architecture/adr/auditsphere-architecture-adr-0009-archive-authority-release-store-approved.md): the append-only release/archive store is authoritative; SharePoint is a secondary copy and is `OBSERVED` only after hash and grant readback.
- [ADR-0007](../architecture/adr/auditsphere-architecture-adr-0007-fee-invoice-automation-drafts-only-approved.md): optional invoice automation is off by default and creates drafts only; finance approval and posting remain separate.
- [ADR-0013](../architecture/adr/auditsphere-architecture-adr-0013-approved-mapping-lineage-for-practice-time-approved.md): time may retain exact approved mapping lineage; it does not post journals or decide classification.
- [ADR-0018](../architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md): specified reporting bases and role aliases are approved within scope; the ADR explicitly leaves broader fee, accounting, legal, and professional policies open.
- [ADR-0016](../architecture/adr/auditsphere-architecture-adr-0016-certificate-signed-final-report-approved.md): firm-held certificate signing is supported, but certificate custody and legal acceptance are separate.

The approved methodology addendum names **Mohamed Fazrin, Firm Methodology Owner**. That appointment is the only named role-holder found in the reviewed repository authority sources. It does not appoint him as Engagement Partner, records custodian, finance controller, EQR reviewer, accessibility reviewer, product owner, deployment operator, or backup owner. No individual for those seats is named in the reviewed approved sources. That is a repository evidence limitation, not a claim that the firm has no such people. Connected Drive search returned no matching result on the first query and then failed with a transport error; this brief therefore makes no claim about documents outside the repository.

The user supplied the `AuditSphere | STE Audit User Role Nominations | 20 Nov 2026` tracker for role review. Its recorded read found 13 populated nominations, **0/9 roles with an approved nominee**, 11 missing exact client/engagement IDs, all populated rows still marked `Needs nomination`, and no Fazrin Administrator row. The sheet says it does not grant access. It is therefore a nomination source, not an approved assignment sheet, and it does not name the accountable owners for Q01/Q02/Q04-Q08. No application role, Entra permission or sheet value was changed. Read-only summary: `.local/autonomous/evidence/pg19-live-readonly-5d38e40e-20261121T061336Z/role-sheet-readonly-summary.json`.

## Decisions, owners, and closure evidence

For each row, the listed role is a proposed accountable role, not an appointment. “Actual person recorded” reports only named appointments found in reviewed approved sources.

### Q01 · Engagement completion and terminal eligibility

- **Existing source / gap:** ADR-0004 requires a derived lifecycle projection and gate-specific commands. The gap audit traces lifecycle, completion, archive handoff, and `RecordsArchiveService`; it does not identify an approved service-specific terminal policy or supported producer for the archive prerequisite. The proposed product brief and complete PMS specification are not authority.
- **Proposed owner / actual person:** Product/practice owner plus the responsible Engagement Partner for each affected service route. No actual person for either role is recorded. Mohamed Fazrin is named only as Firm Methodology Owner; do not infer Partner authority.
- **Recommended default / impact:** Keep existing gates and archive refusal. Define route-specific evidence and an auditable gate command; retain derived stage semantics. Do not add a generic `SetStage`, treat task completion, report download, freeze, or archive creation as completion, or apply audit sign-off to bookkeeping. Until approved, T03 and dependent first-archive T05 remain blocked.
- **Acceptance checklist:** identify the authoritative requirement version and exact enabled route; define terminal meaning and current prerequisites; eligible actor and any independent review; behavior under hold/freeze; reopen/amendment rules; generation/revision, authorization, lock, replay and retained receipt; prove no archive/completion cycle; approve by the qualified owner and responsible Partner.

### Q02 · Records-profile policy and approval

- **Existing source / gap:** ADR-0009 establishes archive custody boundaries. `RecordsProfile`, profile creation, archive use, and handoff are present in `Records.cs`, `RecordsArchiveService`, and `UiEndpoints.AuditRecords.cs`; creation defaults unapproved. The approved profile contents, jurisdiction/route, retention trigger/duration, hold treatment, approval authority, and amendment basis remain unspecified in approved sources.
- **Proposed owner / actual person:** Records owner/custodian and qualified practice owner. Neither actual person is named in reviewed approved sources. Do not infer a person from commit authors or the methodology addendum.
- **Recommended default / impact:** New profiles remain unapproved; keep archive creation blocked without an approved exact-scope version. Do not invent a retention period, disposal or hold-release permission, auto-approve a profile, or rewrite versions already bound to archives. T04 and dependent T05 remain blocked.
- **Acceptance checklist:** approve profile fields and route/jurisdiction; retention start event and duration by record class; legal-hold start/release authority and treatment; eligible independent approver and conflict rule; review basis and amendment/version rules; archive binding and historic-version behavior; evidence and sign-off retained against the exact reviewed version.

### Q03 · Exact-source CI

- **Existing evidence / status:** **PASS for executable candidate `9245f337ab0a943fa37aa39eb9e59ec34337ee59` only.** Fast/docs run [44875320945](https://github.com/nirzaf/AuditSphere/actions/runs/44875320945), image run [44875825037](https://github.com/nirzaf/AuditSphere/actions/runs/44875825037), and exact full run [44876075907](https://github.com/nirzaf/AuditSphere/actions/runs/44876075907) passed. All eight full-run terminal logs prove checkout of that exact SHA; all three image jobs prove the same source and retain immutable pins. Angular passed 2,138 tests in 200 files, API 441/441, Domain 2,071/2,071, and Chrome/PostgreSQL E2E 351/351; zero failures or skips. Contracts/publication and the full-release contract passed. Evidence is under `.local/autonomous/evidence/ci-9245f337-current-20261121/`; full review: `full/full-terminal-review.json`.
- **Owner / actual person:** Engineering/release owner; no individual appointment is named in reviewed sources. The workflow jobs and immutable artifacts establish the CI result, not human release approval.
- **Impact / closure:** Q03 is closed for exact SHA `9245f337` only. It does not establish production recovery, human approval, or whole-practice acceptance. A later code successor needs its applicable exact-source gates; predecessor results must not be substituted.

### Q04 · Deployment configuration, custody, backup, and restore

- **Existing source / gap:** AGENTS.md and ADR-0009/0016 define provider, archive, and certificate boundaries; the [operational evidence register](../spec/completion-operational-evidence.json) says local restore only and operational acceptance not assessed, with RPO/RTO, capacity, retention, and custody values unset. Candidate `9245f337ab0a943fa37aa39eb9e59ec34337ee59` passed exact-source CI and a pinned PG19 synthetic rehearsal but has not been deployed. The latest read-only Hostinger baseline at 2026-11-21 08:57:17 UTC still shows PostgreSQL 18.6 on mutable `postgres:18-alpine`, a writable data volume and an `auditsphere` superuser login. The two listed provider snapshots (Nov 20 and Nov 13) are not a fresh quiesced database/catalog plus actual volume/configuration/credential/overlay backup or an independent restore proof. No secrets or client records were read. The parseable evidence copy is `.local/autonomous/evidence/pg19-9245-status-refresh-20261121/normalized-hostinger-readonly-baseline.json`; its receipt preserves and hash-binds the original capture without changing facts.
- **Proposed owner / actual person:** Deployment/service owner, records custodian, infrastructure/backup owner, and applicable tenant/provider operators. No actual individuals for these roles are named in reviewed approved sources. D-OV04 was already requested; do not repeat that request.
- **Recommended default / impact:** No production deployment, permission change, provider mutation, or claim of live custody/restore acceptance. Maintain current access and mail pause. Release remains blocked until the owner-authorized evidence is collected for one immutable candidate. A synthetic PG19 rehearsal is not a restored production copy; final result: `.local/autonomous/evidence/pg19-image-rehearsal-9245f337-internal/attempt-03/result.json`.
- **Acceptance checklist:** exact-source fast/full/images; current image rehearsal with retained actual immutable pins; fresh read-only live baseline; fresh quiesced database/catalog and actual mutable Docker-volume, credential, and configuration backups; independent cleanup/runtime guards; deployment-bound configuration and migration readback; archive hash/grant custody evidence; restored-copy/backup evidence; genuine public HTTPS and authenticated acceptance; bind all passes and receipt to the candidate. No destructive restore is implied.

### Q05 · Client/engagement offboarding

- **Existing source / gap:** Offboarding readiness, closeout, and financial-coverage paths exist in `PracticeCrmService.OffboardingReadiness.cs`, `PracticeCrmService.OffboardingCloseout.cs`, and `PracticeCrmService.OffboardingFinancialCoverage.cs`. The gap audit did not re-audit all behavior or locate an approved final treatment policy.
- **Proposed owner / actual person:** Engagement/practice leadership with records/compliance and finance/access owners. No actual people are appointed in reviewed approved sources.
- **Recommended default / impact:** Preserve unresolved mandate, balance, dispute, and retention distinctions. Do not write off balances, delete identities or records, silently restore access, or mark closure complete while required decisions are unresolved. Offboarding is not broadly accepted until the policy and positive/negative path evidence are reviewed.
- **Acceptance checklist:** approved treatment for open mandates and work; balance/dispute resolution and authority; access revocation and separate reactivation approval; retained records and legal holds; closeout evidence; idempotent retry and denial cases; authorized role-specific review.

### Q06 · Effective-dated time rating

- **Existing source / gap:** ADR-0013 covers optional approved FSLI lineage; ADR-0018 approves named rate aliases and reporting bases, not the firm's adopted charge categories, actual rates, or override authority. The revision-2.3 accounting requirements are proposed.
- **Proposed owner / actual person:** Practice/resource management and finance. No actual person is named in reviewed approved sources.
- **Recommended default / impact:** Require an explicitly approved effective-dated category/rate mapping. Missing or stale rates make dependent values unavailable; do not infer a rate from title, role, cost, or a proposed example. Preserve captured historical rate/mapping identity. Rate automation and value reporting remain limited until approved.
- **Acceptance checklist:** approve categories, currency, rates, effective intervals, staff eligibility, exceptions and approver; verify boundary dates, missing/stale rate behavior, historical pinning, authorization and report basis against a reviewed sample.

### Q07 · Billing-to-firm-ledger recognition

- **Existing source / gap:** ADR-0007 permits draft invoice creation only and preserves FinanceReviewer approval/FinanceManager posting. ADR-0018 distinguishes firm receivables from client bookkeeping. No approved complete invoice/payment-to-ledger recognition, account mapping, currency, or cutover policy was found.
- **Proposed owner / actual person:** Finance controller/accounting lead. No actual person is named in reviewed approved sources.
- **Recommended default / impact:** Keep automatic journal recognition off. An invoice being posted or paid does not by itself establish revenue recognition. Do not mix firm revenue with client books or group consolidation. Ledger automation remains blocked pending controller-approved accounting rules.
- **Acceptance checklist:** define recognized source events, accounts, recognition timing, currency/FX basis, cutover and reversals; prove idempotency and duplicate/unknown-outcome recovery; reconcile a representative period to authorized source documents; independently review maker/checker permissions and retain the approved mapping/version.

### Q08 · Route, professional, accessibility, and operating acceptance

- **Existing source / gap:** AGENTS.md preserves separate firm, client, and group books; import-first client accounting remains supported; native bookkeeping and tax filing need separate authorization. ADR-0017 requires the staffed Engagement Partner for specified partner-only actions; ADR-0016 separates certificate validity from legal acceptance. The operational evidence register leaves production targets and accessibility acceptance unassessed. Chrome is the requested browser; Edge is not part of verification.
- **Proposed owner / actual person:** EQR/professional-standards reviewer, product owner, accessibility reviewer, and operations owner. No actual individuals for these roles are named in reviewed approved sources.
- **Recommended default / impact:** Claim acceptance only for named routes and exercised personas. Keep professional conclusions human, require independent review where approved policy says so, and do not imply native client bookkeeping, tax, or every practice route is complete. Full PMS acceptance remains open.
- **Acceptance checklist:** signed route/feature coverage register; representative firm, audit, import-first client and group journeys; human EQR and output review; named accessibility reviewer and Chrome/assistive-technology outcomes; approved RPO/RTO, capacity and latency targets with measurements; security/authorization and recovery evidence; observed exceptions and accepted residual risks.

## Consolidated decisions that still need people

1. **T03/T04/T05:** nominate the actual product/practice owner and responsible Engagement Partner for Q01, and the actual records custodian/approver and qualified practice owner for Q02; provide the versioned approved route and records rules using the checklists above. These decisions block those dependent archive-eligibility tasks only.
2. **Production release:** name the deployment/service, records-custody, and infrastructure/backup owners and provide the candidate-bound Q04 evidence. This blocks release; CI alone does not authorize deployment.
3. **Whole-practice claim:** name the Q05–Q08 accountable individuals and approve only the policies/routes they own. These are scoped feature/acceptance dependencies, not a reason to halt independent engineering.

Until names and decisions are recorded by the authorized owners, the proposed defaults remain recommendations only. The automation should continue unrelated ready engineering and should not mark the whole assignment, production release, or PMS acceptance complete.
