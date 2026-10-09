# AuditSphere — Central Documentation Index

**Status:** CURRENT

**Purpose:** Canonical directory, authority hierarchy, and reading guide for all repository documentation.

**Authority:** Supreme documentation navigation authority after [`AGENTS.md`](../AGENTS.md).

**Audience:** AI coding agents and human developers.

---

The owner retired the former system specification, prototype parity source and UI migration document. Use the current STE functional requirements, current architecture and execution coverage for implementation; retired text remains in Git history only.

Latest finance-family review: [Blazor firm finance source review](migration/auditsphere-migration-blazor-finance-source-review.md). All three source/action rows remain partial pending full behavior and authorization evidence.

Supporting Web artifacts: [Blazor supporting-file source review](migration/auditsphere-migration-blazor-supporting-files-source-review.md) records hash-pinned partial reviews for the 17 previously unanalyzed host, configuration, styling and browser-support files. No supporting artifact is accepted as parity or approved for removal.

Current presentation migration: [Angular migration and route ownership](architecture/auditsphere-angular-migration-current.md), with reviewed source evidence in the [Blazor administration source review](migration/auditsphere-migration-blazor-administration-source-review.md), [Blazor PBC source review](migration/auditsphere-migration-blazor-pbc-source-review.md), [Blazor accounting artifact source review](migration/auditsphere-migration-blazor-accounting-artifact-source-review.md), [Blazor accounting workspace and period maintenance source review](migration/auditsphere-migration-blazor-accounting-maintenance-source-review.md), [Blazor audit planning and fieldwork source review](migration/auditsphere-migration-blazor-audit-planning-fieldwork-source-review.md), [Blazor acceptance checklist source review](migration/auditsphere-migration-blazor-acceptance-checklist-source-review.md), [Blazor engagement activation source review](migration/auditsphere-migration-blazor-engagement-activation-source-review.md), [Blazor engagement creation source review](migration/auditsphere-migration-blazor-engagement-creation-source-review.md), [Blazor completion and file records source review](migration/auditsphere-migration-blazor-completion-file-records-source-review.md), [Blazor shell, search, portfolio and profile source review](migration/auditsphere-migration-blazor-shell-portfolio-source-review.md), [Blazor commercial workflow source review](migration/auditsphere-migration-blazor-commercial-source-review.md), [Blazor resource planning, budget and staffing source review](migration/auditsphere-migration-blazor-resource-planning-source-review.md), [Blazor practice operations source review](migration/auditsphere-migration-blazor-practice-operations-source-review.md), [Blazor finance source review](migration/auditsphere-migration-blazor-finance-source-review.md), [Blazor records source review](migration/auditsphere-migration-blazor-records-source-review.md), [Blazor statement and intake source review](migration/auditsphere-migration-blazor-statement-intake-source-review.md), [Blazor ReviewPoint source review](migration/auditsphere-migration-blazor-review-point-source-review.md), [Blazor Workpaper source review](migration/auditsphere-migration-blazor-workpaper-source-review.md), [Blazor Operations Recovery source review](migration/auditsphere-migration-blazor-operations-recovery-source-review.md), [Blazor Client Portal Home source review](migration/auditsphere-migration-blazor-client-portal-source-review.md), [Blazor shared confirmation source review](migration/auditsphere-migration-blazor-confirm-dialog-source-review.md), and [Blazor group consolidation source review](migration/auditsphere-migration-blazor-consolidation-source-review.md). The owner attached a new migration guide; former retired UI documents remain historical.

## 1. Start Here

When starting work in this repository, follow this deterministic reading sequence:

1. **[`AGENTS.md`](../AGENTS.md):** Core engineering rules, three financial boundaries, absolute prohibitions ("Never" rules), and local environment instructions.

2. **This Index (`docs/auditsphere-docs-index.md`):** Complete authority hierarchy and catalog of canonical documentation.

3. **[`docs/architecture/auditsphere-architecture-current-architecture.md`](architecture/auditsphere-architecture-current-architecture.md):** Binding implementation architecture (modular monolith, static capability services, partial classes).

4. **[`docs/architecture/auditsphere-architecture-code-map.md`](architecture/auditsphere-architecture-code-map.md):** Capability-to-file directory mapping business capabilities to Domain, Application, Infrastructure, UI, and Tests.

5. **Execution ledger ([`docs/execution/status.json`](execution/status.json)):** Query it with the `jq` command in [`AGENTS.md`](../AGENTS.md) §6 instead of reading the whole file. It is the single source of truth for verified commit SHA, test counts, migration counts, active slice and external blockers.

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

- **Architecture Wins over Proposed Detail:** Historical blueprints or task cards mentioning MediatR, bUnit, or microservices are superseded by the current modular monolith architecture.

- **Evidence Proves Execution, Not Architecture:** Execution logs prove what ran locally; they do not alter requirement or architecture definitions.

- **Historical Sources Preserve Provenance:** Historical sources preserve byte-level requirement text; they never act as implementation authority.

- **Volatile Metrics Live Only in `status.json`:** Test counts, migration counts, CI run IDs, and verified commit SHAs belong exclusively to `status.json`.

---

## 3. Architecture & Code Structure

| Canonical Document | Purpose | Authority |

|---|---|---|

| [`docs/architecture/auditsphere-architecture-current-architecture.md`](architecture/auditsphere-architecture-current-architecture.md) | Authoritative modular monolith architecture, capability partials, pure calculators, and durable operation patterns. | `CURRENT` |

| [`docs/architecture/auditsphere-architecture-code-map.md`](architecture/auditsphere-architecture-code-map.md) | Business-capability-to-code directory mapping services, DbContext partials, calculators, UI workbenches, and tests. | `CURRENT` |

| [`docs/architecture/auditsphere-m365-tenant-administration-permissions.md`](architecture/auditsphere-m365-tenant-administration-permissions.md) | Microsoft 365 capability permission matrix, consent contract and external-operation lifecycle; optional capabilities implemented and disabled by default. | `APPROVED` |

| [`docs/architecture/auditsphere-architecture-document-naming-policy.md`](architecture/auditsphere-architecture-document-naming-policy.md) | Standard markdown naming rules (`auditsphere-<area>-<document-type>-<subject>[-<id>][-<status>].md`) and uniqueness rules. | `CURRENT` |

| [`docs/architecture/adr/auditsphere-architecture-index-adr-register-current.md`](architecture/adr/auditsphere-architecture-index-adr-register-current.md) | ADR register: ADR-0001 to ADR-0008, retroactive records that stay `PROPOSED` until the owner confirms them. | `CURRENT` (register) |
| [`docs/architecture/auditsphere-architecture-contract-data-model-current.md`](architecture/auditsphere-architecture-contract-data-model-current.md) | Data model index: storage conventions, model partials by specification module, schema change procedure. | `CURRENT` |
| [`docs/architecture/auditsphere-architecture-policy-non-functional-requirements-current.md`](architecture/auditsphere-architecture-policy-non-functional-requirements-current.md) | Non-functional requirements: enforced limits, rate budgets and open targets (P7, P8). | `CURRENT` |
| [`docs/architecture/auditsphere-architecture-index-agent-context-current.md`](architecture/auditsphere-architecture-index-agent-context-current.md) | Agent context slots in loading order, context hazards and the pack apply steps. | `CURRENT` |
| [`docs/architecture/auditsphere-angular-conventions-current.md`](architecture/auditsphere-angular-conventions-current.md) | Native Angular coding conventions, draft/assent rules and safe AI-assisted implementation boundaries for the presentation migration. | `CURRENT` |
| [`docs/architecture/auditsphere-architecture-http-boundary-and-contract-current.md`](architecture/auditsphere-architecture-http-boundary-and-contract-current.md) | Hardened HTTP boundary: authenticated `/api/ui` groups, rate-limit catalog, security headers, request-size policy, `ApiError` body, and the canonical OpenAPI 3.1 contract with CI drift protection. | `CURRENT` |
| [`docs/migration/auditsphere-migration-blazor-retirement-inventory.md`](migration/auditsphere-migration-blazor-retirement-inventory.md), [route matrix](migration/auditsphere-migration-blazor-angular-route-parity.md), [feature register](migration/auditsphere-migration-blazor-feature-parity.md), [test register](migration/auditsphere-migration-blazor-test-parity.md), [removal gate](migration/auditsphere-migration-blazor-removal-readiness.md), and [stop-gate report](migration/auditsphere-migration-blazor-retirement-final-report.md) | Source discovery and evidence registers for the gradual Blazor retirement. Route ownership is not behavior parity; physical removal is currently blocked. | `PARTIAL` |

---

## 4. Current Normative Requirements

| Canonical Document | Purpose | Authority |

|---|---|---|

| [`docs/requirements/auditsphere-accounting-module-requirements-current.md`](requirements/auditsphere-accounting-module-requirements-current.md) | Current STE functional requirements: commercial, onboarding, planning, fieldwork, completion, records and firm operations. | `CURRENT` |

| [`docs/requirements/auditsphere-requirements-specification-product-brief-current.md`](requirements/auditsphere-requirements-specification-product-brief-current.md) | Product brief: problem, personas, in-scope modules, non-goals and success measures. | `CURRENT` |
| [`docs/requirements/auditsphere-requirements-catalog-domain-glossary-current.md`](requirements/auditsphere-requirements-catalog-domain-glossary-current.md) | Domain glossary: one meaning per term, with the code identifier to use in new code. | `CURRENT` |

---

## 5. Proposed Requirements & Backlogs

These documents describe planned features, prototype gap closures, or onboarding designs that are **not yet accepted or implemented capability**:

| Document | Purpose | Authority |

|---|---|---|

| [`docs/requirements/auditsphere-m365-onboarding-user-stories.md`](requirements/auditsphere-m365-onboarding-user-stories.md) | Proposed tenant administrator onboarding, invitation flows, and role assignment designs (AS-M365-SSO-001). | `PROPOSED` |

| [`docs/execution/auditsphere-execution-workflow-roadmap-ste-closure-proposed.md`](execution/auditsphere-execution-workflow-roadmap-ste-closure-proposed.md) | STE closure roadmap: milestones M0 to M4 that order the existing and new backlog. | `PROPOSED` |
| [`docs/execution/auditsphere-execution-user-stories-ste-v21-remaining-proposed.md`](execution/auditsphere-execution-user-stories-ste-v21-remaining-proposed.md) | Remaining STE v2.1 stories STE-NXT-001 to STE-NXT-013 with acceptance criteria. | `PROPOSED` |
| [`docs/execution/auditsphere-execution-tracker-technical-spikes-proposed.md`](execution/auditsphere-execution-tracker-technical-spikes-proposed.md) | Time-boxed technical spikes SPK-01 to SPK-04. | `PROPOSED` |
| [`docs/execution/auditsphere-execution-report-spk-01-archive-immutability-proposed.md`](execution/auditsphere-execution-report-spk-01-archive-immutability-proposed.md) | SPK-01 finding: four ways to protect the ISA 230 archive, compared, with a recommendation. | `PROPOSED` |
| [`docs/execution/auditsphere-audit-report-normalized-pbt-spk-03-proposed.md`](execution/auditsphere-audit-report-normalized-pbt-spk-03-proposed.md) | SPK-03 methodology note on normalized profit before tax, with a worked example, for the owner's decision (STE-NXT-010). | `PROPOSED` |

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

- **Reference Excerpts:** 10 contract reference papers under `docs/task_breakdown/reference/` (`HISTORICAL_REFERENCE`).

---

## 7. Audit Fieldwork Workstream

- **Audit Program Library & Engagements:** Tasks T055–T059 under `docs/task_breakdown/tasks/17_Audit_Foundation/`.

- **Fieldwork Core:** Tasks T060–T065 under `docs/task_breakdown/tasks/18_Audit_Fieldwork_Core/` (Cash, Receivables, Inventory, Revenue, Payables).

- **Fieldwork Extended:** Tasks T066–T072 under `docs/task_breakdown/tasks/19_Audit_Fieldwork_Extended/` (Fixed assets, Expenses, Payroll, Loans, Related parties, Tax, Fraud/JE testing).

- **Audit Completion:** Tasks T073–T075 under `docs/task_breakdown/tasks/20_Audit_Completion/` (Analytical review, Going concern, Subsequent events & handover).

---

## 8. Current Execution State & Backlogs

| Document | Purpose | Authority |

|---|---|---|

| [`docs/execution/auditsphere-execution-index-task-board-current.md`](execution/auditsphere-execution-index-task-board-current.md) | **Agent task board**: every R2R, AUD and STE task with its record status, code verdict, blockers, open gaps and next action. Generated by `scripts/docs/build-task-board.py`. Start here to pick up work. | `CURRENT` |
| [`docs/execution/auditsphere-execution-index-deduplicated-backlog-current.md`](execution/auditsphere-execution-index-deduplicated-backlog-current.md) | **Prioritized execution queue**: deduplicates overlapping task IDs and labels each next action as code, test/evidence, owner decision, governance or external acceptance. | `CURRENT` |
| [`docs/execution/auditsphere-execution-report-task-codebase-audit-current.json`](execution/auditsphere-execution-report-task-codebase-audit-current.json) | Codebase audit record behind the board: each item's requests, tests, gaps and reviewer outcome, at commit `13407156`. | `CURRENT` |
| [`docs/execution/status.json`](execution/status.json) | **Machine-Readable Ledger** — authoritative record of verified commit SHA, test counts, migration counts, external gates, and active slice. | `CURRENT` (Metrics authority) |

| [`docs/execution/auditsphere-execution-current-slice.md`](execution/auditsphere-execution-current-slice.md) | **Active Slice Handoff** — summary of the active vertical slice, recent changes, local verification state, and next actions. | `CURRENT` |

| [`docs/execution/auditsphere-execution-pending-tasks.md`](execution/auditsphere-execution-pending-tasks.md) | **Pending Backlog** — open local tasks, unresolved decisions, debt, and blocked external gates (P1–P10). | `CURRENT` |

| [`docs/execution/auditsphere-execution-tracker-ste-v21-gap-verification-current.md`](execution/auditsphere-execution-tracker-ste-v21-gap-verification-current.md) | Requirement-by-requirement verification of the STE v2.1 specification against current code. | `CURRENT` |
| [`docs/execution/auditsphere-execution-user-stories-ste-v21-gap-closure-current.md`](execution/auditsphere-execution-user-stories-ste-v21-gap-closure-current.md) | The ten STE-GAP-001 to STE-GAP-010 definitions verbatim from the owner's gap analysis, mapped to the verification table and to each story's state. | `CURRENT` |
| [`docs/execution/ste-v2-1-acceptance-manifest.json`](execution/ste-v2-1-acceptance-manifest.json) | Machine-readable STE v2.1 acceptance manifest: 62 mandatory checks, their evidence status, external blockers and the overall result. Generated by `scripts/acceptance/generate-ste-manifest.py`. | `CURRENT` |
| [`docs/execution/auditsphere-execution-report-spk-04-ledger-split-current.md`](execution/auditsphere-execution-report-spk-04-ledger-split-current.md) | SPK-04 finding: how the execution ledger was split without losing evidence, and what the archives hold. | `CURRENT` |
| [`docs/execution/status-archive-2026-10.json`](execution/status-archive-2026-10.json) | Archive of the `status.json` history keys (`verification`, `localEvidence`, `documentationWork`), moved verbatim on 2026-10-08. | `HISTORICAL` |
| [`docs/execution/auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md`](execution/auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md) | Older slice sections moved verbatim from the current-slice handoff on 2026-10-08. | `HISTORICAL` |
| [`docs/execution/auditsphere-execution-report-agent-pack-application-historical.md`](execution/auditsphere-execution-report-agent-pack-application-historical.md) | Record of the 2026-10-08 application of the agent context pack: checks run, and how each conflicting pack file was merged. | `HISTORICAL` |
| [`docs/architecture/auditsphere-architecture-patch-allow-claude-md-root-file-historical.patch`](architecture/auditsphere-architecture-patch-allow-claude-md-root-file-historical.patch) | Applied patch that lets `CLAUDE.md` pass the root-file checks of the docs gates. | `HISTORICAL` |
| [`docs/execution/auditsphere-execution-guide-agent-task-prompt-current.md`](execution/auditsphere-execution-guide-agent-task-prompt-current.md) | Agent task prompt template with a filled example. | `CURRENT` |

---

## 9. Testing Documentation

| Document | Purpose | Authority |

|---|---|---|

| [`docs/testing/auditsphere-testing-e2e-automation-strategy.md`](testing/auditsphere-testing-e2e-automation-strategy.md) | Playwright end-to-end automation strategy, CI pipeline execution models, and test fixtures. | `CURRENT` |

| [`docs/testing/auditsphere-testing-test-case-catalog.md`](testing/auditsphere-testing-test-case-catalog.md) | Inventory of unit, integration, and E2E test suites with category mapping. | `CURRENT` |
| [`docs/testing/auditsphere-scope-and-tenant-admin-test-cases.md`](testing/auditsphere-scope-and-tenant-admin-test-cases.md) | Proposed scope-isolation and live Development tenant administration acceptance cases; execution results belong in the ledger. | `CURRENT` |

| [`docs/testing/auditsphere-testing-strategy-definition-of-done-current.md`](testing/auditsphere-testing-strategy-definition-of-done-current.md) | Test strategy and Definition of Done for the restored suites: what counts as verification. | `CURRENT` |

---

## 10. Operations & Runbooks

| Document | Purpose | Authority |

|---|---|---|

| [`docs/operations/auditsphere-operations-runbook-restore-drill.md`](operations/auditsphere-operations-runbook-restore-drill.md) | Runbook and execution boundaries for local PostgreSQL loopback restore rehearsals (`scripts/db/restore-drill.sh`). | `CURRENT` |

| [`docs/operations/auditsphere-operations-decision-connection-capacity.md`](operations/auditsphere-operations-decision-connection-capacity.md) | PostgreSQL connection pool sizing, concurrency limits, and PgBouncer evaluation. | `CURRENT` |

| [`docs/operations/auditsphere-operations-guide-telemetry.md`](operations/auditsphere-operations-guide-telemetry.md) | OpenTelemetry ActivitySource and Meter diagnostic configuration for Web and Worker hosts. | `CURRENT` |

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
