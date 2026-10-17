# AuditSphere — Atomic Implementation Specification for the Identified Gaps

**Status:** SPECIFIED, with explicitly blocked decision, context and external-acceptance portions.

**Mode:** SPEC_ONLY — repository inspection and specification authoring only.

**Specification scope:** All 33 work items in the supplied gap report, plus its expressly identified native-bookkeeping and production-volume acceptance boundaries.

**Task count:** 43 dependency-ordered tasks. A task may be implementation, a necessary decision, or acceptance; this is not a count of 43 confirmed defects.

**Prepared at:** 2026-10-16T21:24:54Z UTC.

**Repository:** `nirzaf/AuditSphere`. Requested/default ref: `master`.

**Resolved source HEAD:** `3ec9c5d3d0cea098a190d4fdd8618120bd11aebd`.

**Supplied report baseline:** `ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318`.

**Output:** One UTF-8 Markdown specification; no source, database, infrastructure, Git, workflow, provider or production change has been made.

---

## Completion checklist

**Snapshot:** 2026-10-17 · `master` at `fa60da52` (103 commits after the spec baseline `3ec9c5d3`).

**Legend:** `[x]` implemented on `master` **and** focused verification observed · `[~]` implemented on `master`, focused verification not recorded · `[ ]` open, partial under an unmet gate, or blocked (gate in brackets).

**Basis:** commits that change each task's owner files (table B2), commit subjects and the repository's own records. One check was run for this snapshot: `python3 -m unittest discover -s scripts/release -p 'test_verify_release_source.py'` → 3 tests OK. No full suite, database-backed test or hosted-CI result is used as proof.

**Warning:** hosted CI on head `fa60da52` fails at `Build · .NET Release` (OpenAPI contract drift). No green hosted CI is recorded for the implementing commits.

**Totals:** 1 completed · 12 implemented, not verified · 30 open or blocked.

**File history:** renamed from `AuditSphere_Atomic_Implementation_Spec (1).md`.

### Current intent and evidence defects (Tasks 01–08)
- [~] **01** Activation requires the current commercial key at commit — `6c375894`. The API test for AC-01-03 is not in that change set.
- [~] **02** All completion consumers require current independent procedure evidence — `0b0f0ac1`, `c1b7c5e9`.
- [~] **03** Invoice preparation binds every billed source to the permitted client — `8488ce1b`, `6c375894`.
- [~] **04** Finance mutations retain and recover one exact original outcome — `8488ce1b`, `40497f0b`, `8b9e95d3`.
- [~] **05** Invoice drafts and assent belong to one exact route visit — `48dae6f5`, `737b1684`.
- [~] **06** Portal polling no longer owns active document-editor lifetime — `6c375894`. Branch commit `2e559c0a` (`atomic/06-portal-editor-lifetime`) is not on master.
- [~] **07** Shared-document comments recover the exact original submission — `57094f9f`, `be41bae0`.
- [~] **08** PBC receipts bind a stable provider version to exact bytes — `6c375894`.

### Ordinary workflow connections (Tasks 09–18)
- [ ] **09** One current scoped recipient policy — [BLOCKED_DECISION]. Partial: `e81703ca` rejects expired finance routing contacts.
- [ ] **10** Invoice Send creates an exact durable dispatch — [BLOCKED_DECISION]. Not started.
- [ ] **11** FSLI coverage uses exact reviewed relationships — not started (depends on 02).
- [~] **12** Workflow templates instantiate fresh scoped PBC drafts — `6c375894`, `eaa69bf7`, `63eb1c8e`, `32bbfa91` (includes migration `WorkflowTemplatePbcDefinitions`).
- [ ] **13** Assignment policy and rework fallback accepted — [BLOCKED_DECISION].
- [~] **14** Partner-review phase begins at manager handover — `0b0f0ac1`.
- [ ] **15** SRM includes reviewed significant-estimate conclusions — [BLOCKED_DECISION]. Not started.
- [ ] **16** Management-letter narrative capacity accepted — [BLOCKED_EXTERNAL].
- [ ] **17** Correspondence JSON format and inquiry coverage accepted — [BLOCKED_EXTERNAL].
- [~] **18** Administrators discover older unresolved operations — `6c375894`, `0b0f0ac1`, `63109ac5`. Preservation of `3ec9c5d3` recovery-evidence visibility not checked.

### Firm financial integrity and reporting (Tasks 19–30)
- [ ] **19** Reporting profiles and independent golden fixtures approved — [BLOCKED_DECISION].
- [ ] **20** Enabled reporting outputs bind approved policy revisions — [BLOCKED_DECISION]. Partial commit `2873144a` touches owner files; not evaluated.
- [ ] **21** Firm billing-to-ledger accounting treatment approved — [BLOCKED_DECISION].
- [ ] **22** Billing events have a reconciled idempotent ledger handoff — [BLOCKED_DECISION]. Not started.
- [ ] **23** Reviewed nil cash-flow sections distinguish no activity from missing data — [BLOCKED_DECISION]. Owner files touched by several cash-flow commits; not evaluated.
- [ ] **24** First approved indirect cash-flow profile reconciles end to end — [BLOCKED_DECISION]. Depends on 20 and 23.
- [~] **25** Annual comparatives can use a separately authorized prior engagement — `151c716f`, `ad4e90f8`, `e206f1a6`. Spec dependency 20 is still open.
- [ ] **26** Group scopes have real reporting-period and compatibility evidence — [BLOCKED_DECISION]. Partial commit `2873144a`; not evaluated.
- [ ] **27** Approved component pins are immutable and replaced through supersession — not started (depends on 26).
- [ ] **28** Group currentness includes component generation and required reviews — not started (depends on 26, 27).
- [ ] **29** Intercompany eliminations consume exact source exposure once — not started (depends on 28).
- [ ] **30** Reviewed consolidation run produces a scoped group financial package — [BLOCKED_DECISION]. Not started.

### API contracts, release and hosting (Tasks 31–38)
- [ ] **31** Success contracts describe real API bodies — [BLOCKED_CONTEXT].
- [ ] **32** Live release uses an independently controlled checkpoint provider — [BLOCKED_EXTERNAL].
- [ ] **33** Secondary SharePoint archive copy is hash- and permission-verified — [BLOCKED_EXTERNAL].
- [ ] **34** External document history and checkout boundary approved — [BLOCKED_DECISION].
- [x] **35** Release promotion requires complete successful verification of its exact source — `0b0f0ac1` (`release.yml`, `release-verification.yml`, `test_verify_release_source.py`), `6c375894` (`verify-release-source.py`). Observed in this snapshot: 3 stdlib tests OK. No hosted CI result recorded for `0b0f0ac1`.
- [ ] **36** Selected deployment runs its new migration image — [BLOCKED_DECISION]. `e62594ad` pins deployments to image digests (partial).
- [ ] **37** Engagement activity reads bounded before materialization — [BLOCKED_CONTEXT].
- [ ] **38** Native close/opening and reporting-source handoff resolved — [BLOCKED_CONTEXT].

### Acceptance closure (Tasks 39–43)
- [ ] **39** Current source/action evidence governs parity and retirement — [BLOCKED_EXTERNAL].
- [ ] **40** Production identity, mail and document isolation accepted — [BLOCKED_EXTERNAL].
- [ ] **41** Independent recovery restores database, artifacts, checkpoints and keys — [BLOCKED_EXTERNAL].
- [ ] **42** Operations and capacity acceptance use existing diagnostics — [BLOCKED_EXTERNAL].
- [ ] **43** Named release profile has complete professional and operational closure — [BLOCKED_EXTERNAL].

---

## A. Requirement, authority and delivery contract

### A1. Requested result and source provenance

Implement the smallest correct changes needed to close the supplied report's actual gaps. Preserve implemented behavior and approved deviations. Do not use this request to recreate complete modules, invent policies, or convert an acceptance-only observation into an assumed schema/renderer requirement.

The requested source is **`auditsphere-gap-analysis-and-technical-stories-2026-10-16.md`**, SHA-256 `cc532e8ab1d5ab3d7c2bdf87e9bbff8353143c3de3974d015d35e9c629fa5696`. Its section 5 lists the 33 work items; sections 6–9 supply the defect/decision/acceptance evidence, sections 10–12 preserve sequencing and evidence limits, and its appendices preserve 91 grouped requirements and twelve acceptance criteria. Original report IDs are retained as aliases, not new repository issue IDs.

The current ref was resolved through the connected GitHub API. The [baseline-to-HEAD comparison](https://github.com/nirzaf/AuditSphere/compare/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318...3ec9c5d3d0cea098a190d4fdd8618120bd11aebd) contains one commit, [recovery-session evidence visibility](https://github.com/nirzaf/AuditSphere/commit/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd). It changes only:

- `src/AuditSphereOps.Ui/src/app/features/admin/operations.ts`;
- `src/AuditSphereOps.Ui/src/app/features/admin/operations.spec.ts`;
- `tests/AuditSphereOps.E2E.Tests/AngularOperationsCommandJourneyTests.cs`.

That change preserves approved recovery-session evidence while hiding unavailable approval controls. Task 18 must preserve it. It does not add older-operation discovery or settle the report's other findings.

**Evidence boundary:** this is a source-grounded expansion of the supplied report, with a fresh ref/diff and targeted authority/code reads—not a new exhaustive runtime audit of every report claim. Source links copied from the report deliberately retain its immutable baseline and exact line fragments. Except for the three files above, the comparison supports unchanged source inheritance. In the changed Operations files, line numbers must be rebound to current symbols. No defect reproduction, application test, build, provider acceptance or production observation is claimed.

The current `status.json` header still records `headCommit=e4686058fe7b059f195ed664c1e7ae7bf3f3be01`, `verifiedCommit=12e51e6031605245c22df8331d7ca12f345dbf36`, `verifiedAt=2026-10-15T13:28:06Z`, `productionEnabled=false`, and partial active implementation/acceptance. These are **ledger observations**, not verification of the resolved HEAD above. The report's hosted CI observations refer to its older baseline and observation time. No new current-HEAD CI result was established here; do not carry forward an in-progress run as a pass.

### A2. Authoritative sources and precedence

Start with the current root rules and document hierarchy, then read only the relevant capability authorities and implementation owners:

| Source | Use in this specification |
|---|---|
| [AGENTS.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/AGENTS.md) and [CLAUDE.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/CLAUDE.md) | Existing minimal-change, authorization, financial-integrity and fast-local/CI-only full-test rules. CLAUDE imports AGENTS. |
| [docs/auditsphere-docs-index.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/auditsphere-docs-index.md) | Authority hierarchy and source navigation. |
| [docs/architecture/auditsphere-architecture-current-architecture.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/architecture/auditsphere-architecture-current-architecture.md) | Modular monolith, static capability services, EF partials, durable work and retained rollback host. |
| [docs/architecture/auditsphere-architecture-code-map.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/architecture/auditsphere-architecture-code-map.md) | Existing capability/file/test ownership; implementation evidence takes precedence over stale descriptive rows. |
| [docs/requirements/auditsphere-accounting-module-requirements-current.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/requirements/auditsphere-accounting-module-requirements-current.md) | Current functional authority. Requirement numbers/line references in the report also retain its exact STE source copy. |
| [docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md) | Original supplied STE v2.2 wording and the preserved crosswalk. |
| [docs/architecture/adr/auditsphere-architecture-index-adr-register-current.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/architecture/adr/auditsphere-architecture-index-adr-register-current.md) | Approved deviations; only applicable CURRENT/APPROVED decisions bind implementation. |
| [docs/architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md) | Approved report bases and the professional/commercial decisions explicitly left open. |
| [docs/architecture/adr/auditsphere-architecture-adr-0009-archive-authority-release-store-approved.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/architecture/adr/auditsphere-architecture-adr-0009-archive-authority-release-store-approved.md) | Independent authoritative release store plus the separately verified read-only SharePoint secondary archive. |
| [docs/task_breakdown/modules/auditsphere-r2r-module-26-consolidation-contract.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/task_breakdown/modules/auditsphere-r2r-module-26-consolidation-contract.md) | Real group context, immutable component pins, source consumption, currentness and group output. Its logical command names are not proof of existing APIs. |
| [docs/execution/auditsphere-execution-index-deduplicated-backlog-current.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/execution/auditsphere-execution-index-deduplicated-backlog-current.md) and [docs/execution/auditsphere-execution-index-task-board-current.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/execution/auditsphere-execution-index-task-board-current.md) | Existing ownership/dependencies: T003/T014, T011–T040, T041–T047 and T048–T054; do not create a competing task ledger or mark cards complete from this document. |
| [docs/execution/status.json](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/execution/status.json) and [docs/execution/auditsphere-execution-current-slice.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/execution/auditsphere-execution-current-slice.md) | Read specific current fields/sections only. Historical evidence does not certify a later SHA. |
| [docs/testing/auditsphere-testing-strategy-definition-of-done-current.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/testing/auditsphere-testing-strategy-definition-of-done-current.md) | Targeted local verification, minimal effective tests, exact-SHA CI evidence and independent acceptance. |

Fresh source reads additionally checked `EngagementLifecycleService.ActivateAsync`, the existing current-procedure evidence predicate, the PBC request creation/send split, and the actual OpenAPI verification script/emitter project reference. Other detailed anchors are attributed to the supplied report and unchanged-source comparison. Root rule discovery found root `AGENTS.md`; an implementing agent must still read any applicable nested rules in its actual checkout before editing.

**Scope override for this delivery:** use a feature branch and PR. This explicitly replaces the repository's current direct-`master` default for this work. Do not change the repository's global Git policy incidentally.

**Already implemented policy:** the current `AGENTS.md` and testing strategy already require focused local tests and full suites through CI/CD. Do not add a redundant “rewrite AGENTS” task. Task 35 closes the still-missing automatic full-regression release gate.

### A3. Verified system boundaries

The source/report baseline is .NET 10/C# 14, ASP.NET Core, EF Core 10/Npgsql/PostgreSQL 18.6, Angular 22/Material-CDK and a .NET Worker. Respect the actual pins in `global.json`, `Directory.Packages.props`, `.config/dotnet-tools.json`, lockfiles and UI `.nvmrc`; this specification does not authorize upgrades. The report records Angular 22.2.1, Node 26.8.1 and SDK 10.0.300; “Angular 26.8” was a diagram error, not an upgrade requirement.

| Owner | Responsibility |
|---|---|
| `src/AuditSphereOps.Domain/` | Domain records and invariants; keep pure calculations deterministic in their established Domain/Application owners. |
| `src/AuditSphereOps.Application/` | Capability services, queries, current authorization, financial/professional workflows and durable-operation contracts. |
| `src/AuditSphereOps.Infrastructure/` | EF configuration/migrations and provider adapters; one capability-partial DbContext. |
| `src/AuditSphereOps.Api/` | HTTP/authentication/antiforgery/error/typed-contract boundary. |
| `src/AuditSphereOps.Ui/` | Angular presentation and exact-intent UI lifetimes; same-origin API only. |
| `src/AuditSphereOps.Worker/` | Durable effects, retries and reconciliation under approved capability identities. |
| `src/AuditSphereOps.Web/` | Retained rollback/reference host, not canonical UI and not authorized for removal by this work. |

Canonical staff/client routes in the report are `/app/` and `/portal/`; retain deployment-owned route helpers and actual route declarations. Do not resurrect earlier `/ui/app/` or `/ui/portal/` examples as requirements. `/api/ui/*` remains the API namespace.

Preserve independent **firm books**, **client accounting**, and **group consolidation**. Group calculations and journals never mutate component books. Native client bookkeeping requires its existing accepted service and approved cutover; existing external-source work remains usable. No new ERP, microservices, brokers, MediatR, generic repository/UnitOfWork, event sourcing, new malware scanner, rate-limit infrastructure, KMS or provider/package dependency is authorized.

### A4. Actors, scope and acceptance

The actors are current firm/client/engagement/group-scoped staff, FinanceManager/FinanceReviewer, responsible preparers/reviewers and the staffed Engagement Partner, authorized client users, firm-wide operations administrators, isolated workers, qualified methodology owners and independent release/operations reviewers. These are **existing domain roles and approval responsibilities**, not permission to invent or grant a new application role.

Every changed command, query, receipt lookup, provider enqueue and export must enforce current scope/session/holds at the Application/API boundary. UI `can*` flags never grant permission. Group-only summary access must not become raw component-client access. Client/account/source/engagement identifiers supplied together must be validated together.

The measurable program outcome is:

1. Each actual defect has its specific successful path and material denial/stale/replay/race regression protected at the layer that enforces it.
2. Missing connections produce real, current, exact outcomes—not status-only dispatch, refreshed-list “recovery,” balanced-but-unproved consolidation, or simulated live custody.
3. Approved records, issued bytes, source identity, receipts and professional decisions remain immutable; new revisions/corrections preserve lineage.
4. Every applicable supplied requirement has accepted current evidence or an explicit approved deviation/exclusion; no mandatory blocked item silently disappears.
5. Local development uses minimal focused checks; full regression evidence comes from the existing SHA-pinned CI/CD workflow and separate external acceptance.

**Non-goals:** duplicate sampling/rates/WIP/unallocated-payment engines; automatic historical repricing/revenue backfill; unsupported lease/tax/EOS methodologies; autonomous audit opinions; automatic final-fee-payment release gating; generic mailbox ingestion; mandatory PDF/DOCX correspondence conversion; arbitrary narrative-field expansion; Blazor retirement; forced Azure migration; production activation or live experiments.

### A5. Decision, context and external gates

| Gate | Applies to | Required resolution; permitted progress |
|---|---|---|
| `BLOCKED_DECISION` — purpose routing | Tasks 09–10 | ADR-0018 DEC-09 precedence/override policy. Reject obviously inactive/expired/ambiguous choices without inventing fallback policy; dispatch depends on the accepted resolver. |
| `BLOCKED_DECISION` — professional workflow/content | Tasks 13, 15; acceptance Tasks 16–17 | Approve pre-work ownership/pickup, significant-estimate methodology and realistic output expectations. Existing rework and JSON/narrative output stay implemented, not assumed missing. |
| `BLOCKED_DECISION` — reporting policies/fixtures | Tasks 19–20, 23–24, 26, 30 | Qualified T003 approval and independent expected fixture outcomes; preserve T014/T007/T013 card dependencies. No implementation-derived golden answers. |
| `BLOCKED_DECISION` — firm billing accounting | Tasks 21–22 | Recognition dates/basis, control accounts, advances/unapplied cash, tax/currency and legacy cutover before posting integration. |
| `BLOCKED_DECISION` — hosting and provider promises | Tasks 34, 36 | Choose the supported deployment route and external edit/checkout promise. Do not build two platforms or claim provider capabilities not proved. |
| `BLOCKED_CONTEXT` — residual JSON success contracts | Task 31 | Known priority owners are specified. Obtain the actual remaining endpoint/DTO/decoder census before assigning the residual no-guess implementation slice. Full large OpenAPI retrieval was unavailable here. |
| `BLOCKED_CONTEXT` — activity caller contract | Task 37 | Resolve exact API/UI callers and approved workload bounds before changing those unidentified owners. |
| `BLOCKED_CONTEXT` — native close/opening handoff | Task 38 | The report explicitly did not establish the route. Trace actual contracts and profile applicability before specifying any missing implementation. |
| `BLOCKED_EXTERNAL` — custody, archive, identity, recovery and acceptance | Tasks 08 live verification, 16–17, 32–33, 39–43 | Named owners, authorized environment, exact provider/runtime observations, qualified/independent approval and evidence. Local simulations do not close these gates. |

A dependent task may contain a complete technical recipe yet remain blocked for **execution or promotion** by an unmet prerequisite. The task state and gate are separate. Independently executable defect repairs do not wait for unrelated policy decisions. Conditional scope can be `NOT_APPLICABLE` only through a recorded approved release-profile decision, never an agent assumption.

### A6. Evidence and state vocabulary

- `VERIFIED_SOURCE`: source text actually retrieved or the report's explicitly attributed source evidence; identify **direct current read** versus **report-at-baseline plus unchanged diff**. It is never a runtime PASS.
- `INFERENCE`: consequence or proposed extension reasoned from those sources; not observed behavior.
- `RECOMMENDATION` / `NEW_REQUIREMENT`: this specification's proposed implementation, data/contract shape or owner decision. A proposed identifier is not an existing API.
- Capability states: `IMPLEMENTED`, `PARTIAL`, `MISSING`, `UNVERIFIED`, `NOT_APPLICABLE`.
- Handoff states: `SPECIFIED`, `UNVERIFIED`, `BLOCKED_CONTEXT`, `BLOCKED_DECISION`, `BLOCKED_EXTERNAL`. `SPECIFIED` does not mean executed, accepted or complete.

All proposed file paths/symbols/DTOs/error additions must be checked for collision and fit against the current checkout. Reuse the real existing owner when it already satisfies the required responsibility; record a justified path rebind rather than creating a duplicate. An unresolved material contract is a bounded context blocker, not permission for speculative scaffolding.

---

## B. Gap matrix and dependency-ordered task index

### B1. Gap matrix

**Evidence key:** `R` = the supplied report's attributed source finding and cited section; `D` = checked baseline-to-HEAD diff; `C` = direct current source/authority reading in this session. None is application execution evidence. Tasks specify only the missing behavior or acceptance; the current implementation is retained.

| Requirement / priority | Expected outcome | Existing behavior and classification | Evidence / owner | State | Remaining gap | Tasks |
|---|---|---|---|---|---|---|
| AUD-WF-01 · P1 | Recheck commercial consent at activation | ActivateAsync checks current risk, holds and the paid allocated advance, but not the current exact commercial consent; activation preview omits that basis. Quotation revocation uses a firm lock while activation begins with a client lock. Confirmed source defect. | R lines 199–220; D; C (selected anchor/authority); Acceptance / CRM | PARTIAL | SPECIFIED | [01](#task-01) |
| AUD-WF-02 · P1 | Require current procedure evidence for completion | Completion credits raw Reviewed state even for an old risk-unlinked result after input-generation changes and planning is reapproved. The stricter current-procedure evidence predicate already exists. Confirmed source defect. | R lines 220–242; D; C (selected anchor/authority); Fieldwork / Completion | PARTIAL | SPECIFIED | [02](#task-02) |
| AUD-WF-03 · P2 | Make invoice dispatch an evidenced operation | SendInvoiceAsync only transitions Posted → Sent and stamps SentAt; it does not render, enqueue or verify an invoice dispatch. Missing workflow connection. | R lines 242–256; D; Billing / Documents | PARTIAL | BLOCKED_DECISION | [10](#task-10) |
| AUD-WF-04 · P1 | Resolve current finance/commercial recipients consistently | FeeAgreementService chooses the first FINANCE route, can select an inactive fallback and omits contact validity dates; it disagrees with the purpose resolver's explicit ambiguity handling. Confirmed defect + precedence decision. | R lines 256–274; D; CRM / Billing | PARTIAL | BLOCKED_DECISION | [09](#task-09) |
| AUD-WF-05 · P2 | Use exact FSLI procedure relationships and current coverage | FinancialStatementDrillDownQuery and StatementReviewWorkspace use normalized name/substrings and raw Reviewed state to attach procedures to financial-statement lines. Confirmed source defect. | R lines 274–290; D; Statements / Fieldwork | PARTIAL | SPECIFIED | [11](#task-11) |
| AUD-WF-06 · P2 | Instantiate template PBC references as scoped draft requests | WorkflowTemplateStep stores PbcRequestReference, but plan preview/execution omits it. PbcService.CreateRequestAsync exists and requires an Active engagement; sending is a separate command. Missing workflow connection. | R lines 290–304; D; C (selected anchor/authority); Workflow / PBC | PARTIAL | SPECIFIED | [12](#task-12) |
| AUD-WF-07 · P2 | Accept assignment/pickup policy and enabled rework queues | Pre-work procedure ownership is undecided. Current submission records the actual preparer; enabled WorkflowExperience already creates one linked rework task and falls back to unassigned manager routing when the preparer is ineligible. Decision / acceptance; rework already exists. | R lines 304–318; D; Practice / Fieldwork | IMPLEMENTED foundation; acceptance/policy UNVERIFIED | BLOCKED_DECISION | [13](#task-13) |
| AUD-WF-08 · P2 | Show the correct Manager-to-Partner handover | EngagementLifecycleQuery requires current Partner clearance before projecting Partner Approval, leaving initial Partner review under manager responsibility. Projection mismatch; process interpretation. | R lines 318–332; D; Lifecycle / UI | PARTIAL | SPECIFIED | [14](#task-14) |
| AUD-WF-09 · Acceptance | Accept management-letter authoring and narrative capacity | FindingType is free text capped at 100 database characters and rendered as Matter with impact/recommendation/response. A missing mandatory narrative field has not been demonstrated. Conditional presentation work. | R lines 332–344; D; Professional template owner | IMPLEMENTED foundation; acceptance/policy UNVERIFIED | BLOCKED_EXTERNAL | [16](#task-16) |
| AUD-WF-10 · P2 | Include significant-estimate conclusions in SRM | SRM renders risk/procedure/difference facts but no explicit significant-estimate assessment/conclusion; generic procedure text and a critical-risk flag are not that summary. Partial content + methodology decision. | R lines 344–356; D; Completion | PARTIAL | BLOCKED_DECISION | [15](#task-15) |
| AUD-WF-11 · Acceptance | Accept existing correspondence trail format and coverage | The bundle already contains Audit_trail.json with Confirmations, Communications and ClearedQueries, bounded reads and a pinned source digest. PDF/DOCX is not required by the cited source. Conditional presentation work. | R lines 356–368; D; Professional template owner | IMPLEMENTED foundation; acceptance/policy UNVERIFIED | BLOCKED_EXTERNAL | [17](#task-17) |
| AUD-FIN-01 · P1 | Bind invoice time sources to the permitted client | BillingService validates TIME by firm/id/revision and amount but omits the target client's source relationship; non-TIME kinds bypass typed validation. A later unbilled-time query detects the mismatch only after the write. Application source-scope defect. | R lines 368–384; D; Firm Billing | PARTIAL | SPECIFIED | [03](#task-03) |
| AUD-FIN-02 · P1 | Persist exact finance command identities and receipts | Generic record-receipt, allocation and credit requests lack a durable command identity. The existing unique receipt/invoice allocation pair prevents same-pair double allocation, but does not recover the original success. Confirmed replay/recovery gap. | R lines 384–404; D; Firm Billing / API | PARTIAL | SPECIFIED | [04](#task-04) |
| AUD-FIN-03 · P1 before integrated-books claim | Approve billing-to-GL treatment and reconciliation | Billing posting/receipts/credits are separate from firm journals. ADR-0018 defines reporting bases but does not approve revenue recognition, advance liabilities, taxes, control accounts or cutover. Disconnected path + accounting decision. | R lines 404–422; D; Firm Ledger / Billing | PARTIAL | BLOCKED_DECISION | [21](#task-21), [22](#task-22) |
| AUD-FIN-04 · P1 | Bind enabled reporting to approved policy revisions | AccountingCapabilityProfile exists, but qualified framework/edition/effectivity/method and independent GOLD-R2R acceptance remain open under T003. Partial code + professional blocker. | R lines 422–438; D; C (selected anchor/authority); Accounting policy / M20–M25 | PARTIAL | BLOCKED_DECISION | [19](#task-19), [20](#task-20) |
| AUD-FIN-05 · P1 for complete entity package | Complete indirect cash flow and evidenced nil activity | Required-section and lineage gates reject an ordinary operating-only period: omitted investing/financing fails, while artificial zero rows have no legitimate nonzero source contribution. Partial method + confirmed gate defect. | R lines 438–458; D; M24 | PARTIAL | BLOCKED_DECISION | [23](#task-23), [24](#task-24) |
| AUD-FIN-06 · P2 | Allow authorized annual comparatives across engagements | Build-input options and Package.cs restrict comparatives to the same engagement, while Presentation.cs already allows a separately authorized prior engagement for the same entity. Confirmed ordinary-route restriction. | R lines 458–474; D; M24 | PARTIAL | SPECIFIED | [25](#task-25) |
| AUD-FIN-07 · P1 | Enforce group period and component compatibility | Consolidation scope stores an arbitrary nonempty PeriodId and caller PeriodBasis text; component actual dates/book/basis/framework are not fully reconciled with a real group context. Confirmed missing input validation. | R lines 474–492; D; C (selected anchor/authority); M26 | PARTIAL | BLOCKED_DECISION | [26](#task-26) |
| AUD-FIN-08 · P1 | Recheck component currentness throughout group use | Group runs compare immutable package hash/status/currency after a one-time review check; a new accepted source or changed required review can leave an old group result labeled CURRENT_APPROVED. Confirmed eligibility gap. | R lines 492–510; D; M25 / M26 | PARTIAL | SPECIFIED | [28](#task-28) |
| AUD-FIN-09 · P1 | Consume exact IC source exposure once | IC matches accept caller seller/buyer/matched amounts with no source-line identities, so a fresh reference can reconsume the same economic exposure while still producing a balanced elimination. Confirmed lineage/consumption gap. | R lines 510–526; D; M26 | PARTIAL | SPECIFIED | [29](#task-29) |
| AUD-FIN-10 · P1 | Supersede approved component pins without deleting history | Replacing a component in a draft scope deletes the old row even if that component was already independently approved; approved source fields lack complete persisted protection. Confirmed history protection gap. | R lines 526–544; D; M26 / Persistence | PARTIAL | SPECIFIED | [27](#task-27) |
| AUD-FIN-11 · P2 after prerequisites | Build a reviewed group financial package | Consolidation reports expose reviewed lines, but the entity package builder requires a client/engagement/adjustment-plan context and has no typed group assembly route. Missing end-to-end route. | R lines 544–555; D; M26 → M25 / T047 | PARTIAL | BLOCKED_DECISION | [30](#task-30) |
| AUD-UI-01 · P1 | Bind billing drafts, assent and callbacks to an invoice visit | InvoiceDetail retains seven form groups and reviewed booleans across invoice changes; asynchronous commands and reconciliation are fenced only by session invalidation. Confirmed source defect. | R lines 622–667; D; Angular Finance | PARTIAL | SPECIFIED | [05](#task-05) |
| AUD-UI-02 · P1 | Preserve portal child editors during summary polling | ClientPortalHome wraps child editors in ws.data(); the 30-second summary reload clears that data, destroys children and can discard file selection, drafts and an in-flight command context. Confirmed source defect. | R lines 667–702; D; Angular Portal | PARTIAL | SPECIFIED | [06](#task-06) |
| AUD-UI-03 · P2 | Recover the exact original shared-document comment | CommentAsync creates a fresh GUID for each body-only request; a generic documents read clears uncertainty, without identifying the original request. Confirmed recovery gap. | R lines 702–730; D; Portal / Completion | PARTIAL | SPECIFIED | [07](#task-07) |
| AUD-UI-04 · P2 | Find older unresolved operations | OperationRecoveryService.ListAsync selects the latest 50 firm operations; Angular pages only those rows, so older uncertain/blocked/dead-letter work can disappear from discovery. Missing bounded discovery. | R lines 730–744; D; C (selected anchor/authority); Operations / UI | PARTIAL | SPECIFIED | [18](#task-18) |
| AUD-UI-05 · P1 on changed high-risk APIs | Declare success DTOs and verify client compatibility | The emitted OpenAPI has many description-only 200 responses; concrete portal documents, operations and portal accounting package responses lack schemas while clients maintain their own shapes. Partial contract coverage. | R lines 744–766; D; API / Angular | PARTIAL | BLOCKED_CONTEXT | [31](#task-31) |
| AUD-UI-06 · Release gate | Close source/action and retirement acceptance evidence | Migration readiness remains NOT_READY; one Operations parsed-action inventory row is stale and other source/action/supporting-file rows include unreviewed or unexecuted behavior. Acceptance incomplete; one stale inventory row. | R lines 766–786; D; Migration / QA / Operations | PARTIAL / acceptance UNVERIFIED | BLOCKED_EXTERNAL | [39](#task-39) |
| AUD-PLAT-01 · P1 | Bind PBC readback to stable provider version bytes | GraphPbcProviderSink observes a version, then may download unfenced current content and combine it with the prior version ID. Staged SHA/size checks do not by themselves establish that version-to-byte association. Source-confirmed race; live reproduction not run. | R lines 791–807; D; Graph / PBC | PARTIAL | SPECIFIED | [08](#task-08) |
| AUD-PLAT-02 · P1 before live release | Implement and compose independent live checkpoints | GraphReleaseCheckpointStore is a live-provider-not-approved stub; API rejects general live external effects and API/Worker compose a local checkpoint store. Stub/composition missing + external gate. | R lines 807–821; D; Records / API / Worker | MISSING live provider; local implementation retained | BLOCKED_EXTERNAL | [32](#task-32) |
| AUD-PLAT-03 · P1 for archive promise | Complete verified SharePoint archive copies | Local freeze records BLOCKED_EXTERNAL and no current path assigns observed external read-only state; the selected separate read-only archive library is approved but not implemented. Missing provider path + external evidence. | R lines 821–835; D; C (selected anchor/authority); Records / Client-sites | PARTIAL | BLOCKED_EXTERNAL; BLOCKED_DECISION | [33](#task-33), [34](#task-34) |
| AUD-PLAT-04 · P1 for selected route | Require full verified release source and exact migration execution | release.yml gates only on ci.yml; full release-verification already runs nightly/manual on one resolved SHA, but is not automatically enforced by the release path. Release gate + conditional Azure defect. | R lines 835–849; D; C (selected anchor/authority); CI / Deployment | PARTIAL | SPECIFIED; BLOCKED_DECISION | [35](#task-35), [36](#task-36) |
| AUD-PLAT-05 · Production gate | Accept production identity, recovery, capacity and operations | EngagementActivityQuery loads multiple full engagement histories and sorts them in memory, leaving a documented production-volume risk. Implementation/evidence/owner acceptance. | R lines 849–866; D; C (selected anchor/authority); Operations / Professional owners | PARTIAL / acceptance UNVERIFIED | BLOCKED_CONTEXT; BLOCKED_EXTERNAL | [37](#task-37), [38](#task-38), [40](#task-40), [41](#task-41), [42](#task-42), [43](#task-43) |

### B2. Task index

Dependencies below are technical/acceptance dependencies inside this specification. Existing T-card dependencies and independent review requirements remain binding; these rows do not change their lifecycle status. Tasks with independent prerequisites can proceed independently—do not serialize the entire program behind a single professional decision.

| Task | Concrete outcome | Dependencies | Principal owner files | Acceptance | Gate |
|---|---|---|---|---|---|
| [01](#task-01) | Activation requires the current commercial key at commit | None | `src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs`<br>`src/AuditSphereOps.Application/Acceptance/EngagementActivationWorkspace.cs` | AC-01-01–03 | SPECIFIED |
| [02](#task-02) | All completion consumers require current independent procedure evidence | None | `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Completion.cs`<br>`src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs` | AC-02-01–02 | SPECIFIED |
| [03](#task-03) | Invoice preparation binds every billed source to the permitted client | None | `src/AuditSphereOps.Application/Practice/BillingService.cs`<br>`tests/AuditSphereOps.Domain.Tests/BillingCommandScopeIsolationTests.cs` | AC-03-01–02 | SPECIFIED |
| [04](#task-04) | Finance mutations retain and recover one exact original outcome | 03 | `src/AuditSphereOps.Application/Practice/BillingService.cs`<br>`src/AuditSphereOps.Application/Practice/FeeAgreementService.cs` | AC-04-01–03 | SPECIFIED |
| [05](#task-05) | Invoice drafts and assent belong to one exact route visit | 04 | `src/AuditSphereOps.Ui/src/app/features/finance/invoice.ts`<br>`src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts` | AC-05-01–03 | SPECIFIED |
| [06](#task-06) | Portal polling no longer owns active document-editor lifetime | None | `src/AuditSphereOps.Ui/src/app/features/portal/home.ts`<br>`src/AuditSphereOps.Ui/src/app/features/portal/documents.ts` | AC-06-01–02 | SPECIFIED |
| [07](#task-07) | Shared-document comments recover the exact original submission | 06 | `src/AuditSphereOps.Application/Documents/ClientPortalReviewCommands.cs`<br>`src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs` | AC-07-01–03 | SPECIFIED |
| [08](#task-08) | PBC receipts bind a stable provider version to exact bytes | None | `src/AuditSphereOps.Infrastructure/Providers/GraphPbcProviderSink.cs`<br>`src/AuditSphereOps.Infrastructure/Providers/GraphSelectedSiteDrive.cs` | AC-08-01–02 | SPECIFIED |
| [09](#task-09) | All finance and commercial recipients use one current scoped resolution policy | None | `src/AuditSphereOps.Application/Practice/FeeAgreementService.cs`<br>`src/AuditSphereOps.Application/Practice/FeeAgreementService.PaymentVouchers.cs` | AC-09-01–02 | BLOCKED_DECISION |
| [10](#task-10) | Invoice Send creates an exact durable dispatch rather than a status-only success | 04, 09 | `src/AuditSphereOps.Application/Practice/BillingService.cs`<br>`src/AuditSphereOps.Application/Practice/CommercialMailDeliveryHandler.cs` | AC-10-01–03 | BLOCKED_DECISION |
| [11](#task-11) | FSLI workprogram coverage uses exact reviewed relationships | 02 | `src/AuditSphereOps.Application/Accounting/Intake/FinancialStatementDrillDownQuery.cs`<br>`src/AuditSphereOps.Application/Accounting/Intake/StatementReviewWorkspace.cs` | AC-11-01–02 | SPECIFIED |
| [12](#task-12) | Workflow templates instantiate fresh scoped PBC drafts | None | `src/AuditSphereOps.Domain/Practice/WorkflowTemplates.cs`<br>`src/AuditSphereOps.Application/Practice/WorkflowTemplateService.cs` | AC-12-01–02 | SPECIFIED |
| [13](#task-13) | The approved assignment policy and existing rework fallback are accepted | None | `docs/architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md`<br>`src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts` | AC-13-01–02 | BLOCKED_DECISION |
| [14](#task-14) | Partner-review phase begins at manager handover, before Partner clearance | 02 | `src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs`<br>`tests/AuditSphereOps.Domain.Tests/EngagementLifecycleQueryTests.cs` | AC-14-01–02 | SPECIFIED |
| [15](#task-15) | SRM includes reviewed significant-estimate conclusions | 02, 11 | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs`<br>`src/AuditSphereOps.Domain/Completion/AuditDeliverables.cs` | AC-15-01–02 | BLOCKED_DECISION |
| [16](#task-16) | Professionally accept the existing management-letter narrative capacity | None | `src/AuditSphereOps.Ui/src/app/features/audit/plan.ts`<br>`src/AuditSphereOps.Ui/src/app/features/audit/records.ts` | AC-16-01–02 | BLOCKED_EXTERNAL |
| [17](#task-17) | Accept correspondence JSON format and supported inquiry coverage | None | `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.Assembly.cs` | AC-17-01–02 | BLOCKED_EXTERNAL |
| [18](#task-18) | Administrators can discover old unresolved operations | None | `src/AuditSphereOps.Application/Operations/OperationRecoveryService.cs`<br>`src/AuditSphereOps.Api/Ui/UiEndpoints.Operations.cs` | AC-18-01–03 | SPECIFIED |
| [19](#task-19) | Qualified owners approve the first reporting profiles and independent golden fixtures | None | `docs/requirements/auditsphere-reporting-profile-acceptance-proposed.md` | AC-19-01–02 | BLOCKED_DECISION |
| [20](#task-20) | Every enabled reporting output binds approved effective policy revisions | 19 | `src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs`<br>`src/AuditSphereOps.Domain/Accounting/ReportingPolicyRevision.cs` | AC-20-01–02 | BLOCKED_DECISION |
| [21](#task-21) | Approve the firm's billing-to-ledger accounting treatment | 04 | `docs/requirements/auditsphere-firm-billing-ledger-treatment-proposed.md` | AC-21-01–01 | BLOCKED_DECISION |
| [22](#task-22) | Billing events have a reconciled idempotent firm-ledger handoff | 21 | `src/AuditSphereOps.Application/Practice/BillingService.cs`<br>`src/AuditSphereOps.Application/Practice/LedgerService.cs` | AC-22-01–02 | BLOCKED_DECISION |
| [23](#task-23) | Reviewed nil cash-flow sections distinguish no activity from missing data | 20 | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementServiceContracts.cs`<br>`src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSources.cs` | AC-23-01–02 | BLOCKED_DECISION |
| [24](#task-24) | The first approved indirect cash-flow profile reconciles end to end | 20, 23 | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementServiceContracts.cs`<br>`src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSources.cs` | AC-24-01–03 | BLOCKED_DECISION |
| [25](#task-25) | Annual comparatives can use a separately authorized prior engagement | 20 | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialPackageBuildInputsQuery.cs`<br>`src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Package.cs` | AC-25-01–02 | SPECIFIED |
| [26](#task-26) | Group scopes have real reporting-period and compatibility evidence | 20 | `src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs`<br>`src/AuditSphereOps.Domain/Accounting/GroupReportingPeriod.cs` | AC-26-01–02 | BLOCKED_DECISION |
| [27](#task-27) | Approved component pins are immutable and replaced through supersession | 26 | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Components.cs`<br>`src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs` | AC-27-01–02 | SPECIFIED |
| [28](#task-28) | Group currentness includes component generation and required professional reviews | 26, 27 | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Runs.cs`<br>`src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Reports.cs` | AC-28-01–03 | SPECIFIED |
| [29](#task-29) | Intercompany eliminations consume exact source exposure once | 28 | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationServiceContracts.cs`<br>`src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Intercompany.cs` | AC-29-01–02 | SPECIFIED |
| [30](#task-30) | An exact reviewed consolidation run produces a scoped group financial package | 24, 25, 26, 27, 28, 29 | `src/AuditSphereOps.Application/Accounting/Consolidation/GroupFinancialPackageWorkspace.cs`<br>`src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Reports.cs` | AC-30-01–02 | BLOCKED_DECISION |
| [31](#task-31) | Success contracts describe real API bodies and fail on incompatible decoder changes | 04, 07, 18 | `src/AuditSphereOps.Api/Contracts/ApiContract.cs`<br>`src/AuditSphereOps.Api/Ui/UiEndpoints.Finance.cs` | AC-31-01–03 | BLOCKED_CONTEXT |
| [32](#task-32) | Live release uses an independently controlled checkpoint provider | None | `src/AuditSphereOps.Infrastructure/Providers/GraphReleaseCheckpointStore.cs`<br>`src/AuditSphereOps.Api/ApiHost.Providers.cs` | AC-32-01–02 | BLOCKED_EXTERNAL |
| [33](#task-33) | The secondary SharePoint archive copy is hash- and permission-verified | 08, 32 | `src/AuditSphereOps.Application/Records/FileFreezeService.cs`<br>`src/AuditSphereOps.Application/Documents/ClientSharePointSites.cs` | AC-33-01–03 | BLOCKED_EXTERNAL |
| [34](#task-34) | External document history and checkout promises have an approved supported boundary | None | `docs/requirements/auditsphere-external-document-observation-scope-proposed.md` | AC-34-01–02 | BLOCKED_DECISION |
| [35](#task-35) | Release promotion requires complete successful verification of its exact source | None | `.github/workflows/release.yml`<br>`.github/workflows/release-verification.yml` | AC-35-01–02 | SPECIFIED |
| [36](#task-36) | The selected deployment runs its new migration image and exact execution | 35 | `.github/workflows/release.yml`<br>`infra/modules/migration-job.bicep` | AC-36-01–02 | BLOCKED_DECISION |
| [37](#task-37) | Engagement activity reads are bounded before materialization | None | `src/AuditSphereOps.Application/Records/EngagementActivityQuery.cs`<br>`tests/AuditSphereOps.Domain.Tests/EngagementActivityPagingTests.cs` | AC-37-01–02 | BLOCKED_CONTEXT |
| [38](#task-38) | Resolve native close/opening and accepted reporting-source handoff | None | `docs/execution/auditsphere-execution-current-slice.md` | AC-38-01–02 | BLOCKED_CONTEXT |
| [39](#task-39) | Current source/action evidence—not route counts—governs parity and retirement | 01, 02, 05, 06, 07, 11, 14, 18 | `docs/migration/auditsphere-migration-blazor-retirement-inventory.md`<br>`docs/migration/auditsphere-migration-blazor-feature-parity.md` | AC-39-01–03 | BLOCKED_EXTERNAL |
| [40](#task-40) | Accept current production identity, mail and document isolation | 08, 09, 10, 32, 33 | `docs/execution/status.json` | AC-40-01–02 | BLOCKED_EXTERNAL |
| [41](#task-41) | Independent recovery restores database, artifacts, checkpoints and keys | 32, 33, 35, 36 | `docs/execution/status.json` | AC-41-01–02 | BLOCKED_EXTERNAL |
| [42](#task-42) | Operations and capacity acceptance use existing diagnostics and bounded workloads | 18, 37 | `docs/execution/status.json` | AC-42-01–02 | BLOCKED_EXTERNAL |
| [43](#task-43) | The named release profile has complete professional and operational closure | 01, 02, 03, 04, 05, 06, 07, 08, 09, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42 | `docs/execution/status.json`<br>`docs/execution/auditsphere-execution-current-slice.md` | AC-43-01–02 | BLOCKED_EXTERNAL |

---

## C. Shared implementation and verification contract

The following applies to every task without requiring repeated boilerplate or duplicate tests.

### C1. Preflight, ownership and integrity

Before changing source, inspect the requested task's actual checkout, base SHA and working changes; read root/nested rules, the current architecture/code map, the exact owner and adjacent test. Preserve unrelated changes. Rebind changed line numbers by symbol; use the existing task board and query only relevant execution fields. Do not run application commands in this SPEC_ONLY session.

Prefer a cohesive vertical change through existing services/partials, API and Angular where inseparable. Thin endpoints must compose the Application guard, not reimplement it. Use existing cancellation, `CommandResult` errors, validation bounds, review basis, transaction ownership, lock ordering and durable operation patterns. Audit all callers that could bypass a newly introduced invariant, including retained Web or worker callers when they exist.

For protected financial/review writes: server-derived firm/actor; exact scope of every target/source; current grants/session/holds; expected revision/generation and exact reviewed intent; deterministic ordered locking; reauthorization after waiting; atomic state/receipt/audit publication; same-key recovery; immutable prior history. Denial or rollback must not leave reservations, invoice rows, accepted review state or provider-success claims. No transaction should remain open during rendering, human review or external I/O.

A receipt lookup is currently authorized historical evidence. It may return an original successful result without pretending the original source remains currently approvable. A missing receipt while the original might still commit is not proof of failure. Retry only the original bounded identity/intent until resolved; do not invent a new idempotency key.

### C2. Additive migrations, contracts and rollback

Only tasks that actually change persistence generate migrations. Never edit a committed migration. Use the existing migration directory and context/interface/configuration owners; exact EF timestamped filenames are future generated output, not fabricated existing paths. Each task names a **PROPOSED logical migration** when necessary. The PR must list the actual generated `.cs`, designer and model-snapshot paths after generation.

Use scoped FKs, uniqueness and database guards where the invariant requires them. Preserve nullable/legacy evidence meaning. Missing old consent, policy, request identity, source allocation or provider proof cannot be backfilled from today's latest record. Corrections use reviewed amendments, supersession or corrective journals—not deletion or rewritten source attribution.

Deploy additive schema before code that requires it. Explicitly assess old/new API/worker compatibility, draining or guarding incompatible workers and the selected migration entry point. For data-bearing changes, “rollback” normally means schema-compatible prior binaries or a forward correction. Do not promise destructive `Down()` execution can safely undo newly recorded financial/evidence data. Existing sealed/issued bytes remain readable under current authorization.

New/changed HTTP shapes need named runtime-consistent success metadata, exact string money/64-bit revisions where established, current error/nullability semantics and matching Angular decoders. The existing JSON OpenAPI emitter is not a TypeScript generator. Regenerate the one canonical contract only when impacted.

### C3. Minimal effective test policy

**Entire test suites run only through CI/CD. Do not run unfiltered solution/project, all Angular or all E2E suites locally.** One coherent edit should receive one smallest meaningful proving cohort; after a fix rerun only failed/affected cases. Reuse sufficient current evidence rather than accumulating counts.

Prefer extending the nearest behavior fixture. Equivalent cases should be parameterized. The Given/When/Then rows below are acceptance dimensions, not an automatic separate test per bullet, method, endpoint or role. Zero new tests is appropriate where existing assertions genuinely protect unchanged behavior or an acceptance-only decision. Do not delete/skip/weaken a unique invariant merely to reduce test count.

Place proof where the fault is observable: pure arithmetic in existing calculator tests; scope/locks/receipt/immutability in PostgreSQL-backed Application tests; cookie/antiforgery/HTTP/serialization at API; input/assent/lifetime/decoding in Angular; a representative existing browser journey only for wiring the cheaper layers cannot prove. Do not duplicate the same guard at every layer. Financial expected values must come from an independently accepted rule/fixture, never the same implementation generating its own expected answer.

### C4. Future command cookbook — NOT RUN — SPEC_ONLY

All commands in this document are **NOT RUN — SPEC_ONLY**. No source test/build/generator/service/provider command was executed to produce this specification. Commands are intended for a later authorized implementation checkout, from repository root unless stated.

**Prerequisites, once only when needed:** pinned .NET SDK/tools, approved Node, restored locked dependencies and isolated PostgreSQL for DB-backed tests. Do not reinstall/re-restore for each edit. Use `auditsphere_tests` on the approved local test endpoint (normally port 5433), never development/production databases. Missing environment is `BLOCKED_TEST_ENV` in the implementation report, not PASS. Local verification must keep external effects disabled and use the existing Test/simulation composition; do not point an emitter, API fixture or worker at production settings or live credentials.

Known bounded capability runner entry points, to be used only when its actual catalog covers the change:

```bash
# NOT RUN — SPEC_ONLY
./scripts/agent-check --list
./scripts/agent-check review-permissions
# Execution is a separate future opt-in for the exact mapped cohort:
./scripts/agent-check review-permissions --run
```

The runner's prerequisite read is not a passed test. A generic pilot cannot prove an unrelated command's acceptance. Do not run a pilot as well as the same direct cohort merely to duplicate evidence.

**Targeted .NET:** the test project path and `--filter` mechanism are existing. Task-specific test prefixes below are explicitly **PROPOSED** selection anchors for the required regression assertions, not claims those methods already exist. Reuse an adequate existing method under its actual name instead; record the exact effective filter. Do not rename/create a duplicate test solely to satisfy an illustrative selector. A source read or discovery can bind the selector; only a nonzero executed, asserted result proves it.

```bash
# NOT RUN — SPEC_ONLY: example of a bounded existing cohort, not every task's default.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore --configuration Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~BillingCommandScopeIsolationTests'
```

`dotnet test` builds the selected project by default. Do not also run an unnecessary full solution build. Use `--no-build` only when the exact edited source/tests and required dependencies were already built in the same configuration. Never reuse stale binaries.

**Targeted Angular:** `test:ci` is the existing non-watch script. `--include` must select the exact existing/project-relative feature spec; an unfiltered invocation is CI-only.

```bash
# NOT RUN — SPEC_ONLY
npm --prefix src/AuditSphereOps.Ui run test:ci -- \
  --include 'src/app/features/finance/invoice-read.spec.ts'
```

For browser-visible behavior, later authorized manual/dynamic browser verification may complement the focused spec, using the exact synthetic identity, route and state. Missing authenticated host access must be reported; a shell screenshot is not workflow evidence. Full automated browser regression stays in CI.

**Affected contracts only:** `verify-openapi.sh` uses a Release `--no-build` emitter. The emitter's project references the API, so build that project and its references rather than an unrelated full solution just for contract verification. Restore prerequisites must already be satisfied.

```bash
# NOT RUN — SPEC_ONLY; only after a relevant contract change.
dotnet build tools/AuditSphereOps.OpenApiEmitter/AuditSphereOps.OpenApiEmitter.csproj \
  --no-restore --configuration Release --maxcpucount:1
# Regenerate only when the intentional shape changed:
dotnet run --project tools/AuditSphereOps.OpenApiEmitter --no-build \
  --configuration Release -- contracts/auditsphere-openapi.json
bash scripts/contracts/verify-openapi.sh
```

**Affected EF model only:** after the edited API/Infrastructure Release graph is current:

```bash
# NOT RUN — SPEC_ONLY
dotnet build src/AuditSphereOps.Api/AuditSphereOps.Api.csproj \
  --no-restore --configuration Release --maxcpucount:1
dotnet ef migrations has-pending-model-changes \
  --project src/AuditSphereOps.Infrastructure \
  --startup-project src/AuditSphereOps.Api --no-build --configuration Release
```

Reuse a just-completed suitable Release build; do not execute duplicate builds from the two snippets. Schema/trigger behavior needs the task's disposable PostgreSQL case, not just model-drift success. Application migrations are never applied to a real environment by this document.

For docs-only changes, run only relevant documentation validation and `git diff --check`. Commands below are existing; use only the validator whose contract was affected:

```bash
# NOT RUN — SPEC_ONLY
git diff --check
python3 scripts/docs/validate-markdown-documentation.py
python3 scripts/docs/validate-markdown-filenames.py
python3 scripts/docs/validate-narrative-metrics.py
```

**PASS criteria:** exact selected source and effective command, nonzero expected tests executed with assertions, zero relevant failures, no silently skipped required case, no source change during execution, and truthful omitted checks. Record counts/durations only actually observed. New tests compiled but not executed are not PASS. Manual/CI/provider evidence must remain separately labeled.

### C5. CI, release and external evidence

- `.github/workflows/ci.yml` remains the fast push/PR check: Angular build/specs, Release compilation, database-free guards, EF/OpenAPI, publication and documentation. It is not the full .NET/PostgreSQL/browser regression.
- `.github/workflows/release-verification.yml` remains full nightly/manual verification of one resolved immutable SHA, with complete unfiltered suites, isolated PostgreSQL and aggregate release gate. **Only this CI tier owns full regression.**
- Current `release.yml` enforces fast CI but not the full workflow; Task 35 adds that missing release precondition. Do not describe it as already active or incidentally make full CI mandatory on every small PR.
- CI is not production permission. Real consent, site/sender boundaries, provider version fencing, independent custody, backup/restore, professional outputs, accessibility and release acceptance remain separate.

A release decision needs the resolved tested source, run/attempt and successful required jobs, not only a branch name or potentially different `workflow_dispatch` metadata SHA. A valid prior result can be reused for the same unchanged candidate; a different commit is not automatically equivalent. Preserve the repository's exact-SHA requirement rather than silently relaxing it.

### C6. External technical references, not new product authority

The provider and runner details were checked against primary documentation: [Microsoft Graph historical version content](https://learn.microsoft.com/en-us/graph/api/driveitemversion-get-contents?view=graph-rest-1.0), [current item content](https://learn.microsoft.com/en-us/graph/api/driveitem-get-content?view=graph-rest-1.0), [Angular CLI test](https://angular.dev/cli/test), and [.NET selective tests](https://learn.microsoft.com/en-us/dotnet/core/testing/selective-unit-tests). These support technical constraints, not professional accounting judgments or tenant acceptance.

The historical version-content endpoint does not support the current version. Task 08 must not replace all current reads with that endpoint, or assume an undocumented conditional GET behavior. Actual tenant support for the chosen stable observation remains separately unverified.

---

## C7. Atomic tasks

Each task's seven subsections are the implementation handoff. All scope, proposed identifiers, evidence limits and shared verification rules above remain binding. Read the exact task and its dependencies, not the entire test suite or ledger.

<a id="task-01"></a>

## Atomic Task 01 — Activation requires the current commercial key at commit

### 1. Objective

**Requirements:** AUD-WF-01. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** None.

**Current:** ActivateAsync checks current risk, holds and the paid allocated advance, but not the current exact commercial consent; activation preview omits that basis. Quotation revocation uses a firm lock while activation begins with a client lock.

**Target:** Only a currently authorized Partner can activate the exact engagement with both current keys and the independent advance gate; successful historical replay retains its original meaning.

**Non-goals:** No second lifecycle engine, automatic consent renewal, payment initiation, final-fee-payment release gate or history rewrite.

**Acceptance:** AC-01-01…AC-01-03 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs` |
| MODIFY | `src/AuditSphereOps.Application/Acceptance/EngagementActivationWorkspace.cs` |
| MODIFY | `src/AuditSphereOps.Application/Acceptance/DualKeyEvidence.cs` |
| MODIFY | `src/AuditSphereOps.Application/Practice/QuotationService.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Practice/ProposalResponseEvidence.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/EngagementActivationReviewTests.cs` |
| MODIFY | `tests/AuditSphereOps.Api.Tests/EngagementActivationReviewApiTests.cs` |

**GENERATE — PROPOSED if needed:** `CurrentCommercialActivationEvidence` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. If retained commercial IDs or a deferred guard change is required, generate an additive CurrentCommercialActivationEvidence migration and preserve old nullable evidence. Do not edit NativeEngagementActivationReview or relabel old records as newly verified.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing: ActivateAsync(IAuditSphereDbContext, ActorContext, Guid, CancellationToken, EngagementActivationRequest?) returns CommandResult<Guid>; EngagementActivationWorkspace.StateAsync/PreviewAsync and DualKeyEvidence supply existing owners. Keep the existing activation routes and CommandResult error mapping. PROPOSED: extend the review-basis payload with exact commercial evidence identities; do not add a new activation API.

**Source:** `AUD-WF-01`, supplied report L199–L220; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs); [src/AuditSphereOps.Application/Acceptance/EngagementActivationWorkspace.cs](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Acceptance/EngagementActivationWorkspace.cs); [src/AuditSphereOps.Application/Practice/QuotationService.cs](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/QuotationService.cs).

**Direct current read:** [src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs#L27-L121).

### 4. Recipe

1. Read the existing dual-key resolver and extract its exact quotation/approval/dispatch/accepted-response/fee-agreement linkage for this engagement. Use that resolver in state, preview and first execution; no fallback to a lead status, historical paid state or generic contact consent.
2. Serialize first activation and every mutation that invalidates the selected commercial key using the existing firm → client → dependent aggregate → actor/grant order. Audit ambient transaction callers before introducing an earlier lock; acquiring the firm row after an already-held client row is forbidden.
3. Include the quotation revision, required approval decisions, accepted response, relevant agreement/milestone evidence, engagement generation and current actor basis in the existing preview hash. Any change after assent requires a new preview; reuse current error codes for stale evidence, denied scope and blocked gates.
4. After locks, reauthorize and evaluate both keys, holds and full allocated advance; persist activation, receipt/evidence and portal-intent changes atomically. Recheck the captured identities before commit. Extend the database publication guard additively where Application checks alone leave direct writes inconsistent.
5. Preserve the current actor/request/hash checks on an already successful activation. Returning the original authorized receipt is a historical read, not a new activation or a fresh assertion that today's keys are valid. Legacy activations are never backfilled with invented commercial consent.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-01-01** · `tests/AuditSphereOps.Domain.Tests/EngagementActivationReviewTests.cs`

Given a paid advance and current risk, parameterize absent/revoked/superseded/mismatched commercial evidence: first activation is denied without an activation or portal-state side effect; the exact valid case succeeds.

**AC-01-02** · `tests/AuditSphereOps.Domain.Tests/EngagementActivationReviewTests.cs`

Use a deterministic two-transaction barrier for revocation versus activation; only a valid serialization can commit. Reuse the fixture for stale-preview and original-receipt replay.

**AC-01-03** · `tests/AuditSphereOps.Api.Tests/EngagementActivationReviewApiTests.cs`

Extend the existing reviewed activation contract assertion only for the added review-basis fields and current error mapping; do not duplicate all authorization cases.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~EngagementActivationReviewApiTests&FullyQualifiedName~ActivationCommercialEvidence_'

# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~EngagementActivationReviewTests&FullyQualifiedName~ActivationCommercialEvidence_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-01-01…AC-01-03 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-02"></a>

## Atomic Task 02 — All completion consumers require current independent procedure evidence

### 1. Objective

**Requirements:** AUD-WF-02. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** None.

**Current:** Completion credits raw Reviewed state even for an old risk-unlinked result after input-generation changes and planning is reapproved. The stricter current-procedure evidence predicate already exists.

**Target:** Completion, SRM facts, clearance/opinion/signing readiness and later coverage consumers agree on exact current result and independent review.

**Non-goals:** No blanket procedure-status reset, deletion of old reviews, automatic professional review or per-procedure E2E duplication.

**Acceptance:** AC-02-01…AC-02-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Completion.cs` |
| MODIFY | `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Audit/ProcedureRiskBandEvaluator.cs` |
| MODIFY | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs` |

### 3. Anchors

Existing internal record CurrentProcedureEvidence and EvaluateProcedureEvidenceForAuthorizedScopeAsync(db, firmId, clientId, engagementId, procedureId, ct). It checks generation, CurrentResultRevision, captured risk basis, open notes, applicability, result/review state, exact reviewed revision and different preparer/reviewer. PROPOSED: a bounded batched overload in the same partial, preserving those semantics.

**Source:** `AUD-WF-02`, supplied report L220–L242; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Completion.cs](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Completion.cs); [src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs:23–39](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs#L23-L39); [src/AuditSphereOps.Application/Audit/ProcedureRiskBandEvaluator.cs:68–72,108–110,220–236,264–280](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Audit/ProcedureRiskBandEvaluator.cs#L68-L72).

**Direct current read:** [src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs#L6-L43).

### 4. Recipe

1. Factor the current predicate without relaxing it; fetch the scoped procedure/result/latest-review/open-note inputs in bounded batches rather than calling several database queries per procedure. Read one coherent generation/planning basis for the evaluation.
2. Replace completion's raw status count with this predicate for every applicable procedure, including RiskId == null. Keep approved non-applicability decisions separate from an applicable procedure's success; absent evidence remains a blocker.
3. Return explicit missing/stale-generation/stale-result/stale-review/open-note reasons. Bind readiness/SRM digests to the actual selected result/review/generation identities so re-rendering an SRM cannot launder old evidence.
4. Trace EvaluateCompletionForAuthorizedScopeAsync into completion facts, SRM, PartnerClearAsync, DecideOpinionAsync and signing. Recheck the authoritative inputs under their existing locks at protected publication boundaries; a read-side green summary is not write authorization.
5. Keep all former results/reviews and prior issued snapshots readable as historical; fresh preparation and independent review, not status rewriting, restores readiness.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-02-01** · `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs`

Given a reviewed risk-unlinked procedure, approve a new mapping and fresh planning: completion remains blocked until a fresh result and independent review exist. Continue the same fixture through SRM/clearance/sign eligibility.

**AC-02-02** · `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs`

Parameterize exact revision mismatch, open note and self-review against the shared evidence evaluator; retain the existing valid risk-linked case. Assert batched behavior does not omit an applicable procedure.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~AuditDeliverablesTests&FullyQualifiedName~CompletionCurrentProcedureEvidence_'
```

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-02-01…AC-02-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-03"></a>

## Atomic Task 03 — Invoice preparation binds every billed source to the permitted client

### 1. Objective

**Requirements:** AUD-FIN-01. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** None.

**Current:** BillingService validates TIME by firm/id/revision and amount but omits the target client's source relationship; non-TIME kinds bypass typed validation. A later unbilled-time query detects the mismatch only after the write.

**Target:** An invoice can reserve only an exact supported source belonging to its authorized target client and permitted engagement context; rejected input leaves no reservation.

**Non-goals:** No claim of a remotely exploitable UI endpoint where none was traced; no client-bookkeeping or group-ledger change.

**Acceptance:** AC-03-01…AC-03-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Practice/BillingService.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Practice/FirmUnbilledTimeQuery.cs` |
| READ-ONLY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Practice.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/BillingCommandScopeIsolationTests.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/BillingTests.cs` |

### 3. Anchors

Existing BillingService.Create/prepare invoice path at report source lines 110–190; ValidateSourceAsync at 514–528; BillingSourceAllocation uniqueness. PROPOSED: pass the server-loaded billing-account client and supported source purpose into the existing validator, not an untrusted client ID from a request.

**Source:** `AUD-FIN-01`, supplied report L368–L384; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Application/Practice/BillingService.cs:110–190](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/BillingService.cs#L110-L190); [src/AuditSphereOps.Application/Practice/FirmUnbilledTimeQuery.cs:161–171](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/FirmUnbilledTimeQuery.cs#L161-L171); [BillingCommandScopeIsolationTests.cs](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/tests/AuditSphereOps.Domain.Tests/BillingCommandScopeIsolationTests.cs).

### 4. Recipe

1. Load and authorize the billing account first, then resolve each source through its actual type owner. TIME must be approved, billable, exact revision/currency/hours/value and owned by the invoice client's permitted engagement; validate any optional engagement attribution explicitly.
2. Enumerate the actually supported source kinds from current callers. Reject unknown kinds; preserve a manual line only where a separately authorized existing contract explicitly permits it. Do not make a fee-milestone path fail by inventing a TIME-only global contract.
3. Lock and recheck the account, client and source under the existing order; retain current uniqueness and cancellation/reversal reservation rules. Validate all lines before publishing an invoice or allocation.
4. Use one transaction for invoice and source reservations. A wrong client, stale source or unsupported kind returns the existing scoped/validation failure without disclosing sibling source data.
5. Provide a read-only detection query/evidence procedure for legacy source mismatches. Route corrections to a human-reviewed workflow; never reassign historical time or delete an invoice to make reconciliation pass.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-03-01** · `tests/AuditSphereOps.Domain.Tests/BillingCommandScopeIsolationTests.cs`

Use a valid authorized target account and a known sibling client's approved TIME source with matching money: deny and verify zero invoice/reservation rows.

**AC-03-02** · `tests/AuditSphereOps.Domain.Tests/BillingTests.cs`

Parameterize permitted source, stale revision, wrong status and unsupported kind through the existing fixture; preserve one-reservation behavior across post/cancel/reversal.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~BillingCommandScopeIsolationTests&FullyQualifiedName~InvoiceTimeSourceScope_'

# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~BillingTests&FullyQualifiedName~InvoiceTimeSourceScope_'
```

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-03-01…AC-03-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-04"></a>

## Atomic Task 04 — Finance mutations retain and recover one exact original outcome

### 1. Objective

**Requirements:** AUD-FIN-02. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** 03.

**Current:** Generic record-receipt, allocation and credit requests lack a durable command identity. The existing unique receipt/invoice allocation pair prevents same-pair double allocation, but does not recover the original success.

**Target:** Record receipt, allocate receipt and issue credit have actor-owned exact request recovery and conflict semantics without duplicating financial effects.

**Non-goals:** No global payment-reference uniqueness, no automatic revenue posting, no replacement of ledger services, no generic command bus.

**Acceptance:** AC-04-01…AC-04-03 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Practice/BillingService.cs` |
| MODIFY | `src/AuditSphereOps.Application/Practice/FeeAgreementService.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Application/Practice/BillingCommandWorkspace.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Domain/Practice/BillingCommandReceipt.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Practice.cs` |
| MODIFY | `src/AuditSphereOps.Api/Ui/UiEndpoints.Finance.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/BillingTests.cs` |
| MODIFY | `tests/AuditSphereOps.Api.Tests/BillingWorkspaceApiTests.cs` |

**GENERATE — PROPOSED if needed:** `BillingCommandReceipts` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Generate additive BillingCommandReceipts tables, scoped FKs, unique identity and append-only guards. Extend IAuditSphereDbContext/OnModelCreating only at the actual existing declaration owners located in preflight. Preserve all historical receipt/reference values; old rows remain LEGACY_UNVERIFIED_COMMAND with no fabricated request IDs.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing RecordReceiptAsync, AllocateReceiptAsync, credit workflow and FeeAgreementService receipt prelookup. Existing native reviewed-workspace patterns, rather than a new command framework, are the implementation template. PROPOSED DTO/record family BillingCommandRequest/BillingCommandReceipt; proposed routes under /api/ui/finance/commands: POST preview, POST execute, GET receipts/{requestId}. Confirm noncollision and use existing route-group conventions before declaration.

**Source:** `AUD-FIN-02`, supplied report L384–L404; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Api/Ui/UiEndpoints.Finance.cs:24–26,217–229](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/Ui/UiEndpoints.Finance.cs#L24-L26); [src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Practice.cs:64,181–182](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Practice.cs#L64); [20260918115026_BillingArtifactWorkflow.cs](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Infrastructure/Persistence/Migrations/20260918115026_BillingArtifactWorkflow.cs).

### 4. Recipe

1. Define one bounded, versioned intent for each of the three supported actions. Include action, exact account/invoice/receipt IDs, expected revisions, exact decimal strings, currency, effective date, reference/reason and existing reviewed basis. Firm/user are server-derived; a session epoch is an authorization fence, not a new idempotency namespace that allows the same request to duplicate after re-login.
2. Persist request identity unique within firm and original actor (plus the established workspace namespace), action, canonical intent/hash, review-basis refs, outcome entity IDs and immutable response version. Use the same local transaction as the financial mutation; do not treat payment reference as a globally unique command key.
3. Serialize identical concurrent requests with the existing receipt/barrier pattern, including absent-receipt lookups. Same identity and identical canonical intent returns the original result; changed intent is IdempotencyConflict. Reauthorize after waits and before publication. A missing lookup while the original might commit remains UNKNOWN; only retry the same original identity.
4. Keep existing allocation uniqueness and over-allocation guards. Wrap or amend all current API and fee-milestone callers of these mutation paths so retries cannot bypass the retained-command contract. A source milestone may deterministically own its request identity; do not generate a new ID on each retry.
5. Expose a redacted, actor-owned receipt lookup with current firm/client/invoice access checks. Historical outcomes may be read after later invoice revisions, but not through revoked or sibling access. No generic refreshed balance proves this command's success.
6. Declare named success schemas and exact Angular decoder inputs as part of this change. Preserve existing terms and reversal workspaces; do not replace their established receipts. Introduce deployment compatibility explicitly before removing an older unsafe request shape.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-04-01** · `tests/AuditSphereOps.Domain.Tests/BillingTests.cs`

Extend one parameterized fixture over receipt/allocation/credit: exact replay returns identical result IDs; changed intent conflicts; forced transactional failure leaves neither command receipt nor partial financial effect.

**AC-04-02** · `tests/AuditSphereOps.Domain.Tests/BillingTests.cs`

A deterministic concurrent record-receipt case creates one receipt. Keep the existing same-pair allocation uniqueness assertion and add exact original success recovery.

**AC-04-03** · `tests/AuditSphereOps.Api.Tests/BillingWorkspaceApiTests.cs`

Check lost-response recovery, absent identity, revoked scope and foreign actor/client opacity; assert real typed response body/decimal identities rather than only HTTP 200.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~BillingWorkspaceApiTests&FullyQualifiedName~FinanceCommandOutcome_'

# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~BillingTests&FullyQualifiedName~FinanceCommandOutcome_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-04-01…AC-04-03 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-05"></a>

## Atomic Task 05 — Invoice drafts and assent belong to one exact route visit

### 1. Objective

**Requirements:** AUD-UI-01. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** 04.

**Current:** InvoiceDetail retains seven form groups and reviewed booleans across invoice changes; asynchronous commands and reconciliation are fenced only by session invalidation.

**Target:** No invoice A draft, assent or callback can affect invoice B or a later visit to A; uncertain outcomes retain only recoverable original request references.

**Non-goals:** No broad Angular state-management package, form rewrite, browser storage of payment payloads/assent or cross-financial-domain changes.

**Acceptance:** AC-05-01…AC-05-03 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/finance/invoice.ts` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/app.routes.ts` |
| READ-ONLY | `src/AuditSphereOps.Ui/src/app/core/unsaved-changes.ts` |
| READ-ONLY | `src/AuditSphereOps.Ui/src/app/core/tab-drafts.ts` |
| MODIFY | `tests/AuditSphereOps.E2E.Tests/AngularInvoiceOutcomeRecoveryJourneyTests.cs` |

### 3. Anchors

Existing routeGuid(), InvoiceDetail context effect, run/reconcile and seven form groups; existing unsavedChangesGuard/tab-drafts patterns. PROPOSED per-component owner tuple: firm/user/session generation + invoiceId + monotonically increasing visit token. Task 04 supplies proposed original outcome DTOs.

**Source:** `AUD-UI-01`, supplied report L622–L667; baseline links retained under A1's evidence limits. [app.routes.ts:59–60](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/app.routes.ts#L59-L60); [invoice.ts:285–296](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/features/finance/invoice.ts#L285-L296); [features/finance/invoice-read.spec.ts](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts).

### 4. Recipe

1. Create one lifetime owner when invoice or identity changes; reset draft values, selected receipts/allocations, preview, assent, message and busy owners. Returning A → B → A creates a new token. Clear protected content immediately on detected authority loss.
2. Bind each action's assent to a canonical normalized input fingerprint plus the server review basis/revisions. Editing amount, reference, reason, date, receipt, target or current terms invalidates assent before execution. Do not store or restore checked assent.
3. Capture the owner and original request reference for command, paging and recovery callbacks. Ignore success/error/finally from another visit or a destroyed component; an old finally cannot clear the current visit's busy state.
4. Register the existing dirty/unknown-outcome navigation guard on the invoice route. Maintain bounded per-actor request references for UNKNOWN effects using the existing storage mechanism; explicit safe navigation does not discard the unresolved server identity.
5. Decode Task 04's exact receipt before success. Reconciliation is complete only after the scoped original receipt is received and validated; refreshing an invoice is presentation refresh, not reconciliation. On re-login, reauthorize before disclosing an old request's outcome.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-05-01** · `src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts`

Extend route-change coverage with entered amount/reference/assent, delayed callbacks and A → B → A; parameterize command success/refusal/unknown and assert all stale callbacks are inert.

**AC-05-02** · `src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts`

Change a reviewed amount or latest terms revision: confirmation requires fresh assent; legitimate same-visit receipt recovery preserves original request identity.

**AC-05-03** · `tests/AuditSphereOps.E2E.Tests/AngularInvoiceOutcomeRecoveryJourneyTests.cs`

Reuse one owned journey for actual navigation plus a lost response. Execute full journeys in CI, not as a routine local prerequisite.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/finance/invoice-read.spec.ts'
```

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-05-01…AC-05-03 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-06"></a>

## Atomic Task 06 — Portal polling no longer owns active document-editor lifetime

### 1. Objective

**Requirements:** AUD-UI-02. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** None.

**Current:** ClientPortalHome wraps child editors in ws.data(); the 30-second summary reload clears that data, destroys children and can discard file selection, drafts and an in-flight command context.

**Target:** Request-list refresh/paging updates the summary without recreating unrelated child editors; detected session/scope loss still removes protected content.

**Non-goals:** No global retain-on-error Api.resource rewrite, persistent file cache, disabled revocation checks or generic portal state framework.

**Acceptance:** AC-06-01…AC-06-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/portal/home.ts` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/portal/documents.ts` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/portal/home.spec.ts` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/portal/documents.spec.ts` |
| READ-ONLY | `src/AuditSphereOps.Ui/src/app/core/api.ts` |
| READ-ONLY | `src/AuditSphereOps.Ui/src/app/core/session.ts` |

### 3. Anchors

Existing ClientPortalHome @if(ws.data()), polling timer, Api.resource.load clearing behavior and PortalDocuments local state. PROPOSED: a small summary/editor lifetime separation inside this feature, not a global stale-data cache.

**Source:** `AUD-UI-02`, supplied report L667–L702; baseline links retained under A1's evidence limits. [home.ts:22–99](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/features/portal/home.ts#L22-L99); [home.ts:139–146](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/features/portal/home.ts#L139-L146); [core/api.ts:42–49](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/core/api.ts#L42-L49).

### 4. Recipe

1. Move summary/request loading into a subview or separate signals so a valid authorized portal shell owns the document component independently of transient list data. Preserve initial authorization/bootstrap denial; never render protected children solely from stale cached data.
2. Expose only the child activity/dirty/unknown flags required to protect deliberate refresh/navigation. Automatic list polling may continue independently; explicit destructive changes require the existing discard confirmation.
3. Fence child command and read callbacks by identity, target, visit and destruction. Parent polling must not issue an extra child mutation or convert unknown status to success/failure.
4. On actual denied/current-session failure, clear protected data, previews and file handles. Distinguish a transient list loading state from authorization failure using the existing error contract; never retain protected content after a known denial.
5. Keep file bytes and assent in memory only. Full browser reload may require file reselection, stated explicitly; recovery of a server effect uses its saved identity, not restored file bytes.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-06-01** · `src/AuditSphereOps.Ui/src/app/features/portal/home.spec.ts`

Type a comment and select a file in the real child or one small activity stub; advance the existing poll timer and change list page: child identity, target, draft and file selection remain.

**AC-06-02** · `src/AuditSphereOps.Ui/src/app/features/portal/documents.spec.ts`

A delayed command around polling dispatches once. Destroy/revoke the owner and flush late callbacks: protected content is not restored and the obsolete callback is inert.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/portal/home.spec.ts'

npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/portal/documents.spec.ts'
```

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-06-01…AC-06-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-07"></a>

## Atomic Task 07 — Shared-document comments recover the exact original submission

### 1. Objective

**Requirements:** AUD-UI-03. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** 06.

**Current:** CommentAsync creates a fresh GUID for each body-only request; a generic documents read clears uncertainty, without identifying the original request.

**Target:** One actor-owned comment request produces at most one immutable communication and can be recovered after a lost reply.

**Non-goals:** No matching-by-text recovery, generic mailbox ingestion, signature provider or blanket upload redesign.

**Acceptance:** AC-07-01…AC-07-03 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Documents/ClientPortalReviewCommands.cs` |
| MODIFY | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs` |
| MODIFY | `src/AuditSphereOps.Api/Ui/UiEndpoints.Portal.cs` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/portal/documents.ts` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/portal/documents.spec.ts` |
| MODIFY | `tests/AuditSphereOps.Api.Tests/UiPortalContractTests.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Domain/Completion/DeliverableCommentReceipt.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.AuditDeliverables.cs` |
| MODIFY — missing assertions only | `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs` |

**GENERATE — PROPOSED if needed:** `PortalCommentCommandReceipts` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add scoped immutable comment-request/outcome relation and uniqueness. Existing comments remain readable without invented request identities or retroactive author attribution.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing body-only portal comment route, ClientPortalReviewCommands.CommentAsync and AuditDeliverableService.CommentAsync; existing final-release lock boundary. PROPOSED CommentRequest/CommentReceipt fields and a scoped receipt GET in the existing portal documents route family; adjacent scan hash/signatory idempotency remains the owner.

**Source:** `AUD-UI-03`, supplied report L702–L730; baseline links retained under A1's evidence limits. [UiEndpoints.Portal.cs:16,94–95](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/Ui/UiEndpoints.Portal.cs#L16); [ClientPortalReviewCommands.cs:13–20,39–46](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Documents/ClientPortalReviewCommands.cs#L13-L20).

### 4. Recipe

1. Extend the existing comment request with requestId, canonical body/hash, exact shared-document/review identity and current preview/revision basis if required by its existing contract. Keep actor, firm and current portal client server-derived.
2. Insert comment and its immutable original outcome in one existing final-release-serialized transaction, using actor/request uniqueness and a race-safe absent lookup. Exact replay returns the original comment ID; same ID/different body or target conflicts.
3. Return bounded identity/attribution needed for legitimate history: comment ID, authorized display attribution, timestamp, target and original request identity. Receipt lookup enforces current client/document scope and original-actor recovery without leaking private staff data.
4. Remove list-refresh-as-confirmation. Preserve an UNKNOWN request until its exact receipt or definitive terminal result is established; an absent lookup does not justify another identity while the original can commit.
5. Reuse the signed-scan same-bytes/same-signatory owner and bundle acknowledgment contract; expose their retained identities where needed, not a second upload subsystem. Task 06 fences their presentation lifetime.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-07-01** · `tests/AuditSphereOps.Api.Tests/UiPortalContractTests.cs`

Parameterize normal comment, identical replay, changed body and foreign actor/client lookup; drop the first reply and recover the same comment exactly once.

**AC-07-02** · `src/AuditSphereOps.Ui/src/app/features/portal/documents.spec.ts`

A matching text in a refreshed list cannot resolve UNKNOWN; the exact typed receipt can. Malformed receipt and late old-visit response cannot show success.

**AC-07-03** · `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs`

Reuse the deliverable fixture for atomic rollback and existing final-release denial; retain existing signed-scan dedup assertions rather than copying them.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~UiPortalContractTests&FullyQualifiedName~PortalCommentOriginalOutcome_'

npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/portal/documents.spec.ts'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-07-01…AC-07-03 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-08"></a>

## Atomic Task 08 — PBC receipts bind a stable provider version to exact bytes

### 1. Objective

**Requirements:** AUD-PLAT-01. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** None.

**Current:** GraphPbcProviderSink observes a version, then may download unfenced current content and combine it with the prior version ID. Staged SHA/size checks do not by themselves establish that version-to-byte association.

**Target:** A transfer receipt names only a provider version whose stable content identity and full digest were established; changed or unknown observations remain retryable/blocked.

**Non-goals:** No unconditional historical-endpoint call for the current version, broad Graph scopes, provider overwrite, live business-file experiment or malware-scanning system.

**Boundary:** Live acceptance remains BLOCKED_EXTERNAL until an authorized selected-site test validates the chosen stable identity semantics.

**Acceptance:** AC-08-01…AC-08-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Infrastructure/Providers/GraphPbcProviderSink.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Providers/GraphSelectedSiteDrive.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Documents/PbcDocumentTransferHandler.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Operations/OperationDispatcher.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/PbcSelectedSiteProviderTests.cs` |

### 3. Anchors

Existing selected-site drive current/historical content methods and PbcSelectedSiteProviderTests.ProvisionedRepository_ResolvesAndTransfersWithExactVersionReadback. Microsoft Graph v1.0 explicitly does not support current-version content through the historical version-content endpoint. PROPOSED: stable readback evidence/fence within the current provider adapter.

**Source:** `AUD-PLAT-01`, supplied report L791–L807; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Infrastructure/Providers/GraphPbcProviderSink.cs:66–73](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Infrastructure/Providers/GraphPbcProviderSink.cs#L66-L73); [GraphSelectedSiteDrive.cs:163–198](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Infrastructure/Providers/GraphSelectedSiteDrive.cs#L163-L198); [PbcDocumentTransferHandler.cs:100–112,138–146](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Documents/PbcDocumentTransferHandler.cs#L100-L112).

### 4. Recipe

1. Keep historical-version reads for genuinely historical versions. For the current version, implement a bounded metadata/version → full current content → metadata/version observation using provider-supported version/ETag identities; capture both item identity and version before and after, not just matching bytes.
2. Accept current readback only when the before/after stable version/content identities match and the full bytes meet the staged SHA-256 and size. Do not claim undocumented If-Match semantics for a GET endpoint. Confirm the chosen metadata fence against the actual provider contract before live enablement.
3. If the intended version became historical, reconcile that exact historical version using the existing adapter. Otherwise return the existing bounded retry/blocked/UNKNOWN disposition; never relabel new bytes as old evidence or create another upload intent.
4. Preserve exact site/drive/folder/name checks, byte limits, cancellation and redirect credential isolation. A preauthenticated content URL must not receive the Graph bearer token and must not be logged or persisted as ordinary evidence.
5. Retain enough safe version/ETag/observation evidence only where the existing receipt contract needs it. Legacy receipt bindings remain unverified where the old observation cannot prove the association; do not rewrite old hashes or versions.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-08-01** · `tests/AuditSphereOps.Domain.Tests/PbcSelectedSiteProviderTests.cs`

Extend the existing fake provider with a barrier between metadata and content; parameterize changed content and change-then-restoration. Neither can produce a wrongly associated receipt.

**AC-08-02** · `tests/AuditSphereOps.Domain.Tests/PbcSelectedSiteProviderTests.cs`

Keep stable current success and existing later historical verification; verify bounded reconciliation does not overwrite or duplicate the uploaded object.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~PbcSelectedSiteProviderTests&FullyQualifiedName~PbcStableVersionReadback_'
```

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-08-01…AC-08-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-09"></a>

## Atomic Task 09 — All finance and commercial recipients use one current scoped resolution policy

### 1. Objective

**Requirements:** AUD-WF-04. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** None.

**Current:** FeeAgreementService chooses the first FINANCE route, can select an inactive fallback and omits contact validity dates; it disagrees with the purpose resolver's explicit ambiguity handling.

**Target:** Invalid or ambiguous recipients never dispatch; approved purpose precedence and justified overrides produce exact retained recipient evidence.

**Non-goals:** No widening PBC audiences, generic tenant directory lookup, silent historical rerouting or new CRM engine.

**Boundary:** DEC-09 purpose precedence/override authority is not approved by ADR-0018. Fail-closed invalid-contact fixes can proceed independently; new fallback policy cannot.

**Acceptance:** AC-09-01…AC-09-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Practice/FeeAgreementService.cs` |
| MODIFY | `src/AuditSphereOps.Application/Practice/FeeAgreementService.PaymentVouchers.cs` |
| MODIFY | `src/AuditSphereOps.Application/Practice/PracticeCrmService.Routing.cs` |
| MODIFY | `src/AuditSphereOps.Application/Practice/CommercialDocumentService.cs` |
| MODIFY | `src/AuditSphereOps.Application/Practice/CommercialMailDeliveryHandler.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/CommercialWorkflowTests.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Documents/PbcService.cs` |

### 3. Anchors

Existing ResolveRecipientAsync, purpose resolution with Ambiguous and reasoned override, and saved-recipient CommercialMailDeliveryHandler. ADR-0018 explicitly leaves DEC-09 routing precedence undecided.

**Source:** `AUD-WF-04`, supplied report L256–L274; baseline links retained under A1's evidence limits. [PracticeCrmService.cs:360–378](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/PracticeCrmService.cs#L360-L378); [src/AuditSphereOps.Application/Practice/FeeAgreementService.cs:418–442](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/FeeAgreementService.cs#L418-L442); [src/AuditSphereOps.Application/Practice/PracticeCrmService.Routing.cs:208–272](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/PracticeCrmService.Routing.cs#L208-L272).

### 4. Recipe

1. Immediately preserve the uncontroversial fail-closed requirements: current active contact, contact and routing effective dates, exact client, supported purpose and nonempty validated address. A caller cannot resolve a sibling contact or select arbitrarily among ambiguous routes.
2. Record the purpose precedence/override decision through the existing ADR owner before enabling a new fallback. Until approved, block ambiguous/missing purpose routes; do not silently choose primary lead contact as a policy.
3. Reuse a shared internal resolver suited to the invoking finance/commercial service. Keep each public command's current role authorization; do not grant finance staff general CRM administration merely to reuse a helper.
4. Retain recipient identity, purpose, effective-policy revision, selected address and override reason in the exact document/dispatch intent. Resolve engagement-letter addressees consistently with approved commercial purpose; do not rewrite historically accepted dispatches.
5. Recheck eligibility before a pending delivery. If the current permitted recipient differs, block and require a new reviewed dispatch rather than silently rerouting. The explicit PBC ClientOwnerUserId audience remains separate unless its approved policy is deliberately changed.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-09-01** · `tests/AuditSphereOps.Domain.Tests/CommercialWorkflowTests.cs`

Parameterize inactive fallback, expired contact, ambiguous routes and a single valid primary. Invalid cases enqueue nothing; approved explicit override retains its reason and exact recipient.

**AC-09-02** · `tests/AuditSphereOps.Domain.Tests/CommercialWorkflowTests.cs`

Changing contact authority after queueing blocks the saved pending intent without sending to another address; historical dispatch remains readable.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~CommercialWorkflowTests&FullyQualifiedName~CorrespondenceRecipientCurrentness_'
```

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-09-01…AC-09-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-10"></a>

## Atomic Task 10 — Invoice Send creates an exact durable dispatch rather than a status-only success

### 1. Objective

**Requirements:** AUD-WF-03. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** 04, 09.

**Current:** SendInvoiceAsync only transitions Posted → Sent and stamps SentAt; it does not render, enqueue or verify an invoice dispatch.

**Target:** An authorized Send action queues the exact posted invoice artifact for the approved finance recipient, with distinct queued/provider-accepted/rejected/unknown evidence.

**Non-goals:** No automatic invoice posting, final-fee-payment release gate, new mail framework or claimed recipient delivery from a provider acceptance response.

**Boundary:** Depends on approved routing (Task 09); invoice wording/manual channel acceptance and actual sender delivery are professional/external gates, not fabricated defaults.

**Acceptance:** AC-10-01…AC-10-03 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Practice/BillingService.cs` |
| MODIFY | `src/AuditSphereOps.Application/Practice/CommercialMailDeliveryHandler.cs` |
| MODIFY | `src/AuditSphereOps.Domain/Practice/Commercial.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Commercial.cs` |
| MODIFY | `src/AuditSphereOps.Api/Ui/UiEndpoints.Finance.cs` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/finance/invoice.ts` |
| CREATE — PROPOSED | `src/AuditSphereOps.Application/Practice/BillingInvoiceDocumentRenderer.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/CommercialWorkflowTests.cs` |
| MODIFY | `tests/AuditSphereOps.Api.Tests/BillingWorkspaceApiTests.cs` |
| MODIFY — missing assertions only | `src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts` |

**GENERATE — PROPOSED if needed:** `InvoiceDispatchArtifacts` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add invoice artifact/dispatch associations and supported notification-kind constraints additively. Do not relabel old Sent rows as provider verified.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing SendInvoiceAsync/TransitionInvoiceAsync, commercial mail notification/receipt lifecycle, posted billing records and Task 09 recipient resolver. PROPOSED invoice notification kind and exact invoice-artifact association; reuse the current rendering libraries and retained artifact store.

**Source:** `AUD-WF-03`, supplied report L242–L256; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Application/Practice/BillingService.cs:206–209](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/BillingService.cs#L206-L209); [src/AuditSphereOps.Domain/Practice/Commercial.cs:270–277](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Domain/Practice/Commercial.cs#L270-L277); [STE v2.2 L97,319,407–411,522](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L97).

### 4. Recipe

1. Build a deterministic invoice artifact from the exact posted invoice, current effective reviewed terms and approved document wording. Store immutable bytes/hash and source IDs; never render from editable draft fields after queue submission.
2. Prepare a reviewed dispatch snapshot containing invoice/artifact identity, recipient evidence, channel and current revision. Refuse draft/unapproved/unposted invoices or missing mail capability. A queued result is not a Sent/Delivered assertion.
3. Enqueue through the existing durable commercial-mail mechanism in the same local transaction as retained dispatch intent. The original request/artifact/recipient identity is the idempotency key; retries recover its receipt instead of emitting another notification.
4. Before provider work, revalidate exact recipient/capability and source eligibility under the existing provider plan contract; do not hold long database transactions open over network I/O. Reconcile lost responses through original message evidence before retrying a potentially accepted send.
5. Map provider accepted, rejected, unknown and any actually observed delivery receipt distinctly in API/UI. Do not claim exactly-once external email where the provider cannot prove it; enforce one local intent and avoid blind resend after UNKNOWN.
6. If manual external sending is professionally allowed, add an explicit recorded-manual-dispatch branch requiring actor, channel, timestamp and evidence. Otherwise omit it. Preserve legacy Sent timestamps as unverified legacy dispatch, not proof a provider delivered them.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-10-01** · `tests/AuditSphereOps.Domain.Tests/CommercialWorkflowTests.cs`

Extend the existing mail fixture with posted invoice artifact/recipient pins; normal queue and exact retry produce one intent, while changed invoice/recipient or an unposted invoice cannot dispatch.

**AC-10-02** · `tests/AuditSphereOps.Api.Tests/BillingWorkspaceApiTests.cs`

Assert typed queued/provider-accepted/unknown states and artifact identity; no HTTP 200 alone claims delivery.

**AC-10-03** · `src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts`

The Send action displays queued and unknown correctly and recovers its exact dispatch receipt without a second click creating another send.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~BillingWorkspaceApiTests&FullyQualifiedName~PostedInvoiceDispatch_'

# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~CommercialWorkflowTests&FullyQualifiedName~PostedInvoiceDispatch_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-10-01…AC-10-03 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-11"></a>

## Atomic Task 11 — FSLI workprogram coverage uses exact reviewed relationships

### 1. Objective

**Requirements:** AUD-WF-05. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** 02.

**Current:** FinancialStatementDrillDownQuery and StatementReviewWorkspace use normalized name/substrings and raw Reviewed state to attach procedures to financial-statement lines.

**Target:** Only an explicit current FSLI/taxonomy-to-procedure relation establishes coverage; stale and unmapped lines are visible and cannot claim review.

**Non-goals:** No fuzzy-match-as-audit-evidence, whole-file lock, chart/taxonomy rewrite or broad new assignment system.

**Acceptance:** AC-11-01…AC-11-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Accounting/Intake/FinancialStatementDrillDownQuery.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/Intake/StatementReviewWorkspace.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Domain/Audit/ProcedureFinancialStatementLink.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs` |
| MODIFY | `src/AuditSphereOps.Application/Audit/AuditProgramService.cs` |
| MODIFY | `src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/engagements/statements.html` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts` |
| CREATE — PROPOSED | `tests/AuditSphereOps.Domain.Tests/ProcedureFinancialStatementLinkTests.cs` |

**GENERATE — PROPOSED if needed:** `ProcedureFinancialStatementLinks` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add immutable reviewed link revisions/scoped FKs or extend the proven existing relation. Leave historical fuzzy associations unverified; no automatic authoritative backfill.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing Matches at drill-down lines 89–94 and raw coverage loop at StatementReviewWorkspace lines 287–315. Existing AuditProcedure/Risk IDs and destination codes are available; Task 02 provides current evidence. PROPOSED link: firm/client/engagement, exact taxonomy/mapping revision/destination ID, procedure ID, effective revision, source evidence and independent decision.

**Source:** `AUD-WF-05`, supplied report L274–L290; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Application/Accounting/Intake/FinancialStatementDrillDownQuery.cs:40–47](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Intake/FinancialStatementDrillDownQuery.cs#L40-L47); [StatementReviewWorkspace.cs:209–215](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Intake/StatementReviewWorkspace.cs#L209-L215); [src/AuditSphereOps.Domain/Audit/Audit.cs:113–134](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Domain/Audit/Audit.cs#L113-L134).

### 4. Recipe

1. Choose the smallest existing reviewed relation to extend; if no exact relation can represent the association, add the proposed scoped relation. Use stable IDs/revisions, never display labels as identity.
2. Allow name similarity only as a preparer suggestion with no coverage authority. Publish links through scoped reviewed commands, current mapping/taxonomy validation and revision fencing; prevent cross-engagement relations and duplicate effective links.
3. Query exact current links for line drill-down and action navigation. Parameterize batches to avoid one query per statement row; reauthorize the final projection.
4. Use Task 02's predicate to aggregate procedure readiness. Explicitly distinguish unmapped, missing work, stale review and current reviewed coverage; unlinked lines must not become Reviewed by vacuous all().
5. Map-generation changes invalidate affected relations or require explicit reviewed carry-forward. Preserve previous link/review history and historical statement output; do not automatically convert ambiguous old substring matches into approved links.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-11-01** · `tests/AuditSphereOps.Domain.Tests/ProcedureFinancialStatementLinkTests.cs`

PROPOSED fixture reusing current statement/fieldwork seeds: colliding labels do not attach, exact links do, and stale mapping/current-procedure evidence prevents Reviewed.

**AC-11-02** · `tests/AuditSphereOps.Domain.Tests/ProcedureFinancialStatementLinkTests.cs`

Parameterize wrong engagement/client relation and concurrent revision replacement; no unauthorized link is persisted. Assert the returned action targets the exact linked set.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~ProcedureFinancialStatementLinkTests&FullyQualifiedName~ExactFinancialStatementProcedureCoverage_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-11-01…AC-11-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-12"></a>

## Atomic Task 12 — Workflow templates instantiate fresh scoped PBC drafts

### 1. Objective

**Requirements:** AUD-WF-06. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** None.

**Current:** WorkflowTemplateStep stores PbcRequestReference, but plan preview/execution omits it. PbcService.CreateRequestAsync exists and requires an Active engagement; sending is a separate command.

**Target:** A plan containing an approved typed PBC template preview creates one fresh unsent request and exact task link, without copied evidence or activation bypass.

**Non-goals:** No new template engine, automatic send, copied prior-year client evidence, grant copying or change to the paid-advance activation boundary.

**Acceptance:** AC-12-01…AC-12-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Domain/Practice/WorkflowTemplates.cs` |
| MODIFY | `src/AuditSphereOps.Application/Practice/WorkflowTemplateService.cs` |
| MODIFY | `src/AuditSphereOps.Application/Practice/EngagementWorkflowPlanService.cs` |
| MODIFY | `src/AuditSphereOps.Application/Documents/PbcService.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.WorkflowTemplates.cs` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/practice/workflow-templates.ts` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/EngagementWorkflowPlanTests.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/WorkflowTemplateTests.cs` |

**GENERATE — PROPOSED if needed:** `WorkflowPlanPbcDrafts` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add typed template definition/version and unique plan-step→PBC binding additively; retain original PbcRequestReference text and historical plan receipts.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing CreatePbcRequestRequest(EngagementId, Objective, EntityScope, PeriodStart, PeriodEnd, Area, RequestedFormat, ControlTotals, ClientOwnerUserId, FirmOwnerUserId, ReviewerUserId, DueDate, Confidentiality, AcceptanceCriteria); PbcService.CreateRequestAsync and SendRequestAsync are separate. PROPOSED typed PBC definition/version inside the existing approved workflow-template structure.

**Source:** `AUD-WF-06`, supplied report L290–L304; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Domain/Practice/WorkflowTemplates.cs:64–79](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Domain/Practice/WorkflowTemplates.cs#L64-L79); [src/AuditSphereOps.Application/Practice/WorkflowTemplateService.cs:366,376,435,468,487](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/WorkflowTemplateService.cs#L366); [src/AuditSphereOps.Ui/src/app/features/practice/workflow-templates.ts:116](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/features/practice/workflow-templates.ts#L116).

**Direct current read:** [src/AuditSphereOps.Application/Documents/PbcService.cs](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/src/AuditSphereOps.Application/Documents/PbcService.cs#L14-L29); [src/AuditSphereOps.Application/Documents/PbcService.cs](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/src/AuditSphereOps.Application/Documents/PbcService.cs#L86-L164).

### 4. Recipe

1. Define the exact reusable PBC instruction fields using the existing CreatePbcRequestRequest shape. Add a versioned typed snapshot/reference to the current template publication workflow; a free-text reference alone is not an executable definition.
2. Plan preview resolves each PBC definition and shows objective, actual period, owners/reviewer, deadline and fresh request state. Resolve current scoped liaison/staff eligibility server-side; no prior-year response/grant/assent is copied.
3. Preserve the existing Active-engagement creation guard. Before activation, preview may show BLOCKED_ACTIVATION; do not create a real PBC request or weaken the guard. Reprepare and explicitly execute the plan's pending PBC materialization after activation using the same retained plan/step identity.
4. Within an owned transaction, reuse CreateRequestAsync for one fresh draft and atomically bind the plan execution/template revision/step/task/request. Add a scoped uniqueness constraint for this identity. Inspect nested transaction behavior; no partial task-only commit when PBC validation fails.
5. Execution never calls SendRequestAsync, uploads content or records received/reviewed status. Changed/unresolvable template or owner revocation requires fresh review, not a fallback request.
6. Preserve already executed template/task history. Old descriptive references stay descriptive/unresolved until deliberately mapped to a reviewed typed definition; do not infer their meaning.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-12-01** · `tests/AuditSphereOps.Domain.Tests/EngagementWorkflowPlanTests.cs`

A reviewed active-engagement plan creates one unsent request and exact task link; same plan/step replay creates none extra. Inject PBC validation failure and assert atomic rollback.

**AC-12-02** · `tests/AuditSphereOps.Domain.Tests/WorkflowTemplateTests.cs`

Changed/missing PBC definition and expired/wrong-client owner fail. A pre-activation plan shows its blocked requirement without bypassing CreateRequestAsync; after activation, fresh review creates fresh current-year state.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~EngagementWorkflowPlanTests&FullyQualifiedName~WorkflowPlanPbcDraft_'

# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~WorkflowTemplateTests&FullyQualifiedName~WorkflowPlanPbcDraft_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-12-01…AC-12-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-13"></a>

## Atomic Task 13 — The approved assignment policy and existing rework fallback are accepted

### 1. Objective

**Requirements:** AUD-WF-07. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** None.

**Current:** Pre-work procedure ownership is undecided. Current submission records the actual preparer; enabled WorkflowExperience already creates one linked rework task and falls back to unassigned manager routing when the preparer is ineligible.

**Target:** An explicit approved ownership/pickup policy is visible and enforced, and the existing enabled rework queue is accepted without rebuilding it.

**Non-goals:** No new task engine, automatic scope grant, duplicate rejection-history mechanism or mandatory exact assignee invented by the agent.

**Boundary:** Qualified/process owner must settle DEC-07. Existing rework implementation is preserved; only an accepted ownership gap authorizes conditional code.

**Acceptance:** AC-13-01…AC-13-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| READ-ONLY | `src/AuditSphereOps.Application/Audit/AuditProgramService.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Practice/PracticeTimeService.cs` |
| READ-ONLY | `src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs` |
| READ-ONLY | `src/AuditSphereOps.Domain/Audit/Audit.cs` |
| MODIFY — conditional | `docs/architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md` |
| MODIFY — conditional | `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts` |
| MODIFY — conditional | `tests/AuditSphereOps.Domain.Tests/EngagementWorkflowTaskTests.cs` |

### 3. Anchors

Existing SubmitResultAsync, ReviewResultAsync(createWorkflowReworkTask), API WorkflowExperience:Enabled wiring and CreateTaskCoreAsync(... allowUnassignedAssignee). ADR-0018 DEC-07 leaves assignment methodology undecided.

**Source:** `AUD-WF-07`, supplied report L304–L318; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Domain/Audit/Audit.cs:113–134](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Domain/Audit/Audit.cs#L113-L134); [src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs:43–46](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs#L43-L46); [src/AuditSphereOps.Application/Practice/PracticeTimeService.cs:93–132](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/PracticeTimeService.cs#L93-L132).

### 4. Recipe

1. Obtain the process owner's decision: explicit assigned owner before work, or eligible engagement-staff pickup with actual preparer attribution. Record the exact authority and reassignment rules; do not decide by inference.
2. If eligible pickup is accepted, document and verify the current path; zero new assignment model is required. If exact assignment is mandated, first reuse a current task binding; only then add a minimal scoped assignment relation with audited reassignments, current eligibility and stale fencing.
3. Verify the selected release configuration deliberately enables the existing workflow experience. A disabled convenience queue does not invalidate the separate mandatory professional rejection/comment history.
4. Exercise current rejection: exactly one result/revision-linked rework task, original preparer where eligible, otherwise an accountable visible unassigned manager queue. Do not recreate existing idempotency or fallback.
5. Ensure task ownership, pickup or completion grants no client scope and cannot set a procedure review, lifecycle clearance or professional sign-off.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-13-01** · `tests/AuditSphereOps.Domain.Tests/EngagementWorkflowTaskTests.cs`

Reuse the existing review-to-task fixture for enabled configuration, eligible preparer and ineligible-preparer fallback; parameterize the eligibility alternatives rather than adding another engine test.

**AC-13-02** · `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts` — acceptance/source owner, not an existing automated test

Manual/CI role journey: the selected policy is visible, unassigned work has a manager owner for routing, and completing the operational task does not approve the procedure.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~EngagementWorkflowTaskTests&FullyQualifiedName~WorkflowReworkAssignmentPolicy_'
```

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-13-01…AC-13-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-14"></a>

## Atomic Task 14 — Partner-review phase begins at manager handover, before Partner clearance

### 1. Objective

**Requirements:** AUD-WF-08. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** 02.

**Current:** EngagementLifecycleQuery requires current Partner clearance before projecting Partner Approval, leaving initial Partner review under manager responsibility.

**Target:** A current manager handover projects Partner Approval with the Partner's own clearance/opinion/signature gates still outstanding.

**Non-goals:** No twelfth lifecycle state, second writable stage, relaxed confirmations or automatic opinion.

**Boundary:** Process-owner acknowledgment of the precise handover predicate is required before rollout; this specification does not delegate professional approval to task state.

**Acceptance:** AC-14-01…AC-14-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Acceptance/EngagementDisplayPhaseMapper.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/EngagementLifecycleQueryTests.cs` |

### 3. Anchors

Existing eleven canonical states and seven-phase mapper; PartnerApprovalReady currently requires clearance. STE v2.2 manager handover and Partner review are distinct steps. PROPOSED handover predicate in the existing derived query, not a stored stage.

**Source:** `AUD-WF-08`, supplied report L318–L332; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs:166–167,198–207](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs#L166-L167); [STE v2.2 L602](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L602); [STE v2.2 L603](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L603).

### 4. Recipe

1. Define handover readiness from current manager-approved applicable work, resolved relevant notes, current SRM and the required independently evaluated confirmation prerequisite. Reuse Task 02's evidence predicate and existing confirmation/SRM owners.
2. Separate ready-for-Partner-review from Partner-clearance-complete. Project the Partner stage and current staffed Partner responsibility before the first clearance when handover is ready; show clearance/opinion/signature as separate incomplete gates.
3. On stale inputs, new open notes or a withdrawn prerequisite, derive the appropriate prior phase and blockers. Never mutate lifecycle state based on an operational task or cached can* flag.
4. Retain all existing Partner-only commands and post-signature exact-bundle/release/archive checks. Confirm the source wording with the responsible process owner where the handover interpretation affects an accepted operating procedure.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-14-01** · `tests/AuditSphereOps.Domain.Tests/EngagementLifecycleQueryTests.cs`

Update the implementation-derived expectation: current manager handover with no Partner clearance projects Partner Approval; parameterize new note/stale work to move the projection back.

**AC-14-02** · `tests/AuditSphereOps.Domain.Tests/EngagementLifecycleQueryTests.cs`

Keep mapper totality and unknown-index rejection; assert tasks cannot force progression and authorization to Partner commands remains unchanged.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~EngagementLifecycleQueryTests&FullyQualifiedName~PartnerHandoverProjection_'
```

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-14-01…AC-14-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-15"></a>

## Atomic Task 15 — SRM includes reviewed significant-estimate conclusions

### 1. Objective

**Requirements:** AUD-WF-10. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** 02, 11.

**Current:** SRM renders risk/procedure/difference facts but no explicit significant-estimate assessment/conclusion; generic procedure text and a critical-risk flag are not that summary.

**Target:** An approved SRM profile includes exact current significant-estimate evidence or a deliberate independently reviewed none-identified conclusion.

**Non-goals:** No universal ISA-compliance claim, autonomous estimate valuation or generated professional conclusions.

**Boundary:** DEC-07 significant-estimates methodology and professional narrative/template approval remain open.

**Acceptance:** AC-15-01…AC-15-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs` |
| MODIFY | `src/AuditSphereOps.Domain/Completion/AuditDeliverables.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.AuditDeliverables.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Audit/AuditPlanningService.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs` |

**GENERATE — PROPOSED if needed:** `SignificantEstimateReviewEvidence` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Use additive estimate-summary revisions/links only when current owners cannot retain the required snapshot; no retroactive invented judgments.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing SRM fact capture, risk IDs, exact current procedure evidence and source digest. PROPOSED EstimateSummaryEntry: affected exact area, estimate identity, key judgment, evidence/result/review refs and human conclusion; no generated professional opinion.

**Source:** `AUD-WF-10`, supplied report L344–L356; baseline links retained under A1's evidence limits. [AuditDeliverableService.cs:53–58,110–117](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs#L53-L58); [STE v2.2 L494](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L494).

### 4. Recipe

1. Obtain DEC-07's estimate-summary methodology and template acceptance. Reuse any current estimate/risk assessment owner; add only the missing structured link/summary revision, not a separate estimate calculator.
2. Capture a preparer's identified estimate, exact FSLI/risk/current procedure evidence, key judgment and proposed narrative. Require the responsible independent review under current scope and generation before the narrative counts in SRM.
3. Render the approved entries with their current source identities. A no-entries query is UNKNOWN, not 'none'; none identified needs an explicit reviewed, period/population-bound conclusion if allowed by the accepted methodology.
4. Include summary revision/evidence/decision in SRM's digest and downstream currentness. Changed assessment or review stales SRM and later protected decisions; preserve prior SRM bytes and decision history.
5. Expose only the minimal existing completion authoring/read UI and API contracts needed to maintain these entries; declare new identifiers as PROPOSED and do not infer authoritative narrative from amounts.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-15-01** · `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs`

A designated estimate appears with exact current reviewed evidence; changing its assessment or linked result stales SRM. An absent assessment is not rendered as an approved none.

**AC-15-02** · `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs`

Parameterize denied scope, self-review and a permitted independently reviewed none-identified conclusion, if that policy is approved.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~AuditDeliverablesTests&FullyQualifiedName~SignificantEstimateSrm_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-15-01…AC-15-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-16"></a>

## Atomic Task 16 — Professionally accept the existing management-letter narrative capacity

### 1. Objective

**Requirements:** AUD-WF-09. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_EXTERNAL`. **Dependencies:** None.

**Current:** FindingType is free text capped at 100 database characters and rendered as Matter with impact/recommendation/response. A missing mandatory narrative field has not been demonstrated.

**Target:** Representative management letters are accepted using the existing model, or a precisely evidenced capacity/label change is approved before implementation.

**Non-goals:** No mandatory new narrative column, renderer rewrite or artificial minimum word count.

**Boundary:** Professional DEC-02 acceptance must establish whether any code change is needed. A new field is not preauthorized by this report.

**Acceptance:** AC-16-01…AC-16-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| READ-ONLY | `src/AuditSphereOps.Application/Audit/AuditPlanningService.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Audit/AuditPlanningService.FindingCommands.cs` |
| READ-ONLY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs` |
| MODIFY — conditional | `src/AuditSphereOps.Ui/src/app/features/audit/plan.ts` |
| MODIFY — conditional | `src/AuditSphereOps.Ui/src/app/features/audit/records.ts` |
| MODIFY — conditional | `src/AuditSphereOps.Ui/src/app/features/audit/finding-command-contracts.ts` |

### 3. Anchors

Existing FindingType free-text behavior, database max 100, Angular 'Finding type' label, recommendation max 2000 and management-letter renderer. Readback's larger allowance does not change the persistence cap. ADR-0018 DEC-02 is the professional owner.

**Source:** `AUD-WF-09`, supplied report L332–L344; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Application/Audit/AuditPlanningService.cs:651–657](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Audit/AuditPlanningService.cs#L651-L657); [src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs:329](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs#L329); [src/AuditSphereOps.Ui/src/app/features/audit/plan.ts:275–277](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/features/audit/plan.ts#L275-L277).

### 4. Recipe

1. Use representative firm-approved deficiencies, impacts and recommendations to review an actual existing management letter. Record whether the 100-character matter is sufficient and whether the current label causes category-only input.
2. If capacity is accepted, close this acceptance item with evidence and zero schema/renderer changes. A small approved label/help change to Deficiency/observation is sufficient where wording alone is the issue.
3. Only when an accepted example proves insufficient capacity, approve an explicit new maximum or separate category/narrative requirement. Then update write validation, EF, DTOs, decoder, UI and exact reviewed source together, using additive schema changes.
4. Preserve all old text and reviewed letters. Neither expanding a field nor renaming a label can silently reissue or reapprove prior artifacts.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-16-01** · `src/AuditSphereOps.Ui/src/app/features/audit/finding-command-contracts.ts` — acceptance/source owner, not an existing automated test

Acceptance-only baseline: no new automated test is required without a behavior change. A capacity/validation change reuses the nearest existing finding command/decoder tests at exact accepted boundaries.

**AC-16-02** · `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs` — acceptance/source owner, not an existing automated test

Professional artifact review confirms Deficiency → Impact → Recommendation is understandable and complete on representative output; retain reviewer and exact artifact hash.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-16-01…AC-16-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-17"></a>

## Atomic Task 17 — Accept correspondence JSON format and supported inquiry coverage

### 1. Objective

**Requirements:** AUD-WF-11. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_EXTERNAL`. **Dependencies:** None.

**Current:** The bundle already contains Audit_trail.json with Confirmations, Communications and ClearedQueries, bounded reads and a pinned source digest. PDF/DOCX is not required by the cited source.

**Target:** Professional owners accept the existing exact-source trail and supported channels, or explicitly scope a demonstrated missing category/optional human-readable view.

**Non-goals:** No mandatory PDF/DOCX trail, automatic external mailbox ingestion or recreated fourth bundle component.

**Boundary:** Professional format/channel-coverage acceptance is outstanding; new rendering or inbox integration needs separate approved scope.

**Acceptance:** AC-17-01…AC-17-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| READ-ONLY | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs` |
| READ-ONLY | `src/AuditSphereOps.Domain/Completion/DeliverableAssembly.cs` |
| MODIFY — conditional | `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.Assembly.cs` |

### 3. Anchors

Existing bundle path 04_Management_correspondence/Audit_trail.json and category/source digest guards. PROPOSED optional presentation only after acceptance; structured JSON remains authoritative source output.

**Source:** `AUD-WF-11`, supplied report L356–L368; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs:44–59](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs#L44-L59); [STE v2.2 L521](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L521); [STE v2.2 L518](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L518).

### 4. Recipe

1. Review a representative exact bundle for formal in-app inquiries, confirmations and cleared queries. Record source category membership, chronology and actor/decision attribution; no generic mailbox coverage is implied.
2. Obtain a decision on whether supported authoritative channels extend beyond in-app PBC communications, confirmation records and client deliverable comments. Record an approved scope boundary or precise additional source, not a speculative connector.
3. Accept JSON with zero renderer changes where it meets the use case. Only after an explicit preference/requirement, add a human-readable view from the same exact source snapshot, with its own manifest hash and retained JSON.
4. Keep bounded reads/overflow refusal, current final-release fences and immutable history; a view must not omit or reinterpret correspondence categories.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-17-01** · `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.Assembly.cs`

Reuse current exact ZIP/category/digest tests. Add an assertion only for a newly approved source category or optional view, not one test per correspondence row.

**AC-17-02** · `src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs` — acceptance/source owner, not an existing automated test

Qualified acceptance records category completeness and usability against the exact reviewed bundle; missing external-channel authority remains a decision, not implemented ingestion.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-17-01…AC-17-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-18"></a>

## Atomic Task 18 — Administrators can discover old unresolved operations

### 1. Objective

**Requirements:** AUD-UI-04. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** None.

**Current:** OperationRecoveryService.ListAsync selects the latest 50 firm operations; Angular pages only those rows, so older uncertain/blocked/dead-letter work can disappear from discovery.

**Target:** A firm-wide authorized administrator can find exact old unresolved work through bounded server filters/cursors and reuse its existing reviewed recovery commands.

**Non-goals:** No raw SQL operator UI, arbitrary replay/drop command, new monitoring dashboard or removal of existing recovery evidence.

**Acceptance:** AC-18-01…AC-18-03 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Operations/OperationRecoveryService.cs` |
| MODIFY | `src/AuditSphereOps.Api/Ui/UiEndpoints.Operations.cs` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/admin/operations.ts` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/admin/operations.spec.ts` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/OperationRecoveryTests.cs` |
| MODIFY | `tests/AuditSphereOps.Api.Tests/OperationRecoverySafetyApiTests.cs` |
| READ-ONLY | `tests/AuditSphereOps.E2E.Tests/AngularOperationsCommandJourneyTests.cs` |

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing ListAsync, operations/commands/state and reviewed command receipts. Preserve master 3ec9c5d's new approved recovery-session evidence panel and hidden approval controls; that change does not solve older-job discovery. PROPOSED bounded list query: supported state filter, optional exact ID, page size and opaque cursor.

**Source:** `AUD-UI-04`, supplied report L730–L744; baseline links retained under A1's evidence limits. [UiEndpoints.Operations.cs:18–27](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/Ui/UiEndpoints.Operations.cs#L18-L27); [operations.ts:54–74,109–117](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/features/admin/operations.ts#L54-L74); [operations.spec.ts](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/features/admin/operations.spec.ts).

**Direct current read:** [src/AuditSphereOps.Ui/src/app/features/admin/operations.ts](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/src/AuditSphereOps.Ui/src/app/features/admin/operations.ts).

### 4. Recipe

1. Reuse firm-wide administrator authorization before every list/lookup and final projection. Validate known disposition filters; exact foreign IDs return the same opaque denial/not-found semantics as current recovery commands.
2. Add server-side keyset pagination ordered by CreatedAt then Id, with a stable first-page upper-bound tuple and a next cursor bound to firm/filter/query version. Apply a hard page maximum consistent with current limits; never fetch the entire backlog for Angular slicing.
3. Allow unresolved-state browsing plus exact operation-ID lookup. Counts remain page/window/filter-specific; exact totals are separate bounded queries only if actually required.
4. Present next/previous or cursor-history navigation while keeping current reviewed command state/preview/receipt flow. Paging cannot reuse assent for another operation; stale list data is never authorization to retry/cancel.
5. Preserve the new recovery-session display behavior. Test discovery under concurrent inserts; exclude newly created rows beyond the captured upper bound until refresh rather than skipping/duplicating older rows.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-18-01** · `tests/AuditSphereOps.Domain.Tests/OperationRecoveryTests.cs`

Seed one old RESULT_UNCERTAIN and more than 50 later completed jobs: filtered cursor discovery reaches it; concurrent inserts do not duplicate/skip the captured page sequence.

**AC-18-02** · `tests/AuditSphereOps.Api.Tests/OperationRecoverySafetyApiTests.cs`

Exact foreign ID and non-firm-wide actor cannot discover protected job identity/payload; current receipt recovery still returns the original operator outcome.

**AC-18-03** · `src/AuditSphereOps.Ui/src/app/features/admin/operations.spec.ts`

Server cursor/filter changes reset operation-specific review, preserve recovery-session evidence, and honestly label counts.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~OperationRecoverySafetyApiTests'

npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/admin/operations.spec.ts'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-18-01…AC-18-03 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-19"></a>

## Atomic Task 19 — Qualified owners approve the first reporting profiles and independent golden fixtures

### 1. Objective

**Requirements:** AUD-FIN-04. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** None.

**Current:** AccountingCapabilityProfile exists, but qualified framework/edition/effectivity/method and independent GOLD-R2R acceptance remain open under T003.

**Target:** A named release profile has approved policy inputs, resource bounds and independently calculated expected fixtures; unsupported profiles remain unavailable.

**Non-goals:** No universal IFRS/ISA/tax certification, activation of every advanced method or forced source-mode migration.

**Boundary:** Qualified method-owner and independent reviewer approvals required by T003/ADR-0018 DEC-04 are absent; no accounting defaults are invented.

**Acceptance:** AC-19-01…AC-19-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| READ-ONLY | `src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Accounting/ClientAccounting/ClientAccountingService.Capabilities.cs` |
| READ-ONLY | `docs/execution/auditsphere-execution-index-task-board-current.md` |
| READ-ONLY | `docs/architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md` |
| CREATE — PROPOSED | `docs/requirements/auditsphere-reporting-profile-acceptance-proposed.md` |

### 3. Anchors

Existing AccountingCapabilityProfile fields include framework, edition, period rule, method, template family and acceptance state. T003 owns methodology/resource/golden acceptance; T014 still has its actual T007/T013 dependencies. Normative M24/M26 DTO names are contracts, not proof of implemented command classes.

**Source:** `AUD-FIN-04`, supplied report L422–L438; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs:102–123](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs#L102-L123); [ClientAccountingService.Capabilities.cs:15–71](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/ClientAccounting/ClientAccountingService.Capabilities.cs#L15-L71); [src/AuditSphereOps.Application/Accounting/StatementLayoutService.cs:13–15,36–40,83,137–171](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/StatementLayoutService.cs#L13-L15).

**Direct current read:** [docs/architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md).

### 4. Recipe

1. Have a qualified methodology owner enumerate the initial entity and group/FX profiles intended for this release. Record framework edition, effective dates, industry limits, early-adoption treatment, currency/rounding, disclosures, cash-flow/classification method and supported ownership cases.
2. Use the existing baseline group profile of one parent/wholly owned subsidiary with aligned periods unless a separately accepted advanced profile is supplied. Preserve advanced implemented code, but do not activate it by assuming control from ownership percentage.
3. Approve source fixtures and independent expected arithmetic/disclosures, including the existing GOLD-R2R set required by T003. Each fixture needs input/source hashes, policy revision, expected result and a reviewer independent from the calculator implementation.
4. Record approved resource/workload bounds and unsupported cases. Values absent from professional evidence remain explicit unresolved decisions; agents must not infer FX rates, control judgments, tax/retention rules or legal framework applicability.
5. Map the signed approval packet back to the existing T003/T014 records. A proposed document remains PROPOSED until actual authority is observed; do not mark a task or capability accepted just because this specification exists.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-19-01** · `docs/requirements/auditsphere-reporting-profile-acceptance-proposed.md` — acceptance/source owner, not an existing automated test

Professional acceptance checks source completeness, independent expected values and reviewer independence. No application test or local full suite is needed for recording a decision.

**AC-19-02** · `src/AuditSphereOps.Application/Accounting/ClientAccounting/ClientAccountingService.Capabilities.cs` — acceptance/source owner, not an existing automated test

Trace each accepted fixture/profile to the existing capability owner and exact required technical prerequisites; no synthetic calculation approves its own expected answer.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

**Only when impacted:** docs: only affected C4 Markdown/naming validators and `git diff --check`. Reuse a suitable current build; do not build twice.

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-19-01…AC-19-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-20"></a>

## Atomic Task 20 — Every enabled reporting output binds approved effective policy revisions

### 1. Objective

**Requirements:** AUD-FIN-04. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** 19.

**Current:** Capability/layout/package paths retain framework-policy strings, but do not consistently resolve an approved effective policy revision and fixture acceptance.

**Target:** Capability acceptance, layout publication and package promotion enforce compatible approved policy revisions and preserve historical as-issued pins.

**Non-goals:** No duplicate AccountingCapabilityProfile, generic rules engine, hidden early adoption or rewriting existing issued artifacts.

**Boundary:** Task 19's genuine approvals are prerequisites; current operational/task-board status cannot substitute.

**Acceptance:** AC-20-01…AC-20-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Domain/Accounting/ReportingPolicyRevision.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/ClientAccounting/ClientAccountingService.Capabilities.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/StatementLayoutService.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Package.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.PresentationPins.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.ClientAccounting.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Profiles.cs` |
| MODIFY — missing assertions only | `tests/AuditSphereOps.Domain.Tests/FinancialStatementPresentationTests.cs` |

**GENERATE — PROPOSED if needed:** `ReportingPolicyRevisionBindings` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add policy revision references and acceptance links with nullable legacy compatibility where needed. Never backfill current approvals into old as-issued records.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing AccountingCapabilityProfile, StatementLayoutService FrameworkPolicyRef, package framework field and independent capability acceptance. PROPOSED ReportingPolicyRevision adds immutable policy/edition/effectivity/method/disclosure/fixture refs; it extends rather than replaces capability records.

**Source:** `AUD-FIN-04`, supplied report L422–L438; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs:102–123](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs#L102-L123); [ClientAccountingService.Capabilities.cs:15–71](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/ClientAccounting/ClientAccountingService.Capabilities.cs#L15-L71); [src/AuditSphereOps.Application/Accounting/StatementLayoutService.cs:13–15,36–40,83,137–171](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/StatementLayoutService.cs#L13-L15).

### 4. Recipe

1. Represent Task 19's approved policies as immutable scoped revisions with exact applicability, source/fixture hashes and independent acceptance references. Keep local construction, professional approval and live capability acceptance as separate states.
2. Resolve policy IDs, not free-text equality, at capability acceptance, layout publication, period/context selection, package preparation, render/pin promotion and finalization. Require compatible entity/group type, dates, framework/edition, method, taxonomy/layout and currency policies.
3. Include policy revisions and fixture acceptance identities in output manifests and review bases. A changed policy requires an explicitly selected new revision and current downstream review, not silent movement to latest.
4. Provide a source-preserving legacy adapter only to display prior string-based records as legacy/unverified. Do not infer historical approval from a string, a calculation hash or an already rendered artifact.
5. Use the existing configuration/API/Angular owners discovered for capability/layout editing; extend their typed responses in-place. Add no independent policy engine or package dependency.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-20-01** · `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Profiles.cs`

Extend existing capability tests for accepted compatible policy, wrong edition/effective date/industry and unapproved fixture. Missing authority blocks publication.

**AC-20-02** · `tests/AuditSphereOps.Domain.Tests/FinancialStatementPresentationTests.cs`

The selected policy revision appears in the exact manifest; a later policy leaves old issued bytes unchanged and stales a newly incompatible candidate.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~ClientAccountingTests&FullyQualifiedName~ApprovedReportingPolicyBinding_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-20-01…AC-20-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-21"></a>

## Atomic Task 21 — Approve the firm's billing-to-ledger accounting treatment

### 1. Objective

**Requirements:** AUD-FIN-03. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** 04.

**Current:** Billing posting/receipts/credits are separate from firm journals. ADR-0018 defines reporting bases but does not approve revenue recognition, advance liabilities, taxes, control accounts or cutover.

**Target:** A qualified firm owner approves exact accounting events and reconciliation treatment before billing is represented as integrated firm books.

**Non-goals:** No assumed accrual revenue, automatic WIP, repricing, bank payment initiation or client-ledger crossover.

**Boundary:** Recognition, control-account, timing, tax/currency and migration treatment require qualified firm accounting approval.

**Acceptance:** AC-21-01…AC-21-01 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| READ-ONLY | `src/AuditSphereOps.Application/Practice/BillingService.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Practice/LedgerService.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Practice/FirmFinanceQuery.cs` |
| READ-ONLY | `docs/architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md` |
| CREATE — PROPOSED | `docs/requirements/auditsphere-firm-billing-ledger-treatment-proposed.md` |

### 3. Anchors

Existing BillingService explicitly leaves firm-ledger creation to another slice; LedgerService owns balanced reviewed postings. This is a disconnected path, not authority to recognize all invoiced amounts immediately as revenue.

**Source:** `AUD-FIN-03`, supplied report L404–L422; baseline links retained under A1's evidence limits. [BillingService.cs:57–60](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/BillingService.cs#L57-L60); [src/AuditSphereOps.Application/Practice/LedgerService.cs:227–309](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/LedgerService.cs#L227-L309); [FirmFinanceQuery.cs:59+](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/FirmFinanceQuery.cs#L59).

### 4. Recipe

1. Approve cash/accrual recognition points, advance/unapplied receipt treatment, control-account mappings, tax/currency scope, received-versus-effective dates, credits/reversals and closed-period corrections. No date is moved to the next open period without explicit policy.
2. Choose the minimal supported integration mode. Recommended initial mode: idempotent source-event journal draft/handoff through current ledger review/posting, with explicit pending/unreconciled states; choose atomic posting only if existing professional approval requirements permit it.
3. Approve reconciliation equations for billing AR, unapplied receipts and ledger control balances. Define adjustments/cutoff state and treatment of pending journal drafts so comparisons do not imply a falsely posted GL.
4. Approve legacy opening/cutover treatment using an observed reconciliation report. Never mass backfill revenue from invoice dates or invent missing source journals.
5. Record worked independent samples for advance, final invoice, partial receipt/allocation, unapplied receipt, credit and reversal, then obtain qualified/independent approval before Task 22.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-21-01** · `docs/requirements/auditsphere-firm-billing-ledger-treatment-proposed.md` — acceptance/source owner, not an existing automated test

Review independent event-by-event debit/credit and reconciliation expectations, including a closed-period correction and failed/pending handoff. This is professional approval, not a software-generated pass.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

**Only when impacted:** docs: only affected C4 Markdown/naming validators and `git diff --check`. Reuse a suitable current build; do not build twice.

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-21-01…AC-21-01 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-22"></a>

## Atomic Task 22 — Billing events have a reconciled idempotent firm-ledger handoff

### 1. Objective

**Requirements:** AUD-FIN-03. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** 21.

**Current:** Billing and firm journals can currently hold independently maintained balances without an exact source-event posting link.

**Target:** Every supported billing accounting event has one explicit controlled ledger disposition and reconciles under the approved firm policy.

**Non-goals:** No duplicate ledger engine, source-triggered autonomous approval, automatic historical revenue recognition or payment execution.

**Boundary:** Task 21's accepted treatment selects the handoff mode and exact journals; no implementing agent chooses accounting policy.

**Acceptance:** AC-22-01…AC-22-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Practice/BillingService.cs` |
| MODIFY | `src/AuditSphereOps.Application/Practice/LedgerService.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Application/Practice/BillingLedgerReconciliation.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Domain/Practice/BillingLedgerEvent.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Practice.cs` |
| MODIFY | `src/AuditSphereOps.Application/Practice/FirmFinanceQuery.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/BillingTests.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/LedgerTests.cs` |

**GENERATE — PROPOSED if needed:** `BillingLedgerSourceEvents` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add source-event uniqueness, scoped journal/receipt links and retained disposition. Legacy opening/cutover journals require separate actual approval; application rollback must not orphan posted links.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing billing source entities/receipts, LedgerService journal/review/posting operations, FirmPostings read models and Task 04 exact command identities. PROPOSED unique source-event identity and retained ledger disposition; no direct insert into posted ledger tables from HTTP.

**Source:** `AUD-FIN-03`, supplied report L404–L422; baseline links retained under A1's evidence limits. [BillingService.cs:57–60](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/BillingService.cs#L57-L60); [src/AuditSphereOps.Application/Practice/LedgerService.cs:227–309](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/LedgerService.cs#L227-L309); [FirmFinanceQuery.cs:59+](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/FirmFinanceQuery.cs#L59).

### 4. Recipe

1. Implement only Task 21's accepted accounting events. Derive journal inputs from server-owned source records and approved policy/account revisions using decimal arithmetic; references include original event and any reversal/correction lineage.
2. Create one source-event disposition under a unique firm/event identity. In controlled-handoff mode, atomically save a reviewed-ready draft link or a durable local pending work item with the billing effect; never label the journal posted until LedgerService actually posts it.
3. Reuse existing independent ledger review, balance checks, period locks and posting receipts. A changed source or policy requires a new reviewed correction rather than modifying posted lines. Replays return the original event disposition and cannot create a second journal.
4. Expose source-to-journal and billing-control reconciliation with explicit pending/unapplied/unreconciled differences. Bind as-of dates and currency/control account selection to approved policy; missing required account mapping blocks accounting publication.
5. Preserve firm/client/group separation and existing automatic-fee draft-only behavior. Handle failure after any stage by exact receipt recovery or an explicit pending/retry state, not by asserting balanced books from billing status.
6. Deploy prospectively with an approved legacy reconciliation/cutover list; preserve historical balances and investigate discrepancies rather than silently backfilling entries.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-22-01** · `tests/AuditSphereOps.Domain.Tests/BillingTests.cs`

Extend one existing billing fixture through accepted event → pending journal → independent posting → reconciled control balance; parameterize advance/final/partial/unapplied/credit where they share the same mechanics.

**AC-22-02** · `tests/AuditSphereOps.Domain.Tests/LedgerTests.cs`

Inject handoff/posting failure and retry; assert single source-event posting, exact debit/credit balance, closed-period refusal and immutable original/correction lineage.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~BillingTests&FullyQualifiedName~BillingLedgerReconciliation_'

# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~LedgerTests&FullyQualifiedName~BillingLedgerReconciliation_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-22-01…AC-22-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-23"></a>

## Atomic Task 23 — Reviewed nil cash-flow sections distinguish no activity from missing data

### 1. Objective

**Requirements:** AUD-FIN-05. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** 20.

**Current:** Required-section and lineage gates reject an ordinary operating-only period: omitted investing/financing fails, while artificial zero rows have no legitimate nonzero source contribution.

**Target:** A section can be evidenced nil for an exact complete source population and approved policy without invented GL rows or weakened nonzero lineage.

**Non-goals:** No fabricated zero GL rows, no treating absence as zero and no relaxed lineage gate for nonzero contributions.

**Boundary:** Approved coverage/classification policy from Tasks 19–20 is required for promotion.

**Acceptance:** AC-23-01…AC-23-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementServiceContracts.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSources.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatementPresentation.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.PresentationPins.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/CashFlowContractTests.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/FinancialStatementPresentationTests.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Domain/Accounting/CashFlowSectionCoverage.cs` |

**GENERATE — PROPOSED if needed:** `CashFlowNilActivityEvidence` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add immutable coverage/assertion revisions and scoped source/review links; old zero rows are not automatically certified nil.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing required OPERATING/INVESTING/FINANCING checks, ResolvedCashFlowMovements, CASH_FLOW_SOURCE_LINEAGE and independently reviewed source/promotion gate. PROPOSED exact section coverage status UNKNOWN | NONZERO_ACTIVITY | REVIEWED_NIL; nil is evidence, not a zero-valued fake movement.

**Source:** `AUD-FIN-05`, supplied report L438–L458; baseline links retained under A1's evidence limits. [FinancialStatements/FinancialStatementService.CashFlowSources.cs:33–34,47–54,80–159](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSources.cs#L33-L34); [FinancialStatementServiceContracts.cs:38–65](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementServiceContracts.cs#L38-L65); [src/AuditSphereOps.Application/Accounting/FinancialStatementPresentation.cs:723–728](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/FinancialStatementPresentation.cs#L723-L728).

### 4. Recipe

1. Add a period/source-population/policy-bound coverage assertion for each required cash-flow section. A preparer identifies the full accepted population and classification coverage; independent review can certify nil only when it establishes no applicable activity.
2. Allow required-section validation to accept either properly resolved nonzero movements or current reviewed nil coverage. Keep missing, incomplete or unreviewed population UNKNOWN and blocking.
3. Reject contradictory nil claims when accepted in-scope source activity exists. Preserve source consumption rules; a nil assertion consumes no invented source line and does not bypass actual nonzero movement provenance.
4. Include coverage identity/revision/reviewer and population digest in presentation/promotion manifests. Later GL acceptance, classification or policy change stales the assertion and affected candidate.
5. Display an explicit reviewed nil section with evidence and no fabricated transaction; preserve old manual movement/artifact history.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-23-01** · `tests/AuditSphereOps.Domain.Tests/CashFlowContractTests.cs`

Use a real operating-only accepted GL fixture: reviewed nil investing/financing passes the complete source-review/promotion path; missing source population and contradictory nil still fail.

**AC-23-02** · `tests/AuditSphereOps.Domain.Tests/FinancialStatementPresentationTests.cs`

Render zero activity distinctly from missing data; changing the population stales coverage. Do not use manually set resolved flags as proof of the full source path.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~CashFlowContractTests&FullyQualifiedName~CashFlowReviewedNilActivity_'

# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~FinancialStatementPresentationTests&FullyQualifiedName~CashFlowReviewedNilActivity_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-23-01…AC-23-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-24"></a>

## Atomic Task 24 — The first approved indirect cash-flow profile reconciles end to end

### 1. Objective

**Requirements:** AUD-FIN-05. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** 20, 23.

**Current:** Current direct cash-contribution infrastructure does not express a complete indirect profit reconciliation; excluding all IsNonCash movements cannot model addbacks and working-capital bridges.

**Target:** An approved indirect profile produces source-backed CFO/CFI/CFF/FX and opening-to-closing cash reconciliation without double consumption or plugs.

**Non-goals:** No deriving arbitrary cash flows from closing TB alone, invented FX, automatic tax policy or all-framework cash-flow engine.

**Boundary:** The independent approved methodology/fixture, not this spec, supplies numerical expected answers and classification policy.

**Acceptance:** AC-24-01…AC-24-03 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementServiceContracts.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSources.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatementPresentation.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatementPresentationRenderer.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/CashFlowContractTests.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/FinancialStatementPresentationTests.cs` |

**GENERATE — PROPOSED if needed:** `IndirectCashFlowSources` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add method/role/bridge/source revision data only where existing schedule storage cannot represent it; preserve legacy schedules and artifacts.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing accepted GL/source allocation and review infrastructure, typed presentation and exact candidate gates. PROPOSED explicit indirect line roles: approved starting-profit reference, noncash reconciliation adjustment, working-capital bridge, investing/financing cash event, FX bridge and separately disclosed noncash transaction.

**Source:** `AUD-FIN-05`, supplied report L438–L458; baseline links retained under A1's evidence limits. [FinancialStatements/FinancialStatementService.CashFlowSources.cs:33–34,47–54,80–159](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSources.cs#L33-L34); [FinancialStatementServiceContracts.cs:38–65](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementServiceContracts.cs#L38-L65); [src/AuditSphereOps.Application/Accounting/FinancialStatementPresentation.cs:723–728](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/FinancialStatementPresentation.cs#L723-L728).

### 4. Recipe

1. Select only Task 19's approved indirect method and starting-profit basis; pin the exact financial package/profit line, source population, policy revision and opening/closing cash membership.
2. Represent addbacks, working-capital changes and actual cash transactions with distinct roles. Derive signs/classifications from approved policy, not generic IsNonCash. Remove acquisition/disposal/reclassification/FX effects from working-capital changes only with exact supported bridge evidence.
3. Reuse source-allocation caps and add explicit consumption rules that prevent the same economic item contributing both as a cash event and as an indirect reconciliation adjustment. Classify interest/dividends/tax only under the approved profile; unsupported input remains blocked.
4. Compute with approved decimal/rounding rules: opening cash + CFO + CFI + CFF + approved FX effect = closing cash. Retain exact source contributions/residual diagnostics; no balancing suspense or arbitrary FX value.
5. Require current independent source/coverage review and Task 23 nil evidence before presentation promotion. Include all method/bridge/source refs in immutable manifests, PDF/Office output and client readback.
6. Preserve any supported direct profile as a separate explicit method; do not silently reinterpret old manual cash schedules as indirect or rewrite issued packages.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-24-01** · `tests/AuditSphereOps.Domain.Tests/CashFlowContractTests.cs`

Run the approved depreciation and working-capital golden example through source resolution/review/promotion; verify no double counting and exact opening/closing reconciliation.

**AC-24-02** · `tests/AuditSphereOps.Domain.Tests/CashFlowContractTests.cs`

Parameterize noncash asset acquisition, unsupported FX/classification and stale population; disclosure is separate from cash and missing facts block.

**AC-24-03** · `tests/AuditSphereOps.Domain.Tests/FinancialStatementPresentationTests.cs`

PDF/Office projection uses the same exact approved method totals and nil/missing semantics; reuse existing deterministic render assertions.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~CashFlowContractTests&FullyQualifiedName~IndirectCashFlowApprovedProfile_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-24-01…AC-24-03 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-25"></a>

## Atomic Task 25 — Annual comparatives can use a separately authorized prior engagement

### 1. Objective

**Requirements:** AUD-FIN-06. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** 20.

**Current:** Build-input options and Package.cs restrict comparatives to the same engagement, while Presentation.cs already allows a separately authorized prior engagement for the same entity.

**Target:** A current annual engagement can pin an eligible prior-year package from the same entity, with separate prior access and exact comparative treatment.

**Non-goals:** No perpetual engagement assumption, widened prior access, automatic latest-package selection or regeneration of historical statements.

**Acceptance:** AC-25-01…AC-25-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialPackageBuildInputsQuery.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Package.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Presentation.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.FinancialPackages.cs` |

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing same-engagement filter and validation, and Presentation.cs's separately authorized prior context. PROPOSED additive exact comparative selection metadata where not already represented; preserve existing AsIssued/Reclassified/RestatedError/PolicyTransition/ProspectiveEstimateChange semantics.

**Source:** `AUD-FIN-06`, supplied report L458–L474; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialPackageBuildInputsQuery.cs:50–55](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialPackageBuildInputsQuery.cs#L50-L55); [FinancialStatementService.Package.cs:109–121](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Package.cs#L109-L121); [FinancialStatementService.Presentation.cs:48–60](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Presentation.cs#L48-L60).

### 4. Recipe

1. Replace same-engagement identity with same legal entity/client plus explicit independent authorization to the selected prior engagement. Filter options by that authorization before projecting metadata; a denied prior package is not enumerated.
2. Validate earlier compatible nonoverlapping period, reporting book/basis, framework-policy edition/effectivity, currency and required exact approved package/composition. Do not broaden client or sibling engagement grants to make the query succeed.
3. Pin the exact prior package revision and explicit comparative treatment. As-issued remains a distinct selectable historical identity; never substitute latest restatement automatically.
4. Recheck both current and prior context under deterministic client/aggregate locks at build/approval. Changes affecting a selected restatement/reclassification stale dependent candidates while old issued outputs remain immutable.
5. Keep current same-engagement cases valid and align query, build, presentation, manifest and decoder behavior on one shared compatibility predicate.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-25-01** · `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.FinancialPackages.cs`

Extend a comparative fixture to two annual engagements/one entity: with both grants it builds and reads; without the prior grant it neither enumerates nor consumes.

**AC-25-02** · `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.FinancialPackages.cs`

Parameterize different entity, overlapping/incompatible period and exact as-issued/restated selection; assert immutable original output and current dependent invalidation.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~ClientAccountingTests&FullyQualifiedName~CrossEngagementAnnualComparative_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-25-01…AC-25-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-26"></a>

## Atomic Task 26 — Group scopes have real reporting-period and compatibility evidence

### 1. Objective

**Requirements:** AUD-FIN-07. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** 20.

**Current:** Consolidation scope stores an arbitrary nonempty PeriodId and caller PeriodBasis text; component actual dates/book/basis/framework are not fully reconciled with a real group context.

**Target:** Every scope and component is pinned to an explicit approved group period/basis/policy and compatible actual component context.

**Non-goals:** No arbitrary client-period adapter, inferred fiscal dates, percentage-only control decision or forced simplification of advanced history.

**Boundary:** Group profile/basis and any alignment bridge require Tasks 19–20's approved methodology.

**Acceptance:** AC-26-01…AC-26-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Domain/Accounting/GroupReportingPeriod.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Scopes.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Components.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationServiceContracts.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Consolidation.cs` |
| MODIFY | `tests/AuditSphereOps.Api.Tests/ConsolidationWorkspaceApiTests.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs` |

**GENERATE — PROPOSED if needed:** `GroupReportingPeriods` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add group reporting period/context and scoped compatibility refs. Leave unverifiable legacy scopes blocked for new approval; preserve all old run evidence.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing ClientGroup/ConsolidationScopeVersion and component package pins; M26 explicitly requires real group dates and forbids borrowing an unrelated client-period GUID. PROPOSED GroupReportingPeriod: FirmId/GroupId, start/end, basis, presentation currency and exact approved policy/context revision.

**Source:** `AUD-FIN-07`, supplied report L474–L492; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Scopes.cs:21,96](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Scopes.cs#L21); [src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Consolidation.cs:22–42](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Consolidation.cs#L22-L42); [ConsolidationService.Components.cs:36–60](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Components.cs#L36-L60).

### 4. Recipe

1. Add the explicit group-period owner unless a current existing type proves the same semantics. Require group-scoped authority, valid dates and supported profile; link scope to the real row via scoped FK.
2. Resolve component period/book/basis/framework/currency/taxonomy from the retained package or accepted external pack, never from caller PeriodBasis labels. Validate at pin submit, component approval, calculation and report.
3. For the first accepted parent/wholly-owned-subsidiary profile require aligned dates and compatible approved policies. Only a separately approved exact alignment bridge can authorize a supported difference; no inferred date proration.
4. Include context/bridge revisions in review basis and manifests. Concurrent group-period or component replacement makes the prior preview stale and cannot quietly change a reviewed calculation.
5. Preserve advanced supported method data and existing membership/cycle safeguards. Keep legacy unresolvable PeriodId values as LEGACY_UNVERIFIED until a human approves a provable mapping; never derive dates from the first component.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-26-01** · `tests/AuditSphereOps.Api.Tests/ConsolidationWorkspaceApiTests.cs`

Aligned actual periods succeed; an arbitrary GUID, FY2025/FY2026 mismatch and wrong basis/policy fail despite identical caller labels.

**AC-26-02** · `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs`

Extend existing real-package fixture with scoped FK/compatibility checks and approved bridge refusal/acceptance as permitted by policy.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~ConsolidationWorkspaceApiTests&FullyQualifiedName~ConsolidationPeriodCompatibility_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-26-01…AC-26-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-27"></a>

## Atomic Task 27 — Approved component pins are immutable and replaced through supersession

### 1. Objective

**Requirements:** AUD-FIN-10. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** 26.

**Current:** Replacing a component in a draft scope deletes the old row even if that component was already independently approved; approved source fields lack complete persisted protection.

**Target:** New reviewed pins supersede old approved pins without deleting or mutating historical identities, decisions or run references.

**Non-goals:** No destructive component replacement, mutable reviewed hash or weakened test merely to permit corruption.

**Acceptance:** AC-27-01…AC-27-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Components.cs` |
| MODIFY | `src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Consolidation.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs` |

**GENERATE — PROPOSED if needed:** `ComponentPinSupersession` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add supersession/current-membership and immutable guards while preserving FKs. Flag already missing history; never reconstruct it from today's package state.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing Remove(oldComponent) replacement and run/translation foreign keys. PROPOSED append-only supersession/replacement decision and current membership pointer with revision fencing; an approved pin's fields themselves remain immutable.

**Source:** `AUD-FIN-10`, supplied report L526–L544; baseline links retained under A1's evidence limits. [ConsolidationService.Components.cs:77–159](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Components.cs#L77-L159); [tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs:394–402](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs#L394-L402).

### 4. Recipe

1. Represent a replacement as a new component pin with predecessor/reason/exact source and independent acceptance. Until the new pin is approved, the previous effective pin is not silently switched to an unapproved source.
2. Protect approved pin identity, hash, source/context and approval fields against update/delete with additive PostgreSQL guards. Store supersession as separate retained evidence; do not update approved rows merely to label them superseded.
3. Switch current membership atomically under group/scope revision fencing and the existing global/client/group lock order. Ensure exactly one effective pin per required member/purpose; concurrent approval/replacement cannot mix sources.
4. Keep every prior run, translation and review FK pointing to the original pin. Old run readback remains historical/stale as appropriate, while a new effective pin requires a new current run.
5. Replace the existing test that corrupts an approved package hash through SaveChanges with a legal new-source/withdrawn-review staleness scenario. Keep a separate direct database mutation test that must now fail.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-27-01** · `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs`

Replace an approved pin in a draft scope: original row/hash/approval and old run remain, new pin requires independent review, and current membership changes once.

**AC-27-02** · `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs`

Direct protected-field update/delete fails in PostgreSQL; deterministic replace/approve race cannot switch reviewed evidence.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~ClientAccountingTests&FullyQualifiedName~ApprovedComponentPinSupersession_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-27-01…AC-27-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-28"></a>

## Atomic Task 28 — Group currentness includes component generation and required professional reviews

### 1. Objective

**Requirements:** AUD-FIN-08. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** 26, 27.

**Current:** Group runs compare immutable package hash/status/currency after a one-time review check; a new accepted source or changed required review can leave an old group result labeled CURRENT_APPROVED.

**Target:** Build, approval, current report, group package and freeze revalidate one coherent minimal component-eligibility manifest without granting raw component access.

**Non-goals:** No raw client access for group roles, continuous rewriting of past runs or transaction spanning rendering/provider I/O.

**Acceptance:** AC-28-01…AC-28-03 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Runs.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Reports.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialPackageReviewService.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Application/Accounting/Consolidation/ComponentEligibilityQuery.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs` |
| MODIFY | `tests/AuditSphereOps.Api.Tests/ConsolidationWorkspaceApiTests.cs` |

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing RequireCurrentAsync has richer checks but requires component roles; do not call it as a group reader and widen grants. PROPOSED trusted internal ComponentEligibilitySnapshot contains source-generation/policy/period/presentation/composition/latest-effective required-review refs and a redacted readiness reason.

**Source:** `AUD-FIN-08`, supplied report L492–L510; baseline links retained under A1's evidence limits. [ConsolidationService.Components.cs:207–209](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Components.cs#L207-L209); [ConsolidationService.Runs.cs:118–124](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Runs.cs#L118-L124); [ConsolidationService.Reports.cs:37–40,66](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Reports.cs#L37-L40).

### 4. Recipe

1. Factor the internal eligibility evaluation from existing package review logic so trusted group code can evaluate pins while the public group query exposes only its permitted minimal summary. Raw statements/artifacts still require separate component authorization.
2. Include accepted input generation, exact package/presentation/composition, policy/context pins and current required review chain in the group manifest. PackageValidated or unchanged calculation hash alone is not professional currentness.
3. At group build/approval acquire a coherent snapshot using established lock order: never lock the group then acquire arbitrary component clients. Sort involved client IDs consistently and audit all affected callers before changing lock acquisition.
4. Recompute/recheck the manifest before approval and current delivery. New accepted TB/GL, review return/withdrawal or pin replacement makes a former result historical/stale and blocks current publication; it does not delete old results.
5. Use the same projection in current report/group package/freeze eligibility and disclose only a redacted stale reason to group-only users. Reaccepting a replacement requires a new exact run, not re-labeling the old run current.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-28-01** · `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs`

Approve a real component/run, accept a new source or later required review return, and assert stale current report/approval without mutating package hash.

**AC-28-02** · `tests/AuditSphereOps.Api.Tests/ConsolidationWorkspaceApiTests.cs`

A group-only actor sees readiness/stale reason but cannot enumerate or download raw component artifacts.

**AC-28-03** · `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs`

Barrier-controlled source acceptance versus group approval yields a coherent accepted manifest or stale refusal, not a mixed snapshot.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~ConsolidationWorkspaceApiTests&FullyQualifiedName~ConsolidationComponentEligibility_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-28-01…AC-28-03 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-29"></a>

## Atomic Task 29 — Intercompany eliminations consume exact source exposure once

### 1. Objective

**Requirements:** AUD-FIN-09. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** 28.

**Current:** IC matches accept caller seller/buyer/matched amounts with no source-line identities, so a fresh reference can reconsume the same economic exposure while still producing a balanced elimination.

**Target:** Every supported match/elimination has exact accepted two-sided source allocations and server-derived available exposure, with explained residuals.

**Non-goals:** No automatic clearing of immaterial mismatches, group writeback to client books, invented exposure or new matching engine.

**Acceptance:** AC-29-01…AC-29-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationServiceContracts.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Intercompany.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Runs.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Journals.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Domain/Accounting/IntercompanySourceAllocation.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Consolidation.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs` |

**GENERATE — PROPOSED if needed:** `IntercompanyExposureConsumption` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add typed immutable source allocations/consumption and scoped indexes/guards. Legacy scalar matches require explicit reviewed bridging or remain unverified; never infer source identity from equal amounts.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing scalar match request, approved member check, reference uniqueness and balanced journal contributions. PROPOSED typed source references identify internal package/external pack or an independently accepted IC schedule, exact line/revision/counterparty/economic role and original/translated amounts.

**Source:** `AUD-FIN-09`, supplied report L510–L526; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationServiceContracts.cs:50–55](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationServiceContracts.cs#L50-L55); [ConsolidationService.Intercompany.cs:14–81](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Intercompany.cs#L14-L81); [ConsolidationService.Runs.cs:283–297](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Runs.cs#L283-L297).

### 4. Recipe

1. Resolve both sides from an accepted source with the required counterparty/economic role. If aggregate component output cannot prove invoice/exposure details, require a reviewed IC schedule linked to the exact component; never synthesize it from supplied scalar totals.
2. Derive source amount and remaining exposure on the server under the current component eligibility manifest. Store original currency, translated amount/rate pin, allocation amount and source identity with exact approved precision.
3. Lock source allocation owners in deterministic order and validate cumulative approved/reserved consumption across matches and elimination journals. A new transaction reference is not new exposure. Support many-to-many partial allocation only with reconciled source totals.
4. Commit match/independent decision and journal consumption atomically at the actual approval boundary. If drafts reserve capacity, define reservation release/expiry explicitly; never silently double consume on approval or replay.
5. Retain explained unmatched residuals and separate reviewed group corrections. Source replacement/currentness changes require reacceptance/new run; prior matches remain historical evidence.
6. Prove baseline investment/equity and receivable/payable eliminations from Task 19's approved fixture. Keep group-only journals out of component client books; no automatic materiality write-off, FX plug or suspense.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-29-01** · `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs`

A second match with a new reference cannot reconsume the same source; concurrent partial matches stay within server-derived availability and retain an explicit residual.

**AC-29-02** · `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs`

Parameterize invented source/taxonomy, stale component and exact replay; accepted many-to-many baseline contributions reconcile once into the run.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~ClientAccountingTests&FullyQualifiedName~IntercompanySourceConsumption_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-29-01…AC-29-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-30"></a>

## Atomic Task 30 — An exact reviewed consolidation run produces a scoped group financial package

### 1. Objective

**Requirements:** AUD-FIN-11. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** 24, 25, 26, 27, 28, 29.

**Current:** Consolidation reports expose reviewed lines, but the entity package builder requires a client/engagement/adjustment-plan context and has no typed group assembly route.

**Target:** A supported approved group profile reaches a reviewed deterministic financial package through the existing artifact/review kernel, with truthful group ownership and lineage.

**Non-goals:** No second renderer/calculator/store, universal advanced-consolidation claim or local-render-as-live-release assertion.

**Boundary:** Dependent group methods, cash-flow/disclosure profiles and independent fixtures must be approved before the package can be accepted.

**Acceptance:** AC-30-01…AC-30-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| CREATE — PROPOSED | `src/AuditSphereOps.Application/Accounting/Consolidation/GroupFinancialPackageWorkspace.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Reports.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Package.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Accounting/FinancialPackageOfficeRenderer.cs` |
| MODIFY | `src/AuditSphereOps.Application/Accounting/FinancialPackageReviewService.cs` |
| CREATE — PROPOSED | `src/AuditSphereOps.Domain/Accounting/GroupFinancialPackage.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Consolidation.cs` |
| CREATE — PROPOSED | `tests/AuditSphereOps.Api.Tests/GroupFinancialPackageApiTests.cs` |

**GENERATE — PROPOSED if needed:** `GroupFinancialPackages` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add typed group-owned package/result/composition refs preserving entity artifacts; retain immutable historical group inputs. No fabricated client/engagement host row.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing reviewed run/perimeter/component/translation/IC/journal contributions and entity artifact, composition/review/hash infrastructure. T047 owns this gap. PROPOSED typed group package input/owner and group-scoped routes in the existing consolidation API family; no fake client engagement.

**Source:** `AUD-FIN-11`, supplied report L544–L555; baseline links retained under A1's evidence limits. [ConsolidationService.Reports.cs:14–66](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Reports.cs#L14-L66); [FinancialStatementService.Package.cs](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Package.cs).

### 4. Recipe

1. Create an immutable typed group input from one exact currently eligible independently reviewed run and its perimeter, policy, period, component pins, translations, alignments, journals and IC source-consumption manifest.
2. Reuse the shared statement/layout/rendering/composition/hash services through the smallest typed group-owner adapter. Do not clone the calculation engine, create an artificial client or make entity foreign keys accept a group GUID.
3. Implement group-scoped preparation/read/review/promotion/export with current independent group reviewer authority. A group grant never becomes raw component artifact access; contribution drill-down remains at permitted group detail.
4. Include only complete approved group statement/cash/equity/note profiles. If required consequential treatment is unsupported, return explicit readiness blockers, not a apparently complete package with missing statements.
5. Revalidate Task 28 currentness at preparation, promotion, release/freeze handoff and any current download claim. Render outside locks from captured immutable inputs, then fence publication against the exact manifest.
6. Preserve entity package behavior and issued artifacts. Live signing/checkpoint/archive readiness remains Tasks 32–33/43; a local deterministic render is not final release acceptance.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-30-01** · `tests/AuditSphereOps.Api.Tests/GroupFinancialPackageApiTests.cs`

PROPOSED integration fixture built from existing real component packages: baseline reviewed run → exact group package; manifest and totals reconcile all contributions.

**AC-30-02** · `tests/AuditSphereOps.Api.Tests/GroupFinancialPackageApiTests.cs`

Parameterize stale component/review/period, wrong group and denied raw component read; repeated same-input assembly yields the same permitted artifact identity/bytes, changed input yields a new result.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~GroupFinancialPackageApiTests&FullyQualifiedName~ReviewedGroupFinancialPackage_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-30-01…AC-30-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-31"></a>

## Atomic Task 31 — Success contracts describe real API bodies and fail on incompatible decoder changes

### 1. Objective

**Requirements:** AUD-UI-05. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_CONTEXT`. **Dependencies:** 04, 07, 18.

**Current:** The emitted OpenAPI has many description-only 200 responses; concrete portal documents, operations and portal accounting package responses lack schemas while clients maintain their own shapes.

**Target:** Changed high-risk endpoints expose exact named success/receipt schemas and client compatibility checks; remaining JSON success coverage is closed incrementally from an explicit census, not guessed DTOs.

**Non-goals:** No fake body schemas, arbitrary 200-count goal, TypeScript generation mandate or assertion that route parity proves DTO compatibility.

**Boundary:** Known high-risk endpoint work is specified. Exact residual endpoint/DTO census was not fully retrieved; an agent must enumerate it from the current local contract and endpoint owners before assigning a no-guess all-endpoint implementation slice.

**Acceptance:** AC-31-01…AC-31-03 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Api/Contracts/ApiContract.cs` |
| MODIFY | `src/AuditSphereOps.Api/Ui/UiEndpoints.Finance.cs` |
| MODIFY | `src/AuditSphereOps.Api/Ui/UiEndpoints.Portal.cs` |
| MODIFY | `src/AuditSphereOps.Api/Ui/UiEndpoints.Operations.cs` |
| MODIFY | `tests/AuditSphereOps.Api.Tests/OpenApiContractTests.cs` |
| READ-ONLY | `scripts/contracts/verify-openapi.sh` |
| GENERATE | `contracts/auditsphere-openapi.json` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/portal/documents.spec.ts` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/admin/operations.spec.ts` |
| MODIFY | `src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts` |

### 3. Anchors

Existing ApiContract/JSON emitter/drift gate, /api/ui/portal/documents, /api/ui/operations and /api/ui/portal/accounting/packages/{id}. Task 04/07/18 own their changed runtime DTOs. The report's entire-endpoint counts are baseline measurements, not a newly recomputed census. The large emitted file could not be retrieved in full in this session.

**Source:** `AUD-UI-05`, supplied report L744–L766; baseline links retained under A1's evidence limits. [ApiContract.cs:10–16](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/Contracts/ApiContract.cs#L10-L16); [portal/documents.ts:9–12](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/features/portal/documents.ts#L9-L12); [admin/operations.ts:13–14](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Ui/src/app/features/admin/operations.ts#L13-L14).

### 4. Recipe

1. Start with these exact high-risk owners and every endpoint changed by this specification. Inspect the actual runtime projection/envelope, then reuse or define a named record and explicit Produces<T>/typed result metadata. Preserve property naming, string money/revisions, nullability, nested shapes and current error/status behavior.
2. Extend OpenApiContractTests with a machine-derived body-bearing JSON success census. Count all successful response codes, not just 200; require truthful file/no-content/redirect declarations and explicit justified exceptions. Do not synthesize fake schemas for bodyless routes.
3. Adopt a nonregressing exact endpoint allowlist for existing omissions during incremental migration. New/changed protected endpoints cannot join that allowlist; remove each omission when its real shape is declared. A legacy allowance is remaining work, not closure.
4. Use existing API fixtures to supply real response bodies to corresponding client decoder tests. Malformed/over-bound/identity-mismatched financial or approval receipts stay unknown/refused; HTTP success alone is not a committed-outcome proof.
5. For the residual census, group actual endpoint owners by capability and apply the same bounded change once per response family. Enumerate exact remaining Map* source/DTO/decoder paths before assigning that residual slice; no invented DTO list is supplied here. Keep this residual completion BLOCKED_CONTEXT until that census is available.
6. Retain the existing emitter and drift command; add no code generator or second contract artifact. Full closure requires no unexplained body-bearing success omissions in the accepted release profile, including all methods/statuses, plus decoder compatibility evidence.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-31-01** · `tests/AuditSphereOps.Api.Tests/OpenApiContractTests.cs`

Schema exists for each migrated real body and nested DTO; removing/changing a required protected member fails the family contract assertion. File/no-content exceptions remain accurate.

**AC-31-02** · `src/AuditSphereOps.Ui/src/app/features/portal/documents.spec.ts`

A real API fixture decodes; parameterized malformed/over-bound/identity-mismatched receipts cannot show committed success.

**AC-31-03** · `src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts`

Reuse Task 05's outcome decoder test instead of duplicating it at every route.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~OpenApiContractTests&FullyQualifiedName~JsonSuccessContractCoverage_'

npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/portal/documents.spec.ts'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-31-01…AC-31-03 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-32"></a>

## Atomic Task 32 — Live release uses an independently controlled checkpoint provider

### 1. Objective

**Requirements:** AUD-PLAT-02. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_EXTERNAL`. **Dependencies:** None.

**Current:** GraphReleaseCheckpointStore is a live-provider-not-approved stub; API rejects general live external effects and API/Worker compose a local checkpoint store.

**Target:** A live release candidate can progress only through an approved independent destination/identity, exact write/readback/reconciliation and fail-closed host composition.

**Non-goals:** No disabling startup safety to force deployment, local receipt as production evidence, new storage architecture or administrator-proof SharePoint claim.

**Boundary:** Approved custody destination/identity and independent live acceptance are essential rollout gates. If the existing approved design cannot prove independent custody, keep live mode blocked and obtain a decision rather than choosing a new provider.

**Acceptance:** AC-32-01…AC-32-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Infrastructure/Providers/GraphReleaseCheckpointStore.cs` |
| MODIFY | `src/AuditSphereOps.Api/ApiHost.Providers.cs` |
| MODIFY | `src/AuditSphereOps.Worker/Program.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Completion/ReleaseSafetyOptions.cs` |
| CREATE — PROPOSED | `tests/AuditSphereOps.Domain.Tests/LiveReleaseCheckpointTests.cs` |

### 3. Anchors

Existing IReleaseCheckpointStore, ReleaseCheckpointHandler, local append-only provider, durable dispatcher/reconciliation and release candidate/manifest identity. The provider name does not itself approve its custody destination. PROPOSED live composition using the existing approved checkpoint boundary only.

**Source:** `AUD-PLAT-02`, supplied report L807–L821; baseline links retained under A1's evidence limits. [src/AuditSphereOps.Infrastructure/Providers/GraphReleaseCheckpointStore.cs:6–21](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Infrastructure/Providers/GraphReleaseCheckpointStore.cs#L6-L21); [src/AuditSphereOps.Api/ApiHost.Providers.cs:92–99](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/ApiHost.Providers.cs#L92-L99); [src/AuditSphereOps.Worker/Program.cs:67–99](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Worker/Program.cs#L67-L99).

### 4. Recipe

1. Confirm the approved independently controlled checkpoint destination, permitted identity, exact site/resource boundary and append-only custody controls. Keep it independent from the staff Full-Control PBC working site and the secondary archive copy; provider administrator authority must be described accurately.
2. Implement existing store write/verify/probe operations using immutable candidate/manifest identities and content-addressed/idempotent intent. Enforce no-overwrite, exact destination, complete digest/length and current capability evidence; never accept a local/simulated receipt for live release.
3. Reconcile a lost write response using original intent/destination and exact readback before retry. Wrong bytes, duplicate identity with different content, revoked permission or unverifiable custody blocks/quarantines the candidate without hiding UNKNOWN.
4. Select the live provider only in an explicitly permitted host/worker composition. Replace the unconditional live startup rejection only after all that mode's required dependencies pass their existing guards; keep default local simulation and every unrelated external gate disabled.
5. Perform provider I/O outside the local publication transaction; commit the exact verified receipt under candidate revision, actor authorization and manifest fences. No Boolean feature flag alone can enable a release.
6. Keep historical issued records immutable/readable and distinguish local validation from accepted live custody. Do not add a KMS, new storage provider or eSignature vendor without a separately approved architecture decision.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-32-01** · `tests/AuditSphereOps.Domain.Tests/LiveReleaseCheckpointTests.cs`

PROPOSED small provider/composition fixture reusing current release/operation seeds: exact readback succeeds; mismatched destination/digest/candidate and local provider in live mode fail closed.

**AC-32-02** · `tests/AuditSphereOps.Domain.Tests/LiveReleaseCheckpointTests.cs`

Lost write response probes/reconciles the original identity without duplicate overwrite. Current actor/candidate revision change prevents local release publication.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~LiveReleaseCheckpointTests&FullyQualifiedName~LiveCheckpointOriginalOutcome_'
```

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-32-01…AC-32-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-33"></a>

## Atomic Task 33 — The secondary SharePoint archive copy is hash- and permission-verified

### 1. Objective

**Requirements:** AUD-PLAT-03. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_EXTERNAL`. **Dependencies:** 08, 32.

**Current:** Local freeze records BLOCKED_EXTERNAL and no current path assigns observed external read-only state; the selected separate read-only archive library is approved but not implemented.

**Target:** An exact released bundle is copied through the existing isolated client-sites capability and becomes OBSERVED only after every hash and expected staff grant is read back.

**Non-goals:** No replacement of authoritative release storage, tenant-wide new permission, mutable archived copy or conflation of provider and local protection.

**Boundary:** ADR-0009 selects the design; only actual exact-site hash/grant/staff-denial evidence closes the provider gate.

**Acceptance:** AC-33-01…AC-33-03 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Records/FileFreezeService.cs` |
| MODIFY | `src/AuditSphereOps.Application/Documents/ClientSharePointSites.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Providers/SharePointClientSiteProvider.cs` |
| MODIFY | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.ClientSharePointSites.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/ClientSharePointSitesTests.cs` |
| MODIFY | `tests/AuditSphereOps.Domain.Tests/SharePointClientSiteProviderTests.cs` |

**GENERATE — PROPOSED if needed:** `SharePointArchiveCopyReceipts` in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`; record actual generated filenames in the PR. Add durable archive-copy receipt/state/target refs if absent; never backfill OBSERVED from historical local freezes.

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Approved ADR-0009 secondary read-only library, independent authoritative release store, existing isolated client-sites worker and FileFreezeService. PROPOSED immutable archive-operation receipt/state evidence; permitted states remain NOT_REQUESTED/REQUESTED/OBSERVED/BLOCKED_EXTERNAL.

**Source:** `AUD-PLAT-03`, supplied report L821–L835; baseline links retained under A1's evidence limits. [FileFreezeService.cs:26–72](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Records/FileFreezeService.cs#L26-L72); [EngagementLifecycleQuery.cs:367](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs#L367); [SharePointClientSiteProvider.cs](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Infrastructure/Providers/SharePointClientSiteProvider.cs).

**Direct current read:** [docs/architecture/adr/auditsphere-architecture-adr-0009-archive-authority-release-store-approved.md](https://github.com/nirzaf/AuditSphere/blob/3ec9c5d3d0cea098a190d4fdd8618120bd11aebd/docs/architecture/adr/auditsphere-architecture-adr-0009-archive-authority-release-store-approved.md).

### 4. Recipe

1. Capture the exact immutable released bundle/manifest and target archive library under the existing dedicated client-sites capability; never copy live mutable working folders as issued evidence.
2. Create/copy through a durable idempotent operation that retains original target/item identities. Verify all full file hashes, manifest coverage and expected library grants after reconciliation; partial copy or unknown permission state remains blocked.
3. Publish OBSERVED only from matching hash and permission readback evidence. Staff receive read-only archive access, but site-collection/tenant administrators retain authority; do not claim write-once/admin-proof storage.
4. Local scheduled/early freeze remains effective during provider outages. Distinguish final-release client upload freeze from the later staff archive freeze; no provider failure reopens protected local work.
5. Replays neither overwrite issued bytes nor duplicate archive sets. Additions after freeze are separately authorized traceable supplementary records, not in-place corrections; detect unexpected changed provider copies on supported readback.
6. Keep the privileged client-sites certificate out of API/general/PBC worker mounts. No new Microsoft permission, Purview or antivirus is introduced.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-33-01** · `tests/AuditSphereOps.Domain.Tests/SharePointClientSiteProviderTests.cs`

Use the existing provider fixture for exact copy/readback, duplicate operation, partial copy, changed hash and mismatched grants; only complete matching evidence yields OBSERVED.

**AC-33-02** · `tests/AuditSphereOps.Domain.Tests/ClientSharePointSitesTests.cs`

Local freeze stays committed during provider outage/UNKNOWN and replay preserves original archive identity.

**AC-33-03** · `src/AuditSphereOps.Infrastructure/Providers/SharePointClientSiteProvider.cs` — acceptance/source owner, not an existing automated test

After explicit authorization, verify real exact-library staff write denial and accepted read access; retain reviewer/evidence separately from synthetic tests.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~SharePointClientSiteProviderTests&FullyQualifiedName~SharePointArchiveCopyEvidence_'

# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~ClientSharePointSitesTests&FullyQualifiedName~SharePointArchiveCopyEvidence_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`; changed persistence: C4 targeted current Release graph, EF model-drift check and disposable-PostgreSQL constraint/rollback assertion. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-33-01…AC-33-03 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-34"></a>

## Atomic Task 34 — External document history and checkout promises have an approved supported boundary

### 1. Objective

**Requirements:** AUD-PLAT-03. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** None.

**Current:** EngagementActivityQuery explicitly excludes direct SharePoint edits; local application locks do not prove an Office/SharePoint checkout lock.

**Target:** Product/operator wording either accurately limits history/locking to application-managed events or an explicitly approved scoped provider observation/locking capability is implemented and accepted.

**Non-goals:** No generic mailbox/event ingestion, new broad tenant scopes, unverified checkout implementation or local-lock-as-provider-lock claim.

**Boundary:** Supported external edit-history/checkout scope and capability are not established. A wider implementation remains BLOCKED_CONTEXT until its exact provider contract is verified; live acceptance is BLOCKED_EXTERNAL.

**Acceptance:** AC-34-01…AC-34-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| READ-ONLY | `src/AuditSphereOps.Application/Records/EngagementActivityQuery.cs` |
| READ-ONLY | `src/AuditSphereOps.Infrastructure/Providers/SharePointClientSiteProvider.cs` |
| READ-ONLY | `src/AuditSphereOps.Infrastructure/Providers/GraphSelectedSiteDrive.cs` |
| CREATE — PROPOSED | `docs/requirements/auditsphere-external-document-observation-scope-proposed.md` |

### 3. Anchors

Existing explicit unobserved-direct-edit disclosure and local document locks. No provider webhook/delta/checkout API is asserted as implemented or authorized by this spec.

**Source:** `AUD-PLAT-03`, supplied report L821–L835; baseline links retained under A1's evidence limits. [FileFreezeService.cs:26–72](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Records/FileFreezeService.cs#L26-L72); [EngagementLifecycleQuery.cs:367](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs#L367); [SharePointClientSiteProvider.cs](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Infrastructure/Providers/SharePointClientSiteProvider.cs).

### 4. Recipe

1. Have the product/professional owner decide whether the release promises all direct external edit history/provider checkout, or only application-managed actions. Cite the accepted requirement and exact boundary.
2. If the narrower boundary is accepted, update only misleading affected UI/operator wording and retain the unobserved-state disclosure. Zero provider code is required; do not hide the approved exclusion.
3. If external observation is mandatory, first verify supported provider APIs, selected-resource consent, event coverage and actual document-lock semantics. Produce the exact capability contract under existing provider owners before coding; unavailable semantics remain an explicit blocker.
4. Use durable checkpointed observation, duplicate-safe retained events and scoped identity; never present an incomplete provider history as exhaustive. A local database lock cannot be described as an external editor lock.
5. Obtain a real authorized external edit/checkout/release and permission-loss acceptance case before enabling the wider promise.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-34-01** · `docs/requirements/auditsphere-external-document-observation-scope-proposed.md` — acceptance/source owner, not an existing automated test

Owner decision states the actual supported events/actors and locking limitations; application-only wording must not assert unobserved provider behavior.

**AC-34-02** · `src/AuditSphereOps.Infrastructure/Providers/GraphSelectedSiteDrive.cs` — acceptance/source owner, not an existing automated test

Provider implementation tests are conditional on an approved verified contract; reuse current fake adapters, then independently observe the exact real-provider case.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

**Only when impacted:** docs: only affected C4 Markdown/naming validators and `git diff --check`. Reuse a suitable current build; do not build twice.

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-34-01…AC-34-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-35"></a>

## Atomic Task 35 — Release promotion requires complete successful verification of its exact source

### 1. Objective

**Requirements:** AUD-PLAT-04. **Current state:** `PARTIAL`. **Gate:** `SPECIFIED`. **Dependencies:** None.

**Current:** release.yml gates only on ci.yml; full release-verification already runs nightly/manual on one resolved SHA, but is not automatically enforced by the release path.

**Target:** The selected release entry point refuses promotion without trusted successful full-regression evidence for the exact immutable source/artifacts, while local feedback and PR CI remain fast.

**Non-goals:** No local full suite, expanded full PR matrix, test skipping, provider/deployment dispatch during specification or green-CI-as-merge authorization.

**Acceptance:** AC-35-01…AC-35-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `.github/workflows/release.yml` |
| MODIFY | `.github/workflows/release-verification.yml` |
| CREATE — PROPOSED | `scripts/release/verify-release-source.py` |
| CREATE — PROPOSED | `scripts/release/test_verify_release_source.py` |
| READ-ONLY | `.github/workflows/ci.yml` |

### 3. Anchors

Existing fast ci.yml, SHA-resolving release-verification.yml with release-gate, and release.yml verify-ci-passed. PROPOSED aggregate release evidence JSON generated as a CI artifact, plus a stdlib-only verifier; no new service or test framework.

**Source:** `AUD-PLAT-04`, supplied report L835–L849; baseline links retained under A1's evidence limits. [.github/workflows/ci.yml](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/.github/workflows/ci.yml); [.github/workflows/release-verification.yml](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/.github/workflows/release-verification.yml); [.github/workflows/release.yml:87–109](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/.github/workflows/release.yml#L87-L109).

### 4. Recipe

1. Preserve nightly/manual full-suite scheduling and the current fast PR gate. Do not run full suites locally, on every file edit or automatically for every small PR merely to enable this control.
2. Have the full workflow publish bounded aggregate evidence only after its existing required jobs succeed: resolved source SHA, workflow definition/source, run ID/attempt, required cohort outcomes and evidence artifact identities. Distinguish the requested ref and actual resolved commit from a dispatch run's metadata head_sha.
3. At release, load trusted workflow-run status and retained aggregate artifact; require completed success for the designated workflow and exact resolved candidate SHA, required successful jobs and nonempty real execution evidence. Reject wrong SHA, failed/cancelled/timed-out/skipped/incomplete results, missing/expired artifact, malformed record and untrusted workflow provenance.
4. Bind build image/artifact digests and migration image to that same source. A nightly run for an earlier SHA, a passing Angular subset or a dispatch branch's head is insufficient. If the gate cannot establish the identity, fail closed before protected release actions.
5. Use existing gh/jq/workflow utilities or a small Python stdlib verifier; tests consume saved responses and files, not live workflow dispatches. Preserve environment approval and action-specific owner authorization.
6. Do not make full regression a required PR check or change branch protection as an incidental pipeline edit. Such policy changes require explicit owner approval; this task's automated requirement is release promotion.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-35-01** · `scripts/release/test_verify_release_source.py`

PROPOSED parameterized stdlib tests with saved fixtures: correct resolved SHA + all-required success is accepted; wrong resolved SHA, dispatch-head mismatch, failed/cancelled/skipped/pending and missing evidence are rejected.

**AC-35-02** · `scripts/release/test_verify_release_source.py`

Bind run ID/attempt and trusted workflow source; a successful unrelated job/artifact or stale candidate evidence cannot satisfy the gate.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED test file; saved fixtures only, no live dispatch.
python3 -m unittest discover -s scripts/release -p 'test_verify_release_source.py'
```

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-35-01…AC-35-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-36"></a>

## Atomic Task 36 — The selected deployment runs its new migration image and exact execution

### 1. Objective

**Requirements:** AUD-PLAT-04. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_DECISION`. **Dependencies:** 35.

**Current:** Azure release starts an already configured migration job before updating its image and polls the first listed execution; the Hostinger workflow in scope builds images, not proof of that Azure deployment.

**Target:** The approved hosting route updates the intended immutable migration job before starting it, waits for that exact execution, and gates API/worker rollout on schema readiness.

**Non-goals:** No forced move to Azure, second production platform, polling unrelated execution, production dispatch or automatic database downgrade.

**Boundary:** The actual production hosting route is not selected by the report. Azure-only work is conditional; a different route needs its real deployment entry-point evidence before commands are specified.

**Acceptance:** AC-36-01…AC-36-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY — Azure only | `.github/workflows/release.yml` |
| MODIFY — Azure only | `infra/modules/migration-job.bicep` |
| READ-ONLY | `.github/workflows/hostinger-acceptance-images.yml` |
| CREATE — PROPOSED | `scripts/release/test_migration_sequence.py` |

### 3. Anchors

Existing Azure migration step precedes Bicep image deployment, and polls execution list position. PROPOSED two-stage migration/job then runtime rollout for the selected Azure route; no Hostinger deployment command is invented.

**Source:** `AUD-PLAT-04`, supplied report L835–L849; baseline links retained under A1's evidence limits. [.github/workflows/ci.yml](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/.github/workflows/ci.yml); [.github/workflows/release-verification.yml](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/.github/workflows/release-verification.yml); [.github/workflows/release.yml:87–109](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/.github/workflows/release.yml#L87-L109).

### 4. Recipe

1. Obtain the actual production-route decision. If Azure is not supported, mark this Azure-specific repair NOT_APPLICABLE and keep that release route disabled/documented; still require Task 35 at the chosen route's real promotion entry point.
2. For Azure, provision/update the migration job to the exact new source-pinned image/digest before starting it, including first deployment. Resolve the existing Bicep environment/module graph; separate migration resource preparation from routing incompatible API/worker revisions.
3. Capture the execution ID returned by the start operation. Poll/get that exact execution with bounded timeout and terminal outcomes; never use [0] from an execution list. Verify intended image and schema readiness after success.
4. Only then roll out compatible API/worker revisions, perform the existing health/read-only smoke checks and require owner-approved promotion. Failure/timeout/unknown execution keeps new traffic and incompatible workers blocked.
5. Record previous/current artifact identities and schema compatibility. Application rollback does not reverse data-bearing migrations; forward repair or a separately rehearsed restore is explicit.
6. Validate command return schemas and selected hosting prerequisites from current official/provider documentation before execution. No dual-platform build-out is authorized merely because both assets exist.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-36-01** · `scripts/release/test_migration_sequence.py`

PROPOSED stdlib fixture/sequence checks: job image update precedes start; polling uses the returned exact execution; unrelated successful run, failure, timeout and wrong image cannot promote.

**AC-36-02** · `infra/modules/migration-job.bicep` — acceptance/source owner, not an existing automated test

Authorized isolated rehearsal covers first deployment and upgrade with the intended new migration image, API/worker order and schema-compatible rollback.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED test file; saved fixtures only, no live dispatch.
python3 -m unittest discover -s scripts/release -p 'test_migration_sequence.py'
```

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-36-01…AC-36-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-37"></a>

## Atomic Task 37 — Engagement activity reads are bounded before materialization

### 1. Objective

**Requirements:** AUD-PLAT-05. **Current state:** `PARTIAL`. **Gate:** `BLOCKED_CONTEXT`. **Dependencies:** None.

**Current:** EngagementActivityQuery loads multiple full engagement histories and sorts them in memory, leaving a documented production-volume risk.

**Target:** Activity paging reads a bounded consistent event window without materializing every source history or leaking cross-scope content.

**Non-goals:** No new event store, global activity scan, unbounded client-side pagination or invented provider event coverage.

**Boundary:** The source query is evidenced, but exact current API/UI caller paths and approved capacity thresholds must be resolved before widening the file list. This is a bounded caller trace, not permission to invent a new endpoint.

**Acceptance:** AC-37-01…AC-37-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY | `src/AuditSphereOps.Application/Records/EngagementActivityQuery.cs` |
| CREATE — PROPOSED | `tests/AuditSphereOps.Domain.Tests/EngagementActivityPagingTests.cs` |

**GENERATE when HTTP shape changes:** `contracts/auditsphere-openapi.json` through the existing emitter; update only affected client decoders.

### 3. Anchors

Existing scoped activity source queries at lines 43–79 of the report-linked file. PROPOSED exact stable activity cursor (timestamp, source discriminator, record ID, initial upper bound, query version); no new activity event store.

**Source:** `AUD-PLAT-05`, supplied report L849–L866; baseline links retained under A1's evidence limits. [ApiHost.Observability.cs:42–64](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/ApiHost.Observability.cs#L42-L64); [Worker/Program.cs:25–37](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Worker/Program.cs#L25-L37); [AuditDiagnostics.cs:20–47](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Diagnostics/AuditDiagnostics.cs#L20-L47).

### 4. Recipe

1. Read the current query DTO and actual API/Angular callers, then extend only those owners with bounded page size, cursor and next-page metadata. The report does not name all caller paths; record them before changing the contract.
2. Apply scope and cursor predicates and limit-plus-one to each existing source before ToList/ToArray. Use a bounded server union or bounded k-way merge; no full-history materialization hidden behind a paged DTO.
3. Use one deterministic global ordering with source-type tie-breaker and ID, captured upper-bound semantics and a cursor bound to actor-visible scope/filter. New later events appear on refresh instead of destabilizing ongoing paging.
4. Preserve actual event provenance and the direct-SharePoint-edit unobserved disclaimer. Counts must be page/window-specific unless a bounded explicit total is separately implemented.
5. Follow the approved workload/resource limits from the capacity packet; add an index only where the query plan and fixture demonstrate need. Do not introduce another event sourcing or telemetry system.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-37-01** · `tests/AuditSphereOps.Domain.Tests/EngagementActivityPagingTests.cs`

PROPOSED fixture with more than one page across multiple same-timestamp event sources: stable ordering, no duplicate/skip, no cross-client projection and bounded materialization.

**AC-37-02** · `tests/AuditSphereOps.Domain.Tests/EngagementActivityPagingTests.cs`

Concurrent new events respect the captured upper bound; revoked access and malformed cursor cannot return old protected data.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

```bash
# NOT RUN — SPEC_ONLY
# PROPOSED regression prefix; use the exact existing method name instead when extending it.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~EngagementActivityPagingTests&FullyQualifiedName~EngagementActivityBoundedPaging_'
```

**Only when impacted:** HTTP shape: C4 targeted Release emitter build/regeneration and `bash scripts/contracts/verify-openapi.sh`. Reuse a suitable current build; do not build twice.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-37-01…AC-37-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-38"></a>

## Atomic Task 38 — Resolve native close/opening and accepted reporting-source handoff

### 1. Objective

**Requirements:** AUD-PLAT-05. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_CONTEXT`. **Dependencies:** None.

**Current:** Native journals/invoices/credits/settlement GL/TB projections exist. The report did not establish native close → next opening → sealed accepted R2R source → reviewed entity package.

**Target:** The named BOOKKEEPING release profile either proves that exact two-period route or records an approved exclusion and scopes only a demonstrated missing handoff.

**Non-goals:** No second ledger, silent external-to-native cutover, tax filing, payment initiation or inferred annual-close completion.

**Boundary:** The report intentionally did not verify this end-to-end route or mandate it for every release profile. Exact missing handoff cannot be specified as implemented or invented from the operational GL view.

**Acceptance:** AC-38-01…AC-38-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| READ-ONLY | `src/AuditSphereOps.Application/Accounting/ClientAccounting/ClientAccountingService.Profiles.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Accounting/ClientOperationalGeneralLedgerWorkspace.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Accounting/AccountingSourceAcceptanceService.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Package.cs` |
| MODIFY — evidence only | `docs/execution/auditsphere-execution-current-slice.md` |

### 3. Anchors

Existing accepted BOOKKEEPING profile, exact native posted-sequence projection and reviewed opening evidence. The source report explicitly classifies this boundary as partial/unverified, not a confirmed missing ledger.

**Source:** `AUD-PLAT-05`, supplied report L849–L866; baseline links retained under A1's evidence limits. [ApiHost.Observability.cs:42–64](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/ApiHost.Observability.cs#L42-L64); [Worker/Program.cs:25–37](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Worker/Program.cs#L25-L37); [AuditDiagnostics.cs:20–47](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Diagnostics/AuditDiagnostics.cs#L20-L47).

### 4. Recipe

1. Confirm whether this route is an exit criterion of the selected native release profile. If excluded for this release, retain the explicit approved limitation; do not advertise complete annual bookkeeping closure.
2. Trace one entity/two periods from approved opening through balanced native sales/purchase/credit/receipt/payment records, one reviewed correction, period close, reviewed next opening, accepted sealed reporting snapshot and reviewed package.
3. Identify the exact existing source-kind/close/opening/acceptance contracts and all owner paths before implementation. An operational query result or exported CSV alone is not an accepted sealed accounting source.
4. If the route exists, reuse one targeted existing fixture and record its exact current CI evidence with zero new ledger code. If a handoff is missing, record its precise transaction, snapshot, posting-sequence high-watermark, source acceptance and review contracts as a bounded amendment before coding.
5. Preserve external-source periods/cutovers, double-entry totals and original posting lineage. Never silently change source mode, derive next opening from unapproved mutable totals or duplicate a posting to make the snapshot balance.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-38-01** · `src/AuditSphereOps.Application/Accounting/ClientOperationalGeneralLedgerWorkspace.cs` — acceptance/source owner, not an existing automated test

Acceptance scenario: one entity/two periods with approved opening, native movements and a correction; next opening reconciles exactly and the sealed source/package retains the same posted sequence.

**AC-38-02** · `src/AuditSphereOps.Application/Accounting/AccountingSourceAcceptanceService.cs` — acceptance/source owner, not an existing automated test

Old/changed source, wrong client, unapproved close/opening and replay cannot create a falsely accepted snapshot or duplicate native movement. Exact test file is BLOCKED_CONTEXT until the real route is traced.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-38-01…AC-38-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-39"></a>

## Atomic Task 39 — Current source/action evidence—not route counts—governs parity and retirement

### 1. Objective

**Requirements:** AUD-UI-06. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_EXTERNAL`. **Dependencies:** 01, 02, 05, 06, 07, 11, 14, 18.

**Current:** Migration readiness remains NOT_READY; one Operations parsed-action inventory row is stale and other source/action/supporting-file rows include unreviewed or unexecuted behavior.

**Target:** Every mandatory action in the selected acceptance scope has an exact current native owner and evidence or an approved retirement; no missing runtime evidence is mislabeled as a missing feature.

**Non-goals:** No Web deletion, route-count-as-parity claim, historical passes as current evidence or mass test generation.

**Boundary:** Mandatory source/action, human accessibility/locale and operational rollback evidence remain unverified. Owner approval is required before retirement.

**Acceptance:** AC-39-01…AC-39-03 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| GENERATE | `docs/migration/auditsphere-migration-blazor-retirement-inventory.md` |
| MODIFY | `docs/migration/auditsphere-migration-blazor-feature-parity.md` |
| MODIFY | `docs/migration/auditsphere-migration-blazor-test-parity.md` |
| MODIFY | `docs/migration/auditsphere-migration-blazor-removal-readiness.md` |
| MODIFY | `docs/architecture/auditsphere-architecture-code-map.md` |
| READ-ONLY | `scripts/migration/generate-blazor-retirement-inventory.py` |
| READ-ONLY | `scripts/ui/inventory.py` |

### 3. Anchors

Existing source/action and supporting-file registers, generator, source inventory, stopped retirement gate and current 3ec9c5d Operations evidence change. Baseline 71 PARTIAL rows/82 removed-method mappings are historical inventory observations, not new code requirements.

**Source:** `AUD-UI-06`, supplied report L766–L786; baseline links retained under A1's evidence limits. [auditsphere-migration-blazor-removal-readiness.md](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/migration/auditsphere-migration-blazor-removal-readiness.md).

### 4. Recipe

1. Regenerate only the stale Operations inventory/action hints from current source using the existing generator and required historical refs. Preserve source-disposition/acceptance labels and historical evidence; a generated hint update is not parity verification.
2. Reconcile known stale code-map prose, such as reviewed staffing, against executable owners. Do not rebuild commands already present or erase prior recorded failures.
3. For each remaining current source family, map action → native component → route → Application scope/validation/receipt owner → distinct acceptance assertion → exact CI/manual evidence. Investigate missing evidence before declaring missing functionality.
4. Order reviews by financial approval/release/portal, then acceptance/staffing/operations, then commercial/time/resources and remaining reads/support assets. Reuse existing assertions and add only a genuinely uncovered invariant, not one test per legacy line.
5. Obtain required current-cookie, route cutover/rollback, asset-cache, accessibility/locale and real Microsoft evidence. Structural route equality and a green focused suite cannot close those gates.
6. Update readiness only from observed evidence. Retain AuditSphereOps.Web/MudBlazor; physical retirement and supported rollback change require a separate explicit owner decision and authorization.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-39-01** · `scripts/ui/inventory.py` — acceptance/source owner, not an existing automated test

Run the existing structural check once after relevant inventory changes; it proves discovery consistency only.

**AC-39-02** · `scripts/migration/generate-blazor-retirement-inventory.py` — acceptance/source owner, not an existing automated test

Run --check with the required full historical objects; distinguish missing history from genuine generated drift.

**AC-39-03** · `docs/migration/auditsphere-migration-blazor-test-parity.md` — acceptance/source owner, not an existing automated test

Review exact current assertions/CI evidence and mandatory real-role/manual gates; do not recreate 82 journeys or manufacture a completion percentage.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

**Only when impacted:** docs: only affected C4 Markdown/naming validators and `git diff --check`. Reuse a suitable current build; do not build twice.

```bash
# NOT RUN — SPEC_ONLY; referenced historical commits must be available.
python3 scripts/ui/inventory.py --check
python3 scripts/migration/generate-blazor-retirement-inventory.py --check
```

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-39-01…AC-39-03 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-40"></a>

## Atomic Task 40 — Accept current production identity, mail and document isolation

### 1. Objective

**Requirements:** AUD-PLAT-05. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_EXTERNAL`. **Dependencies:** 08, 09, 10, 32, 33.

**Current:** Development Microsoft evidence exists, but current production identity/capability/isolation and supported business delivery acceptance remain externally blocked.

**Target:** The selected deployment has independently reviewed real-tenant evidence for every enabled identity/mail/document capability and its denied boundaries.

**Non-goals:** No business mail/customer mutations in unapproved tests, shared broad Graph app identity, production secret publication or simulated production acceptance.

**Boundary:** Actual authorized tenant/consent/sender/site/signing custody and independent reviewers are required. No live provider call is performed by this specification.

**Acceptance:** AC-40-01…AC-40-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| READ-ONLY | `src/AuditSphereOps.Api/ApiHost.Providers.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Practice/CommercialMailDeliveryHandler.cs` |
| READ-ONLY | `src/AuditSphereOps.Infrastructure/Providers/GraphPbcProviderSink.cs` |
| READ-ONLY | `docs/architecture/auditsphere-m365-tenant-administration-permissions.md` |
| MODIFY — evidence only | `docs/execution/status.json` |

### 3. Anchors

Existing per-capability identities/consent records, current RoleGrant/session guards, selected-site PBC and sender boundaries; report gates P1/P2/P2b/M365-ADMIN. Source inspection does not establish production consent.

**Source:** `AUD-PLAT-05`, supplied report L849–L866; baseline links retained under A1's evidence limits. [ApiHost.Observability.cs:42–64](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/ApiHost.Observability.cs#L42-L64); [Worker/Program.cs:25–37](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Worker/Program.cs#L25-L37); [AuditDiagnostics.cs:20–47](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Diagnostics/AuditDiagnostics.cs#L20-L47).

### 4. Recipe

1. Use an explicitly authorized isolated acceptance tenant or approved production acceptance scope with synthetic identities/resources. Record exact deployment SHA/image digest, tenant/capability evidence, role/scope and test scenario; keep sensitive values outside public docs.
2. Verify stable object identity under changed UPN, disabled and wrong-tenant identities, grant expiry/revocation and unrelated client/engagement/group denial. Microsoft group membership must not confer AuditSphere access.
3. For each enabled directory/invitation/membership/mail/site capability, prove the exact allowed operation and a meaningful control-resource denial under its separate least-privileged identity. A credential existing or a generic connectivity check is not acceptance.
4. Exercise approved sender/recipient routing, real PBC exact-version recovery, signature certificate custody/policy and released document receipt semantics. Distinguish provider accepted, actual recipient delivery, client acknowledgment and download.
5. Retain independent reviewer approval and sanitized evidence in existing gates. Any discovered bug becomes a small source-grounded corrective slice; do not add tests merely to re-count existing coverage.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-40-01** · `docs/execution/status.json` — acceptance/source owner, not an existing automated test

Acceptance packet covers normal and denied real-tenant cases with exact identity/role/resource and observed outcome; old Development evidence remains historical.

**AC-40-02** · `src/AuditSphereOps.Api/ApiHost.Providers.cs` — acceptance/source owner, not an existing automated test

No local/simulation composition or unchecked feature flag can satisfy the enabled production capability gate.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-40-01…AC-40-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-41"></a>

## Atomic Task 41 — Independent recovery restores database, artifacts, checkpoints and keys

### 1. Objective

**Requirements:** AUD-PLAT-05. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_EXTERNAL`. **Dependencies:** 32, 33, 35, 36.

**Current:** The development restore drill does not establish production RPO/RTO or independent recovery of external bytes/checkpoints/keys.

**Target:** A rehearsed authorized independent restore reconciles exact issued identities and safely quarantines unknown outcomes before restart.

**Non-goals:** No production reset, invented RPO/RTO, backup-provider replacement or direct restart without independent review.

**Boundary:** Approved independent environment/backups/keys, named operators and firm RPO/RTO targets are required; irreversible operations need action-specific owner authorization.

**Acceptance:** AC-41-01…AC-41-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| READ-ONLY | `scripts/db/restore-drill.sh` |
| READ-ONLY | `src/AuditSphereOps.Application/Operations/OperationRecoveryService.cs` |
| READ-ONLY | `src/AuditSphereOps.Infrastructure/Providers/GraphReleaseCheckpointStore.cs` |
| MODIFY — evidence only | `docs/execution/status.json` |

### 3. Anchors

Existing development-only restore drill and reviewed operation/recovery restart receipts. P7 requires independent production recovery evidence; no production restore command is inferred from the local script.

**Source:** `AUD-PLAT-05`, supplied report L849–L866; baseline links retained under A1's evidence limits. [ApiHost.Observability.cs:42–64](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/ApiHost.Observability.cs#L42-L64); [Worker/Program.cs:25–37](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Worker/Program.cs#L25-L37); [AuditDiagnostics.cs:20–47](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Diagnostics/AuditDiagnostics.cs#L20-L47).

### 4. Recipe

1. Have the firm approve recovery/residency targets, workload and custodially separate restore environment. Inventory the exact database, issued artifact bytes/manifests, checkpoint evidence, schema version, protected keys and necessary configuration.
2. Design an operator-approved backup/restore runbook for the chosen hosting route. Do not run the development reset/restore script against production or assume database restoration includes external evidence.
3. Rehearse recovery with exact source/artifact identities and verify issued hashes, decisions, receipts, source relations and signing-key availability. Record observed data-loss and recovery duration against approved RPO/RTO, not arbitrary targets.
4. Quarantine operations with potentially completed external outcomes and reconcile their original intents before retry/restart. Use the current reviewed recovery commands and independent restart approval, preserving the newly visible recovery-session evidence.
5. Verify schema/runtime compatibility and application rollback/forward repair separately from destructive database restoration. Store evidence privately, with independent reviewer acceptance; no blanket READY status from a successful pg_restore.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-41-01** · `scripts/db/restore-drill.sh` — acceptance/source owner, not an existing automated test

Read-only review confirms its Development-only boundary; it is not the production acceptance command.

**AC-41-02** · `docs/execution/status.json` — acceptance/source owner, not an existing automated test

The authorized drill proves database + artifact + checkpoint + key reconciliation, retained unknown-outcome quarantine and measured approved RPO/RTO; failures remain explicit.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-41-01…AC-41-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-42"></a>

## Atomic Task 42 — Operations and capacity acceptance use existing diagnostics and bounded workloads

### 1. Objective

**Requirements:** AUD-PLAT-05. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_EXTERNAL`. **Dependencies:** 18, 37.

**Current:** OTLP metrics/tracing already expose worker dispositions, but configured alerts, accountable responses and production-volume evidence are not established.

**Target:** Named operators receive and exercise actionable alerts, and the approved workload fits measured bounded query/export/worker limits.

**Non-goals:** No poison-job telemetry framework, rate-limit platform, broker, unapproved dependency or full-suite local benchmarking.

**Boundary:** Firm-approved capacity/alerting targets, selected hosting telemetry and actual operator exercises are needed. No performance percentage is claimed.

**Acceptance:** AC-42-01…AC-42-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| READ-ONLY | `src/AuditSphereOps.Api/ApiHost.Observability.cs` |
| READ-ONLY | `src/AuditSphereOps.Worker/Program.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Diagnostics/AuditDiagnostics.cs` |
| READ-ONLY | `src/AuditSphereOps.Application/Records/EngagementActivityQuery.cs` |
| MODIFY — evidence only | `docs/execution/status.json` |

### 3. Anchors

Existing OTLP-capable AuditDiagnostics including DEAD_LETTER and operation states; older-job discovery Task 18 and bounded activity Task 37 address known operational usability/query issues.

**Source:** `AUD-PLAT-05`, supplied report L849–L866; baseline links retained under A1's evidence limits. [ApiHost.Observability.cs:42–64](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/ApiHost.Observability.cs#L42-L64); [Worker/Program.cs:25–37](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Worker/Program.cs#L25-L37); [AuditDiagnostics.cs:20–47](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Diagnostics/AuditDiagnostics.cs#L20-L47).

### 4. Recipe

1. Approve realistic workload, resource bounds, response/error budgets and storage/backlog thresholds for the actual hosting profile. Record independent capacity targets before calling a result acceptable.
2. Configure the existing diagnostic exporter/collector/alert platform for disposition age/backlog, unavailable capabilities, DB/storage health and failed release/migration. Reuse existing signals; add only a demonstrated missing metric.
3. Exercise alert delivery to named owners and the documented recovery response using synthetic noncustomer work. An exported metric is not an operational alert or an accountable response.
4. Measure selected high-risk paths with the approved dataset: activity paging, older unresolved discovery, accepted source import, rendering/export and durable recovery. Validate bounded memory/query plans, cancellation and concurrent source/role changes.
5. Record bottlenecks and implement only evidenced bounded-query/index/configuration fixes. Reuse one current CI regression result; do not run entire local test suites as a performance benchmark.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-42-01** · `src/AuditSphereOps.Application/Diagnostics/AuditDiagnostics.cs` — acceptance/source owner, not an existing automated test

Existing signal reaches the approved alert channel with correct operation/correlation and redaction; the named operator executes the documented safe response.

**AC-42-02** · `src/AuditSphereOps.Application/Records/EngagementActivityQuery.cs` — acceptance/source owner, not an existing automated test

Approved production-like workload demonstrates bounded pages/materialization after Task 37, with observed latency/resource evidence and explicit failures.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-42-01…AC-42-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

<a id="task-43"></a>

## Atomic Task 43 — The named release profile has complete professional and operational closure

### 1. Objective

**Requirements:** AUD-PLAT-05. **Current state:** `UNVERIFIED`. **Gate:** `BLOCKED_EXTERNAL`. **Dependencies:** 01, 02, 03, 04, 05, 06, 07, 08, 09, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42.

**Current:** Current source contains substantial implementations, but unresolved decisions, source/action gaps and external gates prevent universal requirements-complete or production-accepted claims.

**Target:** The owner can approve a precisely named release profile only when every applicable acceptance row is verified or has an explicit approved deviation/exclusion, with exact current source evidence.

**Non-goals:** No product-completion percentage, automatic production enablement, unapproved merge/deployment or claim that tests certify audit opinions.

**Boundary:** Actual qualified and independent release acceptance is mandatory. Optional/conditional tasks may be NOT_APPLICABLE only with recorded approved release-profile scope; all mandatory blockers remain.

**Acceptance:** AC-43-01…AC-43-02 below.

### 2. Files

Edit only what the outcome requires; `PROPOSED` paths do not assert existing files. Shared exclusions and generated-migration naming rules are in C1–C2.

| Action | Path |
|---|---|
| MODIFY — evidence only | `docs/execution/status.json` |
| MODIFY — evidence only | `docs/execution/auditsphere-execution-current-slice.md` |
| MODIFY — evidence only | `docs/migration/auditsphere-migration-blazor-removal-readiness.md` |
| READ-ONLY | `docs/execution/auditsphere-execution-index-task-board-current.md` |

### 3. Anchors

Report's 91 grouped STE requirement rows, 12 acceptance criteria, 33 gap IDs, T-card completion rules and P1–P10/P2b/M365-ADMIN gates. P4/P5 remain OUT_OF_SCOPE; P6's local evidence does not prove external protection.

**Source:** `AUD-PLAT-05`, supplied report L849–L866; baseline links retained under A1's evidence limits. [ApiHost.Observability.cs:42–64](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Api/ApiHost.Observability.cs#L42-L64); [Worker/Program.cs:25–37](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Worker/Program.cs#L25-L37); [AuditDiagnostics.cs:20–47](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Diagnostics/AuditDiagnostics.cs#L20-L47).

### 4. Recipe

1. Freeze the candidate source and artifact/deployment identities after applicable implementation slices. For every row in the traceability matrix record actual implementation/evidence, approved deviation or approved exclusion; no unresolved mandatory row is silently omitted.
2. Obtain qualified professional output/fixture/signature policy acceptance and real-role new-client, annual, financial preparation, rework, release/archive journeys for the chosen entity/group/native profile. Existing foundations are not recreated merely to demonstrate the journey.
3. Use exact current fast PR CI and full release-verification evidence once for the release candidate; reuse sufficient focused results and close only demonstrated missing invariants. Full automated regression is necessary evidence, not professional or live acceptance.
4. Complete required accessibility, supported locale/time-zone/browser and source/action/rollback evidence; retain Web until its separate removal gate and explicit authorization permit retirement.
5. Complete Tasks 40–42's actual identity/provider/recovery/operations packets, preserving failed or unexecuted results. Keep production activation disabled until all applicable mandatory gates and independent owner review are accepted.
6. Link existing issue/T-card aliases rather than creating a competing task ledger. Use feature branch/PR/review workflow; merge requires AUDITSPHERE-MERGE-APPROVED for the identified PR and action. Deployment/destructive actions require their own explicit authorization, not merely that merge codeword. Its appearance in this specification is not authorization.

### 5. Tests and acceptance

These are proof obligations, not mandatory separate test cases. Reuse/parameterize the closest fixture; add only an uncovered material invariant.

**AC-43-01** · `docs/execution/status.json` — acceptance/source owner, not an existing automated test

Each applicable STE/M20–M26/native/profile criterion has exact source/artifact/role/expected/observed/reviewer evidence; no proposed task or source file presence is marked PASS.

**AC-43-02** · `docs/execution/auditsphere-execution-current-slice.md` — acceptance/source owner, not an existing automated test

Independent reviewer confirms no unexplained mandatory PARTIAL/UNVERIFIED/BLOCKED row, explicit approved exclusions and schema-compatible rollback/deployment handoff.

### 6. Verification

**NOT RUN — SPEC_ONLY.** Apply prerequisites, selector binding, isolated test DB and nonzero-execution/PASS rules in C4. The commands below are future checks, not results.

**Acceptance:** execute only after the named owner/environment/decision gate. Record exact source/artifact/roles, expected and observed result, evidence location and independent reviewer; source inspection is not a manual PASS.

**CI/external:** fast PR CI is separate; full regression runs only in `release-verification.yml` for the exact candidate. Omitted, skipped, zero-test or unavailable checks remain unverified. Preserve this task's decision/context/external gate until actual evidence resolves it.

### 7. Future Done

AC-43-01…AC-43-02 have appropriate observed evidence; the minimal diff preserves applicable scope, financial and immutable-history invariants. Record exact commands/results/omissions, current source, affected docs/contracts, additive legacy treatment and schema-compatible rollback. Resolve the stated gates or retain them explicitly. Follow section E's reviewed PR and owner-authorized release process; this specification is not a PASS or execution authorization.

---

## D. End-to-end traceability and release acceptance

### D1. Atomic acceptance → gap → implementation owner → focused evidence

The acceptance text is in each task's subsection 5. This table identifies its owner and proof location without duplicating test cases. `SPECIFIED` means a described future check, not VERIFIED. A listed test/source file proves neither execution nor full coverage. Task dependencies and external/professional gates still apply.

| Acceptance | Report gap | Task | Principal implementation/evidence owner | Focused test / acceptance owner | Verification boundary | Current handoff state |
|---|---|---|---|---|---|---|
| AC-01-01 | AUD-WF-01 | [01](#task-01) | `src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs` | `tests/AuditSphereOps.Domain.Tests/EngagementActivationReviewTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-01-02 | AUD-WF-01 | [01](#task-01) | `src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs` | `tests/AuditSphereOps.Domain.Tests/EngagementActivationReviewTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-01-03 | AUD-WF-01 | [01](#task-01) | `src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs` | `tests/AuditSphereOps.Api.Tests/EngagementActivationReviewApiTests.cs` | Focused API/real response assertion; isolated fixture; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-02-01 | AUD-WF-02 | [02](#task-02) | `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Completion.cs` | `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-02-02 | AUD-WF-02 | [02](#task-02) | `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Completion.cs` | `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-03-01 | AUD-FIN-01 | [03](#task-03) | `src/AuditSphereOps.Application/Practice/BillingService.cs` | `tests/AuditSphereOps.Domain.Tests/BillingCommandScopeIsolationTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-03-02 | AUD-FIN-01 | [03](#task-03) | `src/AuditSphereOps.Application/Practice/BillingService.cs` | `tests/AuditSphereOps.Domain.Tests/BillingTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-04-01 | AUD-FIN-02 | [04](#task-04) | `src/AuditSphereOps.Application/Practice/BillingService.cs` | `tests/AuditSphereOps.Domain.Tests/BillingTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-04-02 | AUD-FIN-02 | [04](#task-04) | `src/AuditSphereOps.Application/Practice/BillingService.cs` | `tests/AuditSphereOps.Domain.Tests/BillingTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-04-03 | AUD-FIN-02 | [04](#task-04) | `src/AuditSphereOps.Application/Practice/BillingService.cs` | `tests/AuditSphereOps.Api.Tests/BillingWorkspaceApiTests.cs` | Focused API/real response assertion; isolated fixture; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-05-01 | AUD-UI-01 | [05](#task-05) | `src/AuditSphereOps.Ui/src/app/features/finance/invoice.ts` | `src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts` | One affected Angular spec / exact decoder or lifetime; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-05-02 | AUD-UI-01 | [05](#task-05) | `src/AuditSphereOps.Ui/src/app/features/finance/invoice.ts` | `src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts` | One affected Angular spec / exact decoder or lifetime; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-05-03 | AUD-UI-01 | [05](#task-05) | `src/AuditSphereOps.Ui/src/app/features/finance/invoice.ts` | `tests/AuditSphereOps.E2E.Tests/AngularInvoiceOutcomeRecoveryJourneyTests.cs` | Selected workflow proof; full browser regression in CI; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-06-01 | AUD-UI-02 | [06](#task-06) | `src/AuditSphereOps.Ui/src/app/features/portal/home.ts` | `src/AuditSphereOps.Ui/src/app/features/portal/home.spec.ts` | One affected Angular spec / exact decoder or lifetime; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-06-02 | AUD-UI-02 | [06](#task-06) | `src/AuditSphereOps.Ui/src/app/features/portal/home.ts` | `src/AuditSphereOps.Ui/src/app/features/portal/documents.spec.ts` | One affected Angular spec / exact decoder or lifetime; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-07-01 | AUD-UI-03 | [07](#task-07) | `src/AuditSphereOps.Application/Documents/ClientPortalReviewCommands.cs` | `tests/AuditSphereOps.Api.Tests/UiPortalContractTests.cs` | Focused API/real response assertion; isolated fixture; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-07-02 | AUD-UI-03 | [07](#task-07) | `src/AuditSphereOps.Application/Documents/ClientPortalReviewCommands.cs` | `src/AuditSphereOps.Ui/src/app/features/portal/documents.spec.ts` | One affected Angular spec / exact decoder or lifetime; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-07-03 | AUD-UI-03 | [07](#task-07) | `src/AuditSphereOps.Application/Documents/ClientPortalReviewCommands.cs` | `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-08-01 | AUD-PLAT-01 | [08](#task-08) | `src/AuditSphereOps.Infrastructure/Providers/GraphPbcProviderSink.cs` | `tests/AuditSphereOps.Domain.Tests/PbcSelectedSiteProviderTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-08-02 | AUD-PLAT-01 | [08](#task-08) | `src/AuditSphereOps.Infrastructure/Providers/GraphPbcProviderSink.cs` | `tests/AuditSphereOps.Domain.Tests/PbcSelectedSiteProviderTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-09-01 | AUD-WF-04 | [09](#task-09) | `src/AuditSphereOps.Application/Practice/FeeAgreementService.cs` | `tests/AuditSphereOps.Domain.Tests/CommercialWorkflowTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-09-02 | AUD-WF-04 | [09](#task-09) | `src/AuditSphereOps.Application/Practice/FeeAgreementService.cs` | `tests/AuditSphereOps.Domain.Tests/CommercialWorkflowTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-10-01 | AUD-WF-03 | [10](#task-10) | `src/AuditSphereOps.Application/Practice/BillingService.cs` | `tests/AuditSphereOps.Domain.Tests/CommercialWorkflowTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-10-02 | AUD-WF-03 | [10](#task-10) | `src/AuditSphereOps.Application/Practice/BillingService.cs` | `tests/AuditSphereOps.Api.Tests/BillingWorkspaceApiTests.cs` | Focused API/real response assertion; isolated fixture; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-10-03 | AUD-WF-03 | [10](#task-10) | `src/AuditSphereOps.Application/Practice/BillingService.cs` | `src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts` | One affected Angular spec / exact decoder or lifetime; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-11-01 | AUD-WF-05 | [11](#task-11) | `src/AuditSphereOps.Application/Accounting/Intake/FinancialStatementDrillDownQuery.cs` | `tests/AuditSphereOps.Domain.Tests/ProcedureFinancialStatementLinkTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-11-02 | AUD-WF-05 | [11](#task-11) | `src/AuditSphereOps.Application/Accounting/Intake/FinancialStatementDrillDownQuery.cs` | `tests/AuditSphereOps.Domain.Tests/ProcedureFinancialStatementLinkTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-12-01 | AUD-WF-06 | [12](#task-12) | `src/AuditSphereOps.Domain/Practice/WorkflowTemplates.cs` | `tests/AuditSphereOps.Domain.Tests/EngagementWorkflowPlanTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-12-02 | AUD-WF-06 | [12](#task-12) | `src/AuditSphereOps.Domain/Practice/WorkflowTemplates.cs` | `tests/AuditSphereOps.Domain.Tests/WorkflowTemplateTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-13-01 | AUD-WF-07 | [13](#task-13) | `docs/architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md` | `tests/AuditSphereOps.Domain.Tests/EngagementWorkflowTaskTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-13-02 | AUD-WF-07 | [13](#task-13) | `docs/architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md` | `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-14-01 | AUD-WF-08 | [14](#task-14) | `src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs` | `tests/AuditSphereOps.Domain.Tests/EngagementLifecycleQueryTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-14-02 | AUD-WF-08 | [14](#task-14) | `src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs` | `tests/AuditSphereOps.Domain.Tests/EngagementLifecycleQueryTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-15-01 | AUD-WF-10 | [15](#task-15) | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs` | `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-15-02 | AUD-WF-10 | [15](#task-15) | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs` | `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-16-01 | AUD-WF-09 | [16](#task-16) | `src/AuditSphereOps.Ui/src/app/features/audit/plan.ts` | `src/AuditSphereOps.Ui/src/app/features/audit/finding-command-contracts.ts` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-16-02 | AUD-WF-09 | [16](#task-16) | `src/AuditSphereOps.Ui/src/app/features/audit/plan.ts` | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-17-01 | AUD-WF-11 | [17](#task-17) | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs` | `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.Assembly.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-17-02 | AUD-WF-11 | [17](#task-17) | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs` | `src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-18-01 | AUD-UI-04 | [18](#task-18) | `src/AuditSphereOps.Application/Operations/OperationRecoveryService.cs` | `tests/AuditSphereOps.Domain.Tests/OperationRecoveryTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-18-02 | AUD-UI-04 | [18](#task-18) | `src/AuditSphereOps.Application/Operations/OperationRecoveryService.cs` | `tests/AuditSphereOps.Api.Tests/OperationRecoverySafetyApiTests.cs` | Focused API/real response assertion; isolated fixture; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-18-03 | AUD-UI-04 | [18](#task-18) | `src/AuditSphereOps.Application/Operations/OperationRecoveryService.cs` | `src/AuditSphereOps.Ui/src/app/features/admin/operations.spec.ts` | One affected Angular spec / exact decoder or lifetime; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-19-01 | AUD-FIN-04 | [19](#task-19) | `docs/requirements/auditsphere-reporting-profile-acceptance-proposed.md` | `docs/requirements/auditsphere-reporting-profile-acceptance-proposed.md` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-19-02 | AUD-FIN-04 | [19](#task-19) | `docs/requirements/auditsphere-reporting-profile-acceptance-proposed.md` | `src/AuditSphereOps.Application/Accounting/ClientAccounting/ClientAccountingService.Capabilities.cs` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-20-01 | AUD-FIN-04 | [20](#task-20) | `src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs` | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Profiles.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-20-02 | AUD-FIN-04 | [20](#task-20) | `src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs` | `tests/AuditSphereOps.Domain.Tests/FinancialStatementPresentationTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-21-01 | AUD-FIN-03 | [21](#task-21) | `docs/requirements/auditsphere-firm-billing-ledger-treatment-proposed.md` | `docs/requirements/auditsphere-firm-billing-ledger-treatment-proposed.md` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-22-01 | AUD-FIN-03 | [22](#task-22) | `src/AuditSphereOps.Application/Practice/BillingService.cs` | `tests/AuditSphereOps.Domain.Tests/BillingTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-22-02 | AUD-FIN-03 | [22](#task-22) | `src/AuditSphereOps.Application/Practice/BillingService.cs` | `tests/AuditSphereOps.Domain.Tests/LedgerTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-23-01 | AUD-FIN-05 | [23](#task-23) | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementServiceContracts.cs` | `tests/AuditSphereOps.Domain.Tests/CashFlowContractTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-23-02 | AUD-FIN-05 | [23](#task-23) | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementServiceContracts.cs` | `tests/AuditSphereOps.Domain.Tests/FinancialStatementPresentationTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-24-01 | AUD-FIN-05 | [24](#task-24) | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementServiceContracts.cs` | `tests/AuditSphereOps.Domain.Tests/CashFlowContractTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-24-02 | AUD-FIN-05 | [24](#task-24) | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementServiceContracts.cs` | `tests/AuditSphereOps.Domain.Tests/CashFlowContractTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-24-03 | AUD-FIN-05 | [24](#task-24) | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementServiceContracts.cs` | `tests/AuditSphereOps.Domain.Tests/FinancialStatementPresentationTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-25-01 | AUD-FIN-06 | [25](#task-25) | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialPackageBuildInputsQuery.cs` | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.FinancialPackages.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-25-02 | AUD-FIN-06 | [25](#task-25) | `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialPackageBuildInputsQuery.cs` | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.FinancialPackages.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-26-01 | AUD-FIN-07 | [26](#task-26) | `src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs` | `tests/AuditSphereOps.Api.Tests/ConsolidationWorkspaceApiTests.cs` | Focused API/real response assertion; isolated fixture; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-26-02 | AUD-FIN-07 | [26](#task-26) | `src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs` | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-27-01 | AUD-FIN-10 | [27](#task-27) | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Components.cs` | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-27-02 | AUD-FIN-10 | [27](#task-27) | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Components.cs` | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-28-01 | AUD-FIN-08 | [28](#task-28) | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Runs.cs` | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-28-02 | AUD-FIN-08 | [28](#task-28) | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Runs.cs` | `tests/AuditSphereOps.Api.Tests/ConsolidationWorkspaceApiTests.cs` | Focused API/real response assertion; isolated fixture; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-28-03 | AUD-FIN-08 | [28](#task-28) | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationService.Runs.cs` | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-29-01 | AUD-FIN-09 | [29](#task-29) | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationServiceContracts.cs` | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-29-02 | AUD-FIN-09 | [29](#task-29) | `src/AuditSphereOps.Application/Accounting/Consolidation/ConsolidationServiceContracts.cs` | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Consolidation.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-30-01 | AUD-FIN-11 | [30](#task-30) | `src/AuditSphereOps.Application/Accounting/Consolidation/GroupFinancialPackageWorkspace.cs` | `tests/AuditSphereOps.Api.Tests/GroupFinancialPackageApiTests.cs` | Focused API/real response assertion; isolated fixture; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-30-02 | AUD-FIN-11 | [30](#task-30) | `src/AuditSphereOps.Application/Accounting/Consolidation/GroupFinancialPackageWorkspace.cs` | `tests/AuditSphereOps.Api.Tests/GroupFinancialPackageApiTests.cs` | Focused API/real response assertion; isolated fixture; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-31-01 | AUD-UI-05 | [31](#task-31) | `src/AuditSphereOps.Api/Contracts/ApiContract.cs` | `tests/AuditSphereOps.Api.Tests/OpenApiContractTests.cs` | Focused API/real response assertion; isolated fixture; exact-SHA full CI and external gates as applicable | BLOCKED_CONTEXT — NOT RUN |
| AC-31-02 | AUD-UI-05 | [31](#task-31) | `src/AuditSphereOps.Api/Contracts/ApiContract.cs` | `src/AuditSphereOps.Ui/src/app/features/portal/documents.spec.ts` | One affected Angular spec / exact decoder or lifetime; exact-SHA full CI and external gates as applicable | BLOCKED_CONTEXT — NOT RUN |
| AC-31-03 | AUD-UI-05 | [31](#task-31) | `src/AuditSphereOps.Api/Contracts/ApiContract.cs` | `src/AuditSphereOps.Ui/src/app/features/finance/invoice-read.spec.ts` | One affected Angular spec / exact decoder or lifetime; exact-SHA full CI and external gates as applicable | BLOCKED_CONTEXT — NOT RUN |
| AC-32-01 | AUD-PLAT-02 | [32](#task-32) | `src/AuditSphereOps.Infrastructure/Providers/GraphReleaseCheckpointStore.cs` | `tests/AuditSphereOps.Domain.Tests/LiveReleaseCheckpointTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-32-02 | AUD-PLAT-02 | [32](#task-32) | `src/AuditSphereOps.Infrastructure/Providers/GraphReleaseCheckpointStore.cs` | `tests/AuditSphereOps.Domain.Tests/LiveReleaseCheckpointTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-33-01 | AUD-PLAT-03 | [33](#task-33) | `src/AuditSphereOps.Application/Records/FileFreezeService.cs` | `tests/AuditSphereOps.Domain.Tests/SharePointClientSiteProviderTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-33-02 | AUD-PLAT-03 | [33](#task-33) | `src/AuditSphereOps.Application/Records/FileFreezeService.cs` | `tests/AuditSphereOps.Domain.Tests/ClientSharePointSitesTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-33-03 | AUD-PLAT-03 | [33](#task-33) | `src/AuditSphereOps.Application/Records/FileFreezeService.cs` | `src/AuditSphereOps.Infrastructure/Providers/SharePointClientSiteProvider.cs` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-34-01 | AUD-PLAT-03 | [34](#task-34) | `docs/requirements/auditsphere-external-document-observation-scope-proposed.md` | `docs/requirements/auditsphere-external-document-observation-scope-proposed.md` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-34-02 | AUD-PLAT-03 | [34](#task-34) | `docs/requirements/auditsphere-external-document-observation-scope-proposed.md` | `src/AuditSphereOps.Infrastructure/Providers/GraphSelectedSiteDrive.cs` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-35-01 | AUD-PLAT-04 | [35](#task-35) | `.github/workflows/release.yml` | `scripts/release/test_verify_release_source.py` | Saved-fixture stdlib check; no live dispatch; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-35-02 | AUD-PLAT-04 | [35](#task-35) | `.github/workflows/release.yml` | `scripts/release/test_verify_release_source.py` | Saved-fixture stdlib check; no live dispatch; exact-SHA full CI and external gates as applicable | SPECIFIED — NOT RUN |
| AC-36-01 | AUD-PLAT-04 | [36](#task-36) | `.github/workflows/release.yml` | `scripts/release/test_migration_sequence.py` | Saved-fixture stdlib check; no live dispatch; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-36-02 | AUD-PLAT-04 | [36](#task-36) | `.github/workflows/release.yml` | `infra/modules/migration-job.bicep` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_DECISION — NOT RUN |
| AC-37-01 | AUD-PLAT-05 | [37](#task-37) | `src/AuditSphereOps.Application/Records/EngagementActivityQuery.cs` | `tests/AuditSphereOps.Domain.Tests/EngagementActivityPagingTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_CONTEXT — NOT RUN |
| AC-37-02 | AUD-PLAT-05 | [37](#task-37) | `src/AuditSphereOps.Application/Records/EngagementActivityQuery.cs` | `tests/AuditSphereOps.Domain.Tests/EngagementActivityPagingTests.cs` | Focused existing/proposed .NET regression; PostgreSQL when state/locks are involved; exact-SHA full CI and external gates as applicable | BLOCKED_CONTEXT — NOT RUN |
| AC-38-01 | AUD-PLAT-05 | [38](#task-38) | `docs/execution/auditsphere-execution-current-slice.md` | `src/AuditSphereOps.Application/Accounting/ClientOperationalGeneralLedgerWorkspace.cs` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_CONTEXT — NOT RUN |
| AC-38-02 | AUD-PLAT-05 | [38](#task-38) | `docs/execution/auditsphere-execution-current-slice.md` | `src/AuditSphereOps.Application/Accounting/AccountingSourceAcceptanceService.cs` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_CONTEXT — NOT RUN |
| AC-39-01 | AUD-UI-06 | [39](#task-39) | `docs/migration/auditsphere-migration-blazor-retirement-inventory.md` | `scripts/ui/inventory.py` | Saved-fixture stdlib check; no live dispatch; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-39-02 | AUD-UI-06 | [39](#task-39) | `docs/migration/auditsphere-migration-blazor-retirement-inventory.md` | `scripts/migration/generate-blazor-retirement-inventory.py` | Saved-fixture stdlib check; no live dispatch; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-39-03 | AUD-UI-06 | [39](#task-39) | `docs/migration/auditsphere-migration-blazor-retirement-inventory.md` | `docs/migration/auditsphere-migration-blazor-test-parity.md` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-40-01 | AUD-PLAT-05 | [40](#task-40) | `docs/execution/status.json` | `docs/execution/status.json` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-40-02 | AUD-PLAT-05 | [40](#task-40) | `docs/execution/status.json` | `src/AuditSphereOps.Api/ApiHost.Providers.cs` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-41-01 | AUD-PLAT-05 | [41](#task-41) | `docs/execution/status.json` | `scripts/db/restore-drill.sh` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-41-02 | AUD-PLAT-05 | [41](#task-41) | `docs/execution/status.json` | `docs/execution/status.json` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-42-01 | AUD-PLAT-05 | [42](#task-42) | `docs/execution/status.json` | `src/AuditSphereOps.Application/Diagnostics/AuditDiagnostics.cs` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-42-02 | AUD-PLAT-05 | [42](#task-42) | `docs/execution/status.json` | `src/AuditSphereOps.Application/Records/EngagementActivityQuery.cs` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-43-01 | AUD-PLAT-05 | [43](#task-43) | `docs/execution/status.json` | `docs/execution/status.json` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |
| AC-43-02 | AUD-PLAT-05 | [43](#task-43) | `docs/execution/status.json` | `docs/execution/auditsphere-execution-current-slice.md` | Named manual/professional/operator packet; no automated PASS inferred; exact-SHA full CI and external gates as applicable | BLOCKED_EXTERNAL — NOT RUN |

### D2. All 91 grouped STE requirement rows retained

The first four columns below retain the supplied report's requirement wording, source references and baseline disposition. They are not silently corrected, expanded into new modules, or relabeled as current runtime evidence. The additional task column identifies closure/evidence work; it does not mean each requirement needs new code.

Where the source names no defect, Tasks 39/43 preserve action-level and final-profile acceptance rather than manufacturing an implementation task. Apply approved ADR deviations before interpreting literal older wording. Full R2R task-card exit criteria remain separate; this 91-row grouping is not a substitute for every M20–M26 card.

| ID | Required capability | Source | Supplied report disposition / work | Closure / evidence tasks |
|---|---|---|---|---|
| C01 | Preserve all five original functional modules and all eleven canonical lifecycle states; show seven phases as an audited projection, never a second mutable state machine. | [STE v2.2 L11, 29-33, 56-70, 592, 611, 677](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L11) | Source foundations present; relevant workflow/UI stories and full role acceptance remain | [39](#task-39), [43](#task-43) |
| C02 | Unlimited client entities, historical engagements and working papers without per-file subscription penalties; this is not a claim of unbounded physical resources or zero infrastructure costs. | [STE v2.2 L76](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L76) | No per-client license cap established; capacity is bounded and production-volume acceptance remains (AUD-PLAT-05) | [37](#task-37), [38](#task-38), [40](#task-40), [41](#task-41), [42](#task-42), [43](#task-43) |
| C03 | Unified commercial sales, administration/governance, compliance, fieldwork, multilevel quality review, deliverable generation and firm practice management. | [STE v2.2 L77, 129](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L77) | Source foundations present; relevant workflow/UI stories and full role acceptance remain | [39](#task-39), [43](#task-43) |
| C04 | Declared auditing context: ISA/IFRS, primary currency QAR; ISA 210, ISA 220/ISQC 1, ISA 230, ISA 320, ISA 505, ISA 570, ISA 700/705 named. These are source labels; their current normative applicability needs independent validation if compliance is claimed. | [STE v2.2 L5-8, 78-85](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L5-L8) | Professional context stated; current standards applicability is not certified (AUD-FIN-04, AUD-PLAT-05) | [19](#task-19), [20](#task-20), [37](#task-37), [38](#task-38), [40](#task-40), [41](#task-41), [42](#task-42), [43](#task-43) |
| C05 | PREPARER: assigned FSLI procedures, digital/physical evidence references, submit review packages, log daily task hours. | [STE v2.2 L94](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L94) | Source foundations present; relevant workflow/UI stories and full role acceptance remain | [39](#task-39), [43](#task-43) |
| C06 | REVIEWER: substantive test/schedule review, mandatory rework notes, sampling/materiality, SRM, budgets/team hours/milestones. | [STE v2.2 L95](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L95) | Source foundations present; relevant workflow/UI stories and full role acceptance remain | [39](#task-39), [43](#task-43) |
| C07 | APPROVER/Partner: acceptance and commercial authorization, engagement letters, Red areas/SRM, opinion/signatures/seal, releases and regulatory lock. | [STE v2.2 L96](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L96) | Source foundations present; relevant workflow/UI stories and full role acceptance remain | [39](#task-39), [43](#task-43) |
| C08 | CLIENT: isolated external workspace, authorized PBC requests/status, TB/schedules/evidence upload, commercial and final documents; restrictive scope and temporal freeze. | [STE v2.2 L97, 659, 673](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L97) | Source foundations present; relevant workflow/UI stories and full role acceptance remain | [39](#task-39), [43](#task-43) |
| M1.01 | Lead capture supports Phone, WhatsApp, Email, Web Forms and Referrals. | [STE v2.2 L315](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L315) | Implemented channel capture; ADR-0017 makes WhatsApp a lead label, not automated ingestion | [39](#task-39), [43](#task-43) |
| M1.02 | Organizational tree supports Holdings, Subsidiaries and Affiliates. | [STE v2.2 L316](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L316) | Relationships/perimeter source exists; full parent/subsidiary/affiliate acceptance remains | [39](#task-39), [43](#task-43) |
| M1.03 | Multiple contacts and role-based routing: MD/GM receives proposals, engagement letters and final reports; CFO/FD invoices/receipts; accountant/audit liaison PBC requests. | [STE v2.2 L317-320](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L317-L320) | Partial: current proposal routing exists; finance and letter routing need AUD-WF-04 | [09](#task-09) |
| M1.04 | Standardized 1–2 page brief quotation with scope, statutory year, fee, 50/50 payment terms and execution timeline. | [STE v2.2 L324](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L324) | Brief quotation generation exists; page count/content acceptance unverified | [39](#task-39), [43](#task-43) |
| M1.05 | Auto-compiled comprehensive proposal includes firm profile/history/registrations, Partner/team CVs, industry credentials/portfolio, audit methodology and fee/deliverable timeline. | [STE v2.2 L325-330](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L325-L330) | Five-section comprehensive proposal exists with reviewed content and entered CV/timeline; professional output acceptance unverified | [39](#task-39), [43](#task-43) |
| M1.06 | Separate commercial acceptance and Partner risk clearance keys; no engagement-letter generation with missing, rejected, conditional, stale or superseded key. | [STE v2.2 L334-349, 382-390](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L334-L349) | Shared dual-key evidence exists; downstream activation omission is AUD-WF-01 | [01](#task-01) |
| M1.07 | Commercial consent pins exact dispatched quotation revision, scope, entity, service route, period, currency, fee and 50/50 terms. Capture authorized contact, channel, timestamp, evidence. Changed offer requires renewed consent. | [STE v2.2 L382](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L382) | Exact proposal/response evidence exists; complete consent-policy acceptance remains | [39](#task-39), [43](#task-43) |
| M1.08 | Risk approval pins current due-diligence generation; Partner current unconditional decision only. Includes AML/KYC, UBO, independence, conflicts, integrity and relevant risks. | [STE v2.2 L384](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L384) | Current generation-bound risk and recurring evidence exists | [39](#task-39), [43](#task-43) |
| M1.09 | Both valid keys confer eligibility for separately authorized engagement-letter issuance; activation remains blocked until linked advance fully paid and allocated and holds/authorization are clear. | [STE v2.2 L343-349, 386-388](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L343-L349) | Full linked advance gate exists; activation must also retain current commercial key (AUD-WF-01) | [01](#task-01) |
| M1.10 | Decline routes to revised/re-dispatched proposal or closure; conditional/refused risk approval to remediation and fresh unconditional review. Open holds, stale offer, mismatched client/service scope block protected actions. Tasks/staff overrides cannot bypass. | [STE v2.2 L390](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L390) | Holds/refusal/staleness controls exist; AUD-WF-01 closes omitted activation predicate | [01](#task-01) |
| M1.11 | Onboarding cards show independent key state (Pending, Awaiting Evidence, Approved, Rejected, Stale), owner, time, evidence, revision and permitted action; independent EL readiness and advance-payment card; AML/KYC findings hidden from client. | [STE v2.2 L392](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L392) | Native onboarding/readiness workspaces exist; full action/state/accessibility acceptance is AUD-UI-06 | [39](#task-39) |
| M1.12 | Preserve authentic actor, decision generation/evidence/time/rejection reason. Reevaluate both keys during document generation and activation; reject stale and concurrently changed inputs. | [STE v2.2 L394](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L394) | Confirmed activation recheck/preview gap (AUD-WF-01) | [01](#task-01) |
| M1.13 | Engagement-letter templates by type: external statutory audit and source-labelled internal audit/AUP (ISRS 4400). Populate legal name, period, fee, deadlines with minimal manual fields. Apply Partner stamp/signature under generation authority. | [STE v2.2 L399-405](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L399-L405) | Document generation/templates and Partner authority exist; professional wording/addressee acceptance remains (AUD-WF-04) | [09](#task-09) |
| M1.14 | Generate 50% advance invoice with engagement letter; record cheque/transfer reference; immediately compile and dispatch official receipt on recorded payment. | [STE v2.2 L407-411](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L407-L411) | Approved draft-only invoice automation and receipt records exist; invoice send/recipient/exact receipt gaps are AUD-WF-03/04 and AUD-FIN-02 | [04](#task-04), [10](#task-10) |
| M1.15 | Isolated client onboarding and first-use submission gate; current ADR-0003/0017 approve Microsoft sign-in in place of the original temporary-password mechanism. | [STE v2.2 L412-415](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L412-L415); approved ADR0003/0017 | Approved deviation: Microsoft sign-in/first-use activation; production acceptance remains (AUD-PLAT-05) | [37](#task-37), [38](#task-38), [40](#task-40), [41](#task-41), [42](#task-42), [43](#task-43) |
| M1.16 | PBC real-time status: Pending Upload, Under Review, Approved, Rejected/Re-upload Required; rejection requires reason visible immediately to client. | [STE v2.2 L416-417](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L416-L417) | Scoped request/upload/review statuses and reasons exist; portal lifetime/comment recovery and live proof remain (AUD-UI-02/03, AUD-PLAT-01/05) | [06](#task-06), [08](#task-08) |
| M1.17 | Freeze client upload privileges at final audit report release. Do not conflate this with later entire-archive regulatory lock. | [STE v2.2 L418, 604](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L418) | Local final-release upload freeze exists; full scoped provider/release acceptance remains | [39](#task-39), [43](#task-43) |
| M2.01 | New-client acceptance checklist: UBO, AML background, KYC documents, management integrity, financial viability, independence, conflicts. | [STE v2.2 L424-425](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L424-L425) | Implemented current acceptance checklist/evidence; live/professional acceptance remains | [39](#task-39), [43](#task-43) |
| M2.02 | Recurring continuance delta checks: prior-year fees, management/shareholding changes, new credit/loans, litigation/legal notices, fraud/regulatory investigations. | [STE v2.2 L426-427](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L426-L427) | Implemented engagement-specific continuance; approved RV031/RV032 requirements retained | [39](#task-39), [43](#task-43) |
| M2.03 | Operational planning blocked without Partner digital acceptance and separate full allocated advance gate. | [STE v2.2 L388, 428](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L388) | Risk/current advance planning gates exist; current commercial activation gap AUD-WF-01 | [01](#task-01) |
| M2.04 | Visual capacity calendar: availability, target utilization and leave. | [STE v2.2 L432](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L432) | Resource profiles, leave/net capacity and grid exist; role/volume/accessibility acceptance remains | [39](#task-39), [43](#task-43) |
| M2.05 | Explicit assignment of Partner/Approver, Manager or Senior/Reviewer, Associates/Preparers. | [STE v2.2 L433](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L433) | Staffing roles and reviewed staffing commands exist; procedure-level assignment policy is AUD-WF-07 | [13](#task-13) |
| M2.06 | Milestones relative to statutory cutoff; example year-end, fieldwork start, draft report and final signature dates are illustrative, not mandatory dates. | [STE v2.2 L434](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L434) | Milestones/due rules exist; representative statutory-calendar acceptance remains | [39](#task-39), [43](#task-43) |
| M2.07 | Automatically provision five directories: `01_Administration & Planning`, `02_Trial Balance & Schedules`, `03_Fieldwork & Testing`, `04_Drafts & Deliverables`, `05_Final Signed Archive`. Source says at Partner risk acceptance; flow shows after onboarding/advance, so distinguish provision from activation. | [STE v2.2 L197-204, 438-443](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L197-L204) | Provisioning capability exists; full live provision and approved external archive work remain (AUD-PLAT-03/05) | [33](#task-33), [34](#task-34) |
| M2.08 | Current TB benchmark choices: PBT 5–10%; revenue 0.5–2%; assets 0.5–1%; equity/net assets 1–2%. ADR-0011/0017 approve PBT without normalization. | [STE v2.2 L447-451](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L447-L451); approved ADR0011/0017 | Four benchmark calculations exist; unnormalized PBT is an approved ADR-0011/0017 deviation | [39](#task-39), [43](#task-43) |
| M2.09 | PM = benchmark × percentage; TE/performance materiality 50–75% of PM; SAD/trivial threshold 3–5% of PM. | [STE v2.2 L452-455](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L452-L455) | Implemented PM/TE/SAD ranges and source-based calculation | [39](#task-39), [43](#task-43) |
| M2.10 | Manager rounding deviation at most ±5% before Partner approval. | [STE v2.2 L456](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L456) | Implemented practical-rounding bound and current Partner approval | [39](#task-39), [43](#task-43) |
| M2.11 | Green: balance below TE, junior/standard procedures. Amber: balance between TE/PM, senior substantive testing/sampling. Red: exceeds PM or critical estimates/high inherent risk, mandatory Manager execution and Partner review. | [STE v2.2 L457-460](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L457-L460) | Implemented amount/critical-risk and role routing; qualified method acceptance remains | [39](#task-39), [43](#task-43) |
| M2.12 | Partner formally approves current planning/materiality before fieldwork; changed TB/mapping/materiality inputs invalidate stale approvals. | [STE v2.2 L215-217, 600, 624, 669](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L215-L217) | Current planning/materiality checks exist; downstream stale procedure use remains AUD-WF-02 | [02](#task-02) |
| M3.01 | Excel/CSV TB ingest including exports from accounting packages; automatic standardized FSLI mapping with historical memory. | [STE v2.2 L466-467](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L466-L467) | TB/GL import/mapping and historical suggestions exist; accepted source/mapping profile verification remains | [39](#task-39), [43](#task-43) |
| M3.02 | Split financial-statement dashboard: P&L upper half, balance sheet lower half; current balance, prior comparative, percentage variance per row. | [STE v2.2 L468-470](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L468-L470) | Native statement workspace/current-prior/variance projection exists; annual comparative and full UI acceptance remain (AUD-FIN-06, AUD-UI-06) | [25](#task-25), [39](#task-39) |
| M3.03 | Each FSLI links to Analytical Review and substantive Audit Workprogram. | [STE v2.2 L471-473](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L471-L473) | Actions exist; exact procedure association/current coverage requires AUD-WF-05 | [11](#task-11) |
| M3.04 | Concurrent work on separate FSLI rows without whole-file lockouts or overwrite conflicts. | [STE v2.2 L250, 474](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L250) | Per-procedure locking/versioned results exist; full parallel-edit acceptance remains | [39](#task-39), [43](#task-43) |
| M3.05 | Preloaded FSLI procedures cover ownership, valuation, completeness, existence and cutoff. | [STE v2.2 L478](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L478) | Procedure catalogues/assertion coverage exist; method/template acceptance remains | [39](#task-39), [43](#task-43) |
| M3.06 | Auditors can add editable ad-hoc procedural rows for engagement-specific risks. | [STE v2.2 L479](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L479) | Ad-hoc/tailored procedure commands exist | [39](#task-39), [43](#task-43) |
| M3.07 | Sampling calculators: Monetary Unit Sampling (MUS), Systematic Random Sampling and Stratified Attribute Sampling. | [STE v2.2 L480](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L480) | MUS/systematic/stratified/attribute and additional engines exist; professional sampling acceptance remains | [39](#task-39), [43](#task-43) |
| M3.08 | Digital links to spreadsheets/PDFs/PBC uploads and dedicated physical binder/index/box/shelf reference. | [STE v2.2 L481-483](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L481-L483) | Digital links and physical reference/custody data exist; exact provider readback gap AUD-PLAT-01 | [08](#task-08) |
| M3.09 | Comparative financial analytical-review templates and mandatory ISA 570 going-concern checklist. | [STE v2.2 L484](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L484) | Analytical review and going-concern forms/blockers exist | [39](#task-39), [43](#task-43) |
| M3.10 | Preparer performs procedures, attaches current evidence and submits versioned result/package for review. | [STE v2.2 L233-241, 488, 625](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L233-L241) | Versioned current result submission exists; explicit assignment/pickup policy AUD-WF-07 | [13](#task-13) |
| M3.11 | Reviewer independent, appropriately ranked and reviewing current revision; Manager can return individual steps with mandatory notes; automatically Under Rework and assigned to original preparer; resubmission/re-review preserves history. | [STE v2.2 L236-243, 489-491, 626, 668](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L236-L243) | Independent review, mandatory comments and feature-enabled idempotent rework/fallback exist; deployment/queue acceptance remains | [39](#task-39), [43](#task-43) |
| M3.12 | Compile current SRM only after Manager approval of all workprograms; includes variances, AJEs, unadjusted differences relative to SAD/PM and significant estimates. | [STE v2.2 L492-494](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L492-L494) | SRM and differences/AJE summary exist; current procedure predicate AUD-WF-02 and estimates AUD-WF-10 remain | [02](#task-02), [15](#task-15) |
| M3.13 | Zero open relevant review notes, current SRM and critical confirmations returned before Partner-review progression. | [STE v2.2 L602, 626, 669](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L602) | Open-note/confirmation and SRM gates exist; completion/handover fixes AUD-WF-02/08 | [02](#task-02) |
| M3.14 | Partner clears current SRM and Red-risk areas before reporting. | [STE v2.2 L244-245, 495](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L244-L245) | Current staffed Partner clearance exists; current-evidence consumer fix AUD-WF-02 | [02](#task-02) |
| M3.15 | Confirmation grid covers Bank, AR, AP, Inventory and Legal. | [STE v2.2 L499](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L499) | Confirmation requests/grid and evaluation exist; complete channel/category acceptance remains | [39](#task-39), [43](#task-43) |
| M3.16 | Critical unreturned confirmations block final audit report and trigger Pending Confirmation/Holding Letter; v2.2 requires confirmations returned and independently evaluated, not status-only completion. | [STE v2.2 L259-263, 500, 602, 628, 670](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L259-L263) | Critical returned/evaluated rule and holding letter exist; final live release acceptance remains | [39](#task-39), [43](#task-43) |
| M4.01 | Engagement Partner exclusively selects Clean/Unqualified (Unmodified), Qualified, Disclaimer or Adverse opinion. | [STE v2.2 L261-265, 504-510](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L261-L265) | Current staffed Partner opinion authority exists | [39](#task-39), [43](#task-43) |
| M4.02 | Modified opinion requires selected affected FSLI and textual justification injected into basis paragraph. | [STE v2.2 L511](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L511) | Affected area/basis justification exists; professional narrative acceptance remains | [39](#task-39), [43](#task-43) |
| M4.03 | Final certified report embeds authorized Partner digital signature and official firm seal; approved credentials and signed-document integrity preserved. | [STE v2.2 L512, 627, 677](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L512) | Certificate PDF signature/seal path exists; custody, policy and live acceptance remain (AUD-PLAT-02/05) | [32](#task-32) |
| M4.04 | Deliverable 1: independent auditor report plus audited financial statements, certified/sealed/digitally signed PDF. | [STE v2.2 L518](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L518) | Exact report/financial composition and bundle checks exist; entity financial gaps and live release remain | [39](#task-39), [43](#task-43) |
| M4.05 | Deliverable 2: management letter structured Deficiency → Impact → Auditor Recommendation. | [STE v2.2 L519](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L519) | Matter free text + impact + recommendation + response exist; conditional acceptance only (AUD-WF-09) | [16](#task-16) |
| M4.06 | Deliverable 3: LOR populated with engagement figures, exportable for client-letterhead printing, management signature and re-upload. | [STE v2.2 L520](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L520) | LOR generation and exact signed upload/verification/dedup exist; portal lifetime and live acceptance remain | [39](#task-39), [43](#task-43) |
| M4.07 | Deliverable 4: management correspondence audit trail summarizing formal inquiries, confirmation results and cleared queries. | [STE v2.2 L521](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L521) | Structured correspondence JSON exists; format/channel-coverage acceptance only (AUD-WF-11) | [17](#task-17) |
| M4.08 | Deliverable 5: automated remaining 50% professional-fee invoice. Source does NOT impose full final payment as report-release condition. | [STE v2.2 L522, 604](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L522) | Final 50% invoice draft automation exists under ADR-0007; approved posting and evidenced dispatch remain required | [39](#task-39), [43](#task-43) |
| M4.09 | Separately authorized release with verified bundle/current critical-confirmation/professional gates, delivery evidence and upload freeze; task completion cannot issue release. | [STE v2.2 L272-278, 604, 628, 656](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L272-L278) | Current bundle/professional/release checks exist locally; currentness defects and live checkpoint must close | [39](#task-39), [43](#task-43) |
| M4.10 | 60 calendar-day assembly countdown anchored to Partner report signature, or authorized early Partner lock. | [STE v2.2 L526, 605, 629](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L526) | Signature-anchored 60-day countdown and authorized early local lock exist | [39](#task-39), [43](#task-43) |
| M4.11 | Effective committed local archive lock blocks deletion, modification and overwrite permanently; timer expiry alone is not success. Read-only viewing and regulator export remain allowed. | [STE v2.2 L527, 606, 629, 671](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L527) | Effective local archive guard exists; independently observed external path remains (AUD-PLAT-03) | [33](#task-33), [34](#task-34) |
| M4.12 | Immutable timestamped audit record of system actions, reviews and sign-offs. | [STE v2.2 L528](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L528) | Audit/review/operation history foundations exist; approved group pin replacement is a specific weakness (AUD-FIN-10) | [27](#task-27) |
| M4.13 | Display provider retention/protection separately; local archive state does not prove SharePoint/external immutability or production compliance. | [STE v2.2 L629, 671, 677](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L629) | Local/external distinction modeled; OBSERVED provider path not implemented (AUD-PLAT-03) | [33](#task-33), [34](#task-34) |
| M5.01 | Daily operational hours by assigned engagement task/financial statement area; capture role-specific charge rate. | [STE v2.2 L94, 293-296](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L94) | Daily task/engagement time, immutable rate/version snapshot and approval exist | [39](#task-39), [43](#task-43) |
| M5.02 | Tiered source default rates: Partner QAR 1,000/h; Manager 750; Supervisor/Senior 500; Associate/Junior 200. | [STE v2.2 L534-538](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L534-L538) | Role rate versions/aliases exist; accepted current rates apply, not retroactive repricing | [39](#task-39), [43](#task-43) |
| M5.03 | Source-defined engagement profitability = contracted fee − sum(hours × charge-out rate). This is a charge-value measure; no actual cost/margin definition should be invented. | [STE v2.2 L539-541](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L539-L541) | Contracted fee less lifetime approved billable-time standard value exists; separate actual staff-cost metrics are not the source-defined figure ([ContractContributionCalculator.cs:8–16](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/ContractContributionCalculator.cs#L8-L16); [FirmOperationsServices.cs:309–322](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/src/AuditSphereOps.Application/Practice/FirmOperationsServices.cs#L309-L322)) | [39](#task-39), [43](#task-43) |
| M5.04 | Budget versus actual hours by engagement phase, utilization/realization and staff-performance reporting. | [STE v2.2 L293-298, 543](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L293-L298) | Budget/resource/utilization/practice reports exist; approved staff-performance access and real workload acceptance remain | [39](#task-39), [43](#task-43) |
| M5.05 | Separate internal practice ledger records office rent/facility costs, staff salaries/benefits, Partner withdrawals and petty cash. | [STE v2.2 L545-551](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L545-L551) | Separate balanced firm ledger and reviewed postings/period locks exist; expense taxonomy/automation policy remains explicit | [39](#task-39), [43](#task-43) |
| M5.06 | Fees/invoices/receipts link to internal practice ledger and reporting. | [STE v2.2 L299-303](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L299-L303) | Confirmed disconnected billing→GL path; approve recognition and reconciliation before implementing AUD-FIN-03 | [21](#task-21), [22](#task-22) |
| M5.07 | Internal firm Trial Balance, monthly P&L and client AR aging tracking both 50% advance and 50% final payment. | [STE v2.2 L552](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L552) | Firm TB/P&L/AR reports exist; ledger/subledger integration, receipt and source safety remain (AUD-FIN-01/02/03) | [03](#task-03) |
| U01 | Seven-step tracker with plain titles; current phase, responsible user/role, relevant due date/milestone, completion, gate readiness, exact blockers, next permitted action. | [STE v2.2 L31-33, 621](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L31-L33) | Tracker/readiness/next-action projection exists; handover owner mismatch AUD-WF-08 and acceptance AUD-UI-06 | [14](#task-14), [39](#task-39) |
| U02 | Expand advanced phase checklists; contextual navigation to client, planning, FSLI, PBC, review, completion, finance and records workbenches. Responsive and keyboard accessible. | [STE v2.2 L33, 621](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L33) | Contextual workspaces/routes exist; full keyboard/responsive/screen-reader acceptance unverified | [39](#task-39), [43](#task-43) |
| U03 | Distinguish Waiting for Client, Under Review, Rework Required and Blocked; ordinary navigation hides canonical internal lifecycle codes. | [STE v2.2 L33](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L33) | Blocker/state presentation exists; complete empty/denied/stale/rework branch acceptance unverified | [39](#task-39), [43](#task-43) |
| U04 | Phase 1 Onboarding includes quote/response, separated two keys, EL, distinct advance invoice/payment and activation eligibility. | [STE v2.2 L623](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L623) | Commercial/risk/payment views exist; activation/dispatch/receipt fixes AUD-WF-01/03/04, AUD-FIN-02 | [01](#task-01), [04](#task-04) |
| U05 | Phase 2 Planning includes staff/deadlines, provision, import/mapping, PM/TE/SAD, risk assignment and current Partner approval; input change invalidates approval. | [STE v2.2 L624](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L624) | Planning/resource/mapping/materiality views exist; provider and stale evidence follow-through remain | [39](#task-39), [43](#task-43) |
| U06 | Phase 3 Fieldwork includes FSLI links, workprograms/evidence/analytical review/sampling, preparer assignment, versioned submission. | [STE v2.2 L625](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L625) | Fieldwork/versioned submission exists; exact FSLI relationship and assignment policy AUD-WF-05/07 | [11](#task-11) |
| U07 | Phase 4 Manager Review requires independent risk-ranked reviewer, rejection comments/history/rework route, resolved notes and current SRM. | [STE v2.2 L626](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L626) | Review/rework/current-review owners exist; completion consumer fix AUD-WF-02 | [02](#task-02) |
| U08 | Phase 5 Partner Sign-off requires high-risk/SRM review, opinion and modified basis, approved final-report signature credentials. | [STE v2.2 L627](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L627) | Partner actions and certificate signing exist; handover and estimates AUD-WF-08/10; live signing acceptance separate | [14](#task-14) |
| U09 | Phase 6 Report & Delivery shows independently evaluated critical confirmations, controlled statements/report, management letter/LOR/correspondence/final fee and release checkpoints. | [STE v2.2 L628](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L628) | Bundle/document/critical-confirmation owners exist; financial output, exact source and live checkpoint gaps remain | [39](#task-39), [43](#task-43) |
| U10 | Phase 7 Archive & Close displays signature-anchored countdown, authorized early lock, effective local freeze and independently observed external protection. | [STE v2.2 L629](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L629) | Countdown/local freeze/provider-state display exists; verified external archive work AUD-PLAT-03 | [33](#task-33), [34](#task-34) |
| U11 | Admin-approved versioned templates by engagement type/industry; editable task groups, owners, offsets, dependencies, effort, client requests and ad-hoc tasks. Pin engagement revision; template change cannot rewrite existing work or remove domain gates. | [STE v2.2 L632](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L632) | Versioned task templates/plan dependencies exist; template PBC reference is inert (AUD-WF-06) | [12](#task-12) |
| U12 | Work filters: My Tasks, Team Work, Waiting for Client, Under Review, Rework, Overdue, Completed; list, Kanban and calendar views. | [STE v2.2 L632](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L632) | Task queue/list/board/calendar support exists; complete filter/role/action acceptance remains AUD-UI-06 | [39](#task-39) |
| U13 | Recurring engagement creation reuses templates/prior client details/historical mapping suggestions only. Require fresh consent, Partner continuance, current TB/materiality/review/report decisions; never reuse professional sign-offs as current authorization. | [STE v2.2 L635](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L635) | Fresh engagement/template structures exist; native financial close/opening handoff remains a separate acceptance boundary | [39](#task-39), [43](#task-43) |
| U14 | Automator may assign tasks/follow-up checklists/reminders/deadlines, escalate overdue PBC and notify authorized users after genuine domain events. | [STE v2.2 L656](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L656) | Workflow automation/notifications exist in bounded paths; template PBC connection and live event acceptance remain | [39](#task-39), [43](#task-43) |
| U15 | Automator may NOT synthesize approvals, record payment, clear risk, approve materiality, sign opinion, post invoices, release or freeze files. Protected action goes through separate reauthorized domain command/actor. | [STE v2.2 L645-650, 656](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L645-L650) | Protected domain command boundaries exist; no task-based substitute is authorized | [39](#task-39), [43](#task-43) |
| U16 | Every automation reauthorizes, uses idempotency key, pins current version, audits changes and safely rejects stale input. Task dependencies reject cycles. | [STE v2.2 L656](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L656) | Reviewed preview/commit/receipt patterns and cycle controls exist; uncovered finance/comment recovery must close | [39](#task-39), [43](#task-43) |
| U17 | Role queues: Preparer own fieldwork/rework; Reviewer submissions/notes; Partner acceptance/Red/opinion/release; Client authorized commercial/PBC status only. | [STE v2.2 L659](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L659) | Role queues and restricted portal exist; assignment/config/fallback acceptance and current scope matrix remain | [39](#task-39), [43](#task-43) |
| U18 | Server-side records determine phase readiness; mutable task flags are not evidence. Task percentage and mandatory clearance tracked separately, no client AML/KYC leakage, plain blocker explanations. | [STE v2.2 L659](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L659) | Derived gate readiness exists; completion/handover corrections AUD-WF-02/08 preserve this design | [02](#task-02) |

### D3. All twelve STE v2.2 acceptance criteria retained

These twelve source-level criteria are broader than individual regressions. The supplied baseline assessment is preserved; the final column assigns the relevant corrective/acceptance tasks. No universal AC11/AC12 pass is inferred from shared authorization/idempotency infrastructure.

| Source AC | Criterion | Source | Supplied baseline assessment | Corrective / acceptance tasks |
|---|---|---|---|---|
| AC01 | Each canonical state maps to exactly one of seven phases | [STE v2.2 L663](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L663) | Implemented deterministic mapper; structural check is not all journey acceptance. | [14](#task-14), [39](#task-39), [43](#task-43) |
| AC02 | Zero or one approval key blocks engagement-letter issuance | [STE v2.2 L664](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L664) | Dual-key/document evidence exists; preserve exact negative cases in current full acceptance. | [01](#task-01), [39](#task-39), [43](#task-43) |
| AC03 | Both keys allow authorized EL without bypassing full advance | [STE v2.2 L665](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L665) | Separate advance gate exists; AUD-WF-01 fixes activation's omitted commercial recheck. | [01](#task-01), [04](#task-04), [10](#task-10), [43](#task-43) |
| AC04 | Changed offer or risk basis invalidates affected authority | [STE v2.2 L666](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L666) | Exact evidence models exist; AUD-WF-01 omission is a concrete failure at activation. | [01](#task-01), [43](#task-43) |
| AC05 | Rejected/conditional decisions and holds cannot be overwritten by tasks | [STE v2.2 L667](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L667) | Domain controls exist; no task-based exception is authorized. Verify all exposed actors. | [01](#task-01), [02](#task-02), [13](#task-13), [43](#task-43) |
| AC06 | Independent reviewers and mandatory rework comments | [STE v2.2 L668](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L668) | Implemented current review; flag-enabled rework task and invalid-assignee fallback already exist. | [02](#task-02), [13](#task-13), [43](#task-43) |
| AC07 | Open notes and stale materiality/evidence block later decisions | [STE v2.2 L669](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L669) | AUD-WF-02 is a concrete completion-consumer failure after renewed planning; AUD-WF-05 addresses displayed coverage. | [02](#task-02), [11](#task-11), [14](#task-14), [15](#task-15), [28](#task-28), [43](#task-43) |
| AC08 | Critical confirmation cases block release | [STE v2.2 L670](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L670) | Returned and independently evaluated controls exist; current full and real-tenant journey evidence required. | [39](#task-39), [40](#task-40), [43](#task-43) |
| AC09 | Archive only after committed local lock; no implied provider immutability | [STE v2.2 L671](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L671) | Local effective freeze exists; AUD-PLAT-03 missing provider observation remains separate. | [32](#task-32), [33](#task-33), [34](#task-34), [43](#task-43) |
| AC10 | Recurrence/templates never copy valid professional sign-offs | [STE v2.2 L672](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L672) | Fresh structures and source pins exist; complete accepted recurrence evidence remains. | [12](#task-12), [13](#task-13), [38](#task-38), [43](#task-43) |
| AC11 | Scoped actors cannot access unrelated clients | [STE v2.2 L673](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L673) | Extensive explicit authorization exists; AUD-FIN-01 target/source inconsistency and full endpoint/export/recovery matrix need closure. | [03](#task-03), [04](#task-04), [05](#task-05), [07](#task-07), [18](#task-18), [25](#task-25), [28](#task-28), [30](#task-30), [31](#task-31), [39](#task-39), [40](#task-40), [43](#task-43) |
| AC12 | Automation retries cannot duplicate or forge domain events | [STE v2.2 L674](https://github.com/nirzaf/AuditSphere/blob/ec2df24342df5db4d3e2d5bd88e2bcbc4fab2318/docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md#L674) | Reviewed idempotent patterns exist; AUD-FIN-02/AUD-UI-03 identify uncovered repeated commands; AUD-PLAT-01/02 cover provider evidence. | [04](#task-04), [07](#task-07), [08](#task-08), [10](#task-10), [12](#task-12), [22](#task-22), [29](#task-29), [32](#task-32), [33](#task-33), [43](#task-43) |

### D4. Cross-cutting acceptance boundaries not hidden by the 33-item register

| Boundary | Explicit task coverage | Closure rule |
|---|---|---|
| Native BOOKKEEPING two-period close/opening/accepted-source/package route | Task 38, then Tasks 19–25 and 43 where applicable | First establish actual contracts/profile scope. Existing native GL/TB functionality is not a sealed accepted source; no second ledger or silent cutover. |
| Production-volume activity reads | Tasks 37 and 42 | Identify actual callers; bound before materialization; approve workload/resource targets and measure, rather than inventing a throughput claim. |
| Framework, FX, consolidation, cash-flow and professional output eligibility | Tasks 19–20, 23–30, 43 | Qualified policy revisions and independent fixtures precede accepted output; raw configuration strings do not confer approval. |
| Management-letter and correspondence presentation | Tasks 16–17 | Existing output is acceptance-only unless representative approved examples demonstrate a real deficit. No mandatory new field or PDF/DOCX renderer is assumed. |
| Current full regression and migration/deployment identity | Tasks 35–36, 41, 43 | Trusted resolved candidate SHA, complete required CI jobs, immutable artifacts and exact migration execution; never fast CI alone. |
| External/provider/professional evidence | Tasks 08, 32–34, 39–43 | Real authorized observation and independent approval. Simulations, credentials, setup drafts and screenshots do not prove the gate. |
| Source-action parity and rollback host | Task 39 | Preserve updated Operations evidence and all retained Web source. No retirement without the actual stop gate and separate owner authorization. |

### D5. Implementation-ready and blocked portions

All 33 report IDs have an accountable task mapping. This does **not** mean every policy/provider/residual-contract choice has been resolved. The specification deliberately retains three essential-context portions:

1. Task 31's complete residual API success-schema/decoder census beyond the named, evidenced priority endpoint owners.
2. Task 37's exact activity API/UI caller contract and approved workload bounds.
3. Task 38's actual native close/opening/sealed-source handoff, which the supplied report classified as unverified rather than proven missing.

For these portions, read the specified source first, name the exact missing owners/contracts, and append a bounded amendment to this same specification or the existing owner task before implementation. Do not create speculative source paths or call the work complete from an investigation plan. Known, independently specified fixes can proceed without waiting for unrelated context.

---

## E. Handoff, sequencing, rollout and completion

### E1. Authorized execution workflow

**Issue → feature branch → atomic task(s) → focused local checks → fast PR CI → independent review → owner-authorized merge/release → exact-candidate full regression and applicable external gates.**

The release gate occurs **before production promotion**: the sequence above does not permit releasing first and checking afterward. Run/reuse full CI on the exact intended release source; if the final merge changes the candidate SHA, obtain evidence for that exact SHA under the established policy.

Use the existing issue/card aliases and deduplicated backlog. Do not create a parallel ledger with 33 new module claims. Each PR should close one coherent behavior or inseparable dependency group, not contain every task in this specification. No arbitrary file limit overrides a necessary coherent cross-layer change.

This delivery explicitly selects PRs over the root `AGENTS.md` direct-`master` default. Never push directly to master for this work. The designated PR-merge confirmation codeword is **`AUDITSPHERE-MERGE-APPROVED`**, supplied by the owner for AuditSphere. Its occurrence in this file is **not authorization**. Require the owner's explicit authorization/codeword for the identified PR/action, current required review/checks and applicable repository protections. Deployment, production permission/data changes, destructive migrations and other irreversible actions require their own explicit owner authorization. No such action occurs in SPEC_ONLY.

### E2. Practical execution order

The numeric dependency graph is authoritative within this spec. A productive ordering is:

| Workstream | Start with | Then | Do not wait for |
|---|---|---|---|
| Current intent/evidence defects | Tasks 01–08 | Relevant source/action acceptance | Unrelated professional accounting decisions |
| Ordinary workflow connections | Routing Task 09; FSLI/PBC/handover Tasks 11–14 | Invoice dispatch 10; estimates 15; acceptance 16–17 | Unrelated group rendering or hosting |
| Firm financial integrity | Source/receipts Tasks 03–05; policy 21 | Ledger reconciliation 22 | Native client books or group postings |
| Entity reporting | Methodology 19; policy binding 20 | Nil/indirect cash flow 23–24; annual comparatives 25 | Unsupported advanced profiles |
| Group integrity and output | Period 26; preserved pins 27 | Current eligibility 28; source consumption 29; package 30 | A second calculator or entity-book mutation |
| API/operations | Older discovery 18; contract census 31; activity trace 37 | Bounded missing owner amendments only | A new monitoring/event-sourcing platform |
| Live custody and release | Provider fence 08; checkpoint 32; release gate 35 | Archive 33; chosen route 36; independent restore 41 | Forced adoption of an unselected production platform |
| Acceptance closure | Policy/output acceptance and Tasks 38–42 progressively | Final profile 43 | Repeated unfiltered local suites |

Do not delay evidence design until the final task. A proposed code change should name its proving fixture before implementation. Conversely, do not duplicate an existing test solely because several report IDs depend on the same invariant.

### E3. Rollout and rollback checklist

For each affected capability, document the exact additive migration/contract version, deployment order, incompatible old caller/worker treatment, feature/capability gate if one already exists, verification source and recovery path. Introduce no speculative global feature-flag framework.

Retain legacy receipts, financial rows, approved pins, manifests and issued artifacts. New protections apply prospectively with explicit legacy/unverified classification and reviewed correction where required. Never claim old history contains proof that was not recorded. Do not silently convert historical local checkpoints to live custody or local freezes to observed SharePoint protection.

In the selected deployment, verify the new migration image before starting the exact migration execution. Failure, timeout or unknown outcome prevents incompatible API/worker rollout and traffic promotion. Reverting an application image does not undo financial data or forward schema changes. Use schema-compatible rollback, an approved forward repair, or a separately authorized/rehearsed restore.

### E4. Required final implementation evidence

An implementation handoff records: changed files and why; exact source/ref and relevant working changes; fulfilled acceptance IDs; actual commands and observed counts/results; intentionally omitted local checks and CI-owned suites; exact CI run/attempt/resolved source; remaining decisions/context/external blockers; data/contract migration and rollback; independent reviewer and owner approvals.

No card becomes complete merely because a task exists here, a class/route was added, a test compiled, or a partial run passed. Preserve existing T-card completion requirements and update only relevant observed facts in the existing execution records.

For a named release profile, each applicable requirement must end as **implemented and accepted with current evidence**, **approved deviation**, or **approved exclusion**. Unresolved mandatory `PARTIAL`, `UNVERIFIED` or blocked work prevents a complete closure claim. No completion percentage or unconditional “production-ready” verdict is assigned by this specification.

### E5. Verification performed for this document

Performed: reading the supplied report; read-only connected GitHub ref/diff, selected source/authority and tooling-contract inspection; checking this Markdown's task IDs, dependency order, gap mapping, preserved requirement/acceptance row counts and output integrity.

**NOT RUN — SPEC_ONLY:** repository builds, test suites/cohorts, installs/restores, code or inventory generators, EF tooling/migrations, services, workflow dispatches, live provider/tenant calls, deployment, Git writes and production operations. Existing historical report/CI observations remain attributed to their original source and time.

The artifact itself is a plan and evidence index. It is not implementation, a runtime reproduction, professional approval or permission to execute a protected action.

---

**END OF COMPLETE SPECIFICATION — 43 atomic tasks; 33 report work items; 91 preserved grouped STE rows; 12 preserved STE acceptance criteria.**
