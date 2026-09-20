# AuditSphereOps — Pending Work Implementation Story

**Repository:** nirzaf/AuditSphere  
**Authoritative specification:** `AuditSphereOps_NET_Codex_Implementation_Specification.md` v5.0  
**Reviewed baseline:** master at `1184485797f359c89b5f9f82c264d05845dfe0ff`
**Current verified baseline:** 31 migrations, 165/165 PostgreSQL-backed tests, records/archive residual hardening (P6) locally verified

---

## 1. Objective

Complete AuditSphereOps from the current locally verified implementation into a production-ready, evidence-backed audit and accounting operations platform without weakening any control already implemented.

The remaining work is concentrated in:

- Real Microsoft Entra identity and SharePoint integration.
- Real Purview records behavior and records-owner evidence.
- Approved signing and release evidence.
- Independent recovery / production-readiness proof.
- Governance and full real-tenant acceptance.

> [!CAUTION]
> Missing external prerequisites must remain `BLOCKED_EXTERNAL`; mocks, placeholders, local adapters, or documentation examples must never be treated as live evidence.

---

## 2. Preserve the Existing Baseline

The following are already implemented and must remain green:

- .NET 10 modular-monolith solution
- ASP.NET Core + Blazor Interactive Server
- EF Core + PostgreSQL 18.6
- Scope-aware command authorization
- Firm/client/engagement isolation
- Durable operation/outbox model
- Retry, lease, idempotency and uncertain-result handling
- Trial-balance intake and source-reflection control
- Practice CRM
- Time and budget workflow
- Billing artifacts
- Bounded firm ledger
- Exact document snapshots
- Revision/generation-bound approvals
- Release-gate integrity
- Financial-statement mapping and deterministic artifact rendering
- PBC upload state machine and trusted local staging
- Audit planning/materiality/risk/population/workpaper/finding lifecycle
- Core entity catalog
- Client-evaluation and review question banks
- Staff route catalog
- Restricted client portal foundation
- Records profile/archive/legal-hold local model
- Repository binding model
- Fail-closed Graph provider boundaries
- Recovery quarantine and deployment-epoch fencing
- Migration-aware readiness
- PostgreSQL restore rehearsal
- GitHub CI build/migrate/test/readiness pipeline

---

## 3. Status Terminology

| Status | Meaning |
|---|---|
| `NOT_STARTED` | No implementation/evidence yet |
| `IN_PROGRESS` | Work has started |
| `LOCAL_VERIFIED` | Proven only with local/PostgreSQL/test adapters |
| `BLOCKED_EXTERNAL` | Requires tenant/professional/operations owner action |
| `LIVE_VERIFIED` | Proven against an approved live/non-production external service |
| `PRODUCTION_READY` | All technical, governance, and acceptance evidence complete |
| `NOT_APPLICABLE` | Only with explicit approved scope decision |

---

## 4. Dependency Order

```
P0  Repository truth & governance cleanup
 ↓
P1  Entra OIDC + approved runtime identity configuration
 ↓
P2  Live SharePoint/Graph document provider
 ↓
P3  External release checkpoint + capability evidence
 ↓
P4  Purview records profile + reviewer fixtures + behavior evidence
 ↓
P5  Approved signing methodology + signature lineage
 ↓
P6  Records/archive residual hardening
 ↓
P7  Cross-store recovery + production RPO/RTO
 ↓
P8  Production security/observability/capacity
 ↓
P9  Independent review + protected merge governance
 ↓
P10 Full §47 real-tenant acceptance cycle
```

---

## P0 — Repository Truth, Backlog and Governance Cleanup

**Status:** `IN_PROGRESS`

**User story:** As the technical lead, I want repository status, backlog and governance to reflect actual merged reality so future agents and reviewers work from current facts.

**Tasks:**
- [x] Update `README.md` to the latest verified test/migration baseline.
- [x] Reconcile `docs/execution/status.json` with current master.
- [x] Remove stale active branch/PR metadata for merged PR #11.
- [x] Reconcile `docs/execution/current-slice.md`.
- [x] Reconcile `docs/execution/implementation-checklist.md`.
- [x] Reconcile `docs/execution/pending-tasks.md` (this file).
- [ ] Create GitHub issues for the remaining packages (14 issues, 4 milestones).
- [ ] Prepare branch-protection/ruleset configuration for master.
- [ ] Require CI and independent review before production-readiness merges.

**Acceptance criteria:**
- README/status/current-slice/checklist/pending-task docs agree on current master.
- No merged PR is still shown as active.
- Every pending package has a GitHub issue, owner, and milestone.
- No credentials or reusable authorization secrets are stored in Git.
- Traceability is issue → branch → PR → test evidence → review → merge.

---

## P1 — Microsoft Entra OIDC and Runtime Identity

**Status:** `BLOCKED_EXTERNAL`  
**Milestone:** R1 — Tenant Integration  
**GitHub Issue:** #2

**User story:** As an authorized staff or client user, I want AuditSphereOps to authenticate through the approved Entra tenant and map my immutable Microsoft identity to the correct local account so live access uses the same fail-closed authorization model proven locally.

**Application tasks:**
- Complete real Entra OIDC configuration for the approved environment.
- Resolve identity from immutable tenant/object identifiers.
- Never use email/UPN alone as identity key.
- Enforce current local session epoch after authentication.
- Refuse disabled, stale, unmapped, and wrong-tenant identities.
- Preserve staff/client role boundaries.
- Keep simulation disabled in live environments.
- Add startup/readiness checks for identity configuration.
- Record only non-secret capability evidence.

**Required fixtures:** authorized staff, Client A, Client B, disabled/revoked, changed-UPN, wrong-tenant.

**Owner prerequisites:** approved app configuration, redirect URIs, authentication flow, secret/certificate reference in approved secret store, tenant/environment authorization, approved fixture identities.

**Acceptance criteria:**
- Staff sign-in maps to expected (tenant, object) identity.
- Client A cannot become Client B by changing request values.
- Changed UPN/email does not change stable identity.
- Reused email cannot inherit old subject identity.
- Disabled/revoked account fails closed per observed tenant behavior.
- Wrong tenant is denied.
- OIDC misconfiguration keeps protected surfaces locked.
- No demo actor is used in live mode.

**Done when:** OIDC is `LIVE_VERIFIED` with real tenant identities and command-level authorization remains enforced.

---

## P2 — Live SharePoint / Microsoft Graph Document Provider

**Status:** `BLOCKED_EXTERNAL` (depends on P1)  
**Milestone:** R1 — Tenant Integration  
**GitHub Issues:** #3, #5

**User story:** As an authorized engagement participant, I want PBC and engagement documents stored and retrieved from approved SharePoint repositories through bounded Graph permissions so SharePoint remains the canonical document store without tenant-wide access.

**Implementation tasks:**
- Replace fail-closed provider signatures only for explicitly approved environments with real support for: PBC provider sink, document upload, upload-session/chunk handling, metadata lookup, version enumeration, exact-version content retrieval, document snapshot retrieval, provider receipt capture, uncertain-outcome reconciliation, throttling/retry handling, selected-resource scope enforcement, repository-binding verification before every call.

**Security rules:**
- Never accept arbitrary Graph target IDs from browser input.
- Resolve tenant/site/drive/root from stored `RepositoryBinding`.
- Reject unobserved/unapproved bindings.
- Do not use `Sites.Read.All` / `Sites.ReadWrite.All`.
- Never log bearer tokens.

**Acceptance criteria (positive):** authorized staff uploads to Client A repository; Client A uploads only to assigned Client A request scope; provider metadata/version identity is persisted; timeout after external success does not duplicate the effect.

**Acceptance criteria (negative):** Client A→B denied; B→A denied; unrelated site denied; wrong tenant denied; guessed drive/item ID denied; missing binding denied; revoked capability denied.

**Done when:** Graph/SharePoint provider is `LIVE_VERIFIED` against approved non-production resources.

---

## P3 — External Release Checkpoint and Capability Evidence

**Status:** `BLOCKED_EXTERNAL` (depends on P1)  
**Milestone:** R1 — Tenant Integration  
**GitHub Issue:** #4

**User story:** As the release authority, I want final release to require independently observed external checkpoint evidence so a restored or stale application database cannot independently authorize delivery.

**Tasks:**
- Implement approved live `IReleaseCheckpointStore`.
- Keep local checkpoint store test-only.
- Bind checkpoint to firm, engagement, candidate, manifest digest, epoch, timestamp.
- Read back checkpoint before delivery.
- Persist request/result evidence.
- Hard-block release when checkpoint unavailable.
- Reconcile uncertain checkpoint writes.
- Add readiness/capability reporting.

**Acceptance criteria:** missing checkpoint blocks release; wrong manifest digest blocks release; old deployment/recovery epoch blocks release; duplicate request is idempotent; restored old DB cannot release against newer external state without reconciliation; external readback matches release identity exactly.

---

## P4 — Microsoft Purview Records Profile and Reviewer Fixtures

**Status:** `BLOCKED_EXTERNAL` (depends on P1, P2)  
**Milestone:** R2 — Records & Signing  
**GitHub Issues:** #6, #7

**User story:** As the records/compliance owner, I want an approved records profile and named reviewer roles exercised against synthetic SharePoint records so archive verification reflects observed Purview behavior rather than a requested label.

**Required fixtures:** records owner, disposition reviewer, ordinary editor, records custodian, privileged administrator.

**Required Purview configuration:** test-only records profile, approved retention/record label, target SharePoint location, retention trigger/duration, disposition behavior, reviewer assignment, audit enabled, residual-risk statement.

**Integration tasks:**
- Map profile version to local `RecordsProfile`.
- Request approved records action.
- Keep desired label separate from observed label.
- Capture actual protection state, observation timestamp/operator, provider reference.
- Never mark protection observed because request was submitted.
- Preserve legal-hold requested/applied/observed/released states.
- Refuse archive verification until observation requirements are satisfied.

**Acceptance criteria:** named records reviewer exists; approved profile exists; real label/protection state observed; audit evidence retained; legal hold blocks disposal; no production/unrelated content altered.

---

## P5 — Signing Methodology and Signature Lineage

**Status:** `BLOCKED_EXTERNAL` (depends on P1, P3, P4)  
**Milestone:** R2 — Records & Signing  
**GitHub Issue:** #8

**User story:** As an engagement partner and technical committee, I want the final signing method approved and bound to exact released artifacts so the signature proves who signed which exact version under which methodology.

**Professional prerequisites:** approved signing method, certificate/key custody, HSM/Key Vault or equivalent, authorized signatory rules, timestamping policy if applicable, rotation/revocation process.

**Application tasks:**
- Implement approved live signature provider.
- Bind signing request to exact report snapshot, FS snapshot, manifest digest, engagement/signatory/profile version/generations.
- Persist immutable signature lineage.
- Refuse stale artifact signing.
- Refuse release when required lineage missing/invalid.
- Preserve failure/retry evidence.

**Acceptance criteria:** signature applies to exact immutable bytes; content change invalidates old signing authority; unauthorized user cannot request signing; invalid/expired/revoked credential fails closed; signature lineage survives archive/export; no private key material stored in DB or Git.

---

## P6 — Records / Archive Residual Hardening

**Status:** `LOCAL_VERIFIED`  
**Milestone:** R2 — Records & Signing  
**GitHub Issue:** #9

**User story:** As the records custodian, I want archive revisions and disposition controls to remain immutable and reproducible across re-archive events so later actions cannot erase prior evidence.

**Tasks:**
- [x] Deterministic re-archive/version lineage.
- [x] No overwrite of prior manifest/export.
- [x] Predecessor/supersession references.
- [x] Digest-stable structured exports.
- [x] Disposition-package blocking.
- [x] Evidence-safe downgrade paths.
- [x] Repeated archive generation tests.
- [x] Legal-hold/disposition conflict tests.
- [x] Profile-version change tests.
- [x] Superseded records-action tests.

**Acceptance criteria:** re-archive creates new immutable version; old archive remains verifiable; disposition blocked under legal hold; historic profile version never silently changes; digest changes only when canonical content changes; downgrade refuses evidence loss.
- 31 migrations applied (latest: `20260919203959_ArchiveVersionLineage`).
- 165/165 PostgreSQL-backed tests green.
- Restore drill passed with 31 migrations.

---

## P7 — Cross-Store Recovery and Production RPO/RTO

**Status:** `BLOCKED_EXTERNAL` (depends on P3, P4)  
**Milestone:** R3 — Recovery & Production Readiness  
**GitHub Issue:** #10

**User story:** As the operations owner, I want restored database state reconciled with independently held external state before workers resume so recovery cannot replay completed external effects or release stale state.

**Required external setup:** primary application/database environment, custodially separate backup/checkpoint environment, named recovery custodians.

**Tasks:**
- Encrypted production-like DB backup.
- Independent checkpoint preservation.
- Isolated restore.
- Start in `RECOVERY_QUARANTINE`.
- Compare recovery/deployment epoch.
- Compare durable-operation status, external release checkpoints, provider receipts, archive/records state.
- Persist reconciliation findings.
- Require authorized restart.
- Advance recovery epoch.
- Prove stale worker cannot publish.
- Prove completed provider effects are not replayed.
- Measure RPO and RTO.
- Document recovery runbook.

**Acceptance criteria:** older DB + newer external state forces quarantine; exact reconciliation required before resume; stale worker fenced; no duplicate side effects; custodially separate storage used; measured RPO/RTO recorded; evidence human-reviewed.

---

## P8 — Production Security, Secrets, Observability and Capacity

**Status:** `BLOCKED_EXTERNAL` (depends on P1–P5)  
**Milestone:** R3 — Recovery & Production Readiness  
**GitHub Issues:** #11, #12

**User story:** As the operations/security owner, I want production configuration, secret storage, telemetry, and workload capacity explicitly verified so production safety does not depend on developer defaults.

**Secrets/key management:** approved secret store, Entra credential reference, Graph credential reference, signing key reference, Data Protection key-ring, rotation procedure, no secret values in source/logs.

**Observability:** structured logs, correlation IDs, durable-operation metrics, provider failure metrics, retry/quarantine metrics, readiness/liveness dashboards, security audit events, alert thresholds/runbooks.

**Capacity tests:** concurrent staff sessions, client sessions, PBC uploads, TB imports, package generation, worker throughput, Graph throttling, DB connection/lock pressure, archive generation.

**Acceptance criteria:** production startup refuses missing config; production startup refuses simulation adapters; health/capability probes green; workload results measured; alerts exercised in non-production; no PII/secrets in telemetry.

---

## P9 — Independent Review and Protected Merge Governance

**Status:** `BLOCKED_EXTERNAL`  
**Milestone:** R3 — Recovery & Production Readiness  
**GitHub Issue:** #13

**User story:** As the repository owner, I want independent review and protected merges for production-readiness work so owner-authored code is not treated as independent evidence.

**Tasks:**
- Designate independent reviewer.
- Protect master: require CI checks, require review approval, prevent force push/deletion as appropriate, require current-head review after material changes.
- Record reviewer/date/commit in execution ledger.
- Distinguish automated review from human review.
- Never count self-approval as independent review.

**Acceptance criteria:** current candidate has independent human review; valid findings resolved; non-applicable findings include rationale; CI runs on reviewed head; merge uses project authorization workflow; ledger identifies exact merged commit.

---

## P10 — Full §47 Real-Tenant Acceptance Cycle

**Status:** `BLOCKED_EXTERNAL` (depends on P1–P9)  
**Milestone:** R4 — Real-Tenant Acceptance  
**GitHub Issue:** #14

**Epic user story:** As the product owner and professional adoption authority, I want the complete approved client and recurring-client lifecycle executed with real authorized tenant resources so AuditSphereOps is accepted from observed end-to-end evidence rather than component tests alone.

**Required lifecycle:**
Lead → Opportunity → Proposal → Client conversion → Client acceptance → Engagement provisioning → Team assignment → Terms/commercial readiness → Client portal → PBC request → Controlled upload → TB import → Mapping → Adjustments → Financial-statement package → Accounting/management approval → Audit planning → Materiality → Risks/procedures → Populations/sampling → Workpapers → Evidence links → Findings → Review points → Revision-bound approvals → EQR when required → Final report/FS → Signature lineage → External release checkpoint → Delivery → Invoice/receipt/allocation → Archive manifest → Purview protection observation → Legal hold/disposition behavior → Recovery reconciliation → Next-period continuance.

**Final acceptance criteria:**
- All applicable §47 scenarios observed.
- No required scenario remains BLOCKED or NOT_RUN.
- Professional approvals come from named authorized roles.
- External capabilities have real observed evidence.
- Tenant/client isolation proven.
- Release and records controls proven.
- Restore/recovery proven.
- Independent reviewer signs evidence package.
- Product owner approves release profile.

---

## 5. Suggested GitHub Issue Breakdown

| Order | Suggested issue | Milestone |
|---|---|---|
| 1 | Reconcile repository execution ledgers and governance | R1 |
| 2 | Complete live Entra OIDC and identity fixtures | R1 |
| 3 | Implement live bounded Graph/SharePoint provider | R1 |
| 4 | Implement external release checkpoint store | R1 |
| 5 | Verify SharePoint isolation and provider reconciliation | R1 |
| 6 | Configure and integrate Purview records profile | R2 |
| 7 | Execute records reviewer/editor/custodian/admin matrix | R2 |
| 8 | Implement approved signature provider and lineage | R2 |
| 9 | Complete archive version/disposition hardening | R2 |
| 10 | Execute cross-store recovery rehearsal and measure RPO/RTO | R3 |
| 11 | Production secrets/Data Protection/observability | R3 |
| 12 | Production capacity and failure testing | R3 |
| 13 | Independent review and protected-merge evidence | R3 |
| 14 | Execute §47 real-tenant acceptance | R4 |

---

## 6. Standard Issue Workflow

For every executable issue:

1. Read `AGENTS.md`.
2. Read relevant spec sections.
3. Re-read current master and `docs/execution/status.json`.
4. Restate intent, scope, non-goals, dependencies, and acceptance criteria.
5. Create a dedicated branch.
6. Make the smallest coherent change.
7. Add only necessary tests.
8. Run focused tests.
9. Run full relevant PostgreSQL-backed tests.
10. Build with zero warnings.
11. Verify migrations if DB touched.
12. Update execution evidence with observed facts only.
13. Open PR.
14. Wait for CI and review.
15. Resolve valid findings.
16. Re-run validation on current head.
17. Merge only after the project's explicit authorization workflow.

---

## 7. Mandatory Verification Commands

After application changes:

```bash
dotnet tool restore
dotnet restore AuditSphereOps.slnx --locked-mode
dotnet build AuditSphereOps.slnx --no-restore
dotnet test AuditSphereOps.slnx --no-build
```

When schema changes:

```bash
dotnet ef database update \
  --project src/AuditSphereOps.Infrastructure \
  --startup-project src/AuditSphereOps.Web

dotnet ef migrations list \
  --project src/AuditSphereOps.Infrastructure \
  --startup-project src/AuditSphereOps.Web

scripts/db/restore-drill.sh
```

For runtime changes verify:

```
/health/live
/health/ready
relevant staff route(s)
relevant client route(s)
```

Tenant/provider tests must return `BLOCKED` when prerequisites are missing rather than silently skipping.

---

## 8. Non-Goals

Do not introduce:

- Frappe / ERPNext
- Python backend
- React frontend
- Second ERP
- Microservice-per-module design
- Kafka/message broker
- Kubernetes as a prerequisite
- Autonomous AI audit conclusions
- Tenant-wide Graph scopes
- A competing user-facing document repository
- Fake Purview behavior
- Fake signing evidence
- Production secrets in source control
- Automatic disposal without approved records policy

---

## 9. Production Readiness Exit Checklist

### Identity/access
- [ ] Entra OIDC live verified
- [ ] Immutable subject mapping verified
- [ ] Disabled identity behavior verified
- [ ] Wrong-tenant denial verified
- [ ] Client isolation verified

### Documents
- [ ] Selected-site Graph provider verified
- [ ] Exact provider versions verified
- [ ] Upload reconciliation verified
- [ ] Unrelated-site denial verified
- [ ] Snapshot retrieval verified

### Professional release
- [ ] Current approval chain verified
- [ ] EQR verified where required
- [ ] Signing method approved and verified
- [ ] Signature lineage verified
- [ ] External checkpoint verified
- [ ] Final delivery verified

### Records
- [ ] Approved records profile
- [ ] Purview behavior observed
- [ ] Records reviewer named
- [ ] Editor/custodian/admin matrix executed
- [ ] Legal hold observed
- [ ] Archive verified
- [ ] Disposition behavior proven

### Operations
- [ ] Production secret store
- [ ] Data Protection key-ring
- [ ] Observability
- [ ] Alerts
- [ ] Capacity test
- [ ] Cross-store restore
- [ ] RPO measured
- [ ] RTO measured
- [ ] Stale-worker fencing verified

### Governance
- [ ] Independent human review
- [ ] Branch protection
- [ ] Required CI checks
- [ ] Execution ledger current
- [ ] No mandatory blockers
- [ ] §47 evidence complete
- [ ] Product-owner approval
- [ ] Professional-adoption approval

---

## 10. Final Completion Statement

AuditSphereOps may be described as **fully production-ready** only when:

> The complete approved professional lifecycle has been executed against authorized real tenant resources; identity and client isolation, exact document lineage, version-bound approvals, signing, release, records enforcement, recovery reconciliation, operational readiness, independent review, and the full §47 acceptance evidence are complete for the exact release candidate.

Until then, the correct status is:

> **Locally implementation-complete for the verified scope; production-gated by external integration, professional approval, recovery, governance, and acceptance evidence.**
