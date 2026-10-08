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
| Secrets | .NET user secrets in development; approved secret store in production | `AGENTS.md` §8 | Production custody Open (P8) |

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
| JSON command body | `HttpBoundary:BodyLimits:MaxJsonBodyBytes` → `413` | `HttpBoundary` |

These are per-request bounds. None limits how many clients, engagements or working papers the firm may hold (specification §1.1).

## 4. Data integrity and correctness

| Requirement | Value | State |
| --- | --- | --- |
| Monetary precision | `numeric(19,6)`, `decimal`, banker's rounding | Enforced |
| Immutability | Append-only history via database triggers and check constraints | Enforced |
| Determinism | Pure calculators: no clock, network, EF Core or random IDs | Enforced by convention; no automated check |
| Fail-closed inputs | Missing FX rate, stale source, unapproved perimeter block approval | Enforced |
| Archive lock | Local read-only no later than 60 days after report signature; earlier by Partner lock | Enforced locally; SharePoint read-only `BLOCKED_EXTERNAL` (ADR-0006) |
| Materiality rounding | Within ±5 % of computed PM/TE/SAD; database check constraint | Enforced |

## 5. Availability, performance and recovery — Open

| Requirement | Current evidence | State |
| --- | --- | --- |
| Interactive latency (p95 per endpoint class) | No target recorded; a small local PostgreSQL concurrency sample exists in `status.json` | **Open (P8)** — owner to set targets |
| Concurrent users | Local 15-user sample only | **Open (P8)** |
| RPO / RTO | Local restore drill (`scripts/db/restore-drill.sh`, `docs/operations/auditsphere-operations-runbook-restore-drill.md`) | **Open (P7)** — custodially separate rehearsal required |
| Observability | Telemetry guide `docs/operations/auditsphere-operations-guide-telemetry.md`; correlation IDs on errors | Collector and alerts **Open (P8)** |
| Browser support, accessibility, locale | Not specified | **Open** — the Angular migration lists assistive-technology and locale gates as open |

## 6. Hosting constraints

Single modular monolith plus workers on one PostgreSQL; no Kubernetes, microservices or message brokers (`AGENTS.md` §5). Infrastructure as code under `infra/` is a compiled, not deployed, baseline.
