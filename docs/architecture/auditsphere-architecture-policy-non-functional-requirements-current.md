# AuditSphere — Non-Functional Requirements

**Status: CURRENT** for every limit marked *Enforced* (the value is in code at the cited location). Rows marked *Open* have no agreed target yet; do not invent one in a feature task — raise it.

Change a limit only in its source constant or configuration key, and update this table in the same change.

## 1. Security and isolation

| Requirement | Value | Source | State |
| --- | --- | --- | --- |
| Business authorization | Explicit `RoleGrant` scope in every Application command, query and queue; HTTP layer only checks for an authenticated session | `Application/Security/`, `Api/HttpBoundary/HttpPolicies.cs` | Enforced |
| Session revocation | Session epoch rechecked by every protected read and command; protected shells re-verify periodically and clear content on failure | `docs/execution/auditsphere-execution-pending-tasks.md` §2.2 | Enforced (periodic, not push) |
| Browser credentials | No bearer tokens in the browser; same-origin cookie session; antiforgery via XSRF cookie | `docs/architecture/auditsphere-angular-conventions-current.md` | Enforced |
| Security headers | `nosniff`, `no-referrer`, `X-Frame-Options: DENY`, strict CSP with hashed inline scripts | `Api/HttpBoundary/SecurityHeadersMiddleware` | Enforced |
| Error bodies | `{ code, message, correlationId }`; never SQL, stack traces, tokens or internal paths | `Api/HttpBoundary/ApiError.cs` | Enforced |
| Microsoft permissions | One app identity per capability, exactly one role, off by default, consent-verified | `docs/architecture/auditsphere-m365-tenant-administration-permissions.md` | Enforced locally; live `BLOCKED_EXTERNAL` |
| Secrets | .NET user secrets in development; production custody requires P8 approval/evidence; never log or commit secrets | [API development boundary](auditsphere-angular-migration-current.md#api-and-angular-project-boundary); [P8 custody gate](../execution/auditsphere-execution-pending-tasks.md); `AGENTS.md` §6 (no logging/committing secrets) | Production custody Open (P8); no production secret store is selected here |

## 2. Request rate limits (per authenticated identity, configurable under `HttpBoundary:RateLimit`)

| Class | Budget | Applies to |
| --- | --- | --- |
| `NormalRead` | 240 / min | GET/HEAD under `/api` |
| `Command` | 60 / min | State-changing methods |
| `Search` | 30 / min | `/api/ui/search` |
| `Export` | 20 / min, 4 concurrent | CSV, financial-package and deliverable downloads |
| `FileUpload` | 120 / min, 4 concurrent | GL CSV upload, PBC chunk stream |
| `Authentication` | 60 / min (IP-partitioned) | `/auth/sign-in` |
| `M365Administration` | 30 / min | Microsoft administration and consent |

Source: `src/AuditSphereOps.Api/HttpBoundary/ApiRateLimiter.cs`. Rejections return `429` with `Retry-After`; clients never retry commands automatically.

## 3. Size and volume bounds (per request or per operation)

| Item | Bound | Source |
| --- | --- | --- |
| Trial balance upload | 25 MB | `UiEndpoints.Intake.cs` `MaxTrialBalanceUploadBytes` |
| Trial balance XLSX uncompressed | 100,000,000 bytes | `TrialBalanceXlsxImporter.MaxUncompressedBytes` |
| Trial balance export | 50,000 rows | `TrialBalanceDatasetQuery.MaxExportRows` |
| General ledger lines per intake | 500,000 | `ClientAccountingService.GeneralLedger.cs` `MaxGlLines` |
| PBC upload | 250 MB per file, 8 MB chunks | `PbcService.MaxUploadBytes`, `MaxChunkBytes` |
| Signed representation letter | 10 MB readable unencrypted PDF | `AuditDeliverableService.Representations.cs` `MaxSignedLetterBytes` |
| Signature / seal PNG | 512 KB | `AuditDeliverableService.MaxSignatureBytes` |
| Five-part bundle | 100 MB total | `AuditDeliverableService.Bundle.cs` |
| Firm expense evidence | 5 MB | `FirmOperationsServices.MaxEvidenceBytes` |
| Portfolio CSV export | 1,000 clients (more is refused with typed `400`) | `PortfolioQuery.Workspace.cs` `ExportClientLimit` |
| Workflow task evidence references | Eight distinct source references per task, including withdrawn references; changes append history | `WorkflowTaskEvidenceQuery.MaximumRules`, `WorkflowEvidenceRuleGuardSql` |
| Workflow ordering amendment graph | At most 500 workflow tasks and 4,000 effective edges per engagement; evaluate the complete graph or refuse the change | `WorkflowTaskDependencyQuery.MaximumTasks` / `MaximumEdges`, `WorkflowDependencyRevisionGuardSql` |
| JSON command body | `HttpBoundary:BodyLimits:MaxJsonBodyBytes` → `413` | `HttpBoundary` |

These are request or aggregate bounds. None limits how many clients, engagements or working papers the firm may hold (specification §1.1).

## 4. Data integrity and correctness

| Requirement | Value | State |
| --- | --- | --- |
| Monetary precision | `numeric(19,6)`, `decimal`, banker's rounding | Enforced |
| Immutability | Append-only history via database triggers and check constraints | Enforced |
| Determinism | Pure calculators: no clock, network, EF Core or random IDs | Enforced by convention; no automated check |
| Fail-closed inputs | Missing FX rate, stale source, unapproved perimeter block approval | Enforced |
| Archive lock | Local read-only no later than 60 days after report signature; earlier by Partner lock. Issued bytes and manifests belong to the authoritative release/archive store; SharePoint is a secondary copy. | Local controls implemented; ADR-0009's separate read-only library design is approved. Provider protection remains `BLOCKED_EXTERNAL` until exact hashes, reconciled grants and staff-denial evidence are observed; never claim write-once or administrator-proof immutability. |
| Materiality rounding | Within ±5 % of computed PM/TE/SAD; database check constraint | Enforced |

## 5. Availability, performance and recovery — Open

| Requirement | Current evidence | State |
| --- | --- | --- |
| Interactive latency (p95 per endpoint class) | No target recorded; historical application-query sample is in `docs/execution/status-archive-2026-10.json` under `keys.verification.p8bProvisionalStaffQueryBaseline`, not an HTTP p95 measurement | **Open (P8)** — operations owner to set targets |
| Concurrent users | Bounded historical application-query sample only; no accepted browser/session workload or production capacity target | **Open (P8)** — operations owner to define workload and acceptance targets |
| RPO / RTO | Local restore drill (`scripts/db/restore-drill.sh`, `docs/operations/auditsphere-operations-runbook-restore-drill.md`) | **Open (P7)** — custodially separate rehearsal required |
| Observability | Telemetry guide `docs/operations/auditsphere-operations-guide-telemetry.md`; correlation IDs on errors | Collector and alerts **Open (P8)** |
| Browser support, accessibility, locale | Angular conventions require WCAG 2.2 AA text/control contrast and reduced-motion handling; automated checks and migration evidence are bounded | **Open acceptance** — supported browser/locale matrix and independent assistive-technology walkthrough remain required; existing conventions are not blanket accessibility certification |

## 6. Hosting constraints

Single modular monolith plus workers on one PostgreSQL; no Kubernetes, microservices or message brokers (`AGENTS.md` §6). `infra/` contains operator configuration; deployed environments and their actual verification belong in `status.json` and the canonical operator Wiki. Configuration presence does not prove deployment or production acceptance.

Production p95, recovery, capacity and residency targets need the operations/records owners' recorded workload, environment and acceptance rule. Existing request bounds remain the engineering default; they are not service-level targets or professional methodology. No new limit, provider, secret-store choice or deployment is authorized by this reconciliation. See the [reconciliation report](../execution/auditsphere-execution-report-documentation-reconciliation-current.md) for owners and evidence exits.
