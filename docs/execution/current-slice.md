# AuditSphereOps — current state and active slice

The build contract is `AuditSphereOps_NET_Codex_Implementation_Specification.md` (v5.0) at repo root. The v5 specification already requires .NET 10 (SDK 10.0.300) and PostgreSQL 18; an earlier note claiming .NET 10 was a deviation was incorrect. The local server is now PostgreSQL 18.6, matching the spec's required major version.

## P0 — Repository Truth, Backlog and Governance Cleanup (active, master @ `b34447b`)

**Base:** `b34447ba2d87e81ed865081b7dedb1415e5efdce` (merge of PR #11, 2026-09-19)  
**Tests:** 161/161 passing, 0 skipped, PostgreSQL 18.6  
**Migrations:** 30 (latest: `20260919134442_RecordsActionEvidence`)

This slice makes no code or schema changes. It reconciles all execution-ledger documents with the current merged `master` reality, creates GitHub milestones R1–R4 and 14 dependency-ordered issues for P1–P10 remaining work, and prepares the branch-protection configuration for owner action.

Acceptance criteria for P0:
- README, status.json, current-slice.md, implementation-checklist.md, and pending-tasks.md all agree on `b34447b` baseline.
- No merged PR is shown as active.
- Every pending package has a GitHub issue, owner, and milestone.
- No credentials or secrets are stored in Git.

## P6 — Records / Archive Residual Hardening (locally verified, 2026-09-19)

**Base:** `b34447ba2d87e81ed865081b7dedb1415e5efdce`  
**Tests:** 165/165 passing, 0 skipped, PostgreSQL 18.6  
**Migrations:** 31 (latest: `20260919203959_ArchiveVersionLineage`)

This slice closes residual records/archive hardening gaps:
- Migration `20260919203959_ArchiveVersionLineage` adds `predecessor_manifest_id` and `superseded_by_manifest_id` self-referential foreign keys to `archive_manifests` with `ON DELETE RESTRICT`, plus check constraint `ck_archive_manifests_lineage_no_self_ref`.
- `RecordsArchiveService.BuildManifestAsync`: chains back to predecessor manifest on re-archive (version > 1) and marks prior manifest superseded (`superseded_by_manifest_id`) within the same transaction.
- `RecordsArchiveService.ReviewManifestAsync`: rejects reviewing superseded manifests (`ErrorCodes.ProtectedState`).
- `RecordsArchiveService.VerifyArchiveAsync`: blocks archive verification when an active (`OBSERVED`, not `RELEASED`) legal hold exists (`ErrorCodes.GateBlocked`).
- `RecordsArchiveService.ReleaseLegalHoldAsync`: enables releasing an active legal hold.
- Evidence-safe downgrade: `Down` method in migration throws exception (`ERRCODE = '55000'`) refusing downgrade if any version lineage rows exist.
- 4 new PostgreSQL-backed tests in `RecordsArchiveTests`:
  1. `ReArchive_ProducesSeparateVersionWithPredecessorLink`
  2. `VerifyArchive_BlockedByActiveLegalHold`
  3. `BuildManifest_RequiresCurrentApprovedProfile`
  4. `ReArchive_DigestStableForIdenticalContent`
- Full test suite: 165/165 passed on PostgreSQL 18.6 with 0 skipped.
- Restore drill: `scripts/db/restore-drill.sh` passed with 31 migrations.

## Verified locally (as of 2026-09-19)

- SDK 10.0.300; six projects targeting net10.0 (five application projects, one test project).
- EF Core 10.0.12, Npgsql EF provider 10.0.0, `dotnet-ef` tool 10.0.12.
- PostgreSQL 18.6 development cluster at `/opt/homebrew/var/postgresql@18`, loopback + trust auth (development only), port 5433. The local `auditsphere` and `auditsphere_tests` databases were created for this host.
- Thirty-one applied migrations on `auditsphere`, ending at `20260919203959_ArchiveVersionLineage`; `dotnet ef database update` reports no pending migrations. The latest slices add the persisted §25.3 structured export, append-only records-action evidence, the scoped repository-binding/sync/capability model, recovery-session quarantine state, and archive version lineage.
- Build: zero warnings/errors across all 6 projects. Full suite: 165/165 passed, zero skipped locally on PostgreSQL 18.6. New coverage includes archive version lineage with predecessor/supersession links, legal-hold disposition blocking, approved profile version requirement, and manifest digest stability.
- UI Ground Truth: `Release.razor` removed the caller confirmation checkbox and now renders truthful gate statuses (`absent` / `pending` / `verified` / `mismatched` / `expired`) for external checkpoints, Microsoft Purview protection attestations, and signature lineages, with an explicit notice that no live Microsoft records action is observed.
- Restore rehearsal: `scripts/db/restore-drill.sh` dumped and restored `auditsphere` into a generated temporary loopback database, compared migration history and release-checkpoint database summaries, wrote `docs/evidence/restore-drill-latest.json`, then cleaned its generated database and temporary files. The evidence marks external custody and production RPO/RTO as not run.
- Migration-aware readiness: `/health/ready` returned `Healthy`/HTTP 200 after querying `__EFMigrationsHistory`; `/health/live` returned HTTP 200.


## Run verification

On macOS:
```bash
pg_ctl -D /opt/homebrew/var/postgresql@18 -o "-p 5433" start
dotnet tool restore
dotnet build AuditSphereOps.slnx
dotnet test AuditSphereOps.slnx
./scripts/db/restore-drill.sh
```

On Windows:
```powershell
Set-Location 'C:\Users\DELL\repos\AuditSphere'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\db\status.ps1
& 'C:\Users\DELL\.dotnet\dotnet.exe' tool restore
& 'C:\Users\DELL\.dotnet\dotnet.exe' build AuditSphereOps.slnx
& 'C:\Users\DELL\.dotnet\dotnet.exe' test AuditSphereOps.slnx
./scripts/db/restore-drill.sh
```

The database test requires PostgreSQL on 127.0.0.1:5433 and database `auditsphere_tests`; supply `AUDITSPHERE_TEST_CONNECTION` if credentials differ. It refuses other database/host names, creates a random schema, migrates it, and removes only that schema. A missing or wrong-version server fails the test — never an InMemory fallback.

The restore rehearsal is development-only and loopback-only; it does not establish production RPO/RTO or restore any external Microsoft records/checkpoint store.

## Current local completion boundary

Repository bindings now scope document references to an authorized tenant/site/drive/root binding. `GraphPbcProviderSink` and `GraphReleaseCheckpointStore` are signature-only fail-closed boundaries. Web and worker startup refuse external effects without a complete approved identity/connection/epoch composition. Recovery sessions persist quarantine, external epoch, reconciliation scope and reviewed findings before an authorized restart advances the deployment epoch. Records archives persist the complete local §25.3 structured payload and append-only requested/observed records-action evidence.

The EasyGuide tenant evidence is limited to the observed `Sites.Selected` app permission and selected-site `write` grant. Purview was inspected as `qts@easyguide.onmicrosoft.com`; both Information Protection sensitivity labels and Data Lifecycle Management retention labels showed no data, and the creation draft was discarded. No Purview production profile/label behavior, professional signing approval, independent human review, or production RPO/RTO acceptance is claimed.

## Durable outbox and validation worker

Checklist #3 is implemented, locally verified, committed as `cc6384c` and pushed to `origin/master` (private `nirzaf/AuditSphere`). The existing incomplete worker refactor was completed; `Worker` delegates to Application services through the Infrastructure persistence implementation. Pending raw datasets are discovered in batches of 25 and enqueued with a deterministic dataset/revision key. `ValidateTrialBalance.v1` is the only shipped handler: its validation result, audit event, and operation completion commit together. `Accepted` means basic validation, never professional approval or release readiness.

Startup requires `ConnectionStrings__AuditSphere`, `Worker__FirmId`, a Development/Test environment, and `ExternalEffects__Enabled=false`. `Worker__DeploymentEpoch` defaults to 1 and must match the firm's local safety state. Firm/client guards must exist; the migration provisions them for existing clients, and fixture creation provisions them atomically with new test clients. No worker creates missing guards opportunistically. No live provider is registered; test-only simulation handlers require Test plus explicit simulation enablement.

Claiming uses a short PostgreSQL `SKIP LOCKED` transaction. Defaults are a 60-second lease, renewal every 20 seconds, five execution attempts, and persisted exponential retry starting at 5 seconds, capped at 5 minutes before honoring any longer provider delay. Owner/token/state/unexpired-lease checks fence every publication; a new database-time check after waiting for a row lock prevents reviving an expired lease. Firm → client → dataset → operation is the local publication lock order.

Expired pre-effect claims can retry; started/unknown outcomes become `RESULT_UNCERTAIN`. A separately leased reconciliation reads the same deterministic target and verifies its digest. Missing/mismatched evidence blocks completion. Cancellation after an effect begins preserves uncertainty; a local token is not an exactly-once provider guarantee. Blocked/dead-letter operations remain visible in persistence and are not automatically reset; an authorized operator recovery UI is not part of this slice.

### Persistence dictionary for this slice

- `durable_operations`: UUID operation/firm/target/correlation IDs; optional client/engagement/originator IDs; immutable kind/schema/group/mode/authority, target revision (`bigint`), key (`varchar(200)`), bounded typed JSON, versioned length-prefixed request bytes (`bytea`) and SHA-256 hex digest (`varchar(64)`). This encoding is not the future approval-manifest canonicalization format.
- Mutable operation projection: explicit string state; monotonic `attempt_token` (`bigint`) and `attempt_count` (`integer`); UTC next-attempt/lease/completion times; nullable lease owner/result identity/result digest/error code; claimed epoch and reconciliation flag. Completed results and request identity cannot be rewritten.
- Named uniqueness: `ux_operation_firm_key`; engagement FK includes firm/client/engagement, with restrictive deletes. CHECK constraints enforce state, counters, scope, lease shape, and required completion evidence. Indexes support firm/group/state/due-time claims.
- `operation_attempts`: append-only UUID, operation FK, unique operation/token, owner, reconciliation flag, and UTC claim time. `operation_events`: append-only UUID, operation FK/token, stage/action kind, executor, UTC timestamp; originating actor and correlation remain on the immutable request. No credentials or provider exception text are recorded.
- `firm_safety_states`: firm UUID, `LOCAL_ONLY` or `RECOVERY_QUARANTINE`, positive deployment epoch. `client_safety_states`: client UUID, matching firm/client FK, positive input generation. These are coordination records, not completed human authorization or release-gate implementations.
- Request/attempt/event evidence has no automatic expiry/deletion path. Runtime SQL paths are confined to the operation store and typed validation handler; dedicated least-privilege database-role provisioning remains future hardening.

### Migration and recovery boundary

`DurableOutbox` upgrades only an empty legacy operation table; nonempty legacy history stops the migration before modification. Tests cover both previous-schema accounting preservation and this refusal. Downgrade refuses to discard operation evidence; a populated installation needs coordinated recovery, not an old binary against the new schema. Existing monetary precision and accounting migrations are unchanged. Apply migrations explicitly; web startup never applies them.

## Database scripts

`scripts/db/pg-common.ps1` (shared settings + helpers), `status.ps1`, `start.ps1` (idempotent), `stop.ps1`, `init-cluster.ps1`. They refuse a wrong major version, pass SQL through a temp file (PowerShell 5.1 strips quoted identifiers from native arguments), and require `-ExecutionPolicy Bypass` per invocation on this workstation.

## Checklist #4 — command-enforced authorization (done locally, commit `16f10f4`)

`AuthorizationDecision.AuthorizeAsync` is the single inside-command decision: active user, firm scope, stored client/engagement linkage, covering assignment, required roles, client internal-only denial, stale/disabled sessions, and hold/professional-work gates. `TrialBalanceDatasetQuery.GetDatasetAsync` takes only a dataset ID, resolves scope from the stored row, and returns a DTO — never a tracked entity. Synthetic two-firm/two-client fixtures prove cross-firm/client/engagement denial, disabled/stale denial, role gating, hold gating, and nondisclosing guessed-ID failure. New `AuthorizationIntegrity` constraints (unique identity binding, scoped grant/hold FKs, check constraints) are covered by refusal tests. This is local synthetic proof only: no Entra OIDC, no SharePoint grants, no tenant evidence.

## Checklist #5 — TB import, AJ post, source bridge (done locally, commit `995990e`)

CSV intake parses Appendix D to 14 rows with D.2 control totals (1,820,000 each side, signed zero); mixed-currency, duplicate-account, >6dp, formula, and oversized inputs are rejected. The import command authorizes assignment, stamps the source hash, assigns per-engagement revisions, and reuses — never duplicates — identical bytes. AJ-001 drafts as preparer, posts as a different reviewer (self-post denied, double-post denied, unbalanced denied), and posted lines stay frozen. The reviewer bridge records NOT_REFLECTED for TB-1 (plan applies once → profit 175,000, PPE 95,000) and evidence-backed REFLECTED for replacement TB-2 (plan applies zero → profit stays 175,000, not 170,000). UNKNOWN/PARTIALLY and post-plan reflection changes block finalization instead of guessing. XLSX, mapping/FS packages, and ledger posting remain future work.

## Checklist #6 — practice CRM (implemented and merged)

The merged checklist #6 implementation provides guarded commercial commands for lead qualification, opportunity progression, proposal revision/approval/send/response, idempotent conversion to a canonical `PracticeClient` draft, and scoped client-contact creation. Lead creation and every CRM state transition use the firm safety row in a local transaction. Conversion creates the client safety guard, increments an existing client's guarded generation, binds the pending `AcceptanceDecision` to that generation, and never creates or activates an engagement. Contact changes increment the guarded client generation and primary-contact changes serialize under that guard. Sent proposal versions remain immutable; stale revisions, invalid transitions, missing required scope, conflicting identity candidates, unavailable guards, and cross-firm actors fail closed. Migrations `20260918093347_PracticeCrmWorkflow` and `20260918095500_CrmSafetyInvariants` are applied only to the local development database; owner links are firm-scoped and database checks enforce bounded dates, currency, amounts, probabilities, and contact validity.

## Checklist #7 — time and budget approval (implemented and merged, commit `0a34400`)

`PracticeTimeService` implements guarded task creation/assignment/reassignment/completion, integer-minute time drafts with bounded workday and overlap rules, submit/approve segregation, controlled correction revisions, immutable rate snapshots, versioned rate cards, versioned engagement budgets, stale revision checks, and budget-versus-approved-time actuals. Approved time is never edited: correction marks the prior row `SUPERSEDED` and creates a new draft revision with the original rate snapshot.

`20260918111815_PracticeTimeBudgetWorkflow` adds scoped keys/FKs/checks and migrates legacy task/time shape without silently discarding data. Legacy budget rows are retained as `legacy_budget_versions`; legacy hours must be positive and minute-aligned, orphan rows stop the migration, and legacy time remains explicitly non-billable with no invented currency. Applied to local `auditsphere`; no production database was touched.

## Checklist #8 — billing artifacts (implemented and merged, commit `88e3409`, PR #3 → `3bcdaa7`)

`BillingService` implements scoped finance-role commands for billing accounts, invoice draft/review/approval/post/send transitions, receipt recording/allocation, credit notes, and invoice balance queries. Invoice totals are calculated from exact decimal line inputs; credit notes and receipt allocations never mutate the invoice, and concurrent source consumption is fenced by a unique source identity. Posting requires an approved owner finance profile in the account currency; this slice does not create firm-ledger journals or call a delivery provider.

`20260918115026_BillingArtifactWorkflow` preserves the existing allocation table and refuses ambiguous legacy invoice/receipt/allocation rows before adding composite scope FKs, currency/state/amount checks, and append-only triggers for posted billing history. Applied to local `auditsphere`; no production database or delivery provider was touched.

## Checklist #9 — bounded firm ledger (implemented and merged, commit `ea40814`, PR #4 → `c01f3d3`)

`LedgerService` implements finance-role account/period creation, journal draft → review → approval → posting, exact decimal balance checks, source/retry uniqueness, reversal postings, controlled period close/reopen decisions, and posting-account gates. Posting locks the firm guard then the fiscal-period row; close uses the same order and period row, so a close cannot commit around a posting. Posted journals, journal lines, posting headers/lines, source links, receipts, and close decisions are append-only at the database boundary.

`20260918121512_FirmLedgerWorkflow` adds the eleven scoped ledger tables/keys and legacy preflight. It refuses non-empty legacy accounts/journal history where the old schema cannot prove account type or posting authority, normalizes only unambiguous period state, and installs a deferred constraint trigger that rejects a committed posting with fewer than two balanced lines. Four ledger workflow tests plus the legacy migration refusal pass; applied only to local `auditsphere`.

## Checklist #10 — document snapshots (implemented and merged, commit `0f9fe20`, PR #5 → `c091bf2`)

`DocumentSnapshotService` resolves the stored document scope, requires an internal authorized actor, computes SHA-256 from the supplied bytes rather than trusting a caller hash, records exact byte count/source identity, and rejects a duplicate document/provider-version identity. `20260918125731_DocumentSnapshotIntegrity` adds composite scope FKs, bounded version/hash/byte checks, a unique `(firm, document, version)` identity, legacy preflight that refuses ambiguous scope or hash history, and a database append-only trigger for snapshots. Three snapshot tests plus the migration tests pass; applied only to local `auditsphere`. This slice does not retrieve or store bytes through a live Microsoft provider.

## Checklist #11 — revision/generation-bound approvals (implemented and merged, commit `94a8da3`, PR #6 → `87e2523`)

`ApprovalService` resolves the stored workpaper scope, authorizes a reviewer, locks firm/client/target rows in order, and binds a decision to the target revision, client input generation, firm policy generation and manifest digest. Historical `Approval` rows are append-only; `ApprovalApplicability` is a separate projection. Re-evaluation reads current values after the locks, marks changed revision/input/policy as `STALE`, and `RequireCurrentAsync` fails closed. `20260918131715_ApprovalApplicabilityWorkflow` adds policy-generation storage, scoped approval FKs/checks, unique approval identity, refusal of ambiguous legacy approvals, and the immutable approval trigger. Two approval tests plus migration safety pass; applied only to local `auditsphere`. This slice supports stored workpaper targets only and does not complete release-gate or UI flows.

## Checklist #12 — release-gate integrity (implemented and merged, commit `3e3c82a`, PR #7 → `2dd97f9`)

`ReleaseService` creates a stored-workpaper candidate only from a current approval and issues one immutable release event only after rechecking the firm/client guards, professional-work holds, target revision, client input generation, firm policy generation, approval applicability, exact manifest digest, and verified external checkpoint. A firm-scoped authorized release key makes retries idempotent; issuance creates the exact-package delivery outbox intent in the same local transaction and moves the candidate to `ISSUED`. `20260918133707_ReleaseGateWorkflow` adds scoped candidate/release FKs/checks, legacy-release migration refusal, and the append-only release trigger. Two release tests plus migration safety pass; full suite 91/91, 0 skipped; local `auditsphere` has fourteen migrations applied. This slice is provider-free: signing/protection evidence, external checkpoint storage, live delivery and the remaining UI/recovery work stay blocked or future work.

## Checklist #13 — initial Blazor shell (implemented and merged, commit `c127448`, PR #8 → `ff57783`)

The web host now maps a Blazor Interactive Server shell with an accessible layout, `/app` live portfolio metrics and release-candidate table, and `/app/releases/{id}` invoking the real `ReleaseService.IssueAsync` command. `CurrentActorResolver` binds a trusted Entra subject and tenant claim to the stored user, session epoch and firm-scoped active grants; no query-string actor or scope is accepted. OIDC is opt-in configuration; when it is unset, the staff surface remains locked. HTTP smoke evidence: `/`, `/health/live`, `/health/ready`, and `/app` each returned 200; `/app` rendered the sign-in-required state. This is an initial shell, not the complete route catalog, portal, upload transport or operator recovery UI.

## Checklist #14 — CI, restore, migration-aware readiness and bounded PBC transport (implemented locally, clean implementation commit `6e31ab5`)

The web readiness check now verifies both PostgreSQL reachability and an empty `GetPendingMigrationsAsync()` result. `.github/workflows/ci.yml` provisions PostgreSQL 18, restores/builds/tests the solution, applies migrations to the disposable CI database and probes readiness. `Directory.Build.props` enables committed NuGet lock files and setup-dotnet uses `**/packages.lock.json` for caching, fixing the missing-lock-file failure; CI uses normal restore because the web project’s implicit framework-pack asset version differs between the local SDK installation and the hosted runner, while local `dotnet restore --locked-mode` passes. `scripts/db/restore-drill.sh` is constrained to loopback PostgreSQL 18 tooling, compares restored migration evidence with the source, and removes only generated artifacts. The PBC follow-up adds scoped request states, bounded upload intents, sequential immutable chunk receipts, reviewer-gated completion, and client/staff routes. The current transport slice adds a same-origin browser endpoint: sequential 8 MiB chunks are capability-bound, streamed to server-generated staging paths outside webroot, checked for origin/size/offset/hash/scope, and only then recorded. Local build, full suite 94/94, 18-migration restore rehearsal, and `Healthy`/HTTP 200 readiness passed; the upload script returned 200 and an unauthenticated chunk request returned 401. Hosted run `35377533259` passed every workflow step on head `6e31ab5`, including setup-dotnet, restore, build, migration, tests and readiness; its Node 20 message is informational. Trusted final completion, SharePoint/provider delivery and production/cross-store recovery remain unobserved.

## Checklist #14 follow-up stage 2 — trusted PBC final completion (local verified, commit `df73109`)

The trusted final-completion boundary now follows the staged PBC chunk transport. Staff-gated `CompleteUploadAsync` re-reads every staged chunk file in receipt order, re-verifies each chunk digest, the contiguous offsets, the total byte count and the combined SHA-256 against the declared identity, and rejects executable magic-number content (PE/MZ, ELF, Java class, shebang) independently of the declared extension or content type. On success the intent becomes `STAGED` — not `RECEIVED` — and a durable `TransferPbcDocument.v1` operation is enqueued inside the same guarded transaction, with the request row untouched. A chunk-state gate refuses new chunks once an intent is staged or terminal, and idempotent completion replay resolves the known staged intent without a second operation.

`PbcDocumentTransferHandler` (SIMULATED mode, Test composition only) is the durable worker/provider handoff: it re-verifies staged bytes before any provider effect, streams them through the `IPbcProviderSink` boundary, and publishes local reception only after verified provider registration — marking the intent `RECEIVED` with provider receipt digest and registration time, and the PBC request `RECEIVED` under the target locks. Interrupted post-write effects preserve `RESULT_UNCERTAIN` and reconcile by probing the provider target; an interrupted-before-effect reconciliation lands `PROVIDER_BLOCKED` after confirming no observable effect; deterministic rejection is `PROVIDER_BLOCKED`; safe transient failures retry with the durable backoff. `PbcTransferDiscovery` re-queues orphaned `STAGED` intents once per intent revision. In Development the web host and worker only record the truthful queued state: no simulated or live execution happens without the explicit Test composition, and no live Microsoft adapter exists.

`20260918184938_PbcTrustedCompletion` adds the `STAGED` state, `provider_receipt_digest`/`provider_registered_at`/`transfer_operation_id` columns with a scoped FK to `durable_operations`, tightened `STAGED`/`RECEIVED` check constraints (RECEIVED now requires provider registration evidence), upgrade refusal of previously `RECEIVED` intents, and downgrade refusal while upload evidence exists. Applied to the local `auditsphere` (nineteen migrations); no production database was touched. The staff engagement PBC route shows upload intents with their durable transfer state and the completion action; the client portal remains upload-only.

Local evidence at `df73109`: locked build 0 warnings/0 errors; full suite 106/106 on PostgreSQL 18.6 (staged-byte tampering/missing refusals, executable-content rejection, idempotent replay, chunk gating, receipt immutability, worker reception timing, interrupted reconciliation, unknown-outcome block, deterministic rejection, safe retry, post-reception upload refusal); `scripts/db/restore-drill.sh` restored nineteen migrations with latest `20260918184938_PbcTrustedCompletion`; readiness returned `Healthy`/HTTP 200 with no pending migrations and `/`, `/health/live`, `/health/ready`, `/app`, `/portal` all 200. Hosted run 35385844180 passed every CI step on head 08e5327, which also caps every per-schema test pool at 3 connections after hosted run 35385091372 exhausted the runner default connection ceiling at test time (recorded as observed failure evidence in status.json).

## Operator recovery, deterministic financial artifact rendering, and staff route catalog — local verified

The current slice implements:
1. **Authorized Operator Recovery:**
   - Migration `20260918194920_OperatorRecoveryWorkflow` updates the `enforce_operation_request()` PostgreSQL trigger function to permit resetting `attempt_count` back to 0 exclusively when transitioning to `RETRY_WAIT`. It strictly preserves monotonic attempts during normal execution and guarantees complete immutability of `attempt_token`, completed results, and original request identity.
   - `OperationRecoveryService` enforces inside-command role authorization (`Administrator`, `Partner` for quarantine lifting; `Administrator`, `Partner`, `Manager` for operation retry re-arming).
   - `/app/operations` Blazor page renders firm safety state, active quarantine alerts with one-click operator release, and operation listing with retry re-arming.
   - `OperationRecoveryTests` (5 tests) verify quarantine lifting, role-gating refusals, retry state transitions under trigger enforcement, and refusal of illegal mutations.

2. **Deterministic Financial Statement Package Artifact Rendering:**
   - `FinancialStatementService.RenderPackageArtifactAsync` compiles an adjusted package, balance sheet/income statement lines, cash flow bridge, and disclosure responses into a canonical UTF-8 byte stream, computing a deterministic SHA-256 digest and exact byte length.
   - `/app/accounting/packages/{id}` Blazor screen displays the artifact SHA-256 hex digest, size in bytes, and collapsible canonical text preview alongside package line items.
   - `FinancialStatementTests.FinancialPackage_RendersDeterministicArtifact_WithVerifiableSha256` verifies repeatable byte generation and SHA-256 matching.

3. **Staff Route Catalog & Practice Operations:**
   - `/app/clients/{ClientId}`: Verified client profile, contacts list with `CreateClientContactAsync` and primary contact resolution, associated engagements, and safety state.
   - `/app/engagements/{EngagementId}`: Engagement metadata, reporting period, status, hold clearance table (`EngagementHold`), and deep links to PBC requests.
   - `/app/practice/leads`: Guarded lead intake and pipeline progression.
   - `/app/practice/time`: Daily/weekly staff time entry with `SaveTimeDraftAsync`, submission, and segregation-of-duties approval queue with `ApproveTimeAsync`.
   - `/app/practice/invoices/{InvoiceId}`: Invoice review and posting with `PostInvoiceAsync`, line items, and receipt allocations.
   - `/app/finance`: Firm ledger chart of accounts, fiscal periods with `CloseFiscalPeriodAsync`, and recent balanced postings.
   - `/app/operations`: Quarantine alerts with operator release, and operation retry re-arming.
   - `/app/administration`: Firm safety state, runtime environment toggles, and active role grants summary.
   - `/app/accounting/journals/{JournalId}`: Balanced adjustment journal review and posting with separation of duties.
   - Layout & Portfolio navigation links added for all operational surfaces.

Local verification evidence:
- Build: 0 warnings, 0 errors across 6 projects.
- Test suite: 115/115 passed on PostgreSQL 18.6 (0 skipped), including new RouteCatalogTests covering contact generation increments, time entry segregation of duties, and period close rules.
- Restore rehearsal: `scripts/db/restore-drill.sh` dumped and restored 20 migrations with latest `20260918194920_OperatorRecoveryWorkflow`.
- Readiness smoke: Web host started on port 5099; `/health/ready` returned `Healthy`/HTTP 200 with 0 pending migrations; smoke requests to all catalog routes returned HTTP 200.

## Mapping and financial-statement package follow-up — local verified

`FinancialStatementService` now creates and approves versioned source-to-taxonomy mappings, reconstructs an adjusted trial-balance snapshot from a finalized adjustment plan, validates and persists a deterministic opening/closing cash-flow bridge plus bounded disclosure responses, and emits scoped package lines/totals. Packages without supplementary inputs remain `REVIEW_REQUIRED`; complete local inputs reach `VALIDATED` input status but still require accounting/management approval. The migration is local-only; no provider, tenant record, signing or production assertion is made. Scoped mapping/package views and portfolio package links use the authenticated actor resolver. Six `packages.lock.json` files are committed so `actions/setup-dotnet@v4` caching has the required inputs.

## Not complete or production-enabled

The initial Blazor shell now includes scoped mapping/package views and a restricted client PBC inbox/request route. The full staff/client route catalog, scoped forms for the remaining commands, trusted final-completion/provider delivery, and authorized operator recovery UI remain unfinished. Generated document rendering, real provider adapters, full signing/protection evidence, and full cross-store recovery also remain unfinished. Accounting, outbox, CRM, time/budget, billing, bounded firm-ledger, document-snapshot, approval, release, supplementary package inputs, PBC state/upload intent persistence, and their scoped FKs/checks/immutability controls now exist; remaining module chains still need integrity work. TB rows reject UPDATE/DELETE, but this slice does not complete the future promoted-dataset insert freeze/import lifecycle. Validation retains a table-level row-writer lock and loads one TB into memory; no workload claim is made. Hosted CI is now verified, but readiness and local restore proof do not establish production recovery. Checklist #13 remains partial and #14 remains partial because UI/recovery/external gates are open; external checklist #15–16 remain blocked.

## Tenant evidence (EasyGuide, observed 2026-09-18 and 2026-09-19)

- The requested browser session is authenticated as `qts@easyguide.onmicrosoft.com` in tenant `easyguide` (`hamzaholdings.com`), tenant ID `4de3e6fd-51aa-4ba7-b2c5-82106d2e45f0`. Graph Explorer `GET /v1.0/me` returned HTTP 200. The broad site-search query returned HTTP 403; no tenant-wide `Sites.Read.All` consent was granted.
- `AuditSphere Development` is a private one-member site with empty `Client Content`, `Internal Workpapers`, and `Issued Records` libraries. The P0 client and issued-records test sites are private one-member sites with standard empty document surfaces observed.
- Microsoft 365 admin showed the signed-in user with Microsoft 365 E5 Developer (without Windows and Audio Conferencing), Power Apps for Developer, and Power Automate Free. The new Purview portal is reachable with Microsoft 365 connected, but its displayed compliance posture is 0%; no production records profile, retention-label proof, or professional signing/methodology approval was observed.
- On 2026-09-19, Entra showed the single-tenant `AuditSphereOps Development` app (`29be1ee5-e90c-4ecc-b4f9-5bcce14774cc`) with Microsoft Graph `Sites.Selected` application permission granted for EasyGuide. No client secret or certificate was created.
- On 2026-09-19, Graph Explorer under the visible `easyguide` tenant label returned HTTP `201 Created` for a `write` permission on `AuditSphere Development` (`easyguide.sharepoint.com,4668a3d6-8c1a-462d-accc-f95e7534aae5,b3c5cb09-7b20-4270-b352-aa4ca4db3722`). A follow-up `GET /permissions` returned HTTP `200` with `roles: ["write"]` and the same application ID. No tenant-wide `Sites.Read.All` grant was added.
- The selected non-production grant does not provide production credentials, Purview approval, professional sign-off, independent review, or real-tenant RPO/RTO evidence; those gates remain blocked.

## Records profile, archive manifest, retention and hold evidence (local, observed 2026-09-19)

- Migrations `20260919123339_RecordsArchiveWorkflow`, `20260919124558_ArchiveProtectionObservation`, `20260919134031_ArchiveStructuredExports`, and `20260919134442_RecordsActionEvidence` add versioned `RecordsProfile`, scoped archive manifests/entries, persisted §25.3 structured exports, append-only records-action evidence, `RecordsAction`, and `LegalHold` persistence. Archive status is constrained to `ISSUED → ASSEMBLY_IN_PROGRESS → MANIFEST_BUILT → ASSEMBLY_REVIEWED → RECORDS_ACTION_REQUESTED → PROTECTION_OBSERVED → ARCHIVE_VERIFIED`; the database rejects `PROTECTION_OBSERVED`/`ARCHIVE_VERIFIED` without a stored observation state and timestamp.
- `RecordsArchiveService` creates approved-profile metadata, builds a digest-stable manifest from real document snapshots plus a structured `records-export.v1` payload, validates document-reference completeness, records requested versus observed protection, and tracks requested/observed legal holds. It does not call Purview and never treats a requested action as proof.
- `/app/records/archives/{id}` now reads the persisted archive, manifest, entries, records action and holds under the authenticated firm/engagement authorization boundary. Placeholder engagement names, hashes, dates and fake documents were removed.
- Local evidence: `dotnet build AuditSphereOps.slnx` passed with 0 warnings/errors; `dotnet test` passed 161/161 on PostgreSQL 18.6 with 0 skipped; the archive tests passed; web `/health/ready` and `/health/live` returned Healthy/200; and the restore drill passed 30/30 migrations. Deterministic re-archive versions, disposition-package blocking, Purview behavior, production records approval and external recovery remain open.

## Audit planning, materiality, risks, populations, workpapers, findings lifecycle and UI routes (local verified)

The current slice implements:
1. **Audit Planning Service & Domain Operations (§§19–23, 41.5):**
   - Migration `20260918235000_AuditPlanningAndFirmPostingExtensions` adds the audit tables and finance schema posting extensions.
   - `AuditPlanningService` provides guarded command execution for:
     - `CreateMaterialityAssessmentAsync`: Benchmark amount/rate, overall/performance materiality, clearly trivial thresholds (§19.2) with strict threshold ordering validation (`ck_materiality_thresholds`).
     - `CreateAuditRiskAsync`: Account area, assertion, drivers, significance decision (`SIGNIFICANT` vs normal), response descriptions (§19.3).
     - `CreatePopulationVersionAsync`: Extraction parameters, row counts, non-negative monetary control totals, currency, and exclusions (§20.1).
     - `CreateWorkpaperAsync` & `SubmitWorkpaperAsync`: Workpaper revision increments, concurrency conflict rejection on stale revision, immutable submission history (`workpaper_submissions`), and status transitions from `WORKING` to `SUBMITTED_SNAPSHOT` (§21.1).
     - `CreateFindingAsync`: Finding type, impact description, corrected status, and monetary amounts (§23).
     - `AssertBalanced`: Pure domain posting line invariant asserting debits equal credits (§41.5).
2. **Audit & Review Blazor UI Screens:**
   - `/app/audit/plans/{id}`: Audit planning and strategy overview (materiality thresholds, identified risks, populations, workpapers).
   - `/app/audit/populations/{id}`: Population validation, row counts, control totals, and sampling parameters.
   - `/app/audit/workpapers/{id}`: Workpaper working surface, revision tracking, evidence linking, and submission.
   - `/app/audit/findings/{id}`: Finding review, impact evaluation, corrected status, and management response.
   - Assessment and review point support screens: `AssessmentDetail.razor`, `AssessmentDecision.razor`, `ReviewPoint.razor`, `ProposalDetail.razor`, `RecordsArchive.razor`, `Completion.razor`.
3. **Automated Verification:**
   - `AuditPlanningTests` (15 tests) verify NT-21 (materiality, risks, populations, workpapers, findings lifecycle), NT-22 (posting balance domain invariant), NT-23 (tenant prerequisites absence verification with provenance), and NT-24 (immutable approved time entries and client safety state generation invariant).
   - Full test suite: 130/130 passed on PostgreSQL 18.6 with 0 skipped.
   - Restore rehearsal: `scripts/db/restore-drill.sh` dumped and restored 21 migrations with latest `20260918235000_AuditPlanningAndFirmPostingExtensions`.
   - Readiness: `/health/ready` and `/health/live` returned `Healthy`/HTTP 200 with 0 pending migrations.

The checklist #6 implementation is committed as `141a55d2c7a9df8e23f41b6365c3f2ac58148240`; owner-authorized PR #1 merged as `881eda1e44c8eccba7fa7e8dd26f86ee4b4f5599`, with GitGuardian passing, the automated reviewer neutral, and no independent human review observed. Checklist #7 is committed as `0a34400`; owner-authorized PR #2 merged as `b1b23bef1a0f9cbc63a2a757bf369c873671e303`. Checklist #8 implementation `88e3409` is merged as `3bcdaa7812904e8259f128b5bdae0f99d773dce3`; no independent human review was recorded. Checklist #9 implementation `ea4081424df8e3d24fd07feefb69f1d7631bb8c7` was merged by owner-authorized PR #4 as `c01f3d31aea5b4c10f3293862a44439ff42d230d`; no independent human review was recorded. Checklist #10 implementation `0f9fe20` was merged by owner-authorized PR #5 as `c091bf2bf0a859173ee4228b359c90d1b5202320`; no independent human review was recorded. Checklist #11 implementation `94a8da3` was merged by owner-authorized PR #6 as `87e2523dcc276d5a358d7bcbe2015d5f744efa4a`; no independent human review was recorded. Checklist #12 implementation `3e3c82a35b32d4472f328d44529f9a9b048a5a7c` was merged by owner-authorized PR #7 as `2dd97f93ae6f7e61ac9bad7eb378ea48922ff9f7`; no independent human review was recorded. Checklist #13 implementation `c127448e645da2b29018359a3226afa6b310e3b1` was merged by owner-authorized PR #8 as `ff577837241a7bd2797bb5776dbcce7687ed8e05`; no independent human review was recorded. Checklist #14 implementation commits `847cada`, `7fcbc35`, and `bfe5a9a` add the mapping/financial-statement workflow and hosted lock-file cache/restore fixes; hosted run `35375711799` passed all steps on head `a8062b4`, but no independent human review was recorded. The current follow-up also adds migration `20260918170054_SupplementaryFinancialInformation`, migration `20260918173327_PbcUploadWorkflow`, migration `20260918175205_PbcUploadCapability`, migration `20260918194920_OperatorRecoveryWorkflow`, migration `20260918235000_AuditPlanningAndFirmPostingExtensions`, deterministic cash-flow/disclosure persistence, scoped mapping/package views, client/staff PBC routes, sequential immutable chunk receipts, capability-bound same-origin 8 MiB chunk staging, authorized operator recovery, deterministic financial artifact rendering, staff route catalog, audit planning/materiality/risk/population/workpaper/finding lifecycle, and source-relative restore verification. Only the known local development database was migrated. Handoff pointer: `docs/execution/status.json`.

## Audit-planning scope integrity, database immutability, and in-command authorization (locally verified, uncommitted)

Base `8a0f035`. This closes the "remaining entity foreign keys and data-model integrity gaps" backlog item for
the audit-planning domain (§27.4–27.6, §42.3–42.4).

Observed defects that this slice removed, and what replaced them:

1. **Three tables were outside EF ownership.** `materiality_assessments`, `population_versions` and
   `workpaper_submissions` were created by raw SQL in `20260918235000`, which shipped without a Designer file and
   without model-snapshot entries (the snapshot owned 79 entities and none of them were these tables). They are now
   `MaterialityAssessment`, `PopulationVersion` and `WorkpaperSubmission` entities with `DbSet`s on both
   `AuditSphereDbContext` and `IAuditSphereDbContext`, so a future migration diffs them like every other table.
   `AuditSphereDbContext.Database.HasPendingModelChanges()` is asserted false by test `IG-01`.
2. **Scope was weakened, not tightened, by the previous slice.** That migration dropped `NOT NULL` on
   `audit_risks.{firm_id,client_id}`, `workpapers.{firm_id,client_id,procedure_id,state,generation}` and
   `findings.{firm_id,client_id,title,severity}` while the EF model still declared them required, so the database
   silently drifted from the model and EF had no diff to emit. `20260919005220_AuditPlanningScopeIntegrity` restores
   the required scope columns and re-declares `workpapers.procedure_id` as genuinely optional (an empty GUID in a
   scope key defeats duplicate prevention, §42.4).
3. **No child row proved matching client scope.** The three `*_engagement_id_fkey` constraints referenced
   `engagements(id)` alone. They are replaced by composite foreign keys onto `engagements (firm_id,
   practice_client_id, id)` and, where a second parent exists, onto `(firm_id, client_id, engagement_id, id)`
   principals for `audit_risks`, `audit_procedures`, `source_receipts`, `workpapers` and `users (firm_id, id)`.
   Nineteen tables that previously had no firm-inclusive foreign key at all now have at least one, including
   `engagement_assignments`, `eqr_cases`, `written_representations`, `evidence_links`, `review_points`, `archives`,
   `record_states`, `mapping_rules`, `acceptance_decisions` and `evaluation_responses`.
4. **Professional evidence cascaded on delete.** `DeleteBehavior.Cascade` on assignments, EQR cases, written
   representations, source receipts, evidence links and questionnaire definitions became `Restrict` (§42.3).
   `SELECT ... FROM pg_constraint WHERE contype='f' AND confdeltype <> 'r'` now returns zero rows for the whole
   `public` schema, and test `NT-11b` proves an engagement with a workpaper cannot be deleted.
5. **Immutable evidence was only a convention.** Append-only triggers (SQLSTATE `55000`) now protect
   `materiality_assessments`, `population_versions` and `workpaper_submissions` outright, and `workpapers` become
   frozen once their status is `SUBMITTED_SNAPSHOT` (update or delete refused). `NT-12` exercises all eight
   mutations; `NT-12b` confirms a working paper stays editable.
6. **Audit planning commands had no authorization and shifted their columns.** `AuditPlanningService` took a bare
   `ActorId` Guid, inserted with raw SQL, and wrote `audit_risks.description` from `Drivers`,
   `audit_risks.severity` from `SignificanceDecision`, `findings.title`/`severity` from `FindingType`. Every command
   now takes an `ActorContext`, resolves the client from the stored engagement, calls
   `AuthorizationDecision.AuthorizeAsync` (internal-only, professional-work gate, covering grant, planning roles),
   serializes on the engagement row, writes through the model, and returns `CommandResult` codes (§27.7). Severity is
   derived from the significance decision and pinned by `ck_audit_risk_values`, so the two cannot disagree.
7. **Four audit screens were fabricated.** `Workpaper.razor`, `AuditPopulation.razor` and `Finding.razor` rendered
   hardcoded banks, selections and "material uncorrected misstatement" conclusions with no data access, and
   `AuditPlan.razor` queried five `materiality_assessments` columns that do not exist (`currency`,
   `overall_amount`, `benchmark_type`, …), which would throw at runtime. All four now read persisted rows, show
   truthful empty/pending states, and invoke the real commands; the invented audit conclusion was removed (the
   application does not generate professional conclusions).

Migration safety: the new migration refuses before modifying anything when any in-scope planning table holds rows
(`55000`, "needs an explicit disposition"), because the added `NOT NULL` scope and actor columns cannot be backfilled
without inventing an owner or a client — the same stance as the durable-outbox and release-gate migrations. Test
`MIG` proves the refusal and that the legacy shape survives it. The downgrade path re-creates the three formerly
model-external tables and the legacy columns exactly as `20260918235000` defined them; the revert and re-apply were
both executed against the local `auditsphere` database.

Local verification: build 0 warnings / 0 errors; locked restore; full suite 152/152 with 0 skipped (twice); 23-migration
restore rehearsal; `Healthy` readiness with 0 pending migrations; HTTP 200 on `/`, `/health/live`, `/health/ready`,
`/app`, `/portal` and the four audit/release routes. Runner parallelism was capped at three threads in
`tests/AuditSphereOps.Domain.Tests/xunit.runner.json` after the added constraints made the full parallel run
intermittently exhaust the loopback cluster's default shared lock budget (`53200`); no server setting was changed and
no test was skipped. Hosted CI, independent review and any merge remain outstanding.
