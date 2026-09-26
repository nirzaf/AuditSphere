# AuditSphereOps — Pending Work & Unresolved Backlog



**Status:** CURRENT

**Purpose:** Canonical inventory of open local tasks, unresolved decisions, technical debt, and blocked external gates.

**Authority:** Implementation backlog authority for unfinished work. Observed verification state and volatile metrics belong exclusively to [`docs/execution/status.json`](status.json).

**Audience:** Coding agents and developers.



---



## 1. Verified Local Baseline Summary



The core modular monolith, Practice Management, Client Accounting Workspace (TB/GL intake, reconciliations, adjustments, financial statements, packages, multi-rate IAS 21 currency translation, and group consolidation), Audit Fieldwork foundations, and local M365 control-plane draft persistence are locally verified.



- **Observed Metrics & Proofs:** For current test counts, migration counts, verified commit SHA, and active slice evidence, consult [`docs/execution/status.json`](status.json).

- **Completed Capabilities (WP 1–18):** Full capability implementation is verified in local PostgreSQL suites; historical completed item checklists are preserved in Git history and structured execution records.

- **Architectural Reference:** Implemented modular monolith rules and project references are governed by [`docs/architecture/auditsphere-architecture-current-architecture.md`](../architecture/auditsphere-architecture-current-architecture.md).



---



## Story coverage and completion boundaries

This handoff is not an exhaustive list of unfinished user stories. Continue from the
[accounting stories AC-01–AC-28](../auditsphere-accounting-module-requirements-current.md),
[prototype parity stories AS-PAR-001–AS-PAR-062](../auditsphere-audit-user-stories-prototype-gap-closure-proposed.md),
[Microsoft 365 onboarding story](../auditsphere-m365-onboarding-user-stories.md), and
[R2R/audit task index](../task_breakdown/auditsphere-r2r-index-task-breakdown.md).
Checked accounting criteria and historical prototype baselines must be reconciled with
current code and exact acceptance assertions before changing story status. A passing
regression for one screen does not complete the whole AS-PAR-002 authorization audit.

The optional service profiles AS-PAR-046–AS-PAR-062 retain their stated professional
methodology and scope approval dependencies. They do not authorize client operational
ERP, payroll execution, Purview integration, or eSignature provider integration, which
root `AGENTS.md` excludes. Preserve uploaded signed evidence and human decisions.
External validation and independent review remain separate from local implementation.

Follow the system specification §46 work loop: one reviewable slice and PR at a time;
obtain the required independent review and explicit merge authorization before advancing
through acceptance gates. Do not mark the entire documentation backlog complete from
local test results.

---

## 2. Open Local Work & Unresolved Decisions



The following local tasks, architectural decisions, and follow-ups are open for implementation:



### 2.1 Microsoft 365 Onboarding & Selected-Resource Integration

- **DEC-01 (Live Entra OIDC):** Prove live Entra OIDC and directory behavior against the approved tenant. Local roster assignment and invitation persistence are verified locally, but live Graph interaction remains pending live tenant access.

- **Selected-Resource SharePoint/Graph Provider:** Implement and verify the production selected-resource SharePoint/Graph provider, durable client-root worker, engagement folder provisioning, and uncertain-outcome reconciliation.

- **Staff ACLs & Mailbox Delivery:** Implement optional direct staff ACL behavior and Microsoft Graph mailbox delivery only with authorized tenant resources. Purview provider integration is excluded by root `AGENTS.md`; it is not an eligible local implementation task.



### 2.2 Audit & Accounting Parity Follow-ups (AS-PAR-002)

- **Route & Query Revocation Audit:** Continue the whole-application route, query, search, count, export, and direct-command audit to ensure protected projections clear immediately upon role-grant revocation.

- **G16 Period-End Open-Item Methodology:** Formalize and record methodology approval for period-end open-item classification and carrying-amount evidence before enabling automated open-item remeasurement workflows.

- **Stale Content & Parameter Reauthorization:** Verify route-parameter change behavior across all remaining specialized workbench screens.



### 2.3 Technical Debt & Bounded Hardening

- **Automated Documentation Health:** Maintain automated tests for documentation naming, link integrity, and exclusion of volatile metrics from narrative files.

- **Benchmark Baselines:** Maintain local benchmark verification for high-magnitude accounting datasets and concurrent worker operations.



---



## 3. External Gates (P1–P10)



These production milestones require live external infrastructure, tenant credentials, or human partner authorization. They **cannot** be closed by local mocks, simulation adapters, portal screenshots, or documentation claims:



| Phase | Gate / Objective | Status | Blocking Condition |

|---|---|---|---|

| **P1** | Live Entra OIDC Authentication | `BLOCKED_EXTERNAL` | Requires live Azure AD / Entra ID tenant registration with configured client credentials. |

| **P2** | Selected-Resource SharePoint/Graph | `BLOCKED_EXTERNAL` | Requires live Microsoft 365 tenant with approved selected-resource application permissions. |

| **P3** | External Release Checkpoint Store | `BLOCKED_EXTERNAL` | Requires production cryptographic checkpoint infrastructure and immutable remote store. |

| **P4** | Purview Records Profile Integration | `BLOCKED_EXTERNAL` | Purview integration is excluded from core application scope; live tenant label application requires live tenant. |

| **P5** | Document Signing Methodology Lineage | `BLOCKED_EXTERNAL` | eSignature provider integration is excluded from core scope; live custody requires external signing authority. |

| **P6** | Records / Archive Residual Hardening | `LOCAL_VERIFIED` | Immutable archive schema, lineage manifests, and structured exports are locally verified. |

| **P7** | Cross-Store Recovery (RPO/RTO) | `BLOCKED_EXTERNAL` | Requires custodially separate, multi-region cloud backup infrastructure and measured restore drills. |

| **P8** | Production Observability & Secrets | `BLOCKED_EXTERNAL` | Requires Azure Key Vault mount, production OpenTelemetry collector, and sensitive log scrubbing review. |

| **P9** | Independent Review & Protected Merge | `BLOCKED_EXTERNAL` | Requires independent partner sign-off and branch protections. |

| **P10**| Real-Tenant Acceptance (§47) | `BLOCKED_EXTERNAL` | Full operational pilot on customer-authorized live production tenant. |



---



## 4. Required Evidence for Every Local Slice



Every local implementation slice must adhere to the following verification standards:



1. **In-Core Scope Authorization:** Enforce explicit `RoleGrant` scope inside Application commands and queries, never exclusively in Blazor UI components.

2. **Immutability & Lineage:** Produce immutable, version-bound evidence for submitted professional conclusions; never provide an update path for sealed datasets.

3. **PostgreSQL-Backed Testing:** Pass targeted and full test suites against the local PostgreSQL 18.6 cluster on port 5433.

4. **Zero Warnings & Migration Drift:** Solution must build with `0 Warning(s)` and `0 Error(s)` in Release mode; EF Core model must have no pending migration changes (`dotnet ef migrations has-pending-model-changes`).

5. **Truthful Ledger Recording:** Update `docs/execution/status.json` and `docs/execution/auditsphere-execution-current-slice.md` strictly with observed facts.



---



## 5. External Acceptance Rule



Do not convert `BLOCKED_EXTERNAL` to `LOCAL_VERIFIED` or `APPROVED` from local tests, simulation fixtures, simulation adapters, portal screenshots, or status declarations. Each external gate requires its named human owner, authorized environment, exact observed evidence, and independent review.



---



## 6. Resume Procedure



1. Read [`AGENTS.md`](../../AGENTS.md), [`docs/auditsphere-docs-index.md`](../auditsphere-docs-index.md), and [`docs/architecture/auditsphere-architecture-current-architecture.md`](../architecture/auditsphere-architecture-current-architecture.md).

2. Consult [`docs/execution/status.json`](status.json) for current verified commit, active slice, and known blockers.

3. Select one eligible open item from Section 2 whose dependencies are satisfied.

4. Deliver the smallest coherent vertical slice with guarded transactions and scope-checked authorization.

5. Verify via standard build, test, and EF migration checks.

6. Record observed facts in `docs/execution/status.json` and [`docs/execution/auditsphere-execution-current-slice.md`](auditsphere-execution-current-slice.md).
