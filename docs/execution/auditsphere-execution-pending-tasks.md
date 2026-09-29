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

- **Selected-Resource SharePoint/Graph Provider:** Implemented and exercised live in Development: administrator provisioning of client workspace and engagement PBC repository folders, binding capability verification by disposable upload and exact read-back, the live `GraphPbcProviderSink`, the isolated Acceptance `pbc` worker, and a browser journey in which a client portal upload was delivered by the worker process to the selected site with an exact-version SHA-256 receipt. The P2b isolation matrix (client A/B, wrong tenant, guessed IDs, revoked identities, throttling, lost-response and failed-commit reconciliation) passes against the fake Graph drive, with the tenant-reachable cases also passing live. Remaining: production approval and independent review.

- **Staff ACLs & Mailbox Delivery:** Implement optional direct staff ACL behavior and Microsoft Graph mailbox delivery only with authorized tenant resources. Purview provider integration is excluded by root `AGENTS.md`; it is not an eligible local implementation task.



### 2.2 Audit & Accounting Parity Follow-ups (AS-PAR-002)

- **Route & Query Revocation Audit:** Sibling-client differential cases now cover staff list/queue/search routes (client- and engagement-scoped users), the client portal, consolidation group grants, sibling detail routes and the PBC byte endpoint, and closed four existence oracles. Remaining: Application command-level isolation (direct commands with out-of-scope identifiers) and export/count paths not rendered on those routes.

- **G16 Period-End Open-Item Methodology:** Formalize and record methodology approval for period-end open-item classification and carrying-amount evidence before enabling automated open-item remeasurement workflows.

- **Stale Content & Parameter Reauthorization:** Every parameterized staff and portal route now has an in-circuit route-change regression (assessment workbenches were the last added). Remaining: independent review of the whole set; no passive mid-circuit revocation push is claimed.



### 2.2a STE Audit Management specification alignment

Source: the owner-supplied STE Audit Management Tool specification and its requirement-by-requirement comparison
against `867061f` (55 traceability rows). Work proceeds in the comparison's seven gap-closure packages; a row is
closed only with a demonstration that uses real role identities and no direct status edits. Status here is the
work-tracking label, not an acceptance claim; evidence lives in `status.json`.

| Package | Rows | Status |
| --- | --- | --- |
| 1 Commercial calculation and document templates | 1.2-01, 1.2-02, 1.2-03, 1.3-03, 1.3-04, 4.1-05 | READY_FOR_REVIEW — versioned quotation from approved rate cards, complexity and risk premium; configurable approval matrix; one-click branded Quotation and Engagement Letter; 50/50 fee cycle with official receipt email and post-release balance invoice. Manual receipt attachment and the drag-and-drop portal rows remain (packages 2). |
| 2 Onboarding, continuance and commencement | 1.3-01, 1.3-02, 1.3-05, 1.3-T, 2.1-01..03, 2.3-01 | NOT_STARTED |
| 3 Resource planning and materiality | 2.2-01..03, 2.4-01..03 | NOT_STARTED |
| 4 End-user fieldwork connections | 3.1-01..04, 3.2-02..06 | NOT_STARTED |
| 5 Audit completion and final deliverables | 3.3-02..04, 3.4-01..02, 4.1-01..04 | NOT_STARTED |
| 6 Scheduled freeze and external enforcement | 4.2-01..03, OV-02 | NOT_STARTED (live SharePoint read-only enforcement stays `BLOCKED_EXTERNAL` until observed) |
| 7 Firm operations and technical library | OV-05, 4.3-01..03, 4.4-01..02 | NOT_STARTED |

### 2.3 Technical Debt & Bounded Hardening

- **Automated Documentation Health:** Complete: the Markdown health and filename validators plus the narrative volatile-metrics guard run in hosted CI (`ci.yml` "docs-health" job), keeping exact test counts, SHAs and run identifiers out of the top-of-authority narrative documents.

- **Benchmark Baselines:** The standard Domain suite includes a small accounting benchmark, provisional maximum-size trial-balance and PBC-transfer checks, and a 15-user/30-query local PostgreSQL sample. Exact input sizes, measurements and verification live in `status.json`. Continue with actual concurrent browser/session and provider workloads, archive/rendering throughput and production-like sizing before P8b acceptance.



---



## 3. External Gates (P1–P10)



These production milestones require live external infrastructure, tenant credentials, or human partner authorization. They **cannot** be closed by local mocks, simulation adapters, portal screenshots, or documentation claims:



| Phase | Gate / Objective | Status | Blocking Condition |

|---|---|---|---|

| **P1** | Live Entra OIDC Authentication | `BLOCKED_EXTERNAL` | Local administrator, scoped-staff and both client sign-ins are observed; an unsent Draft is hidden, Client X can see one synthetic Sent request, and Client Y is denied it. A changed-UPN Client Y sign-in also reached the same local client scope. Wrong-tenant, disabled/revoked-user and production acceptance remain. |

| **P2** | Selected-Resource SharePoint/Graph | `BLOCKED_EXTERNAL` | Development live evidence is complete for provisioning, verified bindings, a client portal upload delivered by the isolated Acceptance `pbc` worker process, exact-version SHA-256 receipts, probe reconciliation and no-overwrite retries. Production approval and independent review remain. |

| **P2b** | SharePoint Isolation & Reconciliation | `BLOCKED_EXTERNAL` | Automated matrix passes (client A/B, wrong-tenant token and connection, guessed item/intent/drive IDs, revoked client and administrator, 429 throttling, lost-response and failed-commit reconciliation). Live Development: client A/B separation, guessed IDs, local and Entra wrong-tenant refusal and the unrelated-site control passed. Throttling cannot be induced live; production approval and independent review remain. |

| **P3** | External Release Checkpoint Store | `BLOCKED_EXTERNAL` | Requires production cryptographic checkpoint infrastructure and immutable remote store. |

| **P4** | Purview Records Profile Integration | `OUT_OF_SCOPE` | Owner retained the product exclusion; provider issues #17 and #18 were closed as not planned. |

| **P5** | Document Signing Methodology Lineage | `OUT_OF_SCOPE` | Owner retained the product exclusion; provider issue #19 was closed as not planned. Exact uploaded signed-document evidence and human decisions remain in scope. |

| **P6** | Records / Archive Residual Hardening | `LOCAL_VERIFIED` | Immutable archive schema, lineage manifests, and structured exports are locally verified. |

| **P7** | Cross-Store Recovery (RPO/RTO) | `BLOCKED_EXTERNAL` | Requires custodially separate, multi-region cloud backup infrastructure and measured restore drills. |

| **P8** | Production Observability, Secrets & Capacity | `BLOCKED_EXTERNAL` | Local maximum-input and concurrent-query samples are recorded; production secret custody, collector/alerts, browser/provider load targets and sensitive log review remain. |

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
