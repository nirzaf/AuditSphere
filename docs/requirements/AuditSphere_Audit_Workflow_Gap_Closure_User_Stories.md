# AuditSphere — Audit Workflow Gap Closure
## Epic and detailed implementation user stories

| Document control | Value |
|---|---|
| Document version | 1.0 |
| Prepared | 20 September 2026 |
| Target repository | `nirzaf/AuditSphere` |
| Source project context | SteAuditTool |
| Source checklist | `Audit working process - Audit Tool New.docx` |
| Source scope | **20 audit sections; 165 individual procedures** |
| Backlog | **28 issue-ready stories**, grouped into 5 proposed milestones |
| Repository snapshot inspected | `master@ab7c4fe001a1b342880aae6bbca1bbc157c9716a` |
| Status | Partially implemented; local evidence is summarized in the execution ledger; not professional or production acceptance |
| Suggested repository location | `docs/requirements/AuditSphere_Audit_Workflow_Gap_Closure_User_Stories.md` |
| Repository changes made by this document task | Requirements documentation only; implementation evidence is tracked separately |

> **Epic outcome:** An authorized auditor can perform, evidence, review and conclude every applicable procedure in the uploaded checklist using AuditSphere, and the existing final release gate consumes those exact reviewed results. Reuse working capabilities; implement only demonstrated gaps. Do not confuse a template, database entity or generic workpaper with accepted end-to-end functionality.

## Contents

1. [Intent, scope and evidence boundaries](#intent)
2. [Source provenance and implementation baseline](#source-provenance)
3. [Shared acceptance contract](#shared-contract)
4. [Milestones and dependency order](#milestones)
5. [Detailed user stories](#stories)
6. [Minimal data and integration contracts](#data-contracts)
7. [Methodology decisions and external dependencies](#decisions)
8. [End-to-end acceptance scenarios](#acceptance)
9. [Definition of Done and coding-agent execution](#definition-of-done)
10. [Appendix A — exact source-procedure traceability](#traceability)
11. [References](#references)

---

<a id="intent"></a>
## 1. Intent, scope and evidence boundaries

### 1.1 Epic user story

**As an engagement partner and audit team, I want AuditSphere to provide approved audit programs, structured workpapers, supporting calculations and evidence-backed completion for the uploaded 20-section checklist, so that we can carry out the audit consistently and authorize a final report only after the required professional work and reviews are complete.**

The business requirement is **support for performing and documenting the work**, not an autonomous auditor. The application performs authorized calculations, retains evidence, presents exceptions and enforces workflow gates. Qualified people choose methodology, assess evidence, determine materiality, decide accounting/reporting treatment, conclude audit work and authorize reports.

### 1.2 What is source-derived and what is proposed

- **S1 / AWP requirements:** The uploaded document's actual procedures. Appendix A reproduces all 165 procedure statements in their original 20 sections, adding stable `AWP-SS-NN` IDs for traceability. Those IDs are assigned by this backlog, not present in the DOCX.
- **Repository contract:** The inspected `AGENTS.md` and its referenced .NET specification establish architecture, safeguards and existing intended workflows. Relevant repository files are linked at the inspected commit. [R1] [R2]
- **Proposed implementation design:** Story decomposition, new IDs, forms, field sets, logical states, test fixtures, rollout order and additional software safeguards are proposed here to operationalize S1. They are not quoted requirements or proof of already implemented behavior.

The prior conversational coverage assessment is a starting hypothesis, **not a claim that two sections are fully accepted or that 19 sections already work end-to-end**. In particular, sampling, planning completeness, all primary financial statements, disclosure completeness and release prerequisites require direct verification. The current specification already calls for confirmations, going concern and subsequent events; their presence in a specification does not prove their runtime implementation. [R4] [R5]

### 1.3 Scope

Implement missing or incomplete source-backed workflows: audit program library and tailoring; schedule/population reconciliation; manual-first selections and tests; confirmations; specialized account-area forms; narrow approved calculations; overall analytical, going-concern and subsequent-event assessments; complete financial-statement/disclosure review; misstatement evaluation; and integration into existing completion/release and archival evidence.

Planning and financial-statement sections remain in the backlog as **residual-gap verification and completion**, not permission to rebuild functioning modules. All source procedures must be represented and supported, including those whose evidence is obtained through an approved manual process.

Start within the existing approved single-entity financial-statement-audit profile. Do not silently apply this program to accounting-only, internal-audit or other assurance services. Runtime applicability must reflect approved engagement scope and source conditions; it cannot hide an unimplemented product capability.

### 1.4 Non-goals

No stack migration, repository rename, second ledger/ERP, new microservices, generic workflow/form designer, new event bus, or autonomous AI-generated audit decisions. Do not add unrelated infrastructure, security products or dependencies. Reuse the current Microsoft integrations rather than building replacement document storage, bank feeds or a new email platform.

This backlog does not introduce client bookkeeping, tax filing, operational payroll/inventory/asset systems, universal tax/legal rules, group consolidation, a universal ECL engine or an unvalidated statistical sampling method. Broad automation beyond the described approved helper calculations is outside scope.

No claim is made that the checklist exhausts professional standards or local law. Source coverage is not audit-methodology certification or proof of production readiness.

<a id="source-provenance"></a>
## 2. Source provenance and implementation baseline

### 2.1 S1 — uploaded audit working-process document

**File:** `Audit working process - Audit Tool New.docx`  
**Project location previously established:** `/SteAuditTool/Audit working process - Audit Tool New.docx`  
**SHA-256:** `034d3b5023f5b498f0953ef6fea2af8439d72cc107075bd3336e53405091d987`  
**Parsed source:** 8 pages; 20 numbered sections; 165 procedure paragraphs.  
**Traceability convention:** `AWP-<two-digit source section>-<two-digit procedure ordinal>`.

Every source paragraph has exactly one primary story and acceptance-criterion owner in Appendix A. Shared stories provide supporting behavior without duplicating primary ownership. Exact source wording is retained there; explanatory story text is an implementation interpretation.

### 2.2 Repository facts inspected for this backlog

The inspected `AGENTS.md` points to the consolidated `docs/SPECIFICATION.md` (the v5.0 build contract), and specifies the existing .NET/Blazor/EF Core/PostgreSQL approach. The specification is an implementation contract, not executed production evidence. [R1] [R2]

The inspected audit domain includes `MaterialityAssessment`, `AuditRisk`, `AuditProcedure`, `PopulationVersion`, `Workpaper`, `WorkpaperDraft`, `WorkpaperSubmission` and `Finding`. Reuse these where appropriate. Their existence does not establish that every source procedure has a UI, command, sufficient evidence model or passing acceptance scenario. [R3]

The inspected financial calculator already builds package lines and deterministic hashes from mappings, adjustment plans and supplementary cash-flow/disclosure inputs. This is a reuse boundary, not proof that all rendered statements and notes meet the source's review requirements. [R6]

The existing specification requires manual-first sampling, a confirmation register, specialized programs, exact submitted evidence and approval invalidation. It also distinguishes monetary findings, management decisions, EQR, signed artifacts, provider protection and issuance/delivery. Preserve those semantics. [R4] [R5]

**Verification limit:** This document was prepared from the complete uploaded checklist and targeted repository reads. The current repository has since verified local accounting and audit slices; see `docs/execution/current-slice.md` and `docs/execution/status.json` for executed evidence. No local evidence substitutes for live-provider, professional-approval, production-recovery, or independent-review acceptance. `AS-AUD-001` must re-read current repository state before implementation and resolve any behavior already completed since this snapshot.

### 2.4 Current implementation evidence

At `master@ab7c4fe`, the repository records **186/186 PostgreSQL-backed tests passing with 0 skipped**, **44 applied migrations**, a successful loopback restore rehearsal, and a zero-warning build. The accounting slice includes client accounting profiles, versioned trial-balance and GL ingestion, completeness bridges, reconciliations, specialist workbenches, package projections, restatement lineage, bounded same-currency consolidation, journal lineage, close checks, and immutable package-review decisions. Remaining stories are not implicitly complete: user-facing workflow depth, advanced accounting methods, and external acceptance must still be verified in the execution ledger.

### 2.3 Reuse versus gap rule

For every story, the implementation agent must first record one of:

| Disposition | Meaning | Required next action |
|---|---|---|
| `REUSE_VERIFIED` | Existing behavior satisfies the specified acceptance criteria | Link actual code and executed evidence; avoid rebuilding it |
| `DELTA_REQUIRED` | A demonstrated behavior is absent or incomplete | Implement the smallest complete missing slice |
| `VERIFICATION_REQUIRED` | The available evidence is insufficient to decide | Inspect/run relevant existing checks; do not guess |
| `BLOCKED_EXTERNAL` | A required owner approval, provider capability or authorized fixture is absent | Record exact blocker, owner and impact; do not simulate acceptance |

All stories in this document are **proposed/unaccepted** at publication. The “gap” descriptions identify what needs establishing, not a fresh exhaustive code audit or a guarantee that no equivalent feature exists.

<a id="shared-contract"></a>
## 3. Shared acceptance contract

These requirements apply to every relevant story and are part of its acceptance criteria. A story cannot waive them through a local implementation shortcut.

### 3.1 Identity, permissions and professional authority

Use existing firm/client/engagement scoping, actor resolution and approved role policy. Enforce authorization inside commands, queries, downloads, exports, counts and search—not only by hiding buttons. Link source data, tests, workpapers and findings only within permitted scope. Restricted payroll, fraud concerns and internal review deliberations must not leak into client-facing surfaces.

Separate preparer, reviewer, management and partner actions. Apply required independence/segregation; a platform administrator is not automatically an audit decision-maker. Human evidence assessments and professional conclusions must identify the actual authorized decision-maker and exact reviewed input versions.

### 3.2 Evidence and immutable submissions

Every test/calculation must trace to the source receipt, stable source rows, procedure, exact supporting document snapshots, work performed, results, exceptions and conclusion. A live URL or file name alone is insufficient provenance. A checksum establishes byte identity, not truth, authenticity or sufficiency.

Working drafts remain editable under the existing revision and recovery controls. Submission freezes both narrative and all selected structured results, inputs and evidence references. Never retain an immutable narrative that points to mutable calculation rows as though the calculation were frozen. Corrections create new revisions; reviewed or issued history is preserved.

### 3.3 States and applicability

Map the following **logical concepts** onto the existing state model; do not replace state machines merely to match these labels:

```text
Applicability: Pending -> Applicable OR N/A proposed -> N/A approved/rejected
Work: Not started -> In progress -> Submitted -> In review
      -> Changes required -> New submission -> Reviewed for version
Exception: Unresolved -> Investigated -> Supported reviewer disposition
Area: Incomplete -> Review pending -> Accepted for exact dependencies
```

Missing evidence, unperformed work, unsupported calculations, unanswered queries and failed procedures are not approved N/A and are not a passed test. A supported limitation or reporting consequence remains distinguishable from successful testing. Conditions such as “where applicable” are evaluated explicitly and retain rationale/authority.

### 3.4 Calculation and source controls

Use the repository's existing decimal/money policy and declared precision. Keep presentation rounding distinct from persisted calculation precision. Preserve method/version, input dataset versions, sign conventions, currency, units, period and calculation results. Signed effects and non-negative magnitudes are different fields/concepts; do not reinterpret legacy amounts silently.

No fixed audit materiality rate, age threshold, ECL loss rate, cut-off window, sample size, tax rule, depreciation convention or interest basis is prescribed by S1. Use approved, recorded parameters. An absent required method/input produces a visible blocked/unsupported result, never a fabricated zero or invented default.

For unsupported native methods, a reviewed external calculation may be used only when the approved methodology permits it, with exact evidence and documented independent checking. The UI must state that route honestly; it is not proof that a native calculator exists.

### 3.5 Revision, transaction and downstream impact

Reuse the existing expected-revision, command idempotency, ordered guards, transaction ownership and safety-generation protocol. All mutations affecting selected audit evidence or completion must participate. Source replacement, changed tests, materiality, findings, disclosures, representations or professional decisions require appropriate current impact evaluation.

Preserve historical decisions and mark their **current applicability** stale when necessary. Do not hold database locks while hashing/parsing files, contacting providers, rendering documents or waiting for humans. Retried external work must use existing durable operations and actual outcome evidence. [R4] [R5]

### 3.6 Completion and integration truth

A zero reconciliation residual is not sufficient audit evidence. A receipt is not accepted evidence; a confirmation non-response is not agreement; management's acceptance of an adjustment is not proof of posting; and a completed checklist is not approval to issue.

Required professional conclusions must be reviewed before completion. A finding can remain uncorrected with supported reporting treatment; do not force every finding to zero. Separate current-year audit conclusions from future management remediation actions.

Provider and signing boundaries must not be weakened. Report local implementation, professional acceptance and live integration acceptance separately. Local fixtures are synthetic test data, never substitutes for real authorized provider evidence.

### 3.7 Minimal implementation and tests

Reuse existing entities, services, route conventions, tests and deployment approach. Add only necessary fields/child records and focused calculations. No new dependency or general abstraction is justified merely by a story heading. Apply safe migrations preserving historical evidence; make legacy unverified state explicit rather than inventing backfilled approvals.

Every story is a vertical slice: relevant form/read model, authorized command, persistence, validation, evidence/review linkage, required output and focused tests. Acceptance criteria are not a mandate for one test file per checkbox. Reuse existing scenarios and fixtures wherever they actually prove the changed behavior.

<a id="milestones"></a>
## 4. Proposed milestones and dependency order

These milestone names and story IDs are **proposals**, not existing GitHub milestone or issue numbers. Reconcile them with the repository backlog before creating anything. Priority “Blocker” denotes shared/gating functionality; “High” denotes required source scope, not optional work.

| Milestone | Objective | Stories | Exit boundary |
|---|---|---|---|
| GAP-M1 | Source baseline and shared audit execution | 001–007 (007 precedes 005) | Approved source program; scope/evidence/review, reconciled sources, planning, manual selections and confirmations. |
| GAP-M2 | Core fieldwork and audit-difference control | 026 first; then 008–013 | Evidence-backed difference handling, Cash & Bank, Receivables/ECL, Inventory, Revenue and Payables. |
| GAP-M3 | Remaining account areas and journal/fraud work | 014–021 | Assets, Expenses, Payroll, Loans, Equity, Related Parties, Tax and client-journal procedures. |
| GAP-M4 | Overall review and final financial statements | 022–025 | Analytical Review, Going Concern, Subsequent Events and all final statement/disclosure review requirements. |
| GAP-M5 | Completion integration and acceptance | 027–028 | Source-complete local/professional evidence, exact-version completion gates and separately reported live acceptance. |

**Default dependency-safe execution sequence:**

```text
001 -> 002 -> 003 -> 004 -> 007 -> 005 -> 006 -> 026
    -> 008 -> 009 -> 010 -> 011 -> 012 -> 013
    -> 014 -> 015 -> 016 -> 017 -> 018 -> 019 -> 020 -> 021
    -> 022 -> 023 -> 024 -> 025 -> 027 -> 028
```

Numbers above abbreviate `AS-AUD-xxx`. Numbering is for stable identity, not execution order: planning supports selection, and the shared misstatement evaluation is deliberately implemented early. The specific prerequisites in each story are the hard implementation dependencies; references to later area outputs are final acceptance relationships, not permission to duplicate their engines.

Respect any stricter existing repository/WBS sequencing and owner authorization. Authorized independent local work can be accepted locally while a live provider is blocked, but that does not bypass an existing gate or complete a downstream production milestone.

### 4.1 Story index

| Story | Title | Priority | Milestone |
|---|---|---|---|
| [AS-AUD-001](#as-aud-001) | Establish the source-to-code delta and avoid duplicate implementation | Blocker | GAP-M1 |
| [AS-AUD-002](#as-aud-002) | Publish a versioned audit-program library containing the 20 source sections | Blocker | GAP-M1 |
| [AS-AUD-003](#as-aud-003) | Tailor engagement procedures and execute evidence-backed review lifecycles | Blocker | GAP-M1 |
| [AS-AUD-004](#as-aud-004) | Reconcile source schedules, lead schedules and audit populations to the correct GL/TB | Blocker | GAP-M1 |
| [AS-AUD-005](#as-aud-005) | Record auditable selections, item tests, cut-off and subsequent-transaction matching | Blocker | GAP-M1 |
| [AS-AUD-006](#as-aud-006) | Manage controlled external confirmations and alternative procedures | Blocker | GAP-M1 |
| [AS-AUD-007](#as-aud-007) | Close planning, opening-balance and risk-to-program coverage gaps | Blocker | GAP-M1 |
| [AS-AUD-008](#as-aud-008) | Implement Cash & Bank audit workpapers and reconciliation testing | High | GAP-M2 |
| [AS-AUD-009](#as-aud-009) | Implement Trade Receivables ageing, confirmations and collection/cut-off testing | High | GAP-M2 |
| [AS-AUD-010](#as-aud-010) | Support documented ECL recalculation and provision adequacy review | High | GAP-M2 |
| [AS-AUD-011](#as-aud-011) | Implement Inventory count, costing, condition and NRV workpapers | High | GAP-M2 |
| [AS-AUD-012](#as-aud-012) | Implement Revenue / Sales audit testing and cut-off review | High | GAP-M2 |
| [AS-AUD-013](#as-aud-013) | Implement Purchases & Trade Payables and search for unrecorded liabilities | High | GAP-M2 |
| [AS-AUD-014](#as-aud-014) | Implement Fixed Assets roll-forward, verification and depreciation testing | High | GAP-M3 |
| [AS-AUD-015](#as-aud-015) | Implement Expenses analytics, vouching, classification and cut-off testing | High | GAP-M3 |
| [AS-AUD-016](#as-aud-016) | Implement Payroll employee, recalculation and joiner/leaver testing | High | GAP-M3 |
| [AS-AUD-017](#as-aud-017) | Implement Loans & Borrowings schedules, interest and covenant testing | High | GAP-M3 |
| [AS-AUD-018](#as-aud-018) | Implement Equity / Share Capital movement and retained-earnings review | High | GAP-M3 |
| [AS-AUD-019](#as-aud-019) | Implement Related Parties register, transaction testing and disclosure linkage | High | GAP-M3 |
| [AS-AUD-020](#as-aud-020) | Implement Tax & Statutory Liabilities audit schedules and disclosure review | High | GAP-M3 |
| [AS-AUD-021](#as-aud-021) | Implement client Journal Entries & Fraud risk-oriented testing | High | GAP-M3 |
| [AS-AUD-022](#as-aud-022) | Implement reproducible Analytical Review with documented investigations | High | GAP-M4 |
| [AS-AUD-023](#as-aud-023) | Implement Going Concern assessment, forecast review and human conclusion | High | GAP-M4 |
| [AS-AUD-024](#as-aud-024) | Implement Subsequent Events register and reviewed-through-date coverage | High | GAP-M4 |
| [AS-AUD-025](#as-aud-025) | Close Financial Statements & Disclosures residual review gaps | High | GAP-M4 |
| [AS-AUD-026](#as-aud-026) | Implement audit-difference evaluation, corrected/unadjusted schedules and final-TB verification | Blocker | GAP-M2 |
| [AS-AUD-027](#as-aud-027) | Complete final audit gates, representations, professional review and exact report release | Blocker | GAP-M5 |
| [AS-AUD-028](#as-aud-028) | Prove checklist coverage, archive reproducibility and user acceptance | Blocker | GAP-M5 |

<a id="stories"></a>
## 5. Detailed user stories

**Read each story with Sections 3, 7 and 9.** Every acceptance checkbox starts unchecked. Appendix A supplies exact source text and the primary owner for each AWP item. Enabling stories 001–006 and verification story 028 are proposed implementation work supporting S1 and the repository contract.


<a id="as-aud-001"></a>
### AS-AUD-001 — Establish the source-to-code delta and avoid duplicate implementation

**User story:** As a technical lead, I want to reconcile every source procedure with the current implementation and existing backlog before changing code, so that we deliver only the missing behavior and retain evidence for capabilities already present.

**Priority:** Blocker  
**Proposed milestone:** GAP-M1  
**Depends on:** None; first prerequisite.  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** Shared enabling / verification design; see S1 coverage and repository contract.

**Gap to establish:** The earlier coverage discussion was directional, not an executed acceptance audit. A class, page, sample title or README claim does not establish a complete workflow.

**Reuse boundary:** Read existing root/nested AGENTS.md, the root .NET specification, execution ledger, current source and relevant tests. This is not an architecture migration.

**Minimum information:** Source document hash; baseline commit; AWP source ID; relevant specification section; existing issue/PR; code entry points; test/scenario evidence; gap disposition; responsible owner.

**Scope and implementation notes**

- Inventory these 28 story IDs against existing issues and milestones. Link an equivalent existing issue instead of creating duplicate work.
- For each AWP item, distinguish reusable implementation, missing user workflow, missing calculation, missing validation, missing integration and unverified acceptance.
- Retain the document's 20-section order and exact procedure text. Preserve conditions such as “where applicable”, “where appropriate” and “where available”.

**Acceptance criteria**

- [ ] **AS-AUD-001-AC01:** Register all 165 AWP procedure IDs in Appendix A with the original file SHA-256 and one primary story/acceptance-criterion owner; no procedure is silently removed.
- [ ] **AS-AUD-001-AC02:** Record a reviewed delta for every story: REUSE_VERIFIED, DELTA_REQUIRED, VERIFICATION_REQUIRED or BLOCKED_EXTERNAL, with the actual commit and evidence. No story starts as completed solely because this backlog exists.
- [ ] **AS-AUD-001-AC03:** For claimed reuse, identify the command/UI path, persisted data and relevant passing test or witnessed acceptance result. Mark unexecuted checks as NOT_RUN.
- [ ] **AS-AUD-001-AC04:** Confirm the actual target is nirzaf/AuditSphere; preserve existing AuditSphereOps namespaces and files without renaming the repository or touching nirzaf/steauditsphereops.
- [ ] **AS-AUD-001-AC05:** Detect and record overlaps with existing engineering hardening and provider/signing work. Add links and residual acceptance criteria rather than a parallel subsystem.
- [ ] **AS-AUD-001-AC06:** Record unresolved methodology inputs from Section 7. Missing professional policy is an explicit blocker for the affected calculation/approval, not a guessed default.
- [ ] **AS-AUD-001-AC07:** Where S1 and an existing specification differ, record the discrepancy and keep the stricter applicable gate until the authorized owner approves a documented resolution; specifically address all-review-points clearance.

**Focused verification**

- Validate the manifest has 20 sections, 165 unique AWP IDs and one primary owner per ID.
- Inspect at least the planning, source/population, workpaper submission, adjustments, FS and release paths before claiming they can be reused; run only relevant existing tests.
- Demonstrate that an unavailable provider or an unexecuted test cannot be entered as verified acceptance.

**Deliverable:** A reviewed traceability/delta record and reconciled issue plan. Do not add a new application feature or dependency for this story.

---

<a id="as-aud-002"></a>
### AS-AUD-002 — Publish a versioned audit-program library containing the 20 source sections

**User story:** As an audit methodology owner, I want to maintain approved, versioned audit programs based on the uploaded checklist, so that engagements start from the same complete procedure library without changing historical audit files.

**Priority:** Blocker  
**Proposed milestone:** GAP-M1  
**Depends on:** [AS-AUD-001](#as-aud-001)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** Shared enabling / verification design; see S1 coverage and repository contract.

**Gap to establish:** Existing risks/procedures/workpapers do not by themselves prove that the full 20-section, 165-procedure program is seeded, versioned and usable.

**Reuse boundary:** Reuse compatible template/versioning and approval mechanisms. Keep CE/RV acceptance questionnaires distinct from audit-program procedures; do not repurpose those question codes.

**Minimum information:** Program ID/version/status; source hash; section number/title; stable AWP ID; source wording; approved operational guidance; applicability condition; expected evidence; preparer/reviewer role; calculation/form type when needed.

**Scope and implementation notes**

- Seed the entire source checklist as a draft library release. The source wording and approved firm guidance remain separately identifiable.
- Allow controlled draft edits and publication by the existing authorized methodology role. Map roles to existing authorization policy instead of granting ordinary staff publication rights.
- Use small, validated template data. Do not add a general form builder, workflow designer, script execution engine or per-area service.

**Acceptance criteria**

- [ ] **AS-AUD-002-AC01:** The seed contains exactly the 20 source sections and 165 source procedures, preserving source order, wording, AWP IDs and conditional phrases.
- [ ] **AS-AUD-002-AC02:** Seeding is idempotent by program/version/AWP ID. Re-running it does not duplicate procedures or overwrite an approved version.
- [ ] **AS-AUD-002-AC03:** Publishing requires a named authorized approver and completeness validation. A draft/unapproved program cannot be presented as approved methodology.
- [ ] **AS-AUD-002-AC04:** An engagement pins a published version. Publishing a later library version does not mutate existing engagement procedures, evidence or approvals.
- [ ] **AS-AUD-002-AC05:** A change to an adopted engagement program requires a preview, authorized adoption and impact assessment; retain the previous version and changed-procedure links.
- [ ] **AS-AUD-002-AC06:** Source procedures cannot disappear through draft editing: retired/changed items retain provenance and an approved disposition. Firm-added procedures use distinct IDs and cannot masquerade as source text.
- [ ] **AS-AUD-002-AC07:** Conditional source language produces an applicability decision, not an automatic unchecked omission or a forced universal obligation.
- [ ] **AS-AUD-002-AC08:** Unauthorized library publication and cross-firm access fail at the command boundary; rejected commands leave no partial program version.

**Focused verification**

- Seed twice and assert stable 165-item content; compare normalized wording to the source manifest.
- Publish v1, instantiate an engagement, publish v2 and verify v1 evidence remains unchanged.
- Reject publication with a missing source item, duplicate ID or unauthorized actor.

**Deliverable:** A navigable, approved-version-aware library and a deterministic seed. Methodology approval remains a human decision.

---

<a id="as-aud-003"></a>
### AS-AUD-003 — Tailor engagement procedures and execute evidence-backed review lifecycles

**User story:** As an engagement manager, I want to instantiate, assign, tailor, perform and review each applicable procedure against exact evidence versions, so that completion reflects actual work rather than empty folders, free-text labels or unchecked assumptions.

**Priority:** Blocker  
**Proposed milestone:** GAP-M1  
**Depends on:** [AS-AUD-002](#as-aud-002)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** Shared enabling / verification design; see S1 coverage and repository contract.

**Gap to establish:** Generic workpaper creation/submission exists, but full checklist applicability, structured test snapshots and section-level completeness must be verified or added.

**Reuse boundary:** Extend AuditProcedure, Workpaper, WorkpaperSubmission, EvidenceLink, ReviewPoint and ApprovalService; reuse current draft recovery and revision/generation protections. [R3] [R4]

**Minimum information:** Program-instance/version; AWP procedure ID; applicability/reason/approver; scope; owner/due date; actual risk/assertion links; workpaper and structured-result revision; evidence snapshots; conclusion; review decisions; blocker disposition.

**Scope and implementation notes**

- Use a minimal engagement-program aggregate or association only where current entities cannot retain template/applicability identity.
- Keep procedure disposition separate from workpaper content and historical review decisions. Map logical states to existing state names rather than creating a competing workflow.
- Provide staff views for 20 areas and procedure details, evidence, exceptions and next action. Client access remains restricted to approved requests and responses.

**Acceptance criteria**

- [ ] **AS-AUD-003-AC01:** Creating the same program instance twice with the same request identity produces one result. Only the selected engagement and adopted version receive procedures.
- [ ] **AS-AUD-003-AC02:** Each source procedure is Applicable, NotApplicablePendingReview or NotApplicableApproved. Approved N/A needs rationale and authority; missing evidence or a failed procedure is not N/A.
- [ ] **AS-AUD-003-AC03:** An applicable procedure requires work performed, evidence or an explicitly reviewed evidence limitation, results, conclusion and the required current review before acceptance. A limitation is not a successful test.
- [ ] **AS-AUD-003-AC04:** Submission freezes the structured results and exact source/document/calculation revisions together with the narrative; changing a working calculation cannot alter the reviewed snapshot.
- [ ] **AS-AUD-003-AC05:** Source replacement, selected evidence change, revised materiality or changed conclusions invalidate current applicability using existing safety generations and impact handling, without erasing historical approvals.
- [ ] **AS-AUD-003-AC06:** Do not invent a risk GUID to satisfy AuditProcedure.RiskId. Link a real assessed risk for risk-driven work; support genuinely non-risk-driven completion tasks through a small explicit engagement/workpaper association if needed.
- [ ] **AS-AUD-003-AC07:** Authorized reviewer clearance, changes requested, resubmission and approved N/A retain actor/time/reason and enforce applicable preparer/reviewer separation. A client cannot clear internal audit points.
- [ ] **AS-AUD-003-AC08:** Area progress separately reports applicable total, reviewed procedures, approved N/A, pending review, limitations and blockers; N/A is never counted as testing performed.
- [ ] **AS-AUD-003-AC09:** Generation-aware release evaluation includes these new procedure/result dependencies; a background refresh delay cannot leave a changed area release-ready.
- [ ] **AS-AUD-003-AC10:** Prior-year roll-forward copies references and proposed procedures only. Current-year applicability, tests, evidence relevance, conclusions and approvals reset for fresh review.

**Focused verification**

- Exercise working -> submitted -> reviewed -> revised -> stale -> re-reviewed with one calculation-backed workpaper.
- Reject stale saves, self-clearance where separation is required, cross-engagement evidence links and fake N/A completion.
- Change an input while a completion evaluation is pending; verify current authorization does not accept the old evaluation.

**Deliverable:** A reusable procedure execution and review workflow across every audit area, not 20 independently implemented workflow engines.

---

<a id="as-aud-004"></a>
### AS-AUD-004 — Reconcile source schedules, lead schedules and audit populations to the correct GL/TB

**User story:** As an audit preparer, I want to preserve, validate and reconcile source schedules before selecting and testing their records, so that all account-area work uses the correct entity, period, currency and complete stated population.

**Priority:** Blocker  
**Proposed milestone:** GAP-M1  
**Depends on:** [AS-AUD-003](#as-aud-003)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** Shared enabling / verification design; see S1 coverage and repository contract.

**Gap to establish:** TB and population foundations exist; storing a row count or total is not proof of schedule-level reconciliation or usable immutable source rows.

**Reuse boundary:** Reuse existing PBC intake, SourceReceipt, document snapshots, dataset validation, PopulationVersion, mapping and controlled adjustment inputs. Client GL/TB must remain separate from the firm ledger. [R2] [R3]

**Minimum information:** Receipt/hash/parser; schedule type; source/entity/period/as-of date; raw row ID and normalized values; currencies and signs; reconciliation target/version; source/TB totals; exceptions and disposition; approved population revision.

**Scope and implementation notes**

- Implement only the missing shared source-row and reconciliation capability needed by the area stories. Use existing supported imports and structured entry; do not create 20 independent parsers.
- A lead schedule maps selected accounts to an area and reconciles opening, movement and closing values when those inputs exist.
- Retain file formats the current importer actually supports. An unsupported workbook may be stored as evidence and require a reviewed values-only export; do not execute formulas or macros.

**Acceptance criteria**

- [ ] **AS-AUD-004-AC01:** A schedule is identified by source receipt, entity, engagement, period/as-of date, type and currency. Ambiguous dates, missing mandatory IDs and incompatible scope are held for resolution.
- [ ] **AS-AUD-004-AC02:** Preserve stable source-row identities, leading-zero codes, original values and transformations; duplicate file ingestion cannot silently append the same records twice.
- [ ] **AS-AUD-004-AC03:** Compute count/control-total reconciliations against the selected GL/TB version, with stated sign and inclusion/exclusion rules. Unexplained differences prevent final population approval.
- [ ] **AS-AUD-004-AC04:** Retain genuine negative balances, credit notes and reversals with explicit classifications. A non-negative control-total field cannot be used to silently discard signed rows; use compatible signed/absolute totals with declared meaning.
- [ ] **AS-AUD-004-AC05:** A source change creates a new version and an additions/removals/changes comparison; selections and reviews remain bound to their original version and require impact review.
- [ ] **AS-AUD-004-AC06:** Opening balances link to the exact prior-year audited values, with separate restatement/reclassification entries and auditor explanations instead of forced equality.
- [ ] **AS-AUD-004-AC07:** Do not sum unrelated currencies. Any approved conversion keeps source amounts, rate/date/source, converted amount and rounding evidence; unsupported conversion remains visibly unperformed.
- [ ] **AS-AUD-004-AC08:** Large source listings use bounded ingestion and server-side queries, respecting current approved size/time limits; no unbounded tracked EF aggregate or browser grid.
- [ ] **AS-AUD-004-AC09:** Completeness/reliability is a recorded auditor decision. Matching numerical totals alone must not set the population or evidence conclusion to sufficient.

**Focused verification**

- Reconcile an exact-match schedule and an unexplained variance; preserve zero/credit rows and leading-zero account IDs.
- Replace a source and prove the previous approved population and sample row identities remain reproducible.
- Reject mixed currency aggregation, out-of-period use and cross-scope references; test only newly changed parser behaviors.

**Deliverable:** Reusable schedule/lead-schedule reconciliation and immutable row provenance serving all account-area stories.

---

<a id="as-aud-005"></a>
### AS-AUD-005 — Record auditable selections, item tests, cut-off and subsequent-transaction matching

**User story:** As an audit preparer, I want to select records with reasons and document repeatable item-level tests against approved populations, so that value/risk-based samples, cut-off tests and subsequent receipt/payment work retain their exact scope and evidence.

**Priority:** Blocker  
**Proposed milestone:** GAP-M1  
**Depends on:** [AS-AUD-004](#as-aud-004), [AS-AUD-007](#as-aud-007)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** Shared enabling / verification design; see S1 coverage and repository contract.

**Gap to establish:** Population metadata and generic workpapers are not a completed sampling engine. The existing specification requires manual-first selection and controlled item testing. [R4]

**Reuse boundary:** Extend the current population/workpaper model with only the missing selection and item-result records; use existing materiality/risk versions and findings.

**Minimum information:** Plan/objective/assertion; mode; population version; materiality/risk references; selection rationale and date window; selected stable row IDs; value coverage; evidence; expected/observed period; subsequent matched amounts; exception and follow-up.

**Scope and implementation notes**

- Support 100% examination, specific-item selection and an approved manual/non-statistical plan. Numerical statistical selection is excluded unless an existing independently validated method is explicitly adopted.
- Use the same item-test component for invoices, transactions, employees and assets, with small typed field sets for each use.
- Matching proposals can assist the preparer but do not automatically prove collectability, completeness, occurrence or period correctness.

**Acceptance criteria**

- [ ] **AS-AUD-005-AC01:** Selection records the approved population/version, procedure, rationale, selected row IDs and reviewer. Selection totals/counts reproduce from those IDs.
- [ ] **AS-AUD-005-AC02:** Value/risk selection criteria and any manual additions/exclusions are preserved. Duplicate selections cannot inflate coverage; zero/negative records are treated explicitly.
- [ ] **AS-AUD-005-AC03:** An unavailable document leaves a pending test or recorded limitation/alternative procedure. Replacement retains the original item, reason and authorized review; no silent sample substitution.
- [ ] **AS-AUD-005-AC04:** Each item records work/evidence, performer/date, result, exception amount when meaningful, contradictory evidence, follow-up and reviewer conclusion.
- [ ] **AS-AUD-005-AC05:** Cut-off tests retain the year-end, approved before/after window, posting date, invoice date, delivery/receipt/service date and auditor-determined expected period. Posting date alone is not the recognition rule.
- [ ] **AS-AUD-005-AC06:** Subsequent receipts/payments allow partial and many-to-many matches with stable bank/source IDs, dates, amounts, currency and unmatched balances; allocations cannot exceed the supported payment or target balance without explicit reconciliation.
- [ ] **AS-AUD-005-AC07:** Specific-item or non-statistical selections never produce automatic population-wide error projection or statistical assurance claims.
- [ ] **AS-AUD-005-AC08:** A changed population, risk, materiality or selected evidence triggers impact review. It does not quietly reselect items or overwrite prior test results.
- [ ] **AS-AUD-005-AC09:** All item results selected for review are included in the immutable submission and area completion checks; an untested selected item remains visible.

**Focused verification**

- Select high-value and manually chosen rows; retry selection and verify no duplicate coverage.
- Test a year-end invoice with delivery after year-end and an unmatched selected item.
- Match one payment to several invoices and several payments to one invoice; reject over-allocation.
- Verify non-statistical testing emits no unsupported projection or assurance percentage.

**Deliverable:** One shared, manual-first selection/testing capability, with reusable cut-off and subsequent-match forms.

---

<a id="as-aud-006"></a>
### AS-AUD-006 — Manage controlled external confirmations and alternative procedures

**User story:** As an audit senior, I want to control confirmation requests, validate respondents, reconcile responses and track non-responses, so that bank, customer, supplier, financier and related-party confirmation work is traceable and not confused with client-supplied correspondence.

**Priority:** Blocker  
**Proposed milestone:** GAP-M1  
**Depends on:** [AS-AUD-004](#as-aud-004)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** Shared enabling / verification design; see S1 coverage and repository contract.

**Gap to establish:** No dedicated confirmation workflow was established in the prior review. The existing specification already requires a confirmation register; implement its missing behavior rather than treating it as a new platform. [R4]

**Reuse boundary:** Reuse PBC/evidence acquisition, document snapshots, existing authorized communications/durable operations and review mechanisms. Keep confirmation delivery distinct from ordinary client upload.

**Minimum information:** Confirmation ID/type; subject/account/balance/date/currency; independent contact-validation basis; authorized channel; approved request snapshot; send evidence; response/source/receipt; differences; alternative procedures; conclusion/reviewer.

**Scope and implementation notes**

- Provide a manual controlled-send/receipt register first where approved by firm methodology; an authorized staff member can record independently supported dispatch and response evidence.
- Integrate automated delivery only through an already approved provider. This story does not introduce a new email service or authorize sending requests.
- Use explicit states such as Draft, ApprovedForDispatch, Sent, ResponseReceived, AuthenticityReview, Reconciled, AlternativeWorkRequired and Concluded; keep overdue as a computed flag.

**Acceptance criteria**

- [ ] **AS-AUD-006-AC01:** A request is linked to the exact account/balance/population and confirmation date; expected currency, objective and respondent are recorded before approval.
- [ ] **AS-AUD-006-AC02:** Record the contact-validation source and reviewer. Client-forwarded responses remain identified as such and cannot silently become independently authenticated direct replies.
- [ ] **AS-AUD-006-AC03:** Sent status requires actual dispatch evidence. A draft or queued operation is not Sent; retries cannot create duplicate dispatch records or provider effects.
- [ ] **AS-AUD-006-AC04:** ResponseReceipt stores the received version, origin/channel and receipt date. Authenticity/reliability assessment and reconciliation remain explicit reviewer actions.
- [ ] **AS-AUD-006-AC05:** Differences preserve book value, confirmed value, reconciling items, evidence and unresolved residual. Equality of totals alone does not conclude the confirmation.
- [ ] **AS-AUD-006-AC06:** No response remains outstanding or moves to AlternativeWorkRequired; it is never agreement, zero difference or automatic success.
- [ ] **AS-AUD-006-AC07:** An alternative procedure links its purpose, tests, supporting evidence and conclusion. The auditor assesses adequacy without labelling alternative work as a direct response.
- [ ] **AS-AUD-006-AC08:** Bank, customer, supplier, loan and related-party forms share this register; each area can show unresolved cases and accepted alternatives.
- [ ] **AS-AUD-006-AC09:** Portal views cannot expose restricted counterparties, internal assessments or confirmation credentials. Provider-dependent tests remain BLOCKED_EXTERNAL until authorized evidence exists.

**Focused verification**

- Complete a directly received response with a reconciling item and review evidence.
- Record a client-forwarded response and a non-response; ensure neither becomes direct confirmation success.
- Run an approved alternative procedure and verify its distinct conclusion and traceability.
- Test idempotent dispatch recording and cross-client access denial without real external sends.

**Deliverable:** A shared confirmation register and review workflow. Live dispatch acceptance is separate from local register acceptance.

---

<a id="as-aud-007"></a>
### AS-AUD-007 — Close planning, opening-balance and risk-to-program coverage gaps

**User story:** As an engagement manager, I want to complete an evidence-backed planning pack and link the audit strategy to significant balances, risks and procedures, so that existing materiality/risk records become a complete planning workflow without being rebuilt.

**Priority:** Blocker  
**Proposed milestone:** GAP-M1  
**Depends on:** [AS-AUD-004](#as-aud-004)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-01-01`, `AWP-01-02`, `AWP-01-03`, `AWP-01-04`, `AWP-01-05`, `AWP-01-06`, `AWP-01-07`, `AWP-01-08`, `AWP-01-09`

**Gap to establish:** Materiality and risk entities exist, but complete planning-document intake, opening-balance agreement, approved strategy and risk-to-procedure coverage remain acceptance items, not assumed completion.

**Reuse boundary:** Reuse engagement/acceptance data, existing MaterialityAssessment, AuditRisk, approved source datasets, workpapers and program instances. [R2] [R3]

**Minimum information:** Corporate record snapshots; prior audited package/report; current TB/draft FS; opening-balance bridge; business/revenue/system understanding; significant accounts/classes; risk/assertion coverage; materiality revision; strategy approval.

**Scope and implementation notes**

- Add only the missing planning forms and linkages. Do not create a second client master, materiality calculator or acceptance engine.

**Acceptance criteria**

- [ ] **AS-AUD-007-AC01:** Record company registration documents and basic information, with source version, entity identity and current relevance review. — Source: `AWP-01-01`
- [ ] **AS-AUD-007-AC02:** Link the exact prior-year audited financial statements and audit report, distinguishing unavailable prior evidence from documents not yet requested. — Source: `AWP-01-02`
- [ ] **AS-AUD-007-AC03:** Link the current-year Trial Balance and draft financial statements, displaying preliminary/accepted status and the versions actually used for planning. — Source: `AWP-01-03`
- [ ] **AS-AUD-007-AC04:** Produce an opening-balance agreement against prior-year audited values; preserve differences, restatement/reclassification explanations and reviewer conclusions. — Source: `AWP-01-04`
- [ ] **AS-AUD-007-AC05:** Record the auditor's understanding of the business, major revenue streams and accounting system, with evidence or enquiry references. — Source: `AWP-01-05`
- [ ] **AS-AUD-007-AC06:** Identify significant balances and transaction classes and map them to accounts, assertions and relevant program areas; record the auditor's inclusion/exclusion rationale. — Source: `AWP-01-06`
- [ ] **AS-AUD-007-AC07:** Capture significant audit risks, fraud risks and management-judgement areas, with actual response/procedure links. A risk cannot become Responded merely because a procedure was created. — Source: `AWP-01-07`
- [ ] **AS-AUD-007-AC08:** Use the existing materiality assessment for overall/performance amounts, benchmark/version/rationale and required approval. Revised thresholds retain history and trigger impacted testing/completion review. — Source: `AWP-01-08`
- [ ] **AS-AUD-007-AC09:** Prepare and approve the strategy and tailored program; show uncovered significant risks and missing relevant areas before planning approval. — Source: `AWP-01-09`
- [ ] **AS-AUD-007-AC10:** Absence of required source evidence, unapproved materiality or unaddressed planning blockers prevents a false planning-complete status; documented evidence limitations require explicit professional disposition.

**Focused verification**

- Exercise the nine planning procedures with a prior/current TB fixture and one explained opening difference.
- Add a significant risk with no response and verify the planning coverage warning/blocker.
- Revise materiality after tests were submitted and verify historical preservation plus current impact review.

**Deliverable:** A reviewed planning pack that demonstrates all nine source procedures and reuses the existing planning engine.

---

<a id="as-aud-008"></a>
### AS-AUD-008 — Implement Cash & Bank audit workpapers and reconciliation testing

**User story:** As a cash-and-bank audit preparer, I want to reconcile every year-end bank account and document confirmation, outstanding-item and subsequent-statement work, so that the Cash & Bank area has structured evidence and a reviewable conclusion.

**Priority:** High  
**Proposed milestone:** GAP-M2  
**Depends on:** [AS-AUD-005](#as-aud-005), [AS-AUD-006](#as-aud-006)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-02-01`, `AWP-02-02`, `AWP-02-03`, `AWP-02-04`, `AWP-02-05`, `AWP-02-06`, `AWP-02-07`, `AWP-02-08`

**Gap to establish:** An example bank workpaper title does not provide a complete bank-reconciliation and confirmation workflow.

**Reuse boundary:** Use shared schedules/lead schedules, item testing, confirmation register and existing workpaper/finding/review records.

**Minimum information:** Bank/account identity; year-end; ledger/TB/statement balances; reconciliation version; signed reconciling items; item dates/status; confirmation; transaction selections; subsequent statement range.

**Scope and implementation notes**

- Support bank-reconciliation review, not a live bank feed or a cash-management product. Account numbers are displayed only to authorized roles.

**Acceptance criteria**

- [ ] **AS-AUD-008-AC01:** Collect a year-end reconciliation for each identified bank account; compare the account inventory to GL/TB records and show missing reconciliations. — Source: `AWP-02-01`
- [ ] **AS-AUD-008-AC02:** Agree ledger balances to the selected Trial Balance version and retain unresolved differences without forced balancing. — Source: `AWP-02-02`
- [ ] **AS-AUD-008-AC03:** Agree the reconciliation to the exact year-end statement and compute its residual using an explicit sign convention. — Source: `AWP-02-03`
- [ ] **AS-AUD-008-AC04:** Link independently controlled direct bank confirmations and reconcile confirmed balances; outstanding/invalid responses remain visible for professional disposition. — Source: `AWP-02-04`
- [ ] **AS-AUD-008-AC05:** List outstanding cheques, deposits and other reconciling items with dates, signed amounts, support and subsequent-clearance evidence where obtained. — Source: `AWP-02-05`
- [ ] **AS-AUD-008-AC06:** Identify and investigate old or unusual items using recorded auditor-selected criteria; preserve explanations and unresolved exceptions. — Source: `AWP-02-06`
- [ ] **AS-AUD-008-AC07:** Select bank transactions through the common test workflow and record inspected supporting documents, result and exceptions. — Source: `AWP-02-07`
- [ ] **AS-AUD-008-AC08:** Record the subsequent bank-statement period reviewed, unusual transactions identified and follow-up, including a supported no-exception conclusion where appropriate. — Source: `AWP-02-08`
- [ ] **AS-AUD-008-AC09:** The area cannot be accepted merely because the arithmetic residual is zero; required testing, confirmation disposition and review must also be complete.

**Focused verification**

- Using a declared convention, statement 100,000 + deposit 5,000 - outstanding cheque 2,000 reconciles to ledger 103,000; changing the deposit to 4,000 produces a 1,000 residual.
- Verify a missing account reconciliation, aged uncleared item and missing confirmation remain actionable.
- Replace the statement and confirm the previous reviewed calculation stays frozen.

**Deliverable:** Cash & Bank area forms, reconciliation evidence, item-test links and reviewed area conclusion.

---

<a id="as-aud-009"></a>
### AS-AUD-009 — Implement Trade Receivables ageing, confirmations and collection/cut-off testing

**User story:** As a receivables audit preparer, I want to reconcile the receivable ageing and test confirmations, subsequent collections and cut-off, so that recorded receivables have a documented audit trail and feed a separate provision assessment.

**Priority:** High  
**Proposed milestone:** GAP-M2  
**Depends on:** [AS-AUD-005](#as-aud-005), [AS-AUD-006](#as-aud-006)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-03-01`, `AWP-03-02`, `AWP-03-03`, `AWP-03-04`, `AWP-03-05`, `AWP-03-06`, `AWP-03-07`, `AWP-03-09`

**Gap to establish:** Generic source/workpaper support does not establish an ageing, subsequent-collection or receivable-testing workflow.

**Reuse boundary:** Reuse schedules, confirmations, item testing and matching. ECL numerical assessment is owned by AS-AUD-010.

**Minimum information:** Customer/invoice IDs; invoice and due dates; year-end balance/currency; credits/disputes; ageing basis/buckets; GL/TB bridge; significant/overdue rationale; confirmations; receipts; delivery and posting dates.

**Scope and implementation notes**

- Treat credit balances and unapplied receipts explicitly. Do not merge customers by similar names or present an unreviewed match as collection evidence.

**Acceptance criteria**

- [ ] **AS-AUD-009-AC01:** Obtain the year-end receivable ageing, record its as-of date and approved ageing basis, and preserve source rows and configurable bucket boundaries. — Source: `AWP-03-01`
- [ ] **AS-AUD-009-AC02:** Reconcile ageing and receivable totals to the selected GL/TB, with credit balances, exclusions and unresolved differences displayed separately. — Source: `AWP-03-02`
- [ ] **AS-AUD-009-AC03:** Identify significant and overdue customer balances using documented criteria; record disputes, explanations, evidence and follow-up. — Source: `AWP-03-03`
- [ ] **AS-AUD-009-AC04:** Send/record authorized customer confirmation requests and investigate response differences through the common confirmation workflow. — Source: `AWP-03-04`
- [ ] **AS-AUD-009-AC05:** For non-confirmed balances, link alternative procedures and their conclusions; non-response is never confirmation of the balance. — Source: `AWP-03-05`
- [ ] **AS-AUD-009-AC06:** Test selected sales invoices against delivery documents and the ledger, retaining the selected records and all evidence versions. — Source: `AWP-03-06`
- [ ] **AS-AUD-009-AC07:** Match subsequent customer collections to bank statements with dates, currency and partial allocation amounts; display remaining balances and reject double allocation. — Source: `AWP-03-07`
- [ ] **AS-AUD-009-AC08:** Perform year-end sales/receivable cut-off tests using the approved window and relevant delivery/service evidence; record the auditor's expected accounting period and differences. — Source: `AWP-03-09`
- [ ] **AS-AUD-009-AC09:** The area links the ECL assessment from AS-AUD-010. Missing provision assessment cannot be concealed by marking the ageing workpaper complete.

**Focused verification**

- Test exact bucket boundaries, missing due dates, disputed balances and credit notes without silent reclassification.
- Match a 600 receipt against a 1,000 invoice and retain 400 uncollected; reject a second allocation of the same 600.
- Record a confirmation difference, approved alternative work and a year-end cut-off exception.

**Deliverable:** A receivables testing pack linked to a distinct ECL/provision assessment.

---

<a id="as-aud-010"></a>
### AS-AUD-010 — Support documented ECL recalculation and provision adequacy review

**User story:** As a receivables audit reviewer, I want to reproduce the ECL calculation from approved inputs and record my assessment of provision adequacy, so that provision differences are explained, versioned and routed for adjustment without automated accounting judgement.

**Priority:** High  
**Proposed milestone:** GAP-M2  
**Depends on:** [AS-AUD-009](#as-aud-009)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-03-08`

**Gap to establish:** The source requires ECL work; it does not prescribe a model, default loss rates, scenarios or accounting-framework rules.

**Reuse boundary:** Use the approved receivable population, calculation-backed workpapers, immutable submissions and existing findings/adjustment links. Introduce only a bounded approved method or reviewed external calculation route.

**Minimum information:** Method/version/approval; eligible exposure IDs and basis; buckets/segments; rates and source dates; overlays and rationale; specific-assessment inclusion; calculated provision; recorded provision; difference; reviewer conclusion.

**Scope and implementation notes**

- Enable a simple provision-matrix helper only when the firm approves that method for the engagement. Otherwise preserve and review an externally prepared calculation and mark native recalculation unsupported.
- Separate mechanical recomputation, model/data suitability, reasonableness of assumptions and final adequacy judgement.
- Do not implement a universal IFRS credit-risk platform, fabricate expected loss rates or make an automated adequate/inadequate decision.

**Acceptance criteria**

- [ ] **AS-AUD-010-AC01:** Perform/recalculate ECL using an explicit approved methodology and retained inputs; compare the result with the booked provision and record the auditor's adequacy conclusion and evidence. — Source: `AWP-03-08`
- [ ] **AS-AUD-010-AC02:** For an enabled matrix, each eligible exposure is included once in an approved segment, excluded with reason or separately assessed; specific assessments and pooled calculations cannot double-count the same exposure.
- [ ] **AS-AUD-010-AC03:** Rates, historical/forward-looking inputs and overlays have sources and reviewer rationale. Missing method approval, rates or required inputs prevent a calculated result from being accepted.
- [ ] **AS-AUD-010-AC04:** Use decimal arithmetic and approved rounding. Preserve input precision, method version, intermediate outputs and the final result in the submitted workpaper.
- [ ] **AS-AUD-010-AC05:** Changes to exposures, rates, assumptions, overlays or method create a new calculation revision and invalidate affected current reviews.
- [ ] **AS-AUD-010-AC06:** A difference from the booked provision may create/link a proposed finding/adjustment; the calculation cannot post a journal or select the audit opinion.
- [ ] **AS-AUD-010-AC07:** Where a reviewed external calculation is used, the UI explicitly identifies it, links its exact snapshot and records independent checking rather than claiming a native engine produced it.

**Focused verification**

- Synthetic matrix: 80,000 at 1% plus 20,000 at 10% equals 2,800; a booked provision of 2,000 gives an 800 proposed difference before any separately approved overlay.
- Reject duplicate exposure inclusion, missing rates and unapproved calculation methods.
- Change a rate after review and verify the old review no longer authorizes the new result.

**Deliverable:** A bounded, methodology-approved ECL workpaper with recalculation evidence and human adequacy review.

---

<a id="as-aud-011"></a>
### AS-AUD-011 — Implement Inventory count, costing, condition and NRV workpapers

**User story:** As an inventory audit preparer, I want to reconcile inventory records and document physical-count, cost, condition and NRV testing, so that the inventory conclusion is supported by traceable quantities, valuation evidence and resolved count differences.

**Priority:** High  
**Proposed milestone:** GAP-M2  
**Depends on:** [AS-AUD-005](#as-aud-005)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-04-01`, `AWP-04-02`, `AWP-04-03`, `AWP-04-04`, `AWP-04-05`, `AWP-04-06`, `AWP-04-07`, `AWP-04-08`

**Gap to establish:** Source lists and generic workpapers do not provide count sheets, auditor test counts, valuation comparisons or quantity bridges.

**Reuse boundary:** Reuse schedule reconciliation, item selection, evidence and findings. Use a small inventory-result detail rather than a stock-management module.

**Minimum information:** Location/item/unit IDs; listing/count dates; source quantities; count-sheet/final quantities; auditor test counts; movements to year-end; unit cost/support; condition; NRV inputs; cut-off references.

**Scope and implementation notes**

- The application records attendance/observations and auditor decisions; it does not physically verify goods or maintain the client's perpetual inventory.

**Acceptance criteria**

- [ ] **AS-AUD-011-AC01:** Obtain the year-end inventory listing and reconcile its value to the selected GL, with scope, locations, item IDs and units retained. — Source: `AWP-04-01`
- [ ] **AS-AUD-011-AC02:** For applicable count attendance, record date/location, auditor, management count instructions and observations; approved non-applicability needs a reason. — Source: `AWP-04-02`
- [ ] **AS-AUD-011-AC03:** Record auditor test counts and compare to management counts; retain both original values, differences, recounts and reviewer disposition. — Source: `AWP-04-03`
- [ ] **AS-AUD-011-AC04:** Reconcile quantities to count sheets and the final inventory listing; where count date differs from year-end, record supported intervening movements rather than assuming equal dates. — Source: `AWP-04-04`
- [ ] **AS-AUD-011-AC05:** Test sampled costs to purchase invoices or supporting records with quantity/unit/currency consistency and documented costing basis. — Source: `AWP-04-05`
- [ ] **AS-AUD-011-AC06:** Identify slow-moving, damaged and obsolete items from recorded criteria and observations; retain supporting evidence and proposed treatment. — Source: `AWP-04-06`
- [ ] **AS-AUD-011-AC07:** Where applicable, compare cost with NRV using supported selling-price and completion/selling-cost inputs; retain the approved basis and auditor valuation conclusion. — Source: `AWP-04-07`
- [ ] **AS-AUD-011-AC08:** Test purchases and goods received around year-end against receipt/invoice/posting evidence using the common cut-off workflow. — Source: `AWP-04-08`
- [ ] **AS-AUD-011-AC09:** Zero/negative quantities, inconsistent units and duplicate item/location rows require explicit review; no silent netting or unit conversion.

**Focused verification**

- Record book quantity 100 versus auditor count 98 and preserve a difference of -2 through recount and resolution.
- For a synthetic approved basis, unit cost 100 and NRV 90 on 10 units gives a 100 potential write-down; do not post it automatically.
- Show pending count attendance and missing receipt evidence as incomplete, not passed.

**Deliverable:** An inventory audit pack with count/valuation/cut-off evidence and a reviewed area conclusion.

---

<a id="as-aud-012"></a>
### AS-AUD-012 — Implement Revenue / Sales audit testing and cut-off review

**User story:** As a revenue audit preparer, I want to reconcile sales, select value/risk-based tests and evaluate document chains and year-end cut-off, so that revenue testing is repeatable and exceptions feed the audit findings process.

**Priority:** High  
**Proposed milestone:** GAP-M2  
**Depends on:** [AS-AUD-005](#as-aud-005)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-05-01`, `AWP-05-02`, `AWP-05-03`, `AWP-05-04`, `AWP-05-05`, `AWP-05-06`, `AWP-05-07`, `AWP-05-08`

**Gap to establish:** Revenue risks/findings exist as generic records, but the sales listing, document chain and post-year-end credit-note review need explicit workflows.

**Reuse boundary:** Reuse schedules, selection/test results, subsequent matching and findings; consume analytical outputs from AS-AUD-022 when available without duplicating that calculator.

**Minimum information:** Sales/invoice/customer IDs; quantities, prices and supported adjustments; order/delivery/service references; invoice/posting dates; GL/TB mapping; receipts; subsequent credit-note relationships; year-end window.

**Scope and implementation notes**

- Keep source gross/net/tax conventions explicit. A three-document match is evidence for the recorded procedure, not automatic proof of revenue recognition.

**Acceptance criteria**

- [ ] **AS-AUD-012-AC01:** Obtain the sales listing and reconcile total revenue to GL/TB using documented inclusion, returns, credits and currency rules. — Source: `AWP-05-01`
- [ ] **AS-AUD-012-AC02:** Perform and document monthly and annual sales analytical review, retaining comparable periods, expectations and explanations; link the shared analytical-review workpaper. — Source: `AWP-05-02`
- [ ] **AS-AUD-012-AC03:** Select sales samples based on recorded value and risk criteria with stable source IDs and coverage information. — Source: `AWP-05-03`
- [ ] **AS-AUD-012-AC04:** Check selected invoices against customer orders and delivery documents, with supported service-delivery evidence where relevant. — Source: `AWP-05-04`
- [ ] **AS-AUD-012-AC05:** Verify quantity, price, calculation and accounting entry under the documented invoice basis, preserving differences and the exact ledger reference. — Source: `AWP-05-05`
- [ ] **AS-AUD-012-AC06:** Check selected subsequent receipts where relevant with controlled bank/invoice matches and an approved applicability decision when not relevant. — Source: `AWP-05-06`
- [ ] **AS-AUD-012-AC07:** Review significant post-year-end credit notes, link the original invoice and record reason, financial effect and whether further cut-off/correction work is required. — Source: `AWP-05-07`
- [ ] **AS-AUD-012-AC08:** Perform cut-off testing on both sides of year-end with posting and delivery/service dates, expected period and auditor conclusion. — Source: `AWP-05-08`

**Focused verification**

- Reconcile sales including returns/credits and verify a missing source row creates a variance.
- Use a sample with an invoice before year-end, delivery after year-end and a subsequent credit note; retain all related exceptions without double counting.
- Test quantity/price recalculation and a partial subsequent receipt.

**Deliverable:** A sales audit pack with reconciled population, tests, analytics links and cut-off/credit-note dispositions.

---

<a id="as-aud-013"></a>
### AS-AUD-013 — Implement Purchases & Trade Payables and search for unrecorded liabilities

**User story:** As a payables audit preparer, I want to reconcile supplier balances and test purchases, subsequent payments and potential omitted liabilities, so that the audit covers completeness concerns rather than only rechecking already-recorded payables.

**Priority:** High  
**Proposed milestone:** GAP-M2  
**Depends on:** [AS-AUD-005](#as-aud-005), [AS-AUD-006](#as-aud-006)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-06-01`, `AWP-06-02`, `AWP-06-03`, `AWP-06-04`, `AWP-06-05`, `AWP-06-06`, `AWP-06-07`, `AWP-06-08`

**Gap to establish:** Supplier ageing, statement reconciliation and an independent unrecorded-liability search need structured source and test records.

**Reuse boundary:** Use shared schedules, confirmations, selections, subsequent matching and findings; do not create a purchasing/ERP module.

**Minimum information:** Supplier/invoice IDs; ageing; book and supplier-statement balances; unmatched items; PO/GRN/invoice dates; subsequent payments; candidate omitted liability; expected recognition period; amount and disposition.

**Scope and implementation notes**

- The search population must be recorded and justified. Recorded payable rows alone cannot be presented as a complete search for liabilities not recorded.

**Acceptance criteria**

- [ ] **AS-AUD-013-AC01:** Obtain supplier ageing and reconcile it to GL/TB, retaining debit balances, disputed items and reconciliation differences. — Source: `AWP-06-01`
- [ ] **AS-AUD-013-AC02:** Review significant, old and unusual supplier balances using documented criteria and preserve evidence-backed explanations. — Source: `AWP-06-02`
- [ ] **AS-AUD-013-AC03:** Obtain supplier confirmations and investigate differences through the shared controlled confirmation workflow. — Source: `AWP-06-03`
- [ ] **AS-AUD-013-AC04:** Compare supplier statements with the company payable ledger, recording unmatched invoices, credits, payments and reconciling items. — Source: `AWP-06-04`
- [ ] **AS-AUD-013-AC05:** Test selected purchases against invoices, goods-received notes and purchase orders; preserve missing documentation and exceptions. — Source: `AWP-06-05`
- [ ] **AS-AUD-013-AC06:** Check subsequent payments to identify obligations outstanding at year-end, using payment evidence and the underlying receipt/service period. — Source: `AWP-06-06`
- [ ] **AS-AUD-013-AC07:** Perform a documented search for unrecorded liabilities using appropriate sources such as subsequent disbursements, supplier statements and unmatched receipt/invoice records; retain selected candidates and conclusions. — Source: `AWP-06-07`
- [ ] **AS-AUD-013-AC08:** Test purchase/payable cut-off before and after year-end; distinguish invoice/payment date from the supported obligation/receipt period. — Source: `AWP-06-08`
- [ ] **AS-AUD-013-AC09:** An identified omission creates a linked finding and proposed correction for review, never an automatic journal or silent addition to the source ledger.

**Focused verification**

- Identify a post-year-end payment for goods received before year-end that is absent from the year-end payable ledger.
- Reconcile a supplier statement containing an unmatched invoice and a payment in transit.
- Reject a completion claim based solely on a sample of already-recorded AP rows with no completeness-search disposition.

**Deliverable:** A purchases/payables pack including a separately identifiable unrecorded-liability search.

---

<a id="as-aud-014"></a>
### AS-AUD-014 — Implement Fixed Assets roll-forward, verification and depreciation testing

**User story:** As a fixed-assets audit preparer, I want to reconcile the asset register and test movements, depreciation assumptions and impairment indicators, so that PPE balances and related charges have a supported audit conclusion.

**Priority:** High  
**Proposed milestone:** GAP-M3  
**Depends on:** [AS-AUD-005](#as-aud-005)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-07-01`, `AWP-07-02`, `AWP-07-03`, `AWP-07-04`, `AWP-07-05`, `AWP-07-06`, `AWP-07-07`, `AWP-07-08`, `AWP-07-09`

**Gap to establish:** PPE account mappings are not a fixed-asset audit register, roll-forward or depreciation recalculation workflow.

**Reuse boundary:** Use source schedules, prior-period bridges, selected-item workpapers and evidence. Reuse approved financial mappings without creating a fixed-asset management subsystem.

**Minimum information:** Asset ID/class/location; cost and accumulated depreciation; opening/addition/disposal/other movements; purchase/payment/approval evidence; in-service dates; useful life/method/residual; selected recalculation; impairment indicators.

**Scope and implementation notes**

- Numerical depreciation support is limited to firm-approved methods with explicit conventions. Unsupported methods use a reviewed external workpaper; neither tax depreciation nor valuation rules are invented.

**Acceptance criteria**

- [ ] **AS-AUD-014-AC01:** Obtain the fixed asset register and reconcile cost, accumulated depreciation and relevant carrying values to GL, using documented classifications. — Source: `AWP-07-01`
- [ ] **AS-AUD-014-AC02:** Agree opening balances to prior-year audited figures and retain explained restatements/reclassifications separately. — Source: `AWP-07-02`
- [ ] **AS-AUD-014-AC03:** Test additions to invoices, payment records and approvals; preserve asset identity and missing evidence. — Source: `AWP-07-03`
- [ ] **AS-AUD-014-AC04:** Record the auditor's capitalization assessment and rationale for selected expenditure, linked to the approved accounting policy rather than a universal software threshold. — Source: `AWP-07-04`
- [ ] **AS-AUD-014-AC05:** Record physical verification of significant assets/additions where appropriate, including date, location, identifier, observer and result; approved N/A requires a reason. — Source: `AWP-07-05`
- [ ] **AS-AUD-014-AC06:** Test disposals against documents and sale proceeds; reconcile removal of cost/accumulated depreciation and resulting gain/loss under the recorded basis. — Source: `AWP-07-06`
- [ ] **AS-AUD-014-AC07:** Recalculate depreciation for selected assets using documented inputs, method, in-service dates, proration/rounding rules and engine or external-workpaper version. — Source: `AWP-07-07`
- [ ] **AS-AUD-014-AC08:** Review useful lives and depreciation methods independently from the arithmetic check; preserve rationale, changes and reviewer conclusion. — Source: `AWP-07-08`
- [ ] **AS-AUD-014-AC09:** Review impairment, damage and obsolescence indicators, supporting evidence and management treatment; record findings or limitations without automatic valuation conclusions. — Source: `AWP-07-09`

**Focused verification**

- Reconcile an asset roll-forward including an addition and disposal.
- Synthetic full-year straight-line fixture: cost 12,000, residual 0, useful life 3 years gives depreciation 4,000; partial-year behavior uses separately approved day/month conventions.
- Exercise an unsupported method, missing asset evidence and an impairment indicator requiring review.

**Deliverable:** A fixed-assets audit pack with independent policy/judgement review and reproducible selected calculations.

---

<a id="as-aud-015"></a>
### AS-AUD-015 — Implement Expenses analytics, vouching, classification and cut-off testing

**User story:** As an expense audit preparer, I want to reconcile expense listings and examine unusual movements and selected charges, so that expense validity, approval, classification and period allocation are explicitly reviewed.

**Priority:** High  
**Proposed milestone:** GAP-M3  
**Depends on:** [AS-AUD-005](#as-aud-005)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-08-01`, `AWP-08-02`, `AWP-08-03`, `AWP-08-04`, `AWP-08-05`, `AWP-08-06`, `AWP-08-07`, `AWP-08-08`, `AWP-08-09`

**Gap to establish:** Generic tests can hold notes, but expense-specific reconciliation, classification and cutoff fields are not proven end-to-end.

**Reuse boundary:** Reuse shared source/testing forms and analytical-review outputs; link capital-expenditure exceptions to AS-AUD-014 without adding another asset register.

**Minimum information:** Expense source row/account/category; current/prior/budget period and basis; selection reason; invoice; management approval; payment; service period; accounting classification; capital/expense assessment.

**Scope and implementation notes**

- Budget comparisons are conditional on availability; absence is recorded and is not replaced with a fabricated zero budget.

**Acceptance criteria**

- [ ] **AS-AUD-015-AC01:** Obtain the detailed expense listing and reconcile it to GL/TB with recorded exclusions, credits and classification mappings. — Source: `AWP-08-01`
- [ ] **AS-AUD-015-AC02:** Compare expenses with prior year and available budget using comparable periods and documented bases; identify unavailable budget data explicitly. — Source: `AWP-08-02`
- [ ] **AS-AUD-015-AC03:** Identify significant or unusual movements, retain investigation criteria, explanations, supporting evidence and unresolved questions. — Source: `AWP-08-03`
- [ ] **AS-AUD-015-AC04:** Select items based on value and risk through the common selection workflow with preserved rationale and coverage. — Source: `AWP-08-04`
- [ ] **AS-AUD-015-AC05:** Check invoices and supporting documentation and record each selected item's result, exception and follow-up. — Source: `AWP-08-05`
- [ ] **AS-AUD-015-AC06:** Check management approval and payment evidence as distinct checks; a bank payment alone does not prove approval or the proper classification. — Source: `AWP-08-06`
- [ ] **AS-AUD-015-AC07:** Verify accounting classification against the recorded nature of the expense and approved policy; record proposed reclassifications. — Source: `AWP-08-07`
- [ ] **AS-AUD-015-AC08:** Check whether capital expenditure was incorrectly expensed and link exceptions to the relevant asset workpaper and finding without counting the same difference twice. — Source: `AWP-08-08`
- [ ] **AS-AUD-015-AC09:** Perform expense cut-off testing using service/receipt periods and the approved year-end window, with proposed accrual/prepayment corrections where supported. — Source: `AWP-08-09`

**Focused verification**

- Test an unusual current/prior movement when no budget exists; show unavailable rather than zero.
- Test a capital item charged to expense and link one shared finding from both audit areas.
- Record missing approval evidence and an expense belonging partly to a later period.

**Deliverable:** An expense audit pack with analytical, documentary, classification and cut-off conclusions.

---

<a id="as-aud-016"></a>
### AS-AUD-016 — Implement Payroll employee, recalculation and joiner/leaver testing

**User story:** As a payroll audit preparer, I want to reconcile payroll and examine selected employees, salary calculations and employment changes, so that payroll audit work is reproducible while sensitive employee data stays restricted.

**Priority:** High  
**Proposed milestone:** GAP-M3  
**Depends on:** [AS-AUD-005](#as-aud-005)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-09-01`, `AWP-09-02`, `AWP-09-03`, `AWP-09-04`, `AWP-09-05`, `AWP-09-06`, `AWP-09-07`, `AWP-09-08`

**Gap to establish:** A payroll risk entry or analytical-response text does not establish employee-level audit testing.

**Reuse boundary:** Reuse schedule reconciliation, selection, evidence and workpaper approval. This is an audit workpaper, not a payroll processing or HR system.

**Minimum information:** Employee stable ID; payroll period; contract/effective salary; components; allowances/deductions; authorized changes; start/end dates; final payment; bank-match evidence; headcount and expense movements.

**Scope and implementation notes**

- Store only necessary employee details and apply existing field/document restrictions. Payroll rules come from approved contracts and the applicable firm-approved basis; do not hardcode jurisdictional employment formulas.

**Acceptance criteria**

- [ ] **AS-AUD-016-AC01:** Obtain annual/monthly payroll summaries and reconcile payroll components to the selected GL with explained differences. — Source: `AWP-09-01`
- [ ] **AS-AUD-016-AC02:** Select employees for detailed testing with recorded population, criteria and stable employee IDs. — Source: `AWP-09-02`
- [ ] **AS-AUD-016-AC03:** Check employment contracts and salary details including effective dates and authorized changes, using exact evidence versions. — Source: `AWP-09-03`
- [ ] **AS-AUD-016-AC04:** Recalculate selected gross salary, allowances and deductions from approved inputs; retain component-level comparisons and any net-pay reconciliation, with no inferred statutory rates. — Source: `AWP-09-04`
- [ ] **AS-AUD-016-AC05:** Agree selected salary payments to bank statements, preserving payroll-period and employee/payment matches and unexplained differences. — Source: `AWP-09-05`
- [ ] **AS-AUD-016-AC06:** Test new employees against employment documents, authorized start dates and their first payroll periods. — Source: `AWP-09-06`
- [ ] **AS-AUD-016-AC07:** Test terminated employees and final payments against termination evidence and the applicable approved calculation basis; post-termination payments require investigation, not automatic fraud classification. — Source: `AWP-09-07`
- [ ] **AS-AUD-016-AC08:** Review unusual changes in payroll totals or employee numbers using comparable periods, explanations and evidence. — Source: `AWP-09-08`
- [ ] **AS-AUD-016-AC09:** Unauthorized staff and client contacts cannot access unassigned employee-level details, tests or exports; denial must apply to searches and counts as well as detail pages.

**Focused verification**

- Synthetic fixture: approved gross salary 5,000 + allowances 500 - deductions 300 gives net payment 5,200; compare each component independently.
- Test a joiner, a leaver and an unexpected later payment with evidence-based disposition.
- Verify restricted payroll data is absent from an ordinary client portal view and unauthorized export.

**Deliverable:** A restricted payroll audit pack, not an operational payroll engine.

---

<a id="as-aud-017"></a>
### AS-AUD-017 — Implement Loans & Borrowings schedules, interest and covenant testing

**User story:** As a borrowings audit preparer, I want to reconcile loan movements and assess confirmations, terms, interest and balance classification, so that borrowing balances and relevant obligations are supported by reviewable contractual evidence.

**Priority:** High  
**Proposed milestone:** GAP-M3  
**Depends on:** [AS-AUD-005](#as-aud-005), [AS-AUD-006](#as-aud-006)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-10-01`, `AWP-10-02`, `AWP-10-03`, `AWP-10-04`, `AWP-10-05`, `AWP-10-06`, `AWP-10-07`, `AWP-10-08`, `AWP-10-09`

**Gap to establish:** A loan TB account does not provide a loan schedule, repayment evidence, interest workpaper or covenant assessment.

**Reuse boundary:** Reuse schedules, confirmations, subsequent-payment matching, calculations and findings; link relevant conclusions to going-concern and disclosures.

**Minimum information:** Facility/lender ID; principal/currency; opening/drawdown/repayment/closing values; agreements; interest rate changes and accrual dates; day-count basis; maturities; security/covenant terms; waivers and evidence.

**Scope and implementation notes**

- Enable only approved bounded interest computations. Contract interpretation, covenant applicability and classification remain auditor decisions; do not assume one universal day-count or waiver rule.

**Acceptance criteria**

- [ ] **AS-AUD-017-AC01:** Obtain the borrowing schedule and agree balances and relevant movements to GL with explained differences. — Source: `AWP-10-01`
- [ ] **AS-AUD-017-AC02:** Agree opening amounts to the prior-year financial statements and retain any reconciliation changes. — Source: `AWP-10-02`
- [ ] **AS-AUD-017-AC03:** Obtain bank/financier confirmation through the shared register and link unresolved confirmation matters. — Source: `AWP-10-03`
- [ ] **AS-AUD-017-AC04:** Check new borrowings against agreements and bank receipts, retaining facility identity and contractual dates. — Source: `AWP-10-04`
- [ ] **AS-AUD-017-AC05:** Check repayments against bank statements, separating principal, interest and other amounts under the recorded basis. — Source: `AWP-10-05`
- [ ] **AS-AUD-017-AC06:** Recalculate interest expense and accrued interest using the actual documented principal periods, rates, compounding/day-count basis and rounding; unsupported methods require a reviewed external calculation. — Source: `AWP-10-06`
- [ ] **AS-AUD-017-AC07:** Review current/non-current classification against contractual terms and the applicable approved reporting basis, with rationale and relevant subsequent/waiver evidence. — Source: `AWP-10-07`
- [ ] **AS-AUD-017-AC08:** Review terms, security and applicable covenants; retain formula/threshold/source definitions, test dates, measured inputs, breaches, waivers and auditor disposition. — Source: `AWP-10-08`
- [ ] **AS-AUD-017-AC09:** Check subsequent repayments and connect relevant funding or repayment concerns to the going-concern workpaper. — Source: `AWP-10-09`

**Focused verification**

- Synthetic simple-interest fixture: principal 100,000, annual rate 12%, 30 days on an explicitly approved Actual/360 basis gives 1,000 interest.
- Test a rate change, partial principal repayment and an unsupported compounding convention.
- Record a covenant issue and verify the application asks for review rather than automatically changing classification or the opinion.

**Deliverable:** A loan audit pack with movement reconciliation, contractual evidence and reviewed interest/covenant results.

---

<a id="as-aud-018"></a>
### AS-AUD-018 — Implement Equity / Share Capital movement and retained-earnings review

**User story:** As an equity audit preparer, I want to reconcile equity movements and verify shareholding changes and dividends, so that closing equity and retained earnings tie to supported company records and financial statements.

**Priority:** High  
**Proposed milestone:** GAP-M3  
**Depends on:** [AS-AUD-005](#as-aud-005)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-11-01`, `AWP-11-02`, `AWP-11-03`, `AWP-11-04`, `AWP-11-05`, `AWP-11-06`, `AWP-11-07`

**Gap to establish:** Equity mappings exist as accounting infrastructure but do not establish share capital, ownership or dividend audit procedures.

**Reuse boundary:** Reuse client corporate evidence, schedules, selected-item tests and FS links. Do not create a corporate registry or share-management application.

**Minimum information:** Equity component; opening balance; issuance/transfer/other movement; shareholder record reference; dividend authorization/payment; current profit/loss; prior-period adjustment; closing FS line.

**Scope and implementation notes**

- Record legal/company record references as evidence. No external company-registry integration or legal share-validity decision is added.

**Acceptance criteria**

- [ ] **AS-AUD-018-AC01:** Obtain the share-capital and equity movement schedule with separate components and relevant source records. — Source: `AWP-11-01`
- [ ] **AS-AUD-018-AC02:** Agree opening balances with prior-year audited financial statements and explain changes separately. — Source: `AWP-11-02`
- [ ] **AS-AUD-018-AC03:** Agree share capital to company/CR records, retaining document identity, effective date and reconciliation differences. — Source: `AWP-11-03`
- [ ] **AS-AUD-018-AC04:** Check additions, transfers and shareholding changes against supporting documents and record their accounting effect or no-effect rationale. — Source: `AWP-11-04`
- [ ] **AS-AUD-018-AC05:** Check dividend declarations, authority and payment evidence and record declaration/payment dates and relevant amounts. — Source: `AWP-11-05`
- [ ] **AS-AUD-018-AC06:** Reconcile retained earnings using current profit/loss and supported dividends/other movements; do not silently plug an unexplained difference. — Source: `AWP-11-06`
- [ ] **AS-AUD-018-AC07:** Agree closing equity components and total to the selected financial statement package and preserve unresolved differences. — Source: `AWP-11-07`

**Focused verification**

- Synthetic retained earnings: opening 50,000 + profit 20,000 - dividends 5,000 equals closing 65,000, with no other movements assumed.
- Record a transfer of ownership with no capital change and a separate capital issue.
- Change the FS package and verify the equity agreement is re-evaluated against its new version.

**Deliverable:** An equity audit workpaper with source-linked movements and retained-earnings/FS agreement.

---

<a id="as-aud-019"></a>
### AS-AUD-019 — Implement Related Parties register, transaction testing and disclosure linkage

**User story:** As an audit senior, I want to validate management's related-party listing and review relevant transactions, balances and disclosures, so that related-party audit work is identifiable and not confined to a generic risk narrative.

**Priority:** High  
**Proposed milestone:** GAP-M3  
**Depends on:** [AS-AUD-005](#as-aud-005), [AS-AUD-006](#as-aud-006)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-12-01`, `AWP-12-02`, `AWP-12-03`, `AWP-12-04`, `AWP-12-05`, `AWP-12-06`, `AWP-12-07`

**Gap to establish:** A qualitative related-party risk entry is not a complete register, transaction review or disclosure workflow.

**Reuse boundary:** Reuse company/person references where authorized, evidence, source rows, confirmation cases, findings and FS note links; avoid a duplicate contact master.

**Minimum information:** Party identity/type; relationship; management-listed versus auditor-identified source; relevant dates; director/shareholder/key-management evidence; transaction/balance references; confirmation applicability; disclosure references.

**Scope and implementation notes**

- The register stores source-backed identifications and auditor review. It does not infer relationships from names, private data or an unapproved screening provider.

**Acceptance criteria**

- [ ] **AS-AUD-019-AC01:** Obtain and preserve management's related-party listing, with its author/date/version and relevant entity/period. — Source: `AWP-12-01`
- [ ] **AS-AUD-019-AC02:** Check directors, shareholders and key-management records and document additions, discrepancies or unresolved identification matters. — Source: `AWP-12-02`
- [ ] **AS-AUD-019-AC03:** Review related-party transactions during the year using linked source records, the nature of the relationship and appropriate supporting evidence. — Source: `AWP-12-03`
- [ ] **AS-AUD-019-AC04:** Review significant balances with recorded significance criteria and documented explanations. — Source: `AWP-12-04`
- [ ] **AS-AUD-019-AC05:** Confirm significant balances where appropriate or retain an approved applicability decision and alternative evidence. — Source: `AWP-12-05`
- [ ] **AS-AUD-019-AC06:** Check that identified transactions are properly recorded and link any proposed correction to the existing findings/adjustment process. — Source: `AWP-12-06`
- [ ] **AS-AUD-019-AC07:** Verify required disclosures against the approved reporting framework and exact FS note version; unresolved disclosure matters remain open for review. — Source: `AWP-12-07`
- [ ] **AS-AUD-019-AC08:** Internal relationship assessments and restricted evidence are not automatically included in client-facing reports or portal responses.

**Focused verification**

- Start with management's list, add an auditor-identified discrepancy supported by shareholder records, and retain both provenance paths.
- Link a related-party transaction, closing balance, confirmation and disclosure to one party without duplicate counting.
- Revise a relevant disclosure and verify the previous review does not silently apply.

**Deliverable:** A source-backed related-party register linked to tests, confirmations and disclosures.

---

<a id="as-aud-020"></a>
### AS-AUD-020 — Implement Tax & Statutory Liabilities audit schedules and disclosure review

**User story:** As a tax audit preparer, I want to reconcile tax evidence and review payments, liabilities, provisions and disclosures, so that tax audit procedures are supported without confusing the client's liabilities with the firm's billing taxes.

**Priority:** High  
**Proposed milestone:** GAP-M3  
**Depends on:** [AS-AUD-005](#as-aud-005)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-13-01`, `AWP-13-02`, `AWP-13-03`, `AWP-13-04`, `AWP-13-05`, `AWP-13-06`, `AWP-13-07`

**Gap to establish:** Tax fields on the audit firm's invoices and generic disclosure responses do not satisfy client tax-audit requirements.

**Reuse boundary:** Reuse client source schedules, evidence, testing, findings and financial-note links. Do not change the practice billing-tax engine.

**Minimum information:** Tax/statutory obligation type; jurisdiction and period; return/computation snapshot; GL/TB balance; payment reference; liability/penalty; authority correspondence; provision/expense; disclosure; reviewer methodology.

**Scope and implementation notes**

- The source names no jurisdiction, rates, filing deadlines or statutory calculation rules. Record reviewed computations and configure only explicitly approved methods; do not add tax filing or legal-advice automation.

**Acceptance criteria**

- [ ] **AS-AUD-020-AC01:** Obtain the relevant tax returns and computations with period, jurisdiction and exact source versions, distinguishing missing documents from not-applicable obligations. — Source: `AWP-13-01`
- [ ] **AS-AUD-020-AC02:** Reconcile tax balances with the client GL/TB, recording current/other classifications under the approved reporting basis. — Source: `AWP-13-02`
- [ ] **AS-AUD-020-AC03:** Check tax payments against bank statements and preserve partial, unmatched or different-period amounts. — Source: `AWP-13-03`
- [ ] **AS-AUD-020-AC04:** Review outstanding liabilities and penalties with assessment/support references, management explanations and auditor disposition. — Source: `AWP-13-04`
- [ ] **AS-AUD-020-AC05:** Review tax-authority correspondence and record relevant issues, dates, amounts, responses and follow-up without exposing restricted material by default. — Source: `AWP-13-05`
- [ ] **AS-AUD-020-AC06:** Check tax provisions and current-year tax expense against the approved computation and evidence; record recalculation/reconciliation differences and human judgement. — Source: `AWP-13-06`
- [ ] **AS-AUD-020-AC07:** Check relevant tax disclosures in the exact financial statement package against the approved checklist and retain any unresolved matters. — Source: `AWP-13-07`

**Focused verification**

- Reconcile an approved tax computation to GL and a partial bank payment without supplying a fictional tax rate.
- Record a penalty and unresolved authority correspondence and verify they require review.
- Verify practice invoice tax is never imported as a client tax-audit population.

**Deliverable:** A jurisdiction-labelled tax audit pack using authorized evidence, not a universal tax calculation or filing service.

---

<a id="as-aud-021"></a>
### AS-AUD-021 — Implement client Journal Entries & Fraud risk-oriented testing

**User story:** As an audit senior, I want to examine the client's journal population using transparent risk criteria and preserve investigated exceptions, so that journal-entry and management-override work is distinct from audit adjustments and supported by actual source records.

**Priority:** High  
**Proposed milestone:** GAP-M3  
**Depends on:** [AS-AUD-005](#as-aud-005)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-14-01`, `AWP-14-02`, `AWP-14-03`, `AWP-14-04`, `AWP-14-05`, `AWP-14-06`, `AWP-14-07`, `AWP-14-08`

**Gap to establish:** The existing adjustment journals and firm ledger are different from auditing the client's GL journal population. Generic risk text does not provide the required journal analyses.

**Reuse boundary:** Reuse source ingestion, populations, manual selections, workpapers and restricted findings. Use bounded deterministic predicates rather than an AI fraud model or user-programmable rule engine.

**Minimum information:** Source journal/line ID; effective/posting dates; manual/system origin when supplied; account; amount/currency; creator/approver fields when supplied; reversal link; criteria/version; match explanations; selected tests and findings.

**Scope and implementation notes**

- The word “fraud” denotes an audit area, not an automatic accusation. A criterion match creates a review candidate, never a finding of fraud or an automatic financial adjustment.

**Acceptance criteria**

- [ ] **AS-AUD-021-AC01:** Obtain the client journal-entry listing for the audit period and document completeness/reliability checks and any missing source metadata. — Source: `AWP-14-01`
- [ ] **AS-AUD-021-AC02:** Identify unusual, manual and high-value entries using auditor-approved explicit criteria and source fields; show the exact reason each entry is selected or flagged. — Source: `AWP-14-02`
- [ ] **AS-AUD-021-AC03:** Focus on entries posted near year-end with a recorded date window and a clear distinction between posting and effective dates. — Source: `AWP-14-03`
- [ ] **AS-AUD-021-AC04:** Select risk-based journal samples with preserved criteria, stable IDs, manual selections and coverage; do not imply statistical assurance. — Source: `AWP-14-04`
- [ ] **AS-AUD-021-AC05:** Check support and authorization for each selected journal; absent approval metadata remains unknown until independently supported. — Source: `AWP-14-05`
- [ ] **AS-AUD-021-AC06:** Review unusual entries affecting revenue, expenses and reserves using the actual account mapping and recorded reasons. — Source: `AWP-14-06`
- [ ] **AS-AUD-021-AC07:** Review reversals and unusual post-year-end journals using a separately identified subsequent-period population and source-supported links. — Source: `AWP-14-07`
- [ ] **AS-AUD-021-AC08:** Document fraud indicators or management-override concerns with restricted visibility, contradictory evidence, follow-up and the auditor's conclusion; no automated accusation or opinion. — Source: `AWP-14-08`
- [ ] **AS-AUD-021-AC09:** Flag results are reproducible from the preserved population and criteria version. Large queries remain bounded, and missing origin/authorization fields cannot be silently treated as benign.

**Focused verification**

- Use synthetic manual, high-value, year-end and reversal entries and verify transparent flag explanations.
- Omit source creator/approval fields and ensure the result says unavailable, not unauthorized or approved.
- Verify audit-adjustment journals and firm ledger entries are not substituted for the client journal population.
- Check restricted concern records do not appear in ordinary portal search or exports.

**Deliverable:** A transparent, source-backed client-journal audit workflow with human fraud/override assessment.

---

<a id="as-aud-022"></a>
### AS-AUD-022 — Implement reproducible Analytical Review with documented investigations

**User story:** As an audit reviewer, I want to compare periods, trends, margins and relevant ratios using preserved inputs and investigate significant fluctuations, so that analytical conclusions can be reproduced and are not just free-text procedure descriptions.

**Priority:** High  
**Proposed milestone:** GAP-M4  
**Depends on:** [AS-AUD-005](#as-aud-005)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-15-01`, `AWP-15-02`, `AWP-15-03`, `AWP-15-04`, `AWP-15-05`, `AWP-15-06`, `AWP-15-07`, `AWP-15-08`

**Gap to establish:** The reviewed domain has generic risk responses but no evidenced complete analytical-review workflow. The source does not prescribe ratio definitions, day bases or fluctuation thresholds.

**Reuse boundary:** Reuse versioned TB/GL/schedules and pure calculation conventions; store analytical results in existing workpapers with reviewed evidence and conclusions.

**Minimum information:** Review type; periods/source revisions; approved account/sign mapping; comparable values; metric formula/version; denominator/day basis; expectations; investigation thresholds; computed result; management explanation/evidence; conclusion.

**Scope and implementation notes**

- Differentiate planning analytics, substantive analytical procedures and final overall review as required by the existing specification. Do not reuse one approval as proof of all three.
- Implement a small approved metric catalog, not a BI platform or arbitrary expression engine.
- All formulas, sign conventions, thresholds and fallback denominators below are design requirements to be approved, not formulas dictated by S1.

**Acceptance criteria**

- [ ] **AS-AUD-022-AC01:** Compare current-year results with prior year using aligned entity, period, currency and approved account mappings; disclose incomparable data rather than fabricating comparatives. — Source: `AWP-15-01`
- [ ] **AS-AUD-022-AC02:** Analyse monthly revenue/expense trends from available monthly source data and identify missing periods explicitly; annual totals cannot be arbitrarily distributed into months. — Source: `AWP-15-02`
- [ ] **AS-AUD-022-AC03:** Compute and compare gross-profit and net-profit margins under approved numerator/revenue definitions and sign conventions; preserve formulas and input account mappings. — Source: `AWP-15-03`
- [ ] **AS-AUD-022-AC04:** Analyse significant movements in major accounts with absolute changes and, where meaningful, percentage changes under a recorded denominator convention. — Source: `AWP-15-04`
- [ ] **AS-AUD-022-AC05:** Calculate relevant approved ratios/KPIs with versioned formulas and source inputs; unsupported or irrelevant metrics remain unavailable or approved N/A rather than zero. — Source: `AWP-15-05`
- [ ] **AS-AUD-022-AC06:** Review receivable/payable/inventory days where applicable, recording average-or-closing balance basis, credit-sales/purchases/cost denominator, day count and any explicitly approved proxy. — Source: `AWP-15-06`
- [ ] **AS-AUD-022-AC07:** Investigate significant/unexpected fluctuations against documented expectations and investigation criteria; a threshold flag creates required review, not an automatic conclusion. — Source: `AWP-15-07`
- [ ] **AS-AUD-022-AC08:** Record management explanations and supporting evidence and the auditor's corroboration/conclusion; an explanation without support cannot silently clear a required investigation. — Source: `AWP-15-08`
- [ ] **AS-AUD-022-AC09:** Zero/missing denominators return Undefined/InsufficientData with reason, never infinity or a misleading zero; negative bases and sign changes are labelled and use an approved method.
- [ ] **AS-AUD-022-AC10:** Final analytical review binds the exact final TB/FS and current adjustments. A replacement package makes the current review stale until impact is assessed.

**Focused verification**

- Synthetic fixture: revenue 1,000,000, gross profit 300,000 and net profit 100,000 yields 30% and 10% margins under positive-value definitions.
- With approved average AR 100,000, annual credit sales 1,000,000 and 365 days, receivable days equals 36.5; changing the day basis must change the versioned result.
- Test a zero denominator, missing month, negative prior balance and unsupported management explanation.
- Replace the final TB and verify the final review is not still current.

**Deliverable:** An approved, reproducible analytical-review workpaper with investigated exceptions and distinct review purposes.

---

<a id="as-aud-023"></a>
### AS-AUD-023 — Implement Going Concern assessment, forecast review and human conclusion

**User story:** As an engagement manager, I want to review management's going-concern assessment and supporting forecasts, obligations and subsequent performance, so that the final conclusion is supported by current evidence rather than a questionnaire flag.

**Priority:** High  
**Proposed milestone:** GAP-M4  
**Depends on:** [AS-AUD-017](#as-aud-017), [AS-AUD-022](#as-aud-022)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-16-01`, `AWP-16-02`, `AWP-16-03`, `AWP-16-04`, `AWP-16-05`, `AWP-16-06`, `AWP-16-07`, `AWP-16-08`, `AWP-16-09`

**Gap to establish:** A going-concern indicator in the acceptance questionnaire is not the substantive assessment required by this source.

**Reuse boundary:** Reuse planning, loan, analytical and evidence workpapers plus existing professional approval and release dependencies. Do not create a general forecasting system.

**Minimum information:** Management assessment/version; approved assessment horizon; financial position; working-capital sources; forecast version; inflow/outflow assumptions; loan/facility commitments; adverse indicators; actual subsequent results; conclusion and reporting links.

**Scope and implementation notes**

- The assessment horizon, reporting implications and approval authority come from approved methodology. S1 does not define a universal time horizon, probability threshold or automatic opinion rule.

**Acceptance criteria**

- [ ] **AS-AUD-023-AC01:** Obtain management's going-concern assessment and record responsibility, date, scope, horizon and the exact version reviewed. — Source: `AWP-16-01`
- [ ] **AS-AUD-023-AC02:** Review current financial position and working capital against reconciled sources, retaining relevant trends and concerns. — Source: `AWP-16-02`
- [ ] **AS-AUD-023-AC03:** Review cash-flow forecasts, preserve forecast inputs/versions and check arithmetic and source consistency; do not confuse a historical cash-flow statement with a forecast. — Source: `AWP-16-03`
- [ ] **AS-AUD-023-AC04:** Check expected cash inflows and major payments against supporting evidence and record timing/amount assumptions and uncertainties. — Source: `AWP-16-04`
- [ ] **AS-AUD-023-AC05:** Review loan repayments and financing facilities using the borrowing workpaper, contractual evidence, available headroom and relevant unresolved conditions. — Source: `AWP-16-05`
- [ ] **AS-AUD-023-AC06:** Review losses, negative cash flows and overdue liabilities, documenting management responses and corroboration. — Source: `AWP-16-06`
- [ ] **AS-AUD-023-AC07:** Assess significant forecast assumptions; any sensitivity/scenario work records its approved assumptions and results without presenting them as predictions. — Source: `AWP-16-07`
- [ ] **AS-AUD-023-AC08:** Consider subsequent trading performance using dated actual evidence and investigate differences from the forecast. — Source: `AWP-16-08`
- [ ] **AS-AUD-023-AC09:** Record the auditor's conclusion, rationale, relevant uncertainty/disclosure/reporting implications and authorized review; the application cannot generate or approve this professional conclusion. — Source: `AWP-16-09`
- [ ] **AS-AUD-023-AC10:** Changed forecasts, financing facts or significant subsequent performance trigger impact review of going concern, related disclosures and completion.

**Focused verification**

- Check a synthetic forecast bridge: opening cash 10,000 + inflows 30,000 - outflows 45,000 gives closing cash -5,000 and requires review, not an automatic opinion.
- Record an unconfirmed funding assumption and compare forecast receipts with subsequent actual collections.
- Revise financing evidence after partner review and verify affected completion dependencies become stale.

**Deliverable:** A version-bound going-concern pack with forecast checking, evidence and an explicit human conclusion.

---

<a id="as-aud-024"></a>
### AS-AUD-024 — Implement Subsequent Events register and reviewed-through-date coverage

**User story:** As an audit senior, I want to record post-year-end review procedures, evaluate identified events and verify their financial-statement treatment, so that subsequent events are explicitly covered through the required review date and cannot disappear inside generic workpapers.

**Priority:** High  
**Proposed milestone:** GAP-M4  
**Depends on:** [AS-AUD-005](#as-aud-005)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-17-01`, `AWP-17-02`, `AWP-17-03`, `AWP-17-04`, `AWP-17-05`, `AWP-17-06`, `AWP-17-07`, `AWP-17-08`

**Gap to establish:** The prior code review did not establish a dedicated subsequent-events workflow. The existing .NET specification already names this program and its completion dependency. [R4] [R5]

**Reuse boundary:** Reuse source evidence, tests, findings, adjustment plans and exact FS/release versions; add only the register and coverage data not already represented.

**Minimum information:** Event ID/title; occurrence/discovery dates; entity; source; financial/non-financial effect; review period/through date; management discussion; auditor disposition and rationale; adjustment/disclosure references; reviewer and refreshed assessment.

**Scope and implementation notes**

- Dates define review coverage; a later report date or newly received evidence can require additional procedures. Do not automatically classify an event from its date alone.

**Acceptance criteria**

- [ ] **AS-AUD-024-AC01:** Record the post-year-end bank statements/transactions reviewed, their covered dates and identified matters or supported no-exception result. — Source: `AWP-17-01`
- [ ] **AS-AUD-024-AC02:** Review significant post-year-end sales, purchases and payments using the selected sources and recorded significance criteria. — Source: `AWP-17-02`
- [ ] **AS-AUD-024-AC03:** Review board/management minutes, preserving meeting dates, minute versions, relevant matters and any missing coverage. — Source: `AWP-17-03`
- [ ] **AS-AUD-024-AC04:** Check new loans, investments and major asset purchases after year-end and link related evidence and affected areas. — Source: `AWP-17-04`
- [ ] **AS-AUD-024-AC05:** Review litigation and significant legal developments based on authorized source evidence; record confidentiality, scope limitations and auditor follow-up rather than giving legal advice. — Source: `AWP-17-05`
- [ ] **AS-AUD-024-AC06:** Document discussions with management, including participants, date, matters discussed and corroborating evidence. — Source: `AWP-17-06`
- [ ] **AS-AUD-024-AC07:** Record the auditor's adjustment/disclosure/no-change decision and rationale under the approved reporting basis; unresolved decisions remain pending. — Source: `AWP-17-07`
- [ ] **AS-AUD-024-AC08:** Verify required adjustments/disclosures in the exact final FS/TB version and link the treatment evidence before concluding the relevant event. — Source: `AWP-17-08`
- [ ] **AS-AUD-024-AC09:** The overall review records its reviewed-through date and required coverage date. A later report-date proposal or relevant new evidence prevents reuse of an obsolete completion assessment.
- [ ] **AS-AUD-024-AC10:** Events discovered after issuance enter the existing amendment/assessment process; preserve the original issued package and never overwrite it to hide a correction.

**Focused verification**

- Record one event requiring an adjustment, one disclosure and one supported no-change decision without software-selected professional conclusions.
- Review through a synthetic date, then advance the intended report date and verify missing-period work is required.
- Add an event after issuance and confirm the old artifact/approval history stays unchanged.

**Deliverable:** An explicit subsequent-events program, event register, coverage record and final-treatment verification.

---

<a id="as-aud-025"></a>
### AS-AUD-025 — Close Financial Statements & Disclosures residual review gaps

**User story:** As a financial statement audit reviewer, I want to verify the exact final statements, comparatives and notes against audited inputs and all relevant audit conclusions, so that existing financial-package generation is backed by complete statement/disclosure review.

**Priority:** High  
**Proposed milestone:** GAP-M4  
**Depends on:** [AS-AUD-018](#as-aud-018), [AS-AUD-019](#as-aud-019), [AS-AUD-020](#as-aud-020), [AS-AUD-023](#as-aud-023), [AS-AUD-024](#as-aud-024), [AS-AUD-026](#as-aud-026)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-18-01`, `AWP-18-02`, `AWP-18-03`, `AWP-18-04`, `AWP-18-05`, `AWP-18-06`, `AWP-18-07`, `AWP-18-08`, `AWP-18-09`

**Gap to establish:** Versioned mappings, deterministic package calculations and disclosure fields are reusable foundations, not proof that every primary statement, note and comparative review is complete. [R6]

**Reuse boundary:** Extend existing FinancialStatementService/Calculator, mapping/adjustment plans and package validation; use approved render/import mechanisms rather than replacing the accounting engine.

**Minimum information:** Final package/version/hash; audited TB and adjustment-plan versions; statement lines; cash-flow and equity bridges; policy/estimate notes; comparatives; disclosure checklist/version/applicability; audit-area links; mathematical and rendered-output review.

**Scope and implementation notes**

- Support the current approved single-entity reporting framework/template family. This backlog does not add group consolidation or certify every reporting framework.
- Require real selected statement/notes artifacts in the approved readable output format, whether generated by the supported renderer or supplied and verified under the existing workflow.
- Native generation is not complete merely because canonical text or a package hash exists; inspect actual output content, figures and required components.

**Acceptance criteria**

- [ ] **AS-AUD-025-AC01:** Agree final financial statements to the audited Trial Balance and selected adjustment plan, retaining line-to-account mappings and explaining any presentation/rounding differences. — Source: `AWP-18-01`
- [ ] **AS-AUD-025-AC02:** Review the statement of financial position and profit/loss, including supported line values, totals and cross-statement consistency. — Source: `AWP-18-02`
- [ ] **AS-AUD-025-AC03:** Review the cash-flow statement and statement of changes in equity with complete opening/movement/closing bridges and the exact input versions; neither component may be an empty placeholder. — Source: `AWP-18-03`
- [ ] **AS-AUD-025-AC04:** Review accounting policies and significant estimates with evidence of consistency with the recorded accounting treatment and relevant audit conclusions. — Source: `AWP-18-04`
- [ ] **AS-AUD-025-AC05:** Agree comparatives with prior-year audited FS and separately evidence approved restatements/reclassifications. — Source: `AWP-18-05`
- [ ] **AS-AUD-025-AC06:** Check notes against supporting schedules through a versioned disclosure checklist; each item has a supported response or authorized N/A rationale. — Source: `AWP-18-06`
- [ ] **AS-AUD-025-AC07:** Check related-party, tax and going-concern disclosures against their reviewed workpapers and required reporting treatment. — Source: `AWP-18-07`
- [ ] **AS-AUD-025-AC08:** Check subsequent-event disclosures against AS-AUD-024 event dispositions and the selected final package. — Source: `AWP-18-08`
- [ ] **AS-AUD-025-AC09:** Perform a final mathematical/consistency review of the actual selected artifacts as well as stored figures, including totals, note references, units/currency, periods and comparative labels. — Source: `AWP-18-09`
- [ ] **AS-AUD-025-AC10:** A changed final statement, note, mapping, adjustment or relevant audit conclusion invalidates affected current reviews; preserve original artifact hashes and approvals.
- [ ] **AS-AUD-025-AC11:** Methodology/checklist incompleteness, missing statements or unsupported output format is visible and blocks final acceptance; no claim of general framework compliance is inferred from arithmetic tests.

**Focused verification**

- Validate a complete four-statement-and-notes fixture and its audited-TB, cash-flow and equity bridges.
- Reject a missing statement of changes in equity, unexplained comparative mismatch and a note inconsistent with a reviewed event.
- Change a rendered figure while leaving structured inputs unchanged and ensure exact artifact verification detects the mismatch.
- Verify zero-adjustment engagements can produce a valid selected final package without a fabricated journal.

**Deliverable:** A complete, reviewed final FS/notes package using existing accounting infrastructure, with only verified residual features added.

---

<a id="as-aud-026"></a>
### AS-AUD-026 — Implement audit-difference evaluation, corrected/unadjusted schedules and final-TB verification

**User story:** As an audit manager, I want to track every identified audit difference, obtain management responses and evaluate its individual, aggregate and qualitative effects, so that the final misstatement assessment supports a human reporting conclusion and cannot be reduced to a corrected flag.

**Priority:** Blocker  
**Proposed milestone:** GAP-M2  
**Depends on:** [AS-AUD-005](#as-aud-005)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-19-01`, `AWP-19-02`, `AWP-19-03`, `AWP-19-04`, `AWP-19-05`, `AWP-19-06`, `AWP-19-07`

**Gap to establish:** Finding.MonetaryAmount/Corrected and adjustment plans are foundations. The formal schedule, aggregate evaluation and evidence-backed correction status require explicit end-to-end acceptance. [R3, R5]

**Reuse boundary:** Extend the current Finding and adjustment/source-reflection services; retain one authoritative journal and final-TB calculation path. Do not create a second ledger.

**Minimum information:** Difference ID/type; affected accounts/statements/entity/period; signed impact and amount basis; source workpaper; management response; correction authorization; linked journal/revision/reflection evidence; current disposition; materiality/evaluation version; qualitative/reporting conclusion.

**Scope and implementation notes**

- Separate monetary and disclosure matters, factual/judgemental/projected classifications where approved, control deficiencies and evidence limitations.
- Treat monetary direction and statement impact explicitly. A balanced debit/credit correction must not be counted as twice its economic misstatement.
- Recognize proposed, management-accepted, authorized, posted, reflected-in-final-TB and verified-corrected as distinct facts; map them to existing state/history structures.

**Acceptance criteria**

- [ ] **AS-AUD-026-AC01:** Record all identified audit differences with stable IDs, supporting workpapers, financial/narrative impact and a traceable disposition; retain duplicate-link and correction history rather than silently deleting items. — Source: `AWP-19-01`
- [ ] **AS-AUD-026-AC02:** Obtain and preserve management's response to each proposed adjustment, including accepted, declined or pending decisions and authorized responder/evidence. — Source: `AWP-19-02`
- [ ] **AS-AUD-026-AC03:** Recalculate each difference's financial-statement impact using explicit sign/account/period mappings; disclose non-monetary impacts and avoid counting debit and credit legs as separate identical errors. — Source: `AWP-19-03`
- [ ] **AS-AUD-026-AC04:** Maintain current corrected/adjusted and uncorrected/unadjusted schedules that reconcile to the master register. Display pending/partial/verified states so a partial correction is not hidden. — Source: `AWP-19-04`
- [ ] **AS-AUD-026-AC05:** Compare individual and aggregate unadjusted differences with the approved current materiality assessment; show relevant signed and absolute/grouped views under documented methodology without silently netting unrelated errors. — Source: `AWP-19-05`
- [ ] **AS-AUD-026-AC06:** Record the auditor's assessment of remaining differences and reporting implications, including qualitative effects and relevant prior-period/projection considerations. A threshold comparison cannot generate an audit opinion. — Source: `AWP-19-06`
- [ ] **AS-AUD-026-AC07:** Verify agreed adjustments are actually posted in the authorized accounting/reporting path and reflected in the final TB, with journal revision and posting/source evidence; management agreement alone is not proof of posting. — Source: `AWP-19-07`
- [ ] **AS-AUD-026-AC08:** Use existing source-reflection decisions so already reflected journals are not applied twice. UNKNOWN, partial reflection or stale decisions must block plan finalization until resolved.
- [ ] **AS-AUD-026-AC09:** A manually entered Corrected flag cannot satisfy the new correction-verification gate without underlying evidence; legacy rows lacking that evidence remain unverified until reviewed.
- [ ] **AS-AUD-026-AC10:** Changing a finding, management response, posting/reflection evidence, materiality or final TB creates a new evaluation dependency and invalidates affected completion reviews.
- [ ] **AS-AUD-026-AC11:** Preserve legitimate engagements with no identified or agreed monetary adjustments. Finalize a no-adjustment path against the accepted TB without inventing a zero-value journal.
- [ ] **AS-AUD-026-AC12:** An uncorrected difference may remain with a properly reviewed reporting consequence. Do not require every difference to become zero or corrected before any appropriate report can proceed.
- [ ] **AS-AUD-026-AC13:** Sample-based projected amounts may be included only with an approved method and explicit known/projected overlap handling; this story does not enable an unvalidated statistical projection engine.

**Focused verification**

- Synthetic fixture: 10,000 revenue overstatement and 8,000 expense understatement both overstate profit; grouped profit impact is 18,000, not 2,000. With approved materiality 15,000, show the comparison and require human evaluation.
- Add a separate -5,000 profit impact and display signed total 13,000 and absolute component total 23,000 without claiming either alone determines the opinion.
- Verify a debit/credit journal for 10,000 is not recorded as a 20,000 misstatement.
- Retest a posted correction reflected in a replacement TB, a partial correction, a qualitative below-threshold issue and a no-adjustment engagement.
- Change materiality after evaluation; the previous result must remain historical rather than authorize completion.

**Deliverable:** A complete audit-differences register, corrected/unadjusted schedules, versioned evaluation and evidence-backed final-TB correction bridge.

---

<a id="as-aud-027"></a>
### AS-AUD-027 — Complete final audit gates, representations, professional review and exact report release

**User story:** As an engagement partner, I want to see authoritative completion evidence and authorize only the exact reviewed report/financial-statement package, so that issuance cannot occur with missing required work, stale conclusions or unverified signing/protection.

**Priority:** Blocker  
**Proposed milestone:** GAP-M5  
**Depends on:** [AS-AUD-025](#as-aud-025), [AS-AUD-026](#as-aud-026)  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** `AWP-20-01`, `AWP-20-02`, `AWP-20-03`, `AWP-20-04`, `AWP-20-05`, `AWP-20-06`, `AWP-20-07`, `AWP-20-08`, `AWP-20-09`, `AWP-20-10`

**Gap to establish:** Completion/EQR/representation/release records exist, but that alone does not prove the source's complete set of professional prerequisites is enforced by the release command.

**Reuse boundary:** Extend existing completion projections and ReleaseService gate evaluation. Reuse exact snapshots, approvals, signatures, checkpoints, records protection and recovery safeguards; do not build a second release path. [R5]

**Minimum information:** Applicable program and area completions; review-point clearance; risk responses; current materiality/misstatement assessment; going-concern/subsequent-event/FS/final-analytics reviews; signed representation; reviewer decisions; report date; final artifacts/signature lineage; release evidence.

**Scope and implementation notes**

- Implement professional completion evaluation locally against actual persisted evidence. Live signing, SharePoint/Purview and authorized release remain separately gated existing integrations.
- Map senior/manager/partner approval to approved role policy. Engagement-quality review is conditional under the existing specification, not an additional universal obligation invented from S1.
- S1 requires all review points cleared. Do not quietly treat deferred/cancelled points as cleared; resolve the documented policy difference in Section 7 before permitting an alternative.

**Acceptance criteria**

- [ ] **AS-AUD-027-AC01:** Verify every applicable source procedure is completed, cross-referenced and currently reviewed, with authorized N/A decisions where permitted; a section title or empty workpaper is insufficient. — Source: `AWP-20-01`
- [ ] **AS-AUD-027-AC02:** Verify all required audit review points are cleared by authorized reviewers with evidence. Future management remediation actions remain separate and are not falsely marked implemented just to close the audit. — Source: `AWP-20-02`
- [ ] **AS-AUD-027-AC03:** Confirm every significant risk has an adequate recorded response and reviewed work/conclusion, not just a planned procedure link. — Source: `AWP-20-03`
- [ ] **AS-AUD-027-AC04:** Require the current final materiality assessment and audit-differences evaluation, including unresolved items' reviewed reporting consequences. — Source: `AWP-20-04`
- [ ] **AS-AUD-027-AC05:** Require current going-concern and subsequent-event procedures/conclusions, including reviewed-through-date coverage for the proposed report date. — Source: `AWP-20-05`
- [ ] **AS-AUD-027-AC06:** Require completion and review of the financial-statement disclosure checklist for the exact final package. — Source: `AWP-20-06`
- [ ] **AS-AUD-027-AC07:** Obtain the signed management representation letter; verify authorized signatory, relevant dates, entity/period, required scope and exact evidence/package relationship. An Obtained boolean without the signed evidence is insufficient. — Source: `AWP-20-07`
- [ ] **AS-AUD-027-AC08:** Require final analytical review against the selected final TB/FS and resolve its required investigations. — Source: `AWP-20-08`
- [ ] **AS-AUD-027-AC09:** Record the required senior, manager and partner reviews with current authority and exact submission dependencies; also retain existing conditional EQR gates without allowing a technical administrator to impersonate professional approval. — Source: `AWP-20-09`
- [ ] **AS-AUD-027-AC10:** Finalize the auditor's report and signed FS as distinct selected artifacts, using approved methodology, exact-version professional/management approvals and verified signing lineage; release only through the existing protected command. — Source: `AWP-20-10`
- [ ] **AS-AUD-027-AC11:** The authoritative server-side release check re-reads scope, actor authority, current generations, unresolved blockers and required evidence under existing transaction guards. UI checkboxes or cached green badges cannot authorize release.
- [ ] **AS-AUD-027-AC12:** Unknown/missing signature, provider protection, release checkpoint or recovery evidence remains blocked according to the existing release/delivery boundary; simulation success cannot become production acceptance.
- [ ] **AS-AUD-027-AC13:** A new relevant input or changed report date after review requires impact assessment and refreshed applicability. Preserve prior signed/issued artifacts and use the existing amendment process after issuance.
- [ ] **AS-AUD-027-AC14:** Readiness is shown separately for procedure completion, professional approvals, signing/protection, issuance and delivery. Delivery failure never creates a duplicate release event.

**Focused verification**

- Attempt completion with each class of missing prerequisite: unperformed procedure, review point, risk response, final materiality/evaluation, representation, analytics or disclosure review.
- Submit a new finding or subsequent event after approval and verify the release command rejects the stale package even before background projection updates.
- Verify a supported human-approved reporting consequence for an uncorrected misstatement is not blocked merely because its amount is nonzero.
- Exercise signature-hash mismatch, wrong signer scope, missing provider evidence and duplicate release request through existing focused gate tests.
- Record authorized live signing/protection/release results only in the real-tenant acceptance track; otherwise retain BLOCKED_EXTERNAL.

**Deliverable:** A source-complete professional completion pack integrated into the existing exact-version release gate, with local and live acceptance reported separately.

---

<a id="as-aud-028"></a>
### AS-AUD-028 — Prove checklist coverage, archive reproducibility and user acceptance

**User story:** As an audit methodology owner and technical reviewer, I want to verify the complete source checklist through realistic workflows and preserved evidence, so that the milestone closes only when the implemented audit experience is demonstrably usable and its limitations are explicit.

**Priority:** Blocker  
**Proposed milestone:** GAP-M5  
**Depends on:** All stories AS-AUD-001–AS-AUD-027, subject to their actual required scope and explicitly recorded acceptance blockers.  
**Status:** Proposed / acceptance not yet verified.

**Source ownership:** Shared enabling / verification design; see S1 coverage and repository contract.

**Gap to establish:** Code existence, a seeded program and a green generic test count are not source-complete acceptance evidence.

**Reuse boundary:** Reuse the repository test harness, existing PostgreSQL integration profile, artifact/records export pipeline and execution ledger. Add focused scenario coverage for changed behavior only.

**Minimum information:** AWP/AC coverage record; commit/test/fixture; screenshot or witnessed workflow evidence where useful; methodology reviewer; local/live acceptance result; blocker owner; selected snapshot/export manifest; known limitations.

**Scope and implementation notes**

- Use a compact set of synthetic or explicitly authorized anonymized engagements covering all source procedures, including one end-to-end engagement and targeted edge cases.
- Do not require 165 independent test files or create a parallel verification framework. One coherent scenario can prove multiple AWP/AC requirements.
- Professional methodology acceptance, technical checks and actual external-provider acceptance are separate signatures/evidence tracks.

**Acceptance criteria**

- [ ] **AS-AUD-028-AC01:** Every AWP ID has a supported UI/command path, persisted result and evidence-backed acceptance record or an explicit unresolved blocker. Missing evidence prevents the overall milestone from being labelled complete.
- [ ] **AS-AUD-028-AC02:** All 20 sections and 165 procedures are available in the published source program; engagement N/A decisions do not count as implementation of an otherwise unsupported procedure.
- [ ] **AS-AUD-028-AC03:** Run the end-to-end scenario and focused negative scenarios in Section 8, reusing existing tests; record actual commit, commands/results and non-executed checks honestly.
- [ ] **AS-AUD-028-AC04:** Demonstrate source -> population/selection -> procedure/workpaper -> evidence/result -> finding/adjustment -> final evaluation -> report package traceability, including a relevant source change and renewed review.
- [ ] **AS-AUD-028-AC05:** Extend existing authorized archive exports to include adopted program versions, applicability decisions, structured tests/calculations, confirmation provenance, new assessments and exact submitted snapshots. This is not permission to release the internal audit file to clients.
- [ ] **AS-AUD-028-AC06:** An archived/exported calculation and its narrative can be reconstructed from preserved inputs and versions without reading changed live source files; external record protection is verified separately.
- [ ] **AS-AUD-028-AC07:** Verify cross-firm/client/engagement isolation, restricted payroll/fraud access, stale revision rejection and approved N/A behavior using shared controls plus only necessary feature regressions.
- [ ] **AS-AUD-028-AC08:** An authorized audit owner witnesses the workflows and signs off methodology/formula/template choices and source coverage. Technical success cannot substitute for that approval.
- [ ] **AS-AUD-028-AC09:** Report LocalFeatureAcceptance, ProfessionalAcceptance and LiveIntegrationAcceptance separately. Unavailable authorized signing/provider fixtures remain blocked and prevent a claim of complete production readiness.
- [ ] **AS-AUD-028-AC10:** Update execution/traceability records, user instructions and known limitations. Merge remains subject to current-head reviews/checks and the owner's explicit authorization; this file is not that authorization.

**Focused verification**

- Execute a single-entity year-end scenario plus targeted scenarios for non-response, source replacement, no-adjustment finalization, signed adverse monetary impacts and report-date extension.
- Compare an export's immutable input/result references to the reviewed workpaper revisions.
- Inspect the 165-row traceability manifest for orphaned IDs, duplicate ownership and unsupported completion claims.

**Deliverable:** A reviewed source-coverage manifest, reproducible audit pack, truthful acceptance evidence and remaining external blocker list.

---

<a id="data-contracts"></a>
## 6. Minimal data and integration contracts

The names below describe required information, **not an instruction to create a new table for every row**. Inspect existing schema and choose the smallest compatible extension. Keep established `AuditSphereOps.*` namespaces and the existing modular-monolith boundaries.

| Information boundary | Required information / relationship | Reuse-first implementation direction |
|---|---|---|
| Program library and engagement adoption | Source hash, immutable published version, 20 sections, stable AWP IDs, conditional guidance, approved engagement adoption | Extend a compatible existing template mechanism or add a small audit-program model; keep CE/RV semantics unchanged |
| Procedure applicability and execution | Program/AWP identity, scope, owner, approved N/A, actual risk/assertion links, workpaper and review | Extend `AuditProcedure`/workpaper association; do not manufacture dummy risk records |
| Schedule and population | Source row identities, as-of period, signed data, count/totals, reconciliation, accepted version | Reuse `SourceReceipt`, source datasets and `PopulationVersion`; add actual immutable rows only where missing |
| Selections and item outcomes | Stable selected IDs, method/rationale, evidence, expected/observed result, cut-off dates, payment allocations, exceptions | Add the smallest shared selection/result records needed; no area-specific sampling engines |
| Specialized workpaper detail | Field sets listed in each area story; calculation inputs/results/method and source references | Explicit validated types or child records; avoid an arbitrary script/form engine |
| Confirmation case | Verified contact basis, approved request, actual send/receive provenance, differences, alternative work and conclusion | One shared register integrated with existing evidence and authorized communications |
| Overall assessment | Analytical, going-concern and subsequent-event inputs, coverage dates, results and human conclusions | Versioned details attached to existing workpaper/submission and review lifecycles |
| Audit differences | Classification, signed statement impacts, management decision, correction evidence, current evaluation | Extend `Finding` and existing adjustment/source-reflection services; no second ledger |
| Completion proof | Exact source-program coverage, current professional decisions, final FS/report, representations, signing/protection evidence | Extend current completion evaluation, manifests and release service; no shortcut issue command |
| Archive export | All selected program/procedure/result/calculation/evidence versions required to reproduce reviewed work | Extend existing authorized records/manifest exports; do not change retention policy or client-release permissions |

### 6.1 Query/command boundaries

The following capabilities must have explicit application-layer commands using current conventions: publish a program; adopt/tailor an engagement program; decide applicability; approve a reconciled population; record/approve selections; save and submit structured tests; record/reconcile confirmations; evaluate area conclusions; record/verify differences; review final assessments; evaluate completion.

Do not invent a new public REST API if existing Blazor/application commands are sufficient. Reuse current route naming, navigation and design. Queries return actionable next steps and truthful incomplete states. Destructive deletion of used source/evidence objects is not an ordinary edit operation.

### 6.2 Small shared record invariants

1. All workflow/result associations carry the scope needed for database-enforced isolation; cross-scope foreign keys must be rejected, not repaired by trusting UI input.
2. Published/adopted methodology and reviewed evidence versions remain reconstructible. Version identity must not be a mutable label pointing to new content.
3. Selected row IDs resolve within the exact population version. Replacement populations do not rewrite selections.
4. Subsequent-payment allocations cannot silently reuse the same amount; credit/reversal handling is explicit.
5. Current correction status and completion readiness are derived from authoritative evidence and current reviews, not writable success flags.
6. Approved snapshots/export manifests include all new structured details that could change the professional conclusion.

<a id="decisions"></a>
## 7. Methodology decisions and external dependencies

### 7.1 Source gaps that must not be filled by guesswork

S1 provides an audit procedure checklist, not a complete software or professional-methodology specification. Record the following decisions against the approved firm/engagement profile. A decision may reuse an existing approved policy; do not create a new configurable framework merely to store it.

| Decision | Authorized source/owner | Affected stories | Required behavior while unresolved |
|---|---|---|---|
| Reporting framework, entity/period, presentation currency, accepted statement/template family | Engagement partner / approved existing profile | 007, 018, 020, 023–027 | Do not claim final FS/report acceptance |
| Program wording/tailoring, required evidence and N/A authority | Audit methodology owner | 002–003 and all area stories | Keep unpublished guidance draft; block unsupported N/A approval |
| Significant-balance/item criteria, age bands, cut-off and subsequent-review windows | Engagement audit team under approved methodology | 005, 008–009, 011–024 | Require explicit recorded parameters or a supported documented manual selection |
| Materiality benchmark, rates, thresholds and qualitative evaluation | Authorized auditor/reviewer | 007, 010, 022, 026–027 | Use no invented materiality defaults; block dependent acceptance |
| ECL model, segmentation, rates, overlays and independent checking | Authorized audit/accounting specialists | 010 | Native method stays disabled; approved reviewed external work is labelled explicitly |
| Inventory costing/NRV units and inputs; depreciation and interest conventions | Approved policy/contracts and technical reviewer | 011, 014, 017 | Unsupported calculations remain unperformed/externally reviewed, not silently approximated |
| Payroll contractual/statutory basis; tax jurisdiction/computation source | Authorized client records and responsible specialist | 016, 020 | No fabricated legal rules, rates or deadlines |
| Metric definitions, account signs, comparable periods, average balances and day/denominator bases | Audit methodology owner | 022 | Undefined/insufficient data is shown explicitly |
| Going-concern horizon; subsequent-event review coverage and reporting implications | Engagement partner under approved methodology | 023–025, 027 | No automatic horizon, event classification or report opinion |
| Aggregate misstatement basis, permitted grouping/netting, projections and qualitative treatment | Audit methodology owner / partner | 026–027 | Preserve separate effects and require human evaluation; no automatic opinion |
| Role eligibility, review sequence, representation signatory/date and conditional EQR | Approved firm policy / engagement owner | 003, 007, 025–027 | Missing/unauthorized decision cannot satisfy a gate |
| Approved signing method, storage protection and live release evidence | Authorized professional, records and platform owners | 027–028 | Remain blocked; do not convert test adapters into live evidence |

### 7.2 Explicit source/specification tension: review-point clearance

S1 `AWP-20-02` says **“Ensure all review points have been cleared.”** The existing specification also describes controlled dispositions such as approved nonblocking deferral or a concluded reporting implication. These are not silently interchangeable. [R4]

For this checklist's default implementation, required audit review points must be cleared by an authorized reviewer with evidence of their resolution. A supported reporting consequence can resolve the *review question* without pretending that the underlying misstatement was corrected; the finding remains correctly classified. A future client remediation action is a different record, not an outstanding audit review point.

An implementation must not count a deferred/cancelled review point as cleared merely to make the checklist green. Any alternative completion treatment needs an explicit methodology-owner decision, recorded change to the adopted checklist interpretation and corresponding tests. Preserve the original S1 wording in the source manifest.

### 7.3 External acceptance tracks

| Track | Can be proven using local controlled fixtures? | What is still needed for real acceptance? |
|---|---|---|
| Program generation, forms, calculations, source reconciliation, authorization and local gates | Yes, with actual executed tests against appropriate repository infrastructure | Professional/formula/template approval and representative usability validation |
| Manual confirmation register | Yes, for state/provenance behavior | Actual authorized dispatch/response evidence for a real confirmation |
| Automated confirmation communication | Only command/contract behavior; no live-delivery claim | Approved provider/channel, credentials, recipient authority and observed outcome |
| SharePoint/Purview evidence storage/protection | Only local boundary behavior | Existing approved tenant/binding/grants/profile and observed provider behavior |
| Representation and signed report verification | Local validation rules only | Authorized signatories, real signed artifacts and approved verification method |
| Final issuance and external delivery | Local invariants and failure handling only | All existing live provider/protection/checkpoint/signature/recovery/professional gates |

This document neither authorizes external messages nor changes the existing provider roadmap. Record dependencies by the actual current GitHub issue IDs discovered during implementation. A blocked external test is not passed, and a skipped test is not acceptance evidence.

<a id="acceptance"></a>
## 8. End-to-end acceptance scenarios

### 8.1 Main proving scenario — one single-entity year-end audit

Use synthetic data in one declared currency, a prior audited package, a current-year TB and suitable supporting schedules. Choose an approved test methodology/profile and a published source program. Test values are illustrative software fixtures, not actual client amounts or recommended policy rates.

1. Create/adopt the program, assign the team, approve applicability and perform the nine planning procedures. Reconcile prior/current openings and approve actual materiality/risk/strategy inputs.
2. Ingest/reconcile bank, AR, inventory, sales, AP, asset, expense, payroll, borrowing, equity, related-party, tax and client journal sources as appropriate. Preserve source rows/versions and approved populations.
3. Record value/risk/manual selections and tests; perform confirmations, differences and alternatives. Include an unanswered confirmation, partial receipt allocation, inventory count discrepancy and an unrecorded liability.
4. Complete the account-area tests and approved ECL/depreciation/payroll/interest arithmetic. Record unresolved evidence and professional judgements separately from calculated results.
5. Complete client-journal/fraud-oriented tests and monthly/comparative analytical review. Obtain and corroborate management explanations.
6. Complete going-concern and subsequent-event work, including a defined reviewed-through date and an event requiring financial-statement treatment.
7. Record audit differences, management responses and controlled adjustments. Reconcile verified corrections into the final TB without reapplying already reflected journals; preserve uncorrected items and the human evaluation.
8. Review the final statements and notes, including cash flow, changes in equity, comparatives, significant policies/estimates and related-party/tax/going-concern/subsequent-event disclosures.
9. Complete final analytics, obtain/verify the signed representation, clear required review points, confirm significant-risk responses and obtain required senior/manager/partner and conditional EQR approvals.
10. Attempt release against exact selected artifacts. Local gate tests prove enforcement; real issuance/delivery occurs only in the separate authorized live acceptance track. Export the preserved audit evidence through existing authorized records functionality.

**Expected evidence:** Every supported AWP requirement maps to a specific persisted workflow/result and witnessed or automated acceptance result. A template row or an approved runtime N/A is not proof of the feature needed to perform that procedure on an applicable engagement.

### 8.2 Required focused adverse/boundary scenarios

| Scenario ID | Scenario | Expected result |
|---|---|---|
| GAP-E2E-01 | Same source program or ingest/selection command is retried | One logical result; no duplicated procedures, source rows or coverage |
| GAP-E2E-02 | Cross-client/engagement evidence, population or journal is referenced | Command/database scope rejection; no partial write or data leak |
| GAP-E2E-03 | Required evidence is missing and preparer attempts N/A or completion | No automatic N/A/pass; explicit limitation and authorized disposition required |
| GAP-E2E-04 | Client-forwarded confirmation or no response is entered | Neither is labelled independently authenticated direct agreement |
| GAP-E2E-05 | A selected item lacks support or is quietly replaced | Preserve original selection; require reason/review for replacement or alternative work |
| GAP-E2E-06 | A schedule has credit/negative rows, mixed currencies or unexplained variance | No silent deletion/netting; incompatible aggregation held; reconciliation remains open |
| GAP-E2E-07 | One bank payment is over-allocated across invoices | Reject unsupported allocation; retain actual matched and remaining amounts |
| GAP-E2E-08 | Calculation method/rate/denominator is missing or unsupported | Visible blocked/undefined/external-reviewed route; no fictional zero or formula |
| GAP-E2E-09 | Source/materiality/calculation changes after submission | Old submitted snapshot preserved; current dependent approval becomes stale pending impact review |
| GAP-E2E-10 | Opposing or duplicate misstatement effects are aggregated | Explicit signs/grouping; no double-counted journal legs or silent inappropriate netting |
| GAP-E2E-11 | Adjustment marked accepted/corrected without posting/reflection proof | Correction remains unverified; final-TB completion gate not satisfied |
| GAP-E2E-12 | The engagement needs no monetary adjustments | Valid no-adjustment finalization; no fabricated journal is required |
| GAP-E2E-13 | Report date advances beyond subsequent-events reviewed-through date | Further coverage/assessment required before final completion |
| GAP-E2E-14 | All tests are performed but a required review, representation or statement is missing | Server-side completion/release remains blocked |
| GAP-E2E-15 | A supported uncorrected item remains with human-reviewed reporting consequences | No forced zero-balance or false correction; appropriate approved reporting remains possible |
| GAP-E2E-16 | New finding/event arrives during candidate evaluation | Current generation/manifest check rejects stale readiness, even if a UI projection is old |
| GAP-E2E-17 | Live signing/protection/checkpoint is unavailable or mismatched | Local evidence stays local; production/release acceptance remains blocked |
| GAP-E2E-18 | Sensitive payroll/fraud evidence is requested through portal/export/search | Access denial and no confidential metadata leakage |
| GAP-E2E-19 | Evidence changes after issuance | Original issued package preserved; new assessment/amendment workflow used |
| GAP-E2E-20 | Archived calculation is reconstructed after source replacement | Use preserved inputs/method/results; do not read the changed live source as historical evidence |

These scenarios describe acceptance obligations, not 20 mandatory new end-to-end test suites. Prefer existing unit/integration/security/acceptance coverage, adding only the missing focused tests.

<a id="definition-of-done"></a>
## 9. Definition of Done and coding-agent execution

### 9.1 Definition of Ready per story

Before writing code, read the current source and relevant approved specification sections; identify the target issue, acceptance IDs, present behavior, smallest delta, dependencies, non-goals and test evidence needed. Resolve required methodology values through existing approved policy or record the exact blocker. Inspect the relevant application, domain, persistence and UI paths rather than inferring implementation from a README.

### 9.2 Definition of Done per story

- [ ] All story acceptance criteria and applicable shared controls are demonstrated; reusable behavior is linked rather than rebuilt.
- [ ] The actual user path works: scoped read model/form -> authorized command -> persisted state -> evidence/results -> required review -> relevant completion effect.
- [ ] Negative paths are truthful and actionable: missing/invalid data, wrong scope/role, stale revisions, unknown provider state and unsupported methods are not fake successes.
- [ ] Submitted structured/narrative evidence and prior approvals remain immutable and reproducible; relevant changes invalidate current applicability correctly.
- [ ] Any necessary migration preserves existing data and constraints. Legacy gaps are explicitly unverified; no historical approval or evidence is fabricated during backfill.
- [ ] Relevant existing tests and minimal new regressions pass under the required repository profile; actual commands, commit and results are recorded. No test is described as run unless it was run.
- [ ] Source IDs, acceptance IDs, implementation references, review evidence and known limitations are updated in the existing execution/traceability records.
- [ ] Required current-head reviews are complete and valid findings are resolved; no unrelated dependency, abstraction, temporary code, broad refactor or provider bypass is introduced.
- [ ] Affected professional methodology acceptance and live integration acceptance are recorded separately. A local story cannot close an unverified external release gate.

### 9.3 Epic exit criteria

**Coverage:** All 20 sections and 165 source procedures have product support and current traceability. Each AWP ID has one primary acceptance owner and demonstrated behavior; no unresolved source gap is hidden behind generic workpaper support.

**Usability:** Authorized auditors can execute each applicable workflow, inspect evidence/calculations, raise and resolve review points, and produce the required reviewed outputs. Specialized methods that remain unsupported are explicitly excluded from native automation and have an approved, evidence-backed route where allowed; they are never represented as implemented calculators.

**Completion safety:** The existing release command consumes the exact current source-program, audit-area, final-assessment, representation, approval and artifact evidence. Missing or stale prerequisites cannot be bypassed from the UI or another command.

**Acceptance:** The technical owner records executed results and the audit methodology owner accepts relevant forms, calculation bases and professional workflow. Real operational readiness additionally requires the existing live identity/document/signing/protection/recovery gates; this backlog cannot waive them.

### 9.4 GitHub workflow and merge authorization

Use existing repository/nested `AGENTS.md`, the approved spec and current GitHub rules. Recommended issue titles are `[AS-AUD-xxx] <story title>`; these are stable backlog identifiers, not pre-existing issue numbers. Reuse existing labels/milestones when equivalent. Suggested labels such as `area:audit` or `type:story` are proposals only and must be reconciled with the actual repository.

Create one focused issue/branch/PR per coherent story or small child slice. Record intent, acceptance criteria, non-goals and dependencies before editing. Avoid conflicts with other active work; do not introduce parallel agents or touch unrelated files. A story can be split only when a complete acceptance path remains clear and parent coverage is retained.

Target the repository's **actual current base branch**; the inspected snapshot used `master`, not an assumed `main`. Run relevant checks, request the required configured independent reviews, fix valid findings and keep the PR merge-ready. Do not weaken branch/ruleset controls, bypass reviews or count an automated neutral response as an independent approval where one is required.

**Merge authorization codeword:** `AUDITSPHERE-MERGE-APPROVED`.

The codeword is a required confirmation where the established project workflow requires it; its presence in this file is **not** authorization to merge. The owner must explicitly authorize the specific merge/action, with current required reviews/checks and repository protections still satisfied. Likewise, a request to generate this Markdown file does not authorize issue creation, commits, external messaging, data deletion, deployment or production release.

### 9.5 Ready-to-use coding-agent handoff

```text
Target nirzaf/AuditSphere only. Read current root/nested AGENTS.md and the
approved .NET specification. Use this document as the source-backed gap
backlog, not as proof that any feature or test is complete.

Start with AS-AUD-001. Re-read actual branch/commit, existing issues and
relevant code/tests. Map all AWP source IDs, identify reusable behavior and
implement only the earliest eligible missing slice in the documented
order. Preserve source wording and distinguish proposed software controls
from professional-methodology requirements.

Keep the existing architecture, namespaces, evidence/scope/revision controls,
accounting engine and release/provider boundaries. No dummy risks, fake
posting/confirmation/signature evidence, automatic professional conclusions,
or synthetic data presented as real acceptance. Record external blockers.

For each slice, state intent, acceptance criteria and non-goals; use an
isolated branch, the smallest complete diff and focused tests. Update the
existing execution/traceability records only with observed results. Produce
a PR and satisfy required current-head independent reviews and checks.

Do not merge without explicit owner authorization for the action and the
required AUDITSPHERE-MERGE-APPROVED confirmation. Do not interpret this file
or a passed local test as permission for a live professional release.
```


<a id="traceability"></a>
## Appendix A — Exact source-procedure traceability

The procedure text below is from the uploaded DOCX. Only identifiers, owner mappings and Markdown table formatting are added. Source statement ownership does not assert that the implementation or acceptance criterion is already complete. Shared stories AS-AUD-002–AS-AUD-006 support the mapped domain stories as relevant.

### A.1 Section-level completeness

| Source section | Procedure count | Primary story ownership |
|---|---:|---|
| 1. Planning & Risk Assessment | 9 | [AS-AUD-007](#as-aud-007) |
| 2. Cash & Bank | 8 | [AS-AUD-008](#as-aud-008) |
| 3. Trade Receivables | 9 | [AS-AUD-009](#as-aud-009), [AS-AUD-010](#as-aud-010) |
| 4. Inventory | 8 | [AS-AUD-011](#as-aud-011) |
| 5. Revenue / Sales | 8 | [AS-AUD-012](#as-aud-012) |
| 6. Purchases & Trade Payables | 8 | [AS-AUD-013](#as-aud-013) |
| 7. Fixed Assets | 9 | [AS-AUD-014](#as-aud-014) |
| 8. Expenses | 9 | [AS-AUD-015](#as-aud-015) |
| 9. Payroll | 8 | [AS-AUD-016](#as-aud-016) |
| 10. Loans & Borrowings | 9 | [AS-AUD-017](#as-aud-017) |
| 11. Equity / Share Capital | 7 | [AS-AUD-018](#as-aud-018) |
| 12. Related Parties | 7 | [AS-AUD-019](#as-aud-019) |
| 13. Tax & Statutory Liabilities | 7 | [AS-AUD-020](#as-aud-020) |
| 14. Journal Entries & Fraud | 8 | [AS-AUD-021](#as-aud-021) |
| 15. Analytical Review | 8 | [AS-AUD-022](#as-aud-022) |
| 16. Going Concern | 9 | [AS-AUD-023](#as-aud-023) |
| 17. Subsequent Events | 8 | [AS-AUD-024](#as-aud-024) |
| 18. Financial Statements & Disclosures | 9 | [AS-AUD-025](#as-aud-025) |
| 19. Audit Differences & Adjustments | 7 | [AS-AUD-026](#as-aud-026) |
| 20. Final Completion & Audit Report | 10 | [AS-AUD-027](#as-aud-027) |
| **Total** | **165** | **20 source sections, 21 domain stories plus 7 shared/verification stories** |

### A.2 Source section 1 — Planning & Risk Assessment

**Source:** S1, section 1, parsed page(s) 1.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-01-01` | Obtain company registration documents and basic company information. | [AS-AUD-007-AC01](#as-aud-007) |
| `AWP-01-02` | Obtain prior-year audited financial statements and audit report. | [AS-AUD-007-AC02](#as-aud-007) |
| `AWP-01-03` | Obtain current-year Trial Balance and draft financial statements. | [AS-AUD-007-AC03](#as-aud-007) |
| `AWP-01-04` | Agree opening balances with prior-year audited balances. | [AS-AUD-007-AC04](#as-aud-007) |
| `AWP-01-05` | Understand the nature of business, major revenue streams and accounting system. | [AS-AUD-007-AC05](#as-aud-007) |
| `AWP-01-06` | Identify significant account balances and transaction classes. | [AS-AUD-007-AC06](#as-aud-007) |
| `AWP-01-07` | Identify significant audit risks, fraud risks and areas involving management judgement. | [AS-AUD-007-AC07](#as-aud-007) |
| `AWP-01-08` | Determine materiality and performance materiality. | [AS-AUD-007-AC08](#as-aud-007) |
| `AWP-01-09` | Prepare the audit strategy and audit program. | [AS-AUD-007-AC09](#as-aud-007) |

### A.3 Source section 2 — Cash & Bank

**Source:** S1, section 2, parsed page(s) 1.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-02-01` | Obtain bank reconciliation for all bank accounts at year-end. | [AS-AUD-008-AC01](#as-aud-008) |
| `AWP-02-02` | Agree bank ledger balances with the Trial Balance. | [AS-AUD-008-AC02](#as-aud-008) |
| `AWP-02-03` | Agree bank reconciliation balances with year-end bank statements. | [AS-AUD-008-AC03](#as-aud-008) |
| `AWP-02-04` | Obtain direct bank confirmations and reconcile confirmed balances. | [AS-AUD-008-AC04](#as-aud-008) |
| `AWP-02-05` | Check outstanding cheques, deposits and other reconciling items. | [AS-AUD-008-AC05](#as-aud-008) |
| `AWP-02-06` | Investigate old or unusual outstanding items. | [AS-AUD-008-AC06](#as-aud-008) |
| `AWP-02-07` | Test selected bank transactions to supporting documents. | [AS-AUD-008-AC07](#as-aud-008) |
| `AWP-02-08` | Review subsequent bank statements for unusual transactions. | [AS-AUD-008-AC08](#as-aud-008) |

### A.4 Source section 3 — Trade Receivables

**Source:** S1, section 3, parsed page(s) 1–2.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-03-01` | Obtain the year-end receivable ageing report. | [AS-AUD-009-AC01](#as-aud-009) |
| `AWP-03-02` | Agree total receivables and ageing to the GL/TB. | [AS-AUD-009-AC02](#as-aud-009) |
| `AWP-03-03` | Review significant and overdue customer balances. | [AS-AUD-009-AC03](#as-aud-009) |
| `AWP-03-04` | Send customer balance confirmations and investigate differences. | [AS-AUD-009-AC04](#as-aud-009) |
| `AWP-03-05` | Perform alternative procedures for non-confirmed balances. | [AS-AUD-009-AC05](#as-aud-009) |
| `AWP-03-06` | Test selected sales invoices to delivery documents and ledger. | [AS-AUD-009-AC06](#as-aud-009) |
| `AWP-03-07` | Check subsequent customer collections through bank statements. | [AS-AUD-009-AC07](#as-aud-009) |
| `AWP-03-08` | Perform/recalculate ECL and assess adequacy of provision. | [AS-AUD-010-AC01](#as-aud-010) |
| `AWP-03-09` | Test sales and receivable cut-off around year-end. | [AS-AUD-009-AC08](#as-aud-009) |

### A.5 Source section 4 — Inventory

**Source:** S1, section 4, parsed page(s) 2.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-04-01` | Obtain the year-end inventory listing and agree it to the GL. | [AS-AUD-011-AC01](#as-aud-011) |
| `AWP-04-02` | Attend/observe physical inventory count where applicable. | [AS-AUD-011-AC02](#as-aud-011) |
| `AWP-04-03` | Perform auditor test counts and reconcile differences. | [AS-AUD-011-AC03](#as-aud-011) |
| `AWP-04-04` | Check inventory quantities against count sheets/final listing. | [AS-AUD-011-AC04](#as-aud-011) |
| `AWP-04-05` | Test inventory costs to purchase invoices or supporting records. | [AS-AUD-011-AC05](#as-aud-011) |
| `AWP-04-06` | Review slow-moving, damaged and obsolete inventory. | [AS-AUD-011-AC06](#as-aud-011) |
| `AWP-04-07` | Compare cost with NRV where applicable. | [AS-AUD-011-AC07](#as-aud-011) |
| `AWP-04-08` | Test purchases and goods received around year-end for cut-off. | [AS-AUD-011-AC08](#as-aud-011) |

### A.6 Source section 5 — Revenue / Sales

**Source:** S1, section 5, parsed page(s) 2.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-05-01` | Obtain sales listing and reconcile total revenue to GL/TB. | [AS-AUD-012-AC01](#as-aud-012) |
| `AWP-05-02` | Perform analytical review of monthly and annual sales. | [AS-AUD-012-AC02](#as-aud-012) |
| `AWP-05-03` | Select sales samples based on value and risk. | [AS-AUD-012-AC03](#as-aud-012) |
| `AWP-05-04` | Check invoices to customer orders/delivery documents. | [AS-AUD-012-AC04](#as-aud-012) |
| `AWP-05-05` | Verify quantity, price, calculation and accounting entry. | [AS-AUD-012-AC05](#as-aud-012) |
| `AWP-05-06` | Check selected subsequent receipts where relevant. | [AS-AUD-012-AC06](#as-aud-012) |
| `AWP-05-07` | Review significant credit notes after year-end. | [AS-AUD-012-AC07](#as-aud-012) |
| `AWP-05-08` | Perform revenue cut-off testing before and after year-end. | [AS-AUD-012-AC08](#as-aud-012) |

### A.7 Source section 6 — Purchases & Trade Payables

**Source:** S1, section 6, parsed page(s) 2–3.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-06-01` | Obtain supplier ageing and reconcile it to the GL/TB. | [AS-AUD-013-AC01](#as-aud-013) |
| `AWP-06-02` | Review significant, old and unusual supplier balances. | [AS-AUD-013-AC02](#as-aud-013) |
| `AWP-06-03` | Obtain supplier confirmations and investigate differences. | [AS-AUD-013-AC03](#as-aud-013) |
| `AWP-06-04` | Compare supplier statements with the company's payable ledger. | [AS-AUD-013-AC04](#as-aud-013) |
| `AWP-06-05` | Test selected purchases to invoices, GRNs and purchase orders. | [AS-AUD-013-AC05](#as-aud-013) |
| `AWP-06-06` | Check subsequent payments to identify outstanding liabilities. | [AS-AUD-013-AC06](#as-aud-013) |
| `AWP-06-07` | Perform search for unrecorded liabilities. | [AS-AUD-013-AC07](#as-aud-013) |
| `AWP-06-08` | Test purchase and payable cut-off around year-end. | [AS-AUD-013-AC08](#as-aud-013) |

### A.8 Source section 7 — Fixed Assets

**Source:** S1, section 7, parsed page(s) 3.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-07-01` | Obtain the fixed asset register and reconcile it to the GL. | [AS-AUD-014-AC01](#as-aud-014) |
| `AWP-07-02` | Agree opening balances with prior-year audited balances. | [AS-AUD-014-AC02](#as-aud-014) |
| `AWP-07-03` | Test additions to invoices, payment records and approvals. | [AS-AUD-014-AC03](#as-aud-014) |
| `AWP-07-04` | Determine whether expenditure is correctly capitalized. | [AS-AUD-014-AC04](#as-aud-014) |
| `AWP-07-05` | Physically verify significant additions/assets where appropriate. | [AS-AUD-014-AC05](#as-aud-014) |
| `AWP-07-06` | Test disposals to disposal documents and sale proceeds. | [AS-AUD-014-AC06](#as-aud-014) |
| `AWP-07-07` | Recalculate depreciation for selected assets. | [AS-AUD-014-AC07](#as-aud-014) |
| `AWP-07-08` | Check useful lives and depreciation method. | [AS-AUD-014-AC08](#as-aud-014) |
| `AWP-07-09` | Review assets for impairment, damage or obsolescence. | [AS-AUD-014-AC09](#as-aud-014) |

### A.9 Source section 8 — Expenses

**Source:** S1, section 8, parsed page(s) 3.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-08-01` | Obtain detailed expense listing and reconcile to GL/TB. | [AS-AUD-015-AC01](#as-aud-015) |
| `AWP-08-02` | Perform analytical review against prior year and budget where available. | [AS-AUD-015-AC02](#as-aud-015) |
| `AWP-08-03` | Identify significant and unusual expense movements. | [AS-AUD-015-AC03](#as-aud-015) |
| `AWP-08-04` | Select samples based on value and risk. | [AS-AUD-015-AC04](#as-aud-015) |
| `AWP-08-05` | Check invoices and supporting documentation. | [AS-AUD-015-AC05](#as-aud-015) |
| `AWP-08-06` | Check management approval and payment evidence. | [AS-AUD-015-AC06](#as-aud-015) |
| `AWP-08-07` | Verify correct accounting classification. | [AS-AUD-015-AC07](#as-aud-015) |
| `AWP-08-08` | Check whether any capital expenditure has been incorrectly expensed. | [AS-AUD-015-AC08](#as-aud-015) |
| `AWP-08-09` | Perform expense cut-off testing. | [AS-AUD-015-AC09](#as-aud-015) |

### A.10 Source section 9 — Payroll

**Source:** S1, section 9, parsed page(s) 3–4.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-09-01` | Obtain annual/monthly payroll summary and reconcile to GL. | [AS-AUD-016-AC01](#as-aud-016) |
| `AWP-09-02` | Select employees for detailed testing. | [AS-AUD-016-AC02](#as-aud-016) |
| `AWP-09-03` | Check employment contracts and salary details. | [AS-AUD-016-AC03](#as-aud-016) |
| `AWP-09-04` | Recalculate gross salary, allowances and deductions. | [AS-AUD-016-AC04](#as-aud-016) |
| `AWP-09-05` | Agree selected salary payments to bank statements. | [AS-AUD-016-AC05](#as-aud-016) |
| `AWP-09-06` | Test new employees and supporting employment documents. | [AS-AUD-016-AC06](#as-aud-016) |
| `AWP-09-07` | Check terminated employees and final payments. | [AS-AUD-016-AC07](#as-aud-016) |
| `AWP-09-08` | Review unusual changes in payroll or employee numbers. | [AS-AUD-016-AC08](#as-aud-016) |

### A.11 Source section 10 — Loans & Borrowings

**Source:** S1, section 10, parsed page(s) 4.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-10-01` | Obtain loan/borrowing schedule and agree it to GL. | [AS-AUD-017-AC01](#as-aud-017) |
| `AWP-10-02` | Agree opening balances to prior-year financial statements. | [AS-AUD-017-AC02](#as-aud-017) |
| `AWP-10-03` | Obtain bank/financier confirmation. | [AS-AUD-017-AC03](#as-aud-017) |
| `AWP-10-04` | Check new loans against agreements and bank receipts. | [AS-AUD-017-AC04](#as-aud-017) |
| `AWP-10-05` | Check repayments against bank statements. | [AS-AUD-017-AC05](#as-aud-017) |
| `AWP-10-06` | Recalculate interest expense and accrued interest. | [AS-AUD-017-AC06](#as-aud-017) |
| `AWP-10-07` | Check current and non-current classification. | [AS-AUD-017-AC07](#as-aud-017) |
| `AWP-10-08` | Review loan terms, security and covenant requirements where applicable. | [AS-AUD-017-AC08](#as-aud-017) |
| `AWP-10-09` | Check subsequent repayments. | [AS-AUD-017-AC09](#as-aud-017) |

### A.12 Source section 11 — Equity / Share Capital

**Source:** S1, section 11, parsed page(s) 4.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-11-01` | Obtain share capital and equity movement schedule. | [AS-AUD-018-AC01](#as-aud-018) |
| `AWP-11-02` | Agree opening balances with prior-year audited FS. | [AS-AUD-018-AC02](#as-aud-018) |
| `AWP-11-03` | Agree share capital to company/CR records. | [AS-AUD-018-AC03](#as-aud-018) |
| `AWP-11-04` | Check additions, transfers or changes in shareholding. | [AS-AUD-018-AC04](#as-aud-018) |
| `AWP-11-05` | Check dividend declarations and payments. | [AS-AUD-018-AC05](#as-aud-018) |
| `AWP-11-06` | Reconcile retained earnings movement with profit/loss. | [AS-AUD-018-AC06](#as-aud-018) |
| `AWP-11-07` | Agree closing equity balances to financial statements. | [AS-AUD-018-AC07](#as-aud-018) |

### A.13 Source section 12 — Related Parties

**Source:** S1, section 12, parsed page(s) 4–5.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-12-01` | Obtain management's related-party listing. | [AS-AUD-019-AC01](#as-aud-019) |
| `AWP-12-02` | Check directors, shareholders and key management records. | [AS-AUD-019-AC02](#as-aud-019) |
| `AWP-12-03` | Review related-party transactions during the year. | [AS-AUD-019-AC03](#as-aud-019) |
| `AWP-12-04` | Review significant related-party balances. | [AS-AUD-019-AC04](#as-aud-019) |
| `AWP-12-05` | Confirm significant balances where appropriate. | [AS-AUD-019-AC05](#as-aud-019) |
| `AWP-12-06` | Check whether transactions are properly recorded. | [AS-AUD-019-AC06](#as-aud-019) |
| `AWP-12-07` | Verify required related-party disclosures in the financial statements. | [AS-AUD-019-AC07](#as-aud-019) |

### A.14 Source section 13 — Tax & Statutory Liabilities

**Source:** S1, section 13, parsed page(s) 5.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-13-01` | Obtain tax returns and tax computations. | [AS-AUD-020-AC01](#as-aud-020) |
| `AWP-13-02` | Reconcile tax balances with the GL/TB. | [AS-AUD-020-AC02](#as-aud-020) |
| `AWP-13-03` | Check tax payments against bank statements. | [AS-AUD-020-AC03](#as-aud-020) |
| `AWP-13-04` | Review outstanding tax liabilities and penalties. | [AS-AUD-020-AC04](#as-aud-020) |
| `AWP-13-05` | Review correspondence with tax authorities. | [AS-AUD-020-AC05](#as-aud-020) |
| `AWP-13-06` | Check tax provisions and current-year tax expense. | [AS-AUD-020-AC06](#as-aud-020) |
| `AWP-13-07` | Check relevant tax disclosures in the financial statements. | [AS-AUD-020-AC07](#as-aud-020) |

### A.15 Source section 14 — Journal Entries & Fraud

**Source:** S1, section 14, parsed page(s) 5.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-14-01` | Obtain journal entry listing for the audit period. | [AS-AUD-021-AC01](#as-aud-021) |
| `AWP-14-02` | Identify unusual, manual and high-value journal entries. | [AS-AUD-021-AC02](#as-aud-021) |
| `AWP-14-03` | Focus on journals posted close to year-end. | [AS-AUD-021-AC03](#as-aud-021) |
| `AWP-14-04` | Select samples based on risk. | [AS-AUD-021-AC04](#as-aud-021) |
| `AWP-14-05` | Check supporting documents and authorization. | [AS-AUD-021-AC05](#as-aud-021) |
| `AWP-14-06` | Review unusual journals affecting revenue, expenses or reserves. | [AS-AUD-021-AC06](#as-aud-021) |
| `AWP-14-07` | Check reversed or unusual post-year-end journals. | [AS-AUD-021-AC07](#as-aud-021) |
| `AWP-14-08` | Document any fraud indicators or management override concerns. | [AS-AUD-021-AC08](#as-aud-021) |

### A.16 Source section 15 — Analytical Review

**Source:** S1, section 15, parsed page(s) 5–6.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-15-01` | Compare current-year results with prior year. | [AS-AUD-022-AC01](#as-aud-022) |
| `AWP-15-02` | Analyse monthly revenue and expense trends. | [AS-AUD-022-AC02](#as-aud-022) |
| `AWP-15-03` | Compare gross profit and net profit margins. | [AS-AUD-022-AC03](#as-aud-022) |
| `AWP-15-04` | Analyse significant movements in major accounts. | [AS-AUD-022-AC04](#as-aud-022) |
| `AWP-15-05` | Calculate relevant ratios and key performance indicators. | [AS-AUD-022-AC05](#as-aud-022) |
| `AWP-15-06` | Review receivable, payable and inventory days where applicable. | [AS-AUD-022-AC06](#as-aud-022) |
| `AWP-15-07` | Investigate significant or unexpected fluctuations. | [AS-AUD-022-AC07](#as-aud-022) |
| `AWP-15-08` | Obtain management explanations and supporting evidence. | [AS-AUD-022-AC08](#as-aud-022) |

### A.17 Source section 16 — Going Concern

**Source:** S1, section 16, parsed page(s) 6.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-16-01` | Obtain management's going-concern assessment. | [AS-AUD-023-AC01](#as-aud-023) |
| `AWP-16-02` | Review current financial position and working capital. | [AS-AUD-023-AC02](#as-aud-023) |
| `AWP-16-03` | Review cash flow forecasts. | [AS-AUD-023-AC03](#as-aud-023) |
| `AWP-16-04` | Check expected cash inflows and major payments. | [AS-AUD-023-AC04](#as-aud-023) |
| `AWP-16-05` | Review loan repayments and financing facilities. | [AS-AUD-023-AC05](#as-aud-023) |
| `AWP-16-06` | Review losses, negative cash flows and overdue liabilities. | [AS-AUD-023-AC06](#as-aud-023) |
| `AWP-16-07` | Assess significant assumptions used in forecasts. | [AS-AUD-023-AC07](#as-aud-023) |
| `AWP-16-08` | Consider subsequent trading performance. | [AS-AUD-023-AC08](#as-aud-023) |
| `AWP-16-09` | Document the auditor's conclusion. | [AS-AUD-023-AC09](#as-aud-023) |

### A.18 Source section 17 — Subsequent Events

**Source:** S1, section 17, parsed page(s) 6–7.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-17-01` | Review post-year-end bank statements and transactions. | [AS-AUD-024-AC01](#as-aud-024) |
| `AWP-17-02` | Review significant sales, purchases and payments after year-end. | [AS-AUD-024-AC02](#as-aud-024) |
| `AWP-17-03` | Review board/management meeting minutes. | [AS-AUD-024-AC03](#as-aud-024) |
| `AWP-17-04` | Check new loans, investments or major asset purchases. | [AS-AUD-024-AC04](#as-aud-024) |
| `AWP-17-05` | Review litigation and significant legal developments. | [AS-AUD-024-AC05](#as-aud-024) |
| `AWP-17-06` | Discuss significant events with management. | [AS-AUD-024-AC06](#as-aud-024) |
| `AWP-17-07` | Determine whether events require adjustment or disclosure. | [AS-AUD-024-AC07](#as-aud-024) |
| `AWP-17-08` | Ensure relevant events are reflected in the financial statements. | [AS-AUD-024-AC08](#as-aud-024) |

### A.19 Source section 18 — Financial Statements & Disclosures

**Source:** S1, section 18, parsed page(s) 7.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-18-01` | Agree final financial statements to the audited Trial Balance. | [AS-AUD-025-AC01](#as-aud-025) |
| `AWP-18-02` | Check statement of financial position and profit/loss. | [AS-AUD-025-AC02](#as-aud-025) |
| `AWP-18-03` | Check cash flow statement and statement of changes in equity. | [AS-AUD-025-AC03](#as-aud-025) |
| `AWP-18-04` | Review accounting policies and significant estimates. | [AS-AUD-025-AC04](#as-aud-025) |
| `AWP-18-05` | Check comparative figures with prior-year audited FS. | [AS-AUD-025-AC05](#as-aud-025) |
| `AWP-18-06` | Check note disclosures and supporting schedules. | [AS-AUD-025-AC06](#as-aud-025) |
| `AWP-18-07` | Check related-party, tax and going-concern disclosures. | [AS-AUD-025-AC07](#as-aud-025) |
| `AWP-18-08` | Check subsequent-event disclosures. | [AS-AUD-025-AC08](#as-aud-025) |
| `AWP-18-09` | Perform final financial statement consistency and mathematical review. | [AS-AUD-025-AC09](#as-aud-025) |

### A.20 Source section 19 — Audit Differences & Adjustments

**Source:** S1, section 19, parsed page(s) 7.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-19-01` | Record all identified audit differences. | [AS-AUD-026-AC01](#as-aud-026) |
| `AWP-19-02` | Obtain management's response to proposed adjustments. | [AS-AUD-026-AC02](#as-aud-026) |
| `AWP-19-03` | Recalculate the financial statement impact of each difference. | [AS-AUD-026-AC03](#as-aud-026) |
| `AWP-19-04` | Update the adjusted and unadjusted misstatement schedule. | [AS-AUD-026-AC04](#as-aud-026) |
| `AWP-19-05` | Compare total unadjusted differences with materiality. | [AS-AUD-026-AC05](#as-aud-026) |
| `AWP-19-06` | Assess whether remaining differences affect the audit conclusion. | [AS-AUD-026-AC06](#as-aud-026) |
| `AWP-19-07` | Ensure all agreed adjustments are posted and reflected in the final TB. | [AS-AUD-026-AC07](#as-aud-026) |

### A.21 Source section 20 — Final Completion & Audit Report

**Source:** S1, section 20, parsed page(s) 7–8.

| Source ID | Exact source procedure | Primary acceptance criterion |
|---|---|---|
| `AWP-20-01` | Ensure all audit sections are completed and cross-referenced. | [AS-AUD-027-AC01](#as-aud-027) |
| `AWP-20-02` | Ensure all review points have been cleared. | [AS-AUD-027-AC02](#as-aud-027) |
| `AWP-20-03` | Confirm all significant risks have been addressed. | [AS-AUD-027-AC03](#as-aud-027) |
| `AWP-20-04` | Review audit differences and final materiality assessment. | [AS-AUD-027-AC04](#as-aud-027) |
| `AWP-20-05` | Complete going-concern and subsequent-event procedures. | [AS-AUD-027-AC05](#as-aud-027) |
| `AWP-20-06` | Complete financial statement disclosure checklist. | [AS-AUD-027-AC06](#as-aud-027) |
| `AWP-20-07` | Obtain signed management representation letter. | [AS-AUD-027-AC07](#as-aud-027) |
| `AWP-20-08` | Perform final analytical review. | [AS-AUD-027-AC08](#as-aud-027) |
| `AWP-20-09` | Complete senior/manager/partner review. | [AS-AUD-027-AC09](#as-aud-027) |
| `AWP-20-10` | Finalize the auditor's report and signed financial statements. | [AS-AUD-027-AC10](#as-aud-027) |

<a id="references"></a>
## References

**S1.** User-supplied `Audit working process - Audit Tool New.docx`, all 20 sections, source SHA-256 recorded in Section 2.1. Appendix A preserves the complete procedure-level basis; the source file is not modified by this task.

The following are commit-pinned repository references inspected to ground reuse and governance. They are evidence of what the files specify or contain, **not** evidence that their described runtime capabilities were independently executed for this document.

- **R1 — Existing agent instructions:** [AGENTS.md][R1].
- **R2 — Existing implementation specification:** [Root .NET specification][R2], particularly introduction, scope and Sections 16–24.
- **R3 — Existing audit domain:** [Audit.cs][R3], including materiality, risks, procedures, populations, workpaper drafts/submissions and findings.
- **R4 — Existing sampling, confirmation, workpaper and review contract:** [Specification Sections 20–22][R4].
- **R5 — Existing findings, professional completion and release contract:** [Specification Sections 23–24][R5].
- **R6 — Existing pure financial-package calculations:** [FinancialStatementCalculator.cs][R6].

No outside accounting/tax/legal standard was researched or incorporated as an additional source. Professional policy choices not specified by S1 must be supplied through approved methodology as listed in Section 7.

[R1]: https://github.com/nirzaf/AuditSphere/blob/ab7c4fe001a1b342880aae6bbca1bbc157c9716a/AGENTS.md
[R2]: https://github.com/nirzaf/AuditSphere/blob/ab7c4fe001a1b342880aae6bbca1bbc157c9716a/docs/SPECIFICATION.md
[R3]: https://github.com/nirzaf/AuditSphere/blob/ab7c4fe001a1b342880aae6bbca1bbc157c9716a/src/AuditSphereOps.Domain/Audit/Audit.cs
[R4]: https://github.com/nirzaf/AuditSphere/blob/ab7c4fe001a1b342880aae6bbca1bbc157c9716a/docs/SPECIFICATION.md#s20
[R5]: https://github.com/nirzaf/AuditSphere/blob/ab7c4fe001a1b342880aae6bbca1bbc157c9716a/docs/SPECIFICATION.md#s23
[R6]: https://github.com/nirzaf/AuditSphere/blob/ab7c4fe001a1b342880aae6bbca1bbc157c9716a/src/AuditSphereOps.Application/Accounting/FinancialStatementCalculator.cs

---

**Document integrity check:** 28 unique story IDs; 165 unique source-procedure IDs; one primary acceptance owner per source procedure; all source procedures mapped; dependency sequence validated. This is a document-structure check, not an application test result.
