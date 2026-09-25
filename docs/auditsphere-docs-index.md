# AuditSphere — Central Documentation Index







**Status:** CURRENT



**Purpose:** Canonical directory, authority hierarchy, and reading guide for all repository documentation.



**Authority:** Supreme documentation navigation authority after [`AGENTS.md`](../AGENTS.md).



**Audience:** AI coding agents and human developers.







---







## 1. Start Here







When starting work in this repository, follow this deterministic reading sequence:







1. **[`AGENTS.md`](../AGENTS.md):** Core engineering rules, three financial boundaries, absolute prohibitions ("Never" rules), and local environment instructions.



2. **This Index (`docs/auditsphere-docs-index.md`):** Complete authority hierarchy and catalog of canonical documentation.



3. **[`docs/architecture/auditsphere-architecture-current-architecture.md`](architecture/auditsphere-architecture-current-architecture.md):** Binding implementation architecture (modular monolith, static capability services, partial classes).



4. **[`docs/architecture/auditsphere-architecture-code-map.md`](architecture/auditsphere-architecture-code-map.md):** Capability-to-file directory mapping business capabilities to Domain, Application, Infrastructure, UI, and Tests.



5. **[`docs/execution/status.json`](execution/status.json):** The single source of truth for verified commit SHA, test counts, migration counts, active slice, and external blockers.



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



| [`docs/architecture/auditsphere-architecture-document-naming-policy.md`](architecture/auditsphere-architecture-document-naming-policy.md) | Standard markdown naming rules (`auditsphere-<area>-<document-type>-<subject>[-<id>][-<status>].md`) and uniqueness rules. | `CURRENT` |







---







## 4. Current Normative Requirements







| Canonical Document | Purpose | Authority |



|---|---|---|



| [`docs/auditsphere-requirements-system-specification-current.md`](auditsphere-requirements-system-specification-current.md) | **v5.0 Build Contract** — system objectives, data models, entity lifecycle, accounting rules, and acceptance tests. | `CURRENT` |



| [`docs/auditsphere-accounting-module-requirements-current.md`](auditsphere-accounting-module-requirements-current.md) | Detailed accounting and consolidation gap analysis, roadmap, and user stories AC-01 through AC-28. | `CURRENT` |







---







## 5. Proposed Requirements & Backlogs







These documents describe planned features, prototype gap closures, or onboarding designs that are **not yet accepted or implemented capability**:







| Document | Purpose | Authority |



|---|---|---|



| [`docs/auditsphere-audit-user-stories-prototype-gap-closure-proposed.md`](auditsphere-audit-user-stories-prototype-gap-closure-proposed.md) | Detailed parity user stories and gap-closure backlogs derived from audit prototype analysis (AS-PAR-001..002). | `PROPOSED` |



| [`docs/auditsphere-m365-onboarding-user-stories.md`](auditsphere-m365-onboarding-user-stories.md) | Proposed tenant administrator onboarding, invitation flows, and role assignment designs (AS-M365-SSO-001). | `PROPOSED` |







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



| [`docs/execution/status.json`](execution/status.json) | **Machine-Readable Ledger** — authoritative record of verified commit SHA, test counts, migration counts, external gates, and active slice. | `CURRENT` (Metrics authority) |



| [`docs/execution/auditsphere-execution-current-slice.md`](execution/auditsphere-execution-current-slice.md) | **Active Slice Handoff** — summary of the active vertical slice, recent changes, local verification state, and next actions. | `CURRENT` |



| [`docs/execution/auditsphere-execution-pending-tasks.md`](execution/auditsphere-execution-pending-tasks.md) | **Pending Backlog** — open local tasks, unresolved decisions, debt, and blocked external gates (P1–P10). | `CURRENT` |







---







## 9. Testing Documentation







| Document | Purpose | Authority |



|---|---|---|



| [`docs/testing/auditsphere-testing-e2e-automation-strategy.md`](testing/auditsphere-testing-e2e-automation-strategy.md) | Playwright end-to-end automation strategy, CI pipeline execution models, and test fixtures. | `CURRENT` |



| [`docs/testing/auditsphere-testing-test-case-catalog.md`](testing/auditsphere-testing-test-case-catalog.md) | Inventory of unit, integration, and E2E test suites with category mapping. | `CURRENT` |







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
