# AuditSphere — Central Documentation Index

**Status:** CURRENT

**Purpose:** Canonical directory, authority hierarchy, and reading guide for all repository documentation.

**Authority:** Supreme documentation navigation authority after [`AGENTS.md`](../AGENTS.md).

**Audience:** AI coding agents and human developers.

---

The repository ships Angular as the canonical staff/client presentation. The legacy Blazor Web host was retired by owner decision and merged to `master` as `e7ebe27c`; its source remains available at the pre-removal commit and is historical evidence, not current UI guidance. The supplied 20-section/165-procedure audit working process is implemented through Atomic Tasks 01–09; Task 10 remains blocked on the methodology owner's D1 policy and Task 11 is technically prepared only. Current evidence is in [status.json](execution/status.json) and the [current-slice handoff](execution/auditsphere-execution-current-slice.md).

## 0. Quick map

| Need | Start here |
|---|---|
| Runtime architecture, boundaries and flows | [Current architecture](architecture/auditsphere-architecture-current-architecture.md) · [Code map](architecture/auditsphere-architecture-code-map.md) · [Enterprise target (PROPOSED)](architecture/auditsphere-architecture-specification-enterprise-target-proposed.md) |
| Data, schema and HTTP contracts | [Data model](architecture/auditsphere-architecture-contract-data-model-current.md) · [HTTP boundary](architecture/auditsphere-architecture-http-boundary-and-contract-current.md) · [OpenAPI artifact](../contracts/auditsphere-openapi.json) |
| Approved decisions and deviations | [ADR register](architecture/adr/auditsphere-architecture-index-adr-register-current.md) |
| Product requirements | [STE v2.2 requirements](requirements/auditsphere-accounting-module-requirements-current.md) |
| Audit programme execution | [Atomic specification](task_breakdown/source/auditsphere-audit-specification-working-process-atomic-proposed.md) · [Extracted 165-procedure source](task_breakdown/source/auditsphere-audit-working-process-procedures-source.md) |
| Current state and evidence | [status.json](execution/status.json) · [Current-slice handoff](execution/auditsphere-execution-current-slice.md) · [Pending work](execution/auditsphere-execution-pending-tasks.md) |
| API and Angular parity work | [Implementation tracker](execution/auditsphere-execution-tracker-api-angular-parity-current.md) · `apiAngularParity` in [status.json](execution/status.json) |
| Testing and Definition of Done | [Testing strategy](testing/auditsphere-testing-strategy-definition-of-done-current.md) |
| Migration history and retirement evidence | [Removal gate](migration/auditsphere-migration-blazor-removal-readiness.md) · [Angular migration](architecture/auditsphere-angular-migration-current.md) |
| Operations and recovery | [Restore drill](operations/auditsphere-operations-runbook-restore-drill.md) |
| R2R task breakdown | [R2R index](task_breakdown/auditsphere-r2r-index-task-breakdown.md) |
| Proposed gap-remediation plan | [43-task gap specification](task_breakdown/source/auditsphere-implementation-spec-atomic-gap-remediation-proposed.md) |

## 1. Start Here

When starting work in this repository, follow this deterministic reading sequence:

1. **[`AGENTS.md`](../AGENTS.md):** Core engineering rules, three financial boundaries, absolute prohibitions ("Never" rules), and local environment instructions.

2. **This Index (`docs/auditsphere-docs-index.md`):** Complete authority hierarchy and catalog of canonical documentation.

3. **[`docs/architecture/auditsphere-architecture-current-architecture.md`](architecture/auditsphere-architecture-current-architecture.md):** Binding implementation architecture (modular monolith, static capability services, partial classes).

4. **[`docs/architecture/auditsphere-architecture-code-map.md`](architecture/auditsphere-architecture-code-map.md):** Capability-to-file directory mapping business capabilities to Domain, Application, Infrastructure, UI, and Tests.

5. **Execution ledger ([`docs/execution/status.json`](execution/status.json)):** Query only relevant fields, for example `jq '{active, externalGates}' docs/execution/status.json`, instead of reading the whole file. It is the single source of truth for verified commit SHA, test counts, migration counts, active slice and external blockers. Per-slice observations never make an earlier full-suite result current.

6. **[`docs/execution/auditsphere-execution-current-slice.md`](execution/auditsphere-execution-current-slice.md):** Compact handoff of recent completed slices and next eligible actions.

---

## 2. Authority Order

When two documents appear to conflict or contain differing detail, precedence is strictly evaluated as follows:

```text



1. AGENTS.md (Repo invariants, boundaries, safety rules)



2. docs/architecture/auditsphere-architecture-current-architecture.md (Implementation architecture)



3. Current requirements contracts (System specification, accounting roadmap)



4. Workstream/module contracts (R2R modules 20–26)



5. Task cards & proposed backlogs (T001–T075, proposed parity stories)



6. Execution status & evidence records (status.json, restore drill, approval manifests)



7. Historical reference documents (R2R reference papers)



8. HISTORICAL_SOURCE documents (Original preserved blueprints & stories)



```

### Key Authority Distinctions

- **Approved ADRs govern explicit deviations:** apply only the scope recorded in the [ADR register](architecture/adr/auditsphere-architecture-index-adr-register-current.md). A later ADR does not approve unrelated methodology, tenant actions or production acceptance. Reversible documentation corrections and compliant routine engineering choices use the owner's delegation in `AGENTS.md`; qualified professional and external gates remain separate.

- **Architecture Wins over Proposed Detail:** Historical blueprints or task cards mentioning MediatR, bUnit, or microservices are superseded by the current modular monolith architecture.

- **Evidence Proves Execution, Not Architecture:** Execution logs prove what ran locally; they do not alter requirement or architecture definitions.

- **Historical Sources Preserve Provenance:** Historical sources preserve byte-level requirement text; they never act as implementation authority.

- **Volatile Metrics Live Only in `status.json`:** Test counts, migration counts, CI run IDs, and verified commit SHAs belong exclusively to `status.json`.

---

## 3. Architecture & Code Structure

| Canonical Document | Purpose | Authority |

|---|---|---|

| [`docs/architecture/auditsphere-architecture-current-architecture.md`](architecture/auditsphere-architecture-current-architecture.md) | Authoritative modular monolith architecture, capability partials, pure calculators, and durable operation patterns. | `CURRENT` |

| [`docs/architecture/auditsphere-architecture-specification-enterprise-target-proposed.md`](architecture/auditsphere-architecture-specification-enterprise-target-proposed.md) | Enterprise target: retain-and-harden monolith, five priorities with code anchors, adoption plan. Proposes no rewrite; authorizes nothing. | `PROPOSED` |

| [`docs/architecture/auditsphere-architecture-code-map.md`](architecture/auditsphere-architecture-code-map.md) | Business-capability-to-code directory mapping services, DbContext partials, calculators, UI workbenches, and tests. | `CURRENT` |

| [`docs/architecture/auditsphere-m365-tenant-administration-permissions.md`](architecture/auditsphere-m365-tenant-administration-permissions.md) | Microsoft 365 capability permission matrix, consent contract and external-operation lifecycle; optional capabilities implemented and disabled by default. | `APPROVED` |

| [`docs/architecture/auditsphere-architecture-document-naming-policy.md`](architecture/auditsphere-architecture-document-naming-policy.md) | Standard markdown naming rules (`auditsphere-<area>-<document-type>-<subject>[-<id>][-<status>].md`) and uniqueness rules. | `CURRENT` |

| [`docs/architecture/adr/auditsphere-architecture-index-adr-register-current.md`](architecture/adr/auditsphere-architecture-index-adr-register-current.md) | ADR register: operative decisions through ADR-0018, including firm X.509 PDF signing, STE v2.2 definitions and practice-reporting bases; ADR-0008 is superseded; professional and live-provider gates remain separate. | `CURRENT` (register) |
| [`docs/architecture/auditsphere-architecture-contract-data-model-current.md`](architecture/auditsphere-architecture-contract-data-model-current.md) | Data model index: storage conventions, model partials by specification module, schema change procedure. | `CURRENT` |
| [`docs/architecture/auditsphere-architecture-policy-non-functional-requirements-current.md`](architecture/auditsphere-architecture-policy-non-functional-requirements-current.md) | Non-functional requirements: enforced limits, rate budgets and open targets (P7, P8). | `CURRENT` |
| [`docs/architecture/auditsphere-architecture-index-agent-context-current.md`](architecture/auditsphere-architecture-index-agent-context-current.md) | Agent context slots in loading order, context hazards and the pack apply steps. | `CURRENT` |
| [`docs/architecture/auditsphere-angular-conventions-current.md`](architecture/auditsphere-angular-conventions-current.md) | Native Angular coding conventions, draft/assent rules and safe AI-assisted implementation boundaries for the presentation migration. | `CURRENT` |
| [`docs/architecture/auditsphere-architecture-http-boundary-and-contract-current.md`](architecture/auditsphere-architecture-http-boundary-and-contract-current.md) | Hardened HTTP boundary: authenticated `/api/ui` groups, rate-limit catalog, security headers, request-size policy, `ApiError` body, and the canonical OpenAPI 3.1 contract with CI drift protection. | `CURRENT` |
| [`docs/migration/auditsphere-migration-blazor-retirement-inventory.md`](migration/auditsphere-migration-blazor-retirement-inventory.md), [route matrix](migration/auditsphere-migration-blazor-angular-route-parity.md), [feature register](migration/auditsphere-migration-blazor-feature-parity.md), [test register](migration/auditsphere-migration-blazor-test-parity.md), [removal gate](migration/auditsphere-migration-blazor-removal-readiness.md), and [stop-gate report](migration/auditsphere-migration-blazor-retirement-final-report.md) | Historical source-discovery and evidence registers for the retired Blazor host. The removal was merged to `master` as `e7ebe27c`; the current tree contains only Angular. Route ownership was never behavior parity, and no parity, rollback or external acceptance is claimed. | `HISTORICAL_REFERENCE` |

---

## 4. Current Normative Requirements

| Canonical Document | Purpose | Authority |

|---|---|---|

| [`docs/requirements/auditsphere-accounting-module-requirements-current.md`](requirements/auditsphere-accounting-module-requirements-current.md) | Current STE functional requirements (v2.2): commercial, onboarding, planning, fieldwork, completion, records and firm operations, plus the seven-phase workflow experience layer and enhanced Dual-Key Acceptance. | `CURRENT` |

| [`docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md`](requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md) | Upstream STE Audit Management requirements, version 2.2, as supplied by the owner. Source text: the requirements copy above is the implementation authority, and recorded deviations are in the ADR register. | `CURRENT` |

| [`docs/requirements/auditsphere-requirements-specification-product-brief-current.md`](requirements/auditsphere-requirements-specification-product-brief-current.md) | Product brief: problem, personas, in-scope modules, non-goals and success measures. | `CURRENT` |
| [`docs/requirements/auditsphere-requirements-catalog-domain-glossary-current.md`](requirements/auditsphere-requirements-catalog-domain-glossary-current.md) | Domain glossary: one meaning per term, with the code identifier to use in new code. | `CURRENT` |

---

## 5. Proposed Requirements & Backlogs

These documents describe planned features, prototype gap closures, or onboarding designs that are **not yet accepted or implemented capability**:

| Document | Purpose | Authority |
|---|---|---|
| [`docs/execution/auditsphere-execution-user-stories-ste-v22-implementation-backlog-proposed.md`](execution/auditsphere-execution-user-stories-ste-v22-implementation-backlog-proposed.md) | Supplied v2.2 baseline backlog with decision/external gates and current integration notes; story IDs are local to that document. | `PROPOSED` |
| [`docs/requirements/auditsphere-m365-onboarding-user-stories.md`](requirements/auditsphere-m365-onboarding-user-stories.md) | Proposed tenant administrator onboarding, invitation flows, and role assignment designs (AS-M365-SSO-001). | `PROPOSED` |
| [`docs/execution/auditsphere-execution-workflow-roadmap-ste-closure-proposed.md`](execution/auditsphere-execution-workflow-roadmap-ste-closure-proposed.md) | STE closure roadmap: milestones M0 to M4 that order the existing and new backlog. | `PROPOSED` |
| [`docs/execution/auditsphere-execution-user-stories-ste-v21-remaining-proposed.md`](execution/auditsphere-execution-user-stories-ste-v21-remaining-proposed.md) | Remaining STE v2.1 stories STE-NXT-001 to STE-NXT-013 with acceptance criteria. | `PROPOSED` |
| [`docs/execution/auditsphere-execution-user-stories-cloud-practice-management-v22-proposed.md`](execution/auditsphere-execution-user-stories-cloud-practice-management-v22-proposed.md) | Owner-supplied practice management completion program: stories PM-00 to PM-17 and the candidate-gap appendix. A plan, not implementation authority. | `PROPOSED` |
| [`docs/requirements/auditsphere-requirements-specification-complete-practice-management-proposed.md`](requirements/auditsphere-requirements-specification-complete-practice-management-proposed.md) | Complete-product scope, service profiles, criteria and separate professional/live release gates; inventory uses the existing PM crosswalk. | `PROPOSED` |
| [`docs/execution/auditsphere-execution-tracker-practice-management-v22-crosswalk-current.md`](execution/auditsphere-execution-tracker-practice-management-v22-crosswalk-current.md) | Disposition of each PM story and candidate gap against the code, with the basis for each. | `CURRENT` |
| [`docs/execution/auditsphere-execution-report-practice-management-criterion-audit-current.md`](execution/auditsphere-execution-report-practice-management-criterion-audit-current.md) | Source/test-body audit of all 87 expanded practice-management criteria, namespaced `ASPM-*`, with legacy aliases and open acceptance gates. | `CURRENT` |
| [`docs/execution/auditsphere-execution-tracker-technical-spikes-proposed.md`](execution/auditsphere-execution-tracker-technical-spikes-proposed.md) | Time-boxed technical spikes SPK-01 to SPK-04. | `PROPOSED` |
| [`docs/execution/auditsphere-execution-report-spk-01-archive-immutability-proposed.md`](execution/auditsphere-execution-report-spk-01-archive-immutability-proposed.md) | SPK-01 finding: four ways to protect the ISA 230 archive, compared, with a recommendation. | `PROPOSED` |
| [`docs/execution/auditsphere-audit-report-normalized-pbt-spk-03-proposed.md`](execution/auditsphere-audit-report-normalized-pbt-spk-03-proposed.md) | SPK-03 proposal retained for rationale. STE-NXT-010's decision is closed by ADR-0011: mapped PBT excluding tax, without normalization. A future normalization policy needs a separate approved story. | `PROPOSED` (retained rationale) |
| [`docs/task_breakdown/source/auditsphere-implementation-spec-atomic-gap-remediation-proposed.md`](task_breakdown/source/auditsphere-implementation-spec-atomic-gap-remediation-proposed.md) | 43-task atomic gap-remediation specification over the supplied gap report. Several tasks require owner decisions or live acceptance; the plan is not implementation or acceptance authority. | `PROPOSED` |

---

## 6. Record-to-Report (R2R) Workstream

The Record-to-Report workstream covers Modules 20–26 under `docs/task_breakdown/`:

- **Master Index:** [`docs/task_breakdown/auditsphere-r2r-index-task-breakdown.md`](task_breakdown/auditsphere-r2r-index-task-breakdown.md) (Entry point for 75 tasks across 21 work packages).

- **Module Contracts:**

  - [Module 20: Accounting Workspace](task_breakdown/modules/auditsphere-r2r-module-20-accounting-workspace-contract.md)

  - [Module 21: Trial Balance & General Ledger](task_breakdown/modules/auditsphere-r2r-module-21-trial-balance-gl-contract.md)

  - [Module 22: Adjustments & Journals](task_breakdown/modules/auditsphere-r2r-module-22-adjustments-journals-contract.md)

  - [Module 23: Reconciliations & Schedules](task_breakdown/modules/auditsphere-r2r-module-23-reconciliations-specialist-schedules-contract.md)

  - [Module 24: Financial Statements](task_breakdown/modules/auditsphere-r2r-module-24-financial-statements-contract.md)

  - [Module 25: Financial Packages & Release](task_breakdown/modules/auditsphere-r2r-module-25-financial-packages-contract.md)

  - [Module 26: Group Consolidation](task_breakdown/modules/auditsphere-r2r-module-26-consolidation-contract.md)

- **Task Cards:** 75 numbered task files located under `docs/task_breakdown/tasks/` (`auditsphere-r2r-task-t001` through `t054`, and `auditsphere-audit-task-t055` through `t075`).

- **Tracking & Validation:**

  - [`auditsphere-audit-tracker-workflow-traceability.md`](task_breakdown/tracking/auditsphere-audit-tracker-workflow-traceability.md): AC/AWP procedure traceability matrix.

  - [`auditsphere-r2r-report-pack-validation.md`](task_breakdown/tracking/auditsphere-r2r-report-pack-validation.md): Task pack structure and validator rules.

  - [`auditsphere-r2r-tracker-status-history.md`](task_breakdown/tracking/auditsphere-r2r-tracker-status-history.md): Append-only log of task lifecycle transitions.

## 7. Audit Programme and Fieldwork Workstreams

### Supplied 20-section/165-procedure audit working process

- **Supplied source:** [`auditsphere-audit-source-working-process.docx`](task_breakdown/source/auditsphere-audit-source-working-process.docx), preserved by SHA-256 in the specification and an independent procedure fixture; extracted text is in [`auditsphere-audit-working-process-procedures-source.md`](task_breakdown/source/auditsphere-audit-working-process-procedures-source.md).
- **Implementation specification:** [`auditsphere-audit-specification-working-process-atomic-proposed.md`](task_breakdown/source/auditsphere-audit-specification-working-process-atomic-proposed.md), eleven dependency-ordered tasks covering the 20-section, 165-procedure supplied working process. Tasks 01–09 are implemented; Task 10 is `BLOCKED_DECISION` on D1; Task 11 has focused technical evidence only.
- **Native workspace and contracts:** `/app/engagements/:id/audit-fieldwork`; exact `/api/ui/procedures/{id}/commands/{state,preview,actions,receipts}`; append-only command receipts; server-resolved submitted evidence snapshots. See the [code map](architecture/auditsphere-architecture-code-map.md), [contract map](architecture/auditsphere-architecture-contract-data-model-current.md), [current-slice handoff](execution/auditsphere-execution-current-slice.md) and [status.json](execution/status.json).
- **Reference excerpts:** 10 contract reference papers under `docs/task_breakdown/reference/` (`HISTORICAL_REFERENCE`).

### Task cards

- **Audit Program Library & Engagements:** Tasks T055–T059 under `docs/task_breakdown/tasks/17_Audit_Foundation/`.
- **Fieldwork Core:** Tasks T060–T065 under `docs/task_breakdown/tasks/18_Audit_Fieldwork_Core/` (Cash, Receivables, Inventory, Revenue, Payables).
- **Fieldwork Extended:** Tasks T066–T072 under `docs/task_breakdown/tasks/19_Audit_Fieldwork_Extended/` (Fixed assets, Expenses, Payroll, Loans, Related parties, Tax, Fraud/JE testing).
- **Audit Completion:** Tasks T073–T075 under `docs/task_breakdown/tasks/20_Audit_Completion/` (Analytical review, Going concern, Subsequent events & handover).

---

## 8. Current Execution State & Backlogs

| Document | Purpose | Authority |

|---|---|---|

| [`docs/execution/auditsphere-execution-index-task-board-current.md`](execution/auditsphere-execution-index-task-board-current.md) | **Agent task board**: every R2R, AUD and STE task with its record status, code verdict, blockers, open gaps and next action. Generated by `scripts/docs/build-task-board.py`. Start here to pick up work. | `CURRENT` |
| [`docs/execution/auditsphere-execution-user-stories-as-ux-001-figma-angular-parity-approved.md`](execution/auditsphere-execution-user-stories-as-ux-001-figma-angular-parity-approved.md) | Approved, incremental visual-adoption story for the existing Angular application; the React prototype is visual reference only. | `APPROVED` |
| [`docs/execution/auditsphere-execution-tracker-as-ux-001-figma-angular-parity-current.md`](execution/auditsphere-execution-tracker-as-ux-001-figma-angular-parity-current.md) | Package-level implementation and acceptance status for AS-UX-001, including verified partial slices and remaining visual/accessibility evidence. | `CURRENT` |
| [Trial balance-to-report delta review](execution/auditsphere-execution-tracker-trial-balance-report-current.md) | Source-checked intake, existing task mappings, technical corrections and unresolved reporting acceptance. | Execution review; no policy approval |
| [`docs/execution/auditsphere-execution-index-deduplicated-backlog-current.md`](execution/auditsphere-execution-index-deduplicated-backlog-current.md) | **Prioritized execution queue**: deduplicates overlapping task IDs and labels each next action as code, test/evidence, owner decision, governance or external acceptance. | `CURRENT` |
| [`docs/execution/auditsphere-execution-report-task-codebase-audit-current.json`](execution/auditsphere-execution-report-task-codebase-audit-current.json) | Codebase audit record behind the board: each item's requests, tests, gaps and reviewer outcome, at commit `13407156`. | `CURRENT` |
| [`docs/execution/status.json`](execution/status.json) | **Machine-Readable Ledger** — authoritative record of verified commit SHA, test counts, migration counts, external gates, and active slice. | `CURRENT` (Metrics authority) |

| [`docs/execution/auditsphere-execution-current-slice.md`](execution/auditsphere-execution-current-slice.md) | **Active Slice Handoff** — summary of the active vertical slice, recent changes, local verification state, and next actions. | `CURRENT` |

| [`docs/execution/auditsphere-execution-pending-tasks.md`](execution/auditsphere-execution-pending-tasks.md) | **Pending Backlog** — open local tasks, unresolved decisions, debt, and blocked external gates (P1–P10). | `CURRENT` |
| [Documentation reconciliation](execution/auditsphere-execution-report-documentation-reconciliation-current.md) | Current instruction conflicts, controlling ADRs, delegated engineering resolutions and the proposed D1 terminal-policy review packet. Does not approve professional policy or external acceptance. | `CURRENT` (reconciliation; D1 proposal unapproved) |
| [`docs/execution/auditsphere-execution-audit-production-readiness-gap-closure-current.md`](execution/auditsphere-execution-audit-production-readiness-gap-closure-current.md) | **Production-readiness audit and gap-closure register** — findings, residual CP stories and production gates. **NOT COMPLETE**; not a completion certificate or current-SHA verification. | `PARTIAL` |

| [`docs/execution/auditsphere-execution-tracker-ste-v21-gap-verification-current.md`](execution/auditsphere-execution-tracker-ste-v21-gap-verification-current.md) | Requirement-by-requirement verification of the STE v2.1 specification against current code, plus the v2.2 workflow-experience delta. | `CURRENT` |
| [`docs/execution/auditsphere-execution-user-stories-ste-v21-gap-closure-current.md`](execution/auditsphere-execution-user-stories-ste-v21-gap-closure-current.md) | The ten STE-GAP-001 to STE-GAP-010 definitions verbatim from the owner's gap analysis, mapped to the verification table and to each story's state. | `CURRENT` |
| [`docs/execution/ste-v2-1-acceptance-manifest.json`](execution/ste-v2-1-acceptance-manifest.json) | Machine-readable STE v2.1 acceptance manifest (v2.1 scope, not regenerated for v2.2): 62 mandatory checks, their evidence status, external blockers and the overall result. Generated by `scripts/acceptance/generate-ste-manifest.py`. | `CURRENT` |
| [`docs/execution/auditsphere-execution-report-spk-04-ledger-split-current.md`](execution/auditsphere-execution-report-spk-04-ledger-split-current.md) | SPK-04 finding: how the execution ledger was split without losing evidence, and what the archives hold. | `CURRENT` |
| [`docs/execution/status-archive-2026-10.json`](execution/status-archive-2026-10.json) | Archive of closed `status.json` evidence, including verification, local evidence, documentation work and superseded targeted-verification records, preserved verbatim. | `HISTORICAL` |
| [`docs/execution/auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md`](execution/auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md) | Older slice sections moved verbatim from the current-slice handoff on 2026-10-08. | `HISTORICAL` |
| [`docs/execution/auditsphere-execution-report-agent-pack-application-historical.md`](execution/auditsphere-execution-report-agent-pack-application-historical.md) | Record of the 2026-10-08 application of the agent context pack: checks run, and how each conflicting pack file was merged. | `HISTORICAL` |
| [`docs/architecture/auditsphere-architecture-patch-allow-claude-md-root-file-historical.patch`](architecture/auditsphere-architecture-patch-allow-claude-md-root-file-historical.patch) | Applied patch that lets `CLAUDE.md` pass the root-file checks of the docs gates. | `HISTORICAL` |
| [`docs/execution/auditsphere-execution-guide-agent-task-prompt-current.md`](execution/auditsphere-execution-guide-agent-task-prompt-current.md) | Agent task prompt template with a filled example. | `CURRENT` |

---

## 9. Testing Documentation

| Document | Purpose | Authority |

|---|---|---|

| [`docs/testing/auditsphere-testing-e2e-automation-strategy.md`](testing/auditsphere-testing-e2e-automation-strategy.md) | Playwright end-to-end automation strategy, CI pipeline execution models, and test fixtures. | `CURRENT` |

| [`docs/testing/auditsphere-testing-test-case-catalog.md`](testing/auditsphere-testing-test-case-catalog.md) | Historical baseline inventory of unit, integration and E2E suites. Current discovery and results belong in `status.json`. | `HISTORICAL_REFERENCE` |
| [`docs/testing/auditsphere-scope-and-tenant-admin-test-cases.md`](testing/auditsphere-scope-and-tenant-admin-test-cases.md) | Proposed scope-isolation and live Development tenant administration acceptance cases; execution results belong in the ledger. | `CURRENT` |

| [`docs/testing/auditsphere-testing-strategy-definition-of-done-current.md`](testing/auditsphere-testing-strategy-definition-of-done-current.md) | Test strategy and Definition of Done for the restored suites: what counts as verification. | `CURRENT` |
| [Agent capability checks](testing/auditsphere-testing-guide-agent-capability-checks-current.md) | Bounded task packets, read-only prerequisite checks, opt-in focused verification and evidence reports. | `CURRENT` |

---

## 10. Operations & Runbooks

| Document | Purpose | Authority |

|---|---|---|

| [`docs/operations/auditsphere-operations-runbook-restore-drill.md`](operations/auditsphere-operations-runbook-restore-drill.md) | Runbook and execution boundaries for local PostgreSQL loopback restore rehearsals (`scripts/db/restore-drill.sh`). | `CURRENT` |

| [`docs/operations/auditsphere-operations-decision-connection-capacity.md`](operations/auditsphere-operations-decision-connection-capacity.md) | PostgreSQL connection pool sizing, concurrency limits, and PgBouncer evaluation. | `CURRENT` |

| [`docs/operations/auditsphere-operations-guide-telemetry.md`](operations/auditsphere-operations-guide-telemetry.md) | OpenTelemetry ActivitySource and Meter diagnostic configuration for the API and Worker hosts. | `CURRENT` |

| [`infra/auditsphere-deployment-iac-runbook.md`](../infra/auditsphere-deployment-iac-runbook.md) | Source-controlled Azure Bicep / Container Apps definition of the production topology; templates compile locally and live deployment remains `BLOCKED_EXTERNAL`. | `CURRENT` |

---

## 11. Evidence & Methodology Approvals

| Document | Purpose | Authority |

|---|---|---|

| [`docs/evidence/auditsphere-evidence-approval-methodology-ste-meth-app-001-approved.md`](evidence/auditsphere-evidence-approval-methodology-ste-meth-app-001-approved.md) | Firm Methodology Owner approval `STE-METH-APP-001` for Financial Statement Template v1.0, Audit Program v1.0, and ECL Provision Matrix v1.0. | `APPROVED` |

| [`docs/evidence/auditsphere-evidence-approval-methodology-ste-meth-app-001-addendum-approved.md`](evidence/auditsphere-evidence-approval-methodology-ste-meth-app-001-addendum-approved.md) | Methodology addendum extending approval to advanced consolidation methods and IAS 21 currency translation. | `APPROVED` |

---

## 12. Historical Requirement Sources

These files preserve original uploaded requirements and must **never** be edited, reformatted, or treated as architectural authority:

| Document | Purpose | Authority |

|---|---|---|

| [`docs/task_breakdown/source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md`](task_breakdown/source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md) | Original Record-to-Report blueprint for Modules 20–26. Hash-pinned by pack validator (`3847e73e...`). | `HISTORICAL_SOURCE` |

| [`docs/task_breakdown/source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md`](task_breakdown/source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md) | Original audit workflow gap-closure user stories. Hash-pinned by pack validator (`c05b20c9...`). | `HISTORICAL_SOURCE` |

---

## 13. AI Agent Reading Rules

1. **One Authority per Fact:**

   - Architecture → `docs/architecture/auditsphere-architecture-current-architecture.md`

   - Code files → `docs/architecture/auditsphere-architecture-code-map.md`

   - Filenames → `docs/architecture/auditsphere-architecture-document-naming-policy.md`

   - Live metrics (test/migration counts, SHA) → `docs/execution/status.json`

2. **Never Overwrite Historical Evidence:** Sealed datasets, approved mappings, applied journals, issued packages, and review decisions are append-only.

3. **Fail-Closed Methodology:** Missing exchange rates, unsupported valuation methods, or unapproved perimeters must fail closed and block approval/release.

4. **Inspect Code Before Writing:** Many features in task cards or user stories are already implemented. Always inspect existing classes and methods in `AuditSphereOps.Application` and `AuditSphereOps.Domain`.

5. **No Speculative Architecture:** Do not introduce MediatR, microservices, repository abstractions, or external message brokers.
