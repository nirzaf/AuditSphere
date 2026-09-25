# AuditSphereOps — Current State & Active Slice Handoff







**Status:** CURRENT



**Purpose:** Compact, authoritative handoff of the active implementation slice, recent verified changes, local environment state, and next actions.



**Authority:** Active execution handoff document. Volatile metrics (exact test counts, migration count, verified commit SHA, CI run IDs) belong exclusively to [`docs/execution/status.json`](status.json).



**Audience:** AI coding agents and human developers.







---







## 1. Active Implementation Scope







- **Active Work Package:** Audit workflow gap-closure, client accounting implementation, and repository-wide documentation & AI navigability refactoring.



- **Implementation Status:** `IN_PROGRESS`



- **Acceptance Status:** `LOCAL_VERIFIED`



- **Scope Boundary:** Import-first preparation, audit, and consolidation workspace (not an operational client ERP). Excludes client sales/purchase/inventory operations, payroll execution, and payment initiation. Purview and eSignature provider integrations are excluded from product scope: exact uploaded signed-document evidence, SHA-256 identities, human decisions, and release manifests are preserved without claiming provider acceptance.







---







## 2. Recent Completed Slices (Handoff Summary)







### 2.1 Documentation Standardization & AI Navigability Refactor



- **Standardized Filenames:** All non-root Markdown files standardized to `auditsphere-<area>-<document-type>-<subject>[-<id>][-<status>].md` with globally unique basenames. Root `README.md` and `AGENTS.md` preserved as conventional exceptions.



- **Capability-Focused Partials:** Services (`ConsolidationService`, `ClientAccountingService`, `AccountingAnalysisService`, `AuditFieldworkService`, `FinancialStatementService`), `AuditSphereDbContext`, and `ClientAccountingTests` decomposed into focused capability partial files with unchanged static APIs and test semantics.



- **Architectural Reference Guards:** `ArchitectureGuardTests.cs` enforces project reference layering (Domain references no outer project, Application references Domain only, Infrastructure references Application and Domain, never a host).



- **Documentation Policy & Automated Guards:** Established `docs/architecture/auditsphere-architecture-document-naming-policy.md`, `scripts/docs/validate-markdown-filenames.py`, and `MarkdownNamingGuardTests.cs`.



- **R2R Source Hashing Preserved:** `HISTORICAL_SOURCE` banners added and excluded from hash verification, preserving exact SHA-256 requirement hashes for R2R modules 20–26 and Audit workflow sources.







### 2.2 Re-Review Repairs & Currency Translation Completion (RR-01–RR-09, F01–F08)



- **Stable Reserve Line Identity (RR-01):** Derived cumulative translation reserve line IDs from immutable translation snapshots (`TranslationIdentities.CumulativeTranslationReserveLineId`), making replay, approval, and readback stable.



- **Fail-Closed FX Rates (RR-02, RR-03):** Removed missing-rate aggregate fallbacks; missing rate purposes return typed `gate.blocked` errors. Same functional and presentation currency translates as identity rate 1 without consuming observations.



- **Exact Line Rate Identity (RR-04):** Manifest rows carry their own line's consumed rate purpose and rate; classification resolves through recognized declared sections or approved mappings.



- **IAS 21 Rate Bridge (F01–F03):** `LineTranslationCalculator` computes translation reserve from equity and P&L rate differences with zero residual signed sum (`GOLD-R2R-07`), persisting `CTA_RESERVE` in consolidation balances.



- **Task Scope Reconciliation (RR-08, F06):** Reconciled T054 as R2R-only handover, moving audit handover `AS-AUD-028-AC10` to T075 with declared audit acceptance inputs.







---







## 3. Observed Local Verification State







All verifications are run against local PostgreSQL 18.6 on port 5433:







| Check | Expected Result | Authority Pointer |



|---|---|---|



| **Solution Build (Release)** | `0 Warning(s), 0 Error(s)` | `dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj --configuration Release` |



| **PostgreSQL Test Suites** | All tests pass, 0 skips (Domain, Api, E2E Playwright) | Consult [`docs/execution/status.json`](status.json) for authoritative counts |



| **EF Model Drift Check** | No pending model changes | `dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web` |



| **R2R Task Pack Status** | `valid: true`, 0 errors, 75 tasks | `python3 docs/task_breakdown/tools/task_status.py validate` |



| **Task Pack Self-Tests** | 21/21 passed | `python3 docs/task_breakdown/tools/test_task_status.py` |



| **Markdown Filename Guard** | 100% compliant, 0 collisions | `python3 scripts/docs/validate-markdown-filenames.py` |



| **Git Diff Cleanliness** | 0 whitespace or formatting errors | `git diff --check` |







---







## 4. Known Blockers & External Boundaries







The following external gates require live cloud infrastructure or partner sign-off and cannot be closed in local development:







- **P1 (Entra OIDC Authentication):** `BLOCKED_EXTERNAL` — requires live Azure AD tenant app registration and client secret mount.



- **P2 (Selected-Resource SharePoint/Graph):** `BLOCKED_EXTERNAL` — requires live Microsoft 365 tenant with selected-resource application scopes.



- **P3 (External Release Checkpoint Store):** `BLOCKED_EXTERNAL` — requires remote cryptographic immutable storage.



- **P7 (Cross-Store Recovery):** `BLOCKED_EXTERNAL` — requires multi-region cloud backup infrastructure and tested RPO/RTO.



- **P8 (Production Secrets & Observability):** `BLOCKED_EXTERNAL` — requires production Key Vault and OpenTelemetry collector.



- **P9 (Independent Merge Review):** `BLOCKED_EXTERNAL` — requires partner sign-off.



- **P10 (Real-Tenant Acceptance §47):** `BLOCKED_EXTERNAL` — requires customer-authorized tenant execution.







---







## 5. What the Next Agent Should Know







1. **Read Order:** Always read [`AGENTS.md`](../../AGENTS.md) and [`docs/auditsphere-docs-index.md`](../auditsphere-docs-index.md) before performing work.



2. **Architecture Authority:** [`docs/architecture/auditsphere-architecture-current-architecture.md`](../architecture/auditsphere-architecture-current-architecture.md) defines the implemented architecture. Do not introduce MediatR, microservices, or new layers.



3. **Capability File Map:** Consult [`docs/architecture/auditsphere-architecture-code-map.md`](../architecture/auditsphere-architecture-code-map.md) to locate relevant partial classes, domain records, and test fixtures.



4. **Volatile Facts Rule:** Never add live test counts, migration counts, or commit hashes to narrative documentation. Record them only in [`docs/execution/status.json`](status.json).



5. **Next Work Packages:** Consult [`docs/execution/auditsphere-execution-pending-tasks.md`](auditsphere-execution-pending-tasks.md) for open local work:



   - Continue the AC-01–AC-28 and AS-PAR-002 route/parameter revocation review.



   - Formalize G16 period-end open-item methodology approval.



   - Maintain documentation health validation.







---







## 6. Standard Verification Commands







```bash



# 1. Build solution in Release



dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj --no-restore --configuration Release







# 2. Check for EF Core model drift



dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web







# 3. Validate R2R task pack & pinned source hashes



python3 docs/task_breakdown/tools/task_status.py validate







# 4. Run task status self-tests



python3 docs/task_breakdown/tools/test_task_status.py







# 5. Validate markdown naming policy



python3 scripts/docs/validate-markdown-filenames.py







# 6. Run domain and architecture guard tests



dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --filter "FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~MarkdownNamingGuardTests"



```
