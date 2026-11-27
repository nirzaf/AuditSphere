# Audit remediation continuation — 2026-11-27

## Source and scope

The required verified baseline is `62059aa2e622bcb6245a211380021f5cf315cd97` (2026-11-26). At continuation start, local `master`, fetched `origin/master`, and HEAD were `5eedb70e9419ad2051191419bad52c9d3991b112`. The focused performance slice is committed and pushed as `fe5685c7a5a5bb6f37c978f7a2ccf79744a34a9a`. Five unrelated dirty Angular specs are preserved. No worktree, branch, package pin, migration, provider effect, production change, merge, or release was created or performed.

## Implemented continuation slices

### M365-011 — IMPLEMENTED

The supported same-tenant connection replacement path advances the connection revision and marks new consent required while retaining old capability-verification rows. Before this change, `TenantCapabilityService.StatusesAsync` selected the latest firm/tenant/capability row without proving it belonged to the authoritative current connection and verified consent attempt.

- `src/AuditSphereOps.Application/Microsoft365/TenantCapabilityService.cs`: verification records bind to the current connection and verified consent attempt; status exposes only matching current evidence. Missing or mismatched binding projects `NOT_VERIFIED` and existing `GateBlocked`; mail evidence remains on its separate signed/enrolled binding.
- `tests/AuditSphereOps.Domain.Tests/TenantAdministrationTests.cs`: supported replacement, stale denial with zero provider calls, and fresh supported consent/verification restoring the provider path. Old evidence remains retained.
- `docs/architecture/auditsphere-architecture-code-map.md`: owner boundary updated.
- Verification: `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 -p:UseSharedCompilation=false -nodeReuse:false --filter 'FullyQualifiedName~TenantAdministrationTests.CapabilityVerification_SameTenantConnectionReplacementRequiresFreshConsentAndVerification|FullyQualifiedName~TenantAdministrationTests.CapabilityVerification_NotGrantedAndStaleStatesBlockMutations'` — **PASS, 2/2**, PostgreSQL-backed isolated fixtures, synthetic provider only.
- No schema change or live tenant/provider call. This is the supported transition observed in the controlled test; it does not claim universal unauthorized provider use.

### PERF-DELIV-01 / AC-OUT-13 — IMPLEMENTED

Before the change, `AuditDeliverableService.ListAsync` materialized `AuditDeliverable.Content` for a metadata list and called full currentness evaluation separately for every listed row. The representative authorized fixture generated three actual deliverables through supported commands.

- `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs`: list projects only the metadata required by the existing response; it reuses the exact-engagement metadata projection for source links and latest versions, computes the shared factual digest once per authorized list request, and computes currentness once per current kind. The dedicated authorized download remains the content-reading path. Ordering, signature metadata, row visibility, and stale/current distinctions are retained.
- `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs`: measures database reader commands and materialized deliverable-content bytes; checks current and stale list projections, standalone currentness, signed listing, and a query-count bound against the recorded baseline.
- `docs/architecture/auditsphere-architecture-code-map.md`: listing/download ownership documented.
- Measurement on three generated records: retained content **5,062 bytes**; before, `ListAsync` used **319 reader commands** and materialized **5,062 content bytes**, while standalone `IsCurrentAsync` used 104 commands and materialized zero content bytes. After, `ListAsync` used **106 reader commands** and materialized **zero content bytes**; standalone `IsCurrentAsync` remained 104 commands and zero content bytes. These are fixture/query observations, not a latency or production-SLO claim.
- Verification: `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~AuditDeliverablesTests.DeliverableList_ProjectsMetadataAndSharesCurrentnessCalculation' --logger 'trx;LogFileName=perf-final.trx' --results-directory /tmp/auditsphere-perf-final` — **PASS, 1/1**; TRX recorded the 106/0 final measurement. The combined final regression command for `DeliverableList_ProjectsMetadataAndSharesCurrentnessCalculation`, `CompletionAndSrmRequireCurrentIndependentlyReviewedProcedureEvidence`, and `PartnerSigning_AppliesTheFirmCertificate_AndFailsClosedWithoutAUsableOne` — **PASS, 3/3**. No cache persists across requests and no index was added.

### AUTH-006 — BLOCKED_CONTEXT; existing application denial retained

`AuditSphereDbContext.Security.cs` has separate firm/client and firm/engagement foreign keys plus `ck_role_grant_scope`; it does not bind `RoleGrant.ClientId` to the stored parent client of its engagement. The existing `AuthorizationDecisionTests.EngagementGrantMustMatchItsStoredClientParent` inserts that mismatched synthetic row and verifies application authorization denies it. This is integrity hardening evidence, not an observed disclosure.

No migration was made. The exact missing inputs are an authorized target identity and explicit read authority for a bounded production history preview, and an isolated upgrade fixture demonstrating the intended clean-history migration path. The test fixture deliberately contains a malformed grant for the application-denial regression. No historical grant was rewritten or revoked.

- Verification: `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~AuthorizationDecisionTests.EngagementGrantMustMatchItsStoredClientParent'` — **PASS, 1/1**; existing application denial retained. No migration or production data read was attempted.

### PBCX-005 — BLOCKED_DECISION

Source trace: `PbcService.CompleteUploadAsync` checks originating authority before staging and atomically stores the trusted operation intent; `PbcDocumentTransferHandler`, `PbcRepositoryBindingResolver`, `PostgresOperationStore`, and `OperationDispatcher` retain operation, source, resource, and publication fences. The worker does not recheck the originating user’s grant/session after durable enqueue before dispatch/publication. The existing `RevokedIdentities_CannotProvisionStageOrComplete` test does not cover revocation after staging.

No controlled post-staging revocation test or worker change was made because the approved rule for whether an already-accepted durable intent survives originating-user revocation is absent. Missing decision: name the qualified owner and decide whether enqueue-time intent remains trusted after later revocation, its exact scope/source/digest/current-resource binding, and how unknown dispatch/publication outcomes are reconciled. No exploit is asserted and no revoked user authority is granted to the worker.

### AUTO-025 — BLOCKED_CONTEXT; readiness remains fail-closed

WU-025 expects source-owned observed identity, grant/resource, expiry, positive/negative probe, configuration-digest, and resume-binding evidence. Current `TenantCapabilityVerification` / `CapabilityStatus` do not retain those generic facts or define their capture owner, freshness, and redaction rules. The existing readiness response retains `INCOMPLETE_EVIDENCE` and `engineering:api-runtime-capability-evidence-projection-required`; no missing observation was inferred from configuration or a generic `VERIFIED` row. No projection was changed.

`pwsh -NoProfile -File scripts/m365/tests/Readiness.Tests.ps1 -Offline` is **BLOCKED_TEST_ENV**: this host has .NET 11 only, while the installed PowerShell host requires `Microsoft.NETCore.App 10.0.0`. No runtime or pinned dependency was installed or changed. Required context to resume: approved per-capability field sources, owner, freshness, redaction, exact firm/current-connection/consent binding, and receipt/resume source. Offline Plan/DryRun/WhatIf remains separate from external readiness.

## Disposition register

Statuses apply to the named engineering slice; IMPLEMENTED does not mean production acceptance. Historical focused checks remain tied to the SHAs and evidence named in `auditsphere-audit-fix-history-20261127.md`, the dated evidence files, and `archive/auditsphere-audit-fixes-baseline-status-20261126.json`.

| Finding / requirement group | Disposition | Boundary / remaining condition |
|---|---|---|
| F01 | IMPLEMENTED | Consolidation lock ordering and immutable component inputs retained. |
| F02 | IMPLEMENTED | Firm-finance lock-wait authority and seal rechecks; focused PostgreSQL evidence is historical and source-bound. |
| F03 | IMPLEMENTED | Expense/journal transitions are atomic; cancelled-only period and SQL immutability behavior retained; no historical repair. |
| F04 | IMPLEMENTED | Versioned completeness proofs preserve v1 history; no production migration. |
| F05 | IMPLEMENTED | Cash-flow lineage binds exact nullable package book; historical bytes/links not rewritten. |
| F06 | IMPLEMENTED | New external GL lines require posting/ACTIVE accounts; no incident or history mutation claimed. |
| F07 / AC-LAY-01 | IMPLEMENTED | Minimal client-owned layout producer is on master; focused tests/OpenAPI evidence is source-bound. |
| F08 / PRA-109 | IMPLEMENTED | Safe report currency supports approved nonchargeable cost; missing/mixed cost basis stays unavailable. |
| F09 / PRA-099 / PRA-103 | IMPLEMENTED | Unsafe budget actuals project null; no FX or rate-policy adoption. |
| F10 | ALREADY_FIXED | Storage-aligned period/book bounds verified in the earlier current-master check. |
| F11 | IMPLEMENTED | Explicit period context for balance reporting; overlapping periods do not use first-match selection. |
| F12 | IMPLEMENTED containment | Generic self-comparison is blocked; AC-REC-07–10 remain separately gated. |
| F13 | IMPLEMENTED | Corrupt pending allocation evidence fails closed without clearing retained evidence. |
| F14 / ARCX-003 | IMPLEMENTED | Current Graph bytes use item content; historical bytes remain version-pinned. |
| F15 / AUTO-021 | IMPLEMENTED | Each archive receipt compares to its matched file timestamp; local PowerShell verification is separately environment-blocked where noted in historical evidence. |
| F16 / VERIFY-001 | IMPLEMENTED | Strict test-result/TRX assertions retained. |
| F17 | ALREADY_FIXED | Identity-switch time/draft reset verified in earlier focused Angular evidence. |
| F18 | ALREADY_FIXED | Criticality requires actual trimmed rationale and service validation. |
| F19 / FW-PHASE-01–04 | BLOCKED_DECISION | No adopted phase/due-gate methodology or legacy-procedure mapping. |
| F20 / SIGN-01 / SIGN-03 / SIGN-05 / SIGN-06 | BLOCKED_DECISION | Qualified owner must approve composition/management concurrence, representation dates/signatory, modified-opinion matters, and additional-section applicability. |
| F21 / REL-09 / REL-10 / REL-13 / ARC-01–03 | BLOCKED_DECISION | Terminal lifecycle, recipient/channel and records/retention/hold/disposition rules remain unapproved; ARC-05 first-archive eligibility stays blocked. |
| F22 / DEPLOY-008 | BLOCKED_DECISION | Azure `release.yml` and IaC remain untouched pending explicit hosting-authority decision. |
| F23 / AC-GL-13 | BLOCKED_DECISION | No approved original-upload byte custody, retention, hold, or deletion authority. |
| F24 / AC-OUT-14 / AC-OUT-15 | BLOCKED_DECISION | No approved canonical seal/composition relationship, artifact set, or currentness consumer. |
| F25 / AUTO-020 / M365-022 | IMPLEMENTED; live gate BLOCKED_EXTERNAL | Current mail composition regressions are offline only; owner-approved retained Acceptance target, exact signed current evidence, recipient, and authorized live mutation scope are absent. |
| F26 | BLOCKED_DECISION | No approved partial-disposition/balanced-replacement professional rule. |
| F27 | IMPLEMENTED | Possible fiscal month/day pairs validated; retained impossible-history precheck/repair and non-leap February 29 rollover remain separate. |
| F28 / PRA-007 / PRA-008 | IMPLEMENTED | Reviewed profile/contact maintenance and retained receipts are on master. |
| F29 / VERIFY-010 | IMPLEMENTED | Completion runner single-writer and crash/resume protections are on master. |
| F30 | ALREADY_FIXED | Signed TB/source-side magnitude limits and parser refusal are present and verified. |
| F31 | IMPLEMENTED | Final rendered bundle bytes and ZIP are bounded before persistence. |
| FW-BANK-01 | IMPLEMENTED | Typed bank producer/review handoff, scoped receipts, locks and transaction participation; browser journey remains for exact-SHA CI. |
| ARC-05 | IMPLEMENTED UI handoff | Exact selected bundle and actor-local recovery use the existing archive request identity; selected-bundle API/browser journey remains unverified locally; F21 still blocks first-archive eligibility. |
| PRA-127 | IMPLEMENTED | Native finance flows capture strict UTC receipt time; no new backdating cutoff or allocation-date rewrite. |
| M365-014 | IMPLEMENTED | Graph directory response byte limits apply to actual bytes read. |
| M365-011 | IMPLEMENTED | Supported same-tenant replacement regression and current-binding guard are at `5eedb70e`; this continuation’s 2/2 test result is current local evidence. |
| M365-019 / M365-020 / M365-021 / M365-022 / LIVE-001 / DEPLOY-003 / DEPLOY-004 | BLOCKED_EXTERNAL | Independent target, environment, restore, identity, mail, selected-site, and recipient evidence remains absent; see rows below. |
| PBCX-005 | BLOCKED_DECISION | Originating-authority continuation after enqueue has no approved rule; see trace above. |
| PERF-DELIV-01 / AC-OUT-13 | IMPLEMENTED | Measured metadata projection and per-request currentness sharing; no production latency/SLO claim. |
| PRA-015 | IMPLEMENTED | Quotation rate reads were measured and exact-key per-call memoization was narrowly applied. |
| HTTP-002 | NO_CHANGE_REQUIRED | Existing incremental legacy envelope contract preserved; no blanket migration. |
| PLAT-002 | IMPLEMENTED | Worker-composition documentation reflects `WorkerCapabilityProfile.Evaluate` and `WorkerOptions.Validate`; no deployment approval implied. |
| AUTH-006 | BLOCKED_CONTEXT | Existing application denial passes; database tuple hardening awaits authorized history evidence and the isolated migration fixture. |
| AUTO-025 | BLOCKED_CONTEXT | Required runtime observation ownership/freshness sources are not represented by the current evidence contract; current blocker remains. |
| AUTO-018 | BLOCKED_DECISION | Legacy mail model remains approved; no Exchange permission migration packet. |
| PRA-010 | BLOCKED_DECISION | No approved legal-entity survivor, conflict, reparenting, retention, or access rule. |
| PRA-028 / PRA-031 / PRA-032 / PRA-033 / PRA-039 / PRA-118 | BLOCKED_DECISION | No approved stage-appropriate executed-terms, signer/evidence, amendment-materiality, or professional-work gate. |
| PRA-058 | BLOCKED_DECISION | No approved delegation scope, qualifications, expiry, revocation, or separation policy. |
| PRA-098 / PRA-110 | BLOCKED_DECISION | Rate category/currency/effective-date/approval/confidentiality rules absent; independent F08 defect fix is implemented. |
| PRA-133 / PRA-134 / PRA-152 | BLOCKED_DECISION | Revenue recognition, event-to-account, exception, FX, reversal and cutover policy absent; automatic recognition stays off. |
| PRA-157 | BLOCKED_DECISION | Final offboarding settlement/access/records decision absent; current closeout refusal remains. |
| AC-GL-03 | BLOCKED_DECISION | No approved large-GL producer, start-recovery, staging/custody, abandoned-load or cancellation contract. |
| AC-NAT-02 | BLOCKED_DECISION | No approved existing-client native cutover, opening population, freeze, residual, reviewer or recovery decision. |
| AC-REC-07 / AC-REC-08 / AC-REC-09 / AC-REC-10 | BLOCKED_DECISION | Independent source/control pairings, adjustment-reflection basis, carry and review rules absent; F12 containment remains implemented. |

## Blocked-decision checklist

- **PRA-028 / PRA-031 / PRA-032 / PRA-033 / PRA-039 / PRA-118:** qualified owner must decide CE-084 stage truth, acceptable external/digital execution evidence, signer eligibility, authorization versus dispatch versus execution, effective terms version, material amendment treatment, and the exact professional-work gate. No generated-letter prerequisite is inferred.
- **PRA-133 / PRA-134 / PRA-152:** qualified accounting owner must approve event-to-account mapping, advance/deferred/final recognition timing, receipt/credit/refund/write-off/overpayment treatment, currency/FX, period/reversal handling, and cutover. Automatic recognition remains off.
- **F21 / REL-09 / REL-10 / REL-13 / ARC-01–03:** owner must decide terminal command/accountable actor; recipient identity, expiry, revocation and channel; accepted/delivered/unknown/acknowledged meanings; jurisdiction/service route; retention trigger/duration, holds, disposition owner, backup/custody and independent approval. Local archive creation stays distinct from observed provider protection.
- **F22 / DEPLOY-008:** owner decision explicitly authorizing any change to the retained Azure release path. Future repair scope remains: set/verify immutable migration image before start, poll the exact returned execution identity, deploy API/worker only after success, and derive smoke targets from selected deployment outputs. No workflow, IaC or credential edit was made.
- **F19 / FW-PHASE-01–04:** methodology owner must adopt due gates for planning, fieldwork submission, pre-signing review and post-signing/pre-release, plus treatment of all legacy procedure assignments and signed-report evidence. No proposed mapping was applied.
- **F20 / SIGN-01 / SIGN-03 / SIGN-05 / SIGN-06:** qualified owner must approve exact statement composition and management approval evidence/signatory; report and representation dates plus the meaning of proximity/re-signing; allowed pervasive/evidence-limitation matters; and approved going-concern, emphasis, other-matter and key-audit-matter templates/applicability. No professional conclusion was inferred.
- **F24 / AC-OUT-14 / AC-OUT-15:** approved architecture decision must state whether the two-digest seal is canonical, subordinate to composition, or superseded; mandatory artifact set; consuming currentness/approval gate; and historical compatibility.
- **F23 / AC-GL-13:** data owner must approve exact evidence-content store/receipt, original-byte custody, retention and hold/deletion treatment, size limit, and authority to persist bytes. No raw uploads were added or reconstructed.
- **AC-GL-03:** owner must adopt the large-GL native producer, supported bounded transport/profile, start-intent recovery/resume owner, abandoned `LOADING` and cancellation treatment, and T015–T017 handoff or approved narrower dependency. F23 custody is required first if bytes are staged.
- **AC-NAT-02:** qualified client/accounting owner must approve source mode, effective period/book boundary, opening TB and AR/AP populations, residual, source freeze, mandate, reviewer and recovery for the exact client cutover.
- **F26:** professional owner must approve M22 partial-acceptance semantics, linked rejected remainder, balanced replacement authority and treatment of already partial/posted/exported history.
- **AC-REC-07–10:** qualified accounting owner must approve independent source/control pairing, signed convention, adjusted/unadjusted/reflected-AJE basis, carry semantics and review applicability.
- **PRA-058:** qualified owner must decide who may delegate which task/approval, bounded scope/start/end, revocation/absence, qualifications and separation; assignment alone is not approval authority.
- **PRA-098 / PRA-110:** qualified owner must approve charge-out and internal-cost categories, currencies, valuation/effective intervals, gaps/overlaps, approvers, overrides, historical cutover, cost-rate review roles and confidentiality.
- **AUTO-018:** separate mail-migration packet must identify tenant, service principal, approved sender set/excluded control mailbox, authorization model, additive-grant removal, rollback and provider readback. Legacy remains the approved model.
- **PRA-010:** qualified owner must approve survivor identity, duplicate evidence, roles, conflicts, references/history/records/access consequences and immutable source-to-survivor mapping.
- **PRA-157:** qualified owner must decide mandate/work, balance/dispute, access, records/retention/restoration and accountable closeout reviewers; reactivation remains separately approved.
- **PBCX-005:** qualified owner must decide whether an accepted durable intent survives originator grant/session revocation and define exact intent/source/resource/unknown-outcome fences.

## Exact-source CI and release boundary

At this observation (2026-11-27T01:38:07Z), `origin/master` and HEAD are `8ada67a742f438074f8fa1fe3f72930e06d7fa95`; five unrelated local Angular spec edits remain uncommitted and untouched. Fast CI run **45922099495** on that exact SHA passed its docs/MCP/M365 jobs; change classification skipped Angular and .NET builds because the pushed change was documentation-only. The latest full release-verification run, **45920288769**, was on parent SHA `3f96dcd6e43dd06cd7b905eba4db7ec295df5682` and finished **FAILURE**: its exact-source Release fallback build succeeded, but Angular had 9 five-second timeouts in `src/AuditSphereOps.Ui/src/app/features/practice/resources.spec.ts` (2,153 passed, 9 failed across 201 files). The retained `release-verification-logs-angular` artifact records the failures. Complete Domain/API/E2E and contract/publication jobs were skipped after the Angular failure. Image run **45922282423** on `8ada67a7` resolved the source but skipped both image build and summary jobs. Thus there is no exact-`8ada67a7` full-suite result and no `FULL_CI_PASS`. The five local spec edits were excluded from CI and do not qualify or repair the candidate. No browser/live-provider acceptance is inferred.

No deployment, production migration/data or permission change, live provider mutation, merge, or professional/accounting approval occurred. The assignment is **INCOMPLETE**; no release is authorized by the conditions in this task.

## Production/external dependencies — separate gates

Each row below is `BLOCKED_EXTERNAL` or `BLOCKED_DECISION`; no successful local test, image, or generic capability status satisfies it.

| Dependency | Current evidence | Missing owner/evidence | Deterministic resume condition |
|---|---|---|---|
| Target/source/image identity (M365-019, DEPLOY-003) | No candidate release/production acceptance evidence for this source. | Qualified release owner; approved target and immutable source/image identities. | Approved deployment packet names exact SHA, images and target. |
| Protected environment | No production environment protection/readback supplied. | Deployment owner and protected-environment evidence. | Read back the approved protected environment for the named target. |
| Database/catalog and mutable mounts/config/credential backup | Not observed for this candidate. | Database and operations owner; exact catalog, mounts, overlays, configuration and credential-backup evidence. | Bounded source/target inventory and recoverable backups match the approved target. |
| Independent restore rehearsal | No rehearsal for this candidate/target. | Independent recovery owner and retained result. | Successful isolated restore of the exact backup and mutable state, with evidence. |
| RPO/RTO/maintenance/rollback | No current approval for this candidate. | Qualified service owner’s approved window, RPO/RTO and rollback plan. | Exact target-specific plan is approved and rehearsed. |
| Migrator/runtime roles and recovery epoch | No target-specific role/epoch evidence. | Database/security owner and approved privilege/readback. | Least-privilege migrator/runtime identities and recovery epoch are verified on the exact target. |
| OIDC/session/tenant/resource isolation | No current live acceptance readback. | Identity/security owner and authorized test identities/resources. | Exact-source live isolation checks pass with retained evidence. |
| Mail authorization/sender/control behavior | Offline current-composition tests only; no approved retained Acceptance target or signed current receipt. | Mail/tenant owner, pinned signer, exact current firm/connection/consent and authorized sender/control scope. | Independently signed current evidence is bound to the approved Acceptance target and authorized recipient; no synthetic signature qualifies. |
| Selected-site PBC/archive readback | No live target readback. | Selected-site owner, authorized site and source/target identity. | Exact selected-site source/archive readback is retained and independently checked. |
| Recipient confirmation | No observed delivery acceptance. | Accountable recipient/service owner and approved channel/identity. | Required recipient confirmation is retained; HTTP/provider acceptance alone is insufficient. |

No live test, production migration, deployment, credential rotation, permission grant, email, PBC/archive mutation, external acceptance, or professional/accounting sign-off occurred. Exact current-source CI and all required release gates remain separate.
