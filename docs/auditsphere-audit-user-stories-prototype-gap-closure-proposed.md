# AuditSphere — Prototype Parity and Gap-Closure User Stories

**Document ID:** AS-PAR-SPEC-001
**Version:** 1.0 — proposed implementation backlog
**Prepared:** 23 September 2026, Asia/Qatar
**Scope:** Requirements handoff; implementation status is tracked separately in the execution ledger.
**Repository location:** `docs/auditsphere-audit-user-stories-prototype-gap-closure-proposed.md`.

> This is an implementation and verification contract, not an acceptance certificate. All newly specified implementation is proposed. A repository-recorded pass, an inspected assertion, a screenshot, a successful CI build, a synthetic browser interaction and an observed live-provider outcome are different evidence classes. None is silently substituted for another.

## Contents

- [1. Baseline, source precedence and limitations](#baseline)
- [2. Shared implementation and evidence contracts](#contracts)
- [3. Gap inventory and dependency index](#gap-index)
- [4. Detailed Agile stories](#stories)
- [5. Native prototype requirement traceability](#native-matrix)
- [6. Exact prototype acceptance-test results](#acceptance-matrix)
- [7. Exact permission conditions and persona routes](#permissions)
- [8. Workpaper workspace and audit-area coverage](#workspace)
- [9. Unnumbered specification tables and service coverage](#supplemental)
- [10. Existing configuration and future composition changes](#configuration)
- [11. Validation recipes and completion evidence](#validation)
- [12. External prerequisites and release gates](#prerequisites)
- [13. Preservation and documentation-only delivery checks](#preservation)
- [14. Immutable source and existing-test register](#sources)

<a id="baseline"></a>
## 1. Baseline, source precedence and limitations

| Repository | Inspected branch / immutable commit | Evidence basis |
| --- | --- | --- |
| nirzaf/AuditSphere | master / `f64a1f4ef05bf0372cd60af5878031601a9dad61` | Live GitHub reads of pinned code/specification/configuration/test sources; current-head CI metadata inspected. |
| nirzaf/auditsphere-visual-prototype | main / `3f30d348289d6d94dd49cb1d976e23018183eec9` | Canonical source.json, roles.json, permissions.json, workpaper test source and compatibility runtime; newest workpaper addition included. |

The controlling implementation architecture is **.NET 10, ASP.NET Core/Blazor Interactive Server, EF Core and PostgreSQL 18.6**, with existing xUnit tests. The pinned repository uses SDK `10.0.300`, EF Core `10.0.12` and Npgsql EF provider `10.0.0` in its recorded baseline. These are repository facts, not a recommendation to upgrade dependencies during parity work. Keep current compatible approved versions; any upgrade is a separately reviewed change. [SPEC](#e-spec), [STATUS](#e-status), [CI](#e-ci)

The prototype is a **synthetic browser visualization**, not a backend authentication, authorization, storage, signing, ledger or retention reference implementation. Its React/Vite hosting choice does not authorize a React replacement for the Blazor application. Production should implement the business interaction and control, not copy demo IDs, names, pre-cleared rows, global JavaScript state or localStorage business authority. [P-README](#e-p-readme), [P-ROLES](#e-p-roles), [P-RUNTIME](#e-p-runtime)

Repository evidence records **246/246 tests and 91 migrations** at `dc9cb0b`, with production disabled. The current inspected head has a successful [CI run 35779410156](https://github.com/nirzaf/AuditSphere/actions/runs/35779410156). This document did not run those tests, launch either application, perform tenant operations or inspect the user's local untracked evidence. The current README’s 220-test/81-migration/PDF-pending statements lag newer execution evidence: preserve history and do not reclassify the implemented PDF/Office export or advanced-method local work as missing. [STATUS](#e-status), [PENDING](#e-pending)

The review combines direct code/assertion inspection and repository-recorded verification. It is not a proof that every method in every existing test file was inspected. Where only a file or service seam is confirmed, its link is explicitly a **reuse/verification candidate**, not a passing assertion. “Not established” means the complete criterion was not proved from the inspected sources; it does not assert that an uninspected helper cannot implement a portion of it. Future issues must recheck the current head before editing.

**Inventory in this document:** 62 Agile stories with 311 Given/When/Then criteria; 259 native prototype requirement identifiers (including all 46 acceptance scenarios and 30 annual-question identifiers); 44 permission contracts; 14 personas; 18 service variants; and 126 immutable source/test references. The supplemental tables retain the 27 source sections, six workspace tabs, 20 workpaper scenario groups and 20 audit-area families. These are inventory counts, not implementation-completion counts.

### 1.1 Source precedence and explicit conflict dispositions

| Conflict / ambiguity | Required disposition |
| --- | --- |
| Legacy Perfex/PHP/MySQL/GreenTech/project/hook wording in P:ARC, P:TR and P:AUT | Use the native .NET modular monolith and existing bounded Practice services under S:§1.3/§41. Preserve source provenance and business controls, not legacy runtime coupling. No commercial ERP package is required just to satisfy this backlog. |
| Prototype P:AT-01–46 versus controlling S:AT/ET/VT/NT scenarios | Namespace P: for prototype and S: for docs/auditsphere-requirements-system-specification-current.md. Same numbers are not equivalent tests; retain both catalogues and actual scenario mappings. |
| Prototype Q01–Q40 and grouped Y01–Y30 versus 62 CE / 30 RV originals | Retain original CE/RV IDs and wording. AS-PAR-007/011 require owner-approved semantic mappings. Q and CE numbering is not a conversion rule. Source Y wording is only supplied in six grouped ranges. |
| Prototype direct N/A toggle versus its own AUD-02/AT-46 approval rule | The written requirement controls: require a reason and authorized independent applicability disposition before excluding a procedure from completion. Do not copy the unsafe toggle semantics. |
| Four seeded workpapers versus 165 controlled audit procedures | The four workpapers are a UI fixture. Retain the existing AWP catalogue and all original audit stories; 165 adopted rows do not prove 165 procedures executed/reviewed. |
| Prototype example, prototype UI seed and S:Appendix D monetary fixtures | Use independent named fixtures; never mix their amounts or substitute one fixture’s expected totals for another. |
| Eight assignable role codes versus fourteen personas and extra role strings in commands | Define approved capability/alias mappings and negative tests. Do not invent staff grants or automatically upgrade Administrator/ClientUser permissions. |
| Native operating-client ledger versus existing bounded firm ledger | Initial external-books/reporting-layer workflows remain separate. Managed client bookkeeping requires an approved scope extension, not renaming the firm ledger. |
| Local providers versus real Microsoft and mail providers | Web startup refuses unapproved general external effects; the Worker has a special Acceptance/mail composition with existing adapters. A successful local or mail-only acceptance is not general production composition. |
| Service catalogue versus first enabled production profile | Preserve all services in traceability; specialist extensions remain blocked until separately approved. Disabled services are not quietly dropped from the requirements or falsely offered to users. |
| Professional standards/thresholds and source external citations | This is source-based software planning, not a new legal/accounting standards verification. Qualified owners supply applicable effective policies; no external source citation certifies compliance. |
| Prior AS-AUD/accounting/M365 backlogs and current execution pointers | This document adds AS-PAR IDs and links to existing work. No renumbering, replacement, wholesale “done” changes, history cleanup or pointer rewrite is authorized. |

### 1.2 Coverage status and evidence notation

| Status | Meaning in this document |
| --- | --- |
| covered | The bounded behavior is supported by inspected code/assertions or explicit repository-recorded local evidence. It is not a full production, every-route or every-service acceptance claim. Preserve and reverify rather than rebuild. |
| partial | A relevant persisted model/service/UI/test exists, but at least one required field, transition, authority check, interaction or acceptance proof remains unresolved. |
| proposed | A complete corresponding capability or verification lane was not established; the story defines future work without claiming implementation. |
| blocked | Completion or activation requires separate scope/methodology/runner/tenant/provider/custody/owner authorization or unavailable external evidence. A local sub-slice may be implemented only in an approved lane. |

Evidence classes: **C** = code behavior inspected; **A** = an actual test assertion inspected; **R** = repository-recorded execution only; **F** = relevant existing file/seam confirmed but its complete assertions were not reviewed; **N** = new verification proposed; **X** = external observation/authorization required. The source register marks these boundaries in prose. A test filename alone is F, not A. Requirement status and latest execution result must remain separate fields.

<a id="contracts"></a>
## 2. Shared implementation and evidence contracts

Every story includes the following acceptance constraints by reference. They are not permission to refactor unrelated code.

**C1 — Authority and scope.** Resolve the actor from the trusted session; re-read active identity, session epoch, role/capability and firm/client/engagement/classification scope. Filter before loading sensitive data or computing counts. Authorize each command inside its transaction and each byte endpoint before issuing access. UI route visibility and a user-supplied approver ID are never authority.

**C2 — Typed field contract.** Every changed record has a stable ID, applicable existing scope IDs, lifecycle, current revision, attributable actors/timestamps and required provenance. Money/percentages use approved decimal precision, UTC is retained with configured display timezone, leading-zero identifiers stay strings, and absent/zero/unknown/N/A remain distinct. Drafts may be incomplete; the specified transition checks all M/C/O fields and conditional evidence. Original data and normalized derivatives remain separately traceable. Do not add a generic JSON-only domain model to avoid typed invariants.

**C3 — Version and independence.** Freeze reviewed content, inputs, document versions and hashes. Approval decisions include exact target/version/stage, actor, role-at-decision, authority/delegation, rationale, time and evidence. Self-review checks use person identity across multiple roles, not the current persona label. Preserve prior approvals and invalidate only affected current dependencies. A read-only legacy projection with an unproven Cleared/Obtained flag cannot satisfy a new gate without real supporting evidence.

**C4 — Atomicity and retries.** Use existing PostgreSQL transaction/locking/idempotency conventions and append-only evidence. Local state/history/outbox commits atomically; external actions occur through existing durable workers. Preserve scoped request identity, generation/revision fences and uncertain-after-effect outcomes. A retry returns the prior committed result or a conflict; it must not invent exactly-once external delivery or roll back a financial transaction that already committed.

**C5 — Minimal architecture-preserving change.** Reuse current Domain/Application/Infrastructure/Web/Worker seams. Domain remains persistence-agnostic; Razor invokes guarded services rather than updating approved entities directly. An introduced class name in a story is **proposed**, not a claim that it exists. Before adding a model/service/configuration key, inspect for an equivalent; prefer a narrow extension. No additional ERP, framework, message broker, microservice, in-page Office editor, autonomous professional AI, bespoke malware scanner or ClamAV dependency.

**C6 — Migration and historical preservation.** New persisted fields require reviewed additive EF migrations, empty/prior-schema tests, model-drift check, index/FK/uniqueness validation and evidence-preserving downgrade behavior. Ambiguous historical ownership or approvals stay quarantined/unverified; do not manufacture values, execute blanket backfills, rewrite prior migrations or remove append-only triggers. Use a narrowly scoped restore regression when new evidence relationships affect restore manifests.

**C7 — Source/fixture integrity.** Existing source wording, AWP/CE/RV IDs, schemas and approved accounting golden fixtures remain immutable baselines. Prototype samples are synthetic scenarios only. Read the complete controlling source paragraphs and field tables for each linked P: identifier; the compact matrix labels do not narrow those requirements.

**C8 — Completion evidence.** Each criterion needs a named positive test, appropriate rejection/retry/concurrency test, actual result and exact commit. Use existing xUnit tests first; add minimal missing assertions. Browser acceptance uses the actual Blazor UI and persisted database through the proposed approved Playwright lane. Provider-contract/local simulation tests never substitute for TenantIntegration, signing, records, production restore or professional decisions.

**C9 — Configuration, CI and documentation scope.** Unless a story explicitly identifies a later authorized change, reuse existing infrastructure/adapters, keep CI unchanged, and add only the required source-to-test and operator documentation in its future PR. No story permits production-setting edits, secret generation, live resource creation, deployment or irreversible operations as part of this documentation task.

**C10 — Dependencies.** Linked predecessors are implementation/control dependencies, not approval to reorder the existing execution plan. Service-specific gates apply only when required by an approved profile; for example, accounting-only work must not fabricate an EQR merely to traverse an audit story. Conditional N/A must itself be authorized and evidenced. A separate implementation lane requires the owner decision defined by AS-PAR-001.

### 2.1 Existing implementation to preserve

Preserve the existing scoped accounting profiles/periods/books, TB/GL normalization and resumable intake, hierarchy/mapping controls, journal-source reflection and difference-impact lineage, source-bound reconciliations, ECL/inventory/specialist schedules, deterministic financial packages and XLSX/DOCX/**PDFsharp/MigraDoc PDF** exports. Preserve approved scoped advanced-method fixtures and local browser acceptance, without turning them into blanket production activation. Preserve server-backed workpaper drafts, immutable submissions, current scope guards, outbox leases, recovery quarantine, records manifest lineage and M365 acceptance-created waiting workspace intents. [PENDING](#e-pending), [STATUS](#e-status), [WP-UI](#e-wp-ui), [PROGRAM](#e-program)

The assertion-inspected `AuditProgramWorkflowTests.ControlledCatalog_UsesScopedAppendOnlyWorkflow` checks 165-item publish/adopt, repeat identity, selected result submission/review and stale generation. It does **not** execute all 165 procedures or the six-tab browser. `RouteCatalogTests` includes database client-contact, time segregation and fiscal-period-close tests, not Playwright route execution. [T-AuditProgramWorkflowTests](#e-t-auditprogramworkflowtests), [T-RouteCatalogTests](#e-t-routecatalogtests)

<a id="gap-index"></a>
## 3. Gap inventory and dependency index

Each row owns a concrete delta or verification gap. The detailed story links exact unmet prototype acceptance wording in section 6 and the controlling specification/source files. Owners are roles to be assigned by the project—not invented named approvers. Priorities are proposed ordering within an authorized lane, not changes to existing WBS acceptance gates.

| Story / gap | Baseline status | Priority | Controlling .NET sections | Dependencies |
| --- | --- | --- | --- | --- |
| [AS-PAR-001](#as-par-001) — Approve an additive parity baseline and resolve conflicting contracts | proposed | P0 | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§2](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s02), [§36](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s36), [§40](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s40), [§46](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s46), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | No story predecessor; existing repository authorization and approved baseline are still required. |
| [AS-PAR-002](#as-par-002) — Enforce scope on reads, counts, downloads and every state-changing path | partial | P0 | [§6](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s06), [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§28](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s28), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-001](#as-par-001) |
| [AS-PAR-003](#as-par-003) — Align assignable roles, delegation and professional authority | partial | P0 | [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-002](#as-par-002) |
| [AS-PAR-004](#as-par-004) — Implement the complete review-point response and clearance lifecycle | partial | P0 | [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003) |
| [AS-PAR-005](#as-par-005) — Deliver persona navigation, actionable queues and contextual help | partial | P1 | [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§28](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s28), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003) |
| [AS-PAR-006](#as-par-006) — Complete inquiry validation, duplicate resolution and discovery | partial | P1 | [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003) |
| [AS-PAR-007](#as-par-007) — Implement factual intake and the Q-to-CE questionnaire crosswalk | partial | P1 | [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003), [AS-PAR-006](#as-par-006) |
| [AS-PAR-008](#as-par-008) — Complete restricted compliance disposition and acceptance routing | partial | P1 | [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-007](#as-par-007) |
| [AS-PAR-009](#as-par-009) — Replace proposal simulations with persisted negotiation and review | partial | P1 | [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-006](#as-par-006), [AS-PAR-008](#as-par-008) |
| [AS-PAR-010](#as-par-010) — Complete engagement terms, activation and client/entity mapping | partial | P1 | [§6](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s06), [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003), [AS-PAR-008](#as-par-008), [AS-PAR-009](#as-par-009) |
| [AS-PAR-011](#as-par-011) — Deliver annual change comparison, continuance and safe roll-forward | partial | P1 | [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-007](#as-par-007), [AS-PAR-008](#as-par-008), [AS-PAR-010](#as-par-010) |
| [AS-PAR-012](#as-par-012) — Complete engagement service plans, team capacity and scoped task handoffs | partial | P1 | [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010) |
| [AS-PAR-013](#as-par-013) — Complete two-stage PBC adequacy review and request ownership | partial | P1 | [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003), [AS-PAR-012](#as-par-012) |
| [AS-PAR-014](#as-par-014) — Make controlled uploads, replacement history and approved template access usable | partial | P1 | [§9](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s09), [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-002](#as-par-002), [AS-PAR-013](#as-par-013) |
| [AS-PAR-015](#as-par-015) — Add client-team nominations, verified signatory powers and deliverable access | partial | P1 | [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-013](#as-par-013) |
| [AS-PAR-016](#as-par-016) — Make client clarifications a structured, version-aware workflow | partial | P1 | [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-004](#as-par-004), [AS-PAR-013](#as-par-013) |
| [AS-PAR-017](#as-par-017) — Complete strategy, materiality approval and announcement documents | partial | P1 | [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§20](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s20), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003), [AS-PAR-012](#as-par-012) |
| [AS-PAR-018](#as-par-018) — Implement the integrated six-tab audit-area workspace | partial | P1 | [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§20](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s20), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-004](#as-par-004), [AS-PAR-014](#as-par-014), [AS-PAR-017](#as-par-017) |
| [AS-PAR-019](#as-par-019) — Bind evidence, workbook versions and clearance to a frozen execution manifest | partial | P1 | [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§12](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s12), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-004](#as-par-004), [AS-PAR-014](#as-par-014) |
| [AS-PAR-020](#as-par-020) — Complete populations, samples and controlled confirmation journeys | partial | P1 | [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§20](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s20), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-017](#as-par-017), [AS-PAR-019](#as-par-019) |
| [AS-PAR-021](#as-par-021) — Complete audit-program applicability, execution and current coverage | partial | P1 | [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§20](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s20), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003), [AS-PAR-004](#as-par-004), [AS-PAR-018](#as-par-018), [AS-PAR-020](#as-par-020) |
| [AS-PAR-022](#as-par-022) — Close accounting-intake and reporting-navigation residuals without rebuilding the engine | partial | P1 | [§16](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s16), [§17](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s17), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-002](#as-par-002), [AS-PAR-005](#as-par-005), [AS-PAR-019](#as-par-019) |
| [AS-PAR-023](#as-par-023) — Complete management adjustment decisions and aggregate misstatement evaluation | partial | P1 | [§17](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s17), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003), [AS-PAR-015](#as-par-015), [AS-PAR-022](#as-par-022) |
| [AS-PAR-024](#as-par-024) — Complete exact-artifact financial review and client-safe presentation | partial | P1 | [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-015](#as-par-015), [AS-PAR-022](#as-par-022), [AS-PAR-023](#as-par-023) |
| [AS-PAR-025](#as-par-025) — Complete manager completion, summary memorandum and human-owned report decisions | partial | P1 | [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-004](#as-par-004), [AS-PAR-017](#as-par-017), [AS-PAR-021](#as-par-021), [AS-PAR-023](#as-par-023), [AS-PAR-024](#as-par-024) |
| [AS-PAR-026](#as-par-026) — Implement independent EQR eligibility, concerns and completion | partial | P1 | [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025) |
| [AS-PAR-027](#as-par-027) — Complete version-bound representations and approved signing preparation | partial | P1 | [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-015](#as-par-015), [AS-PAR-024](#as-par-024), [AS-PAR-025](#as-par-025) |
| [AS-PAR-028](#as-par-028) — Complete release, dispatch, recipient acknowledgement and re-delivery | partial | P1 | [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§12](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s12), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-024](#as-par-024), [AS-PAR-025](#as-par-025), [AS-PAR-026](#as-par-026), [AS-PAR-027](#as-par-027) |
| [AS-PAR-029](#as-par-029) — Complete commercial exceptions, WIP, expenses and collection workflows | partial | P1 | [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-012](#as-par-012) |
| [AS-PAR-030](#as-par-030) — Complete records administration, assembly, holds and controlled disposal | partial | P1 | [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§30](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s30), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-003](#as-par-003), [AS-PAR-019](#as-par-019), [AS-PAR-028](#as-par-028) |
| [AS-PAR-031](#as-par-031) — Implement controlled reissue, suspension, termination and handover | partial | P1 | [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§34](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s34), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-011](#as-par-011), [AS-PAR-028](#as-par-028), [AS-PAR-030](#as-par-030) |
| [AS-PAR-032](#as-par-032) — Complete resumable M365 setup and accepted-client workspace provisioning handoff | partial | P1 | [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§9](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s09), [§10](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s10), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45) | [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010) |
| [AS-PAR-033](#as-par-033) — Compose and verify the selected-resource document provider | blocked | P1 | [§9](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s09), [§10](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s10), [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§12](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s12), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§39](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s39), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45) | [AS-PAR-002](#as-par-002), [AS-PAR-019](#as-par-019), [AS-PAR-032](#as-par-032) |
| [AS-PAR-034](#as-par-034) — Verify live identity, revocation and production actor resolution | blocked | P1 | [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§39](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s39), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45) | [AS-PAR-003](#as-par-003), [AS-PAR-032](#as-par-032) |
| [AS-PAR-035](#as-par-035) — Accept one existing notification provider and complete truthful message outcomes | blocked | P1 | [§12](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s12), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45) | [AS-PAR-013](#as-par-013), [AS-PAR-034](#as-par-034) |
| [AS-PAR-036](#as-par-036) — Complete versioned firm policy, calendars, obligations and recurrence | partial | P1 | [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-001](#as-par-001), [AS-PAR-003](#as-par-003), [AS-PAR-011](#as-par-011), [AS-PAR-012](#as-par-012) |
| [AS-PAR-037](#as-par-037) — Complete financial and operational reporting with consistent measures | partial | P1 | [§16](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s16), [§17](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s17), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44) | [AS-PAR-002](#as-par-002), [AS-PAR-005](#as-par-005), [AS-PAR-022](#as-par-022), [AS-PAR-029](#as-par-029), [AS-PAR-036](#as-par-036) |
| [AS-PAR-038](#as-par-038) — Complete migration attribution, historical evidence and controlled cutover | blocked | P1 | [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§34](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s34), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45), [§46](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s46) | [AS-PAR-001](#as-par-001), [AS-PAR-002](#as-par-002) |
| [AS-PAR-039](#as-par-039) — Prove separate-custody recovery and preserve uncertain external outcomes | blocked | P1 | [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§30](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s30), [§34](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s34), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45) | [AS-PAR-028](#as-par-028), [AS-PAR-030](#as-par-030), [AS-PAR-038](#as-par-038) |
| [AS-PAR-040](#as-par-040) — Add missing CI evidence gates without disturbing current checks | partial | P1 | [§4](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s04), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45), [§46](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s46) | [AS-PAR-001](#as-par-001) |
| [AS-PAR-041](#as-par-041) — Introduce an approved Playwright E2E lane for the Blazor application | proposed | P1 | [§33](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s33), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45), [§46](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s46), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-001](#as-par-001), [AS-PAR-040](#as-par-040) |
| [AS-PAR-042](#as-par-042) — Complete production composition, secret custody, observability and capacity acceptance | blocked | P1 | [§4](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s04), [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§10](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s10), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§30](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s30), [§35](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s35), [§39](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s39), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-033](#as-par-033), [AS-PAR-034](#as-par-034), [AS-PAR-035](#as-par-035), [AS-PAR-039](#as-par-039), [AS-PAR-040](#as-par-040) |
| [AS-PAR-043](#as-par-043) — Control service catalogue versions and explicitly disabled capability profiles | partial | P1 | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§31](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s31), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-001](#as-par-001), [AS-PAR-003](#as-par-003), [AS-PAR-012](#as-par-012) |
| [AS-PAR-044](#as-par-044) — Implement authorized manual filing evidence before optional filing adapters | blocked | P1 | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45) | [AS-PAR-015](#as-par-015), [AS-PAR-028](#as-par-028), [AS-PAR-043](#as-par-043) |
| [AS-PAR-045](#as-par-045) — Perform independent complete-cycle acceptance and adopt the verified release | blocked | P0-release | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§31](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s31), [§33](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s33), [§34](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s34), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45), [§46](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s46), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-011](#as-par-011), [AS-PAR-021](#as-par-021), [AS-PAR-028](#as-par-028), [AS-PAR-030](#as-par-030), [AS-PAR-031](#as-par-031), [AS-PAR-038](#as-par-038), [AS-PAR-039](#as-par-039), [AS-PAR-040](#as-par-040), [AS-PAR-041](#as-par-041), [AS-PAR-042](#as-par-042), [AS-PAR-043](#as-par-043) |
| [AS-PAR-046](#as-par-046) — Approve and implement the Internal audit service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025) |
| [AS-PAR-047](#as-par-047) — Approve and implement the Business valuation service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025) |
| [AS-PAR-048](#as-par-048) — Approve and implement the Feasibility study service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025) |
| [AS-PAR-049](#as-par-049) — Approve and implement the Forensic audit and investigation service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-008](#as-par-008), [AS-PAR-030](#as-par-030) |
| [AS-PAR-050](#as-par-050) — Approve and implement the Financial forecasting and projections service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025) |
| [AS-PAR-051](#as-par-051) — Approve and implement the AML compliance review service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-008](#as-par-008), [AS-PAR-030](#as-par-030) |
| [AS-PAR-052](#as-par-052) — Approve and implement the Management consulting service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025) |
| [AS-PAR-053](#as-par-053) — Approve and implement the Fixed-asset verification and tagging service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025) |
| [AS-PAR-054](#as-par-054) — Approve and implement the Inventory management audit service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025) |
| [AS-PAR-055](#as-par-055) — Approve and implement the ERP implementation and independent review engagement service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025) |
| [AS-PAR-056](#as-par-056) — Approve and implement the Corporate IFRS/IAS training service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025) |
| [AS-PAR-057](#as-par-057) — Approve and implement the Recurring managed bookkeeping service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-022](#as-par-022), [AS-PAR-024](#as-par-024) |
| [AS-PAR-058](#as-par-058) — Approve and implement the Annual accounts and compilation service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-022](#as-par-022), [AS-PAR-024](#as-par-024) |
| [AS-PAR-059](#as-par-059) — Approve and implement the Review engagement service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-022](#as-par-022), [AS-PAR-024](#as-par-024) |
| [AS-PAR-060](#as-par-060) — Approve and implement the Agreed-upon procedures service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025) |
| [AS-PAR-061](#as-par-061) — Approve and implement the Tax compliance service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-022](#as-par-022), [AS-PAR-024](#as-par-024), [AS-PAR-044](#as-par-044) |
| [AS-PAR-062](#as-par-062) — Approve and implement the Payroll administration service profile | blocked | P3-scope | [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47) | [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-022](#as-par-022), [AS-PAR-024](#as-par-024) |

### 3.1 Suggested implementation slices

Start with scope/read/write controls and review-point integrity (001–004), then connect persona navigation and commercial/client intake (005–016). Complete planning/workpaper/fieldwork and current evidence (017–021), followed by financial review and professional completion (022–027). Preserve the existing external acceptance order for live release, records and recovery (028, 030–035, 038–042, 045). Commercial/operations improvements (029, 036–037) and service-profile governance (043–044) can proceed only under their approved dependency lane. Stories 046–062 are **conditional scope extensions**, not automatic first-release commitments.

Before selecting any AS-PAR story, reconcile it with the original AS-AUD/accounting/M365 issue owner. A covered existing behavior gets verification/reuse work only; a newly demonstrated defect gets the smallest correction. Do not implement a second review, accounting, provider or recurrence engine merely because a different document uses a different noun.

<a id="stories"></a>
## 4. Detailed Agile stories

<a id="as-par-001"></a>
### AS-PAR-001 — Approve an additive parity baseline and resolve conflicting contracts

**Story:** As a **product owner and technical lead**, I want to **adopt a traceable delta without changing the architecture, existing plans or acceptance evidence**.

**Baseline:** `proposed` · **Proposed priority:** `P0` · **Acceptance owner:** product owner and technical lead.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§2](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s02), [§36](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s36), [§40](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s40), [§46](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s46), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:AC-01–03; P:TR-01–02; P:ARC-01–05; P:INT-01; P:AUT-01; P:LC-09; P:API-02.

**Related existing source:** [SPEC](#e-spec), [AGENTS](#e-agents), [OLD-AUD](#e-old-aud), [OLD-ACC](#e-old-acc), [OLD-M365](#e-old-m365), [STATUS](#e-status), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** The prototype retains Perfex/PHP/MySQL and GreenTech language, while the controlling repository specification requires native .NET. Prototype P:AT identifiers also differ from S:AT identifiers. A source-level crosswalk and owner disposition are necessary; this document is a proposal, not approval.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-001-AC01 | the two pinned source revisions and existing execution ledger | a parity issue is prepared | record P: and S: requirement namespaces, exact commits, affected existing issue/story IDs and acceptance scope; never equate identifiers by number alone |
| AS-PAR-001-AC02 | a legacy runtime, one-project-per-engagement or /ste/v1 wording conflicts with the .NET baseline | the owner disposes the conflict | retain its business intent using existing .NET seams, record the explicit adaptation, and do not install Perfex, ERPNext, MySQL or a second frontend |
| AS-PAR-001-AC03 | a prior work package is externally blocked | local implementation is proposed out of its original sequence | obtain and record an owner-approved separate lane; do not mark the blocking prerequisite satisfied or silently change its order |
| AS-PAR-001-AC04 | an existing plan, execution pointer or untracked evidence file | this documentation or a later narrow story is delivered | preserve its bytes and history unless a distinct authorized change explicitly requires an update; no historical approval is manufactured |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | No domain or schema change. Proposed traceability metadata belongs in this new document until an implementation issue is authorized. |
| Application services | No application change. Create a requirement-to-existing-story disposition before creating duplicate work. |
| Blazor / user interaction | No UI change. Describe adapted boundaries in future help text, not a legacy-runtime compatibility layer. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Documentation checks: unique identifiers, valid internal links, dependency DAG, all source families inventoried, unchanged existing files.

**Existing test seams:** documentation validation only; no application tests are claimed for this documentation story.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** No story predecessor; existing repository authorization and approved baseline are still required.

**External prerequisites / blocked acceptance:** Product owner/architect adoption is required for disputed scope or a separate implementation lane. This documentation request does not authorize implementation, merge or deployment.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-002"></a>
### AS-PAR-002 — Enforce scope on reads, counts, downloads and every state-changing path

**Story:** As a **security reviewer**, I want to **make client, engagement and classification isolation consistent across the entire application**.

**Baseline:** `partial` · **Proposed priority:** `P0` · **Acceptance owner:** security reviewer.

**Controlling specification:** [§6](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s06), [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§28](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s28), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:WF-01; P:NFR-01–02; P:DATA-01/04; P:API-01/03; P:AT-07/23/24.

**Related existing source:** [AUTH](#e-auth), [ACTOR](#e-actor), [WP-UI](#e-wp-ui), [NOTE-UI](#e-note-ui), [COMPLETE-UI](#e-complete-ui), [LEADS](#e-leads), [INVOICE-UI](#e-invoice-ui), [PBC-CLIENT](#e-pbc-client), [PORTAL](#e-portal).

**Exact gap / evidence limit:** AuthorizationDecision is a reusable guarded boundary, but inspected legacy screens use firm-only reads or direct writes. Potential cross-engagement/read leaks must be demonstrated or refuted by tests; this review does not assert an exploited vulnerability.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-07](#p-at-07):** Entity B screens, search results, totals, APIs and downloads remain inaccessible

> **[P:AT-23](#p-at-23):** Each client's GL, TB, statements, cash flow, ageing and exports contain only its authorized data

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-002-AC01 | a user assigned to engagement A but not sibling B in the same firm | they request B through a route, search, count, export, download or direct command | deny without exposing names, existence-sensitive metadata, balances or bytes; scope before loading or aggregating sensitive records |
| AS-PAR-002-AC02 | an identity is disabled, a grant revoked or its session epoch changes during an open circuit | the next read, draft save, approval or worker handoff occurs | re-read current access, deny stale authority and preserve existing evidence without fallback to a broader projection |
| AS-PAR-002-AC03 | a privileged page displays a denied service result | the page handles that result | do not calculate a fallback balance or display previously loaded restricted workpaper content |
| AS-PAR-002-AC04 | a valid scoped user submits the same authorized operation twice | the command is retried | return a stable result without duplicate mutations, and keep authorization checks inside the command transaction |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse AppUser, RoleGrant, session epochs and existing firm/client/engagement IDs. Add classification-aware relationships only where a demonstrated gap requires them. |
| Application services | Route protected queries through narrowly scoped application query functions; keep business guards inside commands. Remove direct state mutations from Razor event handlers, not merely hide buttons. |
| Blazor / user interaction | Audit the listed pages first, then all financial/review/client routes and byte endpoints; display non-disclosing errors and clear inaccessible data from component state. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Database positive/negative scope matrix plus proposed browser and HTTP tests; assert zero unauthorized rows/bytes and no partial writes. Capture route, actor scope and redacted rejection evidence.

**Existing test seams:** [T-AuthorizationDecisionTests](#e-t-authorizationdecisiontests), [T-AuditScopeIntegrityTests](#e-t-auditscopeintegritytests), [T-Microsoft365AccessTests](#e-t-microsoft365accesstests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-001](#as-par-001)

**External prerequisites / blocked acceptance:** None for isolated local implementation; release acceptance still follows the applicable external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-003"></a>
### AS-PAR-003 — Align assignable roles, delegation and professional authority

**Story:** As a **practice leadership and system administrator**, I want to **assign the prototype responsibilities without technical administration becoming professional approval**.

**Baseline:** `partial` · **Proposed priority:** `P0` · **Acceptance owner:** practice leadership and system administrator.

**Controlling specification:** [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:GOV-01/03/04; P:roles.json; all P:permissions.json; P:NFR-02/05.

**Related existing source:** [ROLES](#e-roles), [AUTH](#e-auth), [PROGRAM](#e-program), [P-ROLES](#e-p-roles), [P-PERM](#e-p-perm).

**Exact gap / evidence limit:** The assignable catalogue has eight codes while command allowlists use additional strings. Administrator appears in professional program/review allowlists. Fourteen personas are not a consistent effective-permission model.

**Exact prototype permission acceptance anchor:** `access.execute` — “Scope verification and approved authority; not self-granting professional power.” ([P-PERM](#e-p-perm)).

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-003-AC01 | a technical administrator has no professional grant | they attempt acceptance, materiality approval, workpaper review, management approval, partner authorization or EQR | deny each operation even with a firm-wide technical grant |
| AS-PAR-003-AC02 | an authorized role assignment is proposed | it is applied | validate immutable identity, active status, role/capability, exact scope, qualification where required and incompatible engagement duties atomically; preserve last-administrator protection |
| AS-PAR-003-AC03 | one person legitimately has compatible multiple roles | they change their workspace persona | change presentation only; effective authority still derives from active scoped grants and does not permit self-review |
| AS-PAR-003-AC04 | a delegate replaces an absent reviewer | the delegation is accepted | store authorizer, scope, reason, start/end and qualification evidence; expire it without rewriting past decisions and reassign open responsibilities through explicit events |
| AS-PAR-003-AC05 | legacy aliases or an unmapped role are migrated | a new role catalogue is adopted | publish an explicit mapping and quarantine ambiguities; do not bulk-promote ClientUser or Administrator to signatory, reviewer or partner |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Extend the existing role/grant model with the smallest capability/delegation and authority-evidence fields needed. Proposed role names require owner approval, not presumed production assignments. |
| Application services | One authoritative role/capability catalogue must feed role administration and command policies. Preserve exact (tid, oid) mapping and revocation/session-epoch behavior. |
| Blazor / user interaction | Render assignable roles, their scope and incompatibilities; identify active persona without exposing the prototype demo role switch as authentication. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Parameterize the 44 permission rows in the matrix; test each positive role, wrong role, wrong scope, same-person reviewer and revoked/expired delegation.

**Existing test seams:** [T-RoleAdministrationTests](#e-t-roleadministrationtests), [T-AuthorizationDecisionTests](#e-t-authorizationdecisiontests), [T-Microsoft365AccessTests](#e-t-microsoft365accesstests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-002](#as-par-002)

**External prerequisites / blocked acceptance:** Quality/practice owner approves professional role/delegation matrix; tenant owner separately authorizes identity fixtures.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-004"></a>
### AS-PAR-004 — Implement the complete review-point response and clearance lifecycle

**Story:** As a **preparer and independent reviewer**, I want to **respond to review matters while only an eligible checker can clear the exact response**.

**Baseline:** `partial` · **Proposed priority:** `P0` · **Acceptance owner:** preparer and independent reviewer.

**Controlling specification:** [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-20; P:GOV-01–04; P:AT-19/28/29/44; P:review.raise/respond/clear.

**Related existing source:** [NOTE-UI](#e-note-ui), [APPROVAL](#e-approval), [PROGRAM](#e-program), [AUDIT-DOM](#e-audit-dom).

**Exact gap / evidence limit:** ReviewPoint.razor sets Cleared directly and saves through EF. It does not expose the required attributable response/review history, independent clearance or current-target checks. Guarded procedure-review code elsewhere does not repair this separate path.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-19](#p-at-19):** Approval rejected; independent authorized reviewer required

> **[P:AT-28](#p-at-28):** Note remains awaiting reviewer clearance; response does not auto-clear

> **[P:AT-44](#p-at-44):** Conflict detected; approvals bind to the version actually reviewed

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-004-AC01 | an eligible reviewer opens a point on a frozen workpaper version | the point is submitted | persist target ID/revision/hash/location, required action, severity, assignee, due date and workpaper/package/engagement blocking scope |
| AS-PAR-004-AC02 | a preparer answers an open point with new support | the response is committed | append response and evidence-version links, move to RESPONDED, preserve the original note and do not clear it |
| AS-PAR-004-AC03 | the responder or original preparer attempts clearance | Clear is invoked directly or through the UI | deny without modifying any projection, decision or downstream gate |
| AS-PAR-004-AC04 | the issuer or formally assigned qualified replacement reviews a current response | they clear or reopen with rationale | append a version-bound disposition; stale sources, open required actions and expired delegation prevent clearance |
| AS-PAR-004-AC05 | a legacy row is marked Cleared but lacks supporting decision history | the new projection is built | preserve that historical value as legacy evidence; require a current verified disposition before it satisfies a new approval gate |
| AS-PAR-004-AC06 | two reviewers or a retry act on the same revision | the command executes | allow one coherent result or a conflict and never lose either submitted narrative |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse ReviewPoint; add only missing append-only ReviewPointResponse and ReviewPointDecision records (proposed names), exact target/evidence bindings and optimistic concurrency. Do not backfill fabricated reviewer stamps. |
| Application services | Proposed guarded Raise/Respond/Clear/Reopen functions in the existing Reviews module; reuse AuthorizationDecision, transaction conventions and approval freshness logic. Keep Cleared as a derived compatibility projection, not authority. |
| Blazor / user interaction | Replace direct EF event-handler writes with commands; provide a response editor, history, current target preview and eligibility-aware actions. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Extend existing review tests or add one focused review-point test file. Test self-clear, stale target, wrong engagement, responder acting under another role, issuer replacement, retry, legacy unproven clearance and browser refresh.

**Existing test seams:** [T-ApprovalTests](#e-t-approvaltests), [T-AuditPlanningTests](#e-t-auditplanningtests), [T-AuditProgramWorkflowTests](#e-t-auditprogramworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003)

**External prerequisites / blocked acceptance:** None for isolated local implementation; release acceptance still follows the applicable external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-005"></a>
### AS-PAR-005 — Deliver persona navigation, actionable queues and contextual help

**Story:** As a **all fourteen prototype personas**, I want to **start from the correct authorized work queue and retain context across handoffs**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** all fourteen prototype personas.

**Controlling specification:** [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§28](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s28), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:UX-01–03; P:OBJ-01/02; P:roles.json routes; P:REP-03.

**Related existing source:** [LAYOUT](#e-layout), [ACC-NAV](#e-acc-nav), [PORTFOLIO](#e-portfolio), [PORTAL](#e-portal), [P-ROLES](#e-p-roles), [P-GUIDE](#e-p-guide).

**Exact gap / evidence limit:** The current shared sidebar is not persona-specific. Accounting context/navigation exists, but there is no equivalent complete set of role homes and cross-workflow queues. RouteCatalogTests is not browser route evidence.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-005-AC01 | each of the fourteen personas has approved scoped grants | they sign in | show only relevant routes and real assigned preparation/review/client/partner/operations queues, with zero-data and no-access states distinguished |
| AS-PAR-005-AC02 | a user follows a client→engagement→period→workpaper→package link | navigation or browser back occurs | retain authorized entity, engagement, book, period, currency, mode and version context; never choose an unrelated default client |
| AS-PAR-005-AC03 | a required gate prevents an action | the action is displayed | state the missing input, responsible owner and permitted next step without revealing restricted compliance details |
| AS-PAR-005-AC04 | a keyboard-only user opens tabs, dialogs, errors and history | they navigate and close them | focus remains usable, tab labels and validation summaries are accessible, and no task is implicitly submitted by navigation |
| AS-PAR-005-AC05 | role-guide, privileges or requirements views are opened | the page renders | explain current effective scope, limitations and stable requirement references; do not expose raw private runtime state or credentials |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Prefer query projections over new dashboard tables; no extra generic workflow engine. |
| Application services | Reuse scoped portfolio/accounting queries and add only missing bounded queue projections. Counts and drill-down use identical predicates. |
| Blazor / user interaction | Compose native Blazor pages and components; keep functional accounting navigation. Replace visual-only demo controls with supported, permission-derived actions. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Proposed Playwright journey for each persona/route allowlist and direct denied URL, empty states, breadcrumbs and keyboard focus. Do not assert pixel parity without separate visual acceptance.

**Existing test seams:** [T-AuthorizationDecisionTests](#e-t-authorizationdecisiontests), [T-RouteCatalogTests](#e-t-routecatalogtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003)

**External prerequisites / blocked acceptance:** None for isolated local implementation; release acceptance still follows the applicable external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-006"></a>
### AS-PAR-006 — Complete inquiry validation, duplicate resolution and discovery

**Story:** As a **relationship owner**, I want to **convert a real inquiry into a reviewed service brief without duplicate clients**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** relationship owner.

**Controlling specification:** [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-01–03; P:INT-01; P:AT-01–03; P:lead.write.

**Related existing source:** [LEADS](#e-leads), [CRM](#e-crm), [CLIENT](#e-client).

**Exact gap / evidence limit:** Leads.razor persists creation/qualification but permits optional contact fields and does not expose the full intake, duplicate-resolution or reviewed discovery journey.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-01](#p-at-01):** Save permitted draft if applicable; reject submission with field-specific guidance

> **[P:AT-02](#p-at-02):** One lead/conversion result; prior result returned without duplicate clients

> **[P:AT-03](#p-at-03):** Updated scoped discovery and acceptance route; original inquiry history preserved

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-006-AC01 | an inquiry draft lacks contact/service details | Save draft or Submit is selected | allow only the permitted incomplete draft; Submit returns field-specific errors for contact name, business identity, valid channel, source, service interest, jurisdiction/unknown and received time |
| AS-PAR-006-AC02 | an authenticated staff entry or approved public form is submitted | the inquiry is accepted | retain the immutable original and normalization/source/notice evidence, assign an owner and response target; public intake is separately rate-limited and collects no sensitive ownership scans |
| AS-PAR-006-AC03 | duplicate registration/contact candidates exist | an authorized screener resolves them | record surviving ID, aliases, field conflicts and rationale; link repeated inquiries or create a new opportunity for an existing client without destructive automatic merging |
| AS-PAR-006-AC04 | discovery identifies changed entities, service, deadline or assurance needs | the manager reviews the versioned brief | record inclusions/exclusions, assumptions, data volumes, books/system, intended users, deliverables, dependencies and fee/resource basis; reroute acceptance rather than reuse incompatible approval |
| AS-PAR-006-AC05 | an identical event or conversion is retried | the service handles the request | return the same outcome and retain rejected/deferred/lost reasons without creating a duplicate client or financial posting |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Extend existing Lead/Opportunity with missing inquiry provenance, screening/discovery revision and approval references; retain existing IDs and conversion lineage. |
| Application services | Extend PracticeCrmService commands, scoped reads and idempotent conversion; validate draft versus submission rules server-side. |
| Blazor / user interaction | Add screening and discovery forms to the native lead workbench; make duplicate decisions explicit and keep firm-only observations out of prospect views. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Existing CRM regression plus focused mandatory-field, duplicate/resubmit, changed scope and owner-scope tests; proposed lead→discovery→acceptance browser journey.

**Existing test seams:** [T-PracticeCrmTests](#e-t-practicecrmtests), [T-RouteCatalogTests](#e-t-routecatalogtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003)

**External prerequisites / blocked acceptance:** None for isolated local implementation; release acceptance still follows the applicable external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-007"></a>
### AS-PAR-007 — Implement factual intake and the Q-to-CE questionnaire crosswalk

**Story:** As a **onboarding coordinator and client respondent**, I want to **supply complete current facts without changing the original question banks or exposing internal assessment**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** onboarding coordinator and client respondent.

**Controlling specification:** [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:INT-02/03; P:Q01–Q40; P:LC-04/07; P:AT-05; P:source §7.

**Related existing source:** [ASSESS](#e-assess), [ACCEPT](#e-accept), [OLD-AUD](#e-old-aud), [SPEC](#e-spec).

**Exact gap / evidence limit:** CE/RV banks and progress display exist, but the prototype Q01–Q40 are not proven mapped to the 62 CE questions, and a complete respondent/edit/reopen/attest workflow is not established by section counters.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-05](#p-at-05):** Required follow-up evidence and review appear; case cannot bypass the condition

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-007-AC01 | the original CE and RV banks are loaded | the prototype questionnaire is mapped | retain every original identifier/wording/version and publish an explicit many-to-many mapping from P:Q IDs; identify missing data fields or additional prompts without relabeling Q01 as CE-001 |
| AS-PAR-007-AC02 | a client or coordinator edits factual responses | they save or submit | store raw and normalized answers, respondent, scope, template/revision, evidence versions and validation status; autosave is not attestation and unknown mandatory answers block submission |
| AS-PAR-007-AC03 | conditional ownership, predecessor, payroll, tax or evidence rules apply | answers change | show all applicable follow-ups using approved bounded rules, including repeatable/effective-dated parties; never execute user-supplied scripts or assume a global threshold |
| AS-PAR-007-AC04 | Q38–Q40 and restricted Q09 assessments are requested from a client view | the query and rendering execute | exclude firm-only judgments and restricted evidence, while exposing only approved factual questions/clarifications |
| AS-PAR-007-AC05 | a submitted response needs correction | the respondent reopens it | create a new response revision with differences and a fresh declaration; retain previous attestation and stale affected assessment decisions |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse QuestionnaireTemplate, QuestionDefinition and existing response/evidence models; add only missing typed repeatable-party/authority and response-revision fields after crosswalk review. |
| Application services | Implement missing bounded response/draft/submission and rule-result commands in Acceptance; return all missing requirements and next-review routing, not an automatic acceptance. |
| Blazor / user interaction | Provide sectioned respondent and coordinator forms with M/C/O labels, evidence requests and progress derived from actual required questions. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Assert all 40 Q entries resolve to approved mappings or explicit blocked decisions; preserve 62 CE and 30 RV source wording. Parameterized conditional/unknown/reopen/classification tests and respondent/coordinator E2E.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-AcceptanceDecisionTests](#e-t-acceptancedecisiontests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-006](#as-par-006)

**External prerequisites / blocked acceptance:** Compliance/quality owner approves question mapping, applicability and evidence rules; jurisdiction facts and thresholds are not supplied by this document.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-008"></a>
### AS-PAR-008 — Complete restricted compliance disposition and acceptance routing

**Story:** As a **compliance officer and engagement partner**, I want to **make a reasoned acceptance decision only after current required clearances**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** compliance officer and engagement partner.

**Controlling specification:** [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-04; P:G01; P:Q09/34/38–40; P:AT-05/06; P:compliance.decide.

**Related existing source:** [ASSESS](#e-assess), [ACCEPT-UI](#e-accept-ui), [ACCEPT](#e-accept), [AUTH](#e-auth).

**Exact gap / evidence limit:** Acceptance persistence and waiting-workspace intent are real. Restricted case collection, clarification and independently attributable specialist disposition are not a complete first-class journey.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-06](#p-at-06):** New professional work blocked; reason and authorized follow-up retained

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-008-AC01 | a submitted case contains multiple unresolved rules | compliance opens it | show all assigned restrictions, evidence sufficiency and unresolved conditions without collapsing them into a single numeric score |
| AS-PAR-008-AC02 | compliance requests clarification, enhanced review or recommends decline | the decision is saved | retain reason, exact case/evidence revision, reviewer and scope; publish only permitted factual requests to onboarding/client |
| AS-PAR-008-AC03 | the partner accepts conditionally | the command executes | distinguish pre-start blockers from approved post-start follow-ups, each with owner/deadline; an unresolved prohibition cannot be bypassed by a warning acknowledgement |
| AS-PAR-008-AC04 | acceptance evidence or service scope changes before activation | the partner decision is reused | reject stale authority and route reassessment; preserve the original decision and do not duplicate the client-workspace intent |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Extend existing SpecialistClearance and acceptance condition evidence only where missing; restricted classification must be query-enforced. |
| Application services | Reuse AcceptanceDecisionService.RecordAsync and existing generation checks; add missing case/clearance transition commands rather than replace the working acceptance path. |
| Blazor / user interaction | Create a restricted compliance queue and case view with permitted outcome handoff; preserve truthful WAITING_FOR_INTEGRATION behavior. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Negative classification, self-review, stale case, all-rule aggregation and activation-block tests; independent coordinator→compliance→partner browser sessions.

**Existing test seams:** [T-AcceptanceDecisionTests](#e-t-acceptancedecisiontests), [T-AuthorizationDecisionTests](#e-t-authorizationdecisiontests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-007](#as-par-007)

**External prerequisites / blocked acceptance:** Named qualified compliance/partner owners supply policy and actual professional dispositions. No screening subscription or automated reporting provider is mandated.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-009"></a>
### AS-PAR-009 — Replace proposal simulations with persisted negotiation and review

**Story:** As a **relationship owner and commercial reviewer**, I want to **issue a controlled proposal and capture a version-bound client response**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** relationship owner and commercial reviewer.

**Controlling specification:** [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-05; P:AT-03/04/36/44; P:proposal.submit/review.

**Related existing source:** [PROPOSAL](#e-proposal), [CRM](#e-crm), [LEADS](#e-leads).

**Exact gap / evidence limit:** ProposalDetail.razor has a hard-coded sample projection and Task.Delay handlers changing only component state. Existing CRM services must drive the page instead.

**Exact prototype permission acceptance anchor:** `proposal.submit` — “Current discovery/scope and separate pricing review.” ([P-PERM](#e-p-perm)).

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-009-AC01 | a real proposal ID is opened | the page loads | read only that authorized persisted proposal, lines, fee/currency, validity, terms, author and revision history; no sample defaults or synthetic status changes |
| AS-PAR-009-AC02 | a preparer submits a pricing revision | independent commercial review occurs | check accepted scope, rate/fee basis, discounts and approval limits; self-approval is denied and internal economics are absent from client outputs |
| AS-PAR-009-AC03 | negotiation changes fees, entities, service or dates | a revised proposal is saved | create a new version with comparison; invalidate only relevant decisions and route scope/independence changes back to assessment |
| AS-PAR-009-AC04 | a verified client responds to a current or expired proposal | the response is recorded | bind identity, authority, document hash and revision; expired/stale acceptance cannot authorize the current version or activate an engagement |
| AS-PAR-009-AC05 | email is unavailable or page reload follows submission | the workflow resumes | retain committed business state and separate queued/failed/sent delivery evidence; no Task.Delay success stub remains |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Preserve existing Proposal/Opportunity records; add only absent revision, authority, expiry or delivery references with additive migration. |
| Application services | Wire existing PracticeCrmService operations, adding missing commands for negotiation/client response with idempotency and concurrency. |
| Blazor / user interaction | Replace stub details/actions and connect the actual response route; ensure interactive rendering is configured where required. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** CRM positive/reject/retry tests plus persisted browser reload and independent pricing/client-response journey; assert request-ID changes never display the same sample proposal.

**Existing test seams:** [T-PracticeCrmTests](#e-t-practicecrmtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-006](#as-par-006), [AS-PAR-008](#as-par-008)

**External prerequisites / blocked acceptance:** None for isolated local implementation; release acceptance still follows the applicable external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-010"></a>
### AS-PAR-010 — Complete engagement terms, activation and client/entity mapping

**Story:** As a **onboarding coordinator and authorized signatory**, I want to **activate only the exact accepted and contracted entity/service scope**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** onboarding coordinator and authorized signatory.

**Controlling specification:** [§6](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s06), [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-06/07/09; P:G02; P:AT-02/04/25; P:source §19.2.

**Related existing source:** [CRM](#e-crm), [CLIENT](#e-client), [ENGAGE](#e-engage), [ACCEPT](#e-accept), [PORTAL](#e-portal).

**Exact gap / evidence limit:** Client/engagement and acceptance foundations exist, but binding terms, authority verification and a cohesive activation/portal handoff are not demonstrated end-to-end.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-04](#p-at-04):** Commercial acceptance recorded; professional engagement activation blocked

> **[P:AT-25](#p-at-25):** Financial dataset not duplicated; approvals/workpapers remain engagement-specific

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-010-AC01 | a reviewed proposal and current acceptance exist | terms are prepared | use the approved service template with exact entity/period/framework, responsibilities, fees, confidentiality, access and effective dates; retain proposal and terms versions/hashes |
| AS-PAR-010-AC02 | terms are unsigned, expired, changed, wrong-scope or signed by an unverified contact | professional activation is attempted | deny with precise outstanding requirements; allowed administrative collection remains explicitly non-professional |
| AS-PAR-010-AC03 | valid accepted scope and verified executed terms are presented twice | conversion/activation runs | create or link one client/entity/contact/engagement mapping, no office or client ledger posting, and separate billing contacts from signatories |
| AS-PAR-010-AC04 | one entity-period is reused by another service engagement | the second engagement is created | reference the approved shared dataset without cloning balances or inheriting the first engagement’s approvals or access |
| AS-PAR-010-AC05 | an invitation fails or expires | onboarding resumes | keep the contact/invitation pending, permit idempotent reissue, and never label a queued invitation an active authenticated user |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse PracticeClient, ClientContact, Engagement and existing terms models where present; add a scoped versioned terms/authority snapshot only if the existing model lacks it. |
| Application services | Keep one guarded activation command checking current acceptance, effective terms, permitted mode/resources/conditions; no generic status setter. |
| Blazor / user interaction | Add a terms/activation checklist and client 360 handoff; all IDs are stable entity/service references, not a revived Perfex project requirement. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Conversion retry, expired/changed terms, contact authority, shared-period nonduplication and portal pending-state tests; later contract→activation browser acceptance.

**Existing test seams:** [T-PracticeCrmTests](#e-t-practicecrmtests), [T-AcceptanceDecisionTests](#e-t-acceptancedecisiontests), [T-Microsoft365AccessTests](#e-t-microsoft365accesstests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-008](#as-par-008), [AS-PAR-009](#as-par-009)

**External prerequisites / blocked acceptance:** Client/firm signatory authority and allowed signature method require business approval. Live invitation/identity and legal signing evidence remain external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-011"></a>
### AS-PAR-011 — Deliver annual change comparison, continuance and safe roll-forward

**Story:** As a **relationship owner, client respondent and partner**, I want to **renew current authority without treating last year’s evidence as this year’s work**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** relationship owner, client respondent and partner.

**Controlling specification:** [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-08/25; P:Y01–Y30; P:AT-08–10/37; P:continuance.decide.

**Related existing source:** [ASSESS](#e-assess), [ACCEPT](#e-accept), [ROLL-UI](#e-roll-ui), [RESTATE-UI](#e-restate-ui), [OLD-AUD](#e-old-aud).

**Exact gap / evidence limit:** Period roll-forward and continuance decision foundations exist; the annual questionnaire/delta/obligation/renewal workspace is not a complete equivalent. Prototype Y questions are defined in six grouped ranges, not 30 individually worded prompts.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-08](#p-at-08):** New annual snapshot and continuance decision created; prior answers remain unchanged

> **[P:AT-09](#p-at-09):** No invalid percentage; effective rule creates a review exception and scope decision

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-011-AC01 | a prior approved annual snapshot exists | the respondent starts the next review | show prior values and Unchanged/Changed/Previously unknown now known/No longer applicable, preserving new value, effective date, reason and evidence when required |
| AS-PAR-011-AC02 | all answers are unchanged | the response is submitted | create a new attested snapshot and current firm independence/capacity review; never copy a prior approval as current continuance |
| AS-PAR-011-AC03 | a metric starts at zero or uses a different period/currency | delta evaluation runs | show absolute change and percentage-not-meaningful or a comparability exception; use an approved effective-dated rule and obtain human obligation/scope disposition |
| AS-PAR-011-AC04 | a partner approves current continuance | a new engagement/period is rolled forward | copy only approved structure/reference links/draft books; new IDs and predecessor lineage are retained while samples, conclusions, approvals and signatures are not inherited |
| AS-PAR-011-AC05 | a material change occurs between scheduled reviews | the event is accepted | open a scoped reassessment and specific restrictions/next actions without suspending unrelated authorized work |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Add missing annual case/delta/obligation links to existing questionnaire and acceptance models; keep prior-period facts and opening bridges immutable. |
| Application services | Reuse current roll-forward and acceptance commands; introduce only a bounded approved rule evaluator/occurrence identity, not a generic rules platform. |
| Blazor / user interaction | Build renewal and continuance views showing field-level changes, restrictions, current reviewer and next-period readiness. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** No-change, zero-base, currency mismatch, event-trigger, retry and no-copied-signoff tests; new-client and recurring-client browser cycles.

**Existing test seams:** [T-AcceptanceDecisionTests](#e-t-acceptancedecisiontests), [T-ClientAccountingTests](#e-t-clientaccountingtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-007](#as-par-007), [AS-PAR-008](#as-par-008), [AS-PAR-010](#as-par-010)

**External prerequisites / blocked acceptance:** Owner-approved frequency, obligation/jurisdiction rules and six-range Y-to-RV mapping are required; do not invent tax thresholds or individual Y wording.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-012"></a>
### AS-PAR-012 — Complete engagement service plans, team capacity and scoped task handoffs

**Story:** As a **engagement manager**, I want to **coordinate resources and dependencies without task completion bypassing a professional gate**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** engagement manager.

**Controlling specification:** [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-09; P:AUT-06; P:UX-03; P:team.assign; P:AT-10/11/46.

**Related existing source:** [ENGAGE](#e-engage), [TIME-UI](#e-time-ui), [TIME](#e-time), [PLAN-UI](#e-plan-ui), [CATALOG](#e-catalog).

**Exact gap / evidence limit:** Assignments, tasks, due dates and time exist but not a consolidated versioned service-plan/team workload experience.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-10](#p-at-10):** Tasks/templates and permitted reference data copied; signatures, conclusions and approvals reset

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-012-AC01 | an accepted engagement chooses an approved service/template version | the plan is instantiated | create tasks, PBC and workpaper obligations once, with M/C/O applicability, inputs, outputs, owner/reviewer, effort, dependencies and explicit calendar-relative dates |
| AS-PAR-012-AC02 | a staff assignment is changed | the manager saves | validate existing eligible staff and scope, qualification/conflicts, capacity and dates; do not expand the staff member’s entity permissions by selecting their name |
| AS-PAR-012-AC03 | an optional task is proposed N/A or a required task lacks evidence | the plan is advanced | require a reason and authorized applicability decision; no tick-box completion posts journals, clears reviews or issues reports |
| AS-PAR-012-AC04 | signed scope or budget changes after activation | the change request is approved | record affected deliverables/deadlines/fees and professional consequences without silently reducing required procedures |
| AS-PAR-012-AC05 | a team member is absent or revoked | open work is reassigned | preserve attribution and route to a qualified replacement with its own acceptance of responsibility |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Extend existing EngagementAssignment and task/template structures with missing version/dependency/applicability and allocation information; scope occurrences and uniqueness. |
| Application services | Use PracticeTimeService and planning/engagement commands; separate staffing authority, professional approval and commercial budget approval. |
| Blazor / user interaction | Add team/workload and engagement-cockpit projections, conflict explanations, ownership and actionable dependency links. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Instantiation retry, calendar/absence, unauthorized assignment, dependency and N/A approval tests; manager→preparer→reviewer browser handoff.

**Existing test seams:** [T-PracticeTimeTests](#e-t-practicetimetests), [T-AuditPlanningTests](#e-t-auditplanningtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010)

**External prerequisites / blocked acceptance:** None for isolated local implementation; release acceptance still follows the applicable external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-013"></a>
### AS-PAR-013 — Complete two-stage PBC adequacy review and request ownership

**Story:** As a **client finance contributor, coordinator and reviewer**, I want to **supply requested files and obtain a reviewable adequacy decision distinct from upload receipt**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** client finance contributor, coordinator and reviewer.

**Controlling specification:** [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-11/12; P:AT-12/13; P:pbc.assign/upload/accept/clarify; P:source §9.1.

**Related existing source:** [PBC-UI](#e-pbc-ui), [PBC-CLIENT](#e-pbc-client), [PORTAL](#e-portal), [DOC-DOM](#e-doc-dom).

**Exact gap / evidence limit:** Request creation, threads, upload intents and durable transfer status are real. A complete administrative-versus-technical adequacy workflow, catalogue and responsibility reassignment are not proven by the current screens.

**Exact prototype permission acceptance anchor:** `pbc.assign` — “Only contacts already approved for the same entity.” ([P-PERM](#e-p-perm)).

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-013-AC01 | the service requires a PBC category | a request is created | store entity/engagement/period, expected span, format, description, confidentiality, responsible approved contributor, firm owner, reviewer, deadline and adequacy criteria |
| AS-PAR-013-AC02 | one or several uploaded documents reach trusted byte verification | administrative and technical review occur | record receipt separately from completeness and suitability; technical acceptance targets exact versions, and uploader cannot accept their own submission |
| AS-PAR-013-AC03 | the evidence is incomplete, wrong-period or illegible | the reviewer requests replacement | retain original files/rejections and thread, define the missing evidence and due owner, and keep dependent procedures blocked |
| AS-PAR-013-AC04 | a client administrator reassigns responsibility | the action is requested | allow only already-approved contacts within the same entity, preserving assignment history; do not grant new entity access |
| AS-PAR-013-AC05 | a request category is not applicable | the reviewer approves its reason | retain the disposition and never silently remove a required PBC obligation |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse existing PbcRequest, uploads, communications and evidence versions; add only missing administrative/technical review decision and assignment history fields. |
| Application services | Extend current PBC commands for assign/adequacy/reject/reopen; transitions must validate current scope/version and outbox notification state. |
| Blazor / user interaction | Add reviewer inbox, explicit acceptance/rejection controls and category templates; show Requested/Received/Validation/Accepted states truthfully. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** PBC multiple-file/replacement/role/scope/state tests and two-client browser journey; receipt must never satisfy adequacy automatically.

**Existing test seams:** [T-PbcTests](#e-t-pbctests), [T-PbcTransferTests](#e-t-pbctransfertests), [T-DocumentSnapshotTests](#e-t-documentsnapshottests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-012](#as-par-012)

**External prerequisites / blocked acceptance:** Live storage verification requires the approved document provider; local transfer simulation proves only a synthetic transport path.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-014"></a>
### AS-PAR-014 — Make controlled uploads, replacement history and approved template access usable

**Story:** As a **preparer and client contributor**, I want to **upload real files without manual hashing or confusing a template with performed work**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** preparer and client contributor.

**Controlling specification:** [§9](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s09), [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:NFR-03; P:LC-11; P:AT-12; P:WP-06–10; P:workpaper.write.

**Related existing source:** [PBC-CLIENT](#e-pbc-client), [PBC-UI](#e-pbc-ui), [WP-UI](#e-wp-ui), [WEB-HOST](#e-web-host), [WORKER](#e-worker), [DOC-DOM](#e-doc-dom).

**Exact gap / evidence limit:** The client form asks the user to enter SHA-256. Workpaper-specific approved template download and completed workbook/evidence replacement are not integrated. Existing chunk and snapshot protections must remain intact.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-12](#p-at-12):** New immutable version, original retained, affected workpapers identified for review

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-014-AC01 | an authorized user selects an allowed file | upload preparation starts | compute a digest through a bounded browser/approved transport path and independently verify bytes at the trusted server boundary; never require the user to invent or paste a hash |
| AS-PAR-014-AC02 | an upload is interrupted, duplicated, reordered, oversized or has a hash mismatch | chunks are processed | enforce existing request-bound capabilities, sequence/expiry/limits and idempotency; report actual progress and reject invalid completion without claiming provider receipt |
| AS-PAR-014-AC03 | a template is downloaded | the operation completes | record optional access evidence but leave the workpaper’s execution and clearance state unchanged |
| AS-PAR-014-AC04 | a completed workbook or evidence file replaces an approved version | the replacement is committed | create a new immutable version with original retained, media/size/hash/uploader/time and source linkage, and invalidate only affected current approvals |
| AS-PAR-014-AC05 | a file contains macros, active content, misleading MIME or path-like names | the parser/preview boundary receives it | apply allowlisted type/content policy and isolated safe processing; no macros or embedded programs execute and filenames never determine executable paths |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse DocumentRef, preserved snapshots, upload intent/chunk and evidence-link models. Keep local storage transient and bounded, not a parallel live DMS. |
| Application services | Reuse current streaming transport and snapshot commands; add a workpaper attachment purpose and version-bound template selector where missing. |
| Blazor / user interaction | Provide file picker, byte progress, retry/expired intent guidance, version history, scoped downloads and distinct template versus completed-work labels. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Boundary tests at actual configured chunk/file limits, mismatch and malicious filename tests, same-byte retry and replacement freshness; browser refresh/reselect handling without persisting sensitive bytes in localStorage.

**Existing test seams:** [T-PbcTransferTests](#e-t-pbctransfertests), [T-DocumentSnapshotTests](#e-t-documentsnapshottests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-002](#as-par-002), [AS-PAR-013](#as-par-013)

**External prerequisites / blocked acceptance:** Canonical SharePoint delivery requires AS-PAR-033; approved template bytes require methodology-owner evidence. No ClamAV or new scanning dependency.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-015"></a>
### AS-PAR-015 — Add client-team nominations, verified signatory powers and deliverable access

**Story:** As a **client administrator and authorized management signatory**, I want to **coordinate contacts without self-promoting to staff or professional authority**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** client administrator and authorized management signatory.

**Controlling specification:** [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-07/22; P:UX-03; P:client.nominate; P:management.approve; P:roles client_admin/client_finance/client.

**Related existing source:** [ROLES](#e-roles), [PORTAL](#e-portal), [PBC-CLIENT](#e-pbc-client), [MGMT-UI](#e-mgmt-ui), [FS-REVIEW](#e-fs-review), [SETUP-UI](#e-setup-ui).

**Exact gap / evidence limit:** ClientUser is the main current client role. Requests and package decisions exist, but the prototype’s distinct administrator, contributor and verified signatory responsibilities are not clearly enforced or fully surfaced.

**Exact prototype permission acceptance anchor:** `client.nominate` — “Existing entity only; signatory nomination requires firm verification.” ([P-PERM](#e-p-perm)).

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-015-AC01 | a client administrator nominates a contact | the nomination is submitted | create a scoped access request for firm verification, not an immediate grant; no nomination may add firm staff powers or sibling entities |
| AS-PAR-015-AC02 | a contributor opens a management approval URL | authority is checked | deny approval without separate verified signatory capability even when the contributor can upload evidence or administer contacts |
| AS-PAR-015-AC03 | verified management reviews a package | the decision is recorded | present the exact internally reviewed artifact and approved action/adjustment summary; retain explicit decision, authority, artifact hash/version and evidence, distinct from receipt acknowledgement |
| AS-PAR-015-AC04 | a client asks to revoke access or reassign a request | the firm processes it | retain historic authorship, invalidate current sessions/capabilities and expose only permitted status to the requester |
| AS-PAR-015-AC05 | the client dashboard is rendered | it lists terms, requests, deliverables, invoices and upcoming actions | show only explicitly authorized client-safe records; never expose internal risks, materiality, discussions, firm economics or unpublished workpapers |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Add scoped client-capability/authority evidence and nomination state to existing user/contact/grant structures; legacy ClientUser grants default to no unproven signing powers. |
| Application services | Extend RoleAdministration and client query/review commands; verified authority must be rechecked at decision time, not only at invitation. |
| Blazor / user interaction | Add client-team/access-request pages and separate contributor/signatory home actions; reuse package review and request threads. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Three-persona positive/negative matrix, nomination/escalation/revocation tests, cross-client byte denial and a real separate-session management approval journey.

**Existing test seams:** [T-RoleAdministrationTests](#e-t-roleadministrationtests), [T-Microsoft365AccessTests](#e-t-microsoft365accesstests), [T-PbcTests](#e-t-pbctests), [T-ClientAccountingTests](#e-t-clientaccountingtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-013](#as-par-013)

**External prerequisites / blocked acceptance:** Firm verification of management authority and live identity/invitation delivery remain independent requirements.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-016"></a>
### AS-PAR-016 — Make client clarifications a structured, version-aware workflow

**Story:** As a **preparer, client contributor and reviewer**, I want to **resolve missing evidence without exposing internal review judgments or auto-clearing an answer**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** preparer, client contributor and reviewer.

**Controlling specification:** [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-12; P:API-04; P:AT-13/28/34; P:pbc.clarify.

**Related existing source:** [PBC-UI](#e-pbc-ui), [PBC-CLIENT](#e-pbc-client), [NOTE-UI](#e-note-ui), [DOC-DOM](#e-doc-dom).

**Exact gap / evidence limit:** Two-sided request threads exist, but a structured query with blocking scope, issuer/reviewer disposition and impact linkage is not established by a free-text reply alone.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-13](#p-at-13):** Receipt recorded separately from adequacy; reviewer can reopen/seek clarification

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-016-AC01 | an internal review matter needs factual client input | staff creates a client query | record precise client-visible wording, purpose, linked request/account/workpaper, evidence expected, owner, due date and blocking scope; exclude private deliberations |
| AS-PAR-016-AC02 | a client supplies a reply or new file | the reply is saved | append it as answered/received only, preserving old responses and exact versions |
| AS-PAR-016-AC03 | the issuer or authorized independent reviewer evaluates the answer | they accept, clarify or reopen | record a reasoned disposition and identify which dependency was satisfied; the client cannot mark their own evidence adequate |
| AS-PAR-016-AC04 | an approved answer changes balances, source facts or assumptions | the dependent workflow resumes | create or link the new source revision and stale affected calculations/clearances instead of silently editing their result |
| AS-PAR-016-AC05 | evidence remains unavailable | a permitted alternative or estimate is proposed | require professional approval, explicit limitation/amount/revisit date and retained blocker; never insert a zero or auto-select an audit opinion |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Extend current PBC communication/request structures with query target/blocking/decision links rather than create a second chat system. |
| Application services | Guard query creation, response and disposition in Documents/Reviews seams with existing audit/outbox conventions. |
| Blazor / user interaction | Connect internal review point→redacted client query→reviewer disposition with two distinct visibility projections. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Confidentiality projection, insufficient reply, same-author clearance, stale target and alternate evidence tests; client/staff timeline browser journey.

**Existing test seams:** [T-PbcTests](#e-t-pbctests), [T-ApprovalTests](#e-t-approvaltests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-004](#as-par-004), [AS-PAR-013](#as-par-013)

**External prerequisites / blocked acceptance:** None for isolated local implementation; release acceptance still follows the applicable external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-017"></a>
### AS-PAR-017 — Complete strategy, materiality approval and announcement documents

**Story:** As a **audit preparer, manager and partner**, I want to **approve a reasoned plan tied to current inputs before dependent fieldwork**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** audit preparer, manager and partner.

**Controlling specification:** [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§20](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s20), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-10; P:G03; P:AT-30; P:TR-07; P:source §11.3.

**Related existing source:** [PLAN-UI](#e-plan-ui), [PLAN](#e-plan), [CATALOG](#e-catalog), [ENGAGE](#e-engage).

**Exact gap / evidence limit:** Materiality/risk entry and program adoption exist. A complete typed strategy, independent materiality approval, impact assessment and separately authorized announcement journey is not demonstrated by entry forms.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-30](#p-at-30):** Server recalculates, records rationale and requires impact assessment/approval

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-017-AC01 | an audit engagement is planned | the preparer submits strategy and materiality | retain business/control understanding, assertions, scope, approach, team, timetable, communications, specialist/EQR decisions and exact source benchmark/currency/version |
| AS-PAR-017-AC02 | materiality is calculated | a benchmark is selected | use server decimals and approved rounding, retain candidate and chosen basis/rationale, performance/trivial amounts and qualitative considerations; negative/near-zero bases require a justified alternative |
| AS-PAR-017-AC03 | a prepared strategy/materiality version is reviewed | senior, manager and partner decisions are made | bind each distinct required approval to exact current versions and prevent self-review or technical-admin-only approval |
| AS-PAR-017-AC04 | approved source metrics, scope or materiality change | a revision is submitted | preserve history, identify impacted populations/samples/workpapers/difference evaluations, require impact review and stale their affected approvals |
| AS-PAR-017-AC05 | an announcement is required | its operational details and release are approved | generate from approved entity/team/scope data and track dispatch/client acknowledgement separately from engagement-letter acceptance |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Extend existing MaterialityAssessment/planning and document models with missing approval/source/impact links; add typed strategy/announcement metadata only where absent. |
| Application services | Reuse AuditPlanningService and document/approval services; do not calculate materiality solely in Razor or permit automatic professional selection. |
| Blazor / user interaction | Add strategy/materiality review stages, version comparisons and announcement preparation/release links to AuditPlan. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Benchmark boundary, concurrent revision, independent approval and impact graph tests; browser strategy→approval→changed-source journey.

**Existing test seams:** [T-AuditPlanningTests](#e-t-auditplanningtests), [T-AuditProgramWorkflowTests](#e-t-auditprogramworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-012](#as-par-012)

**External prerequisites / blocked acceptance:** Methodology owner supplies approved materiality bases/ranges and document template; operational receipt is not professional approval.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-018"></a>
### AS-PAR-018 — Implement the integrated six-tab audit-area workspace

**Story:** As a **preparer and senior reviewer**, I want to **perform and clear one audit area without disconnected screens or simulated completion**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** preparer and senior reviewer.

**Controlling specification:** [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§20](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s20), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:AUD-01/02; P:LC-20; P:WP-01–20; P:workpaper.write/review.

**Related existing source:** [WP-UI](#e-wp-ui), [FIELD-UI](#e-field-ui), [PLAN](#e-plan), [PROGRAM](#e-program), [AUDIT-DOM](#e-audit-dom), [P-WP](#e-p-wp).

**Exact gap / evidence limit:** Workpaper.razor supports server draft saving and immutable submission but not the prototype’s integrated Overview, Guidelines, Workbook, Evidence, Clearance and History workspace. Preserve its acknowledged-draft and concurrency safeguards.

**Exact prototype permission acceptance anchor:** `workpaper.write` — “Assigned procedure, recorded sources and conclusions.” ([P-PERM](#e-p-perm)).

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-018-AC01 | an authorized workpaper is selected | the workspace opens | display all six accessible tabs with one current entity/engagement/period/version context and navigation back to the audit-area index |
| AS-PAR-018-AC02 | the preparer reads Overview and Guidelines | the page renders | show objective/assertions/risks, approved methodology/procedure text, materiality/population/source versions, required documentation, assigned people and current dependencies |
| AS-PAR-018-AC03 | the preparer changes work performed or conclusion | autosave, explicit save, discard, refresh or circuit reconnect occurs | retain the existing server-backed draft/acknowledgement protocol; stale target conflicts cannot overwrite saved work and autosave never submits |
| AS-PAR-018-AC04 | Workbook and Evidence are completed | Submit is selected | flush the last acknowledged draft and freeze the exact content, completed-file/evidence versions and required execution results; missing required evidence/conclusion prevents submission |
| AS-PAR-018-AC05 | independent review returns or clears work | the workspace updates | show current review points and disposition, reviewer/version/time stamp and recalculated progress, not sample clearances |
| AS-PAR-018-AC06 | a source or workpaper version changes | History is opened | preserve previous submissions/clearances and show invalidation reason; current progress counts only current, eligible clearances |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse WorkpaperDraft, WorkpaperSubmission, AuditProcedureResult and review/evidence models. Add missing frozen manifest links rather than duplicate the workpaper engine. |
| Application services | Compose AuditPlanningService draft/submission, AuditProgramService result/review and the new review-point commands; one authoritative lifecycle owns each status. |
| Blazor / user interaction | Implement six Blazor tabs (page or accessible modal), template/file/evidence actions, clear dependency messages and current/history views; no legacy JS state becomes production authority. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Replay all 20 numbered prototype scenario groups through proposed .NET Playwright tests with persisted fixtures; adapt the unsafe N/A toggle to an independent approval and keep sample percentages test-only.

**Existing test seams:** [T-AuditPlanningTests](#e-t-auditplanningtests), [T-AuditProgramWorkflowTests](#e-t-auditprogramworkflowtests), [T-ApprovalTests](#e-t-approvaltests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-004](#as-par-004), [AS-PAR-014](#as-par-014), [AS-PAR-017](#as-par-017)

**External prerequisites / blocked acceptance:** None for isolated local implementation; release acceptance still follows the applicable external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-019"></a>
### AS-PAR-019 — Bind evidence, workbook versions and clearance to a frozen execution manifest

**Story:** As a **audit reviewer**, I want to **review the exact sources and completed work actually submitted**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** audit reviewer.

**Controlling specification:** [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§12](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s12), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:AUD-01; P:GOV-01/02; P:DATA-08; P:AT-12/29/36/44; P:WP-08–18.

**Related existing source:** [DOC-DOM](#e-doc-dom), [AUDIT-DOM](#e-audit-dom), [PROGRAM](#e-program), [APPROVAL](#e-approval), [EVID-UI](#e-evid-ui).

**Exact gap / evidence limit:** Evidence references and immutable submissions exist, but the combined workpaper workbook/PBC/direct evidence/clearance manifest and user-facing mutation impact are not fully established.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-29](#p-at-29):** Dependent draft reports/approvals marked stale and routed for re-review

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-019-AC01 | a workpaper links accepted PBC or direct evidence | the link is created | validate firm/client/engagement/classification, exact version/hash and permitted purpose; preserve evidence type and provenance |
| AS-PAR-019-AC02 | a user unlinks or replaces evidence after submission | the action is authorized | create a new working revision/manifest and retain prior links in the historical submission; never mutate the reviewer’s frozen snapshot |
| AS-PAR-019-AC03 | a reviewer approves the workpaper | the service records clearance | bind actor/role-at-decision, delegation where applicable, workpaper/result revision, template/methodology, materiality/population/source generation and exact evidence hashes |
| AS-PAR-019-AC04 | a relevant source, mapping, materiality or evidence dependency changes | the current clearance is evaluated | mark it stale or require a recorded impact assessment; unrelated work is not reset without a dependency reason, and issued artifacts remain historical |
| AS-PAR-019-AC05 | evidence references are dangling, mismatched or unavailable | submission/release attempts validation | fail closed with actionable scope-safe errors rather than treating a reference string as verified bytes |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Extend existing EvidenceLink/submission/approval structures with exact-version dependency manifest entries where needed. Unique scoped manifest keys and append-only historical links are required. |
| Application services | Centralize dependency freshness evaluation in existing application seams; keep reference linking separate from proof of external byte preservation. |
| Blazor / user interaction | Add evidence picker, direct/PBC provenance, current-versus-submitted snapshot links, missing-byte warnings and an invalidation timeline. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Cross-client evidence, stale manifest, link/unlink, same-name replacement, concurrent submission and immutable-history tests; restore manifest hashes with parent records.

**Existing test seams:** [T-DocumentSnapshotTests](#e-t-documentsnapshottests), [T-ApprovalTests](#e-t-approvaltests), [T-AuditScopeIntegrityTests](#e-t-auditscopeintegritytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-004](#as-par-004), [AS-PAR-014](#as-par-014)

**External prerequisites / blocked acceptance:** Provider capability and exact-byte preservation proof are required for live evidence. A local hash alone is not live storage acceptance.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-020"></a>
### AS-PAR-020 — Complete populations, samples and controlled confirmation journeys

**Story:** As a **auditor and independent reviewer**, I want to **perform reproducible tests over reconciled populations and distinguish direct confirmations from client evidence**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** auditor and independent reviewer.

**Controlling specification:** [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§20](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s20), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:AUD-03/04; P:LC-10/16; P:AT-27/30; P:source §11.2.

**Related existing source:** [POP-UI](#e-pop-ui), [PLAN](#e-plan), [FIELD](#e-field), [PROGRAM](#e-program), [EVID-UI](#e-evid-ui).

**Exact gap / evidence limit:** Population and typed fieldwork foundations exist, but full item-level sample provenance/substitution and auditor-controlled confirmation journeys require acceptance evidence and usable workpaper handoff.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-020-AC01 | a population is prepared | it is accepted for sampling | retain definition, source hash/version, scope/date range, reconciliation totals and exclusions with reviewer conclusion |
| AS-PAR-020-AC02 | manual or approved statistical selection is performed | the sample is submitted | retain method/rationale, strata, seed where used, selected source item IDs and any substitutions/reasons; arbitrary manual samples must not claim statistical assurance |
| AS-PAR-020-AC03 | selected items are tested | results are reviewed | record work, source-document references, exceptions, approved projection method where applicable and links to findings/adjustments; changed population invalidates affected selections |
| AS-PAR-020-AC04 | a confirmation is requested | dispatch and response are recorded | preserve authorization, independently verified recipient/source, auditor control, attempts/reminders, response bytes, authenticity evaluation and exceptions |
| AS-PAR-020-AC05 | a confirmation is absent or only client-supplied evidence exists | completion is attempted | require approved alternative procedures and an appropriate conclusion; never relabel a client statement as independently received confirmation |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Extend existing PopulationVersion and typed confirmation/testing structures only for missing selection/substitution/provenance fields; preserve immutable versions. |
| Application services | Reuse AuditPlanningService/AuditFieldworkService and operation/evidence seams; any statistical method needs separate approved fixtures, not an invented formula. |
| Blazor / user interaction | Link the population grid and confirmation case to workpaper evidence/results with explicit reviewer actions and limitations. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Population reconciliation, deterministic selection, substitutions, stale source, direct-versus-client evidence and alternate-procedure tests; bounded browser sample→workpaper flow.

**Existing test seams:** [T-AuditPlanningTests](#e-t-auditplanningtests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-017](#as-par-017), [AS-PAR-019](#as-par-019)

**External prerequisites / blocked acceptance:** Professional sampling/confirmation methodology and verified contact evidence are owner-supplied. No new confirmation network/API is required; approved manual evidence is a separate supported route.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-021"></a>
### AS-PAR-021 — Complete audit-program applicability, execution and current coverage

**Story:** As a **audit manager and reviewer**, I want to **see which required procedures have current evidence and independent review**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** audit manager and reviewer.

**Controlling specification:** [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§20](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s20), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:AUD-02/05; P:AT-46; P:G05/06; P:WP-20; existing AWP catalogue.

**Related existing source:** [FIELD-UI](#e-field-ui), [PROGRAM](#e-program), [CATALOG](#e-catalog), [FIELD](#e-field), [OLD-AUD](#e-old-aud).

**Exact gap / evidence limit:** The catalogue/adoption test proves 165 records and selected execution, not all 165 performed procedures. The fieldwork UI displays “Execute from source workflow” and generic N/A routing rather than a complete execution/review flow.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-46](#p-at-46):** Reason and authorized applicability decision retained; mandatory requirements cannot be silently skipped

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-021-AC01 | the controlled program is adopted | the fieldwork index is shown | retain all source procedure IDs/wording, section and adopted version; never substitute the prototype’s four sample workpapers for the full required programme |
| AS-PAR-021-AC02 | a procedure is proposed not applicable | an independent authorized reviewer decides | retain a specific rationale and applicability decision; a draft toggle or generic placeholder is not sufficient |
| AS-PAR-021-AC03 | an applicable procedure is selected | work is entered and submitted | open the correct execution/workpaper flow with source-bound structured results, evidence references and conclusion; do not permit title-only completion |
| AS-PAR-021-AC04 | a reviewer considers a result | approval is attempted | enforce different preparer, current input generation, exact result revision, required review points and scope; append the decision |
| AS-PAR-021-AC05 | coverage is calculated | one source changes or an N/A proposal is unapproved | count only current reviewed applicable procedures as complete; exclude N/A only after approval and display stale, pending and blocked counts separately |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse AuditProgramVersion/Procedure/Result/Review and applicability states. Extend only missing reviewed-N/A provenance/coverage freshness; do not renumber existing AWP IDs. |
| Application services | Wire the existing publish/adopt, applicability, result-submission and independent-review command contracts in AuditProgramService; do not duplicate this service or introduce a second execution engine. |
| Blazor / user interaction | Replace inert fieldwork labels with command-driven handoffs and a current coverage view; reviewer actions must collect substantive rationale. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Retain ControlledCatalog_UsesScopedAppendOnlyWorkflow and add missing N/A, no-evidence, stale-progress and section-level browser tests. Catalogue count alone remains insufficient acceptance.

**Existing test seams:** [T-AuditProgramWorkflowTests](#e-t-auditprogramworkflowtests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-004](#as-par-004), [AS-PAR-018](#as-par-018), [AS-PAR-020](#as-par-020)

**External prerequisites / blocked acceptance:** None for isolated local implementation; release acceptance still follows the applicable external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-022"></a>
### AS-PAR-022 — Close accounting-intake and reporting-navigation residuals without rebuilding the engine

**Story:** As a **accounting preparer and reviewer**, I want to **trace imported data through current source, mapping, reconciliation and reports in one scoped workflow**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** accounting preparer and reviewer.

**Controlling specification:** [§16](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s16), [§17](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s17), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-13–19; P:DAT-01/02; P:REP-01/02/04; P:AT-14–18/22–27/33.

**Related existing source:** [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui), [MAP-UI](#e-map-ui), [AJE-UI](#e-aje-ui), [EVID-UI](#e-evid-ui), [FS-UI](#e-fs-ui), [CONSOL-UI](#e-consol-ui), [ADV-UI](#e-adv-ui), [PENDING](#e-pending).

**Exact gap / evidence limit:** Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-14](#p-at-14):** Batch remains staged, exceptions visible, no ledger/snapshot approval bypass

> **[P:AT-15](#p-at-15):** Duplicate detected using scope/source identity/hash; no duplicate posting

> **[P:AT-16](#p-at-16):** Operating-mode controls prevent cumulative double counting

> **[P:AT-17](#p-at-17):** Approved normalization, totals and mapping applied consistently; ambiguity rejected

> **[P:AT-18](#p-at-18):** Reconciliation and authorized explanation/adjustment required before acceptance

> **[P:AT-24](#p-at-24):** Server rejects cross-scope reference; no partial financial writes

> **[P:AT-26](#p-at-26):** Only approved components included; eliminations and consolidated version traceable

> **[P:AT-27](#p-at-27):** Reconciling items required; no forced plug entry or automatic unexplained clearance

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-022-AC01 | a financial import starts | its purpose is confirmed | show explicit source/closing/opening/movement/detailed/audit-only purpose supported by the selected capability, source convention and exact entity/book/period/basis/currency; unsupported managed posting remains unavailable |
| AS-PAR-022-AC02 | CSV/XLSX rows include leading zeros, alternate sign conventions, dimensional repeats or unmatched openings | validation occurs | retain original/normalized values and every outcome, apply only an approved normalization/aggregation rule, and block ambiguous/unbalanced or unauthorized source acceptance |
| AS-PAR-022-AC03 | a corrected TB already includes an approved AJE | the replacement is reconciled | reuse the existing reflection/supersession logic and show the effect before confirmation; never add the same adjustment twice |
| AS-PAR-022-AC04 | a user drills from a statement amount | they follow lineage | reach mapped TB accounts, current adjustment revisions and source evidence with context intact; same-named accounts in another client or group are never included |
| AS-PAR-022-AC05 | a report, ageing, reconciliation or budget comparison is requested | the query executes | require explicit authorized report scope and documented parameters; unsupported report/method remains visibly unavailable rather than returning a generic total |
| AS-PAR-022-AC06 | an approved group/FX profile is used | results are reviewed | preserve the existing component, membership, rate, source and golden-fixture controls; local advanced-method acceptance does not automatically enable a production capability |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current accounting entities and source hashes; fill a field only when the traceability/fixture check identifies an actual missing field. No global account pool or new duplicate ledger. |
| Application services | Reuse existing Accounting services and durable GL/package processors; extend guarded projections and missing validation seams only. |
| Blazor / user interaction | Complete import-purpose controls, exception/replacement previews, current-context navigation and amount-level drill-down. Keep XLSX/DOCX/PDF renderers and current approved formulas. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** First map each P:AT row to existing assertions; add only missing boundary cases. Run focused accounting regression and proposed same-entity/multiple-engagement, colliding-code clients and group browser journeys.

**Existing test seams:** [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-AdjustmentBridgeTests](#e-t-adjustmentbridgetests), [T-AccountingIntegrityTests](#e-t-accountingintegritytests), [T-FinancialStatementTests](#e-t-financialstatementtests), [T-TrialBalanceXlsxImporterTests](#e-t-trialbalancexlsximportertests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-002](#as-par-002), [AS-PAR-005](#as-par-005), [AS-PAR-019](#as-par-019)

**External prerequisites / blocked acceptance:** None for isolated local implementation; release acceptance still follows the applicable external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-023"></a>
### AS-PAR-023 — Complete management adjustment decisions and aggregate misstatement evaluation

**Story:** As a **accountant, management signatory and partner**, I want to **resolve differences with exact financial impact and a supported professional conclusion**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** accountant, management signatory and partner.

**Controlling specification:** [§17](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s17), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-17/18/21; P:AT-19/22/34; P:journal.propose/review/consent; P:G05.

**Related existing source:** [AJE-UI](#e-aje-ui), [FIELD](#e-field), [FS-REVIEW](#e-fs-review), [FINDING-UI](#e-finding-ui), [PENDING](#e-pending).

**Exact gap / evidence limit:** Exact-journal difference impact and governed correction states are recorded as implemented. Distinct client-consent UX, materiality-linked aggregate evaluation and completion-level professional disposition remain to be evidenced.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-22](#p-at-22):** Superseding reconciliation prevents counting adjustment twice

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-023-AC01 | a supported difference/AJE is proposed | technical review occurs | reuse the exact journal revision, approved mapping and impact hash; retain account/assertion/cause, gross/net/currency, qualitative/tax/prior-period data when applicable |
| AS-PAR-023-AC02 | verified management accepts or rejects changes to its accounts | the decision is saved | record exact reviewed adjustment/version and reason; rejection remains an unadjusted item, not a deleted proposal or automatically overridden client decision |
| AS-PAR-023-AC03 | offsetting differences are evaluated together | the partner records completion | show gross, signed/net, corrected and unadjusted totals by currency with current materiality and qualitative rationale; offsetting values do not disappear |
| AS-PAR-023-AC04 | an AJE is exported or marked reflected externally | evidence is checked | distinguish download/export from confirmed external posting and require supported source-reflection proof before excluding a reporting adjustment |
| AS-PAR-023-AC05 | a journal/mapping/materiality source changes | a previous evaluation is reused | reject stale impact and route current review while preserving prior conclusions and posted history |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse difference states/impact payloads and approved journal lineage; add only missing aggregate evaluation and signatory-consent links. |
| Application services | Extend current difference/journal and package-review services, not a separate misstatement calculator; never auto-select a report opinion from a threshold. |
| Blazor / user interaction | Add management decision queue and aggregate completion view with exact-version impact, reasons and outstanding matters. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Existing impact/reflection tests plus unadjusted rejection, offsets, stale materiality and client authority tests; preparer→reviewer→management→partner browser flow.

**Existing test seams:** [T-AdjustmentBridgeTests](#e-t-adjustmentbridgetests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests), [T-ClientAccountingTests](#e-t-clientaccountingtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-015](#as-par-015), [AS-PAR-022](#as-par-022)

**External prerequisites / blocked acceptance:** Qualified partner owns aggregate/materiality evaluation; management authority is separately verified.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-024"></a>
### AS-PAR-024 — Complete exact-artifact financial review and client-safe presentation

**Story:** As a **accounting reviewer and client signatory**, I want to **approve the complete artifact that will be considered for release, not only summary totals**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** accounting reviewer and client signatory.

**Controlling specification:** [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-19/22; P:REP-01/04; P:AT-29/33/36; P:G08.

**Related existing source:** [FS-UI](#e-fs-ui), [FS-REV-UI](#e-fs-rev-ui), [MGMT-UI](#e-mgmt-ui), [FS-REVIEW](#e-fs-review), [PENDING](#e-pending).

**Exact gap / evidence limit:** Exact rendered package bytes and management/accounting/partner decisions already exist. ClientFinancialPackage currently emphasizes metadata/totals; a full approved-artifact preview/download and ordered client review handoff need completion evidence.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-33](#p-at-33):** TB ties to all statements, equity reconciles, cash-flow totals reconcile, notes and comparatives checked

> **[P:AT-36](#p-at-36):** Evidence attached to old version; current version remains awaiting approval

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-024-AC01 | a supported package is internally reviewed | management opens it | present the exact canonical artifact and approved Office/PDF renditions with entity, period, framework/template, draft status, package revision and hash |
| AS-PAR-024-AC02 | management submits a decision | the service accepts it | verify signatory scope and required prior review order, bind the exact artifact/version and preserve explicit approval versus acknowledgement |
| AS-PAR-024-AC03 | cash-flow/disclosure inputs or required comparatives are missing | package approval is attempted | show the pending requirements and reject completion; never manufacture a cash-flow bridge from closing balances alone |
| AS-PAR-024-AC04 | the package, input source or renderer-controlled content changes | a prior decision is queried | preserve history and require new required reviews; an old email/approval applies only to the old artifact |
| AS-PAR-024-AC05 | an editable export is downloaded | it is subsequently changed outside the system | do not represent the edited file as the signed issued artifact; require controlled reimport/new revision if it is to be reviewed |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Preserve current FinancialPackage/artifact/review decisions and renderer hashes; add explicit client sharing/presentation links only where missing. |
| Application services | Reuse FinancialPackageReviewService and scoped artifact download endpoints; no new renderer is necessary. |
| Blazor / user interaction | Add safe full-artifact access, approval stage explanations, version comparison and disclosure/lineage visibility in the existing pages. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Cross-format totals/hash metadata, exact artifact stage order, stale/old-version decisions and restricted client downloads; proposed management review E2E.

**Existing test seams:** [T-FinancialStatementTests](#e-t-financialstatementtests), [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-ApprovalTests](#e-t-approvaltests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-015](#as-par-015), [AS-PAR-022](#as-par-022), [AS-PAR-023](#as-par-023)

**External prerequisites / blocked acceptance:** Use recorded approved renderer/template scope; new framework/method versions require methodology approval. Live signing remains separate.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-025"></a>
### AS-PAR-025 — Complete manager completion, summary memorandum and human-owned report decisions

**Story:** As a **manager and engagement partner**, I want to **review the whole current file before authorizing an exact professional conclusion**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** manager and engagement partner.

**Controlling specification:** [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-21/23; P:source §11.3; P:G05–09; P:AT-31/34; P:TR-07.

**Related existing source:** [COMPLETE-UI](#e-complete-ui), [RELEASE](#e-release), [PROGRAM](#e-program), [FINDING-UI](#e-finding-ui), [COMPLETION-DOM](#e-completion-dom).

**Exact gap / evidence limit:** The completion screen displays persisted gates and package reviews but does not establish a complete manager summary/report-opinion workflow or prove every professional blocker is recomputed through one authoritative release decision.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-31](#p-at-31):** No audit report issuance; responsibilities remain separate

> **[P:AT-34](#p-at-34):** Explicit pending/rework state; no manufactured zero values or automatic opinion

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-025-AC01 | senior-cleared work is ready for manager review | a completion decision is submitted | check signed scope, current strategy/materiality, all applicable procedures/risks, reconciliations, significant findings, unadjusted differences, required consultations and current disclosure/going-concern/subsequent-event conclusions |
| AS-PAR-025-AC02 | the manager prepares the summary memorandum | it is reviewed | retain work performed, significant judgments, evidence links, remaining matters/dispositions and exact TB/mapping/workpaper/package versions; budget pressure never waives necessary work |
| AS-PAR-025-AC03 | the partner proposes a report conclusion | the report is saved | require the approved engagement/framework template, human-selected conclusion, grounds/references, required report sections, signer authority and exact financial package |
| AS-PAR-025-AC04 | a mandatory gate is absent, stale, merely a checkbox or represented by an unsupported legacy status | release readiness is checked | return explicit blocking reasons; do not regard no recorded work or no recorded review notes as proof of completed professional work |
| AS-PAR-025-AC05 | a manager or partner returns part of the file | rework is assigned | preserve history, route precise affected dependencies and repeat all required impacted reviews including management where accounts changed |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Add missing typed summary/report version and completion decision manifests in existing Audit/Completion modules; do not redefine package approval records. |
| Application services | Use the same authoritative completion/readiness evaluation for UI and ReleaseService, with no bypass through a generic status update. |
| Blazor / user interaction | Create manager and partner queues, typed memorandum/report editors and an explainable readiness checklist linked to exact evidence. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Gate-by-gate absent/stale/positive tests, dependency-return tests and package-version binding; proposed senior→manager→partner browser flow with rejection branches.

**Existing test seams:** [T-ReleaseTests](#e-t-releasetests), [T-ReleaseEvidenceTests](#e-t-releaseevidencetests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-004](#as-par-004), [AS-PAR-017](#as-par-017), [AS-PAR-021](#as-par-021), [AS-PAR-023](#as-par-023), [AS-PAR-024](#as-par-024)

**External prerequisites / blocked acceptance:** Named manager/partner and methodology owner approve professional content/report templates and the report date; no automated opinion or backdating.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-026"></a>
### AS-PAR-026 — Implement independent EQR eligibility, concerns and completion

**Story:** As a **engagement quality reviewer**, I want to **evaluate significant judgments independently before any required audit release**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** engagement quality reviewer.

**Controlling specification:** [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-23; P:G09; P:AT-32; P:eqr.complete; P:roles eqr.

**Related existing source:** [COMPLETION-DOM](#e-completion-dom), [COMPLETE-UI](#e-complete-ui), [RELEASE](#e-release), [AUTH](#e-auth).

**Exact gap / evidence limit:** EqrCase stores status/notes/assigned reviewer and the completion page displays status. A complete eligible reviewer, concern/response, exact-package and renewed-review workflow is not established by that model.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-32](#p-at-32):** Issue blocked until eligible quality reviewer completes required decision

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-026-AC01 | firm policy requires an EQR | a reviewer is appointed | retain applicability and eligibility evidence; reject the engagement partner/preparer or an unqualified/ineligible assignment for the same case |
| AS-PAR-026-AC02 | an assigned EQR opens the case | evidence is loaded | show only its authorized significant judgments and exact package/workpaper versions, not a general client portfolio |
| AS-PAR-026-AC03 | the reviewer raises a concern | the engagement team responds | retain separate concern, response, evidence and independent disposition history; the EQR does not prepare the team’s work or silently clear unresolved matters |
| AS-PAR-026-AC04 | all required concerns are resolved on current evidence | EQR completion is submitted | append the reviewer’s exact-manifest decision and rationale; it does not replace management approval or partner responsibility |
| AS-PAR-026-AC05 | a relevant package or judgment changes after completion | release is attempted | require a documented impact assessment and renewed affected EQR review; required-but-incomplete EQR blocks issue |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Extend EqrCase with exact assignment/eligibility/version manifest; proposed EqrConcern/Response/Decision records are additive and append-only where evidential. |
| Application services | Add scoped EQR commands in the existing Completion module and integrate their freshness into ReleaseService; do not use mutable Status alone as gate evidence. |
| Blazor / user interaction | Provide dedicated quality-review queue, case, concerns, significant-judgment references and renewed-review state. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Self/team-review denials, unauthorized portfolio access, unresolved/stale concern blockers, revised-package invalidation and independent EQR E2E. Entity-count tests alone are insufficient.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ReleaseTests](#e-t-releasetests), [T-AuthorizationDecisionTests](#e-t-authorizationdecisiontests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** Quality-management owner determines applicability/eligibility and supplies approved methodology; reviewer professional concurrence cannot be supplied by software.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-027"></a>
### AS-PAR-027 — Complete version-bound representations and approved signing preparation

**Story:** As a **authorized management signatory and partner**, I want to **provide required representations tied to the exact final package and preserve genuine signing evidence**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** authorized management signatory and partner.

**Controlling specification:** [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-06/22/23; P:G08/09; P:GOV-03; P:AT-31/36.

**Related existing source:** [COMPLETION-DOM](#e-completion-dom), [COMPLETE-UI](#e-complete-ui), [MGMT-UI](#e-mgmt-ui), [RELEASE-SAFETY](#e-release-safety).

**Exact gap / evidence limit:** WrittenRepresentation stores a Boolean and signatory name but no explicit package/hash fields in the inspected model. SignatureLineage is a foundation, not proof of implemented legally approved signing.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-027-AC01 | a representation is required by the selected profile | a draft is created | bind approved wording/version, entity/engagement/period, exact final package and required signatory authority |
| AS-PAR-027-AC02 | management signs or provides an approved manually verified representation | receipt is reviewed | preserve original bytes/hash, identity/intent/authority evidence, actual dates, method and verifier; a typed name or image is not automatically a verified signature |
| AS-PAR-027-AC03 | the financial package changes | the old representation is evaluated | retain it as historical evidence and require the appropriate revised representation/approval rather than copying Obtained=true |
| AS-PAR-027-AC04 | signature preparation is authorized | a signing request is created | bind pre-sign artifact, request identity and approved custody/method; provider or manual verification must record exact signed bytes and evidence |
| AS-PAR-027-AC05 | required representation/signature evidence is missing or unverified | release is attempted | block that profile; simulation, a downloaded PDF or a template stamp must not satisfy signing acceptance |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Add missing representation revision, package/artifact/hash and authority references; preserve unproven historical records accurately. Reuse SignatureLineage rather than create a second signing table. |
| Application services | Add guarded representation draft/request/verify/revise commands in Completion and integrate with existing release safety checks. |
| Blazor / user interaction | Provide client representation review and staff verification screens with a clear distinction between requested, received and verified. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Wrong-version/wrong-signatory, changed-package and hash mismatch tests locally; real signing/custody proof only on an authorized acceptance runner.

**Existing test seams:** [T-ReleaseEvidenceTests](#e-t-releaseevidencetests), [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-DocumentSnapshotTests](#e-t-documentsnapshottests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-015](#as-par-015), [AS-PAR-024](#as-par-024), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** P5: approved signing methodology/custody/verifier and authorized signatories; no particular external e-sign vendor is selected here.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-028"></a>
### AS-PAR-028 — Complete release, dispatch, recipient acknowledgement and re-delivery

**Story:** As a **partner and records dispatcher**, I want to **deliver only the authorized immutable content with truthful external outcome states**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** partner and records dispatcher.

**Controlling specification:** [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§12](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s12), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-24; P:G09/10; P:AT-31/35/36; P:release.issue/dispatch.

**Related existing source:** [RELEASE](#e-release), [RELEASE-UI](#e-release-ui), [CHECKPOINT](#e-checkpoint), [WORKER](#e-worker), [COMPLETE-UI](#e-complete-ui).

**Exact gap / evidence limit:** Local candidate, exact-package review and checkpoint safeguards exist. General live provider composition, independent checkpoint acceptance and the complete deliverable/acknowledgement journey remain gated.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-35](#p-at-35):** Sent/delivered/submitted states do not become acknowledged/filing-accepted automatically

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-028-AC01 | all required current reviews, EQR, representations and signing/protection evidence exist | issue authorization runs | freeze the exact manifest and authorized recipients; recompute current gates and refuse mismatched bytes or expired authority |
| AS-PAR-028-AC02 | dispatch is retried or the worker crashes after a possible external effect | recovery executes | reconcile using the existing durable-operation identity, retain uncertain outcomes and never substitute another artifact or claim exactly-once email delivery |
| AS-PAR-028-AC03 | a provider accepts dispatch | status is displayed | distinguish queued, sent, delivered if observed, viewed if supported and acknowledged; a dispatch success is not client receipt or filing acceptance |
| AS-PAR-028-AC04 | a client follows a deliverable link | download or acknowledgement occurs | recheck exact scope/recipient permission, show immutable package identity, and append a receipt acknowledgement without granting approval or internal workpaper access |
| AS-PAR-028-AC05 | only recipients or channel change | redispatch is requested | require new recipient/channel authority while preserving signed content and original issue event; content correction routes through controlled reissue |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse ReleaseCandidate/Release/Checkpoint/durable operations; add missing recipient/dispatch/acknowledgement evidence structures, not mutable sent flags as proof. |
| Application services | Extend existing ReleaseService and handler composition with current authorization and proof-based reconciliation. Independent checkpoint store must not share the same untrusted writable custody. |
| Blazor / user interaction | Add release readiness, dispatch history, uncertainty handling and client deliverables/acknowledgement pages. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Local manifest/gate/retry tests plus independent-checkpoint mismatch/readback and authorized tenant dispatch tests; redacted provider receipts required.

**Existing test seams:** [T-ReleaseTests](#e-t-releasetests), [T-ReleaseEvidenceTests](#e-t-releaseevidencetests), [T-DurableOutboxTests](#e-t-durableoutboxtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-024](#as-par-024), [AS-PAR-025](#as-par-025), [AS-PAR-026](#as-par-026), [AS-PAR-027](#as-par-027)

**External prerequisites / blocked acceptance:** P2/P3/P4/P5 and approved general provider composition; local signing/records/checkpoint simulations cannot satisfy live issue acceptance.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-029"></a>
### AS-PAR-029 — Complete commercial exceptions, WIP, expenses and collection workflows

**Story:** As a **billing officer and commercial approver**, I want to **manage fees independently of professional conclusions and client books**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** billing officer and commercial approver.

**Controlling specification:** [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:BIL-01–04; P:AT-19/20/21/38; P:invoice.draft/review/issue; P:receipt.record; P:time.record/review.

**Related existing source:** [BILL](#e-bill), [LEDGER](#e-ledger), [TIME](#e-time), [INVOICE-UI](#e-invoice-ui), [FINANCE-UI](#e-finance-ui), [TIME-UI](#e-time-ui).

**Exact gap / evidence limit:** Invoices, credits, receipts, allocations, time and the bounded firm ledger exist. The complete billing-officer queue, fee exception approvals, recoverable expenses, WIP and collection/settlement journeys require delta verification.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-20](#p-at-20):** All lines and event committed once, or none; retry identifies committed result

> **[P:AT-21](#p-at-21):** Rejected unless an authorized reopen/new-period correction route is used

> **[P:AT-38](#p-at-38):** Office AR/revenue posted only; client books unaffected unless separately authorized

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-029-AC01 | signed fee arrangements and approved time/expenses exist | a billing draft is prepared | retain firm-office entity, bill-to client/contact, service scope, fee model/currency/tax profile/due date and source evidence; schedule only approved retainer/recurring/milestone/time-based billing |
| AS-PAR-029-AC02 | a preparer requests a discount, credit, write-off, refund or fee change | a commercial reviewer decides | apply separately approved limits/reasons and maker-checker separation; no unauthenticated callback or mutable UI state posts it |
| AS-PAR-029-AC03 | an approved invoice posts or a receipt allocates | the transaction is retried | preserve existing balanced/immutable posting and bounded-allocation controls, source keys and reversal lineage; confirm settlement separately from a reported gateway outcome |
| AS-PAR-029-AC04 | a staff member edits approved time or a receipt-backed expense | correction occurs | use the existing approved correction chain or a documented additive equivalent, retaining the rate/amount snapshot and source receipt |
| AS-PAR-029-AC05 | fees remain unpaid or are written off | professional readiness is evaluated | do not alter an audit opinion, clear an evidence gate or delete an engagement file; any permitted commercial hold has separately approved scope |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Extend current Practice billing/time models only for proven missing expense/schedule/exception evidence; do not put office revenue into client books. |
| Application services | Use BillingService/LedgerService/PracticeTimeService and scoped projections. A payment gateway is optional and not introduced by this story. |
| Blazor / user interaction | Add draft/review/issue and exception queues, WIP/ageing/collection views, expenses and independent approval handoff; remove any broad read fallback discovered under AS-PAR-002. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse existing financial integrity tests; add missing exception, settlement, office/client separation and expense cases; billing/preparer/approver browser journey.

**Existing test seams:** [T-BillingTests](#e-t-billingtests), [T-LedgerTests](#e-t-ledgertests), [T-PracticeTimeTests](#e-t-practicetimetests), [T-RouteCatalogTests](#e-t-routecatalogtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-012](#as-par-012)

**External prerequisites / blocked acceptance:** Commercial/finance owner approves fee limits, tax profile and collection policy; real bank/gateway settlement requires separate authorized evidence.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-030"></a>
### AS-PAR-030 — Complete records administration, assembly, holds and controlled disposal

**Story:** As a **records administrator**, I want to **preserve the issued file and execute only separately approved records actions**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** records administrator.

**Controlling specification:** [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§30](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s30), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-25/26; P:G11/12; P:AT-40/41; P:NFR-08/10; P:archive.assemble/hold.record/disposal.request.

**Related existing source:** [ARCHIVE-UI](#e-archive-ui), [RECORDS](#e-records), [COMPLETION-DOM](#e-completion-dom), [TENANT](#e-tenant), [RESTORE](#e-restore).

**Exact gap / evidence limit:** Archive manifests/lineage and local hold/protection states are substantial. The records persona’s assembly/disposition/handover interface and positive live Purview behavior are not complete; requested label is not observed protection.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-40](#p-at-40):** Disposal blocked and logged; hold release requires authorized decision

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-030-AC01 | an issued engagement is ready for administrative assembly | records staff build a manifest | verify workpaper index, required source/TB/AJE/review/communication/delivery lineage and approved classification/profile before recording assembly completion |
| AS-PAR-030-AC02 | a label or protection action is requested | the operation updates | retain desired and observed states separately; no local LOCKED value, requested action or enabled publication policy proves record protection |
| AS-PAR-030-AC03 | a hold instruction is recorded or retention expires | disposition is requested | validate scope, instruction authority, policy version/start event and expiry; active holds block disposal and cannot be auto-released |
| AS-PAR-030-AC04 | a separately authorized disposition is executed | completion is recorded | retain minimal certificate and exact target/evidence scope, handle derived caches and restored-data suppression per approved policy, and never equate termination with deletion |
| AS-PAR-030-AC05 | additional material is added after assembly or an archive is reassembled | a new manifest is produced | preserve original bytes/version, reason/actor/reviewer and predecessor/supersession lineage; no backdated evidence or silent rewrite |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse RecordsProfile, ArchiveManifest/Entry, RecordsAction and LegalHold with existing append-only lineage. Add missing request/approval/certificate records only if required. |
| Application services | Extend RecordsArchiveService and guarded records worker actions; local hold decision and provider enforcement must remain distinct. |
| Blazor / user interaction | Add records queue, assembly checklist, requested-versus-observed protection, hold/disposition requests and scoped archive inspection. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Local profile/hold/manifest regressions and restore checks; live label readback and approved edit/delete/hold tests only with authorized synthetic targets and reviewed evidence.

**Existing test seams:** [T-RecordsArchiveTests](#e-t-recordsarchivetests), [T-ReleaseEvidenceTests](#e-t-releaseevidencetests), [T-OperationRecoveryTests](#e-t-operationrecoverytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-019](#as-par-019), [AS-PAR-028](#as-par-028)

**External prerequisites / blocked acceptance:** P4: approved Purview profile/license/record reviewers and target-specific protected behavior tests. Destructive disposition requires explicit target/environment authorization, not this backlog.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-031"></a>
### AS-PAR-031 — Implement controlled reissue, suspension, termination and handover

**Story:** As a **partner, records administrator and client administrator**, I want to **change or end a service without losing evidence or leaving unauthorized access active**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** partner, records administrator and client administrator.

**Controlling specification:** [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§22](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s22), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§34](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s34), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-26; P:G12; P:AT-37/41; P:handover.request; P:TR-10.

**Related existing source:** [RELEASE](#e-release), [RECORDS](#e-records), [RESTATE-UI](#e-restate-ui), [ROLL-UI](#e-roll-ui), [ROLES](#e-roles), [OPS-UI](#e-ops-ui).

**Exact gap / evidence limit:** Period restatement, archive lineage and revocation primitives exist. A complete relationship/service suspension, post-issue assessment, authorized export and scheduled offboarding workflow is not proven by those primitives.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-37](#p-at-37):** Authorized reason, new versions, impact review and replacement notices; original package retained

> **[P:AT-41](#p-at-41):** Scoped approved export, receipt and access revocation; retained professional evidence preserved

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-031-AC01 | a fact is discovered after issue | a partner approves a scoped time-limited reopening | retain original issue, reason, affected entity/period/package and permitted actions; create new revisions and repeat affected senior/manager/management/partner/EQR decisions |
| AS-PAR-031-AC02 | a replacement is authorized | it is issued | retain discoverable superseded copies and a new release/delivery or filing link; do not replace bytes under an old version |
| AS-PAR-031-AC03 | one service is suspended | the instruction takes effect | block only authorized scope, retain contractual/legal deadlines and required record access, and require current acceptance/terms/restart conditions before resuming |
| AS-PAR-031-AC04 | termination/handover is requested | the partner and records owner approve the plan | separate client-owned books/documents from firm workpapers, define recipient/manifest/receipt, outstanding deadlines and independent commercial treatment |
| AS-PAR-031-AC05 | the approved effective time arrives | offboarding runs or retries | revoke corresponding portal/API grants, capabilities, future invitations/jobs and provider access where applicable without deleting historical attribution or legally retained records |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Add missing scoped post-issue assessment, suspension/offboarding plan and handover manifest/decision records in existing modules. Reuse release supersession and role revocation data. |
| Application services | Compose existing guarded services through durable operations; each external revocation has its own observed result and uncertainty state. |
| Blazor / user interaction | Provide reissue impact preview, restart checklist and handover/access status, not a destructive “delete client” shortcut. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Original artifact remains unchanged, stale approvals cannot release replacement, service-specific hold, retry/effective-date revocation and unauthorized full-file export tests; later offboarding E2E.

**Existing test seams:** [T-ReleaseTests](#e-t-releasetests), [T-RecordsArchiveTests](#e-t-recordsarchivetests), [T-RoleAdministrationTests](#e-t-roleadministrationtests), [T-OperationRecoveryTests](#e-t-operationrecoverytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-011](#as-par-011), [AS-PAR-028](#as-par-028), [AS-PAR-030](#as-par-030)

**External prerequisites / blocked acceptance:** Partner/records/data owners approve scope, recipients and retention. Live revocation, external handover and destructive actions require their own authorization.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-032"></a>
### AS-PAR-032 — Complete resumable M365 setup and accepted-client workspace provisioning handoff

**Story:** As a **system administrator and onboarding coordinator**, I want to **configure approved resources and know whether a client workspace actually exists**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** system administrator and onboarding coordinator.

**Controlling specification:** [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§9](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s09), [§10](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s10), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45). All work also follows S:§§44–47.

**Prototype requirements:** P:SET-01/04/05; P:LC-07/09; P:API-06/07; P:system.configure.

**Related existing source:** [SETUP-UI](#e-setup-ui), [ADMIN-UI](#e-admin-ui), [ROLES](#e-roles), [ACCEPT](#e-accept), [OLD-M365](#e-old-m365), [CONFIG](#e-config), [PENDING](#e-pending).

**Exact gap / evidence limit:** Bootstrap/resume, exact immutable identity assignment, consent/resource revisions, folder templates and acceptance-created WAITING_FOR_INTEGRATION intent are already implemented locally. The missing portion is approved provider activation/provisioning and complete observable setup handoff.

**Exact prototype permission acceptance anchor:** `system.configure` — “Approved configuration and separate change control; no live secrets in demo.” ([P-PERM](#e-p-perm)).

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-032-AC01 | an installation requires configuration | an authorized administrator claims or resumes setup | reuse short-lived hashed bootstrap capability and non-secret draft state, show missing prerequisites and prevent last-administrator loss |
| AS-PAR-032-AC02 | tenant/site/library/root and folder templates are selected | configuration is activated | require exact approved revision, explicit resource consent/grant evidence and approved immutable allowlisted template; a guessed URL or merely present credential cannot mark READY |
| AS-PAR-032-AC03 | a client is accepted before integration is ready | the acceptance commits | retain one waiting client-root intent without calling SharePoint in the request or fabricating a URL |
| AS-PAR-032-AC04 | the approved provider later provisions the client and engagement folders | verification finishes | reconcile deterministic logical keys with exact remote item IDs and expose a link only after observed success; retries create no duplicate roots |
| AS-PAR-032-AC05 | optional mail or records setup is absent | the wizard is completed locally | show NOT_CONFIGURED and the resulting blocked capabilities; do not fabricate a mailbox, label, site or secret |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse connection revisions, ClientWorkspace, templates and role evidence from the existing M365 story; no duplicate setup state machine. |
| Application services | Complete missing integration handoffs through current durable operations and existing acceptance intent; keep external effects out of acceptance transactions. |
| Blazor / user interaction | Extend existing setup/admin/client workspace screens with explicit consent/provisioning/readback status, retry and non-disclosing error guidance. |
| Infrastructure / configuration | Use only current setup/identity/connection and worker keys listed in the configuration register. Do not add a temporary broad permission or fake tenant variable. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Existing onboarding tests plus resume/conflict/duplicate intent/changed consent revision tests; approved live provisioning proof is a separate evidence entry.

**Existing test seams:** [T-Microsoft365OnboardingTests](#e-t-microsoft365onboardingtests), [T-Microsoft365AccessTests](#e-t-microsoft365accesstests), [T-RoleAdministrationTests](#e-t-roleadministrationtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010)

**External prerequisites / blocked acceptance:** Approved tenant/app/resource owner decisions and AS-PAR-033/034 are required for live provisioning; recorded local setup is not live Graph acceptance.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-033"></a>
### AS-PAR-033 — Compose and verify the selected-resource document provider

**Story:** As a **integration engineer and document owner**, I want to **perform bounded SharePoint operations with verified bytes, isolation and recovery behavior**.

**Baseline:** `blocked` · **Proposed priority:** `P1` · **Acceptance owner:** integration engineer and document owner.

**Controlling specification:** [§9](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s09), [§10](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s10), [§11](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s11), [§12](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s12), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§39](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s39), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45). All work also follows S:§§44–47.

**Prototype requirements:** P:ARC-07/10/13; P:API-05–07; P:AT-12/39; P:NFR-03/07.

**Related existing source:** [WEB-HOST](#e-web-host), [WORKER](#e-worker), [DOC-DOM](#e-doc-dom), [OLD-M365](#e-old-m365), [TENANT](#e-tenant), [PENDING](#e-pending).

**Exact gap / evidence limit:** The runtime deliberately refuses unapproved general external effects. Existing local/simulation/provider boundaries and partial tenant setup do not constitute an accepted live SharePoint provider composition.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-39](#p-at-39):** New action denied/stopped; duplicate callback not reapplied; authorized retry/manual route visible

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-033-AC01 | the authorized tenant has consent, explicit selected-resource grants and approved credential custody | a provider capability test runs | verify each required create/read/upload/download/version/snapshot/folder endpoint against the exact allowed resource and denied sibling resource; no tenant-wide fallback |
| AS-PAR-033-AC02 | a PBC or workpaper file is transferred | the operation reconciles | bind logical document ID, request/version, provider item/version, byte count and independently checked hash; do not claim receipt after staging only |
| AS-PAR-033-AC03 | a timeout occurs after a possible write | the worker resumes | use deterministic request identity and remote readback, distinguishing completed, retryable, blocked and uncertain outcomes without blind duplicate upload |
| AS-PAR-033-AC04 | a token/grant is revoked or the source version changes | a new operation or release readback occurs | deny new effects, revalidate current scope and mark affected dependencies stale; historical snapshots remain available under their retained authorization |
| AS-PAR-033-AC05 | webhook notifications are missing or an incremental cursor expires | reconciliation runs | use the approved bounded polling/delta/full-resync path, expose last successful reconciliation and never rely on webhook delivery alone |
| AS-PAR-033-AC06 | Office collaboration is requested | a current document is opened | use approved separate-tab Microsoft access; do not introduce embedded editing, application Excel-session assumptions or cell-level sync |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse RepositoryBinding, DocumentRef/snapshots, capabilities, cursors and durable-operation evidence. Add only endpoint-specific missing version/proof fields. |
| Application services | Implement/complete adapters behind existing interfaces and composition roots, not inside Razor or a competing document database. |
| Blazor / user interaction | Expose provider mode, missing grants, sync health, reconciliation outcomes and exact current/submitted document actions. |
| Infrastructure / configuration | No exact new provider configuration keys are invented. Derive any missing option from the selected existing interface in an approved implementation issue; keep all values outside Git. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** ProviderContract tests with no network first; then separately authorized TenantIntegration scenarios with allowed/denied resources, hashes/version readbacks and uncertain-outcome proof. Local simulation cannot close P2.

**Existing test seams:** [T-ProviderBoundaryTests](#e-t-providerboundarytests), [T-PbcTransferTests](#e-t-pbctransfertests), [T-DocumentSnapshotTests](#e-t-documentsnapshottests), [T-DurableOutboxTests](#e-t-durableoutboxtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-002](#as-par-002), [AS-PAR-019](#as-par-019), [AS-PAR-032](#as-par-032)

**External prerequisites / blocked acceptance:** P2: authorized non-production tenant, exact site/library/root, selected grants, short-lived or approved certificate-based credentials, approved runner and destructive-target limits. Routine permissions must stay bounded.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-034"></a>
### AS-PAR-034 — Verify live identity, revocation and production actor resolution

**Story:** As a **security owner**, I want to **prove that only approved active tenant identities receive current scoped application access**.

**Baseline:** `blocked` · **Proposed priority:** `P1` · **Acceptance owner:** security owner.

**Controlling specification:** [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§8](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s08), [§39](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s39), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45). All work also follows S:§§44–47.

**Prototype requirements:** P:NFR-02; P:GOV-04; P:API-06; P:AT-07/39.

**Related existing source:** [WEB-HOST](#e-web-host), [ACTOR](#e-actor), [ROLES](#e-roles), [CONFIG](#e-config), [PENDING](#e-pending).

**Exact gap / evidence limit:** OIDC and immutable actor mapping are coded, while recorded live identity acceptance is still blocked. Development identity and roster operations are not evidence of live directory/Conditional Access behavior.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-034-AC01 | an approved staff or invited client authenticates | OIDC returns a principal | validate tenant and immutable object identity, bind the existing AppUser and current scoped grants, and reject missing/forged/wrong-tenant identity |
| AS-PAR-034-AC02 | a user is disabled, removed or revoked after login | a new protected action or download is requested | deny through the current session-epoch/grant checks and satisfy the owner-approved revocation policy across open circuits and queued work |
| AS-PAR-034-AC03 | a client has a verified contact but no signatory authority | they authenticate | allow only their assigned contributor/admin capabilities, not management approval |
| AS-PAR-034-AC04 | production startup is requested with development identity or simulation enabled | composition validation runs | refuse startup or the prohibited capability; there is no hidden debug-login route or client-selected role header |
| AS-PAR-034-AC05 | live identity evidence cannot be obtained | the acceptance report is written | record BLOCKED with the missing fixture/authority rather than copying a local identity test into the tenant evidence column |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse existing user/identity/session/grant/observation models; no new authentication database or shared browser superuser. |
| Application services | Keep trusted actor resolution independent from UI persona selection; add only proven missing lifecycle verification/revalidation behavior. |
| Blazor / user interaction | Use configuration-required/access-not-assigned pages for missing grants and clear reauthentication guidance without identifying another user/client. |
| Infrastructure / configuration | Reuse Identity:TenantId/ClientId/ClientSecret/CallbackPath and DevelopmentIdentity controls as currently implemented. Production credential method/custody is an approved deployment decision. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Local token/actor/epoch negatives plus owner-authorized sign-in, disabled/revoked and cross-tenant acceptance evidence; do not trigger real account changes without separate authorization.

**Existing test seams:** [T-Microsoft365AccessTests](#e-t-microsoft365accesstests), [T-RoleAdministrationTests](#e-t-roleadministrationtests), [T-AuthorizationDecisionTests](#e-t-authorizationdecisiontests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-003](#as-par-003), [AS-PAR-032](#as-par-032)

**External prerequisites / blocked acceptance:** P1: tenant identity owner approval, permitted staff/client/wrong-tenant/disabled fixtures, approved MFA/session policy and test runner; no tenant IDs or real accounts invented.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-035"></a>
### AS-PAR-035 — Accept one existing notification provider and complete truthful message outcomes

**Story:** As a **operations owner and client recipient**, I want to **receive safe notifications with distinct queue, transmission and receipt states**.

**Baseline:** `blocked` · **Proposed priority:** `P1` · **Acceptance owner:** operations owner and client recipient.

**Controlling specification:** [§12](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s12), [§15](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s15), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45). All work also follows S:§§44–47.

**Prototype requirements:** P:AUT-02–05; P:WF-03; P:API-04/05; P:AT-35/39/45.

**Related existing source:** [WORKER](#e-worker), [PBC-UI](#e-pbc-ui), [PBC-CLIENT](#e-pbc-client), [PENDING](#e-pending).

**Exact gap / evidence limit:** Graph, SMTP and Resend mail adapters are already composed for an isolated Acceptance/mail worker. A production-composed and accepted general notification path is not implied by these adapters or queued UI status.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-035-AC01 | a business transaction commits a notification request | the worker handles it | load current recipient/entity permission and the approved template/provider, send only minimal reference/action/deadline/authenticated-link content and record attempt identity |
| AS-PAR-035-AC02 | mail fails after a successful business decision | status is projected | retain the business decision and show pending/failed/uncertain delivery; never roll it back or claim receipt |
| AS-PAR-035-AC03 | the chosen provider is configured for Acceptance | a bounded test is authorized | use only its approved synthetic recipient allowlist, sender and credential custody; other providers need not be enabled |
| AS-PAR-035-AC04 | a result or callback repeats or arrives late | the handler processes it | deduplicate and match the originating request/permitted state; no replayed callback repeats a business approval |
| AS-PAR-035-AC05 | production notification activation is requested | release review occurs | require approved composition, least privilege, logging/redaction and live delivery/reconciliation proof; do not claim exactly-once email or that provider acceptance equals read acknowledgement |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse PbcCommunication and durable operation/attempt/event records; add observed-provider metadata only if required by the accepted adapter contract. |
| Application services | Extend current PbcMailDeliveryHandler/discovery and existing adapters where a specific gap is proven; do not introduce another mail provider. |
| Blazor / user interaction | Display queued/sent/failed/uncertain distinctly and preserve reply/thread history; invitations and document requests remain separate from access grants. |
| Infrastructure / configuration | Preserve current Acceptance + ExternalEffects:Enabled + Worker:Group=mail boundary. Current provider-specific key names are listed later; no values are supplied. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Local payload/redaction/retry tests, then isolated approved Acceptance mail proof. Production acceptance requires a separately approved composition, not merely flipping an environment flag.

**Existing test seams:** [T-PbcTests](#e-t-pbctests), [T-DurableOutboxTests](#e-t-durableoutboxtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-013](#as-par-013), [AS-PAR-034](#as-par-034)

**External prerequisites / blocked acceptance:** Approved mail runner, sender, restricted recipients and selection of GRAPH, SMTP or RESEND. No new vendor account or subscription is required by this document.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-036"></a>
### AS-PAR-036 — Complete versioned firm policy, calendars, obligations and recurrence

**Story:** As a **policy owner and operations coordinator**, I want to **apply approved effective rules without hard-coded legal defaults or duplicated reminders**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** policy owner and operations coordinator.

**Controlling specification:** [§13](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s13), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:SET-01–05; P:AUT-01–06; P:WF-03–05; P:NFR-12; P:AT-09/45/46.

**Related existing source:** [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui), [TIME](#e-time), [WORKER](#e-worker), [OLD-M365](#e-old-m365).

**Exact gap / evidence limit:** Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-45](#p-at-45):** Occurrence deduplication and safe recovery; no duplicate professional decisions or filings

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-036-AC01 | a firm policy/template is drafted | the owner publishes it | record scope, version, author/reviewer, effective interval and approval; existing engagements retain their selected version unless an authorized impact review adopts the change |
| AS-PAR-036-AC02 | mandatory firm/accounting configuration is absent | a dependent workflow starts | return visible NOT_CONFIGURED/blocked requirements rather than inventing currency policy, tax rates, thresholds, retention, fee limits or report methodology |
| AS-PAR-036-AC03 | a calendar/deadline rule is evaluated | tasks and reminders are scheduled | distinguish service targets from statutory deadlines, record timezone/working calendar/holiday/absence/extension evidence and responsible delegates |
| AS-PAR-036-AC04 | the same schedule fires twice or crashes midway | the worker retries | use a scoped occurrence key and current policy/permission checks; preserve attempts and create no duplicate business decision, filing or invoice |
| AS-PAR-036-AC05 | a new jurisdiction/service/optional connector is requested | activation is considered | require named owner approval and supported capability evidence; disabled integrations do not block an explicitly supported manual route or silently authorize external effects |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Add only missing versioned policy/calendar/obligation/occurrence records to current modules; bounded explicit predicates, not executable custom scripts or a generalized workflow framework. |
| Application services | Reuse durable outbox leasing/retry and existing template approval patterns. Domain events carry aggregate/version/scope/actor/correlation references and are evaluated against persisted state. |
| Blazor / user interaction | Add configuration/version impact, recurring due/overdue queues, missing-policy explanations and human rule disposition. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Effective-date/holiday/zero-base/duplicate occurrence/crash/revocation tests; policy update must not change issued artifacts or auto-approve current work.

**Existing test seams:** [T-DurableOutboxTests](#e-t-durableoutboxtests), [T-PracticeTimeTests](#e-t-practicetimetests), [T-Microsoft365OnboardingTests](#e-t-microsoft365onboardingtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-001](#as-par-001), [AS-PAR-003](#as-par-003), [AS-PAR-011](#as-par-011), [AS-PAR-012](#as-par-012)

**External prerequisites / blocked acceptance:** Professional, jurisdiction, records and finance owners approve policy values. Proposed operational defaults are not statutory obligations or measured SLA evidence.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-037"></a>
### AS-PAR-037 — Complete financial and operational reporting with consistent measures

**Story:** As a **manager, finance officer and authorized client**, I want to **see accurate scoped progress and trace every displayed amount or status**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** manager, finance officer and authorized client.

**Controlling specification:** [§16](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s16), [§17](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s17), [§18](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s18), [§23](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s23), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§41](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s41), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44). All work also follows S:§§44–47.

**Prototype requirements:** P:REP-01–04; P:UX-01–03; P:OBJ-04; P:AT-23/33.

**Related existing source:** [PORTFOLIO](#e-portfolio), [ACC-UI](#e-acc-ui), [EVID-UI](#e-evid-ui), [FS-UI](#e-fs-ui), [TIME-UI](#e-time-ui), [FINANCE-UI](#e-finance-ui), [OPS-UI](#e-ops-ui).

**Exact gap / evidence limit:** Portfolio and accounting projections exist. The full operational catalogue and all scoped report/filter/drill-down/export acceptance paths are not established by those screens.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-037-AC01 | a report is requested | parameters are applied | require authorized entity/group, book, period, source/adjusted/issued version, basis, currency and mapping/comparison context; missing scope must never mean all clients |
| AS-PAR-037-AC02 | an operational dashboard shows completion or overdue counts | a user drills down | use documented measure definitions and the same authorized underlying query; checked tasks do not count as independent clearance or issued delivery |
| AS-PAR-037-AC03 | the financial report catalogue is traversed | an enabled report runs | provide TB/journals/GL/statements/equity/cash flow/comparatives/account history/AR-AP ageing/reconciliation/budget outputs where supported, and explicit gaps otherwise |
| AS-PAR-037-AC04 | the operational catalogue is traversed | a view runs | cover lead conversion, acceptance/continuance, PBC, engagement/review/differences, staffing/time/WIP/billing, filing, completion and retention due within the actor’s remit |
| AS-PAR-037-AC05 | the same report exports to supported formats | results are compared | totals, source versions and lineage agree; metadata identifies generation time/version and an editable export is not mislabeled an issued artifact |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Prefer projections and narrowly scoped indexes to duplicated aggregates; any cached key must include scope, relevant revision/generation and permission context. |
| Application services | Reuse current accounting/portfolio/finance queries and renderer calculations; do not calculate approved balances in browser components. |
| Blazor / user interaction | Add missing catalogue links, parameter banners, semantic completion statuses and amount/status drill-down; hide unsupported transitions truthfully. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Parameterized scope/filter/export parity tests and documented metric fixtures; browser drill-down and inaccessible-count negatives. Reference workloads are approved separately.

**Existing test seams:** [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-FinancialStatementTests](#e-t-financialstatementtests), [T-AccountingBenchmarkTests](#e-t-accountingbenchmarktests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-002](#as-par-002), [AS-PAR-005](#as-par-005), [AS-PAR-022](#as-par-022), [AS-PAR-029](#as-par-029), [AS-PAR-036](#as-par-036)

**External prerequisites / blocked acceptance:** None for isolated local implementation; release acceptance still follows the applicable external gates.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-038"></a>
### AS-PAR-038 — Complete migration attribution, historical evidence and controlled cutover

**Story:** As a **data owner and release authority**, I want to **move approved data without guessing ownership or rewriting past approvals**.

**Baseline:** `blocked` · **Proposed priority:** `P1` · **Acceptance owner:** data owner and release authority.

**Controlling specification:** [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§34](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s34), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45), [§46](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s46). All work also follows S:§§44–47.

**Prototype requirements:** P:TR-03–13; P:DATA-05/08; P:AT-42/43; P:source §19.4.

**Related existing source:** [OLD-AUD](#e-old-aud), [OLD-ACC](#e-old-acc), [CTX](#e-ctx), [STATUS](#e-status), [RESTORE](#e-restore), [CI](#e-ci).

**Exact gap / evidence limit:** Current migrations and ambiguity quarantine are implemented; an approved full legacy-data mapping, rehearsal and production cutover is not proven by loopback schema tests. No vendor source purchase or data import is authorized by this document.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-42](#p-at-42):** Required schema/reference data established without manual changes; representative workflows pass

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-038-AC01 | an authorized source package is selected | migration is designed | inventory clients/contacts/entities/engagements/periods/financial rows/files/approvals/schedules, source IDs and permitted-use evidence; map each item to the current .NET model |
| AS-PAR-038-AC02 | ownership or historic approval cannot be proven | a migration row is processed | keep it in restricted quarantine with reason and original ID; do not assign a default client, invent signatures, backdate review or label imported work re-performed |
| AS-PAR-038-AC03 | empty and prior supported schemas are upgraded | migrations run | create required tables/reference data without manual edits, preserve append-only constraints and reject a downgrade that destroys evidence |
| AS-PAR-038-AC04 | a full rehearsal completes | reconciliation is reviewed | compare row/file counts, hashes, entity-period/control totals, opening continuity, office balances, contact/grant ownership and unresolved exceptions |
| AS-PAR-038-AC05 | cutover is proposed | release authority decides | approve freeze/delta strategy, backups/keys, exact window, final reconciliation, recovery decision points and first-live checks; retain protected source archive after go-live |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Use additive EF migrations and existing ambiguity quarantine; proposed source-mapping registry records provenance and owner decisions, not arbitrary data ownership. |
| Application services | Implement only scoped idempotent migration adapters necessary for the authorized source, using existing services/invariants where appropriate. |
| Blazor / user interaction | Provide restricted exception review and reconciliation reporting rather than exposing unresolved records to clients. |
| Infrastructure / configuration | Preserve scripts and existing migration history. Any future migration runner/backup change is a separate reviewed implementation task. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Existing migration/quarantine regressions, clean/prior-schema tests and approved rehearsal manifests. Production cutover acceptance requires signed reconciliation and recovery readiness.

**Existing test seams:** [T-AccountingBackfillMigrationTests](#e-t-accountingbackfillmigrationtests), [T-AccountingIntegrityTests](#e-t-accountingintegritytests), [T-OutboxMigrationTests](#e-t-outboxmigrationtests), [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-001](#as-par-001), [AS-PAR-002](#as-par-002)

**External prerequisites / blocked acceptance:** Data owner supplies authorized source bytes/licensing, mapping decisions and rehearsal/cutover approval. No production import, source freeze, rotation or history rewrite is authorized now.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-039"></a>
### AS-PAR-039 — Prove separate-custody recovery and preserve uncertain external outcomes

**Story:** As a **operations and records owners**, I want to **restore coordinated application, documents, keys and release evidence without duplicate effects**.

**Baseline:** `blocked` · **Proposed priority:** `P1` · **Acceptance owner:** operations and records owners.

**Controlling specification:** [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§30](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s30), [§34](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s34), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45). All work also follows S:§§44–47.

**Prototype requirements:** P:ARC-13; P:NFR-04/08; P:AT-43/45; P:TR-11/12.

**Related existing source:** [RESTORE](#e-restore), [DB-RESTORE](#e-db-restore), [RECORDS](#e-records), [RELEASE](#e-release), [CHECKPOINT](#e-checkpoint), [OPS-UI](#e-ops-ui), [CAPACITY](#e-capacity).

**Exact gap / evidence limit:** The loopback restore drill is valuable local evidence, not independent custody or production RPO/RTO. Existing quarantine/reconciliation must not be bypassed after restoring an older database.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-43](#p-at-43):** Counts/hashes/control totals agree, ownership mappings approved, recovery evidence retained

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-039-AC01 | an approved isolated restore drill starts | data stores are restored | include PostgreSQL, preserved documents/manifests, identity/configuration, encryption/Data Protection dependencies and independently administered release checkpoints |
| AS-PAR-039-AC02 | the database is older than a completed external effect | workers restart | enter recovery quarantine, compare independent identities/digests and reconcile before resuming; never blindly replay the restored outbox |
| AS-PAR-039-AC03 | keys are rotated or a historical artifact is encrypted under an older key | recovery verifies access | prove the approved historical decryption/key-version strategy without exposing key material |
| AS-PAR-039-AC04 | counts/hashes or accounting/release identities disagree | validation finishes | record failure/blocked status and keep protected operations disabled; no backup-success flag can substitute for restoration |
| AS-PAR-039-AC05 | approved RPO/RTO targets are measured | owners review the drill | record actual recovery point/time, dependency availability, workload and scope; local timing must not be presented as production compliance |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse RecoverySession/epochs/quarantine, archive and checkpoint evidence; no new parallel recovery state machine. |
| Application services | Reuse existing recovery and reconciliation commands; authority to recover infrastructure does not confer authority to approve financial/professional decisions. |
| Blazor / user interaction | Expose redacted reconciliation status, safe operator actions and explicit resume authority in Operations. |
| Infrastructure / configuration | Inspect current restore script arguments and evidence output paths before execution; never overwrite untracked/private or prior latest-evidence files without a distinct authorization. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Retain loopback regression, then approved cross-store restore with stable manifest/financial totals and no duplicate release/posting. Targets of 15-minute RPO/four-hour RTO remain unproven until observed.

**Existing test seams:** [T-OperationRecoveryTests](#e-t-operationrecoverytests), [T-RecordsArchiveTests](#e-t-recordsarchivetests), [T-ReleaseEvidenceTests](#e-t-releaseevidencetests), [T-DurableOutboxTests](#e-t-durableoutboxtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-028](#as-par-028), [AS-PAR-030](#as-par-030), [AS-PAR-038](#as-par-038)

**External prerequisites / blocked acceptance:** P7: independent backup/checkpoint custody, keys, approved isolated runner/targets, retention/records permissions and owner-authorized recovery exercise.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-040"></a>
### AS-PAR-040 — Add missing CI evidence gates without disturbing current checks

**Story:** As a **technical lead and reviewer**, I want to **obtain reproducible build, database, migration and security evidence for each authorized code change**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** technical lead and reviewer.

**Controlling specification:** [§4](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s04), [§42](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s42), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45), [§46](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s46). All work also follows S:§§44–47.

**Prototype requirements:** P:NFR-06/11/13; P:TR-04; P:AT-42; P:AC-01.

**Related existing source:** [CI](#e-ci), [STATUS](#e-status), [PG](#e-pg), [SPEC](#e-spec).

**Exact gap / evidence limit:** Current CI builds, migrates, tests and probes readiness. It uses postgres:18 rather than an exact 18.6 image and floating major action refs, and does not show a Playwright lane. This story specifies a later reviewed delta only.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-040-AC01 | a future code PR is authorized | CI executes | retain existing required check names/behavior while enforcing locked dependency restore, the approved SDK and PostgreSQL 18.6 test baseline; any pin change is verified rather than guessed |
| AS-PAR-040-AC02 | tests/migrations finish | evidence is collected | retain redacted test results, actual tool/DB versions, clean/prior-schema outcomes and EF model-drift result tied to the exact commit |
| AS-PAR-040-AC03 | dependency/action provenance is reviewed | workflow updates are proposed | select verified full action SHAs/image digests and approved dependency/license/SBOM/security checks in that later PR; no invented hashes or new subscription dependency |
| AS-PAR-040-AC04 | an untrusted PR or documentation-only change runs | the pipeline evaluates conditions | never expose tenant credentials or execute privileged pull_request_target code; documentation lint must not masquerade as application acceptance |
| AS-PAR-040-AC05 | a mandatory runner or test dependency is unavailable | the gate reports | record BLOCKED/not-run through an explicit wrapper/evidence record; do not convert skipped tests, continue-on-error or \|\| true to a successful acceptance |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | No product-domain change; do not split/rename existing xUnit tests merely because an example layout lists several test projects. |
| Application services | No application change unless a separately identified testability seam is approved. Keep PostgreSQL-backed tests and actual runner model. |
| Blazor / user interaction | No UI change. |
| Infrastructure / configuration | Future-only edits to .github/workflows/ci.yml and approved manifests; preserve active workflows now. Resolve VSTest/Microsoft.Testing.Platform usage before adding flags. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Review a future CI run showing preserved baseline checks and new explicit evidence; this document generation does not execute or change the workflow.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-AccountingBackfillMigrationTests](#e-t-accountingbackfillmigrationtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-001](#as-par-001)

**External prerequisites / blocked acceptance:** Repository owner approves later workflow changes and protected environments; actual approved browser/provider runner availability is not assumed.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-041"></a>
### AS-PAR-041 — Introduce an approved Playwright E2E lane for the Blazor application

**Story:** As a **QA engineer and product owner**, I want to **verify real persisted user journeys instead of treating prototype or service tests as browser acceptance**.

**Baseline:** `proposed` · **Proposed priority:** `P1` · **Acceptance owner:** QA engineer and product owner.

**Controlling specification:** [§33](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s33), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45), [§46](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s46), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:NFR-13; P:AT-01–46; P:AC-01–03; P:WP-01–20; P:roles.json.

**Related existing source:** [P-WP](#e-p-wp), [P-FLOW](#e-p-flow), [CI](#e-ci), [PG](#e-pg), [SEED](#e-seed), [WEB-HOST](#e-web-host), [WORKER](#e-worker).

**Exact gap / evidence limit:** Prototype Python/Playwright checks exercise synthetic browser state. RouteCatalogTests exercises database service behavior. A current complete Blazor browser suite is not established by either source.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-041-AC01 | an approved browser runner and isolated PostgreSQL fixture are available | E2E starts | use the actual Blazor app, separate user sessions and real persisted records; label provider mode and avoid mocking business commands or directly changing JavaScript state |
| AS-PAR-041-AC02 | all P:AT, permission and role-route cases are enumerated | test selection is built | map each to an existing assertion, a new browser case or an explicit blocked prerequisite; no filename-only or screenshot-only coverage claims |
| AS-PAR-041-AC03 | the workpaper lifecycle is exercised | a source changes and a reviewer retries | verify both visible statuses and database-backed immutable history, exact versions, denial paths and progress invalidation |
| AS-PAR-041-AC04 | a required journey cannot start because the runner/provider/methodology is absent | results are emitted | record BLOCKED/NOT_RUN, distinct from failed and passed tests; no skipped scenario is counted as release acceptance |
| AS-PAR-041-AC05 | a failed browser journey is triaged | artifacts are stored | retain redacted trace/screenshots, commit, fixture ID/hash, actor role, expected/actual result and logs without tokens or client documents |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | No production-domain schema changes solely for tests. Synthetic fixtures must use isolated schemas/installation IDs and existing seed helpers. |
| Application services | Prefer a proposed minimal .NET Playwright test project only after runner/package approval; reuse current .NET host and xUnit conventions. Do not move the UI to React or the backend to Python. |
| Blazor / user interaction | Add only stable semantic selectors/accessibility/testability hooks required by real journeys; no production test-login endpoint. |
| Infrastructure / configuration | No E2E dependency, project, workflow or browser is installed by this documentation task. Future configuration must make test-only identity and effects explicit and reject them in Production. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Proposed E2E coverage matrix must show every enabled service’s create→execute→review→deliver→close journey and relevant rejection/retry paths; publish actual results, not simulated gate status.

**Existing test seams:** [T-RouteCatalogTests](#e-t-routecatalogtests), [T-AuditProgramWorkflowTests](#e-t-auditprogramworkflowtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-001](#as-par-001), [AS-PAR-040](#as-par-040)

**External prerequisites / blocked acceptance:** Approved browser binaries/package version/runner and an owner-approved implementation lane are required. Live-tenant acceptance remains a separate profile.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-042"></a>
### AS-PAR-042 — Complete production composition, secret custody, observability and capacity acceptance

**Story:** As a **technical operations and security owners**, I want to **operate the approved application boundary without enabling unproven external effects**.

**Baseline:** `blocked` · **Proposed priority:** `P1` · **Acceptance owner:** technical operations and security owners.

**Controlling specification:** [§4](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s04), [§7](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s07), [§10](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s10), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§30](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s30), [§35](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s35), [§39](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s39), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:NFR-02/04–09/11; P:ARC-11–13; P:source §22.2; P:AC-02.

**Related existing source:** [WEB-HOST](#e-web-host), [WORKER](#e-worker), [CONFIG](#e-config), [TELEMETRY](#e-telemetry), [CAPACITY](#e-capacity), [CI](#e-ci), [PENDING](#e-pending).

**Exact gap / evidence limit:** Local instrumentation and capacity evidence exist. Production host/provider composition is deliberately blocked; secrets, cross-store custody, deployment security and representative production targets are not accepted by local benchmarks.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-042-AC01 | an approved production deployment is proposed | startup validates configuration | require exact approved identity/provider composition, active capability profile and safety policy; refuse development identity, simulation adapters or contradictory release settings |
| AS-PAR-042-AC02 | secrets, signing keys or persistent Data Protection keys are configured | deployment/rotation is reviewed | use approved protected references/custody, TLS/trusted proxy settings and historical recovery validation; do not copy keys or private values into examples/Git |
| AS-PAR-042-AC03 | long-lived Blazor circuits and workers are deployed or drained | maintenance occurs | honor current identity/grant checks, graceful drain, connection/capacity bounds, readiness and independent provider health without using liveness to hide schema failure |
| AS-PAR-042-AC04 | telemetry and support access are enabled | a sensitive operation fails | record redacted identifiers/correlation/latency/error classification, not financial document content, credentials or signatures; support access remains scoped, time-bound and audited |
| AS-PAR-042-AC05 | performance/availability targets are proposed | acceptance is measured | approve workload/hardware and report actual p95/session/import/report/availability results; do not extrapolate a local 2,000-transaction benchmark into the prototype’s 100-session or million-line target |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Use existing telemetry/operation/audit evidence models and bounded retention; no new SaaS control plane or broker. |
| Application services | Finish approved composition roots behind current interfaces, with runtime policy validation and separate privileged records/signing authorities where required. |
| Blazor / user interaction | Show environment/capability/provider mode and redacted operational health, never a green production badge because configuration fields are present. |
| Infrastructure / configuration | Actual existing keys are enumerated in the register. New deployment-specific storage/exporter/custody settings must be designed and approved in a later implementation PR, not invented now. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Production configuration rejection tests, representative capacity run, sensitive-log review, deployment/readiness/drain rehearsal and approved operations evidence linked to the exact build.

**Existing test seams:** [T-ProviderBoundaryTests](#e-t-providerboundarytests), [T-AccountingBenchmarkTests](#e-t-accountingbenchmarktests), [T-OperationRecoveryTests](#e-t-operationrecoverytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-033](#as-par-033), [AS-PAR-034](#as-par-034), [AS-PAR-035](#as-par-035), [AS-PAR-039](#as-par-039), [AS-PAR-040](#as-par-040)

**External prerequisites / blocked acceptance:** P8 plus P1–P7 as relevant: approved infrastructure, secret/key ownership, provider composition, licenses, workload and operations sign-off. Deployment targets are not supplied here.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-043"></a>
### AS-PAR-043 — Control service catalogue versions and explicitly disabled capability profiles

**Story:** As a **practice owner and methodology owner**, I want to **retain all prototype services without falsely enabling unimplemented professional work**.

**Baseline:** `partial` · **Proposed priority:** `P1` · **Acceptance owner:** practice owner and methodology owner.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§31](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s31), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.1–17.13; P:SET-02/04/05; P:LC-09; P:AT-11; P:AC-03.

**Related existing source:** [SPEC](#e-spec), [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PENDING](#e-pending), [METH](#e-meth), [METH-ADD](#e-meth-add).

**Exact gap / evidence limit:** The prototype retains twelve service families plus six extra accounting-practice templates. The .NET first-production boundary enables only specifically approved profiles; it is not authorization to build or enable every catalogue entry.

**Exact prototype acceptance results not fully closed by this story’s current journey** (quoted from `source.json` §24.1; bounded covered sub-controls remain credited in the matrix):

> **[P:AT-11](#p-at-11):** Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-043-AC01 | a service catalogue entry is visible | a user chooses it | show its actual implementation and approved capability status; disabled service selection cannot instantiate a misleading complete workflow |
| AS-PAR-043-AC02 | a service is proposed for activation | the owner reviews its profile | record jurisdiction/entity class, framework/version, scope/currency/group rules, templates, procedures, reviewer/authority, signing, records and required test evidence |
| AS-PAR-043-AC03 | a new service template is adopted | an engagement is created | instantiate only service-appropriate inputs/tasks/reviews/deliverables with pinned versions; consulting/training/compilation must not inherit an audit opinion |
| AS-PAR-043-AC04 | an already implemented accounting/advanced-method profile has scoped methodology evidence | the catalogue is reconciled | preserve that evidence and identify remaining runtime/production acceptance independently; do not reset it to missing or broaden approval |
| AS-PAR-043-AC05 | an unsupported service remains deferred | the release is assessed | retain its story and requirements as blocked/proposed, expose honest unavailable transitions, and assess complete-system acceptance only for explicitly enabled profiles |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Service Capability Profile/template/version structures; add only missing service catalogue metadata and explicit activation evidence links. |
| Application services | Use current capability gating; no generalized plugin marketplace, runtime code execution or additional ERP. |
| Blazor / user interaction | Create service catalogue and profile readiness/disabled explanations with links to exact owner decisions and required evidence. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** One safe denied-activation test for every unapproved entry and one complete E2E journey per enabled version; all variant stories below are conditional on an approved scope decision.

**Existing test seams:** [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-001](#as-par-001), [AS-PAR-003](#as-par-003), [AS-PAR-012](#as-par-012)

**External prerequisites / blocked acceptance:** New service scope needs product/practice approval and named professional methodology owner; source catalogue presence is not that approval.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-044"></a>
### AS-PAR-044 — Implement authorized manual filing evidence before optional filing adapters

**Story:** As a **authorized filing coordinator and client signatory**, I want to **distinguish permission, submission, regulator acceptance and correction without assuming an API**.

**Baseline:** `blocked` · **Proposed priority:** `P1` · **Acceptance owner:** authorized filing coordinator and client signatory.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§26](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s26), [§29](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s29), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45). All work also follows S:§§44–47.

**Prototype requirements:** P:LC-24; P:G10; P:API-07; P:AT-35/39; P:source §17.13 tax.

**Related existing source:** [RELEASE](#e-release), [DOC-DOM](#e-doc-dom), [WORKER](#e-worker), [SPEC](#e-spec).

**Exact gap / evidence limit:** The controlling first-production scope excludes automatic tax-return filing. The prototype nevertheless specifies a conditional manual/authorized filing lifecycle; it must be tracked as disabled until explicitly scoped, not replaced with a fabricated regulator integration.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-044-AC01 | a contracted filing has an approved mandate and supported format | a filing record is prepared | bind entity/period, exact released payload/version, authority, deadline and responsible coordinator |
| AS-PAR-044-AC02 | filing occurs manually through an authorized external channel | evidence is entered | record actual submission reference/time and receipt bytes as submitted, not automatically accepted |
| AS-PAR-044-AC03 | acceptance or rejection is later observed | the record is updated | append the independent authority response; retain rejection reason and new-version resubmission linkage without rewriting the original |
| AS-PAR-044-AC04 | an API is not approved or credentials are absent | an automated action is requested | keep it unavailable; use only the approved manual evidence route where permitted and never invent a regulator endpoint |
| AS-PAR-044-AC05 | a payment instruction accompanies filing | it is displayed | do not treat it as authority to execute a bank payment or as evidence of settlement |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Proposed FilingCase/Submission/Response records belong to existing Completion/Practice seams and reference immutable documents; no operational tax engine is implied. |
| Application services | Guard manual evidence capture and transitions through existing release/scope conventions. Any later adapter is a separately authorized story with callback/replay tests. |
| Blazor / user interaction | Provide mandate, submitted/accepted/rejected/corrected states and evidence review, not an always-success Submit button. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Local state/authority/version/replay tests; acceptance evidence must come from the authorized authority response, not a local mock.

**Existing test seams:** [T-ReleaseEvidenceTests](#e-t-releaseevidencetests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-015](#as-par-015), [AS-PAR-028](#as-par-028), [AS-PAR-043](#as-par-043)

**External prerequisites / blocked acceptance:** Explicit contracted scope, jurisdiction owner, client mandate and approved manual process; automated filing is excluded until separately approved.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-045"></a>
### AS-PAR-045 — Perform independent complete-cycle acceptance and adopt the verified release

**Story:** As a **product owner, accounting owner, partner and release authority**, I want to **accept only enabled services with complete observed end-to-end evidence**.

**Baseline:** `blocked` · **Proposed priority:** `P0-release` · **Acceptance owner:** product owner, accounting owner, partner and release authority.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§31](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s31), [§33](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s33), [§34](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s34), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§45](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s45), [§46](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s46), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:AC-01–03; P:AT-01–46; P:source §25/26; P:NFR-13.

**Related existing source:** [STATUS](#e-status), [PENDING](#e-pending), [SLICE](#e-slice), [CI](#e-ci), [SPEC](#e-spec), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** The repository reports local verification and a successful CI run but explicitly retains production and independent-review blockers. No previous percentage estimate or this backlog is an acceptance certificate.

**Normative acceptance anchor:** the full linked prototype requirement paragraphs and field tables above; observable completion is defined by the criteria below. Any unsupplied professional policy remains an explicit external prerequisite, not an invented rule.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-045-AC01 | an exact build and approved enabled profile are nominated | independent acceptance begins | execute a permitted new-client and recurring-client cycle through acceptance/terms, source intake, preparation/review, management/professional decisions, delivery, archive and renewal |
| AS-PAR-045-AC02 | a source/permission/approval changes during the journey | negative acceptance cases run | prove fail-closed behavior and retained history across UI, database, workers and external stores; record actual rejection and retry evidence |
| AS-PAR-045-AC03 | one mandatory tenant, runner, signing, records, migration or reviewer prerequisite is missing | release is evaluated | retain BLOCKED/NOT_RUN and disabled production profile rather than averaging it into a completion percentage |
| AS-PAR-045-AC04 | all applicable evidence is available | owners approve release | bind product functionality, accounting reconciliation, partner/quality decisions, data migration and technical/recovery readiness to the exact commit/profile/resource scope |
| AS-PAR-045-AC05 | the selected branch/PR is ready | repository integration is considered | follow current protected review/merge policy and explicit authorized action; this document supplies no merge codeword or blanket merge authorization |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | No new domain model is required solely for acceptance; use existing capability/evidence records and protected external acceptance records. |
| Application services | No new application logic unless an acceptance failure opens a narrow implementation issue with its own evidence. |
| Blazor / user interaction | Preserve truthful disabled features and observed provider states in the accepted build. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Complete evidence index per §44.5, reviewed P:AT/permission/service matrices, exact-build sign-off and explicit remaining exclusions. Do not edit historical execution pointers during this documentation delivery.

**Existing test seams:** [T-FinancialStatementTests](#e-t-financialstatementtests), [T-ReleaseTests](#e-t-releasetests), [T-RecordsArchiveTests](#e-t-recordsarchivetests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-011](#as-par-011), [AS-PAR-021](#as-par-021), [AS-PAR-028](#as-par-028), [AS-PAR-030](#as-par-030), [AS-PAR-031](#as-par-031), [AS-PAR-038](#as-par-038), [AS-PAR-039](#as-par-039), [AS-PAR-040](#as-par-040), [AS-PAR-041](#as-par-041), [AS-PAR-042](#as-par-042), [AS-PAR-043](#as-par-043)

**External prerequisites / blocked acceptance:** P9/P10: independent human review, protected merge/release approval, all applicable professional and technical owners, approved live resources/runners.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-046"></a>
### AS-PAR-046 — Approve and implement the Internal audit service profile

**Story:** As a **internal-audit manager**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** internal-audit manager.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.2; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved internal audit creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact prototype acceptance anchor:** [P:AT-11](#p-at-11) — “Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training” The service-specific M/C/O and processing/output contract is P:source §17.2.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-046-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-046-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require charter/terms, process scope and owners, control descriptions, relevant populations and reporting sponsor; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-046-AC03 | the service is performed | the assigned team progresses | execute planning → control/fraud assessment → process/transaction testing → observations → factual validation → management actions → independent conclusion → follow-up with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-046-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce internal-audit report, control findings, action owners/dates and independently verified follow-up tied to the exact manifest and service-specific authority; A process-owner response cannot delete the auditor’s finding; no statutory financial-statement opinion template. |
| AS-PAR-046-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. A process-owner response cannot delete the auditor’s finding; no statutory financial-statement opinion template. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. System access, regulated controls and specialist procedures are conditional; analytics/workshops are optional. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-047"></a>
### AS-PAR-047 — Approve and implement the Business valuation service profile

**Story:** As a **valuation specialist**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** valuation specialist.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.3; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved business valuation creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact prototype acceptance anchor:** [P:AT-11](#p-at-11) — “Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training” The service-specific M/C/O and processing/output contract is P:source §17.3.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-047-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-047-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require subject/legal interest, valuation date, purpose, intended users, basis/premise, historical financials, capital structure and management assumptions; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-047-AC03 | the service is performed | the assigned team progresses | execute research → framework → financial/operational/market data → normalization/ratios → approved valuation methods → sensitivities → reconciliation → report with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-047-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce versioned model/report with assumptions, limitations and attributed sources tied to the exact manifest and service-specific authority; Management confirms factual inputs, not the independent valuation outcome; no invented discount-rate methodology. |
| AS-PAR-047-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. Management confirms factual inputs, not the independent valuation outcome; no invented discount-rate methodology. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Forecasts, asset valuations, debt, discount-rate inputs and comparables are conditional; site visits/sensitivities are optional as scoped. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-048"></a>
### AS-PAR-048 — Approve and implement the Feasibility study service profile

**Story:** As a **engagement manager and domain specialists**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** engagement manager and domain specialists.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.4; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved feasibility study creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact prototype acceptance anchor:** [P:AT-11](#p-at-11) — “Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training” The service-specific M/C/O and processing/output contract is P:source §17.4.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-048-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-048-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require venture, objectives, scope, market, capital/operating assumptions, financing plan, timeframe and decision criteria; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-048-AC03 | the service is performed | the assigned team progresses | execute research → scoped market/technical/financial/organizational/environmental analysis → scenarios/sensitivity → dependencies → recommendation with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-048-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce study, financial model, assumption register, alternatives and recommendation tied to the exact manifest and service-specific authority; Forecasts remain assumptions-based, not guaranteed outcomes; each specialist reviews their own authorized domain. |
| AS-PAR-048-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. Forecasts remain assumptions-based, not guaranteed outcomes; each specialist reviews their own authorized domain. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Technical design, permits, environmental/social and staffing assumptions are conditional; alternative locations/technology are optional. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-049"></a>
### AS-PAR-049 — Approve and implement the Forensic audit and investigation service profile

**Story:** As a **authorized forensic specialist**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** authorized forensic specialist.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.5; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved forensic audit and investigation creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact prototype acceptance anchor:** [P:AT-11](#p-at-11) — “Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training” The service-specific M/C/O and processing/output contract is P:source §17.5.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-049-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-049-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require written authority, allegation/question, scope, permitted custodians/evidence sources, handling rules, legal liaison where required and recipients; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-049-AC03 | the service is performed | the assigned team progresses | execute plan → preserve evidence/chain of custody → reproduce analysis → evaluate indicators → authorized interviews → corroborate → limitations/factual findings → restricted review/report with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-049-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce chain-of-custody/evidence register, reproducible analyses, interviews and restricted report tied to the exact manifest and service-specific authority; Routine client portal disclosure is denied; conclusions distinguish evidence, inference and unresolved facts. |
| AS-PAR-049-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. Routine client portal disclosure is denied; conclusions distinguish evidence, inference and unresolved facts. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Legal hold, interview permission, specialist and regulator reporting authority are conditional; advanced analytics are optional. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-008](#as-par-008), [AS-PAR-030](#as-par-030)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-050"></a>
### AS-PAR-050 — Approve and implement the Financial forecasting and projections service profile

**Story:** As a **forecast model owner and independent reviewer**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** forecast model owner and independent reviewer.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.6; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source), [PENDING](#e-pending), [FIELD](#e-field).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved financial forecasting and projections creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact prototype acceptance anchor:** [P:AT-11](#p-at-11) — “Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training” The service-specific M/C/O and processing/output contract is P:source §17.6.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-050-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-050-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require objective/horizon, historical data, model owner, assumptions, business drivers, finance terms and scenarios; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-050-AC03 | the service is performed | the assigned team progresses | execute scope/data plan → historical review → assumptions evaluation → methodology → integrated model → scenario review → findings with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-050-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce forecast model, assumption register, projected statements/cash needs and report tied to the exact manifest and service-specific authority; Management owns assumptions; an assurance conclusion requires separately authorized assurance terms. |
| AS-PAR-050-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. Management owns assumptions; an assurance conclusion requires separately authorized assurance terms. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Capex, working capital, tax, FX, funding/covenant assumptions are conditional; stress/sensitivity cases are optional. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests), [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-051"></a>
### AS-PAR-051 — Approve and implement the AML compliance review service profile

**Story:** As a **compliance specialist**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** compliance specialist.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.7; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved aml compliance review creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact prototype acceptance anchor:** [P:AT-11](#p-at-11) — “Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training” The service-specific M/C/O and processing/output contract is P:source §17.7.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-051-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-051-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require approved jurisdiction/profile, regulated activities, policies, customer population, due-diligence records, monitoring, training and retention evidence; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-051-AC03 | the service is performed | the assigned team progresses | execute plan → policy/control review → CDD/KYC sampling → monitoring/reporting-process evaluation → training/records assessment → findings/actions → follow-up with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-051-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce restricted compliance report and action plan tied to the exact manifest and service-specific authority; A suspicious-activity task is not authority to file a report; anti-tipping-off and restricted recipient rules apply. |
| AS-PAR-051-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. A suspicious-activity task is not authority to file a report; anti-tipping-off and restricted recipient rules apply. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Restricted reporting/investigation records and regulatory authorizations are conditional; effectiveness analytics are optional. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-008](#as-par-008), [AS-PAR-030](#as-par-030)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-052"></a>
### AS-PAR-052 — Approve and implement the Management consulting service profile

**Story:** As a **consulting manager**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** consulting manager.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.8; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved management consulting creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact prototype acceptance anchor:** [P:AT-11](#p-at-11) — “Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training” The service-specific M/C/O and processing/output contract is P:source §17.8.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-052-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-052-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require business problem, scoped stakeholders, baseline measures, target outcomes, sources and decision owner; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-052-AC03 | the service is performed | the assigned team progresses | execute organizational analysis → options/strategy → operational/financial evaluation → performance measures → recommendations → authorized support → outcome tracking with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-052-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce assessment, roadmap, action owners and success measures tied to the exact manifest and service-specific authority; Client management owns implementation choices; consulting does not transfer management responsibility to an audit team. |
| AS-PAR-052-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. Client management owns implementation choices; consulting does not transfer management responsibility to an audit team. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Organizational/strategic/operational/market/change data are conditional; implementation coaching is optional. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-053"></a>
### AS-PAR-053 — Approve and implement the Fixed-asset verification and tagging service profile

**Story:** As a **asset-verification team and accounting reviewer**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** asset-verification team and accounting reviewer.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.9; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source), [PENDING](#e-pending), [FIELD](#e-field).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved fixed-asset verification and tagging creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact prototype acceptance anchor:** [P:AT-11](#p-at-11) — “Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training” The service-specific M/C/O and processing/output contract is P:source §17.9.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-053-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-053-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require asset register, locations, custodians, ownership evidence, cut-off, tagging scheme and reconciliation basis; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-053-AC03 | the service is performed | the assigned team progresses | execute scope/register → location/count plan → categorization → physical verification/tagging → existence/completeness reconciliation → exceptions → authorized valuation/depreciation scope → report with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-053-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce verified asset/tag register, exceptions and amendment proposals tied to the exact manifest and service-specific authority; Register changes require client authorization and accounting review; tagging does not confer a fair-value opinion. |
| AS-PAR-053-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. Register changes require client authorization and accounting review; tagging does not confer a fair-value opinion. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Leased assets/disposals/valuation policies/specialists are conditional; barcode/RFID/mobile devices are optional, not new required dependencies. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests), [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-054"></a>
### AS-PAR-054 — Approve and implement the Inventory management audit service profile

**Story:** As a **inventory audit team**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** inventory audit team.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.10; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source), [PENDING](#e-pending), [FIELD](#e-field).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved inventory management audit creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact prototype acceptance anchor:** [P:AT-11](#p-at-11) — “Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training” The service-specific M/C/O and processing/output contract is P:source §17.10.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-054-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-054-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require inventory records/sites, count instructions/cut-off, control procedures, valuation policies and movement history; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-054-AC03 | the service is performed | the assigned team progresses | execute plan → process/control review → count observation/testing → quantity reconciliation → costing/valuation → turnover/loss-prevention → compliance → recommendations with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-054-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce count/valuation exceptions, control findings, authorized adjustment proposals and scoped report tied to the exact manifest and service-specific authority; Unresolved recounts/movements remain tracked and block relevant conclusions; a quantity tick does not clear valuation. |
| AS-PAR-054-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. Unresolved recounts/movements remain tracked and block relevant conclusions; a quantity tick does not clear valuation. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Third-party/consigned/WIP/obsolete inventory and costing are conditional; cycle-count analytics are optional. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests), [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-055"></a>
### AS-PAR-055 — Approve and implement the ERP implementation and independent review engagement service profile

**Story:** As a **client process owner and technical reviewer**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** client process owner and technical reviewer.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.11; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved erp implementation and independent review engagement creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact prototype acceptance anchor:** [P:AT-11](#p-at-11) — “Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training” The service-specific M/C/O and processing/output contract is P:source §17.11.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-055-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-055-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require business requirements, process owners, system landscape, selection criteria, data inventory, security, migration scope and acceptance criteria; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-055-AC03 | the service is performed | the assigned team progresses | execute plan → selection/contract → design/configuration → migration/integration → UAT → training → authorized go-live → stabilization → independent review where scoped with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-055-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce requirements/design, mappings/configuration, reconciled migration, test/UAT/training/handover and review report tied to the exact manifest and service-specific authority; This is a client-service workflow, not permission to replace AuditSphere with an ERP; client UAT and deployment authority remain distinct. |
| AS-PAR-055-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. This is a client-service workflow, not permission to replace AuditSphere with an ERP; client UAT and deployment authority remain distinct. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Procurement/vendor/interfaces/segregation/cutover permissions are conditional; phased rollout or independent post-implementation review is optional. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-056"></a>
### AS-PAR-056 — Approve and implement the Corporate IFRS/IAS training service profile

**Story:** As a **training lead and technical reviewer**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** training lead and technical reviewer.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.12; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved corporate ifrs/ias training creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact prototype acceptance anchor:** [P:AT-11](#p-at-11) — “Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training” The service-specific M/C/O and processing/output contract is P:source §17.12.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-056-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-056-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require audience/needs, objectives, selected standards/topics/version, delivery mode, schedule, materials owner and assessment approach; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-056-AC03 | the service is performed | the assigned team progresses | execute needs → objectives → materials/technical review → logistics → instruction → exercises/questions → assessment/feedback → completion records with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-056-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce reviewed materials, attendance, results and certificate only where defined criteria are met tied to the exact manifest and service-specific authority; Attendance does not trigger an audit opinion or claim statutory accreditation not actually granted. |
| AS-PAR-056-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. Attendance does not trigger an audit opinion or claim statutory accreditation not actually granted. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Jurisdiction/language/accessibility/venue/certification criteria are conditional; workshops/case studies/coaching are optional. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-057"></a>
### AS-PAR-057 — Approve and implement the Recurring managed bookkeeping service profile

**Story:** As a **accounting owner and client management**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** accounting owner and client management.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.13: recurring bookkeeping; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source), [PENDING](#e-pending), [FIELD](#e-field).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved recurring managed bookkeeping creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact source scope:** P:source §17.13 additional accounting practice templates. Its service-specific M/C/O and processing/output contract controls this story. [P:AT-11](#p-at-11) applies to the twelve original services; use its service-tailoring approach only by explicit analogy here, not as a claim that these additional services were among those twelve.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-057-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-057-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require approved client-book mandate, explicit entity/book/period, authoritative source, source transactions and permitted posting authority; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-057-AC03 | the service is performed | the assigned team progresses | execute source → approved posting → bank/control reconciliation → monthly close → management accounts → manager review → delivery with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-057-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce monthly scoped books/close evidence and management package tied to the exact manifest and service-specific authority; The bounded firm ledger is not a client transactional GL; external-books TB plus reporting adjustments cannot be relabeled managed bookkeeping. |
| AS-PAR-057-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. The bounded firm ledger is not a client transactional GL; external-books TB plus reporting adjustments cannot be relabeled managed bookkeeping. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Operational client posting is a scope extension requiring approved independence, accounting controls and data authority; no external accounting vendor is mandated. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests), [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-022](#as-par-022), [AS-PAR-024](#as-par-024)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-058"></a>
### AS-PAR-058 — Approve and implement the Annual accounts and compilation service profile

**Story:** As a **accounting reviewer and client signatory**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** accounting reviewer and client signatory.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.13: annual accounts/compilation; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source), [PENDING](#e-pending), [FIELD](#e-field).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved annual accounts and compilation creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact source scope:** P:source §17.13 additional accounting practice templates. Its service-specific M/C/O and processing/output contract controls this story. [P:AT-11](#p-at-11) applies to the twelve original services; use its service-tailoring approach only by explicit analogy here, not as a claim that these additional services were among those twelve.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-058-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-058-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require approved TB, accounting policies, entity/period and a permitted non-assurance service profile; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-058-AC03 | the service is performed | the assigned team progresses | execute adjustments → statements/notes → management approval → appropriate compilation/non-assurance report → delivery with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-058-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce approved accounts/notes and the service-appropriate report tied to the exact manifest and service-specific authority; No audit assurance is implied by using existing financial-package calculations or a partner review. |
| AS-PAR-058-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. No audit assurance is implied by using existing financial-package calculations or a partner review. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Use the already implemented accounting engine; compilation reporting wording/requirements remain subject to methodology approval. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests), [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-022](#as-par-022), [AS-PAR-024](#as-par-024)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-059"></a>
### AS-PAR-059 — Approve and implement the Review engagement service profile

**Story:** As a **review-engagement lead**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** review-engagement lead.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.13: review engagement; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved review engagement creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact source scope:** P:source §17.13 additional accounting practice templates. Its service-specific M/C/O and processing/output contract controls this story. [P:AT-11](#p-at-11) applies to the twelve original services; use its service-tailoring approach only by explicit analogy here, not as a claim that these additional services were among those twelve.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-059-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-059-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require approved terms, applicable review standard, entity/period, statements and required inquiry/analytical sources; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-059-AC03 | the service is performed | the assigned team progresses | execute inquiries/analytics → additional required procedures → reviewed statements → limited-assurance conclusion where authorized with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-059-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce review engagement evidence and permitted limited-assurance report tied to the exact manifest and service-specific authority; Do not substitute an external-audit checklist or a generic audit opinion for the approved review methodology. |
| AS-PAR-059-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. Do not substitute an external-audit checklist or a generic audit opinion for the approved review methodology. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Additional procedures arise under the approved standard and findings; their applicability is not guessed by software. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-022](#as-par-022), [AS-PAR-024](#as-par-024)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-060"></a>
### AS-PAR-060 — Approve and implement the Agreed-upon procedures service profile

**Story:** As a **engagement lead and named intended users**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** engagement lead and named intended users.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.13: agreed-upon procedures; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved agreed-upon procedures creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact source scope:** P:source §17.13 additional accounting practice templates. Its service-specific M/C/O and processing/output contract controls this story. [P:AT-11](#p-at-11) applies to the twelve original services; use its service-tailoring approach only by explicit analogy here, not as a claim that these additional services were among those twelve.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-060-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-060-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require agreed purpose/users, exact procedures, terms and acceptance; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-060-AC03 | the service is performed | the assigned team progresses | execute perform the agreed procedures → retain evidence/results → review factual findings → approved release with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-060-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce factual-findings report describing exact work and results tied to the exact manifest and service-specific authority; No audit opinion or assurance conclusion is expressed unless there is a separately scoped service. |
| AS-PAR-060-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. No audit opinion or assurance conclusion is expressed unless there is a separately scoped service. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Procedure changes need renewed agreement and versioning; the platform must not invent professional procedures. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-061"></a>
### AS-PAR-061 — Approve and implement the Tax compliance service profile

**Story:** As a **qualified tax reviewer and client signatory**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** qualified tax reviewer and client signatory.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.13: tax compliance; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source), [PENDING](#e-pending), [FIELD](#e-field).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved tax compliance creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact source scope:** P:source §17.13 additional accounting practice templates. Its service-specific M/C/O and processing/output contract controls this story. [P:AT-11](#p-at-11) applies to the twelve original services; use its service-tailoring approach only by explicit analogy here, not as a claim that these additional services were among those twelve.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-061-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-061-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require approved jurisdiction/registrations/rules, TB/tax records, period and filing mandate; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-061-AC03 | the service is performed | the assigned team progresses | execute book-to-tax adjustments → calculation/return → technical review → client authorization → manual approved submission/acceptance → payment instruction/receipt reconciliation with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-061-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce versioned computation/return, authorization, filing evidence and payment reconciliation tied to the exact manifest and service-specific authority; No hard-coded universal tax law/rate, unapproved filing API or automatic bank execution authority. |
| AS-PAR-061-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. No hard-coded universal tax law/rate, unapproved filing API or automatic bank execution authority. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Rules/methods/filing formats come from qualified owners; use AS-PAR-044 before any optional provider automation. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests), [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-022](#as-par-022), [AS-PAR-024](#as-par-024), [AS-PAR-044](#as-par-044)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="as-par-062"></a>
### AS-PAR-062 — Approve and implement the Payroll administration service profile

**Story:** As a **payroll preparer, independent reviewer and client approver**, I want to **complete the specifically contracted service without borrowing an incompatible audit conclusion**.

**Baseline:** `blocked` · **Proposed priority:** `P3-scope` · **Acceptance owner:** payroll preparer, independent reviewer and client approver.

**Controlling specification:** [§1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s01), [§14](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s14), [§19](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s19), [§21](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s21), [§24](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s24), [§25](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s25), [§27](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s27), [§43](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s43), [§44](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s44), [§47](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md#s47). All work also follows S:§§44–47.

**Prototype requirements:** P:source §17.13: payroll administration; P:LC-09/20–26; P:AT-11; P:AC-03.

**Related existing source:** [ENGAGE](#e-engage), [CATALOG](#e-catalog), [PROGRAM](#e-program), [SPEC](#e-spec), [P-SOURCE](#e-p-source), [PENDING](#e-pending), [FIELD](#e-field).

**Exact gap / evidence limit:** Shared engagement, evidence, review and release foundations exist. No complete separately approved payroll administration creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled.

**Exact source scope:** P:source §17.13 additional accounting practice templates. Its service-specific M/C/O and processing/output contract controls this story. [P:AT-11](#p-at-11) applies to the twelve original services; use its service-tailoring approach only by explicit analogy here, not as a claim that these additional services were among those twelve.

**Acceptance criteria — Given / When / Then**

| Criterion | Given | When | Then |
| --- | --- | --- | --- |
| AS-PAR-062-AC01 | this service has no approved capability/methodology version | an engagement is activated | reject activation with the named missing approval; preserve the catalogue and proposed work rather than simulate a complete service |
| AS-PAR-062-AC02 | an explicit scope extension and exact methodology are approved | the template is instantiated | require authorized employee changes, approved pay rules, time/benefits, entity/period and confidentiality scope; apply conditional/optional inputs as described below and retain source/authority evidence |
| AS-PAR-062-AC03 | the service is performed | the assigned team progresses | execute gross-to-net → independent review → client payroll approval → authorized export → payment/filing evidence → payroll/GL reconciliation with versioned evidence, exceptions and independent reviewer handoffs |
| AS-PAR-062-AC04 | the reviewed work is ready for client/professional release | the permitted approvers decide | produce approved payroll run/export and reconciled payment/filing/ledger evidence tied to the exact manifest and service-specific authority; A typed payroll audit schedule is not an operational payroll engine; bank payment or filing acceptance cannot be inferred from an export. |
| AS-PAR-062-AC05 | the service’s inputs, assumptions, scope or deliverable change | a decision or issued package is reconsidered | invalidate impacted draft approvals or use controlled reissue, retain history and complete approved delivery/retention/closure |

**Implementation guidance and technical constraints**

| Layer / concern | Required future delta |
| --- | --- |
| Domain / data | Reuse current Engagement, capability/template, workpaper, evidence and approval records. Add only the service-specific structured inputs/results necessary for the approved method; proposed schema requires an additive migration and no parallel engine. |
| Application services | Implement service-specific commands/calculators only within existing application modules and approved bounded methods. A typed payroll audit schedule is not an operational payroll engine; bank payment or filing acceptance cannot be inferred from an export. |
| Blazor / user interaction | Provide a service-specific input/checklist/workpaper/report view within the Blazor shell, with explicit unsupported/blocked states. Pay/tax/benefit rules and effective dates require qualified owner approval; no payroll SaaS or payment integration is introduced by default. |
| Infrastructure / configuration | Reuse current EF/PostgreSQL, durable-operation and provider contracts. No new provider or configuration switch is required. |
| Migration / CI / documentation | Apply C6 and C9 only where the delta needs them. Preserve current schemas/workflows now. In a later authorized PR, link exact changed model/command/route to assertions and update only the permitted evidence/documentation; do not overwrite original plans. |

**Test strategy and completion evidence:** Reuse relevant existing invariants; add one focused golden/negative fixture per enabled method plus one service-specific create→execute→review→deliver→close browser journey and unauthorized activation test. Methodology/signoff/production status stays blocked until actual evidence exists.

**Existing test seams:** [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-ProviderBoundaryTests](#e-t-providerboundarytests), [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests). These links are reuse candidates unless the source register explicitly identifies an inspected assertion; new assertions and browser cases are proposed.

**Verification profiles:** documentation checks for this delivery; later use Unit/Database/Web/E2E as relevant under §11. Add migration/restore checks for persisted evidence changes. TenantIntegration/Recovery/independent professional approval are separate when specified.

**Dependencies:** [AS-PAR-043](#as-par-043), [AS-PAR-003](#as-par-003), [AS-PAR-010](#as-par-010), [AS-PAR-019](#as-par-019), [AS-PAR-025](#as-par-025), [AS-PAR-022](#as-par-022), [AS-PAR-024](#as-par-024)

**External prerequisites / blocked acceptance:** Product/practice owner authorizes this scope extension; a named qualified methodology owner supplies professional rules/templates and reviewer eligibility. Live provider/signing/records gates remain independent.

**Definition of done:** every applicable criterion above has exact-commit evidence and owner review under C8. Code complete, local verified, external blocked and production accepted are recorded separately. No status in this document means the story was implemented by generating this file.

<a id="native-matrix"></a>
## 5. Native prototype requirement traceability

Canonical basis: [P-SOURCE](#e-p-source). This matrix inventories every native numbered requirement family in the source. Labels are source-derived summaries, not replacements for the full M/C/O fields or paragraphs. The exact 46 required test results and 44 permission conditions are retained in §§6–7. Native identifiers are preserved, including Q/Y/G without a hyphen. Repeated `source.sections`, `source.lifecycle` and `source.services` views are deduplicated, not counted as independent requirements.

Each owning story provides controlling .NET sections, specific source/test seams, the missing behavior and verification plan. A `covered` row credits a bounded local control only; its cited evidence is not permission to activate production or a new service.

### 5.OBJ — OBJ requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:OBJ-01 | One controlled client/entity/engagement/period/workpaper/deliverable workspace | partial | [LAYOUT](#e-layout), [ACC-NAV](#e-acc-nav) — The current shared sidebar is not persona-specific. Accounting context/navigation exists, but there is no equivalent complete set of role homes and cross-workflow queues. RouteCatalogTests is not browser route evidence. | [AS-PAR-005](#as-par-005) |
| P:OBJ-02 | Explicit transition inputs, permitted actor, processing, reviewer, evidence and next state | partial | [ENGAGE](#e-engage), [TIME-UI](#e-time-ui) — Assignments, tasks, due dates and time exist but not a consolidated versioned service-plan/team workload experience. | [AS-PAR-012](#as-par-012) |
| P:OBJ-03 | Separate management accounting responsibility from independent auditor opinion | partial | [COMPLETE-UI](#e-complete-ui), [RELEASE](#e-release) — The completion screen displays persisted gates and package reviews but does not establish a complete manager summary/report-opinion workflow or prove every professional blocker is recomputed through one authoritative release decision. | [AS-PAR-025](#as-par-025) |
| P:OBJ-04 | Trace an issued statement line to mapping, adjusted TB, journal, source and evidence | partial | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing. | [AS-PAR-022](#as-par-022) |
| P:OBJ-05 | Maintainable reproducible licensed/provenance-controlled baseline and tested extensions | partial | [SPEC](#e-spec), [AGENTS](#e-agents) — The prototype retains Perfex/PHP/MySQL and GreenTech language, while the controlling repository specification requires native .NET. Prototype P:AT identifiers also differ from S:AT identifiers. A source-level crosswalk and owner disposition are necessary; this document is a proposal, not approval. | [AS-PAR-001](#as-par-001) |

### 5.GOV — GOV requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:GOV-01 | Exact-object/version/scope/stage/identity/role/time/statement/conditions/hash approval evidence | partial | [NOTE-UI](#e-note-ui), [APPROVAL](#e-approval) — ReviewPoint.razor sets Cleared directly and saves through EF. It does not expose the required attributable response/review history, independent clearance or current-target checks. Guarded procedure-review code elsewhere does not repair this separate path. | [AS-PAR-004](#as-par-004) |
| P:GOV-02 | Material dependency changes invalidate affected approvals and detect concurrent edits | partial | [DOC-DOM](#e-doc-dom), [AUDIT-DOM](#e-audit-dom) — Evidence references and immutable submissions exist, but the combined workpaper workbook/PBC/direct evidence/clearance manifest and user-facing mutation impact are not fully established. | [AS-PAR-019](#as-par-019) |
| P:GOV-03 | Separate acceptance, journal, workpaper, management, partner, EQR, release and archive decisions | partial | [COMPLETE-UI](#e-complete-ui), [RELEASE](#e-release) — The completion screen displays persisted gates and package reviews but does not establish a complete manager summary/report-opinion workflow or prove every professional blocker is recomputed through one authoritative release decision. | [AS-PAR-025](#as-par-025) |
| P:GOV-04 | Qualified scoped time-bounded delegation and preserved attribution | partial | [ROLES](#e-roles), [AUTH](#e-auth) — The assignable catalogue has eight codes while command allowlists use additional strings. Administrator appears in professional program/review allowlists. Fourteen personas are not a consistent effective-permission model. | [AS-PAR-003](#as-par-003) |

### 5.DAT — DAT requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:DAT-01 | Client relationship→legal entities→entity periods; multiple engagements may reference one immutable snapshot | partial | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing. | [AS-PAR-022](#as-par-022) |
| P:DAT-02 | Explicit group membership and separate consolidation adjustments, no implied unrelated-client access | partial | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing. | [AS-PAR-022](#as-par-022) |
| P:DAT-03 | Stable human/system IDs, scope, version, lifecycle, actor, classification, provenance and retention | partial | [AUTH](#e-auth), [ACTOR](#e-actor) — AuthorizationDecision is a reusable guarded boundary, but inspected legacy screens use firm-only reads or direct writes. Potential cross-engagement/read leaks must be demonstrated or refuted by tests; this review does not assert an exploited vulnerability. | [AS-PAR-002](#as-par-002) |
| P:DAT-04 | Office invoices are firm revenue, never automatic client-service income | covered | [BILL](#e-bill), [LEDGER](#e-ledger) — The bounded office ledger/financial separation is repository-recorded; retain regression. Complete billing exception UX remains separate. | [AS-PAR-029](#as-par-029) |

### 5.SET — SET requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:SET-01 | Approved firm/offices/currencies/calendar/services/numbering/leads/roles/files/email/storage/retention setup | partial | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |
| P:SET-02 | Approved entity periods/frameworks/charts/imports/reconciliation/materiality/templates/rates/budgets | partial | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |
| P:SET-03 | Effective jurisdiction thresholds, measurement periods, dates, holidays, sources and qualified review | blocked | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |
| P:SET-04 | Only approved optional integrations and service packs; supported manual routes remain available | partial | [SPEC](#e-spec), [ENGAGE](#e-engage) — The prototype retains twelve service families plus six extra accounting-practice templates. The .NET first-production boundary enables only specifically approved profiles; it is not authorization to build or enable every catalogue entry. | [AS-PAR-043](#as-par-043) |
| P:SET-05 | Draft→Reviewed→Published→Retired policy/template versions with controlled active adoption | partial | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |

### 5.WF — WF requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:WF-01 | Server transition checks actor, scope, prerequisites, required fields, revision and blockers | partial | [AUTH](#e-auth), [ACTOR](#e-actor) — AuthorizationDecision is a reusable guarded boundary, but inspected legacy screens use firm-only reads or direct writes. Potential cross-engagement/read leaks must be demonstrated or refuted by tests; this review does not assert an exploited vulnerability. | [AS-PAR-002](#as-par-002) |
| P:WF-02 | Save draft is not submit/approve/post/issue/archive; server derives state and totals | partial | [NOTE-UI](#e-note-ui), [APPROVAL](#e-approval) — ReviewPoint.razor sets Cleared directly and saves through EF. It does not expose the required attributable response/review history, independent clearance or current-target checks. Guarded procedure-review code elsewhere does not repair this separate path. | [AS-PAR-004](#as-par-004) |
| P:WF-03 | Atomic business state/history/outbox, notifications after commit and truthful failure states | partial | [WORKER](#e-worker), [PBC-UI](#e-pbc-ui) — Graph, SMTP and Resend mail adapters are already composed for an isolated Acceptance/mail worker. A production-composed and accepted general notification path is not implied by these adapters or queued UI status. | [AS-PAR-035](#as-par-035) |
| P:WF-04 | Scoped idempotency and correlation; no duplicate objects or silently accepted partial batch | partial | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |
| P:WF-05 | Awaiting-information owner/due/next-action and no waiver of mandatory professional duties | partial | [PBC-UI](#e-pbc-ui), [PBC-CLIENT](#e-pbc-client) — Two-sided request threads exist, but a structured query with blocking scope, issuer/reviewer disposition and impact linkage is not established by a free-text reply alone. | [AS-PAR-016](#as-par-016) |

### 5.LC — LC requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:LC-01 | Capture the inquiry | partial | [LEADS](#e-leads), [CRM](#e-crm) — Leads.razor persists creation/qualification but permits optional contact fields and does not expose the full intake, duplicate-resolution or reviewed discovery journey. | [AS-PAR-006](#as-par-006) |
| P:LC-02 | Initial screening and duplicate resolution | partial | [LEADS](#e-leads), [CRM](#e-crm) — Leads.razor persists creation/qualification but permits optional contact fields and does not expose the full intake, duplicate-resolution or reviewed discovery journey. | [AS-PAR-006](#as-par-006) |
| P:LC-03 | Discovery and service definition | partial | [LEADS](#e-leads), [CRM](#e-crm) — Leads.razor persists creation/qualification but permits optional contact fields and does not expose the full intake, duplicate-resolution or reviewed discovery journey. | [AS-PAR-006](#as-par-006) |
| P:LC-04 | New-client evaluation and acceptance | partial | [ASSESS](#e-assess), [ACCEPT-UI](#e-accept-ui) — Acceptance persistence and waiting-workspace intent are real. Restricted case collection, clarification and independently attributable specialist disposition are not a complete first-class journey. | [AS-PAR-008](#as-par-008) |
| P:LC-05 | Scope, pricing, proposal, and negotiation | partial | [PROPOSAL](#e-proposal), [CRM](#e-crm) — ProposalDetail.razor has a hard-coded sample projection and Task.Delay handlers changing only component state. Existing CRM services must drive the page instead. | [AS-PAR-009](#as-par-009) |
| P:LC-06 | Engagement letter and contractual authorization | partial | [CRM](#e-crm), [CLIENT](#e-client) — Client/engagement and acceptance foundations exist, but binding terms, authority verification and a cohesive activation/portal handoff are not demonstrated end-to-end. | [AS-PAR-010](#as-par-010) |
| P:LC-07 | Convert to client and activate portal | partial | [CRM](#e-crm), [CLIENT](#e-client) — Client/engagement and acceptance foundations exist, but binding terms, authority verification and a cohesive activation/portal handoff are not demonstrated end-to-end. | [AS-PAR-010](#as-par-010) |
| P:LC-08 | Annual validation and continuance | partial | [ASSESS](#e-assess), [ACCEPT](#e-accept) — Period roll-forward and continuance decision foundations exist; the annual questionnaire/delta/obligation/renewal workspace is not a complete equivalent. Prototype Y questions are defined in six grouped ranges, not 30 individually worded prompts. | [AS-PAR-011](#as-par-011) |
| P:LC-09 | Create the engagement and service plan | partial | [ENGAGE](#e-engage), [TIME-UI](#e-time-ui) — Assignments, tasks, due dates and time exist but not a consolidated versioned service-plan/team workload experience. | [AS-PAR-012](#as-par-012) |
| P:LC-10 | Planning, strategy memorandum, materiality, and announcement | partial | [PLAN-UI](#e-plan-ui), [PLAN](#e-plan) — Materiality/risk entry and program adoption exist. A complete typed strategy, independent materiality approval, impact assessment and separately authorized announcement journey is not demonstrated by entry forms. | [AS-PAR-017](#as-par-017) |
| P:LC-11 | Issue PBC requests and receive documents | partial | [PBC-UI](#e-pbc-ui), [PBC-CLIENT](#e-pbc-client) — Request creation, threads, upload intents and durable transfer status are real. A complete administrative-versus-technical adequacy workflow, catalogue and responsibility reassignment are not proven by the current screens. | [AS-PAR-013](#as-par-013) |
| P:LC-12 | Resolve missing data and client queries | partial | [PBC-UI](#e-pbc-ui), [PBC-CLIENT](#e-pbc-client) — Two-sided request threads exist, but a structured query with blocking scope, issuer/reviewer disposition and impact linkage is not established by a free-text reply alone. | [AS-PAR-016](#as-par-016) |
| P:LC-13 | Import and validate financial data | partial | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing. | [AS-PAR-022](#as-par-022) |
| P:LC-14 | Map accounts and establish reporting basis | partial | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing. | [AS-PAR-022](#as-par-022) |
| P:LC-15 | Bookkeeping and transaction posting | blocked | [ENGAGE](#e-engage), [CATALOG](#e-catalog) — Shared engagement, evidence, review and release foundations exist. No complete separately approved recurring managed bookkeeping creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled. | [AS-PAR-057](#as-par-057) |
| P:LC-16 | Reconcile accounts and prepare schedules | partial | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing. | [AS-PAR-022](#as-par-022) |
| P:LC-17 | Propose, approve and resolve adjusting entries | partial | [AJE-UI](#e-aje-ui), [FIELD](#e-field) — Exact-journal difference impact and governed correction states are recorded as implemented. Distinct client-consent UX, materiality-linked aggregate evaluation and completion-level professional disposition remain to be evidenced. | [AS-PAR-023](#as-par-023) |
| P:LC-18 | Assemble and validate adjusted trial balance | partial | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing. | [AS-PAR-022](#as-par-022) |
| P:LC-19 | Generate financial statements and disclosures | partial | [FS-UI](#e-fs-ui), [FS-REV-UI](#e-fs-rev-ui) — Exact rendered package bytes and management/accounting/partner decisions already exist. ClientFinancialPackage currently emphasizes metadata/totals; a full approved-artifact preview/download and ordered client review handoff need completion evidence. | [AS-PAR-024](#as-par-024) |
| P:LC-20 | Preparer submission and first-level review | partial | [NOTE-UI](#e-note-ui), [APPROVAL](#e-approval) — ReviewPoint.razor sets Cleared directly and saves through EF. It does not expose the required attributable response/review history, independent clearance or current-target checks. Guarded procedure-review code elsewhere does not repair this separate path. | [AS-PAR-004](#as-par-004) |
| P:LC-21 | Engagement-manager review | partial | [COMPLETE-UI](#e-complete-ui), [RELEASE](#e-release) — The completion screen displays persisted gates and package reviews but does not establish a complete manager summary/report-opinion workflow or prove every professional blocker is recomputed through one authoritative release decision. | [AS-PAR-025](#as-par-025) |
| P:LC-22 | Client management clearance and approval of accounts | partial | [FS-UI](#e-fs-ui), [FS-REV-UI](#e-fs-rev-ui) — Exact rendered package bytes and management/accounting/partner decisions already exist. ClientFinancialPackage currently emphasizes metadata/totals; a full approved-artifact preview/download and ordered client review handoff need completion evidence. | [AS-PAR-024](#as-par-024) |
| P:LC-23 | Partner final review and EQR | partial | [COMPLETION-DOM](#e-completion-dom), [COMPLETE-UI](#e-complete-ui) — EqrCase stores status/notes/assigned reviewer and the completion page displays status. A complete eligible reviewer, concern/response, exact-package and renewed-review workflow is not established by that model. | [AS-PAR-026](#as-par-026) |
| P:LC-24 | Issue, deliver and submit where contracted | partial | [RELEASE](#e-release), [RELEASE-UI](#e-release-ui) — Local candidate, exact-package review and checkpoint safeguards exist. General live provider composition, independent checkpoint acceptance and the complete deliverable/acknowledgement journey remain gated. | [AS-PAR-028](#as-par-028) |
| P:LC-25 | Assemble, lock, retain and renew the file | partial | [ARCHIVE-UI](#e-archive-ui), [RECORDS](#e-records) — Archive manifests/lineage and local hold/protection states are substantial. The records persona’s assembly/disposition/handover interface and positive live Purview behavior are not complete; requested label is not observed protection. | [AS-PAR-030](#as-par-030) |
| P:LC-26 | Post-issue changes, suspension, termination and handover | partial | [RELEASE](#e-release), [RECORDS](#e-records) — Period restatement, archive lineage and revocation primitives exist. A complete relationship/service suspension, post-issue assessment, authorized export and scheduled offboarding workflow is not proven by those primitives. | [AS-PAR-031](#as-par-031) |

### 5.INT — INT requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:INT-01 | Separate public initial inquiry from authenticated detailed acceptance/annual questionnaire; validated callbacks optional | partial | [LEADS](#e-leads), [CRM](#e-crm) — Leads.razor persists creation/qualification but permits optional contact fields and does not expose the full intake, duplicate-resolution or reviewed discovery journey. | [AS-PAR-006](#as-par-006) |
| P:INT-02 | Question/version/respondent/scope/raw-normalized answer/evidence/time/validation/reviewer plus repeatable arrays | partial | [ASSESS](#e-assess), [ACCEPT](#e-accept) — CE/RV banks and progress display exist, but the prototype Q01–Q40 are not proven mapped to the 62 CE questions, and a complete respondent/edit/reopen/attest workflow is not established by section counters. | [AS-PAR-007](#as-par-007) |
| P:INT-03 | Draft autosave versus attested submission; reopen revisions and restricted assessment separation | partial | [ASSESS](#e-assess), [ACCEPT](#e-accept) — CE/RV banks and progress display exist, but the prototype Q01–Q40 are not proven mapped to the 62 CE questions, and a complete respondent/edit/reopen/attest workflow is not established by section counters. | [AS-PAR-007](#as-par-007) |

### 5.Q — Q requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:Q01 | M: Exact legal/trading names; original-language spelling and aliases | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q02 | M: Entity type, jurisdiction, registration or approved alternative, incorporation date and extract | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q03 | M: Registered and principal operating addresses | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q04 | M: Business activities and operating countries | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q05 | M: Financial year-end and first/changed periods | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q06 | C: Direct/ultimate owners, percentage/control basis/effective dates and approved identification threshold | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q07 | M: Directors, finance lead, management signatories and document contributors with role evidence | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q08 | C: Parents, subsidiaries, branches, affiliates and explicit scope | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q09 | C: PEP/sanctions/source-of-funds or wealth declarations under approved policy; restricted detail | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q10 | M: Authority to instruct the firm and approve specific recipient release | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q11 | M: Prior and expected revenue/assets/liabilities/headcount with currency, period and finalized/draft/forecast/estimate basis | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q12 | M: Transaction volumes and entity/bank/currency counts | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q13 | C: Borrowings, covenants, leases, guarantees and financing plans | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q14 | C: Inventory types, locations, valuation and count dates | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q15 | C: Asset register/categories/useful lives/additions/disposals/tagging | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q16 | C: Payroll populations, benefits, deductions and approved pay cycles | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q17 | M: Revenue streams/recognition, customer concentration and contracts | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q18 | C: Related-party transactions, intercompany balances and group packages | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q19 | C: Losses, cash constraints, covenant breaches and going concern | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q20 | M: Accounting system/controller and managed/external/reporting-only mode | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q21 | M: Latest finalized period and periods still open | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q22 | M: TB/GL/bank/ageing/inventory/assets/payroll/tax availability; missing data creates tasks, not balances | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q23 | M: Last bank and control-account reconciliations | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q24 | M: Financial framework, functional/presentation currency and accounting policies | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q25 | C: Interfaces, formats, fiscal calendars, dimensions and access methods | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q26 | M: Who prepares, approves, posts and closes records | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q27 | C: Period system changes, migrations, outages and control changes | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q28 | O: Process maps, management reports, budgets and board packs | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q29 | M: Permitted sharing with firm/specialists/storage providers and purpose | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q30 | C: Tax/regulatory registrations, obligations and deadlines | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q31 | C: Late filings, assessments, investigations and penalties | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q32 | C: Cross-border, transfer-pricing, withholding and customs obligations | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q33 | M: Reason for appointment or provider change | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q34 | C: Predecessor-contact permission, records and disagreements | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q35 | C: Prior opinions, significant findings, uncorrected differences and management letters | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q36 | M: Litigation, fraud allegations, restrictions and significant subsequent events | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q37 | M: Management accepts information/accounts/decision responsibility and required access | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q38 | M firm-only: Competence, time, resources and specialist capacity | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q39 | M for assurance; firm-only: Financial/business/family/fee/prior-service/self-review independence relationships | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |
| P:Q40 | M firm-only: Acceptance support, safeguards/restrictions, responsible owner and review date | partial | [ASSESS](#e-assess), [SPEC](#e-spec), [OLD-AUD](#e-old-aud) — CE question records exist; the exact Q-to-CE/data-field mapping, full respondent form and conditional/restricted behavior require AS-PAR-007 verification. No one-to-one numeric mapping is presumed. | [AS-PAR-007](#as-par-007) |

### 5.AUD — AUD requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:AUD-01 | Complete workpaper context/objective/assertion/risk/procedure/materiality/population/sample/work/evidence/results/exceptions/AJE/conclusion/people/dates | partial | [WP-UI](#e-wp-ui), [FIELD-UI](#e-field-ui) — Workpaper.razor supports server draft saving and immutable submission but not the prototype’s integrated Overview, Guidelines, Workbook, Evidence, Clearance and History workspace. Preserve its acknowledged-draft and concurrency safeguards. | [AS-PAR-018](#as-par-018) |
| P:AUD-02 | Procedure lifecycle, approved N/A rationale and reassessment after dependency changes | partial | [FIELD-UI](#e-field-ui), [PROGRAM](#e-program) — The catalogue/adoption test proves 165 records and selected execution, not all 165 performed procedures. The fieldwork UI displays “Execute from source workflow” and generic N/A routing rather than a complete execution/review flow. | [AS-PAR-021](#as-par-021) |
| P:AUD-03 | Reconciled source population, selection method/seed/strata/items/substitutions/results and approved projection | partial | [POP-UI](#e-pop-ui), [PLAN](#e-plan) — Population and typed fieldwork foundations exist, but full item-level sample provenance/substitution and auditor-controlled confirmation journeys require acceptance evidence and usable workpaper handoff. | [AS-PAR-020](#as-par-020) |
| P:AUD-04 | Auditor-controlled confirmations with verified contact, response authenticity and alternatives | partial | [POP-UI](#e-pop-ui), [PLAN](#e-plan) — Population and typed fieldwork foundations exist, but full item-level sample provenance/substitution and auditor-controlled confirmation journeys require acceptance evidence and usable workpaper handoff. | [AS-PAR-020](#as-par-020) |
| P:AUD-05 | All approved applicable statutory-audit work; forensic investigation separately scoped | partial | [FIELD-UI](#e-field-ui), [PROGRAM](#e-program) — The catalogue/adoption test proves 165 records and selected execution, not all 165 performed procedures. The fieldwork UI displays “Execute from source workflow” and generic N/A routing rather than a complete execution/review flow. | [AS-PAR-021](#as-par-021) |

### 5.G — G requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:G01 | Acceptance facts, clearances and partner decision | partial | [ASSESS](#e-assess), [ACCEPT-UI](#e-accept-ui) — Acceptance persistence and waiting-workspace intent are real. Restricted case collection, clarification and independently attributable specialist disposition are not a complete first-class journey. | [AS-PAR-008](#as-par-008) |
| P:G02 | Current accepted scope, executed terms, entity/period/mode/contacts/resources | partial | [CRM](#e-crm), [CLIENT](#e-client) — Client/engagement and acceptance foundations exist, but binding terms, authority verification and a cohesive activation/portal handoff are not demonstrated end-to-end. | [AS-PAR-010](#as-par-010) |
| P:G03 | Approved strategy, materiality, plan and EQR/specialists | partial | [PLAN-UI](#e-plan-ui), [PLAN](#e-plan) — Materiality/risk entry and program adoption exist. A complete typed strategy, independent materiality approval, impact assessment and separately authorized announcement journey is not demonstrated by entry forms. | [AS-PAR-017](#as-par-017) |
| P:G04 | Reviewed source scope/totals/mappings/duplicates/openings | partial | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing. | [AS-PAR-022](#as-par-022) |
| P:G05 | Current reconciliations/procedures/queries/differences and adjusted TB | partial | [FIELD-UI](#e-field-ui), [PROGRAM](#e-program) — The catalogue/adoption test proves 165 records and selected execution, not all 165 performed procedures. The fieldwork UI displays “Execute from source workflow” and generic N/A routing rather than a complete execution/review flow. | [AS-PAR-021](#as-par-021) |
| P:G06 | Independent evidence review and blocking-note clearance | partial | [NOTE-UI](#e-note-ui), [APPROVAL](#e-approval) — ReviewPoint.razor sets Cleared directly and saves through EF. It does not expose the required attributable response/review history, independent clearance or current-target checks. Guarded procedure-review code elsewhere does not repair this separate path. | [AS-PAR-004](#as-par-004) |
| P:G07 | Manager significant judgment/scope/consistency and summary memorandum | partial | [COMPLETE-UI](#e-complete-ui), [RELEASE](#e-release) — The completion screen displays persisted gates and package reviews but does not establish a complete manager summary/report-opinion workflow or prove every professional blocker is recomputed through one authoritative release decision. | [AS-PAR-025](#as-par-025) |
| P:G08 | Verified management accounts/adjustments/representations | partial | [COMPLETION-DOM](#e-completion-dom), [COMPLETE-UI](#e-complete-ui) — WrittenRepresentation stores a Boolean and signatory name but no explicit package/hash fields in the inspected model. SignatureLineage is a foundation, not proof of implemented legally approved signing. | [AS-PAR-027](#as-par-027) |
| P:G09 | Partner conclusion, required EQR, current evidence and release authority | partial | [COMPLETION-DOM](#e-completion-dom), [COMPLETE-UI](#e-complete-ui) — EqrCase stores status/notes/assigned reviewer and the completion page displays status. A complete eligible reviewer, concern/response, exact-package and renewed-review workflow is not established by that model. | [AS-PAR-026](#as-par-026) |
| P:G10 | Manifest/recipients/dispatch/filing evidence | partial | [RELEASE](#e-release), [RELEASE-UI](#e-release-ui) — Local candidate, exact-package review and checkpoint safeguards exist. General live provider composition, independent checkpoint acceptance and the complete deliverable/acknowledgement journey remain gated. | [AS-PAR-028](#as-par-028) |
| P:G11 | Assembly, retention, holds, restricted archive and next-period trigger | partial | [ARCHIVE-UI](#e-archive-ui), [RECORDS](#e-records) — Archive manifests/lineage and local hold/protection states are substantial. The records persona’s assembly/disposition/handover interface and positive live Purview behavior are not complete; requested label is not observed protection. | [AS-PAR-030](#as-par-030) |
| P:G12 | Authorized reopen/offboard scope, reapproval and preserved handover records | partial | [RELEASE](#e-release), [RECORDS](#e-records) — Period restatement, archive lineage and revocation primitives exist. A complete relationship/service suspension, post-issue assessment, authorized export and scheduled offboarding workflow is not proven by those primitives. | [AS-PAR-031](#as-par-031) |

### 5.BIL — BIL requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:BIL-01 | Approved fee schedules plus time and receipt-backed expenses | partial | [BILL](#e-bill), [LEDGER](#e-ledger) — Invoices, credits, receipts, allocations, time and the bounded firm ledger exist. The complete billing-officer queue, fee exception approvals, recoverable expenses, WIP and collection/settlement journeys require delta verification. | [AS-PAR-029](#as-par-029) |
| P:BIL-02 | WIP/draft/commercial approval/issue/receipts/collections and independent exceptions | partial | [BILL](#e-bill), [LEDGER](#e-ledger) — Invoices, credits, receipts, allocations, time and the bounded firm ledger exist. The complete billing-officer queue, fee exception approvals, recoverable expenses, WIP and collection/settlement journeys require delta verification. | [AS-PAR-029](#as-par-029) |
| P:BIL-03 | Invoice and payment entries affect only the office ledger unless distinct client authorization exists | covered | [BILL](#e-bill), [LEDGER](#e-ledger) — Office-ledger separation is a recorded implemented invariant; preserve financial regression. This does not certify every invoice screen permission. | [AS-PAR-029](#as-par-029) |
| P:BIL-04 | Professional versus commercial closure; permitted holds never change opinions or destroy files | partial | [BILL](#e-bill), [LEDGER](#e-ledger) — Invoices, credits, receipts, allocations, time and the bounded firm ledger exist. The complete billing-officer queue, fee exception approvals, recoverable expenses, WIP and collection/settlement journeys require delta verification. | [AS-PAR-029](#as-par-029) |

### 5.AUT — AUT requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:AUT-01 | Reuse native bounded task/assignment/scheduled capability; professional gates remain explicit (.NET adaptation) | partial | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |
| P:AUT-02 | Typed business events from LeadCaptured through AccessRevoked | partial | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |
| P:AUT-03 | Persisted-state, scoped idempotent handlers with bounded retries, ownership and safe replay | partial | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |
| P:AUT-04 | Configurable working-day response/reminder/escalation/annual targets and delegates | proposed | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |
| P:AUT-05 | Minimal safe notification references, authenticated links, consent and recorded attempts | partial | [WORKER](#e-worker), [PBC-UI](#e-pbc-ui) — Graph, SMTP and Resend mail adapters are already composed for an isolated Acceptance/mail worker. A production-composed and accepted general notification path is not implied by these adapters or queued UI status. | [AS-PAR-035](#as-par-035) |
| P:AUT-06 | Versioned task prerequisites, M/C/O, inputs/outputs/roles/effort/dates/review and recurrence | partial | [ENGAGE](#e-engage), [TIME-UI](#e-time-ui) — Assignments, tasks, due dates and time exist but not a consolidated versioned service-plan/team workload experience. | [AS-PAR-012](#as-par-012) |

### 5.ARC — ARC requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:ARC-01 | Modular monolith; legacy Perfex PHP/MySQL runtime is explicitly superseded by the .NET spec | covered | [SPEC](#e-spec), [AGENTS](#e-agents) — Architecture decision is explicit in S:§1.3 and actual .NET host; no legacy runtime implementation is required. | [AS-PAR-001](#as-par-001) |
| P:ARC-02 | UI/controller transport separated from guarded application transitions and scoped persistence | partial | [AUTH](#e-auth), [ACTOR](#e-actor) — AuthorizationDecision is a reusable guarded boundary, but inspected legacy screens use firm-only reads or direct writes. Potential cross-engagement/read leaks must be demonstrated or refuted by tests; this review does not assert an exploited vulnerability. | [AS-PAR-002](#as-par-002) |
| P:ARC-03 | Office versus client-entity accounting boundaries and authorized cross-context entries | covered | [BILL](#e-bill), [LEDGER](#e-ledger) — Recorded bounded-office-ledger/accounting separation; do not treat it as complete managed client bookkeeping. | [AS-PAR-029](#as-par-029) |
| P:ARC-04 | One posting authority per entity/book/mode; legacy GreenTech adapter is not a new dependency | blocked | [ENGAGE](#e-engage), [CATALOG](#e-catalog) — Shared engagement, evidence, review and release foundations exist. No complete separately approved recurring managed bookkeeping creation-to-closure journey was established in the inspected baseline. Existing specialist calculations or catalogue entries do not prove this service is enabled. | [AS-PAR-057](#as-par-057) |
| P:ARC-05 | Engagement is not an entity ledger; shared periods and explicit groups retain ownership | partial | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing. | [AS-PAR-022](#as-par-022) |
| P:ARC-06 | Scoped relational model, immutable IDs, revisions and integrity | partial | [OLD-AUD](#e-old-aud), [OLD-ACC](#e-old-acc) — Current migrations and ambiguity quarantine are implemented; an approved full legacy-data mapping, rehearsal and production cutover is not proven by loopback schema tests. No vendor source purchase or data import is authorized by this document. | [AS-PAR-038](#as-par-038) |
| P:ARC-07 | Private original/issued object versions with identity/hash/size/media/scope/retention and fresh authorization | partial | [WEB-HOST](#e-web-host), [WORKER](#e-worker) — The runtime deliberately refuses unapproved general external effects. Existing local/simulation/provider boundaries and partial tenant setup do not constitute an accepted live SharePoint provider composition. | [AS-PAR-033](#as-par-033) |
| P:ARC-08 | Scoped reproducible read models/caches and no editable ledger aggregates | partial | [PORTFOLIO](#e-portfolio), [ACC-UI](#e-acc-ui) — Portfolio and accounting projections exist. The full operational catalogue and all scoped report/filter/drill-down/export acceptance paths are not established by those screens. | [AS-PAR-037](#as-par-037) |
| P:ARC-09 | Protected business/access/approval/document/financial/release/admin audit events | partial | [AUTH](#e-auth), [ACTOR](#e-actor) — AuthorizationDecision is a reusable guarded boundary, but inspected legacy screens use firm-only reads or direct writes. Potential cross-engagement/read leaks must be demonstrated or refuted by tests; this review does not assert an exploited vulnerability. | [AS-PAR-002](#as-par-002) |
| P:ARC-10 | Atomic local writes plus durable outbox for long/external work; no distributed transaction claim | partial | [WORKER](#e-worker), [PBC-UI](#e-pbc-ui) — Graph, SMTP and Resend mail adapters are already composed for an isolated Acceptance/mail worker. A production-composed and accepted general notification path is not implied by these adapters or queued UI status. | [AS-PAR-035](#as-par-035) |
| P:ARC-11 | Development/test/acceptance/production boundaries and synthetic isolated fixtures | partial | [WEB-HOST](#e-web-host), [WORKER](#e-worker) — Local instrumentation and capacity evidence exist. Production host/provider composition is deliberately blocked; secrets, cross-store custody, deployment security and representative production targets are not accepted by local benchmarks. | [AS-PAR-042](#as-par-042) |
| P:ARC-12 | Durable worker processing, bounded concurrency and tested deployment scheduling | partial | [WEB-HOST](#e-web-host), [WORKER](#e-worker) — Local instrumentation and capacity evidence exist. Production host/provider composition is deliberately blocked; secrets, cross-store custody, deployment security and representative production targets are not accepted by local benchmarks. | [AS-PAR-042](#as-par-042) |
| P:ARC-13 | Coordinated backup/keys/checkpoints, operational support and redacted monitoring | blocked | [RESTORE](#e-restore), [DB-RESTORE](#e-db-restore) — The loopback restore drill is valuable local evidence, not independent custody or production RPO/RTO. Existing quarantine/reconciliation must not be bypassed after restoring an older database. | [AS-PAR-039](#as-par-039) |

### 5.DATA — DATA requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:DATA-01 | Mutable-record scope/identity/state/actors/times/concurrency; browser approver IDs never override authority | partial | [AUTH](#e-auth), [ACTOR](#e-actor) — AuthorizationDecision is a reusable guarded boundary, but inspected legacy screens use firm-only reads or direct writes. Potential cross-engagement/read leaks must be demonstrated or refuted by tests; this review does not assert an exploited vulnerability. | [AS-PAR-002](#as-par-002) |
| P:DATA-02 | Draft versus submit M/C/O validation; no fabricated null/zero/reference values | partial | [ASSESS](#e-assess), [ACCEPT](#e-accept) — CE/RV banks and progress display exist, but the prototype Q01–Q40 are not proven mapped to the 62 CE questions, and a complete respondent/edit/reopen/attest workflow is not established by section counters. | [AS-PAR-007](#as-par-007) |
| P:DATA-03 | Decimal money/percentage/source precision and posted quantization distinct from display rounding | covered | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Fixed-precision PostgreSQL accounting is implemented and recorded; revalidate any newly added method at posted precision. | [AS-PAR-022](#as-par-022) |
| P:DATA-04 | Client/entities/contacts/engagement components/shared entity-period cardinalities | partial | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing. | [AS-PAR-022](#as-par-022) |
| P:DATA-05 | Scoped unique account/journal/source/occurrence keys and consistent FKs; ambiguous migration quarantine | partial | [OLD-AUD](#e-old-aud), [OLD-ACC](#e-old-acc) — Current migrations and ambiguity quarantine are implemented; an approved full legacy-data mapping, rehearsal and production cutover is not proven by loopback schema tests. No vendor source purchase or data import is authorized by this document. | [AS-PAR-038](#as-par-038) |
| P:DATA-06 | Atomic cross-line journal balance and valid normalized debit/credit lines | covered | [BILL](#e-bill), [LEDGER](#e-ledger) — Bounded firm-ledger integrity is implemented/repository-recorded; client operational ledger remains a separately blocked profile. | [AS-PAR-029](#as-par-029) |
| P:DATA-07 | Non-overlapping entity/book periods and distinct financial/tax/engagement/issue dates | partial | [ACC-UI](#e-acc-ui), [PERIOD-UI](#e-period-ui) — Core TB/GL intake, mappings, reconciliations, adjustments, package exports and bounded consolidation are implemented and recorded locally. Remaining work is field/route acceptance and unresolved workflow details—not replacement engines or a renewed claim that PDF is missing. | [AS-PAR-022](#as-par-022) |
| P:DATA-08 | Append-only approvals/issued artifacts, supersession and changed-dependency invalidation | partial | [DOC-DOM](#e-doc-dom), [AUDIT-DOM](#e-audit-dom) — Evidence references and immutable submissions exist, but the combined workpaper workbook/PBC/direct evidence/clearance manifest and user-facing mutation impact are not fully established. | [AS-PAR-019](#as-par-019) |

### 5.API — API requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:API-01 | Authenticated scoped business commands, not unrestricted writes; public intake separate | partial | [AUTH](#e-auth), [ACTOR](#e-actor) — AuthorizationDecision is a reusable guarded boundary, but inspected legacy screens use firm-only reads or direct writes. Potential cross-engagement/read leaks must be demonstrated or refuted by tests; this review does not assert an exploited vulnerability. | [AS-PAR-002](#as-par-002) |
| P:API-02 | Idempotent create/submit/post/approve/issue and optimistic concurrency; /ste/v1 is a source contract, not required route migration | partial | [SPEC](#e-spec), [AGENTS](#e-agents) — The prototype retains Perfex/PHP/MySQL and GreenTech language, while the controlling repository specification requires native .NET. Prototype P:AT identifiers also differ from S:AT identifiers. A source-level crosswalk and owner disposition are necessary; this document is a proposal, not approval. | [AS-PAR-001](#as-par-001) |
| P:API-03 | Distinct validation/denied/conflict/duplicate/pending/external/success outcomes and scoped pagination | partial | [AUTH](#e-auth), [ACTOR](#e-actor) — AuthorizationDecision is a reusable guarded boundary, but inspected legacy screens use firm-only reads or direct writes. Potential cross-engagement/read leaks must be demonstrated or refuted by tests; this review does not assert an exploited vulnerability. | [AS-PAR-002](#as-par-002) |
| P:API-04 | Event schema/aggregate version/scope/actor/time/correlation/causation and safe references | partial | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |
| P:API-05 | Least-privilege adapters, authenticated callbacks, replay defense and observed outcome proof | blocked | [WEB-HOST](#e-web-host), [WORKER](#e-worker) — The runtime deliberately refuses unapproved general external effects. Existing local/simulation/provider boundaries and partial tenant setup do not constitute an accepted live SharePoint provider composition. | [AS-PAR-033](#as-par-033) |
| P:API-06 | Authority/scope/tenant/secret-reference/revocation records; delegation is not app-wide access | blocked | [WEB-HOST](#e-web-host), [ACTOR](#e-actor) — OIDC and immutable actor mapping are coded, while recorded live identity acceptance is still blocked. Development identity and roster operations are not evidence of live directory/Conditional Access behavior. | [AS-PAR-034](#as-par-034) |
| P:API-07 | Document provider versions/permissions and optional bank-feed/filing identities and receipts | blocked | [RELEASE](#e-release), [DOC-DOM](#e-doc-dom) — The controlling first-production scope excludes automatic tax-return filing. The prototype nevertheless specifies a conditional manual/authorized filing lifecycle; it must be tracked as disabled until explicitly scoped, not replaced with a fabricated regulator integration. | [AS-PAR-044](#as-par-044) |

### 5.UX — UX requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:UX-01 | Context banner and M/C/O fields; explain disabled transitions | partial | [LAYOUT](#e-layout), [ACC-NAV](#e-acc-nav) — The current shared sidebar is not persona-specific. Accounting context/navigation exists, but there is no equivalent complete set of role homes and cross-workflow queues. RouteCatalogTests is not browser route evidence. | [AS-PAR-005](#as-par-005) |
| P:UX-02 | Safe draft autosave, consequence/authority checks, scoped navigation and accessibility | partial | [LAYOUT](#e-layout), [ACC-NAV](#e-acc-nav) — The current shared sidebar is not persona-specific. Accounting context/navigation exists, but there is no equivalent complete set of role homes and cross-workflow queues. RouteCatalogTests is not browser route evidence. | [AS-PAR-005](#as-par-005) |
| P:UX-03 | Role-specific staff queues and client terms/requests/deliverables/invoices/actions | partial | [LAYOUT](#e-layout), [ACC-NAV](#e-acc-nav) — The current shared sidebar is not persona-specific. Accounting context/navigation exists, but there is no equivalent complete set of role homes and cross-workflow queues. RouteCatalogTests is not browser route evidence. | [AS-PAR-005](#as-par-005) |

### 5.REP — REP requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:REP-01 | Financial report catalogue including notes/presentation checks, not just a ledger printout | partial | [PORTFOLIO](#e-portfolio), [ACC-UI](#e-acc-ui) — Portfolio and accounting projections exist. The full operational catalogue and all scoped report/filter/drill-down/export acceptance paths are not established by those screens. | [AS-PAR-037](#as-par-037) |
| P:REP-02 | Explicit authorized entity/book/period/version/currency/basis/mapping/comparison parameters | partial | [PORTFOLIO](#e-portfolio), [ACC-UI](#e-acc-ui) — Portfolio and accounting projections exist. The full operational catalogue and all scoped report/filter/drill-down/export acceptance paths are not established by those screens. | [AS-PAR-037](#as-par-037) |
| P:REP-03 | Defined operational measures and authorized drill-down for all lifecycle queues | partial | [PORTFOLIO](#e-portfolio), [ACC-UI](#e-acc-ui) — Portfolio and accounting projections exist. The full operational catalogue and all scoped report/filter/drill-down/export acceptance paths are not established by those screens. | [AS-PAR-037](#as-par-037) |
| P:REP-04 | Reproducible cross-format totals and amount lineage; issued PDF version and editable-export distinction | partial | [FS-UI](#e-fs-ui), [FS-REV-UI](#e-fs-rev-ui) — Exact rendered package bytes and management/accounting/partner decisions already exist. ClientFinancialPackage currently emphasizes metadata/totals; a full approved-artifact preview/download and ordered client review handoff need completion evidence. | [AS-PAR-024](#as-par-024) |

### 5.NFR — NFR requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:NFR-01 | Deny-by-default server role/practice/entity/engagement/classification/state checks | partial | [AUTH](#e-auth), [ACTOR](#e-actor) — AuthorizationDecision is a reusable guarded boundary, but inspected legacy screens use firm-only reads or direct writes. Potential cross-engagement/read leaks must be demonstrated or refuted by tests; this review does not assert an exploited vulnerability. | [AS-PAR-002](#as-par-002) |
| P:NFR-02 | Approved MFA/session/signatory lifecycle and prompt revocation across jobs/downloads | blocked | [WEB-HOST](#e-web-host), [ACTOR](#e-actor) — OIDC and immutable actor mapping are coded, while recorded live identity acceptance is still blocked. Development identity and roster operations are not evidence of live directory/Conditional Access behavior. | [AS-PAR-034](#as-par-034) |
| P:NFR-03 | Allowlisted bounded safe uploads and isolated preview; no executable paths/macros | partial | [PBC-CLIENT](#e-pbc-client), [PBC-UI](#e-pbc-ui) — The client form asks the user to enter SHA-256. Workpaper-specific approved template download and completed workbook/evidence replacement are not integrated. Existing chunk and snapshot protections must remain intact. | [AS-PAR-014](#as-par-014) |
| P:NFR-04 | Protected secrets, encryption, rotation and historical key recovery | blocked | [WEB-HOST](#e-web-host), [WORKER](#e-worker) — Local instrumentation and capacity evidence exist. Production host/provider composition is deliberately blocked; secrets, cross-store custody, deployment security and representative production targets are not accepted by local benchmarks. | [AS-PAR-042](#as-par-042) |
| P:NFR-05 | Redacted logs, authorized time-bound support and no automatic admin professional power | partial | [WEB-HOST](#e-web-host), [WORKER](#e-worker) — Local instrumentation and capacity evidence exist. Production host/provider composition is deliberately blocked; secrets, cross-store custody, deployment security and representative production targets are not accepted by local benchmarks. | [AS-PAR-042](#as-par-042) |
| P:NFR-06 | Safe source control with reproducible code/migrations/fixtures/manifests, not production evidence/secrets | partial | [CI](#e-ci), [STATUS](#e-status) — Current CI builds, migrates, tests and probes readiness. It uses postgres:18 rather than an exact 18.6 image and floating major action refs, and does not show a Playwright lane. This story specifies a later reviewed delta only. | [AS-PAR-040](#as-par-040) |
| P:NFR-07 | Checkpointed recoverable jobs and staged/atomic financial completion | partial | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |
| P:NFR-08 | Observed database/documents/identity/key restore, not backup notifications | blocked | [RESTORE](#e-restore), [DB-RESTORE](#e-db-restore) — The loopback restore drill is valuable local evidence, not independent custody or production RPO/RTO. Existing quarantine/reconciliation must not be bypassed after restoring an older database. | [AS-PAR-039](#as-par-039) |
| P:NFR-09 | Bounded memory/pagination/cancellation/timeout with truthful post-commit outcomes | partial | [WEB-HOST](#e-web-host), [WORKER](#e-worker) — Local instrumentation and capacity evidence exist. Production host/provider composition is deliberately blocked; secrets, cross-store custody, deployment security and representative production targets are not accepted by local benchmarks. | [AS-PAR-042](#as-par-042) |
| P:NFR-10 | Effective record policy/holds/assembly and attributable later additions | partial | [ARCHIVE-UI](#e-archive-ui), [RECORDS](#e-records) — Archive manifests/lineage and local hold/protection states are substantial. The records persona’s assembly/disposition/handover interface and positive live Purview behavior are not complete; requested label is not observed protection. | [AS-PAR-030](#as-par-030) |
| P:NFR-11 | Dependency/provenance/build/migration/security/regression controls | partial | [CI](#e-ci), [STATUS](#e-status) — Current CI builds, migrates, tests and probes readiness. It uses postgres:18 rather than an exact 18.6 image and floating major action refs, and does not show a Playwright lane. This story specifies a later reviewed delta only. | [AS-PAR-040](#as-par-040) |
| P:NFR-12 | Reviewed versioned policy changes and explicit active-engagement adoption | partial | [ADMIN-UI](#e-admin-ui), [SETUP-UI](#e-setup-ui) — Setup, versioned templates and durable workers provide foundations. A complete firm/jurisdiction/calendar/deadline/service-recurrence policy catalogue with reviewed applicability and occurrence-level evidence is not established. | [AS-PAR-036](#as-par-036) |
| P:NFR-13 | UI-independent calculation/control tests plus E2E for every enabled service | proposed | [P-WP](#e-p-wp), [P-FLOW](#e-p-flow) — Prototype Python/Playwright checks exercise synthetic browser state. RouteCatalogTests exercises database service behavior. A current complete Blazor browser suite is not established by either source. | [AS-PAR-041](#as-par-041) |

### 5.TR — TR requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:TR-01 | Verifiable permitted-use baseline; legacy commercial packages are not mandatory .NET dependencies | partial | [SPEC](#e-spec), [AGENTS](#e-agents) — The prototype retains Perfex/PHP/MySQL and GreenTech language, while the controlling repository specification requires native .NET. Prototype P:AT identifiers also differ from S:AT identifiers. A source-level crosswalk and owner disposition are necessary; this document is a proposal, not approval. | [AS-PAR-001](#as-par-001) |
| P:TR-02 | Retained source concepts mapped to required .NET behavior without vendor-core edits | partial | [SPEC](#e-spec), [AGENTS](#e-agents) — The prototype retains Perfex/PHP/MySQL and GreenTech language, while the controlling repository specification requires native .NET. Prototype P:AT identifiers also differ from S:AT identifiers. A source-level crosswalk and owner disposition are necessary; this document is a proposal, not approval. | [AS-PAR-001](#as-par-001) |
| P:TR-03 | Safe environment/configuration separation and authorized key/credential/history changes | blocked | [WEB-HOST](#e-web-host), [WORKER](#e-worker) — Local instrumentation and capacity evidence exist. Production host/provider composition is deliberately blocked; secrets, cross-store custody, deployment security and representative production targets are not accepted by local benchmarks. | [AS-PAR-042](#as-par-042) |
| P:TR-04 | Reviewed reproducible clean/upgrade migrations, recovery prerequisites and supported downgrade behavior | partial | [OLD-AUD](#e-old-aud), [OLD-ACC](#e-old-acc) — Current migrations and ambiguity quarantine are implemented; an approved full legacy-data mapping, rehearsal and production cutover is not proven by loopback schema tests. No vendor source purchase or data import is authorized by this document. | [AS-PAR-038](#as-par-038) |
| P:TR-05 | Verified scoped entity/book ownership across sources, queries, workers, reports and caches | partial | [OLD-AUD](#e-old-aud), [OLD-ACC](#e-old-acc) — Current migrations and ambiguity quarantine are implemented; an approved full legacy-data mapping, rehearsal and production cutover is not proven by loopback schema tests. No vendor source purchase or data import is authorized by this document. | [AS-PAR-038](#as-par-038) |
| P:TR-06 | Separate immutable preparer/checker/manager/management/partner/EQR/issue/reopen decisions | partial | [COMPLETE-UI](#e-complete-ui), [RELEASE](#e-release) — The completion screen displays persisted gates and package reviews but does not establish a complete manager summary/report-opinion workflow or prove every professional blocker is recomputed through one authoritative release decision. | [AS-PAR-025](#as-par-025) |
| P:TR-07 | Typed strategy/materiality/summary/opinion/announcement documents with evidence/review/issuance | partial | [PLAN-UI](#e-plan-ui), [PLAN](#e-plan) — Materiality/risk entry and program adoption exist. A complete typed strategy, independent materiality approval, impact assessment and separately authorized announcement journey is not demonstrated by entry forms. | [AS-PAR-017](#as-par-017) |
| P:TR-08 | Approved migration inventory, mappings, counts, totals, exceptions and reconciliation | blocked | [OLD-AUD](#e-old-aud), [OLD-ACC](#e-old-acc) — Current migrations and ambiguity quarantine are implemented; an approved full legacy-data mapping, rehearsal and production cutover is not proven by loopback schema tests. No vendor source purchase or data import is authorized by this document. | [AS-PAR-038](#as-par-038) |
| P:TR-09 | Evidence-based attribution; unresolved ownership stays in restricted staging | partial | [OLD-AUD](#e-old-aud), [OLD-ACC](#e-old-acc) — Current migrations and ambiguity quarantine are implemented; an approved full legacy-data mapping, rehearsal and production cutover is not proven by loopback schema tests. No vendor source purchase or data import is authorized by this document. | [AS-PAR-038](#as-par-038) |
| P:TR-10 | Preserve historical approvals accurately; no fabricated/re-performed/backdated history | partial | [OLD-AUD](#e-old-aud), [OLD-ACC](#e-old-acc) — Current migrations and ambiguity quarantine are implemented; an approved full legacy-data mapping, rehearsal and production cutover is not proven by loopback schema tests. No vendor source purchase or data import is authorized by this document. | [AS-PAR-038](#as-par-038) |
| P:TR-11 | Complete migration rehearsal with financial/file/permission reconciliation and owner approval | blocked | [OLD-AUD](#e-old-aud), [OLD-ACC](#e-old-acc) — Current migrations and ambiguity quarantine are implemented; an approved full legacy-data mapping, rehearsal and production cutover is not proven by loopback schema tests. No vendor source purchase or data import is authorized by this document. | [AS-PAR-038](#as-par-038) |
| P:TR-12 | Approved cutover freeze/delta/backup/validation/recovery and protected source archive | blocked | [OLD-AUD](#e-old-aud), [OLD-ACC](#e-old-acc) — Current migrations and ambiguity quarantine are implemented; an approved full legacy-data mapping, rehearsal and production cutover is not proven by loopback schema tests. No vendor source purchase or data import is authorized by this document. | [AS-PAR-038](#as-par-038) |
| P:TR-13 | First-live financial/operational reconciliation and retained transition evidence | blocked | [STATUS](#e-status), [PENDING](#e-pending) — The repository reports local verification and a successful CI run but explicitly retains production and independent-review blockers. No previous percentage estimate or this backlog is an acceptance certificate. | [AS-PAR-045](#as-par-045) |

### 5.AC — AC requirements

| Prototype ID | Requirement locator / required intent | Coverage | Current evidence and exact remaining scope | Owning story |
| --- | --- | --- | --- | --- |
| P:AC-01 | Trace every delivered function to requirements, lifecycle, tests and owner; assess change impact | partial | [SPEC](#e-spec), [AGENTS](#e-agents) — The prototype retains Perfex/PHP/MySQL and GreenTech language, while the controlling repository specification requires native .NET. Prototype P:AT identifiers also differ from S:AT identifiers. A source-level crosswalk and owner disposition are necessary; this document is a proposal, not approval. | [AS-PAR-001](#as-par-001) |
| P:AC-02 | Distinct product, financial, professional, operational and migration acceptance | blocked | [STATUS](#e-status), [PENDING](#e-pending) — The repository reports local verification and a successful CI run but explicitly retains production and independent-review blockers. No previous percentage estimate or this backlog is an acceptance certificate. | [AS-PAR-045](#as-par-045) |
| P:AC-03 | Explicit disabled modes; no enabled incomplete service path | partial | [SPEC](#e-spec), [ENGAGE](#e-engage) — The prototype retains twelve service families plus six extra accounting-practice templates. The .NET first-production boundary enables only specifically approved profiles; it is not authorization to build or enable every catalogue entry. | [AS-PAR-043](#as-par-043) |

### 5.Y — Annual questionnaire identifiers: source-defined groups

The source supplies grouped prompts, not thirty independently worded questions. Each native Y identifier is enumerated below under its source range. A future implementation must approve the Y-to-RV mapping rather than invent missing individual wording. **All six groups are partial**: the original RV bank and continuance/period foundations exist, but the full attested annual comparison workflow is not established. [SPEC](#e-spec), [ASSESS](#e-assess), [OLD-AUD](#e-old-aud).

| Source range / native identifiers | Source-derived group intent | Coverage / remaining criterion | Owner |
| --- | --- | --- | --- |
| Y01–Y05 — P:Y01, P:Y02, P:Y03, P:Y04, P:Y05 | Confirm legal name/status, registrations, addresses, financial year-end and activities. | partial — new attestation, change/effective-date/evidence validation and independently current continuance; no numeric ID equivalence to RV. | [AS-PAR-011](#as-par-011) |
| Y06–Y10 — P:Y06, P:Y07, P:Y08, P:Y09, P:Y10 | Confirm owners/control, directors/signatories, group structure, countries and related parties. | partial — new attestation, change/effective-date/evidence validation and independently current continuance; no numeric ID equivalence to RV. | [AS-PAR-011](#as-par-011) |
| Y11–Y15 — P:Y11, P:Y12, P:Y13, P:Y14, P:Y15 | Update actual/prior and expected/current revenue, assets, headcount, financing and going-concern facts. | partial — new attestation, change/effective-date/evidence validation and independently current continuance; no numeric ID equivalence to RV. | [AS-PAR-011](#as-par-011) |
| Y16–Y20 — P:Y16, P:Y17, P:Y18, P:Y19, P:Y20 | Confirm accounting framework/currency, ERP/interfaces, reconciliations, inventory/assets and payroll. | partial — new attestation, change/effective-date/evidence validation and independently current continuance; no numeric ID equivalence to RV. | [AS-PAR-011](#as-par-011) |
| Y21–Y25 — P:Y21, P:Y22, P:Y23, P:Y24, P:Y25 | Update tax/regulatory obligations, litigation/investigations, fraud/control changes, prior findings and significant events. | partial — new attestation, change/effective-date/evidence validation and independently current continuance; no numeric ID equivalence to RV. | [AS-PAR-011](#as-par-011) |
| Y26–Y30 — P:Y26, P:Y27, P:Y28, P:Y29, P:Y30 | Confirm services/deadlines, management responsibilities, portal access, data-sharing choices and signatory attestation. | partial — new attestation, change/effective-date/evidence validation and independently current continuance; no numeric ID equivalence to RV. | [AS-PAR-011](#as-par-011) |

<a id="acceptance-matrix"></a>
## 6. Exact prototype acceptance-test results

The **Required result** column below transcribes `source.json` §24.1. It is **not** the different AT bank inside docs/auditsphere-requirements-system-specification-current.md. Existing test-file links identify concrete reuse seams; only source-register A evidence proves inspected assertions. All new full-journey browser tests are N/proposed, and no tests were executed for this documentation delivery. A future implementation must record individual method/test-case IDs and actual results before marking the full criterion accepted.

| Prototype scenario | Exact required result | Coverage | Existing tests / unmet acceptance | Story |
| --- | --- | --- | --- | --- |
| <a id="p-at-01"></a>P:AT-01 | Save permitted draft if applicable; reject submission with field-specific guidance | partial | [T-PracticeCrmTests](#e-t-practicecrmtests) — Current lead form does not establish all required intake fields or draft-versus-submit validation. | [AS-PAR-006](#as-par-006) |
| <a id="p-at-02"></a>P:AT-02 | One lead/conversion result; prior result returned without duplicate clients | partial | [T-PracticeCrmTests](#e-t-practicecrmtests) — CRM idempotency is a reuse candidate; complete inquiry/conversion browser retry is not established. | [AS-PAR-006](#as-par-006) |
| <a id="p-at-03"></a>P:AT-03 | Updated scoped discovery and acceptance route; original inquiry history preserved | partial | [T-PracticeCrmTests](#e-t-practicecrmtests) — No complete reviewed discovery/version-change UI was established. | [AS-PAR-006](#as-par-006) |
| <a id="p-at-04"></a>P:AT-04 | Commercial acceptance recorded; professional engagement activation blocked | partial | [T-AcceptanceDecisionTests](#e-t-acceptancedecisiontests), [T-PracticeCrmTests](#e-t-practicecrmtests) — Acceptance checks exist but terms/proposal stub leaves the full activation chain incomplete. | [AS-PAR-010](#as-par-010) |
| <a id="p-at-05"></a>P:AT-05 | Required follow-up evidence and review appear; case cannot bypass the condition | partial | [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-AcceptanceDecisionTests](#e-t-acceptancedecisiontests) — Question bank seeding is not conditional question/evidence/respondent workflow acceptance. | [AS-PAR-007](#as-par-007) |
| <a id="p-at-06"></a>P:AT-06 | New professional work blocked; reason and authorized follow-up retained | partial | [T-AcceptanceDecisionTests](#e-t-acceptancedecisiontests) — Persisted acceptance decisions exist; complete restricted disposition/activation test coverage must be linked. | [AS-PAR-008](#as-par-008) |
| <a id="p-at-07"></a>P:AT-07 | Entity B screens, search results, totals, APIs and downloads remain inaccessible | partial | [T-AuthorizationDecisionTests](#e-t-authorizationdecisiontests), [T-Microsoft365AccessTests](#e-t-microsoft365accesstests) — Guarded services exist; firm-only/read-fallback routes need full sink coverage. | [AS-PAR-002](#as-par-002) |
| <a id="p-at-08"></a>P:AT-08 | New annual snapshot and continuance decision created; prior answers remain unchanged | partial | [T-AcceptanceDecisionTests](#e-t-acceptancedecisiontests) — No-change annual attestation/current decision cannot be inferred from period roll-forward. | [AS-PAR-011](#as-par-011) |
| <a id="p-at-09"></a>P:AT-09 | No invalid percentage; effective rule creates a review exception and scope decision | partial | [T-ClientAccountingTests](#e-t-clientaccountingtests) — Annual comparison/rule workflow and exact zero-base case need assertion and owner policy. | [AS-PAR-011](#as-par-011) |
| <a id="p-at-10"></a>P:AT-10 | Tasks/templates and permitted reference data copied; signatures, conclusions and approvals reset | partial | [T-ClientAccountingTests](#e-t-clientaccountingtests) — Accounting roll-forward exists; engagement task/template and no-copied-approval browser path still needs proof. | [AS-PAR-012](#as-par-012) |
| <a id="p-at-11"></a>P:AT-11 | Correct service-specific tasks, inputs, review gates and deliverable type; no automatic audit opinion for consulting/training | blocked | [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests) — Catalogue entries exceed the approved enabled first-production profiles; specialist methods remain gated. | [AS-PAR-043](#as-par-043) |
| <a id="p-at-12"></a>P:AT-12 | New immutable version, original retained, affected workpapers identified for review | partial | [T-DocumentSnapshotTests](#e-t-documentsnapshottests), [T-PbcTransferTests](#e-t-pbctransfertests) — Snapshots/transport exist; replacement-to-workpaper-clearance impact and live bytes need completion. | [AS-PAR-014](#as-par-014) |
| <a id="p-at-13"></a>P:AT-13 | Receipt recorded separately from adequacy; reviewer can reopen/seek clarification | partial | [T-PbcTests](#e-t-pbctests) — Client replies persist; structured adequacy/query disposition and reviewer UI need completion. | [AS-PAR-016](#as-par-016) |
| <a id="p-at-14"></a>P:AT-14 | Batch remains staged, exceptions visible, no ledger/snapshot approval bypass | partial | [T-TrialBalanceWorkerTests](#e-t-trialbalanceworkertests), [T-ClientAccountingTests](#e-t-clientaccountingtests) — Recorded intake validations exist; verify exact source-acceptance and route rejection assertions. | [AS-PAR-022](#as-par-022) |
| <a id="p-at-15"></a>P:AT-15 | Duplicate detected using scope/source identity/hash; no duplicate posting | partial | [T-TrialBalanceXlsxImporterTests](#e-t-trialbalancexlsximportertests), [T-ClientAccountingTests](#e-t-clientaccountingtests) — Recorded content/source identity exists; link exact same-content/different-name assertion and browser behavior. | [AS-PAR-022](#as-par-022) |
| <a id="p-at-16"></a>P:AT-16 | Operating-mode controls prevent cumulative double counting | partial | [T-AdjustmentBridgeTests](#e-t-adjustmentbridgetests), [T-ClientAccountingTests](#e-t-clientaccountingtests) — Adjustment reflection exists; every exposed import purpose must be tested against double counting. | [AS-PAR-022](#as-par-022) |
| <a id="p-at-17"></a>P:AT-17 | Approved normalization, totals and mapping applied consistently; ambiguity rejected | partial | [T-ClientAccountingTests](#e-t-clientaccountingtests) — Recorded normalization/dimension support exists; exact alternate/repeated-row cases need explicit assertion links. | [AS-PAR-022](#as-par-022) |
| <a id="p-at-18"></a>P:AT-18 | Reconciliation and authorized explanation/adjustment required before acceptance | partial | [T-ClientAccountingTests](#e-t-clientaccountingtests) — Opening bridges exist; prove no approval bypass with unresolved differences in the exposed journey. | [AS-PAR-022](#as-par-022) |
| <a id="p-at-19"></a>P:AT-19 | Approval rejected; independent authorized reviewer required | partial | [T-ApprovalTests](#e-t-approvaltests), [T-AuditProgramWorkflowTests](#e-t-auditprogramworkflowtests) — Procedure-result self-review is tested; the separate ReviewPoint direct mutation remains a gap. | [AS-PAR-004](#as-par-004) |
| <a id="p-at-20"></a>P:AT-20 | All lines and event committed once, or none; retry identifies committed result | covered | [T-LedgerTests](#e-t-ledgertests), [T-DurableOutboxTests](#e-t-durableoutboxtests) — Local bounded firm-ledger atomicity/idempotency is repository-recorded; preserve these tests. | [AS-PAR-029](#as-par-029) |
| <a id="p-at-21"></a>P:AT-21 | Rejected unless an authorized reopen/new-period correction route is used | partial | [T-LedgerTests](#e-t-ledgertests), [T-RouteCatalogTests](#e-t-routecatalogtests) — Fiscal-period close service behavior is assertion-inspected in RouteCatalogTests; retain the closed-period boundary for each enabled posting path. | [AS-PAR-029](#as-par-029) |
| <a id="p-at-22"></a>P:AT-22 | Superseding reconciliation prevents counting adjustment twice | partial | [T-AdjustmentBridgeTests](#e-t-adjustmentbridgetests) — Reflection/difference lineage is recorded locally; preserve exact replacement-source case and browser handoff. | [AS-PAR-023](#as-par-023) |
| <a id="p-at-23"></a>P:AT-23 | Each client's GL, TB, statements, cash flow, ageing and exports contain only its authorized data | partial | [T-AccountingIntegrityTests](#e-t-accountingintegritytests), [T-ClientAccountingTests](#e-t-clientaccountingtests) — Accounting scoping exists but the criterion includes every screen/report/export, not only one query. | [AS-PAR-002](#as-par-002) |
| <a id="p-at-24"></a>P:AT-24 | Server rejects cross-scope reference; no partial financial writes | partial | [T-AccountingIntegrityTests](#e-t-accountingintegritytests) — Scoped FKs/services are recorded; any new command must reuse and retest the exact rejection. | [AS-PAR-022](#as-par-022) |
| <a id="p-at-25"></a>P:AT-25 | Financial dataset not duplicated; approvals/workpapers remain engagement-specific | partial | [T-ClientAccountingTests](#e-t-clientaccountingtests) — Shared-period architecture exists; no duplicated dataset/current approvals must be shown end-to-end. | [AS-PAR-010](#as-par-010) |
| <a id="p-at-26"></a>P:AT-26 | Only approved components included; eliminations and consolidated version traceable | partial | [T-ClientAccountingTests](#e-t-clientaccountingtests) — Bounded consolidation/advanced local fixtures are recorded; production profile authorization remains separate. | [AS-PAR-022](#as-par-022) |
| <a id="p-at-27"></a>P:AT-27 | Reconciling items required; no forced plug entry or automatic unexplained clearance | partial | [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests) — Typed bank reconciliation is recorded; retain residual/no-plug rejection and reviewer UX proof. | [AS-PAR-022](#as-par-022) |
| <a id="p-at-28"></a>P:AT-28 | Note remains awaiting reviewer clearance; response does not auto-clear | partial | [T-ApprovalTests](#e-t-approvaltests) — Current ReviewPoint lacks the response/independent-clearance lifecycle. | [AS-PAR-004](#as-par-004) |
| <a id="p-at-29"></a>P:AT-29 | Dependent draft reports/approvals marked stale and routed for re-review | partial | [T-ApprovalTests](#e-t-approvaltests), [T-AuditProgramWorkflowTests](#e-t-auditprogramworkflowtests) — Freshness exists on guarded paths; integrated workpaper/evidence/approval invalidation remains incomplete. | [AS-PAR-019](#as-par-019) |
| <a id="p-at-30"></a>P:AT-30 | Server recalculates, records rationale and requires impact assessment/approval | partial | [T-AuditPlanningTests](#e-t-auditplanningtests) — Materiality entry exists; approved version and downstream impact/reapproval need complete workflow. | [AS-PAR-017](#as-par-017) |
| <a id="p-at-31"></a>P:AT-31 | No audit report issuance; responsibilities remain separate | partial | [T-ReleaseTests](#e-t-releasetests) — Local release guards exist; integrated manager/partner/report decisions remain incomplete. | [AS-PAR-025](#as-par-025) |
| <a id="p-at-32"></a>P:AT-32 | Issue blocked until eligible quality reviewer completes required decision | partial | [T-ReleaseTests](#e-t-releasetests), [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests) — EqrCase entity/status is not eligible independent concern/review completion evidence. | [AS-PAR-026](#as-par-026) |
| <a id="p-at-33"></a>P:AT-33 | TB ties to all statements, equity reconciles, cash-flow totals reconcile, notes and comparatives checked | partial | [T-FinancialStatementTests](#e-t-financialstatementtests) — Recorded deterministic packages/Office/PDF and validations are reusable; complete client artifact review is a remaining delta. | [AS-PAR-024](#as-par-024) |
| <a id="p-at-34"></a>P:AT-34 | Explicit pending/rework state; no manufactured zero values or automatic opinion | partial | [T-FinancialStatementTests](#e-t-financialstatementtests), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests) — Core fail-closed patterns exist; verify all UI surfaces and report/methodology paths without placeholders. | [AS-PAR-025](#as-par-025) |
| <a id="p-at-35"></a>P:AT-35 | Sent/delivered/submitted states do not become acknowledged/filing-accepted automatically | partial | [T-ReleaseEvidenceTests](#e-t-releaseevidencetests), [T-PbcTests](#e-t-pbctests) — Queued notification and local release evidence do not prove observed delivery/filing acceptance. | [AS-PAR-028](#as-par-028) |
| <a id="p-at-36"></a>P:AT-36 | Evidence attached to old version; current version remains awaiting approval | partial | [T-ClientAccountingTests](#e-t-clientaccountingtests), [T-ApprovalTests](#e-t-approvaltests) — Package decisions are version-bound; complete client signatory/old-version presentation needs E2E. | [AS-PAR-024](#as-par-024) |
| <a id="p-at-37"></a>P:AT-37 | Authorized reason, new versions, impact review and replacement notices; original package retained | partial | [T-ReleaseTests](#e-t-releasetests), [T-RecordsArchiveTests](#e-t-recordsarchivetests) — Archive/restatement primitives exist; post-issue authorization, notices and reissue journey are incomplete. | [AS-PAR-031](#as-par-031) |
| <a id="p-at-38"></a>P:AT-38 | Office AR/revenue posted only; client books unaffected unless separately authorized | covered | [T-LedgerTests](#e-t-ledgertests), [T-BillingTests](#e-t-billingtests) — Office-versus-client ledger boundary is implemented/repository-recorded; preserve regression. | [AS-PAR-029](#as-par-029) |
| <a id="p-at-39"></a>P:AT-39 | New action denied/stopped; duplicate callback not reapplied; authorized retry/manual route visible | blocked | [T-ProviderBoundaryTests](#e-t-providerboundarytests) — Live identity/document/mail provider acceptance and composition remain separately blocked. | [AS-PAR-033](#as-par-033) |
| <a id="p-at-40"></a>P:AT-40 | Disposal blocked and logged; hold release requires authorized decision | partial | [T-RecordsArchiveTests](#e-t-recordsarchivetests) — Local hold blocking exists; independent disposition/hold-release workflow and actual storage behavior remain gated. | [AS-PAR-030](#as-par-030) |
| <a id="p-at-41"></a>P:AT-41 | Scoped approved export, receipt and access revocation; retained professional evidence preserved | partial | [T-RecordsArchiveTests](#e-t-recordsarchivetests), [T-RoleAdministrationTests](#e-t-roleadministrationtests) — No complete handover/offboarding/effective-time access-revocation journey established. | [AS-PAR-031](#as-par-031) |
| <a id="p-at-42"></a>P:AT-42 | Required schema/reference data established without manual changes; representative workflows pass | partial | [T-CoreEntityCatalogTests](#e-t-coreentitycatalogtests), [T-AccountingBackfillMigrationTests](#e-t-accountingbackfillmigrationtests) — Current CI applies migrations and tests; add drift/prior-schema/browser evidence without claiming all scenarios already passed. | [AS-PAR-038](#as-par-038) |
| <a id="p-at-43"></a>P:AT-43 | Counts/hashes/control totals agree, ownership mappings approved, recovery evidence retained | blocked | [T-OperationRecoveryTests](#e-t-operationrecoverytests), [T-RecordsArchiveTests](#e-t-recordsarchivetests) — Loopback drill is not a full migration/ownership or separately administered cross-store restore. | [AS-PAR-039](#as-par-039) |
| <a id="p-at-44"></a>P:AT-44 | Conflict detected; approvals bind to the version actually reviewed | partial | [T-ApprovalTests](#e-t-approvaltests), [T-AuditPlanningTests](#e-t-auditplanningtests) — Guarded workpaper/package concurrency exists; review-point and proposal paths need consistent handling. | [AS-PAR-004](#as-par-004) |
| <a id="p-at-45"></a>P:AT-45 | Occurrence deduplication and safe recovery; no duplicate professional decisions or filings | partial | [T-DurableOutboxTests](#e-t-durableoutboxtests) — Durable lease/retry infrastructure exists; full schedule occurrence/professional/filing paths need acceptance. | [AS-PAR-036](#as-par-036) |
| <a id="p-at-46"></a>P:AT-46 | Reason and authorized applicability decision retained; mandatory requirements cannot be silently skipped | partial | [T-AuditProgramWorkflowTests](#e-t-auditprogramworkflowtests) — Programme applicability foundation exists; prototype direct toggle must become reasoned independent approval. | [AS-PAR-021](#as-par-021) |

### 6.1 Retain the controlling .NET test banks independently

S:§44.1 retains the controlling specification’s **S:AT-01–28, S:ET-01–44, S:VT-01–24 and S:NT-01–24** inventory. Those 120 scenario identifiers are requirements, not a claim of 120 passing tests. Link each AS-PAR implementation to the relevant full S:scenario text and the P:scenario separately; do not infer identity from similar numbering. S:§44.3’s original 62 CE / 30 RV banks and Appendix D arithmetic fixtures stay intact. [SPEC](#e-spec).

<a id="permissions"></a>
## 7. Exact permission conditions and persona routes

### 7.1 All 44 permission contracts

Roles below are **prototype keys**, not new approved .NET role codes. The condition column preserves the prototype `permissions.json` wording. All rows are currently **partial** as an end-to-end assignable-role/command/UI contract: existing narrower checks are reusable, but AS-PAR-003 must reconcile assignable codes, qualifications, scope and separation. `eqr.complete`, client nominations and handover particularly lack complete journeys. Exercise each row for allowed, wrong-role, wrong-scope, same-person and revoked/expired authority cases. [P-PERM](#e-p-perm), [ROLES](#e-roles), [AUTH](#e-auth).

| Permission key | Prototype roles | Exact condition | Coverage / owning stories |
| --- | --- | --- | --- |
| lead.write | relationship | Assigned commercial scope; not client acceptance. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-006](#as-par-006) |
| proposal.submit | relationship | Current discovery/scope and separate pricing review. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-009](#as-par-009) |
| proposal.review | manager | Assigned commercial authority; never own prepared draft. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-009](#as-par-009) |
| intake.write | onboarding | Only the assigned intake case; received is not verified. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-007](#as-par-007) |
| intake.submit | onboarding | Complete required collection checks; checker must differ. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-007](#as-par-007) |
| compliance.decide | compliance | Assigned restricted case, evidence and rationale; not partner acceptance. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-008](#as-par-008) |
| acceptance.decide | partner | Current independent clearances, conditions and permissible service. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-008](#as-par-008) |
| team.assign | manager | Authorized engagement; valid existing team assignments. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-012](#as-par-012) |
| pbc.assign | client_admin | Only contacts already approved for the same entity. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-013](#as-par-013) |
| pbc.upload | client_finance | Assigned request and entity; no adequacy self-approval. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-014](#as-par-014) |
| pbc.accept | reviewer, manager | Exact submission and authorized reviewer; no self-clearance. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-013](#as-par-013) |
| pbc.clarify | preparer, reviewer, manager | Client-facing wording; internal conclusions stay private. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-016](#as-par-016) |
| financial.read | preparer, reviewer, manager, partner | Explicit client/engagement grant; no implicit group access. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-002](#as-par-002) |
| tb.import | preparer | Validated scope and schema; source snapshot, not source-ledger posting. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-022](#as-par-022) |
| mapping.write | preparer | Draft revision only; preserve source account identity. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-022](#as-par-022) |
| mapping.approve | reviewer | Independent maker/checker and current source revision. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-022](#as-par-022) |
| journal.propose | preparer | Balanced reporting proposal with evidence and scope. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-023](#as-par-023) |
| journal.review | reviewer | Not the preparer; evidence and exact proposed version. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-023](#as-par-023) |
| journal.consent | client | Verified signatory; technical review first; no firm-ledger posting. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-023](#as-par-023) |
| workpaper.write | preparer | Assigned procedure, recorded sources and conclusions. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-018](#as-par-018) |
| workpaper.review | reviewer | Independent and assigned; preserve exact submission. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-018](#as-par-018) |
| review.raise | reviewer, manager, partner | Assigned review scope; severity and required action recorded. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-004](#as-par-004) |
| review.respond | preparer | Responder cannot independently clear the response. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-004](#as-par-004) |
| review.clear | reviewer | Issuer or documented qualified replacement; not responder. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-004](#as-par-004) |
| manager.approve | manager | Independent completion review; current package and gates. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-025](#as-par-025) |
| management.approve | client | Exact presented package, verified signatory, reviewed version. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-024](#as-par-024) |
| partner.approve | partner | Final evidence, management approval and all applicable quality gates. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-025](#as-par-025) |
| eqr.complete | eqr | Eligible non-team reviewer; significant concerns resolved; current package. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-026](#as-par-026) |
| release.issue | partner | Exact frozen manifest and all mandatory gates; one issue event. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-028](#as-par-028) |
| release.dispatch | partner, records | Approved recipients/artifacts only; no content substitution. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-028](#as-par-028) |
| archive.assemble | records | Authorized issued/delivered artifacts; no signed-content editing. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-030](#as-par-030) |
| hold.record | records | Instruction and scope required; no automatic hold release. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-030](#as-par-030) |
| disposal.request | records | No active hold; expiry and separate approval remain required. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-030](#as-par-030) |
| client.nominate | client_admin | Existing entity only; signatory nomination requires firm verification. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-015](#as-par-015) |
| invoice.draft | billing | Office/firm ledger context; source, fees and scope required. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-029](#as-par-029) |
| invoice.review | manager | Separate commercial authority and independent reviewer. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-029](#as-par-029) |
| invoice.issue | billing | Approved current commercial draft; not an audit release. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-029](#as-par-029) |
| receipt.record | billing | Valid issued invoice; positive bounded allocation; separate bank verification. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-029](#as-par-029) |
| time.record | preparer, reviewer, manager | Own entry; approval must be independent. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-029](#as-par-029) |
| time.review | manager | Assigned engagement and a different individual. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-029](#as-par-029) |
| access.execute | admin | Scope verification and approved authority; not self-granting professional power. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-003](#as-par-003) |
| system.configure | admin | Approved configuration and separate change control; no live secrets in demo. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-032](#as-par-032) |
| continuance.decide | partner | Current-period assessment, required clearances and authority. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-011](#as-par-011) |
| handover.request | records | Client-owned versus firm documents distinguished; recipient authority required. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-031](#as-par-031) |

### 7.2 All fourteen personas and their complete route catalogue

Every persona also lists the shared routes **role-guide, privileges, requirements**. Each `overview` must be persona-specific and scoped; it is not permission for a demo selector to change identity. The visible route labels are interaction contracts, not a demand to replace working .NET URLs. Required route-to-current-page mapping belongs to AS-PAR-005; missing destinations must remain visible as unavailable rather than link to a false success page. [P-ROLES](#e-p-roles), [P-GUIDE](#e-p-guide), [LAYOUT](#e-layout).

| Persona key / label | Prototype role routes (plus three shared routes) | Responsibilities / restrictions to preserve | Coverage / owners |
| --- | --- | --- | --- |
| relationship — Relationship owner | overview, acquisition, client-summary, services, renewal | Assigned inquiries, discovery, proposals, relationship/communication and renewal. No professional acceptance, ledger/opinion authority or implicit activation. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-006](#as-par-006) |
| onboarding — Onboarding coordinator | overview, intake, client-summary, invitations, renewal | Factual profile/evidence/signature collection and invitation requests. No self-approval, received-as-verified or granting professional authority. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-007](#as-par-007) |
| compliance — Compliance officer | overview, compliance, renewal | Assigned restricted ownership/conflict/AML/evidence reviews and recommendations. No partner acceptance, prohibition override or automatic suspicious-activity filing. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-008](#as-par-008) |
| partner — Engagement partner / lead auditor | overview, acceptance, clients, engagements, accounting, audit, reviews, delivery, continuance, services | Assigned professional acceptance/judgment/report decisions and exact release readiness. No management-on-behalf approval, own-engagement EQR or history rewrite. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-025](#as-par-025) |
| manager — Engagement manager | overview, clients, engagements, team, documents, accounting, audit, reviews, commercial-review, my-time, services | Scope/team/timing/budget and file review, separate commercial authority. No own-work independent approval, management/partner/EQR substitution or hidden scope change. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-012](#as-par-012) |
| preparer — Preparer / associate | overview, documents, accounting, audit, reviews, my-time | Assigned source/mapping/schedule/procedure preparation, evidence, responses and own time. No own clearance, release/opinion or firm-ledger posting by mere assignment. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-018](#as-par-018) |
| reviewer — Senior reviewer | overview, documents, accounting, audit, reviews, my-time | Independent current-version technical checks and issuer/replacement review disposition. No clearance of own response or substitute management/partner/EQR decision. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-004](#as-par-004) |
| eqr — Engagement quality reviewer | overview, quality | Eligible assigned significant-judgment/package review and concerns. No engagement preparation/partner role, self-expanded access or stale completion. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-026](#as-par-026) |
| client_admin — Client administrator | overview, portal, client-team, client-requests, client-deliverables | Permitted contact nominations, responsibility and access requests, published files. No self-promotion to signatory, staff-role grant or internal workpaper/economics access. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-015](#as-par-015) |
| client_finance — Client finance contributor | overview, portal, client-requests, client-deliverables | Assigned uploads/replacements/factual replies and expressly published deliverables. No self-adequacy, management approval or internal review clearance. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-013](#as-par-013) |
| client — Client authorized signatory | overview, portal, client-requests, client-approvals, client-deliverables | Verified terms/adjustment/accounts/representation decisions and receipt acknowledgement. No auditor opinion choice, internal deliberations or stale-version approval. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-024](#as-par-024) |
| billing — Billing officer | overview, billing, commercial-requests | Firm invoice preparation/approved issue, receipts and exception requests. No client ledger authority, own fee-exception approval or professional-release permission. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-029](#as-par-029) |
| records — Records administrator | overview, records, handover | Authorized assembly, manifests, hold/disposition/handover requests and observed protection. No signed-content editing, self-authorized restricted export or requested-as-observed protection. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-030](#as-par-030) |
| admin — System administrator | overview, administration, access-requests, operations | Technical identities/approved grants/configuration, jobs and recovery. No automatic partner/EQR/signatory power, own scope expansion or business-approval bypass. | partial — [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005), [AS-PAR-003](#as-par-003) |

<a id="workspace"></a>
## 8. Workpaper workspace and audit-area coverage

### 8.1 Six-tab contract

| Prototype tab | Existing equivalent | Remaining delta / coverage | Story |
| --- | --- | --- | --- |
| Overview | Workpaper details, objective, procedure and revision | partial — current assertions/risks/materiality/population/people/dependencies and unified audit-area context | [AS-PAR-018](#as-par-018) |
| Guidelines | Adopted programme/source procedure wording | partial — approved methodology/version, procedural guidance and documentation requirements together | [AS-PAR-017](#as-par-017), [AS-PAR-018](#as-par-018) |
| Workbook | Server-backed work-performed/conclusion draft and submission | partial — approved template and completed workbook attachment/version workflow; template download is not work execution | [AS-PAR-014](#as-par-014), [AS-PAR-018](#as-par-018) |
| Evidence | Existing source references, PBC records and accounting-evidence links | partial — scoped PBC/direct picker, immutable version binding and replacement/unlink impact | [AS-PAR-019](#as-par-019) |
| Clearance | AuditProgramService result review and separate review-point page | partial — consistent response/independent clearance/current-manifest gate and actor/version stamp | [AS-PAR-004](#as-par-004), [AS-PAR-018](#as-par-018) |
| History | Immutable submission history | partial — combined file/evidence/response/clearance/reopening/stale-dependency history | [AS-PAR-018](#as-par-018), [AS-PAR-019](#as-par-019) |

### 8.2 All twenty numbered prototype workpaper acceptance groups

The following **local trace IDs P:WP-01–20** refer to numbered groups in [P-WP](#e-p-wp); they are not original source.json IDs or a claim of twenty individual assertions. The fixture may use A1 cash/bank, B1 receivables, C1 assets and F1 completion. Production never inherits their fictional names, dates, hashes, balances or already-cleared states.

| Local trace ID | Prototype scenario group | Coverage | Production acceptance adaptation | Owner |
| --- | --- | --- | --- | --- |
| P:WP-01 | Seed includes four workpapers; two current clearances produce 50% | proposed | Create an isolated equivalent browser fixture, never seed real work as cleared. | [AS-PAR-018](#as-par-018) |
| P:WP-02 | Audit page shows progress and Workspace actions | partial | Derive current approved progress from persisted scope, not demo counts. | [AS-PAR-018](#as-par-018) |
| P:WP-03 | Open the six-tab workspace | proposed | Native accessible Blazor composition remains to be completed. | [AS-PAR-018](#as-par-018) |
| P:WP-04 | Switch Overview, Guidelines, Workbook, Evidence, Clearance, History | proposed | Exercise all six with stable context, keyboard focus and refresh. | [AS-PAR-018](#as-par-018) |
| P:WP-05 | Cash/bank guidelines show procedures and required documentation | partial | Approved actual methodology/version, not unapproved ISA labels as signoff. | [AS-PAR-017](#as-par-017) |
| P:WP-06 | Template download does not change workpaper completion | proposed | Download and approval status are separate business actions. | [AS-PAR-014](#as-par-014) |
| P:WP-07 | Workbook replacement increases version, invalidates clearance, preserves history and lowers progress | partial | Bind actual byte version and dependency manifest; fixture may move 50%→25%. | [AS-PAR-019](#as-par-019) |
| P:WP-08 | Link an additional PBC evidence item | partial | Guard scope, current accepted version and purpose. | [AS-PAR-019](#as-par-019) |
| P:WP-09 | Attach direct confirmation evidence | partial | Preserve independent provenance; sample confirmation is not a live response. | [AS-PAR-020](#as-par-020) |
| P:WP-10 | Unlink evidence | partial | New draft revision only; do not alter submitted historical links. | [AS-PAR-019](#as-par-019) |
| P:WP-11 | Preparer submits workpaper with required conclusion | partial | Existing draft snapshot flow; add exact completed-file/evidence manifest checks. | [AS-PAR-018](#as-par-018) |
| P:WP-12 | Preparer cannot clear workpaper | partial | Guarded result review exists; apply identity-based rule to all paths. | [AS-PAR-004](#as-par-004) |
| P:WP-13 | Reviewer raises note and work moves to changes required | partial | Version/severity/owner/blocking-scope note workflow required. | [AS-PAR-004](#as-par-004) |
| P:WP-14 | Reviewer cannot clear with unresolved notes | partial | Re-evaluate all relevant current blocking points in command. | [AS-PAR-004](#as-par-004) |
| P:WP-15 | Preparer response records new version and returns work for review | proposed | Persist response; never auto-clear it. | [AS-PAR-004](#as-par-004) |
| P:WP-16 | Eligible reviewer independently clears the note | partial | Replace direct Cleared mutation with qualified issuer/replacement decision. | [AS-PAR-004](#as-par-004) |
| P:WP-17 | Independent workpaper clearance stores reviewer/version/time and is current | partial | Append exact-manifest clearance distinct from generic status. | [AS-PAR-019](#as-par-019) |
| P:WP-18 | TB source replacement invalidates affected clearance and preserves history | partial | Current progress can fall to zero in the fixture; retain all old stamps. | [AS-PAR-019](#as-par-019) |
| P:WP-19 | Audit index renders stale/changes-required state | partial | Compute from current dependency freshness, not old reviewed status. | [AS-PAR-021](#as-par-021) |
| P:WP-20 | N/A changes the applicable denominator | partial | Adapt the prototype direct toggle: approved specific rationale and independent disposition must precede denominator change. | [AS-PAR-021](#as-par-021) |

### 8.3 Existing twenty-section / 165-procedure audit catalogue

The existing audit gap plan owns the original source wording, AWP identifiers, field detail and primary story assignments. This document does not renumber them. The adopted programme test validates the catalogue and selected commands; it does not prove all account-area browser executions. For every original AWP item, retain its original AS-AUD ownership and add the relevant AS-PAR execution/evidence/review link. [OLD-AUD](#e-old-aud), [CATALOG](#e-catalog), [T-AuditProgramWorkflowTests](#e-t-auditprogramworkflowtests).

| Source section (original AWP IDs remain in prior plan) | Required source work, summarized | Coverage / evidence boundary | Parity owner |
| --- | --- | --- | --- |
| 1. Planning & Risk Assessment | Registration/prior reports/current TB/opening continuity/business/risks/materiality/strategy | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-017](#as-par-017), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 2. Cash & Bank | All banks, TB/ledger/statement ties, direct confirmations, reconciling items, subsequent clearance and selected transactions | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-020](#as-par-020), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 3. Trade Receivables | Ageing tie, significant/overdue balances, confirmation/alternatives, subsequent receipts, ECL and cut-off | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-022](#as-par-022), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 4. Inventory | Listing tie, observed/test counts, quantities, costs, slow-moving/obsolete/NRV and cut-off | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-021](#as-par-021), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 5. Revenue / Sales | Listing/totals, trend/risk selection, orders/invoices/delivery, calculations, receipts/credit notes and cut-off | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-020](#as-par-020), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 6. Purchases & Trade Payables | Ageing/supplier statements/confirmations, purchase evidence, later payments, unrecorded liabilities and cut-off | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-020](#as-par-020), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 7. Fixed Assets | Register/opening tie, additions/capitalization/physical verification/disposals, depreciation/lives and impairment | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-022](#as-par-022), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 8. Expenses | Listing/analytics/risk samples, support/authorization/payment, classification/capitalization and cut-off | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-020](#as-par-020), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 9. Payroll | GL tie, employee samples/contracts, gross-to-net, bank payments/new/terminated staff and anomalies | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-022](#as-par-022), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 10. Loans & Borrowings | Schedule/openings/confirmations/agreements, receipts/repayments/interest/classification/security/covenants | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-022](#as-par-022), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 11. Equity / Share Capital | Opening/equity movements, registration/shareholding/dividends/retained earnings and statements | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-022](#as-par-022), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 12. Related Parties | Management list, directors/owners, transactions/balances/confirmations/recording and disclosure | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-022](#as-par-022), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 13. Tax & Statutory Liabilities | Computation/return/evidence, GL/payment/liability/penalty/correspondence/provision and disclosure | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-022](#as-par-022), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 14. Journal Entries & Fraud | Complete journal listing, manual/high-value/late entries, risk samples, support/authorization/reversals and indicators | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-023](#as-par-023), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 15. Analytical Review | Prior/monthly/trend/margin/ratio/days analysis, fluctuations and corroborated management explanations | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-022](#as-par-022), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 16. Going Concern | Management assessment, working capital/cash-flow/finance/repayments, assumptions/subsequent trading and conclusion | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-025](#as-par-025), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 17. Subsequent Events | Post-period transactions/minutes/financing/assets/litigation, management discussion and adjustment/disclosure decisions | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-025](#as-par-025), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 18. Financial Statements & Disclosures | Audited TB/statements/CF/equity/policy/estimates/comparatives/notes and consistency checks | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-024](#as-par-024), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 19. Audit Differences & Adjustments | All differences/management response/impact/unadjusted totals/materiality/conclusion and reflected agreed entries | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-023](#as-par-023), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |
| 20. Final Completion & Audit Report | All sections/risks/reviews/current materiality, going concern/subsequent events, disclosures/representations and signed report | partial — [CATALOG](#e-catalog), [FIELD](#e-field), [T-AuditFieldworkWorkflowTests](#e-t-auditfieldworkworkflowtests); existing specialist cores are reusable, complete item-level UI and current evidence must be verified. | [AS-PAR-025](#as-par-025), [AS-PAR-018](#as-par-018), [AS-PAR-021](#as-par-021) |

Prototype audit-area families outside these common areas—such as investment/lease or specialist work—remain subject to the selected approved programme and service profile. Do not silently treat an unmodelled procedure as N/A or infer professional completeness from the catalogue row count. A future verifier must enumerate original AWP IDs from the pinned existing plan and produce a per-item procedure/result/review evidence report; that report is not generated or claimed here.

<a id="supplemental"></a>
## 9. Unnumbered specification tables and service coverage

The local identifiers below are additive locators, not invented native requirements. They prevent unnumbered tables, input contracts and worked examples from disappearing from traceability. The entire referenced source table remains normative; summaries do not reduce its field/detail contract. [P-SOURCE](#e-p-source).

### 9.1 Complete source-section disposition

| Local locator | Canonical source section | Coverage inventory | Status | Owners |
| --- | --- | --- | --- | --- |
| P:SEC-01 | Purpose/objectives | OBJ-01–05 | partial | [AS-PAR-001](#as-par-001), [AS-PAR-005](#as-par-005), [AS-PAR-022](#as-par-022) |
| P:SEC-02 | Roles and governance | 14 personas; GOV-01–04; permissions.json | partial | [AS-PAR-003](#as-par-003), [AS-PAR-004](#as-par-004), [AS-PAR-005](#as-par-005) |
| P:SEC-03 | Hierarchy, modes and field rules | DAT-01–04 and local hierarchy/mode contracts | partial | [AS-PAR-002](#as-par-002), [AS-PAR-022](#as-par-022), [AS-PAR-057](#as-par-057) |
| P:SEC-04 | Firm/client/policy setup | SET-01–05 | partial | [AS-PAR-032](#as-par-032), [AS-PAR-036](#as-par-036), [AS-PAR-043](#as-par-043) |
| P:SEC-05 | State machines and universal transition rules | WF-01–05 and all state families below | partial | [AS-PAR-004](#as-par-004), [AS-PAR-012](#as-par-012), [AS-PAR-036](#as-par-036) |
| P:SEC-06 | Acquisition through conversion | LC-01–07 | partial | [AS-PAR-006](#as-par-006), [AS-PAR-007](#as-par-007), [AS-PAR-008](#as-par-008), [AS-PAR-009](#as-par-009), [AS-PAR-010](#as-par-010) |
| P:SEC-07 | Initial and annual intake | INT-01–03; Q01–Q40; Y01–Y30 grouped | partial | [AS-PAR-007](#as-par-007), [AS-PAR-011](#as-par-011) |
| P:SEC-08 | Annual engagement and planning | LC-08–10 | partial | [AS-PAR-011](#as-par-011), [AS-PAR-012](#as-par-012), [AS-PAR-017](#as-par-017) |
| P:SEC-09 | PBC and queries | LC-11–12; minimum request catalogue | partial | [AS-PAR-013](#as-par-013), [AS-PAR-014](#as-par-014), [AS-PAR-016](#as-par-016) |
| P:SEC-10 | Financial processing | LC-13–19; import options and statements | partial | [AS-PAR-022](#as-par-022), [AS-PAR-023](#as-par-023), [AS-PAR-024](#as-par-024), [AS-PAR-057](#as-par-057) |
| P:SEC-11 | Audit testing and professional documents | AUD-01–05; workpaper families; strategy/summary/opinion | partial | [AS-PAR-017](#as-par-017), [AS-PAR-018](#as-par-018), [AS-PAR-020](#as-par-020), [AS-PAR-021](#as-par-021), [AS-PAR-025](#as-par-025) |
| P:SEC-12 | Preparation through partner/EQR | LC-20–23 | partial | [AS-PAR-004](#as-par-004), [AS-PAR-024](#as-par-024), [AS-PAR-025](#as-par-025), [AS-PAR-026](#as-par-026), [AS-PAR-027](#as-par-027) |
| P:SEC-13 | Delivery, billing, closure and handover | LC-24–26; BIL-01–04 | partial | [AS-PAR-028](#as-par-028), [AS-PAR-029](#as-par-029), [AS-PAR-030](#as-par-030), [AS-PAR-031](#as-par-031), [AS-PAR-044](#as-par-044) |
| P:SEC-14 | End-to-end data transformation matrix | All transformation edges below | partial | [AS-PAR-006](#as-par-006), [AS-PAR-007](#as-par-007), [AS-PAR-010](#as-par-010), [AS-PAR-011](#as-par-011), [AS-PAR-018](#as-par-018), [AS-PAR-022](#as-par-022), [AS-PAR-028](#as-par-028), [AS-PAR-030](#as-par-030) |
| P:SEC-15 | Operational gate matrix | G01–G12 | partial | [AS-PAR-008](#as-par-008), [AS-PAR-010](#as-par-010), [AS-PAR-017](#as-par-017), [AS-PAR-021](#as-par-021), [AS-PAR-025](#as-par-025), [AS-PAR-028](#as-par-028), [AS-PAR-030](#as-par-030), [AS-PAR-031](#as-par-031) |
| P:SEC-16 | Automation and task templates | AUT-01–06 | partial | [AS-PAR-012](#as-par-012), [AS-PAR-035](#as-par-035), [AS-PAR-036](#as-par-036) |
| P:SEC-17 | Service catalogue | 12 original and 6 additional service variants | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-046](#as-par-046), [AS-PAR-047](#as-par-047), [AS-PAR-048](#as-par-048), [AS-PAR-049](#as-par-049), [AS-PAR-050](#as-par-050), [AS-PAR-051](#as-par-051), [AS-PAR-052](#as-par-052), [AS-PAR-053](#as-par-053), [AS-PAR-054](#as-par-054), [AS-PAR-055](#as-par-055), [AS-PAR-056](#as-par-056), [AS-PAR-057](#as-par-057), [AS-PAR-058](#as-par-058), [AS-PAR-059](#as-par-059), [AS-PAR-060](#as-par-060), [AS-PAR-061](#as-par-061), [AS-PAR-062](#as-par-062) |
| P:SEC-18 | Architecture | ARC-01–13; explicit .NET adaptation | partial | [AS-PAR-001](#as-par-001), [AS-PAR-002](#as-par-002), [AS-PAR-033](#as-par-033), [AS-PAR-039](#as-par-039), [AS-PAR-042](#as-par-042) |
| P:SEC-19 | Domain records and compatibility | DATA-01–08; 26 records; source mapping | partial | [AS-PAR-002](#as-par-002), [AS-PAR-007](#as-par-007), [AS-PAR-019](#as-par-019), [AS-PAR-022](#as-par-022), [AS-PAR-038](#as-par-038) |
| P:SEC-20 | Interfaces and sequences | API-01–07; command families and eight-step lifecycle sequence | partial | [AS-PAR-002](#as-par-002), [AS-PAR-033](#as-par-033), [AS-PAR-034](#as-par-034), [AS-PAR-036](#as-par-036), [AS-PAR-044](#as-par-044) |
| P:SEC-21 | Workspaces and report catalogue | UX-01–03; REP-01–04; 11 workspace rows | partial | [AS-PAR-005](#as-par-005), [AS-PAR-015](#as-par-015), [AS-PAR-018](#as-par-018), [AS-PAR-037](#as-par-037) |
| P:SEC-22 | Non-functional/operational targets | NFR-01–13; seven measured target categories | blocked | [AS-PAR-002](#as-par-002), [AS-PAR-014](#as-par-014), [AS-PAR-039](#as-par-039), [AS-PAR-040](#as-par-040), [AS-PAR-041](#as-par-041), [AS-PAR-042](#as-par-042) |
| P:SEC-23 | Modernization and cutover | TR-01–13; six delivery-stage approvals | blocked | [AS-PAR-001](#as-par-001), [AS-PAR-038](#as-par-038), [AS-PAR-045](#as-par-045) |
| P:SEC-24 | Acceptance and release traceability | AT-01–46; AC-01–03 | partial | [AS-PAR-040](#as-par-040), [AS-PAR-041](#as-par-041), [AS-PAR-045](#as-par-045) |
| P:SEC-25 | Worked lifecycle/financial example | Isolated 26,000 TB and 500 adjustment fixture | proposed | [AS-PAR-022](#as-par-022), [AS-PAR-023](#as-par-023), [AS-PAR-024](#as-par-024), [AS-PAR-041](#as-par-041), [AS-PAR-045](#as-par-045) |
| P:SEC-26 | Business approval decisions | Eleven owner decisions below | blocked | [AS-PAR-001](#as-par-001), [AS-PAR-036](#as-par-036), [AS-PAR-043](#as-par-043), [AS-PAR-045](#as-par-045) |
| P:SEC-27 | Glossary and source references | Retain vocabulary, source attribution and limitations; no software/professional certification | covered | [AS-PAR-001](#as-par-001) |

### 9.2 Hierarchy, modes and semantic state machines

| Local locator | Unnumbered contract / exact implementation consequence | Status | Owner |
| --- | --- | --- | --- |
| P:HIER-01 | Relationship→legal entity→financial period/book is separate from professional engagement. Multi-engagement dataset reference must not transfer approval/access ownership. | partial | [AS-PAR-010](#as-par-010), [AS-PAR-022](#as-par-022) |
| P:MODE-01 | Managed books: client-operational posting requires its own approved authority/ledger capability; do not reuse firm office balances. | blocked | [AS-PAR-057](#as-par-057) |
| P:MODE-02 | External books: source system authoritative; adjustments remain reporting proposals until approved inclusion/verified external reflection. | partial | [AS-PAR-022](#as-par-022), [AS-PAR-023](#as-par-023) |
| P:MODE-03 | Reporting-only: no unauthorized operational ledger write-back; preserve explicit purpose and lineage. | partial | [AS-PAR-022](#as-par-022) |
| P:FIELD-01 | Differentiate mandatory/conditional/optional; preserve original/normalized values, identifiers, precision and dates. Missing inputs do not create synthetic references/zeros. | partial | [AS-PAR-007](#as-par-007), [AS-PAR-022](#as-par-022) |
| P:STATE-LEAD | Inquiry/screening/discovery/qualified/deferred/disqualified/proposal/conversion outcomes use explicit transitions and retained reasons. | partial | [AS-PAR-006](#as-par-006), [AS-PAR-009](#as-par-009) |
| P:STATE-ACCEPT | Prepared/submitted/review/conditional/accepted/deferred/declined states preserve separate specialist and partner decisions. | partial | [AS-PAR-007](#as-par-007), [AS-PAR-008](#as-par-008) |
| P:STATE-ENGAGEMENT | Draft/authorized active/held/suspended/completion/issued/archive/reopened/terminated meanings derive from current gates and scope. | partial | [AS-PAR-010](#as-par-010), [AS-PAR-025](#as-par-025), [AS-PAR-031](#as-par-031) |
| P:STATE-PBC | Requested→Awaiting client→Uploaded→Under validation→Accepted, or Rejected/resubmission requested; versions and adequate review are distinct. | partial | [AS-PAR-013](#as-par-013) |
| P:STATE-QUERY | Open/answered/clarification/reopened/cleared retain response and eligible issuer/reviewer disposition. | partial | [AS-PAR-016](#as-par-016) |
| P:STATE-FINANCIAL | Staged/validated/accepted sources and draft/reviewed/approved/posted/reversed journals have distinct authorities, immutability and effects. | partial | [AS-PAR-022](#as-par-022), [AS-PAR-023](#as-par-023) |
| P:STATE-REVIEW | Working/submitted/changes required/responded/reviewed/current clearance/stale distinguish workpaper, note and decision state. | partial | [AS-PAR-004](#as-par-004), [AS-PAR-018](#as-par-018) |
| P:STATE-DELIVERY | Draft/approved/issued and queued/sent/delivered/viewed/acknowledged/filing accepted are separate events. | partial | [AS-PAR-028](#as-par-028), [AS-PAR-044](#as-par-044) |
| P:STATE-RECORDS | Issued/assembled/requested protection/observed protection/verified archive/held/disposition states do not claim external behavior without proof. | partial | [AS-PAR-030](#as-par-030) |

State labels above describe source semantics, not mandatory new enum names. Map them explicitly to current persisted states such as WORKING, SUBMITTED_SNAPSHOT and procedure result/review states. Do not rename historical enums or infer review from a status string merely to match the prototype label.

### 9.3 Logical records and exact source field tables

For each logical record, preserve **all mandatory and conditional/optional attributes in P:source §19.2**, not just the compressed locator below. The owning story must compare actual EF fields/FKs against the exact source row before adding anything. A named logical record may map to several existing .NET classes; new duplicate aggregate creation is not implied.

| Local locator | Logical source record | Field-contract locator (summary) | Coverage / owner |
| --- | --- | --- | --- |
| P:REC-01 | Lead and inquiry | Lead source/time/name/contact/service/owner/status; source/referral/deadline/duplicate evidence | partial — [AS-PAR-006](#as-par-006) |
| P:REC-02 | Client relationship | Legal/display identity, relationship owner, acceptance/lifecycle; group/billing/service preferences | partial — [AS-PAR-010](#as-par-010) |
| P:REC-03 | Legal entity | Client relation, legal type/jurisdiction/registration alternative, framework/currency; branches/parent/tax | partial — [AS-PAR-022](#as-par-022) |
| P:REC-04 | Contact and access grant | Verified contact identity, organization/role/scope/grant status; authority/delegation/expiry/language | partial — [AS-PAR-015](#as-par-015) |
| P:REC-05 | Ownership/control party | Entity/party relation, effective period, verified source; direct/indirect percentage/control | partial — [AS-PAR-007](#as-par-007) |
| P:REC-06 | Acceptance/continuance case | Entity/service/questionnaire revision, assessment/reviewer/decision/conditions and review triggers | partial — [AS-PAR-008](#as-par-008) |
| P:REC-07 | Questionnaire response | Template/respondent/answers/submission, evidence versions, prior comparison and clarifications | partial — [AS-PAR-007](#as-par-007) |
| P:REC-08 | Proposal and terms | Entity/service/version/fees/currency/responsibilities/validity/acceptance; variations and change orders | partial — [AS-PAR-009](#as-par-009) |
| P:REC-09 | Engagement | Entity/service template/period/owner/team/deadlines/status; group/specialist/EQR/predecessor | partial — [AS-PAR-012](#as-par-012) |
| P:REC-10 | Financial period | Entity/book dates/fiscal label/reporting basis/close; comparative mapping/reopening | partial — [AS-PAR-022](#as-par-022) |
| P:REC-11 | Task/work package | Objective/owner/due/requiredness/dependencies/completion evidence; effort/client wording | partial — [AS-PAR-012](#as-par-012) |
| P:REC-12 | Document/version | Scope/original name/opaque storage/type/hash/uploader/time/visibility; derivative/signature/hold/retention | partial — [AS-PAR-014](#as-par-014) |
| P:REC-13 | PBC request/query | Engagement/period/required evidence/owner/respondent/due/status; alternative/rejection/reminder/closure | partial — [AS-PAR-016](#as-par-016) |
| P:REC-14 | Import batch | Entity/book/period/mode/source hash/layout/mapping/totals/status; job/supersession/row exceptions/approval | partial — [AS-PAR-022](#as-par-022) |
| P:REC-15 | TB snapshot and line | Source/adjusted identity, scope/source lineage, account/dimension/debit/credit; comparatives/currency/AJEs | partial — [AS-PAR-022](#as-par-022) |
| P:REC-16 | Account/reporting mapping | Version/scoped source and destination/effectivity/reviewer; tax/cash-flow/disclosure/split rationale | partial — [AS-PAR-022](#as-par-022) |
| P:REC-17 | Journal header and line | Scope/date/source/reason/state; scoped accounts/amounts/currency/dimensions; consent/posting/reversal/evidence | partial — [AS-PAR-023](#as-par-023) |
| P:REC-18 | Reconciliation | Scope/source controls/items/conclusion/preparer/reviewer; confirmation/subsequent clearance | partial — [AS-PAR-022](#as-par-022) |
| P:REC-19 | Workpaper version | Engagement/objective/assertions/source/procedure/result/conclusion; sample/materiality/specialist/crossrefs | partial — [AS-PAR-018](#as-par-018) |
| P:REC-20 | Review note | Target/version/author/severity/category/action/assignee/status; responses/evidence/clearance authority | partial — [AS-PAR-004](#as-par-004) |
| P:REC-21 | Approval decision | Gate/exact object/version/hash/actor/role/time/rationale; delegation/conditions/supersession | partial — [AS-PAR-019](#as-par-019) |
| P:REC-22 | Financial report version | Entity/group/period/currency/framework/TB/map/template/status; restatements/notes/management | partial — [AS-PAR-024](#as-par-024) |
| P:REC-23 | Delivery package | Version/manifest/files/hashes/recipients/issue authority/status; submission/receipt/replacement | partial — [AS-PAR-028](#as-par-028) |
| P:REC-24 | Recurrence/deadline | Service/obligation/entity/rule/period/responsible owner/occurrence; calendar/extension/reminder | partial — [AS-PAR-036](#as-par-036) |
| P:REC-25 | Office billing record | Invoice/credit/time/receipt, engagement/amount/currency/authority/status; allocation/write-off/expenses | partial — [AS-PAR-029](#as-par-029) |
| P:REC-26 | Hold/offboarding record | Scope/reason/authority/effectivity/actions; handover/hold/disposition/certificate | partial — [AS-PAR-031](#as-par-031) |

### 9.4 PBC categories, input options and transformation contracts

The PBC template must retain the source §9 minimum request catalogue across permanent/entity information, prior-year reports, current TB/GL, bank/confirmations, AR/AP, inventory, assets, payroll, borrowing/equity/related parties, tax/statutory evidence and completion/representation material. Each required category gets specific period/entity coverage, format, responsible contact, due date and acceptance criteria; applicability comes from approved service scope rather than indiscriminate requests. [AS-PAR-013](#as-par-013) owns the catalogue; [AS-PAR-020](#as-par-020) owns direct-confirmation provenance. This is **partial**, not proof that a seeded list collects or reviews every required item.

Import options in source §10 remain **partial** until their exposed paths are verified: CSV/XLSX layout, signed/debit-credit convention, source purpose (opening/movements/closing/detail/audit-only), multi-currency/date-basis/dimension fields, comparative/prior-period sources, explicit duplicate aggregation, original/normalized values and per-row exceptions. [AS-PAR-022](#as-par-022) must reject unsupported options instead of exposing inert controls.

| Local edge | Source §14 transformation / §20.3 sequence | Coverage / owner |
| --- | --- | --- |
| P:XFORM-01 | Inquiry → normalized lead/contact candidate/provenance | partial — [AS-PAR-006](#as-par-006) |
| P:XFORM-02 | Screening → reviewed discovery/opportunity/service scope | partial — [AS-PAR-006](#as-par-006) |
| P:XFORM-03 | Questionnaire/evidence → immutable acceptance/conditions | partial — [AS-PAR-008](#as-par-008) |
| P:XFORM-04 | Approved scope/rates → proposal revision/client response | partial — [AS-PAR-009](#as-par-009) |
| P:XFORM-05 | Acceptance/proposal → effective signed terms | partial — [AS-PAR-010](#as-par-010) |
| P:XFORM-06 | Lead/terms → linked client/entity/contact/grants | partial — [AS-PAR-010](#as-par-010) |
| P:XFORM-07 | Annual attested facts → delta/obligations/continuance | partial — [AS-PAR-011](#as-par-011) |
| P:XFORM-08 | Contract/template → scoped engagement/tasks/PBC/team | partial — [AS-PAR-012](#as-par-012) |
| P:XFORM-09 | Uploaded original → document version/validation/evidence links | partial — [AS-PAR-014](#as-par-014) |
| P:XFORM-10 | Raw financial rows → staged normalized data/approved source snapshot | partial — [AS-PAR-022](#as-par-022) |
| P:XFORM-11 | Source account → scoped chart/report/notes mapping | partial — [AS-PAR-022](#as-par-022) |
| P:XFORM-12 | Evidence + GL → reconciliation/items/conclusion | partial — [AS-PAR-022](#as-par-022) |
| P:XFORM-13 | Finding → AJE/technical and management decisions/reflection | partial — [AS-PAR-023](#as-par-023) |
| P:XFORM-14 | Source + unreflected AJEs → adjusted TB/report inputs | partial — [AS-PAR-022](#as-par-022) |
| P:XFORM-15 | Reviewed inputs → statements/notes/artifacts/approval manifest | partial — [AS-PAR-024](#as-par-024) |
| P:XFORM-16 | Authorized exact package → issue/dispatch/receipt/filing evidence | partial — [AS-PAR-028](#as-par-028) |
| P:XFORM-17 | Completed file → archive/hold/retention/renewal/handover lineage | partial — [AS-PAR-030](#as-par-030) |

Every edge preserves source IDs/versions, actor, scope and output identity. No edge implicitly approves the next edge. Source §20.3’s eight-step narrative must be replayed as the complete new-client/recurring-client acceptance journey under AS-PAR-045, including negative and retry states.

### 9.5 Interface, workspace, compatibility and delivery-stage tables

| Local contract | P:source §20.1 command family | Required cross-cutting contract | Owner |
| --- | --- | --- | --- |
| P:CMD-01 | Create inquiry | partial — exact scope/version, idempotency, validated business request, truthful result/error/correlation; original source table retains request/result fields. | [AS-PAR-006](#as-par-006), [AS-PAR-002](#as-par-002) |
| P:CMD-02 | Submit acceptance | partial — exact scope/version, idempotency, validated business request, truthful result/error/correlation; original source table retains request/result fields. | [AS-PAR-008](#as-par-008), [AS-PAR-002](#as-par-002) |
| P:CMD-03 | Convert lead | partial — exact scope/version, idempotency, validated business request, truthful result/error/correlation; original source table retains request/result fields. | [AS-PAR-010](#as-par-010), [AS-PAR-002](#as-par-002) |
| P:CMD-04 | Instantiate engagement | partial — exact scope/version, idempotency, validated business request, truthful result/error/correlation; original source table retains request/result fields. | [AS-PAR-012](#as-par-012), [AS-PAR-002](#as-par-002) |
| P:CMD-05 | Stage TB or transactions | partial — exact scope/version, idempotency, validated business request, truthful result/error/correlation; original source table retains request/result fields. | [AS-PAR-022](#as-par-022), [AS-PAR-002](#as-par-002) |
| P:CMD-06 | Approve/post journal | partial — exact scope/version, idempotency, validated business request, truthful result/error/correlation; original source table retains request/result fields. | [AS-PAR-023](#as-par-023), [AS-PAR-002](#as-par-002) |
| P:CMD-07 | Submit/review workpaper | partial — exact scope/version, idempotency, validated business request, truthful result/error/correlation; original source table retains request/result fields. | [AS-PAR-018](#as-par-018), [AS-PAR-002](#as-par-002) |
| P:CMD-08 | Generate/issue package | partial — exact scope/version, idempotency, validated business request, truthful result/error/correlation; original source table retains request/result fields. | [AS-PAR-028](#as-par-028), [AS-PAR-002](#as-par-002) |
| P:CMD-09 | Close/reopen engagement | partial — exact scope/version, idempotency, validated business request, truthful result/error/correlation; original source table retains request/result fields. | [AS-PAR-031](#as-par-031), [AS-PAR-002](#as-par-002) |

| Local workspace | P:source §21.1 workspace | Coverage / implementation owner |
| --- | --- | --- |
| P:UI-01 | Lead workbench | partial — [AS-PAR-005](#as-par-005), [AS-PAR-006](#as-par-006) |
| P:UI-02 | Client 360 | partial — [AS-PAR-005](#as-par-005), [AS-PAR-010](#as-par-010) |
| P:UI-03 | Acceptance/annual review | partial — [AS-PAR-005](#as-par-005), [AS-PAR-011](#as-par-011) |
| P:UI-04 | Engagement cockpit | partial — [AS-PAR-005](#as-par-005), [AS-PAR-012](#as-par-012) |
| P:UI-05 | PBC and queries | partial — [AS-PAR-005](#as-par-005), [AS-PAR-016](#as-par-016) |
| P:UI-06 | TB/import centre | partial — [AS-PAR-005](#as-par-005), [AS-PAR-022](#as-par-022) |
| P:UI-07 | Accounting workspace | partial — [AS-PAR-005](#as-par-005), [AS-PAR-022](#as-par-022) |
| P:UI-08 | Workpaper/review desk | partial — [AS-PAR-005](#as-par-005), [AS-PAR-018](#as-par-018) |
| P:UI-09 | Reporting/delivery | partial — [AS-PAR-005](#as-par-005), [AS-PAR-028](#as-par-028) |
| P:UI-10 | Practice operations | partial — [AS-PAR-005](#as-par-005), [AS-PAR-029](#as-par-029) |
| P:UI-11 | Administration | partial — [AS-PAR-005](#as-par-005), [AS-PAR-032](#as-par-032) |

| Local compatibility row | P:source §19.4 preserved source concept | Coverage / owner |
| --- | --- | --- |
| P:COMPAT-01 | Customer/contact → client and authorized entity grants | partial — [AS-PAR-010](#as-par-010), [AS-PAR-038](#as-par-038) |
| P:COMPAT-02 | Project → explicit .NET engagement | partial — [AS-PAR-012](#as-par-012), [AS-PAR-038](#as-par-038) |
| P:COMPAT-03 | Tasks/milestones → versioned work packages | partial — [AS-PAR-012](#as-par-012), [AS-PAR-038](#as-par-038) |
| P:COMPAT-04 | Strategy/summary/opinion/announcement → typed reviewed documents | partial — [AS-PAR-017](#as-par-017), [AS-PAR-038](#as-par-038) |
| P:COMPAT-05 | Accounts/journals → confirmed entity/book ownership | partial — [AS-PAR-022](#as-par-022), [AS-PAR-038](#as-par-038) |
| P:COMPAT-06 | Accounting history → preserved posting/import provenance | partial — [AS-PAR-038](#as-par-038), [AS-PAR-038](#as-par-038) |
| P:COMPAT-07 | Attachments → managed immutable document versions | partial — [AS-PAR-014](#as-par-014), [AS-PAR-038](#as-par-038) |
| P:COMPAT-08 | Comments/signatures → discussion versus verified decisions/signing evidence | partial — [AS-PAR-027](#as-par-027), [AS-PAR-038](#as-par-038) |
| P:COMPAT-09 | Recurring tasks/reminders → reviewed obligation rules and deduplicated occurrences | partial — [AS-PAR-036](#as-par-036), [AS-PAR-038](#as-par-038) |

Source §23.5 delivery-stage exit approvals are retained as local IDs **P:STAGE-01 Foundation; 02 Platform/identity; 03 Client lifecycle; 04 Financial processing; 05 Professional delivery; 06 Migration/production**. Their named owner roles and deliverables remain in the canonical table. All are **partial/blocked as complete-stage acceptance**, not done merely because modules exist; AS-PAR-001/045 bind adoption and complete-system evidence without altering the active WBS.

### 9.6 All service variants

| Local service locator | Canonical service | Coverage | Owners | Remaining acceptance |
| --- | --- | --- | --- | --- |
| P:SVC-01 | 17.1 External audit | partial | [AS-PAR-017](#as-par-017), [AS-PAR-018](#as-par-018), [AS-PAR-020](#as-par-020), [AS-PAR-021](#as-par-021), [AS-PAR-025](#as-par-025), [AS-PAR-026](#as-par-026), [AS-PAR-027](#as-par-027), [AS-PAR-028](#as-par-028), [AS-PAR-030](#as-par-030), [AS-PAR-045](#as-par-045) | Core accounting/audit services exist; complete professional/browser/live release path remains gated. |
| P:SVC-02 | 17.2 Internal audit | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-046](#as-par-046) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-03 | 17.3 Business valuation | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-047](#as-par-047) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-04 | 17.4 Feasibility study | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-048](#as-par-048) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-05 | 17.5 Forensic audit and investigation | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-049](#as-par-049) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-06 | 17.6 Financial forecasting and projections | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-050](#as-par-050) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-07 | 17.7 AML compliance review | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-051](#as-par-051) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-08 | 17.8 Management consulting | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-052](#as-par-052) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-09 | 17.9 Fixed-asset verification and tagging | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-053](#as-par-053) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-10 | 17.10 Inventory management audit | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-054](#as-par-054) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-11 | 17.11 ERP implementation and independent review engagement | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-055](#as-par-055) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-12 | 17.12 Corporate IFRS/IAS training | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-056](#as-par-056) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-13 | 17.13: recurring bookkeeping Recurring managed bookkeeping | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-057](#as-par-057) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-14 | 17.13: annual accounts/compilation Annual accounts and compilation | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-058](#as-par-058) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-15 | 17.13: review engagement Review engagement | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-059](#as-par-059) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-16 | 17.13: agreed-upon procedures Agreed-upon procedures | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-060](#as-par-060) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-17 | 17.13: tax compliance Tax compliance | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-061](#as-par-061) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |
| P:SVC-18 | 17.13: payroll administration Payroll administration | blocked | [AS-PAR-043](#as-par-043), [AS-PAR-062](#as-par-062) | Shared platform / specialist cores are reuse candidates; scope-specific methodology, end-to-end journey and production activation are not established. |

### 9.7 Proposed performance targets — not existing measurements

| Local target | Source §22.2 proposed target | Current acceptance / owner |
| --- | --- | --- |
| P:PERF-01 — Interactive operations | p95 ≤2 seconds for ordinary forms/lists, 100 concurrent authenticated sessions, external latency excluded | blocked — approved reference workload and actual measured evidence required; [AS-PAR-042](#as-par-042) |
| P:PERF-02 — Large TB import | 100,000 valid rows within five minutes on approved hardware/storage with full validation/mapping and visible background progress | blocked — approved reference workload and actual measured evidence required; [AS-PAR-042](#as-par-042) |
| P:PERF-03 — Financial report generation | Standard scoped report within 60 seconds over one million ledger lines on an approved indexed reference workload | blocked — approved reference workload and actual measured evidence required; [AS-PAR-042](#as-par-042) |
| P:PERF-04 — Availability | 99.5% monthly application availability with approved measurement/maintenance boundaries | blocked — approved reference workload and actual measured evidence required; [AS-PAR-042](#as-par-042) |
| P:PERF-05 — Recovery point | At most 15 minutes of committed-data loss with coordinated database/document recovery | blocked — approved reference workload and actual measured evidence required; [AS-PAR-042](#as-par-042) / [AS-PAR-039](#as-par-039) |
| P:PERF-06 — Recovery time | Restore agreed production service within four hours including keys/dependencies/access checks | blocked — approved reference workload and actual measured evidence required; [AS-PAR-042](#as-par-042) / [AS-PAR-039](#as-par-039) |
| P:PERF-07 — Financial integrity | Zero duplicate postings and zero unexplained balance/scope differences under idempotency, fault injection and migration tests | blocked — approved reference workload and actual measured evidence required; [AS-PAR-042](#as-par-042) |

These source targets are not replaced by the repository’s smaller local benchmark. Preserve that local evidence with its actual data volume and observed timings. A qualified technical/business owner must approve or explicitly revise targets before they become acceptance thresholds.

### 9.8 Worked example: separate, deterministic lineage fixture

P:source §25 defines a fictional external-books example. Preserve it as **P:EXAMPLE-01**, a proposed cross-workflow fixture owned by [AS-PAR-022](#as-par-022), [AS-PAR-023](#as-par-023), [AS-PAR-024](#as-par-024), [AS-PAR-041](#as-par-041). It is distinct from the UI demo and S:Appendix D.

| Account | Debit | Credit |
| --- | --- | --- |
| Cash | 10,000 | 0 |
| Receivables | 5,000 | 0 |
| Equipment cost | 8,000 | 0 |
| Operating expenses | 3,000 | 0 |
| Payables | 0 | 3,000 |
| Loan | 0 | 7,000 |
| Opening equity | 0 | 10,000 |
| Revenue | 0 | 6,000 |
| Total | 26,000 | 26,000 |

Given approved source TB-001-v1, when an independently reviewed and management-accepted reporting entry debits depreciation expense 500 and credits accumulated depreciation 500 once, then profit is 2,500, assets after accumulated depreciation are 22,500, liabilities 10,000 and closing equity 12,500. If TB-002-v1 already contains that entry, approved reflection/supersession must keep profit 2,500 rather than applying another 500. Closing TB alone is insufficient cash-flow input: missing opening/movement/noncash evidence remains blocked. Follow RN-001 through independent review, exact management/partner/EQR decisions, PKG-001 versioned dispatch/receipt/archive and FY-B current attestation without copying approvals. Actual specimen IDs can be synthetic fixture references, not production GUID defaults.

### 9.9 Owner configuration decisions retained from source §26

| Local decision | Required owner-supplied selection/evidence | Approving role | Status / story |
| --- | --- | --- | --- |
| P:DEC-01 — Jurisdiction/reporting | Enabled jurisdictions/framework/standards/tax rules/effectivity | Qualified accounting/tax owner and partner | blocked until applicable approval evidenced — [AS-PAR-036](#as-par-036) |
| P:DEC-02 — Ledger mode | Managed/external/reporting-only by entity/book, source and write-back authority | Accounting owner and management | blocked until applicable approval evidenced — [AS-PAR-057](#as-par-057) |
| P:DEC-03 — Entity/group ownership | Registry, components, account ownership and consolidation rules | Management and accounting owner | blocked until applicable approval evidenced — [AS-PAR-022](#as-par-022) |
| P:DEC-04 — Professional authority | Reviewer/partner/EQR/delegation/conflict rules | Practice leadership and quality owner | blocked until applicable approval evidenced — [AS-PAR-003](#as-par-003) |
| P:DEC-05 — Acceptance/continuance | Evidence, conditions, routes and frequency | Compliance lead and partner | blocked until applicable approval evidenced — [AS-PAR-008](#as-par-008) |
| P:DEC-06 — Materiality | Approved bases/ranges/performance/trivial/rounding/rationale | Partner and methodology owner | blocked until applicable approval evidenced — [AS-PAR-017](#as-par-017) |
| P:DEC-07 — Records policy | Retention/holds/assembly/access/disposition | Records owner with professional/legal validation | blocked until applicable approval evidenced — [AS-PAR-030](#as-par-030) |
| P:DEC-08 — Integration policy | Approved providers/scopes/tenant mappings/signatory/submission authority | Technical and relevant business owners | blocked until applicable approval evidenced — [AS-PAR-033](#as-par-033) |
| P:DEC-09 — Operational service levels | Reference workload/infrastructure/availability/recovery/support | Technical owner and practice leadership | blocked until applicable approval evidenced — [AS-PAR-042](#as-par-042) |
| P:DEC-10 — Commercial policy | Fees/discounts/triggers/retainers/credits/write-offs/collections | Commercial/finance owner | blocked until applicable approval evidenced — [AS-PAR-029](#as-par-029) |
| P:DEC-11 — Migration/cutover | Mappings/reconciliation/exceptions/freeze/delta/recovery checkpoints | Data/accounting/release owners | blocked until applicable approval evidenced — [AS-PAR-038](#as-par-038) |

Recorded approvals such as STE-METH-APP-001 and its addendum remain valid only within their recorded scope; this table does not revoke them or claim all methodology is absent. Each decision must record owner, selected value, rationale, scope/version, approval evidence and activation condition. No name in a sample, environment variable or generated story is an actual approval.

<a id="configuration"></a>
## 10. Existing configuration and future composition changes

This section is a read-only configuration inventory and implementation constraint. **Do not apply these settings as part of this documentation delivery.** Existing key names are retained; secret values, tenant/site IDs, credentials, certificate material and private endpoints are deliberately omitted. Inspect the pinned host and options classes before a later configuration change. [CONFIG](#e-config), [WEB-HOST](#e-web-host), [WORKER](#e-worker), [RELEASE-SAFETY](#e-release-safety).

| Existing key / environment variable | Observed purpose | Required implementation constraint | Owning stories |
| --- | --- | --- | --- |
| ConnectionStrings:AuditSphere | Existing Web/Worker PostgreSQL connection | Private environment/secret configuration only. Verify target ownership and isolation before migrations/tests; never substitute an in-memory store. | [AS-PAR-002](#as-par-002), [AS-PAR-038](#as-par-038), [AS-PAR-039](#as-par-039), [AS-PAR-040](#as-par-040) |
| Identity:TenantId; Identity:ClientId; Identity:ClientSecret; Identity:CallbackPath | Existing Web identity configuration | Use only the selected approved identity composition. Do not create tenant IDs/secrets or imply consent from populated fields. Live negative identity fixtures remain required. | [AS-PAR-032](#as-par-032), [AS-PAR-034](#as-par-034) |
| DevelopmentIdentity:Enabled; DevelopmentIdentity:TenantId; DevelopmentIdentity:Subject | Existing development/test actor configuration | Never enable in production or allow the browser to choose authoritative roles. Proposed E2E fixtures require an explicitly approved isolated Test composition. | [AS-PAR-002](#as-par-002), [AS-PAR-034](#as-par-034), [AS-PAR-041](#as-par-041) |
| ExternalEffects:Enabled | Existing host/worker gate; disabled baseline | A true value does not approve a general production provider composition. The inspected Web host still refuses live composition; the Worker has only the isolated Acceptance-mail branch. Preserve those refusals until independently authorized changes and tests exist. | [AS-PAR-033](#as-par-033), [AS-PAR-034](#as-par-034), [AS-PAR-035](#as-par-035), [AS-PAR-042](#as-par-042) |
| Application:FirmId; Application:InstallationId; Application:PublicBaseUrl; Application:TimeZone | Existing application identity/public-origin/timezone configuration | Use owner-approved values, not sample client or firm identifiers. A public-origin value must not authorize arbitrary links or cross-tenant destinations. | [AS-PAR-005](#as-par-005), [AS-PAR-032](#as-par-032), [AS-PAR-036](#as-par-036) |
| Setup:FirmId; Setup:InstallationId; Setup:BootstrapProofHash; Setup:InitialAdministratorTenantId; Setup:InitialAdministratorObjectId | Existing protected bootstrap configuration | Persist only the approved hash/identity bindings; do not commit a raw bootstrap capability or invent an initial administrator. Client folder readiness still requires provider evidence. | [AS-PAR-003](#as-par-003), [AS-PAR-032](#as-par-032), [AS-PAR-034](#as-par-034) |
| Application:AllowSimulationAdapters (Web); AllowSimulationAdapters (Worker) | Existing differently located host keys | Document this host-specific distinction; do not silently rename either key or infer production approval from a development default. Simulation adapters may compose only in the explicitly permitted test environment. | [AS-PAR-033](#as-par-033), [AS-PAR-040](#as-par-040), [AS-PAR-041](#as-par-041), [AS-PAR-042](#as-par-042) |
| FeatureActivation:LiveAccountingRelease; FeatureActivation:LiveAuditRelease; FeatureActivation:LiveFirmLedgerPosting | Existing feature activation keys; false baseline | Keep disabled until the corresponding entire enabled-service acceptance path and owner approval exist. Local test completion does not set these values. | [AS-PAR-028](#as-par-028), [AS-PAR-042](#as-par-042), [AS-PAR-043](#as-par-043), [AS-PAR-045](#as-par-045) |
| ReleaseSafety:RequireSignatureLineage; ReleaseSafety:RequireProtectionAttestation; ReleaseSafety:RequireExternalCheckpointBeforeDelivery | Existing release-safety keys | Development defaults are not an approved production policy. Do not turn required checks off to obtain a green journey; exact signing/records/checkpoint obligations follow the approved profile. | [AS-PAR-027](#as-par-027), [AS-PAR-028](#as-par-028), [AS-PAR-030](#as-par-030), [AS-PAR-042](#as-par-042) |
| Worker:Group; Worker:FirmId; Worker:DeploymentEpoch | Existing Worker composition and fencing | Retain positive epoch, assigned firm and group validation. General, processing, records and isolated mail are not interchangeable authority modes. | [AS-PAR-033](#as-par-033), [AS-PAR-035](#as-par-035), [AS-PAR-039](#as-par-039), [AS-PAR-042](#as-par-042) |
| Mail:Provider | Existing isolated Acceptance-mail provider selector | Graph, SMTP and Resend implementations are present. Select only an already approved provider; do not add a vendor, enable all vendors or require a new subscription to close this backlog. | [AS-PAR-035](#as-par-035) |
| GraphMail:TenantId; GraphMail:ClientId; GraphMail:SenderMailbox; GraphMail:CertificatePath; GraphMail:PrivateKeyPath | Existing Graph mail configuration | Certificate/key paths are private mounted resources supplied by the authorized operator. Sender/resource authorization and observed delivery are separate from successful construction. | [AS-PAR-035](#as-par-035) |
| SmtpMail:Host; SmtpMail:Port; SmtpMail:UseSsl; SmtpMail:SenderAddress; SmtpMail:SenderName; SmtpMail:Username; SmtpMail:Password | Existing SMTP mail configuration | Only relevant when that approved existing adapter is selected. Credentials and sender authorization remain external; no fabricated host/account or plaintext secret in the document. | [AS-PAR-035](#as-par-035) |
| ResendMail:ApiKey; ResendMail:SenderAddress | Existing Resend mail configuration | Only relevant when the owner selects this existing adapter. A catalogue entry does not authorize provider registration or sending to real recipients. | [AS-PAR-035](#as-par-035) |
| Storage:PbcProviderSimulationRoot; Storage:ReleaseCheckpointRoot | Existing local provider/checkpoint storage settings | Simulation storage is not the canonical production DMS. A local checkpoint directory does not prove independent custody; preserve the independent release-store requirement. | [AS-PAR-014](#as-par-014), [AS-PAR-028](#as-par-028), [AS-PAR-033](#as-par-033), [AS-PAR-039](#as-par-039) |
| Telemetry:Otlp:Endpoint | Existing telemetry configuration seam | An approved exporter endpoint and sensitive-log review are operational prerequisites, not something to invent in a story. Do not export client content, secrets or signature bytes. | [AS-PAR-042](#as-par-042) |
| AUDITSPHERE_TEST_CONNECTION | Existing test connection environment variable | Supply only through approved isolated runner configuration. Do not print its value, point it at production or overwrite developer environment files. | [AS-PAR-038](#as-par-038), [AS-PAR-040](#as-par-040), [AS-PAR-041](#as-par-041) |

### 10.1 Layer-by-layer change boundary

| Layer | Permitted future change when authorized | Not authorized / not required |
| --- | --- | --- |
| Domain | Extend the existing relevant aggregate with typed scope, revision, authority, evidence or lifecycle data identified by the story. Preserve original IDs and history. | A duplicate ledger, parallel workpaper engine, catch-all approval Boolean, or automatic conversion of historical approvals. |
| Application | Use existing ActorContext, AuthorizationDecision, CommandResult and guarded command/query seams. Add the smallest missing transition or query; retain transaction/freshness checks. | Unrestricted EF mutations in Razor; client-supplied authority; a second workflow platform or event broker. |
| Infrastructure | Add reviewed EF mappings/migrations, composite scope constraints and append-only history protection where needed. Extend existing adapters only after their missing contract is established. | Replacing PostgreSQL with SQLite/InMemory; copying a mock into production; starting a new canonical user-facing file store. |
| Blazor Interactive Server | Compose authorized typed view models, forms, queues, six-tab workpaper views and safe file endpoints around existing commands. Handle circuit refresh/conflicts/access loss. | Replacing the application with React/Vue; sending large document bytes through the interactive circuit; treating a role selector as authentication. |
| Configuration | Prefer existing key families and database-backed approved policy/version records. Every new key, if truly needed later, must be documented as proposed, typed, validated and fail-closed. | Undocumented flags that bypass signing, records, identity, self-review, migration or release checks. |
| CI/tests | Keep the current xUnit/PostgreSQL workflow; propose targeted changes and a separately approved Playwright lane under AS-PAR-040/041. | Editing active CI or installing browser dependencies in this documentation task; reporting unrun browser journeys as covered. |
| Documentation | Add future story-specific evidence references and explicitly authorized execution updates only after observed results. Link the existing plans as controlling historical records. | Rewriting SPECIFICATION, AGENTS, old AS-AUD stories, accounting/M365 plans or execution pointers merely to fit this new backlog. |

### 10.2 Future CI requirements, without a workflow patch

The inspected workflow performs restore/build, database migration, xUnit execution and readiness verification with PostgreSQL. Its `postgres:18` tag is not an immutable PostgreSQL 18.6 pin, and its action references are major-version references. These are future hardening decisions, not changes made here. [CI](#e-ci).

AS-PAR-040 must propose a reviewed compatible runner/database reference, locked dependency restore, migration-drift verification and explicit result artifacts. AS-PAR-041 must propose a separate browser lane with approved Playwright/browser versions, isolated databases and synthetic identities. No live tenant secrets are available to pull requests from untrusted code. Live-provider and recovery jobs require an explicit protected environment/runner decision and must not execute through the ordinary PR lane. Reuse existing CI permissions and artifact policy where adequate; do not invent secret names, runner labels or deployment credentials.

<a id="validation"></a>
## 11. Validation recipes and completion evidence

**All commands in this section are future implementation instructions, not commands executed for this documentation delivery.** A documentation-only change needs document checks, not a database migration, a tenant operation or a restore drill. Run application verification only after a separately authorized implementation change, with the approved scope and runner.

### 11.1 Test profiles and what they can prove

| Profile | Execution basis | Evidence sufficient for | Insufficient for |
| --- | --- | --- | --- |
| Document | Markdown parsing, links, ID coverage, dependency DAG and unchanged-file review | This deliverable is internally navigable and inventories the stated requirement sets. | An implemented feature, passing application test or external outcome. |
| Unit | Existing xUnit framework, deterministic calculation/state/validation tests | Specified local rules and arithmetic with explicit boundary cases. | Database constraints, browser usability or provider behavior. |
| Database / integration | Actual PostgreSQL 18.6 using existing disposable-schema fixture; real commands and persistence | Scoped FKs, atomicity, immutable evidence, concurrency, migration and idempotency behavior under the tested assumptions. | A UI journey, verified identity provider or independently protected records. |
| Provider contract | Existing adapter contract with bounded local test responses and explicit simulation mode | Request/result validation, retry classification and fail-closed handling. | A real message, SharePoint write, Purview protection, signature or filing acceptance. |
| Browser E2E — proposed | Approved Playwright runner against the real Blazor host and isolated PostgreSQL fixture | Actual navigation, role-scoped interactions, reload/circuit behavior, visible errors and resulting persisted transitions. | Live OIDC, storage enforcement or production signing when test identity/adapters are used. |
| Live tenant acceptance | Owner-authorized non-production tenant, exact provider composition/resources/identities and permitted positive/negative operations | Only the observed endpoint/permission/version behavior on those resources. | All tenants, all endpoints, independent custody or production approval by inference. |
| Separate-custody recovery | Approved protected restore environment and coordinated stores/keys/identity dependencies | Observed restoration, reconciliation, denial/fencing and measured RPO/RTO for the approved exercise. | Production recovery from a backup-success message or a same-workstation loopback restore. |

The profile names are acceptance classifications. Do not assume every existing xUnit test already carries a matching Trait, or use a filter that silently runs zero tests. Confirm the discovered case count and selected test methods before interpreting results. Reuse actual existing assertion methods; new method names and Playwright projects are proposed until committed and executed.

### 11.2 Preserve the current worktree and choose the test target

Before any future implementation, inspect the current branch, head, applicable root/nested AGENTS.md, existing dirty work and current source versions. Do not use cleanup, reset, checkout, stash or broad staging commands to make a worktree appear clean. Do not print private environment variables or enumerate untracked evidence content unless authorized.

```bash
# Read-only repository inspection before an authorized implementation.
git rev-parse HEAD
git status --short --untracked-files=all
```

Record the permitted changes separately from pre-existing changes. Confirm that the test database/runner is isolated and owner-approved before invoking any command that can create schemas, run migrations or start a host. The existing PgTestSchema fixture must be used as documented; its disposable cleanup must target only the schema it created. No application database reset or production credential reuse is permitted.

### 11.3 Build, focused tests and complete local regression

Use the pinned compatible repository toolchain. `EVIDENCE_DIR` below is a placeholder for an operator-approved **new, unique directory outside the repository**; this document does not set its value or authorize overwriting an existing directory. The test connection is supplied privately through the approved runner.

```bash
# FUTURE ONLY: run after an authorized implementation change.
dotnet --info
dotnet tool restore
dotnet restore AuditSphereOps.slnx --locked-mode
dotnet build AuditSphereOps.slnx --no-restore --configuration Release

# Existing focused test class; substitute only another verified existing/added class.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-build --no-restore --configuration Release \
  --filter 'FullyQualifiedName~AuditProgramWorkflowTests'

# Complete regression; do not treat an empty filtered run as a pass.
dotnet test AuditSphereOps.slnx --no-build --no-restore \
  --configuration Release \
  --logger 'trx;LogFileName=parity.trx' \
  --results-directory "$EVIDENCE_DIR"
```

For each story, run its relevant existing test classes first, then the full suite for the coherent implementation slice. Record actual discovered, passed, failed and skipped counts and the exact code commit. Do not preserve the old 246-test total as a success condition when legitimate new tests are added. A failure caused by unavailable PostgreSQL is an unavailable prerequisite, not permission to disable database assertions.

### 11.4 Migrations and restore verification

```bash
# FUTURE ONLY: model drift verification using the actual repository projects.
dotnet ef migrations has-pending-model-changes \
  --project src/AuditSphereOps.Infrastructure \
  --startup-project src/AuditSphereOps.Web \
  --configuration Release
```

Any story that changes persisted fields, constraints, seeds or evidence relationships must additionally verify a clean isolated schema and upgrade from the approved prior migration baseline. Compare pre/post row counts, financial totals, immutable-history hashes and ambiguous-record quarantine. Test rejection of cross-scope references, mutation of frozen records, duplicate retries and concurrent stale writes. Do not add a migration merely for a UI composition change.

Do not execute `database update` against an implicitly selected connection. A migration run requires an explicitly confirmed isolated target and approved runner; the normal production startup must not auto-apply migrations. Destructive downgrade of append-only evidence must remain refused rather than forced to pass.

For schema or recovery-sensitive changes, inspect the current `scripts/db/restore-drill.sh` inputs, destructive scope, cleanup and output locations before a separately authorized run. This document intentionally does not invent script flags or invoke it. The existing script/evidence may write a latest-evidence pointer: do not overwrite historical or untracked files to execute a new drill. Resolve a safe, authorized artifact location and record a new receipt. A production separate-custody drill is a different acceptance gate from the existing loopback rehearsal.

### 11.5 Proposed browser acceptance harness and journeys

A possible future project location is `tests/AuditSphereOps.E2E.Tests/`; it is **proposed, not an existing project or a command ready to run now**. Prefer an approved xUnit-compatible Playwright integration and a compatible pinned browser version. Reuse the actual Blazor application and existing fixture conventions rather than creating a parallel UI or a fake business server. Project creation, dependency installation and CI wiring belong to AS-PAR-041 and require their own reviewed change.

| Proposed test family | Required browser journey | Story scope |
| --- | --- | --- |
| E2E-PAR-01 | Assigned commercial user creates inquiry, handles duplicate/discovery, and reaches persisted proposal/terms without bypassing professional acceptance. | [AS-PAR-006](#as-par-006), [AS-PAR-009](#as-par-009), [AS-PAR-010](#as-par-010) |
| E2E-PAR-02 | Coordinator saves/resumes factual intake; compliance accesses restricted fields separately; partner decides only after current required clearances. | [AS-PAR-007](#as-par-007), [AS-PAR-008](#as-par-008) |
| E2E-PAR-03 | New annual attestation shows prior values, handles zero-base comparison and preserves old decisions; next engagement cannot inherit old approval. | [AS-PAR-011](#as-par-011), [AS-PAR-012](#as-par-012) |
| E2E-PAR-04 | Three separate client personas nominate/request access, upload/replace evidence and make only their permitted management decisions. | [AS-PAR-003](#as-par-003), [AS-PAR-013](#as-par-013), [AS-PAR-014](#as-par-014), [AS-PAR-015](#as-par-015), [AS-PAR-024](#as-par-024) |
| E2E-PAR-05 | Staff request → client reply/upload → administrative validation → technical adequacy/clarification; queued notification never displays as observed delivery. | [AS-PAR-013](#as-par-013), [AS-PAR-014](#as-par-014), [AS-PAR-016](#as-par-016), [AS-PAR-035](#as-par-035) |
| E2E-PAR-06 | All six workpaper tabs, template download, working-file replacement, evidence link/unlink, draft save/reload, submission and current independent clearance. | [AS-PAR-004](#as-par-004), [AS-PAR-018](#as-par-018), [AS-PAR-019](#as-par-019) |
| E2E-PAR-07 | Open review point → preparer response → denied self-clearance → eligible clearance → source change → stale/re-review and accurate progress. | [AS-PAR-004](#as-par-004), [AS-PAR-019](#as-par-019), [AS-PAR-021](#as-par-021) |
| E2E-PAR-08 | Actual imported TB/GL context, mapping, reconciliation, AJE/reflection and statement export; colliding account codes in another client never leak. | [AS-PAR-002](#as-par-002), [AS-PAR-022](#as-par-022), [AS-PAR-023](#as-par-023), [AS-PAR-024](#as-par-024) |
| E2E-PAR-09 | Manager completion, exact management package, conditional EQR concerns, representations and partner authorization; unmet professional gates remain blocked. | [AS-PAR-025](#as-par-025), [AS-PAR-026](#as-par-026), [AS-PAR-027](#as-par-027), [AS-PAR-028](#as-par-028) |
| E2E-PAR-10 | Approved invoice/WIP/receipt/exception actions remain in the firm ledger and do not clear an audit or records gate. | [AS-PAR-029](#as-par-029) |
| E2E-PAR-11 | Issued-package history, archive assembly, legal hold, controlled reopen/reissue and scoped handover with access revocation. | [AS-PAR-028](#as-par-028), [AS-PAR-030](#as-par-030), [AS-PAR-031](#as-par-031) |
| E2E-PAR-12 | Every permitted persona follows its routes and unauthorized deep links; revoke access mid-circuit, reload and verify no stale content or action survives. | [AS-PAR-002](#as-par-002), [AS-PAR-003](#as-par-003), [AS-PAR-005](#as-par-005) |
| E2E-PAR-13 | Protected M365 setup resumes; a waiting client workspace is not shown as a real remote folder; failure/reconciliation states remain truthful. | [AS-PAR-032](#as-par-032), [AS-PAR-033](#as-par-033), [AS-PAR-034](#as-par-034) |
| E2E-PAR-14 | Each approved enabled service follows its own full input/execution/review/output/closure journey; disabled catalogue entries stay unavailable. | [AS-PAR-043](#as-par-043), [AS-PAR-045](#as-par-045) |

Use separate browser contexts for each synthetic actor with explicit firm/client/engagement assignments. Exercise clicks/forms/navigation rather than invoking prototype JavaScript actions as a substitute for application behavior. Verify persisted records and exact artifact hashes after the UI transition. Include refresh, stale-version conflict, network interruption, circuit reconnection, keyboard/focus and denied download/search tests. A screenshot proves appearance, not authorization or atomicity; retain redacted trace plus database assertions. Never record real tokens, passwords, personal identity documents or confidential financial bytes in public traces.

Keep fixtures distinct: the prototype’s worked 26,000 TB/500 adjustment example, its four-workpaper demo with two initially clear entries, and the controlling specification’s separate Appendix D arithmetic bank are different scenarios. Preserve original fixture values and expected results; do not combine them into one fabricated “golden” dataset. The 165-item source catalogue requires an item-by-item adopted procedure/result/current review report for the selected engagement, not a hard-coded “100%” progress label.

### 11.6 Evidence record and promotion rules

Prefer the existing evidence format when it can express these fields. The following is a **proposed schema example**, not a completed result or a new mandatory framework. Null values deliberately indicate work not executed; the document does not supply an approval identity, time or result.

```json
{
  "schemaVersion": "proposed.parity-evidence.v1",
  "storyId": "AS-PAR-018",
  "prototypeRequirementIds": [
    "P:AUD-01",
    "P:LC-20",
    "P:WP-03"
  ],
  "specificationSections": [
    "21",
    "22",
    "43",
    "44"
  ],
  "implementationCommit": null,
  "prototypeCommit": "3f30d348289d6d94dd49cb1d976e23018183eec9",
  "profile": "Browser E2E",
  "providerMode": "LOCAL",
  "scenarioId": null,
  "runnerReference": null,
  "command": null,
  "toolchain": null,
  "fixtureHash": null,
  "expected": null,
  "actual": null,
  "result": "NOT_RUN",
  "executedAt": null,
  "evidenceReference": null,
  "reviewerDecisionReference": null,
  "externalAuthorizationReference": null,
  "productionAcceptance": false
}
```

For every completed criterion retain the exact story/criterion ID, original P/S requirement IDs, implementation/source commit, fixture identity/hash, selected actor and scope, command or browser action, expected and actual outcomes, runner/toolchain, timestamps, actual pass/fail/skip/block status and reviewer/evidence reference. Redacted public receipts may point to privately retained evidence through an approved retrieval reference; no invented private URL is needed.

A proposed acceptance wrapper may distinguish **0 = passed, 1 = failed, 2 = blocked prerequisite**, consistent with the project’s fail-closed evidence approach. Do not claim these meanings are the default exit-code semantics of xUnit, dotnet or Playwright. A wrapper must preserve tool output, reject a zero-test run and report blocked/not-run scenarios separately. Skipped, simulated, source-inspected and blocked scenarios never become an observed production PASS.

A story is locally complete only when all its applicable Given/When/Then criteria pass on the exact implementation revision, its relevant regression/migration/browser evidence exists, and an independent reviewer accepts the stated scope. A blocked external sub-criterion remains blocked even after local completion. Service activation additionally requires current methodology, identity/provider/records/signing/recovery gates and named owner approval. Track coverage at criterion level rather than using file counts, route counts or a single estimated completion percentage.

<a id="prerequisites"></a>
## 12. External prerequisites and release gates

The following maps this additive backlog onto the existing execution gate names; it does not reset or close them. Check the live head and approved private evidence before future work. “Blocked” identifies an unmet acceptance prerequisite, not a request to fabricate tokens, bypass controls or repeatedly run an unchanged failing tenant setup. [STATUS](#e-status), [PENDING](#e-pending), [TENANT](#e-tenant), [RESTORE](#e-restore).

| Gate / prerequisite | Inputs requiring separate approval | Completion evidence; prohibited substitution | Accountable role | Parity stories |
| --- | --- | --- | --- | --- |
| P1 — live identity | Approved workforce/client identity approach, test actors and scopes; tenant-owner authorization; exact OIDC composition and disabled/revoked/wrong-tenant fixtures. | Observed successful and denied identity journeys with effective app scopes and session invalidation; no development-identity substitution. | Tenant/security owner; application security reviewer | [AS-PAR-003](#as-par-003), [AS-PAR-015](#as-par-015), [AS-PAR-032](#as-par-032), [AS-PAR-034](#as-par-034) |
| P2 — selected-resource documents | Explicit approved site/library/root and selected grant; endpoint capability review; actual adapter composition, allowed test actions and reconciliation runner. | Application-driven upload/download/version/isolation, exact bytes and retries/uncertain outcomes observed for that binding. A portal login or manual upload alone is insufficient. | Tenant/resource owner and technical integration owner | [AS-PAR-014](#as-par-014), [AS-PAR-019](#as-par-019), [AS-PAR-032](#as-par-032), [AS-PAR-033](#as-par-033) |
| P3 — independent release checkpoint | Separately administered store and access/custody policy; capability/effect evidence; approved mismatch/failure tests. | Write plus independent readback of the exact manifest digest and authorized release identity, with failure blocking delivery. Shared writable application DB/directory is not independent custody. | Release authority and independent custodian | [AS-PAR-028](#as-par-028), [AS-PAR-039](#as-par-039), [AS-PAR-042](#as-par-042) |
| P4 — Purview records | Approved records profile, publication scope, record behavior, reviewer identities and authorized synthetic protection tests. | Positive label application/readback and required edit/delete/hold/record behavior observed. Enabled policy, requested label or empty Explorer results do not establish protection. | Records owner, tenant compliance administrator and professional/legal approver | [AS-PAR-030](#as-par-030), [AS-PAR-033](#as-par-033), [AS-PAR-042](#as-par-042) |
| P5 — signing | Approved signing method, signatory authority, key/service custody and exact pre-sign/signed artifact linkage; no assumed new provider. | Verified identity/intent and exact-byte signature lineage for the chosen method and output; method owner approval retained. A signature image or management acknowledgement is not equivalent. | Professional signing authority and security/custody owner | [AS-PAR-010](#as-par-010), [AS-PAR-027](#as-par-027), [AS-PAR-028](#as-par-028) |
| P6 — local records residuals | Existing locally verified archive work remains credited; additional changes require relevant local regression evidence. | Preserve local archive manifest/lineage/hold/evidence behavior without converting local verification into P4/P7 acceptance. | Records and technical owners | [AS-PAR-030](#as-par-030) |
| P7 — separate-custody restore | Authorized protected environment, independent backups/checkpoints, document/DB recovery points, key/identity recovery access and approved rehearsal scope. | Coordinated restore, immutable history/totals/hash reconciliation, worker epoch fencing, no duplicate release and measured recovery against approved RPO/RTO. | Recovery/data owners and independent custodian | [AS-PAR-038](#as-par-038), [AS-PAR-039](#as-par-039), [AS-PAR-042](#as-par-042) |
| P8 — production operations | Approved host/worker composition, secret custody, TLS/access boundaries, sensitive-log review, capacity/circuit budgets and operational owner. | Actual production-reference deployment/observability/capacity evidence; all enabled features fail closed when a dependency is unavailable. | Technical/security owner and practice leadership | [AS-PAR-033](#as-par-033), [AS-PAR-034](#as-par-034), [AS-PAR-035](#as-par-035), [AS-PAR-040](#as-par-040), [AS-PAR-042](#as-par-042) |
| P9 — independent review and protected delivery | Independent qualified review of the current implementation head and applicable repository protections; explicit merge/release authorization. | Review outcomes/fixes and exact approved commit recorded through normal policy. A generated codeword or this document is not authorization. | Independent reviewers and repository/release owner | [AS-PAR-001](#as-par-001), [AS-PAR-040](#as-par-040), [AS-PAR-045](#as-par-045) |
| P10 — complete real-tenant cycle | All prerequisites for the selected enabled service, approved new/recurring client fixtures, professional/accounting/data owner participation. | Full specification §47 cycle with positive, negative, retry, rework, issue, archive and recurrence evidence plus named sign-off. No green build substitutes for this. | Product, accounting, partner/quality, data and technical owners | [AS-PAR-045](#as-par-045) |
| E2E-RUNNER — proposed local browser lane | Approved runner/browser dependencies, Test-only synthetic identity composition and isolated PostgreSQL data; safe trace retention. | Actual Blazor journeys with persisted assertions and redacted browser evidence, explicitly classified as local when providers are simulated. | Technical/security owner | [AS-PAR-040](#as-par-040), [AS-PAR-041](#as-par-041) |
| SERVICE-PROFILE — conditional scope extension | Approved service/methodology/standards/jurisdiction, responsible professionals, permission/report templates and capability activation decision. | Every enabled service has its own entire reviewed end-to-end journey; unsupported entries remain disabled without an artificial audit opinion. | Methodology owner, relevant service partner and product owner | [AS-PAR-043](#as-par-043), [AS-PAR-046](#as-par-046), [AS-PAR-047](#as-par-047), [AS-PAR-048](#as-par-048), [AS-PAR-049](#as-par-049), [AS-PAR-050](#as-par-050), [AS-PAR-051](#as-par-051), [AS-PAR-052](#as-par-052), [AS-PAR-053](#as-par-053), [AS-PAR-054](#as-par-054), [AS-PAR-055](#as-par-055), [AS-PAR-056](#as-par-056), [AS-PAR-057](#as-par-057), [AS-PAR-058](#as-par-058), [AS-PAR-059](#as-par-059), [AS-PAR-060](#as-par-060), [AS-PAR-061](#as-par-061), [AS-PAR-062](#as-par-062) |
| FILING / BANK / OPTIONAL PROVIDER AUTHORITY | Only when separately contracted/enabled: actual customer mandate, selected existing/approved provider, permitted resources/actions and evidence retention. | Observed authorized submission and separate acceptance or approved manual evidence; no assumed automatic bank execution, tax filing or screening subscription. | Relevant client, professional and integration owners | [AS-PAR-020](#as-par-020), [AS-PAR-044](#as-par-044), [AS-PAR-057](#as-par-057), [AS-PAR-061](#as-par-061), [AS-PAR-062](#as-par-062) |

### 12.1 Current partial observations must remain partial

The redacted tenant receipt records selected-resource configuration, synthetic SharePoint versions 1.0 and 2.0, and a published/enabled synthetic Purview policy. At its last recorded recheck, the file was still unlabeled and protection-dependent checks had not been run. Preserve these observations as partial non-production evidence; do not claim zero external progress, successful protection or permission for destructive tests. [TENANT](#e-tenant).

The Worker’s existing Acceptance-mail branch can verify only its approved sender/delivery contract. It does not prove the general Web production composition, complete SharePoint workflow, Entra negative cases, Purview protection, independent checkpoint, signing or recovery. Business transactions commit before notification; a message failure must not undo an approved decision or become a false delivery/filing acknowledgement. [WORKER](#e-worker).

Professional approval cannot be synthesized by software. Preserve the exact scope of existing methodology approvals and obtain separate approval only for an uncovered method, service or policy. The public repository should retain redacted status and authorized retrieval instructions, not tenant secrets, personal identity fixtures or confidential professional evidence.

<a id="preservation"></a>
## 13. Preservation and documentation-only delivery checks

### 13.1 Protected existing materials

This delivery creates one new Markdown artifact outside the user’s repository. It performs no GitHub write, branch creation, commit, pull request, merge, deployment, database change, tenant provisioning or application/test execution. The user’s local worktree and untracked evidence were not accessible for inspection; no assertion about their contents or cleanliness is made.

| Material | Required preservation rule |
| --- | --- |
| Existing root/nested AGENTS.md and architecture/specification | Read for a later authorized change; do not rewrite instruction precedence or architecture adoption. |
| Historical accounting, audit, M365, WBS and implementation plans | Retain their names, content, original story/requirement IDs and historical decisions. This AS-PAR namespace only adds cross-references. |
| docs/execution/status.json, auditsphere-execution-current-slice.md, auditsphere-execution-pending-tasks.md and auditsphere-execution-implementation-checklist.md | Do not move the active issue, branch, head, test count or acceptance status to this documentation task; no invented execution evidence. |
| docs/evidence/* and private/untracked evidence elsewhere | Do not overwrite, normalize, clean, upload, publish, rename or delete. New verification artifacts require a new authorized location and retention policy. |
| Application/test source, migrations, database scripts and active .github/workflows | No modification in this task, including formatting, dependency installation or “minor fixes.” Proposed changes remain story instructions. |
| Production and tenant settings, roles, grants, secrets, keys and provider resources | No changes. No fabricated placeholders presented as valid production values; no login, signing, protection or release gate bypass. |
| Generated artifact | Only the new .md file is delivered. No source archive, additional evidence file, browser trace, font, spreadsheet or application patch is included. |

### 13.2 Safe future repository adoption

If separately authorized to add this document to Git, first inspect the target branch and verify the proposed path is new. An existing file at that path is a conflict to review, not permission to overwrite. Stage only the exact new Markdown path after confirming the diff contains only that addition. Do not use broad staging, automatic cleanup or an incidental application fix. Any later implementation follows the existing issue dependency and review/merge policy; AS-PAR numbering does not supersede the active work package order.

### 13.3 Documentation validation performed for this artifact

The generation check validates 62 unique story identifiers, their dependencies and all internal anchors; required story sections and Given/When/Then triplets; the declared native requirement families; all 46 P:AT required-result rows; 44 permission rows; 14 personas; six workspace tabs and 20 workpaper scenario groups. It checks source-reference keys and Markdown fences. These are document integrity checks only, not application, security, browser or production acceptance.

A future coverage reviewer must map each exact GWT criterion to an actual named test and evidence receipt. Existing test files listed as F/reuse candidates do not close a criterion until their assertions and results are verified. Broad logical tables remain source-bound: their compact rows must not be used to omit required fields, conditional evidence, a service-specific professional review or a negative acceptance case.

<a id="sources"></a>
## 14. Immutable source and existing-test register

All links below point to the inspected commit, not a moving default branch. **A = assertion-inspected** applies only where expressly stated in the note; **F = file/seam confirmed** does not imply all method bodies were inspected; **R = repository-recorded execution evidence** is not a test run by this reviewer; **N = proposed work** has no existing passing result. The narrative and story gap sections identify directly inspected code behavior. Do not equate a source link with completed acceptance.

| Immutable reference | Existing path | Evidence classification | Inspection / reuse boundary |
| --- | --- | --- | --- |
| <a id="e-p-source"></a>[P-SOURCE](https://github.com/nirzaf/auditsphere-visual-prototype/blob/3f30d348289d6d94dd49cb1d976e23018183eec9/source.json) | `source.json` | F — prototype source / interaction contract | STE-PRD-001, canonical sections 1–27; repeated lifecycle/services projections are not additional requirements. |
| <a id="e-p-roles"></a>[P-ROLES](https://github.com/nirzaf/auditsphere-visual-prototype/blob/3f30d348289d6d94dd49cb1d976e23018183eec9/roles.json) | `roles.json` | F — prototype source / interaction contract | Fourteen persona profiles, routes, allowed responsibilities and restrictions. |
| <a id="e-p-perm"></a>[P-PERM](https://github.com/nirzaf/auditsphere-visual-prototype/blob/3f30d348289d6d94dd49cb1d976e23018183eec9/permissions.json) | `permissions.json` | F — prototype source / interaction contract | Forty-four named permission contracts; application authorization, not a production identity implementation. |
| <a id="e-p-readme"></a>[P-README](https://github.com/nirzaf/auditsphere-visual-prototype/blob/3f30d348289d6d94dd49cb1d976e23018183eec9/README.md) | `README.md` | F — prototype source / interaction contract | Browser-only synthetic state; React/Vite hosts the compatibility renderer; no production backend or provider boundary. |
| <a id="e-p-wp"></a>[P-WP](https://github.com/nirzaf/auditsphere-visual-prototype/blob/3f30d348289d6d94dd49cb1d976e23018183eec9/test_workpaper_workspace.py) | `test_workpaper_workspace.py` | F — prototype source / interaction contract | Inspected acceptance source: 20 numbered scenario groups; six workspace tabs and clearance invalidation. Not executed in this review. |
| <a id="e-p-flow"></a>[P-FLOW](https://github.com/nirzaf/auditsphere-visual-prototype/blob/3f30d348289d6d94dd49cb1d976e23018183eec9/test_prototype.py) | `test_prototype.py` | F — prototype source / interaction contract | Existing prototype browser workflow example; not a .NET acceptance suite. |
| <a id="e-p-runtime"></a>[P-RUNTIME](https://github.com/nirzaf/auditsphere-visual-prototype/blob/3f30d348289d6d94dd49cb1d976e23018183eec9/base-app.js) | `base-app.js` | F — prototype source / interaction contract | Prototype workpaper defaults and runtime, added at the pinned head; sample bytes/hashes and role changes are illustrative. |
| <a id="e-p-guide"></a>[P-GUIDE](https://github.com/nirzaf/auditsphere-visual-prototype/blob/3f30d348289d6d94dd49cb1d976e23018183eec9/ROLE_GUIDE.md) | `ROLE_GUIDE.md` | F — prototype source / interaction contract | Persona and workflow guide; cross-check against canonical source and permissions. |
| <a id="e-spec"></a>[SPEC](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/SPECIFICATION.md) | `docs/auditsphere-requirements-system-specification-current.md` | F — existing source / specification | Controlling .NET v5 specification; business/control semantics plus §§41–47 implementation contract. |
| <a id="e-agents"></a>[AGENTS](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/AGENTS.md) | `AGENTS.md` | F — existing source / specification | Existing repository safety and execution rules; read nested instructions before any later implementation. |
| <a id="e-old-aud"></a>[OLD-AUD](AuditSphere_R2R_Task_Breakdown/source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md) | `docs/AuditSphere_R2R_Task_Breakdown/source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md` | F — existing source / specification | Existing audit gap plan, AWP catalogue and AS-AUD story namespace; preserve every original entry and owner. |
| <a id="e-old-acc"></a>[OLD-ACC](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/AuditSphere_Accounting_module.md) | `docs/auditsphere-accounting-module-requirements-current.md` | F — existing source / specification | Existing accounting implementation contract; preserve and reuse rather than start a second accounting engine. |
| <a id="e-old-m365"></a>[OLD-M365](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/AuditSphere_M365_Simple_Onboarding_User_Story.md) | `docs/auditsphere-m365-onboarding-user-stories.md` | F — existing source / specification | Existing M365 setup/access/workspace backlog; link deltas, do not replace it. |
| <a id="e-status"></a>[STATUS](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/execution/status.json) | `docs/execution/status.json` | R — repository record; scope limited | Repository-recorded verification at dc9cb0b, 246/246 tests, 91 migrations, productionEnabled=false; not a fresh execution by this reviewer. |
| <a id="e-pending"></a>[PENDING](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/execution/pending-tasks.md) | `docs/execution/auditsphere-execution-pending-tasks.md` | R — repository record; scope limited | Newer than README for PDF/Office exports, advanced-method local evidence and M365 local foundation; external gates remain separate. |
| <a id="e-slice"></a>[SLICE](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/execution/current-slice.md) | `docs/execution/auditsphere-execution-current-slice.md` | R — repository record; scope limited | Existing execution narrative; do not rewrite to claim this documentation task implemented anything. |
| <a id="e-tenant"></a>[TENANT](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/evidence/developer-tenant-redacted.json) | `docs/evidence/developer-tenant-redacted.json` | R — repository record; scope limited | Partial non-production observations only: selected-resource setup and file versions; positive Purview protection not observed. |
| <a id="e-restore"></a>[RESTORE](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/evidence/restore-drill-latest.json) | `docs/evidence/restore-drill-latest.json` | R — repository record; scope limited | Existing loopback recovery evidence, not separate-custody production RPO/RTO proof. |
| <a id="e-meth"></a>[METH](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/evidence/methodology-approval-STE-METH-APP-001.md) | `docs/evidence/auditsphere-evidence-approval-methodology-ste-meth-app-001-approved.md` | R — repository record; scope limited | Recorded scoped methodology approval; do not generalize to all service families. |
| <a id="e-meth-add"></a>[METH-ADD](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/evidence/methodology-approval-STE-METH-APP-001-addendum.md) | `docs/evidence/auditsphere-evidence-approval-methodology-ste-meth-app-001-addendum-approved.md` | R — repository record; scope limited | Recorded advanced-method scope extension; activation remains separately governed. |
| <a id="e-auth"></a>[AUTH](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Security/AuthorizationDecision.cs) | `src/AuditSphereOps.Application/Security/AuthorizationDecision.cs` | F — existing source / specification | Inspected: current user/session epoch, covering grants, internal-only and professional-work/hold checks. |
| <a id="e-roles"></a>[ROLES](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Security/RoleAdministrationService.cs) | `src/AuditSphereOps.Application/Security/RoleAdministrationService.cs` | F — existing source / specification | Inspected: eight allowlisted assignable role codes, immutable identity mapping and role changes; other role strings appear elsewhere. |
| <a id="e-actor"></a>[ACTOR](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Authentication/TrustedActorResolver.cs) | `src/AuditSphereOps.Web/Authentication/TrustedActorResolver.cs` | F — existing source / specification | Existing trusted-session actor resolution; reusable boundary, not browser-supplied role authority. |
| <a id="e-layout"></a>[LAYOUT](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Layout/MainLayout.razor) | `src/AuditSphereOps.Web/Components/Layout/MainLayout.razor` | F — existing source / specification | Inspected static shared navigation; not fourteen persona workspaces. |
| <a id="e-acc-nav"></a>[ACC-NAV](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Layout/AccountingNavigation.razor) | `src/AuditSphereOps.Web/Components/Layout/AccountingNavigation.razor` | F — existing source / specification | Existing accounting navigation; preserve current working context and links. |
| <a id="e-portfolio"></a>[PORTFOLIO](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Portfolio.razor) | `src/AuditSphereOps.Web/Components/Pages/Portfolio.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-leads"></a>[LEADS](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Leads.razor) | `src/AuditSphereOps.Web/Components/Pages/Leads.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-proposal"></a>[PROPOSAL](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/ProposalDetail.razor) | `src/AuditSphereOps.Web/Components/Pages/ProposalDetail.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-assess"></a>[ASSESS](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/AssessmentDetail.razor) | `src/AuditSphereOps.Web/Components/Pages/AssessmentDetail.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-accept-ui"></a>[ACCEPT-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/AssessmentDecision.razor) | `src/AuditSphereOps.Web/Components/Pages/AssessmentDecision.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-client"></a>[CLIENT](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/ClientDetail.razor) | `src/AuditSphereOps.Web/Components/Pages/ClientDetail.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-engage"></a>[ENGAGE](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/EngagementDetail.razor) | `src/AuditSphereOps.Web/Components/Pages/EngagementDetail.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-pbc-ui"></a>[PBC-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/PbcRequests.razor) | `src/AuditSphereOps.Web/Components/Pages/PbcRequests.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-pbc-client"></a>[PBC-CLIENT](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/ClientPbcRequest.razor) | `src/AuditSphereOps.Web/Components/Pages/ClientPbcRequest.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-portal"></a>[PORTAL](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/ClientPortal.razor) | `src/AuditSphereOps.Web/Components/Pages/ClientPortal.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-mgmt-ui"></a>[MGMT-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/ClientFinancialPackage.razor) | `src/AuditSphereOps.Web/Components/Pages/ClientFinancialPackage.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-time-ui"></a>[TIME-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/PracticeTime.razor) | `src/AuditSphereOps.Web/Components/Pages/PracticeTime.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-invoice-ui"></a>[INVOICE-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/InvoiceDetail.razor) | `src/AuditSphereOps.Web/Components/Pages/InvoiceDetail.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-finance-ui"></a>[FINANCE-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Finance.razor) | `src/AuditSphereOps.Web/Components/Pages/Finance.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-acc-ui"></a>[ACC-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/AccountingWorkspace.razor) | `src/AuditSphereOps.Web/Components/Pages/AccountingWorkspace.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-period-ui"></a>[PERIOD-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/AccountingPeriod.razor) | `src/AuditSphereOps.Web/Components/Pages/AccountingPeriod.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-map-ui"></a>[MAP-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Mapping.razor) | `src/AuditSphereOps.Web/Components/Pages/Mapping.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-aje-ui"></a>[AJE-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Journals.razor) | `src/AuditSphereOps.Web/Components/Pages/Journals.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-evid-ui"></a>[EVID-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/AccountingEvidence.razor) | `src/AuditSphereOps.Web/Components/Pages/AccountingEvidence.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-fs-ui"></a>[FS-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/FinancialPackage.razor) | `src/AuditSphereOps.Web/Components/Pages/FinancialPackage.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-fs-rev-ui"></a>[FS-REV-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/FinancialPackageReviews.razor) | `src/AuditSphereOps.Web/Components/Pages/FinancialPackageReviews.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-consol-ui"></a>[CONSOL-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Consolidation.razor) | `src/AuditSphereOps.Web/Components/Pages/Consolidation.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-adv-ui"></a>[ADV-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/AdvancedConsolidationWorkflow.razor) | `src/AuditSphereOps.Web/Components/Pages/AdvancedConsolidationWorkflow.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-plan-ui"></a>[PLAN-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/AuditPlan.razor) | `src/AuditSphereOps.Web/Components/Pages/AuditPlan.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-pop-ui"></a>[POP-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/AuditPopulation.razor) | `src/AuditSphereOps.Web/Components/Pages/AuditPopulation.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-field-ui"></a>[FIELD-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/AuditFieldwork.razor) | `src/AuditSphereOps.Web/Components/Pages/AuditFieldwork.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-wp-ui"></a>[WP-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Workpaper.razor) | `src/AuditSphereOps.Web/Components/Pages/Workpaper.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-note-ui"></a>[NOTE-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/ReviewPoint.razor) | `src/AuditSphereOps.Web/Components/Pages/ReviewPoint.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-finding-ui"></a>[FINDING-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Finding.razor) | `src/AuditSphereOps.Web/Components/Pages/Finding.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-complete-ui"></a>[COMPLETE-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Completion.razor) | `src/AuditSphereOps.Web/Components/Pages/Completion.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-release-ui"></a>[RELEASE-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Release.razor) | `src/AuditSphereOps.Web/Components/Pages/Release.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-archive-ui"></a>[ARCHIVE-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/RecordsArchive.razor) | `src/AuditSphereOps.Web/Components/Pages/RecordsArchive.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-roll-ui"></a>[ROLL-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/PeriodRollforward.razor) | `src/AuditSphereOps.Web/Components/Pages/PeriodRollforward.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-restate-ui"></a>[RESTATE-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/PeriodRestatements.razor) | `src/AuditSphereOps.Web/Components/Pages/PeriodRestatements.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-setup-ui"></a>[SETUP-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Microsoft365Setup.razor) | `src/AuditSphereOps.Web/Components/Pages/Microsoft365Setup.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-admin-ui"></a>[ADMIN-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Administration.razor) | `src/AuditSphereOps.Web/Components/Pages/Administration.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-ops-ui"></a>[OPS-UI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Components/Pages/Operations.razor) | `src/AuditSphereOps.Web/Components/Pages/Operations.razor` | F — existing source / specification | Existing source surface; the applicable story describes inspected behavior or verification limits. |
| <a id="e-crm"></a>[CRM](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Practice/PracticeCrmService.cs) | `src/AuditSphereOps.Application/Practice/PracticeCrmService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-time"></a>[TIME](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Practice/PracticeTimeService.cs) | `src/AuditSphereOps.Application/Practice/PracticeTimeService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-bill"></a>[BILL](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Practice/BillingService.cs) | `src/AuditSphereOps.Application/Practice/BillingService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-ledger"></a>[LEDGER](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Practice/LedgerService.cs) | `src/AuditSphereOps.Application/Practice/LedgerService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-accept"></a>[ACCEPT](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Acceptance/AcceptanceDecisionService.cs) | `src/AuditSphereOps.Application/Acceptance/AcceptanceDecisionService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-plan"></a>[PLAN](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Audit/AuditPlanningService.cs) | `src/AuditSphereOps.Application/Audit/AuditPlanningService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-program"></a>[PROGRAM](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Audit/AuditProgramService.cs) | `src/AuditSphereOps.Application/Audit/AuditProgramService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-catalog"></a>[CATALOG](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Audit/AuditProgramCatalog.cs) | `src/AuditSphereOps.Application/Audit/AuditProgramCatalog.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-field"></a>[FIELD](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Audit/AuditFieldworkService.cs) | `src/AuditSphereOps.Application/Audit/AuditFieldworkService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-approval"></a>[APPROVAL](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Reviews/ApprovalService.cs) | `src/AuditSphereOps.Application/Reviews/ApprovalService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-fs-review"></a>[FS-REVIEW](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Accounting/FinancialPackageReviewService.cs) | `src/AuditSphereOps.Application/Accounting/FinancialPackageReviewService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-release"></a>[RELEASE](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Completion/ReleaseService.cs) | `src/AuditSphereOps.Application/Completion/ReleaseService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-checkpoint"></a>[CHECKPOINT](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Completion/ReleaseCheckpointStore.cs) | `src/AuditSphereOps.Application/Completion/ReleaseCheckpointStore.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-release-safety"></a>[RELEASE-SAFETY](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Completion/ReleaseSafetyOptions.cs) | `src/AuditSphereOps.Application/Completion/ReleaseSafetyOptions.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-records"></a>[RECORDS](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Application/Records/RecordsArchiveService.cs) | `src/AuditSphereOps.Application/Records/RecordsArchiveService.cs` | F — existing source / specification | Existing application seam; reuse before proposing another service. |
| <a id="e-completion-dom"></a>[COMPLETION-DOM](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Domain/Completion/Completion.cs) | `src/AuditSphereOps.Domain/Completion/Completion.cs` | F — existing source / specification | Inspected EqrCase, WrittenRepresentation and SignatureLineage fields; entity presence is not a complete workflow. |
| <a id="e-audit-dom"></a>[AUDIT-DOM](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Domain/Audit/Audit.cs) | `src/AuditSphereOps.Domain/Audit/Audit.cs` | F — existing source / specification | Existing Workpaper/related audit models. |
| <a id="e-doc-dom"></a>[DOC-DOM](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Domain/Documents/Documents.cs) | `src/AuditSphereOps.Domain/Documents/Documents.cs` | F — existing source / specification | Existing document/evidence/PBC structures. |
| <a id="e-ctx"></a>[CTX](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.cs) | `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.cs` | F — existing source / specification | Existing EF mappings and model; new migrations must be additive and retain existing triggers/constraints. |
| <a id="e-web-host"></a>[WEB-HOST](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/Program.cs) | `src/AuditSphereOps.Web/Program.cs` | F — existing source / specification | Inspected: OIDC/development identity, queue-only simulation sink, live external-effects startup refusal. |
| <a id="e-worker"></a>[WORKER](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Worker/Program.cs) | `src/AuditSphereOps.Worker/Program.cs` | F — existing source / specification | Inspected: isolated Acceptance mail worker for Graph/SMTP/Resend; Test-only PBC transfer simulation; no general production composition. |
| <a id="e-config"></a>[CONFIG](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/src/AuditSphereOps.Web/appsettings.json) | `src/AuditSphereOps.Web/appsettings.json` | F — existing source / specification | Actual key names/defaults, not approved production configuration. |
| <a id="e-ci"></a>[CI](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/.github/workflows/ci.yml) | `.github/workflows/ci.yml` | F — existing source / specification | Inspected: build/test/readiness, postgres:18, floating major action refs; no Playwright browser lane in this workflow. |
| <a id="e-telemetry"></a>[TELEMETRY](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/operations/telemetry.md) | `docs/operations/auditsphere-operations-guide-telemetry.md` | F — existing source / specification | Existing instrumentation contract; production exporter/custody evidence remains separate. |
| <a id="e-capacity"></a>[CAPACITY](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/docs/operations/connection-capacity-decision.md) | `docs/operations/auditsphere-operations-decision-connection-capacity.md` | F — existing source / specification | Existing capacity decision; preserve actual approved connection/circuit assumptions. |
| <a id="e-db-restore"></a>[DB-RESTORE](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/scripts/db/restore-drill.sh) | `scripts/db/restore-drill.sh` | F — existing source / specification | Existing recovery script: inspect its current inputs/output behavior before any separately authorized invocation. |
| <a id="e-t-acceptancedecisiontests"></a>[T-AcceptanceDecisionTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/AcceptanceDecisionTests.cs) | `tests/AuditSphereOps.Domain.Tests/AcceptanceDecisionTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-accountingbackfillmigrationtests"></a>[T-AccountingBackfillMigrationTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/AccountingBackfillMigrationTests.cs) | `tests/AuditSphereOps.Domain.Tests/AccountingBackfillMigrationTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-accountingbenchmarktests"></a>[T-AccountingBenchmarkTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/AccountingBenchmarkTests.cs) | `tests/AuditSphereOps.Domain.Tests/AccountingBenchmarkTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-accountingintegritytests"></a>[T-AccountingIntegrityTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/AccountingIntegrityTests.cs) | `tests/AuditSphereOps.Domain.Tests/AccountingIntegrityTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-adjustmentbridgetests"></a>[T-AdjustmentBridgeTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/AdjustmentBridgeTests.cs) | `tests/AuditSphereOps.Domain.Tests/AdjustmentBridgeTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-approvaltests"></a>[T-ApprovalTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/ApprovalTests.cs) | `tests/AuditSphereOps.Domain.Tests/ApprovalTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-auditfieldworkworkflowtests"></a>[T-AuditFieldworkWorkflowTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/AuditFieldworkWorkflowTests.cs) | `tests/AuditSphereOps.Domain.Tests/AuditFieldworkWorkflowTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-auditplanningtests"></a>[T-AuditPlanningTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/AuditPlanningTests.cs) | `tests/AuditSphereOps.Domain.Tests/AuditPlanningTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-auditprogramworkflowtests"></a>[T-AuditProgramWorkflowTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs) | `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs` | A — specific assertions inspected | Assertion-inspected ControlledCatalog_UsesScopedAppendOnlyWorkflow: 165-item publish/adopt, idempotency, selected procedure execution/independent review and stale-generation rejection; not execution of all 165 procedures. |
| <a id="e-t-auditscopeintegritytests"></a>[T-AuditScopeIntegrityTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/AuditScopeIntegrityTests.cs) | `tests/AuditSphereOps.Domain.Tests/AuditScopeIntegrityTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-authorizationdecisiontests"></a>[T-AuthorizationDecisionTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/AuthorizationDecisionTests.cs) | `tests/AuditSphereOps.Domain.Tests/AuthorizationDecisionTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-billingtests"></a>[T-BillingTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/BillingTests.cs) | `tests/AuditSphereOps.Domain.Tests/BillingTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-clientaccountingtests"></a>[T-ClientAccountingTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/ClientAccountingTests.cs) | `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-coreentitycatalogtests"></a>[T-CoreEntityCatalogTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/CoreEntityCatalogTests.cs) | `tests/AuditSphereOps.Domain.Tests/CoreEntityCatalogTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-documentsnapshottests"></a>[T-DocumentSnapshotTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/DocumentSnapshotTests.cs) | `tests/AuditSphereOps.Domain.Tests/DocumentSnapshotTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-durableoutboxtests"></a>[T-DurableOutboxTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/DurableOutboxTests.cs) | `tests/AuditSphereOps.Domain.Tests/DurableOutboxTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-errorcatalogtests"></a>[T-ErrorCatalogTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/ErrorCatalogTests.cs) | `tests/AuditSphereOps.Domain.Tests/ErrorCatalogTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-financialstatementtests"></a>[T-FinancialStatementTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/FinancialStatementTests.cs) | `tests/AuditSphereOps.Domain.Tests/FinancialStatementTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-ledgertests"></a>[T-LedgerTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/LedgerTests.cs) | `tests/AuditSphereOps.Domain.Tests/LedgerTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-microsoft365accesstests"></a>[T-Microsoft365AccessTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/Microsoft365AccessTests.cs) | `tests/AuditSphereOps.Domain.Tests/Microsoft365AccessTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-microsoft365onboardingtests"></a>[T-Microsoft365OnboardingTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/Microsoft365OnboardingTests.cs) | `tests/AuditSphereOps.Domain.Tests/Microsoft365OnboardingTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-operationrecoverytests"></a>[T-OperationRecoveryTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/OperationRecoveryTests.cs) | `tests/AuditSphereOps.Domain.Tests/OperationRecoveryTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-outboxmigrationtests"></a>[T-OutboxMigrationTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/OutboxMigrationTests.cs) | `tests/AuditSphereOps.Domain.Tests/OutboxMigrationTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-pbctests"></a>[T-PbcTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/PbcTests.cs) | `tests/AuditSphereOps.Domain.Tests/PbcTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-pbctransfertests"></a>[T-PbcTransferTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/PbcTransferTests.cs) | `tests/AuditSphereOps.Domain.Tests/PbcTransferTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-practicecrmtests"></a>[T-PracticeCrmTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/PracticeCrmTests.cs) | `tests/AuditSphereOps.Domain.Tests/PracticeCrmTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-practicetimetests"></a>[T-PracticeTimeTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/PracticeTimeTests.cs) | `tests/AuditSphereOps.Domain.Tests/PracticeTimeTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-providerboundarytests"></a>[T-ProviderBoundaryTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/ProviderBoundaryTests.cs) | `tests/AuditSphereOps.Domain.Tests/ProviderBoundaryTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-recordsarchivetests"></a>[T-RecordsArchiveTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/RecordsArchiveTests.cs) | `tests/AuditSphereOps.Domain.Tests/RecordsArchiveTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-releaseevidencetests"></a>[T-ReleaseEvidenceTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/ReleaseEvidenceTests.cs) | `tests/AuditSphereOps.Domain.Tests/ReleaseEvidenceTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-releasetests"></a>[T-ReleaseTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/ReleaseTests.cs) | `tests/AuditSphereOps.Domain.Tests/ReleaseTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-roleadministrationtests"></a>[T-RoleAdministrationTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/RoleAdministrationTests.cs) | `tests/AuditSphereOps.Domain.Tests/RoleAdministrationTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-routecatalogtests"></a>[T-RouteCatalogTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/RouteCatalogTests.cs) | `tests/AuditSphereOps.Domain.Tests/RouteCatalogTests.cs` | A — specific assertions inspected | Assertion-inspected database service tests for client contacts, time approval separation and fiscal-period close; its filename does not prove browser route coverage. |
| <a id="e-t-trialbalanceworkertests"></a>[T-TrialBalanceWorkerTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/TrialBalanceWorkerTests.cs) | `tests/AuditSphereOps.Domain.Tests/TrialBalanceWorkerTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-trialbalancexlsximportertests"></a>[T-TrialBalanceXlsxImporterTests](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/TrialBalanceXlsxImporterTests.cs) | `tests/AuditSphereOps.Domain.Tests/TrialBalanceXlsxImporterTests.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-t-unittest1"></a>[T-UnitTest1](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/UnitTest1.cs) | `tests/AuditSphereOps.Domain.Tests/UnitTest1.cs` | F — existing test file / reuse candidate | Existing test file confirmed in the pinned tree; reuse candidate, not evidence that every proposed criterion is already asserted. |
| <a id="e-pg"></a>[PG](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/PgTestSchema.cs) | `tests/AuditSphereOps.Domain.Tests/PgTestSchema.cs` | F — existing source / specification | Existing disposable PostgreSQL schema harness; use real PostgreSQL rather than InMemory/SQLite substitution. |
| <a id="e-seed"></a>[SEED](https://github.com/nirzaf/AuditSphere/blob/f64a1f4ef05bf0372cd60af5878031601a9dad61/tests/AuditSphereOps.Domain.Tests/PlanningSeed.cs) | `tests/AuditSphereOps.Domain.Tests/PlanningSeed.cs` | F — existing source / specification | Existing scoped synthetic audit fixture; do not reuse real identities or change production authentication. |

### 14.1 Original documentation handoff record

At the time this requirements document was prepared, it delivered a proposed documentation backlog only: no application code, tests, workflows, database or tenant resources were changed by that documentation task, and no professional or production acceptance was claimed. Subsequent implementation and verification are recorded separately in the execution ledger. All remaining requirements and external acceptance remain subject to their stated authorizations and evidence gates.
