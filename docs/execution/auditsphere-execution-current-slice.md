
# AuditSphereOps — Current State & Active Slice Handoff

**Status:** CURRENT

**Purpose:** Compact, authoritative handoff of the active implementation slice, recent verified changes, local environment state, and next actions.

**Authority:** Active execution handoff document. Volatile metrics (exact test counts, migration count, verified commit SHA, CI run IDs) belong exclusively to [`docs/execution/status.json`](status.json).

**Audience:** AI coding agents and human developers.

**Chronology:** Sections are newest first. An older section describes the state
when it was written; where a later section supersedes it, the later section is
current. The top-level `verifiedCommit` in `status.json` names the last commit on
which the full suite was run; per-slice records carry their own evidence.

## Client-site current-authority and recovery fences (2026-10-23)

The existing isolated provider checks current staff/administrator/connection authority and durable ownership before writes and receipt return. Publication binds to the authorized staff-set digest. Proven pre-write failures retain recoverability; possible writes preserve the creation fence. Pending creation polls only the original URL within a bounded window. Focused PostgreSQL/provider tests pass, including uncertain reconciliation in its actual `VERIFYING` claim state. Evidence and counts: `clientSiteAuthorityFence` in `status.json`. WU-021 remains partial: the authenticated terminal intent/status adapter and managed-only residual-access evidence are still pending. A concurrent second PowerShell provider draft is unaccepted and must use the existing Application/worker path. No live effects were performed.

## Development feedback workflow (2026-10-23)

[Debug watch guide](../../src/AuditSphereOps.Ui/auditsphere-angular-development.md#fast-backend-development-loop): scoped launcher, focused checks and synthetic hot-reload/restart pass. Warm compilation skips; SHA changes rebuild assemblies. Evidence: `developmentFeedback20261023` in [status.json](status.json). Configured startup fails the identity-split guard; database readiness, authenticated/CSRF, cold-cache and frontend timing acceptance remain open. No overall speedup claimed.

## Selected SharePoint document resource grants (2026-10-23)

WU-020 provides exact selected-site application write-grant reconciliation and a separate certificate-authenticated DOCUMENTS child probe. Ownership, returned tenant/resource identity, exact roles, durable unknown-outcome reconciliation and permission-ID readback precede acceptance. Runtime proof requires positive site/library-root reads and a known unrelated site's actual HTTP 403; operator permission-list absence is insufficient. Focused offline cohorts passed; counts and source evidence belong in `status.json`. No live tenant connection, document delivery or application activation was verified. Protected key-mount custody and real tenant acceptance remain external gates. Next engineering: WU-021.

## Entra application role grant automation (2026-10-23)

WU-019 now provides application-role reconciliation via `Grant-TenantPermissions.ps1` and tenant enrollment validation via `Test-TenantEnrollment.ps1`. Missing application roles are reconciled with idempotency, signed approval verification, exact capability certificate bindings, and strict drift detection against unapproved role assignments. Offline tests confirm that an app-only token or administrator grant cannot substitute for the human OIDC grantor leg (`BLOCKED_BOOTSTRAP`). Next engineering: WU-020 (selected document resources); live acceptance remains a separate leaf.

## Owned Entra automation (2026-10-23)

WU-018 now has registration and certificate-only entry points with offline planning, signed exact-plan approval, ID/ownership checks, fixed operator journals, explicit uncertain-create reconciliation and public-only key overlap. GET retries are bounded and honor provider waits; writes are not retried. Read-only and Apply source pins are enforced. Focused synthetic checks pass; no live tenant action or provider acceptance is claimed. The [operator procedure](../operations/auditsphere-entra-registration-automation.md) documents prerequisites and recovery. Permission/enrollment engineering is described above; live acceptance remains a separate leaf.

## Offline Microsoft 365 plan boundaries (2026-10-22)

Plan validation now enforces the approved capability permission matrix, separate certificate identities, exact redirect origin and required mail/client-site configuration before provider access. Exchange RBAC plans cannot also request unscoped Graph mail permission. Offline positive/negative checks and the PLANNED envelope check pass. The unfinished provisioning scripts are engineering work; missing live tenant credentials block their live acceptance only. They must not be parked wholesale as external work.

## Completion evidence integrity follow-up (2026-10-22)

The runner captures artifact hashes and byte sizes itself, bounds evidence reads, rejects symlink traversal, and revalidates retained artifacts before any completion exit. Historical checkpoints remain readable but cannot certify completion without retained digests. Re-run or independently inspect the original checks before recording new evidence; never backfill a digest merely to turn the gate green. Focused runner/state results are recorded in `autonomousCompletion20261022` in [status.json](status.json); product and live acceptance remain open.

## Autonomous completion run (2026-10-22)

The adopted [autonomous completion specification](../architecture/auditsphere-autonomous-completion-remediation-spec.md) is in progress; engineering completion is not reached and the current head has no exact-candidate full CI. Run state lives in ignored `.local/autonomous/` and is driven by `scripts/agent/complete.py`, `scripts/agent/ci-status.py` and `scripts/agent/verify-completion-state.py`.

RUN-001 to RUN-008 are verified locally. WU-001 to WU-004, WU-007 and WU-008 are implemented with focused evidence. WU-005 waits for R2R T007 and T013, and WU-006 waits for the pending allocation effective-date policy. Behavioural fixes in this run:

- Role grants and portal first sign-ins can only name a user of their own firm (migration `FirmScopedRoleGrantAndPortalSignInUsers`).
- Graph and SharePoint app-only tokens must be issued to the configured application.
- Production workers compose a live group only when it is listed in `Worker:ApprovedLiveGroups`.
- The client accounting profile command returns 403 for an out-of-scope request.

Open findings for WU-015: 42 command endpoints still return 400 for `scope.denied`, and 63 single-column firm-scoped keys could not be probed without seeded rows. Browser journeys are missing for parity rows T008–T010, T016, T025, T026 and T028. Exact metrics are in `status.json` under `autonomousCompletion20261022`.

## Angular quality and accessibility status (2026-10-22)

The accessible-name fix is in `fcbc77a80334191cf45110a40174d47904c9a18d`; the UI source reviewed is `e93044ade9b24e51300134ca25eaeac1c42dcce7`. Consolidation elimination-journal inputs and repeated remove buttons have line-specific accessible names. Its component spec passed 19/19 and the changed-template lint passed. Independent source review covered the shared shell, journal/evidence, planning, consolidation and fieldwork templates and approved the code with follow-ups. Rendered keyboard scrolling/zoom/reduced-motion coverage is incomplete, and human screen-reader speech/announcement review remains open. The resumed built-in browser check found journal-row removal focus loss; its correction and incomplete matrix coverage are recorded in the latest angularQuality ledger entry.

The earlier E2E failures on `efb9c4bd` were fixed in `7b69f6db`; the changed Playwright methods passed 2/2 and the serialized Release solution build passed. Superseded run detail is preserved in the [historical handoff](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). AC-09 is not human sign-off; see [status.json](status.json).

## Current master verification and scope (2026-10-21)

The latest backend slice binds archive readiness and early compliance-lock evidence only to an issued financial-package release whose candidate matches the engagement scope. A focused PostgreSQL regression creates a later issued workpaper release and verifies that neither the review digest nor persisted lock evidence switches to it. The focused test, serialized Release solution build, current-source fast CI and API/worker/migration image builds passed on `3c7741be`; exact evidence is in [status.json](status.json). The unfiltered release run remains in progress on older source `76969482`, so full current-source verification is not established.

Accessibility remains `PARTIAL_AGENT_REVIEW`, not human screen-reader sign-off. The rendered shell review on `3c7741be` verified skip-link focus, reverse Tab and no horizontal overflow at 390px; shell sources are unchanged through `23b33597`. Source review on `23b33597` found no actionable issue in the ownership form labels, table caption or pagination names, but no authenticated group fixture was available. The browser/API review remained anonymous; no screen reader was used. Qualified human review with AT/browser versions remains open. No business data changed. Details are archived in [status.json](status.json).

Component corrections fail closed, preserving the original pin/hash/submitter; correction needs a reviewed package and new scope. Five focused Domain cases passed; browser acceptance remains pending.

API/Angular parity T008 (accounting group creation) has focused local evidence: overview spec 9/9, Domain grant/revocation cohort 3/3, API authority boundary 1/1. T009 (effective-dated group membership) has Angular 3/3, Domain 1/1 and API 1/1. T010 now uses inclusive membership/reporting-period overlap throughout creation, package/component eligibility, readiness and ownership choices; disjoint future membership is denied and multiple records for one member do not duplicate readiness. Its focused API test, PostgreSQL workspace/package test and earlier overview spec pass. T011’s routing assignment now has HTTP regression coverage for CSRF, cross-client denial and persisted routing details; its API assignment/revocation, Domain and Angular checks pass, with a mutation proving the same-client guard. The production identity has no group access, and the local app is stopped, so both authorized browser journeys remain pending. No production data changed. Exact commands/results are in [status.json](status.json).

T026 exposes approved membership windows and blocks uncovered ownership periods before dispatch; the API enforces the same boundary. Focused API 1/1, Angular 3/3, Angular and .NET Release solution builds, and OpenAPI checks pass. Fast CI `39667293119` passed on `23b33597`; browser acceptance lacks an authorized group fixture. No full suites ran locally.

T017/T018 assignment and sales-credit recovery, plus T019/T024 allocation receipt recovery and controlled unallocation, now have focused Domain and Angular evidence. Exact counts and test sources are in [status.json](status.json). Browser acceptance remains unverified.

T029/T030 portal draft preservation and slow package-build polling passed their two focused Angular specs (49/49). T031 consolidation pagination/refusal passed the filtered Domain class (3/3) and Angular spec (18/18). T032 portal history now exercises 501 uploads and 2,001 messages in the existing API test; the targeted API test passed 1/1 and reaches the deep pages while retaining foreign-request and invalid-page denials. Browser interaction was not run because the local UI/API ports were closed. Focused counts, source equivalence and CI state are recorded under `apiAngularParityFocused20261021` in [status.json](status.json).

Overdue PBC reviewer escalation is already configured. Event notifications need approved semantics; monthly accounting handover is only PROPOSED, not current implementation authority.

## Client finance receipt continuation

Agreement summaries now carry a bounded receipt page with separate continuation. Exact-agreement receipt history preserves full SQL cash totals, current access fences and existing settlement-document lineage. One payment allocated across both milestone invoices retains distinct kind/receipt identities. The Angular component loads additional pages only on demand; errors withdraw cached rows while keeping refresh recovery available.

Focused PostgreSQL, Angular and authenticated browser checks pass. The browser proves receipt and agreement paging remain independent, exact monetary cells retain full totals, and keyboard/mobile behavior is preserved. Built-in inspection confirms the older receipt page and focus without console errors. Evidence, intermediate repairs and limits: `portalReceiptPaging20261021` in [status.json](status.json). Whole-product, human AT, full current-source release and provider/professional acceptance remain open. Prior agreement-history evidence is in the [historical handoff](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).

## Paged client document history

The portal now combines retained document sharing/acknowledgement, associated signed-scan upload, bundle assembly and current-account bundle receipt events into a read-only paged history. It preserves exact fractional-time ordering and links to the existing source cards. Internal work and identities stay private; delivery, signature verification and professional approval are not inferred. Unorderable timestamps remain visibly unresolved.

The real browser journey caught and repaired focus loss; built-in inspection also caught base-URL fragment resolution. Source links now retain the portal route and keyboard destination, with stale navigation guards and long-text reflow. Focused evidence, intermediate failures and repairs, final artifacts and CI scope are under `portalDocumentHistory20261021` in [status.json](status.json). Complete commercial/PBC chronology, human screen-reader review, current-source full release and professional/live acceptance remain open. Earlier template notes are preserved in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).

## Separate client upload and reply capabilities

Home and detail now use independent server-projected upload and reply eligibility from the existing command rules. Clients can reply during review, respond to a clarification before replacing files, and upload after resubmission. Missing capability fields fail closed; legacy `CanWrite` remains compatibility data. Home rechecks current window, scope and request state/revision before releasing action hints. Changed capabilities clear local drafts while ordinary polling preserves them.

The real synthetic browser journey proves the clarification/reply/replacement sequence without turning staging into acceptance. Built-in narrow-screen inspection caught and repaired long-message URL overflow. Exact source, focused results, test repairs, screenshots and CI limits are under `portalRequestCapabilities20261021` in [status.json](status.json). Earlier template-picker evidence is in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). Whole-tool, human AT, full release and professional/live-provider acceptance remain open.


## Recent read-only evidence views

Period evidence and management-only communication history are recorded under `accountingPeriodEvidence20261021` and `communicationHistory20261021` in [status.json](status.json), with their full handoffs preserved in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). Professional policies, client-public chronology, human AT and full release acceptance remain open.

## Accessibility recheck and current-SHA release verification (2026-10-21)

Authenticated synthetic PBC checks cover mobile reflow, named controls, skip-link/navigation focus and unsent replies. Touched invalid replies, including whitespace-only text, expose `aria-invalid` and reference their error through `aria-describedby`; correction clears both. Evidence is in [`accessibilityRenderedSweep20261020`](status.json). Full state coverage and qualified human AT acceptance remain open.

The preceding release exposed a test race: recovery renders before the intercepted command completes. The workpaper journey now awaits dispatch before database proof or a superseding save, unwraps the API `value` envelope and propagates callback failures. Earlier focused passes did not prove asynchronous assertions finished. Exact receipt, content, revision and duplicate-write protections remain enforced.

The two repaired portal journeys passed focused E2E 2/2; the focused accessibility cohort passed 3/3 on its recorded source, and the .NET Release solution build passed on `e4dfef25` with 0 warnings/errors. Exact-source fast CI passed. The unfiltered release workflow remains active on an older SHA; no full suites ran locally. The current agent source review found no actionable shell/navigation issue. The current local app ports refused connections, so authenticated browser checks could not be repeated. Browser limits and the pending human screen-reader sign-off are recorded under [`accessibilityReview20261021`](status.json); current CI evidence is under [`currentSourceCi20261021`](status.json).

> **Reading note:** Any older section that says the Web host remains present or that
> retirement is `NOT_READY` describes the state when it was written. The owner retired
> the host and merged that removal to `master` as `e7ebe27c`; the current tree contains
> only Angular.

## Earlier work-table accessibility evidence

The keyboard scrolling, reduced-motion and viewport follow-ups are preserved verbatim in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md), with scoped evidence in [status.json](status.json). Human screen-reader and full current-source release acceptance remain open.

## Versioned workflow task groups and accessibility focus (2026-10-20)

Full browser, keyboard, focused-test and CI narrative moved to the [historical handoff](auditsphere-execution-handoff-history-2026-10-historical.md). Current evidence and open gates remain in `status.json` under `targetedVerification.ste22F07WorkflowPhaseCues` and `targetedVerification.accessibilityRenderedSweep20261020`.

## Exact deadline-task handoff and reconciliation writer fences

Task reminders now open their exact engagement-scoped task, including unassigned work absent from My tasks. Selection is context-bound; actions require matching server queue permissions and revision, and mutations refresh both views. Reconciliation writers serialize with period close and exact-proof approval; approved matching proof append retains the reviewed schedule, and caller-owned transactions are preserved. Focused API, Angular, PostgreSQL, responsive and built-in browser evidence, repaired failures and exact source identities are in `deadlineExactTask20261019` and `reconciliationWriterFences20261019` in [status.json](status.json). Bounded fair reminder evidence is archived with its live pointer. The broader rule catalogue, recurring accounting work-pack acceptance, human AT, professional/provider gates and full current-source release remain open.

## Role-dialog keyboard boundary check and milestone atomicity (2026-10-19)

Scoped keyboard/atomicity evidence and rollout limits are retained in [status.json](status.json) and the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md). Human AT, broader payment concurrency and full release acceptance remain open.

## Milestone invoice source identity (2026-10-19)

Full agreement invoice identity, retained legacy recovery and long-heading evidence are in `targetedVerification.feeInvoiceIdentity20261019` in [status.json](status.json) and the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md). Broader payment/source acceptance remains open.

## Scope-bound workflow receipt recovery (2026-10-19)

Focused evidence and remaining limits are retained in `targetedVerification.workflowEvidenceRecoveryScope20261019` in [status.json](status.json) and the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md).

## Time workspace grant expiry (2026-10-19)

Focused evidence and remaining limits are retained in `targetedVerification.timeWorkspaceGrantExpiry20261019` in [status.json](status.json) and the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md).

## Portal finance and CI wait checks (2026-10-19)

Scoped portal keyboard and CI-wait evidence is retained in the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md) and [status.json](status.json). Broader accessibility and release acceptance remain open.

## Locked upload intent freshness (2026-10-19)

Resume, chunk recording and trusted completion refresh the already locked upload intent before reading token, revision or received state. Existing regressions reproduced stale revision and stale staged-state receipts, then passed after the fix. The original delegation race test also passes with a retained valid token denied after revocation. Exact source, rebuilt API evidence and limits are in `targetedVerification.uploadIntentFreshness20261019` in [status.json](status.json). This corrects reused service contexts; no current HTTP context-reuse path was observed. Whole-family state acceptance, current-source full release and external/professional gates remain open.

## Practice completion safeguards (2026-10-19)

Earlier archive/restore focus, task-receipt lookup, staff-cost ambiguity and portal delegation safeguards are retained in the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md) and their [status.json](status.json) records. Broader state and release gates remain open.

## Earlier workflow and consolidation checks (2026-10-19)

Reviewed operational reopening and approved-component keyboard evidence are retained in the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md) and their scoped records in [status.json](status.json). Broader workflow, human accessibility and full release gates remain open.

## Atomic correspondence dispatch claim (2026-10-19)

The shared recipient/dispatch firm fence ends before provider I/O. Exact source and concurrency checks are retained in [status.json](status.json) and the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md); this does not serialize every professional fact writer.

## Operational cancellation and recovery (2026-10-19)

Original cancellation evidence is retained in the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md) and archived status record.

## Browser CI recovery and search interaction (2026-10-19)

Focused repairs and retained diagnostics are in the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md) and [status.json](status.json). Full API/E2E acceptance and the earlier runner shutdown/hang causes remain unproven.

## Earlier accessibility and CI repair chronology (2026-10-19)

Detailed implementation history moved verbatim to the [historical current-slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). Human assistive-technology review and complete supported-state acceptance remain open.

## Default API Microsoft 365 credential isolation (2026-10-18)

The base Hostinger Compose definition now mounts the API's six allowed capability directories individually and read-only. It excludes the parent credential directory and the privileged client-sites certificate, preserves the three data mounts and leaves all other service settings unchanged. Missing capability host directories fail closed; operators must prepare protected empty directories for disabled capabilities before reviewed recreation. Existing containers retain their previous mounts until recreated and inspected. The compatibility override remains available for older source revisions; the CLI runbook and configuration reference explain the upgrade sequence.

Dummy-input base and override Compose comparisons and documentation checks pass. Actual process visibility is not verified because the local Docker daemon is unavailable; no VPS services, tenant permissions or credentials were changed. The published documentation source is confirmed on remote master with successful fast CI; full release verification and live mount/provider acceptance remain separate. Exact evidence is in `m365ClientTenantGuide` in [status.json](status.json).

## Customer tenant Microsoft 365 operator documentation (2026-10-18)

The [client-tenant CLI runbook](../operations/auditsphere-operations-runbook-client-tenant-microsoft365-current.md) now covers registrations, consent, certificate/secret custody, first administrator, exact SharePoint grants, dedicated-site rollout, Exchange sender scope, mail/DNS alternatives, rotation, recovery and a private AI-agent handoff checklist. The [configuration reference](../operations/auditsphere-operations-reference-microsoft365-configuration-current.md) maps the complete base Compose input set and additional runtime/session/signing/provider settings to source owners. The optional isolation override example preserves data mounts and replaces the API parent credential mount with narrow capability mounts; it is not automatically deployed. The existing operator Wiki pages are published with the CLI procedure, configuration inventory and retained signing guidance; the Wiki revision is recorded in the execution ledger.

Shell/PowerShell/Python syntax, input inventory, dummy-input Compose merges and documentation checks are recorded under `m365ClientTenantGuide` in [status.json](status.json). Customer/provider commands were not executed, no live settings were changed, and no application regression was run for this documentation work. Production live-worker composition, missing Azure credential/session wiring and integrated Exchange RBAC verification remain engineering/release gates; SMTP/Resend attachment limitations and privileged-mount prerequisites are explicit. Documentation completion promotes no professional, task-card or tenant-acceptance state.

## Documentation reconciliation revalidation (2026-10-18)

The documentation reconciliation report was revalidated against `master@d4bbef4d`; PR #46 and the companion PR #47 are already merged. The operative index now keeps Tasks 01–09 at technical-evidence-only, Task 10 `BLOCKED_DECISION`, and Task 11 `PARTIAL`. No task-card, professional-approval or external-gate status was promoted. The R2R index's two links to repository architecture guidance now target the canonical `master` files so the extracted task pack remains internally valid.

Source integrity, Markdown health, naming, narrative-metric, task-pack, generated-board and diff checks passed. Fast CI `39134304836` passed on `d4bbef4d`; it predates this documentation refresh and is not full regression evidence. Full release run `39132911253` is pinned to `f5f96d9e`; Domain failed and API/E2E were still running at the 14:15 UTC observation. No application tests were run locally. See [the reconciliation report](auditsphere-execution-report-documentation-reconciliation-current.md) and [`status.json`](status.json) for the exact record.

The earlier reversible client archive slice and its historical CI limits are retained verbatim in [the historical handoff](auditsphere-execution-handoff-history-2026-10-historical.md#reversible-client-archive-history-2026-10-18).

The earlier firm receivables query slice and historical CI observations remain intact in [the historical handoff](auditsphere-execution-handoff-history-2026-10-historical.md#firm-receivables-age-all-posted-invoice-categories-2026-10-18).

## Client relationship dates and retained history (2026-10-18)

Commit `05406665c6d7e8ba1413b189d00d1cc1d5d12ba0` preserves revoked client relationships in the authorized hierarchy and reports `ACTIVE`, `FUTURE`, `EXPIRED`, or `REVOKED` using the current UTC date and the stored effective period. The legal-entity panel now displays the existing registration number and jurisdiction alongside legal name, tax identifier and entity type. Counterpart details remain omitted unless the actor has current authorization for both client records.

Focused evidence on this source: the PostgreSQL Domain tests `OrganizationRelationships_BuildsHierarchy_AndEnforcesCycleAndBoundaryInvariants` and `OrganizationRelationships_PreservesEffectivePeriodAndProjectsCurrentStatus` passed 2/2; the Angular `client-relationships.spec.ts` passed 8/8; `bash scripts/contracts/verify-openapi.sh` passed; and the Angular production build passed with the existing `commercial/settings.scss` component-style budget warning (7.53 kB against 4.00 kB). Built-in-browser verification remains **NOT_VERIFIED** because the existing port 5029 tabs render only the workspace shell with no route content; I left that process untouched. No client records or live provider settings were changed.

ASPM-CRM-02 remains PARTIAL: the product has no established client archival lifecycle or searchable archived-client history. Its direct fast CI run `39118460754` was cancelled after a newer commit; run `39119098499` failed OpenAPI contract drift on `954a623f` because `effectiveStatus` was missing from the generated contract. The contract was regenerated in `9fad72d0`; run `39119821556` passed on that parent SHA, and `39120222007` passed on `1f4e5430`. Full release run `39117606930` is pinned to `9f15c482`; its Domain job recorded the two failures described above and the run was cancelled before API/E2E completion. Detailed evidence is in [`status.json`](status.json#targetedVerification.aspmCrm02RelationshipHistory20261018). The previous lead-history slice's fast CI passed on commit `6819612a81c902bd278a1842b883e741b084edce` (run `39117697497`); its current browser acceptance remains unverified.

## QAR 20,000 commercial fee acceptance fixture (2026-10-18)

The existing PostgreSQL commercial-cycle regression now uses the commissioned QAR 20,000 audit-fee example: the approved agreement creates QAR 10,000 advance and balance obligations; a QAR 4,000 allocation leaves QAR 6,000 outstanding and `AdvanceCurrentlyPaidAsync` false; a separate QAR 6,000 allocation clears the advance gate. It continues through finance review/posting, exact-offer acceptance, idempotent payment references, partial balance settlement, reversal/recovery, and retained receipts/vouchers. This is a focused service/database acceptance check, not browser acceptance of activation or full product completion.

Focused verification: `dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~AgreedFee_AdvanceReceiptEmailAndBalance_FollowTheFiftyFiftyCycleOnceEach'` passed 1/1 on source commit `954a623f799b0871afc85937d43267726d74ee7c`. The QAR 20,000 / 4,000 / 6,000 fixture therefore corrects the amount assertion that failed in old full run `39117606930` on `9f15c482`. Fast CI run `39119098499` failed on the missing `effectiveStatus` OpenAPI schema; `9fad72d0` regenerated it, and fast runs `39119821556` and `39120222007` passed on `9fad72d0` and `1f4e5430`. The older full release Domain job recorded 1,368 passed and two failed; I cancelled the stale run before its API and Playwright E2E jobs completed. Detailed evidence is archived at [`status-archive-2026-10.json`](status-archive-2026-10.json#keys.targetedVerification.aspmCom03QarAcceptance20261018).

## Retained lead action history (2026-10-18)

ASPM-CRM-01 now retains append-only lead activity for creation, qualification, opportunity creation, proposal revision/review/marking as sent, acceptance or decline, and the exact client-conversion link. Each event is written with its business command; idempotent retries do not add duplicate events. The lead workspace projects source, current owner and creation time, plus a stable chronological history page capped at 200 events. Authorized staff can request older history with a lead-bound keyset cursor.

Existing leads receive only a clearly marked metadata backfill for source, creation time and owner. It does not invent historical actors or actions, and the workspace identifies this legacy-history gap. The criterion remains PARTIAL because historical commercial actions cannot be reconstructed and manual browser acceptance is unavailable.

Focused evidence on this source: the PostgreSQL Domain cohort passed 4/4, the Angular decoder/workspace spec passed 5/5, the API Release/OpenAPI emitter build passed with zero warnings and errors, OpenAPI drift and EF model-drift checks passed, and the Angular production build passed with the existing `commercial/settings.scss` budget warning (7.53 kB against 4.00 kB). Documentation checks and exact source SHA are recorded in [`status.json`](status.json#targetedVerification.aspmCrmLeadHistory20261018).

Built-in-browser acceptance is **NOT_VERIFIED**. The existing API on port 5029 still serves a blank main area and the browser console reports a missing hashed Angular chunk; I left that long-running process untouched. No live provider, client data, professional decision or production gate was changed or accepted. The full release workflow on the preceding source commit `de8b6655d250867262550e998cd6d5fe46388977` is not a pass: its Domain job failed while API and Playwright jobs remained in progress at the last observation. Current-source CI is still required.

## Pinned workflow task designation in queues (2026-10-18)

Commit `e4c5fa5f6def0077662b784650ac8d66f53ba956` projects each templated task's Required/Optional designation from its exact firm, pinned template revision and step. Manual tasks return no designation. The Angular My Work list, board and calendar show the value as plain text. It is informational only and does not gate task completion, lifecycle transitions or professional review.

Focused PostgreSQL coverage passed 1/1 in `EngagementWorkflowPlanTests.PlanPinsPublishedTemplateAndSchedule_AndRecoversRetryWithoutDuplicateTasks`; it verifies the old plan still reports Required/Optional after a later draft revision. The focused `work-queue.spec.ts` passed 18/18, including all three layouts and manual tasks. The OpenAPI emitter Release build passed with 0 warnings/errors, the generated contract is current, and the Angular production build passed with its existing `commercial/settings.scss` budget warning (7.53 kB against 4.00 kB). Fast CI run `39113115375` passed on the preceding source commit `aad8c8de`; full release verification run `39113751546` was dispatched against this exact source commit.

Built-in browser smoke check on the current Angular dev server at port 4301 used the existing synthetic API at port 5029. The queue rendered its manual synthetic task without a Required/Optional label. That API process predates the new response field, so positive template-backed labels were not browser-verified; they are covered by the Angular spec. No business records were changed. ASPM-WF-02 remains PARTIAL: required-task graph semantics, broader role/accessibility acceptance and full release verification remain open.

## Client portal invoice settlement projection (2026-10-18)

ASPM-COM-06 remains `FOCUSED_PASS_BROWSER_SCOPE_LIMITED`: the focused server and Angular evidence is retained, but the client-role browser workflow and invoice-specific provider delivery receipt remain unverified. The detailed implementation packet and its constraints are preserved in the [historical handoff](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md); current check status is in [`status.json`](status.json#targetedVerification.clientPortalInvoiceSettlement20261018).

## Certification revocation versus reviewed staffing (2026-10-18)

Added a deterministic PostgreSQL interleaving regression on source commit `8a01261066f5e105bd993ac999a61b840d2cff29`. The test pauses an independent certification revocation while it holds the firm publication lock, observes a previously reviewed higher-level assignment waiting on that lock, lets the revocation commit, and confirms the assignment is refused with no new assignment, staffing receipt or staffing grant. Its exact result and CI history are retained in [`status.json`](status.json#targetedVerification.staffingCertificationRevocationRace20261018) and the referenced status archive.

ASPM-RES-02 remains PARTIAL: broader resource and release acceptance are still open. This backend transaction-ordering change has no new UI behavior; earlier synthetic-browser evidence for the certification status display remains scoped to that display only.

## Recorded resource availability in the selected range (2026-10-18)

The resource grid now returns and displays the exact saved leave, training and public-holiday entries that overlap its selected weeks, per staff member. Each entry shows its kind, inclusive start/end dates and recorded minutes per day. The projection exposes no private reason text and adds no inferred approval state. Existing aggregate capacity arithmetic and the firm-wide resource authorization boundary are unchanged.

The source API response, generated OpenAPI schema, selected-range decoder, and focused Domain/Angular/browser checks cover the new projection. Exact commands and counts are recorded in [`status.json`](status.json#targetedVerification.resourceAvailabilityProjection20261018). Fast CI run `39109639455` passed on the exact source commit; it is not the full release regression suite. The existing local browser server on port 5029 still serves a shell whose route content is blank; that manual check remains blocked by the stale local host rather than treated as acceptance. ASPM-RES-01 remains PARTIAL pending broad role, range-boundary, responsive/accessibility and current release acceptance.

## Phase- and risk-attributed resource allocations (2026-10-18)

Resource allocations now preserve separate planned-hour lines for each engagement, staff member, Sunday-based week, audit phase and optional risk area. The reviewed preview, request hash, durable receipt and PostgreSQL uniqueness/receipt guards bind the exact line. Different phase/risk lines coexist and all contribute to the weekly capacity total. Historical rows backfill to visible `UNASSIGNED` attribution, remain in capacity totals, and can be removed only through a reviewed zero-minute action for that exact legacy line; new positive allocations require an assignable phase. The Angular form requires the phase, normalizes a selected date to Sunday, and displays phase/risk on saved lines. Existing staffing and enabled-staff authorization checks remain in force.

Focused checks on source commit `5996f36d49bf89feba9c99a7fab7c567c5f41c95`: PostgreSQL Domain regression 1/1 (two lines, aggregate capacity, exact-line removal and retry); resource Angular spec 15/15; resource E2E cohort 4/4; API and E2E Release builds with 0 warnings/errors; EF model drift and OpenAPI checks pass. Angular production build passes with the existing `commercial/settings.scss` warning (7.53 kB vs 4.00 kB). Fast CI run `39108092465` was in progress when this handoff was written; release-wide suites remain CI/CD work.

Built-in browser inspection reached the existing local server on port 5029, where the Resources navigation selected but the workspace stayed blank because the stale shell referenced missing lazy chunk `chunk-Eamlq2bo.js`. I left that running process untouched, so the current source UI was not manually browser-verified. Broader date-boundary, role, responsive/accessibility acceptance and full current-SHA CI remain open. The focused allocation evidence is in `status.json` at `targetedVerification.practiceResourcePhaseRisk20261018`; the earlier eligibility-only record is preserved in the status archive.

## Correspondence contact validity and proposal send-boundary recheck (2026-10-18)

Purpose-specific recipient resolution excludes contacts whose validity interval does not overlap the requested UTC date, including explicitly selected overrides. Dispatch receipt creation checks the contact again at the recorded dispatch instant and refuses inactive, future-dated, or expired contacts. A new worker guard at source commit `395d2ef06e53e7c21aece54f8d85f78c482a9514` rechecks proposal recipients immediately before provider send: prospects must retain the same primary email and remain unconverted; client proposals must still resolve to the exact captured contact, routing row and email. Changed or incomplete authority blocks the durable operation before the provider call; the notification remains `QUEUED` for review rather than being silently redirected.

`CommercialPricingPolicyTests.PendingProposalMail_BlocksWhenProspectRecipientChangesBeforeProviderSend` and `PendingProposalMail_BlocksWhenClientCommercialRouteIsRevokedBeforeProviderSend` pass against PostgreSQL on that commit. Exact metadata is in `status.json` under `targetedVerification.practiceProposalSendRecipientAuthority20261018`. This covers proposal notifications only. Fee receipts/vouchers and holding letters still lack this recheck, the dispatch does not retain the initiating actor for current-role reauthorization, and the check is not serialized against a routing mutation that races after the pre-send query. DEC-09, live provider acceptance and integrated browser acceptance remain open, so ASPM-CRM-03 stays partial.

## Practice-management and workflow bounds (2026-10-18)

Resource allocation eligibility, time-source billing reservation guards against reserved revision edits, and workflow dependency graph bounds (500-task / 4,000-edge limit) were verified with focused PostgreSQL regressions on commits `5996f36d` and `5e9a1eb5`. Exact test evidence is preserved in `status.json` (`targetedVerification.practiceResourceEligibility20261018`, `practiceResourcePhaseRisk20261018`, `workflowDependencyGraphLimit20261018`).

## SharePoint client sites and managed staff reconciliation (2026-10-23)

WU-021 automates modern nongroup SharePoint client site provisioning and managed staff Full Control lifecycle:
- `Configure-SharePointClientSites.ps1`: Derives deterministic client site URLs (`/sites/auditsphere-client-{clientId:N}`), enforces `AuditSphere:{firmId}:{clientId:D}` on modern `STS#3` sites, enforces rollout cutoff guards (pre-cutoff clients require explicit migration approval), validates active status via SPSiteManager, and guarantees isolated DOCUMENTS worker write grant on site.
- `Reconcile-ClientSiteStaff.ps1`: Manages the dedicated `"AuditSphere Assigned Staff"` group, binds Full Control (role type 5), removes obsolete group members, and adds authorized staff members with directory UPN/AadObjectId verification. Client users are strictly excluded from staff Full Control.
- Offline and synthetic verification: 36/36 tests pass in `scripts/m365/tests/ClientSites.Tests.ps1`, including dry-run envelope verification (`verify-result.py --expect PLANNED`), pre-cutoff refusal, idempotent re-apply (0 mutations), and detached CMS approval checks.
- Live tenant execution and operator credentials remain external gates (`GATE-M365-LIVE-TARGET`).

## Engagement workflow phase cues (2026-10-18)

The engagement workflow now independently reads the scoped saved Audit Plan milestone endpoint and displays fieldwork start in phase 3, report and filing dates in phase 6, and archive deadline in phase 7. Dates are displayed only when a saved plan exists; an explicit empty response remains empty, a role-denied response is reported as unavailable, and session/route changes clear cached dates. The panel states that a filing cutoff is not independently validated against jurisdictional law and keeps operational task deadlines separate. The existing plan/configuration page remains the source of the dates; no legal deadline calculator or approval was introduced.

Focused Angular workflow spec passes 3/3, including the populated schedule, explicit no-plan state and 403 denial state; the production build passes with only the existing `commercial/settings.scss` component-style budget warning. Built-in browser read-only verification on the local synthetic engagement observed HTTP 200 for its milestone, workflow and task reads, the no-saved-plan message, and no console errors or warnings. No business data was changed. Exact source commit and current checks are recorded under `targetedVerification.ste22F07WorkflowPhaseCues` in `status.json`. The feature remains partial pending an approved/versioned jurisdictional source mapping, legal validation, broader role/responsive/accessibility acceptance and current CI/CD.

The seven-phase engagement projection now shows each phase's nearest due date from its current, open or in-progress workflow tasks. The UI labels these as task deadlines; they are not statutory milestones and do not satisfy readiness gates. Canonical next-action links are now projected only when the caller has the responsible-role grant and, for staffed audit roles, the matching active engagement assignment. Finance Manager links also require firm-wide scope. A focused PostgreSQL regression covers nearest-date selection, task completion leaving the lifecycle blocked, and authorized versus denied roles. The Angular decoder spec, production build, API Release build and OpenAPI drift check pass; the production build retains the existing commercial stylesheet budget warning. The built-in browser rendered the local synthetic engagement, its blocked lifecycle, task/gate separation, and the scoped Commercial Manager action. The sample had no active dated tasks, so the date label itself was not exercised there; responsive and full role-by-role browser acceptance remain open. No business records were changed. Exact source and verification details are in the current execution tracker and the practice-management criterion audit.

## Practice-management ledger-boundary regression (2026-10-18)

NR-11 is complete for the named three-book and reporting cases. Firm-ledger posting and the actual firm billing-invoice post leave client books unchanged; a client sales-invoice post creates no firm journal/posting and leaves the firm analytics aggregate unchanged; and the approved consolidation workflow preserves exact component TB rows, imported GL, native-journal evidence and package manifests. The original PostgreSQL cohort passed 3/3 on `a67b296f`, and the new firm-billing/client-report extensions passed 2/2 in the current focused run.

NR-12 remains partial. The crosswalk now maps five v2.2/host switches to existing off-path API and worker tests, their retained-state boundaries, and uncovered UI/configuration checks. The Angular work-queue spec also pins the explicit 403 unavailable message and confirms denied responses do not render summary/task cards (2/2 tests passed on the current slice). Built-in browser opened `/app/work`, but the local API currently has workflow tasks enabled and returned its normal view with one synthetic task; the route was read-only and produced no console errors or warnings. Therefore the disabled route itself was not browser-verified. The matrix is evidence mapping, not a fresh full regression or deployed-configuration acceptance. PM-01 remains gated on an approved, versioned jurisdictional source mapping; saved Audit Plan milestones and operational task deadlines are separate projections.

## Practice time return and resubmission (2026-10-18)

RES-04 now has an independent return path with a mandatory reviewer reason. A returned entry can be resubmitted as a new review round on the same time-entry identity; its captured rate and billing classification remain unchanged. PostgreSQL retains submission, return and approval events append-only, and unapproved/returned time remains outside billable WIP. The current Angular time workspace displays the latest return reason and supports resubmission.

The previous schema did not retain the submitter for historical rows. The migration records known legacy submission timestamps as explicitly unattributed and backfills an approval actor only where the prior row stored one. It does not invent historical attribution. Downgrade refuses after any review event is retained.

The focused PostgreSQL workflow, immutable-history and migration-refusal regression passes. The Angular return/resubmission spec, API Release build, EF model-drift check and OpenAPI drift check pass; the Angular production build passes with its existing commercial stylesheet budget warning. Exact commands, counts, commit and warning details are in `status.json` under `completePracticeManagement.returnedTimeWorkflow`.

Built-in browser verification confirms the clean preview serves the current Angular shell and its fingerprinted assets without the old missing-chunk error. That preview returned 401 for `/api/ui/session`, so authenticated time-workspace behavior remains unverified. The already-running local instance on port 5029 still serves a stale bundle; it was left untouched. No business records were changed. Full CI and authenticated browser acceptance remain pending.

## Accounting and browser-journey repair (2026-10-18)

Commit `d5c5ca7ee436c86647c18115acae063a299273c1` fixes the saved purchase-invoice draft preview path: the preparer can supply and validate the approved AP role ID required by preview, and stale preview/assent state is cleared when the role or draft changes. The purchase-invoice draft query now uses a SQL-translatable latest-draft predicate. Search suggestions remain closed while loading, after results, or on errors; long audit-record textareas are constrained to their workspace. Several browser journeys and fixtures were aligned with the current accessible labels and current workflow contracts, without skipping tests.

Focused verification on the pushed source passed: purchase-invoice workflow Angular spec (9/9), workspace search Angular spec (4/4), Angular production build (existing `commercial/settings.scss` stylesheet budget warning), the sales invoice/supplier purchase-credit browser journey (1/1), and General Ledger acceptance journeys (2/2). The built-in browser smoke check used the current Angular dev server on port 4301 with the existing local API on port 5029 and synthetic data: workspace search returned two authorized Doha results, showed no overlapping suggestion list, and reported no console errors. It was read-only; no business records or access settings were changed. The long-running API/static bundle on port 5029 was left untouched.

The full unfiltered release workflow was dispatched against the exact pushed SHA; its run and result are recorded in `status.json`. This focused evidence does not replace full release verification or live-provider acceptance.

## PBC exact-version reviewer acceptance

Staff acceptance now requires the assigned, independently authorized reviewer to confirm the exact provider-verified upload ID, revision and content hash. The accepted request stores an immutable receipt binding that source to its request submission revision, provider identity/digest, reviewer and time. Database constraints validate the receipt against the request, received upload and completed transfer operation, and prevent rewriting the accepted decision or source. Legacy upload rows remain readable but cannot be accepted without matching current provider evidence.

Focused PBC Domain and PostgreSQL guard regressions passed, as did the PBC Angular contract spec; the API Release build and OpenAPI contract drift passed; Angular production build passed with the existing `settings.scss` budget warning. Commit `4bf84641` updates two PBC browser journeys to the current accessible control labels; both selected E2E journeys passed on that commit (2/2), including exact-version reviewer assent, receipt persistence and grant-revocation denial. Fast CI run `39093158735` passed on the same SHA: documentation health, Angular build/specs, .NET Release, architecture guards, EF drift, publication and OpenAPI. The built-in browser still has a stale local PBC bundle (lazy chunk 404), so manual browser acceptance remains unverified. Full release verification is not green: the earlier run on `9e2709b4` had Domain pass, API runner communication loss and E2E timeout with 24 failures before cancellation. Live-provider/business acceptance remains external. Exact evidence is in `status.json` under `targetedVerification.pbcExactVersionAcceptance20261018` and `testSuites.latestAttempt`.

## API and Angular bidirectional parity — active implementation

The [parity implementation tracker](auditsphere-execution-tracker-api-angular-parity-current.md) covers the attached API/Angular specification independently of the R2R and audit-programme task identifiers. Current source contains the authorization, decimal precision, workflow entry, recovery, supplier selection, portal refresh/history and consolidation ownership slices. Code presence is not final functional acceptance; current checks and remaining tasks are recorded under `apiAngularParity` in [status.json](status.json).

The allocation-reversal history route and correction handoff are connected. Review corrections restore API startup/contract generation, forward independent history pages, preserve exact normalized source ordering, clear scope-specific forms and review assent, and allow independent approval after reconciliation. Intercompany option-bound commands require reporting-period membership and reject negative revision sentinels. The ownership query now applies filtering/paging before its final DTO projection. That initial oversized-readiness limitation is superseded by the bounded scope continuation above: components and readiness have independent pages and complete counts. The existing ConsolidationQueryTests source exercises 501 members across six readiness pages; this reconciliation is static inspection, not a fresh test pass or full workflow acceptance.

Focused API, Domain and Angular checks passed for the affected slices; the emitted contract and drift check passed. Built-in browser smoke verified the synthetic overview, scope, external-pack history/source lines and recovered ownership panel. This was read-only browser coverage, not the complete submission/review/pinning journey or responsive/live-provider acceptance. Exact code snapshot, cohorts and limitations are under `apiAngularParity.localVerification` in [status.json](status.json). PR #46 and #47 are merged. Task statuses retain their remaining acceptance work.

GL upload checkpoint and earlier CI observations are preserved in the [historical handoff](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). Current accessibility and release evidence remains above and in [status.json](status.json).

> **Archive:** older dated handoff sections are retained verbatim in [`auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md`](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). They are historical evidence; consult `status.json` and current authority documents for present state.

## Recent focused evidence

Reminder bounds, template prerequisites, quotation timelines, practice-time phases and firm-report/finding recovery are preserved in the [historical handoff](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md) and their keyed records in [status.json](status.json). Full current-source release and applicable human acceptance remain open.
