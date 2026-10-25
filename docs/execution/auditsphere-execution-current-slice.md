
# AuditSphereOps — Current State & Active Slice Handoff

**Status:** CURRENT

**Purpose:** Compact, authoritative handoff of the active implementation slice, recent verified changes, local environment state, and next actions.

**Authority:** Active execution handoff document. Volatile metrics (exact test counts, migration count, verified commit SHA, CI run IDs) belong exclusively to [`docs/execution/status.json`](status.json).

**Audience:** AI coding agents and human developers.

**Chronology:** Sections are newest first. An older section describes the state
when it was written; where a later section supersedes it, the later section is
current. The top-level `verifiedCommit` in `status.json` is a historical baseline;
newer exact-source CI and slice evidence live under `targetedVerification`.

## SharePoint-primary working-document implementation (2026-10-25)

The owner replaced the earlier storage specification with the [functional-parity revision](../architecture/auditsphere-sharepoint-primary-storage-functional-parity-spec-proposed.md); ADR-0019 was rewritten to that storage-only scope. SP-02 to SP-06 are implemented as engineering slices: transfer registration and its migration, exact-version reads behind the existing PBC download and accepted schedule-source consumers, gated staging release, and verified backfill. Remote reads and cleanup stay off by default; no tenant operation, deployment, activation or deletion occurred. The [execution handoff](auditsphere-execution-sharepoint-primary-storage-current.md) lists the remaining owner decisions and pilot gates; focused results are under `targetedVerification.sharePointPrimary`.

## Exact full E2E failure triage and accounting profile assent (2026-10-25)

A recent full verification exposed three E2E findings. After an acknowledged immutable accounting-profile revision, the UI kept the prior revision's assent checked until asynchronous refresh; it now clears assent immediately. A completion-screen test flattened semantic `<dl>` content into text the page does not render; it now verifies the term and exact value separately. The revoked-scope planning-state response timeout did not reproduce in the focused rerun, so its cause remains unproven and the denial assertion is unchanged. Focused Playwright verification passed for the corrected journeys; no full local suite ran. The first follow-up fast gate also found volatile run details in the protected README narrative. Those details were moved out of the README and the local documentation validators now pass. A full run started on that README-invalid source was superseded; corrected-source verification is running. Exact run IDs, counts and job outcomes are recorded only in `status.json`.

## WU-039 focused synthetic journeys and documentation-budget repair (2026-10-25)

The focused Playwright work covers lead recovery, proposal review and client response, conversion receipt recovery, approved rates with FSLI-linked billable time, PBC-to-fieldwork reviewer return, and a same-request staff/client/worker/reviewer handoff. The new PBC case creates the request in the staff inbox, uploads its file through the client portal, verifies the upload intent/chunk and hash, transfers it through the durable worker operation, and confirms the independent reviewer receipt. Its named method passed locally; the prior seven-case cohort passed on its recorded source, but the combined expanded cohort has not been rerun on this candidate. These synthetic journeys do not establish one commercial-to-archive identity lineage. A docs-only fast run found the status ledger just over its size limit; the redundant pointer to history already retained in the archive was removed and the documentation health check passes locally. Exact run states and boundaries are in `targetedVerification.wu039ConnectedJourney20261025` in `status.json`. The exact-source release verification has been dispatched for commit `940ff390`; it resolved the requested source and is queued. Broader planning, reporting, firm ledger, archive, WU-012/WU-014, professional and live acceptance remain open.

## T058 subsequent-match precision guard (2026-10-25)

The subsequent-match service previously rounded a request to six decimal places before comparing it with a sampled balance, so `900.0000004` could be classified as a full match to `900.000000`. It now rejects amounts that change at the ledger's six-place scale and stores accepted amounts unchanged. The regression extends the existing service test. Its exact command, result and source SHA are recorded in `targetedVerification.t058SubsequentAmountPrecision20261025` in `status.json`. This does not validate the free-text reference against an imported bank/GL row; T058 remains partial, with rendered acceptance and policy decisions open. The exact-source full run `40133039876` was verified against `4ed30164`; its documentation validator failed because the two ledger files exceeded their budgets, while unfiltered Domain/API/E2E jobs were still running. The later docs-only commit `7cdb7f98` brought both files under budget; local documentation validation and docs fast CI `40133206996` passed. The older full run is not a pass for the updated tree, and T058 remains partial.

## T058 full-E2E failure triage (2026-10-25)

Full run `40133039876` on `4ed30164` reported 336/339 E2E cases passing and three failures. Two fieldwork journeys assumed Sampling was the initially selected tab after Source-backed schedules was added; their existing setup now explicitly selects Sampling. The successor-preview test read a response body after navigation; it now captures the body before fulfilling the response. The third failure exposed a real keyboard-focus bug: preview focus restoration still required tab index 0 after Sampling moved to index 1. The guard now checks the Sampling tab index. On `d154e4e1`, a fresh Angular build succeeded (with the existing commercial SCSS budget warning), and the affected four-case E2E cohort passed 4/4. The source, exact command, failed-run status and verification limits are recorded in `targetedVerification.t058FullE2eTriage20261025` in `status.json`. The old full run also has a verifier/documentation-budget failure and its API job was still running at last observation; current-source full verification remains pending. T058 is still partial: this cohort does not supply imported bank/GL row links, approved cut-off/MUS policy, finding linkage or qualified methodology acceptance.

## T058 cut-off and subsequent-match API/Angular integration (2026-10-24)

The `RecordCutOffTestAsync` and `MatchSubsequentTransactionAsync` Application commands previously had no HTTP boundary: staff could not record cut-off tests or subsequent settlements from the running UI. Two `/api/ui/selection-items/{id}` endpoints now expose them with strict ISO-date/decimal request parsing, and the sample-set projection carries `cutOffRevision`/`subsequentRevision` so the Angular fieldwork forms fence each append-only revision stream independently (`expectedRevision` → 409 on staleness; selection-reviewed, generation-current, frozen-file and recorder-ownership guards stay server-enforced). Focused verification on `893da808`: `fieldwork.spec.ts` 36/36, `AuditSamplingServiceTests`+`FieldworkConnectionsTests` 5/5, Release build clean, canonical OpenAPI re-emitted and current, lint clean. The focused six-place precision guard is recorded above. Browser/E2E verification of the new forms was not executed locally; exact-source full release verification `40132097923` is a preceding-source run and is not current acceptance. T058 remains PARTIAL: source references are not linked to actual bank/GL rows, and the approved cut-off window, finding links and MUS methodology policy stay open.

## T058 cut-off procedure lineage (2026-10-24)

Cut-off and subsequent-match evidence now retains the selected procedure and approved selection lineage. Corrections append a new `ExpectedRevision`-guarded revision under the existing firm/client/engagement and item lock order; current projections use the latest revision, while stale, unreviewed, superseded-source and frozen-file writes fail closed. Focused `AuditSamplingServiceTests` pass on `62463e0b`. This does not complete T058: the approved cut-off window, API/Angular matching and finding workflows, MUS methodology and rendered/professional acceptance remain open.

The preceding full run `40128614450` on `8feed9c0` exposed a test-harness sequencing failure: its protected down-migration probe rolled back later schema before the same test finished. The assertion remains unchanged and now runs after the workflow's final database checks. The affected Domain test passes 1/1 on `6ebc0662`; exact-source full verification `40131071167` is running. See `status.json` for run/source boundaries.

## Accepted listing source capture and independent row review (2026-10-25)

The backend atomically retains complete listing rows, exact selected-TB controls, independent approval/return and original actor-owned receipts. Offsetting account differences cannot qualify through a zero net residual. Return can record an unavailable-source refusal; recovery reads the original outcome before mutable source state. New sampling/selection and picker eligibility recheck typed source facts and bytes, while preserving historical evidence. Descriptor values are compared independently of the immutable manifest byte hash.

Source-currentness guards cover selection review, item-test record/review and readiness. The Angular reviewer preview binds original rows to the exact capture hash; a reviewer-only, bounded API route now returns those rows, and a PostgreSQL-backed check verifies reviewer success and preparer HTTP 403. The generated OpenAPI contract is current. Exact-source full CI `40144697389` on `6063c221` is running after the contract correction; no browser or qualified professional acceptance is claimed. Remaining technical and external gates are tracked in `status.json`.

## Current confirmation, difference and native-successor work

Explicit Bank-source selection retains the exact independently approved ledger balance, source identity/digest and current/stale provenance. Manual/legacy cases remain unverified; source and confirmation dates stay distinct. Existing case and closure evidence bytes are preserved. A supplied `AGREED` amount cannot differ from the booked amount; authenticity prose is not an approved reconciling explanation.

Direct difference recording/evaluation now reuse scope locks and recheck current authority inside their transactions. A controlled official role revocation reproduced unauthorized writes before the guard; authorized operations and sibling-scope refusal remain supported. This proves the exercised ordering, not every correction-evidence or caller-owned transaction variant.

The native successor browser journey retains the actual catalogue response before target-period navigation. Source, original-request recovery, independent approval and financial assertions remain; this is a test-harness correction, not a product or professional decision. Focused source/API/SQL/Angular/browser checks and exact CI boundaries are in `status.json`.

The whole original specification remains active. Response explanation/document/finding lineage, nonempty successor continuity and other engineering remain open; human AT/native zoom, methodology, activation and live acceptance stay separate. Earlier bank-inspection and CI paragraphs are preserved in the [handoff archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).

## Current work-queue grant expiry

The queue now rechecks grant expiry with a fresh cutoff after its read snapshot, while retaining the original UTC calendar date for task views. The regression crosses a real stored expiry boundary while a sibling engagement grant remains valid: the original query released protected rows/counts; the corrected query refuses the readback. Existing calendar/count checks pass. Broader template, recurrence and reminder acceptance remains separate.

## Accounting and confirmation source handoffs

The requirement manifest now records exact recipe owners and existing checks for import-first reporting, native bookkeeping continuity and retained period evidence. Retained Domain cases and unchanged-source Angular file passes support their specific guards, not whole workflows. Native successor openings remain implementation work; retained native TB/GL handoffs are recorded below. The confirmation source trace distinguishes all supported areas: caller-entered amounts are not independently derived booked balances, and an approved bank reconciliation is a candidate source rather than an implemented adapter. Missing real-client approvals block their acceptance leaves, not independent technical work.

## Balance draft release eligibility

Manual balance drafts and their screen eligibility now require an issued financial-package release in the exact firm, client and engagement, matching the automatic path and approved draft-only policy. A workpaper release is insufficient. Existing fee-cycle variants retain denial/no-mutation, genuine package release, payment and replay assertions. Full current-source CI and broader commercial/browser acceptance remain open; see `status.json` for scoped evidence.

## Direct confirmation publication authority (2026-10-24)

Direct creation and mutation commands now share firm/client/engagement/case lock order and current authorization. Official role-revocation probes reproduced unauthorized writes, which now refuse publication; close already refused that probe and follows the shared lock order. A separate official-close probe reproduced alternative insertion after closure, fixed by reading current status under lock. Response and alternative reviews also preserve the first independent reviewer and timestamp: repeated or replacement review refuses without rewriting evidence, matching the existing workspace restriction. Focused regressions preserve the earlier positive, taxonomy and sibling-isolation checks. These are Application command protections, not universal database append-only proof. Broader concurrency, source-derived booked balances, differences and full confirmation acceptance remain open; exact evidence is in `status.json`.

## Portal internal-risk boundary and context prerequisites (2026-10-24)

The existing portal API test now retains actual internal checklist evidence and proves permitted client reads omit it, while internal risk routes refuse client identities even with a stray internal grant. The authorized staff control still receives the evidence. Removing only the shared internal-only guard makes the regression fail; exact source restoration and the affected API cohort pass. Production behavior is unchanged; broader rendered/download/export acceptance remains open. Reporting-context review separately identifies existing reusable producers without asserting the requested unified activation exists: that contract remains unverified behind its explicit reviewed prerequisites. Exact evidence is in `status.json` and the requirement manifest.

## Contact routing stored-owner boundary (2026-10-24)

Routing reads now require the joined contact to match the route's firm and client. A restrictive composite foreign key prevents persisted cross-client or cross-firm contact associations. A focused real-database regression reproduced sibling-contact exposure on the earlier schema, then passed with defensive reads; valid same-client routing and existing current-authority checks remain supported. The additive migration refuses invalid historical associations without repairing or deleting evidence. Release compilation and EF model drift checks passed; deployment data preflight and exact-source full CI remain open. See `status.json` for source and verification records.

## Module criterion reconciliation (2026-10-24)

Module requirements have source-line traces across commercial, administration, audit, reporting and practice management. Owner/test links are not acceptance; missing anchors stay unverified. Original records remain intact. Approved ADRs govern billing, identity, sampling and domain authority. Workflow/lifecycle aliases and separate inherited parity IDs prevent duplicate obligations. Work-unit/parity/record-to-report findings still need current-source reconciliation. Archive countdown remains pending under ADR-0018. Historical evidence is unchanged; exact source and checks are in `status.json`.

## Client contact and hierarchy read revocation (2026-10-24)

Current authority is rechecked after protected contact/routing/recipient and hierarchy reads. Source-client revocation suppresses the result; newly inaccessible hierarchy counterparts are omitted, and relationship-choice totals include off-page scopes in the final check. Existing scoped and positive-purpose dispatch tests reproduce the original exposure and pass with these guards. HTTP shapes, writer transactions and business routing rules are unchanged. Exact source/evidence and pending full CI are in `status.json`; no live permission change or browser acceptance is claimed.

## Expense journal and posting reference scope (2026-10-24)

Firm expenses now bind optional journal and posting references to their owning firm. Nullable links and ordinary expense posting remain supported. The existing schema test reproduced acceptance of foreign-firm ledger targets before the additive constraint migration and passed afterward; exact evidence is in `status.json`. No historical rows were repaired, and no development or deployed database was migrated. Existing invalid references must be resolved through an authorized data-review process before deployment; full current-source CI and deployed acceptance remain open.

## Critical workflow acceptance tracing (2026-10-24)

PR #50 was already remotely merged; the main checkout is synchronized on `master`. Existing critical workflow criteria now have bounded source/test verification plans. Focused checks passed for exhaustive phase mapping, task/queue isolation and revision fencing, independent review, blank rework-comment refusal without retained mutations, and evidence-policy replay without source-approval synthesis. The latter does not prove every domain-event producer. Follow-up checks exercise missing client approval at letter generation and retain adverse decisions and an open professional hold after ordinary task completion. Existing commercial, note, confirmation, materiality, freeze, annual and worker-profile checks passed locally. No product behavior changed in these test slices; the guards already existed. Exact evidence and pending CI are in `status.json`. Broader accessibility, professional and provider acceptance remain partial.

## Completion-spec discovery and remaining scope-denial statuses (2026-10-24)

The external four-phase completion specification (31 resolution tasks) was reconciled against current authority. The requirement manifest now traces all 46 current requirement rows to existing UI, API, service, persistence and test files at the recorded source; `SOURCE_TRACED` is not behavioral, CI or professional acceptance. EF model drift is NONE per fast CI; deployed-schema comparison needs an authorized target read. The remaining commercial, accounting-setup, planning and acceptance mutation routes that returned 400 for `scope.denied` now return 403; validation, CSRF and success statuses are unchanged, and OpenAPI already declared 403. Policy-gated tasks (T003, D1, allocation effective date, renewal) and live tenant, custody, operational and release gates stay blocked. Dispositions and evidence are in `status.json`.

WU-039's existing seven-case cohort passed on source `94a1bd32`. A new named PBC method separately passed locally: staff creates a request, the client uploads to that exact request, the durable worker transfers those bytes, and an independent reviewer accepts the exact version. The eight methods have not yet been rerun together on one candidate, and the synthetic journeys are not a cross-module commercial-through-archive identity lineage. The exact-source full CI is running on the prior seven-case source; new code requires its own CI. WU-039 remains partial pending broader commercial-through-archive journeys and dependency gates. Evidence and run identities are in `status.json`.

## CI continuation and main-only workflow (2026-10-23)

The nullable capability fixture repair passed the fast CI gate; it is separate from full release acceptance. The fee-settlement browser journey now checks the current forbidden scope-denial contract, retains zero-write checks and rejects private sibling metadata. Its focused owned journey passed. All agents work in the main checkout on `master`; fetch remote changes periodically, inspect overlapping paths and preserve concurrent edits. Full candidate verification and wider integrity/recovery work remain open; exact sources, runs and results are in `status.json`.

## Subworktree integration (2026-10-23)

The prior source-bounded verification and remaining gates are retained in the [slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#subworktree-integration-2026-10-23) and `status.json`.

## Signed Legacy mail evidence enrollment (2026-10-23)

Archived detail: [handoff history](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#signed-legacy-mail-evidence-enrollment-2026-10-23). Current acceptance limits remain in `status.json`.

## Retained parity UI corrections (2026-10-23)

Archived detail: [handoff history](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#retained-parity-ui-corrections-2026-10-23). Current API/Angular acceptance remains under `apiAngularParity` in `status.json`.

## Scoped accounting and staffing persistence (2026-10-23)

Archived detail: [handoff history](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#scoped-accounting-and-staffing-persistence-2026-10-23). Context activation, wider key inventory and broader acceptance remain open; see `status.json`.

## Scope-denial HTTP responses (2026-10-23)

Acceptance, relationship/routing, chart, engagement, invoice and pricing mutations return forbidden for scope denial; validation and success behavior remain unchanged. Authenticated checks cover foreign-firm, sibling and wrong-route denials with unchanged records. Broader authority/race coverage remains open; see `status.json`.

## PBC reply recovery and current-owner fence (2026-10-23)

Key/hash receipts survive page recreation without storing raw text. Replay/readback reload current ownership. Focused API/Angular, migration, contract and owned lost-acknowledgement/navigation checks passed. Broader portal and current-source release acceptance remain open; see `status.json`.

## Development dependency advisory repair (2026-10-23)

The verified dependency repair and scan review are retained in the [slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#development-dependency-advisory-repair-2026-10-23) and `status.json`.

## Mail scope evidence projection (2026-10-23)

The prior source-bounded verification and remaining gates are retained in the [slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#mail-scope-evidence-projection-2026-10-23) and `status.json`.

## Deployment binding inventory (2026-10-23)

The approved Acceptance Compose target has a read-only binding checker and safe public-settings exporter. Exact source, environment declarations, selected capability identities and all-service credential isolation have focused offline evidence; Apply is refused. Model provenance, running mounts, schema, readiness, egress and rollback remain unobserved. Existing CI routes include the cohort. See the local M365 automation guide and `status.json`; no deployment or live acceptance is claimed.

### Rendered deployment bindings (2026-10-25)

On source `02d8b914`, Docker Compose rendered every declared profile against a temporary synthetic-only environment; the command did not start services, and the temporary environment file was removed. The resulting sanitized model contained all nine services and six storage volumes. `Test-DeploymentBindings.ps1` and `Export-DeploymentSettings.ps1` both returned valid `INCOMPLETE_EVIDENCE` envelopes: all 25 applicable static binding checks passed, one outbound-host-policy check is `MISSING`, and none failed. The focused offline deployment-binding script assertions passed 32/32. Runtime environment/mounts, database schema compatibility, worker readiness, approved egress policy, rollback and live acceptance remain unobserved; WU-024 stays partial and externally blocked. Evidence: `status.json` and the ignored local review record.

## Completion evidence identities (2026-10-23)

Completion exposes persisted artifact IDs and full hashes. The existing release validator now separates recorded approvals from current package evidence and blocks prepare after source return or lost Partner authority; issued history is preserved. Focused PostgreSQL/Angular, build and contract checks are recorded in `status.json`. The owned early-lock journey passed; interactive and full completion acceptance remain open.

## CI fixture repair and read-only readiness inventory (2026-10-23)

The tracker fixture asserts cross-firm rejection before browser denial. CI dispatch preserves JSON output, validates its checkpoint before remote work and retains a created run's identity if local saving fails. Focused checks passed; full CI still requires the exact-source aggregate. See `status.json`.

M365 readiness now inventories authenticated GET metadata with exact plan/API key mapping, stable-session checks and explicit missing-evidence blockers. Offline checks passed; setup status never becomes runtime proof. Runtime evidence projection and live acceptance remain open. CLI usage is in the local M365 automation guide; exact results are in `status.json`.

## Accounting mode and period navigation (2026-10-23)

Unsupported inline source-mode changes are locked when periods exist, including beyond the displayed page. Period links retain exact IDs. Definite profile rejections differ from unresolved outcomes; parent setup actions stay fenced for that client after unknown results. Focused Angular checks/build passed. Native child workflows are unchanged; source-currentness projection and broader browser acceptance remain open. Evidence is in `status.json`.

## Archive secondary-copy protocol (2026-10-23)

The provider copies pinned versions to a separate client library with exact staff READ/path grants. API intents/readback need no worker certificate; source-derived plan export verifies checkpoint bytes outside DB transactions and rechecks current authority. Focused PostgreSQL/API, CLI and contract checks passed. Historical matches do not prove custody; connected control/recovery acceptance remains open. Evidence is in `status.json`.

## Consolidation unknown-outcome controls (2026-10-23)

Mutation controls, including external-pack actions, now reflect the existing unknown-outcome fence. Read-only refresh uses the existing workspace resource without clearing uncertainty; navigation and local editing remain available. Focused Angular checks and build passed; no interactive browser verification is claimed. Exact receipt recovery and broader group acceptance remain open. Evidence is in `status.json`.

## Statement content-review identity display (2026-10-23)

Verified scope and remaining acceptance are retained in the [slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#statement-content-review-identity-display-2026-10-23) and `status.json`.

## Immutable consolidation correction lineage (2026-10-23)

New-scope correction requests retain the exact source scope/component pair in the same firm, group and reporting period. Ordinary creation remains nullable; roll-forward uses its existing independent pointer. Application checks, composite foreign keys and a trigger protect provenance; downgrade refuses to discard retained lineage. Angular preserves these pins through handoff/recovery and displays authorized source identities. Focused PostgreSQL, Angular, build, EF and OpenAPI checks passed; exact evidence is in `status.json`. This records correction provenance, not automatic supersession or professional approval. An owned synthetic browser journey passed the correction handoff, exact request/readback and preservation of the approved source pack/run. This is automated browser evidence, not interactive or professional approval; broader release acceptance remains open.

## Explicit mail-model plan boundaries (2026-10-23)

MAIL plans require exact model-specific scope/sender/control identities. ReadOnly now inspects independent Entra grants and Exchange authorization through existing pinned app-only operator sessions, with bounded counted reads and post-read session checks. It never signs in, grants, sends or activates a runtime. Offline checks passed; live result shapes remain unverified. Provider matches still leave incomplete evidence: legacy needs actual API capability evidence; RBAC needs its runtime implementation. Apply refuses until reviewed migration/rollback exists. The local operator guide describes same-process bootstrap/readback and the remaining gates; WU-022 stays partial. Exact evidence and earlier plan-only history are in `status.json`.

## Group report review-currentness guard (2026-10-23)

The verified slice and remaining acceptance are retained in the [slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#group-report-review-currentness-guard-2026-10-23) and `status.json`.

## Per-component consolidation currentness (2026-10-23)

The scope workspace now separates pinned-source currentness from component approval. Existing source validation is shared; exact pin, review, source authorization and perimeter changes demote the read model without replacing pins or modifying component books. Post-snapshot checks never promote stale evidence. Focused Domain/Angular checks, contract verification and an owned synthetic browser journey passed. Built-in browser policy denied the existing tab; no interactive verification is claimed. Broader group-report ownership, source-policy, release and current-source CI remain separate. Exact results are retained in `status.json`.

## Client-site terminal intent integration (2026-10-23)

The terminal scripts now submit reviewed local Application intents and observe exact worker-published receipts. The duplicate PowerShell provisioning algorithm is removed. Signed CMS approval, source pinning, current staff session, CSRF, persisted rollout boundary, bounded polling and final per-target rereads preserve the existing worker's authority. Historical staff evidence projects pending membership, never verified access. Focused PostgreSQL, HTTP and offline terminal checks passed; exact evidence is in `status.json`. Current-source CI and separately approved live tenant acceptance remain open. No tenant permission or provider effect was performed by this engineering run.

## Client-site managed membership and residual evidence (2026-10-23)

The existing worker removes only immutable identities retained in the prior managed receipt. Unknown memberships in its group remain untouched and appear in the durable receipt and append-only administration events; membership stays `PARTIAL`. Focused provider/PostgreSQL checks passed; evidence is recorded in `clientSiteManagedMembership` in `status.json`. This does not certify unrelated groups or direct site ACLs. The authenticated terminal intent/status adapter remains unfinished; the duplicate draft's live entry points stay blocked. No live tenant effects were performed.

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

## Angular quality and accessibility status (2026-10-23)

Typing, capability splits, conventions and lint are implemented. Scoped checks cover journal/evidence/budget previews, navigation, sampling and consolidation pagers. Built-in procedure checks found transient false-unavailable feedback and focus loss; loading and guarded exact-preview focus are corrected without stealing moved focus. The GL after-save fixture retains stale-generation denial/full rollback. Review-point recovery serializes the actor-owned receipt before mutable state with scope/target checks intact. Consolidation capture retains bodies before navigation and preserves source/pin/run assertions. Mapping revocation accepts an already-cleared UI with denial/data-removal checks retained. Focused checks pass. Commercial recovery assertions now select the exact page H1 over its same-name H2. Full CI on `1dfedeb7` passed API413/Angular/Domain/contracts; E2E334 passed. Time-edit refresh and lost-response test ordering now pass the named journey; new full CI is pending. An older proposal lifecycle GET was absent; unchanged local and later exact-source E2E journeys pass, but its cause is unproven and diagnostics remain. CI/source evidence: `status.json`. Wider states/native zoom/human AT remain open; AC-09 is partial. No deployment/live effects.

## Current master verification and scope (2026-10-21)

Historical verification details are preserved verbatim in the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md#verification-snapshot-archived-during-bank-inspection-continuation). They are not current-source acceptance.

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

## Historical workflow, accounting and accessibility slices (2026-10-18–21)

Closed dated narratives are retained in the [historical current-slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md); sections already present there were not duplicated. This compaction changes no verification status. Current work and acceptance limits remain in this handoff and `status.json`.

## PBC exact-version reviewer acceptance

Retained earlier evidence is in the [historical handoff](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#pbc-exact-version-reviewer-acceptance).

Earlier parity implementation observations are retained in the [historical handoff](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#api-and-angular-parity-handoff-archived-2026-10-23).

> **Archive:** older dated handoff sections are retained verbatim in [`auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md`](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). They are historical evidence; consult `status.json` and current authority documents for present state.

## Recent focused evidence

Official revocation cannot authorize stale risk owners or direct schedule creation/review/binding. Focused checks retain prior history; internal create/bind/review commit only with their owning transaction. PBC closure preserves verified source identity. Per-account resolution remains separate from pending capture/review UI and full CI. See [status.json](status.json) and the historical handoff.

## Native generated reporting sources

Native TB/GL handoffs retain focused source, authorization, download and package-currentness evidence. Unknown metadata remains unknown; external imports remain supported. Public period creation is still DRAFT and source acceptance still requires ACTIVE. Full details and acceptance limits are retained in the [historical handoff](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#native-generated-reporting-sources--archived-during-source-capture-continuation). Current exact-source CI, public activation, native zoom/human AT and qualified/live acceptance remain separate.

## Native successor opening continuity

The existing opening owner retains the source-bound empty-subledger v1 path and adds v2 original-item carry for complete, unallocated original opening/sales/purchase invoice populations. Actual original IDs, reference/date semantics, posting artifacts, approved controls and source facts remain retained; no successor invoice is reposted. Later supported successor allocations consume those same originals without changing the immutable capture baseline. Historical partial allocations, unsupported credits, ambiguous effective cutoffs, individually nonzero P&L accounts and incompatible chart/currency remain refused. This is a bounded continuity extension, not a new closing policy or public period activation.

Focused evidence covers original AR 125 less an actual receipt allocation 20 = 105, native AP 100 less an actual supplier credit allocation 40 = 60, returned-purchase resubmission preserving the original journal, and self-hashed amount/party/omission/version/control/receipt forgeries refused by actual database boundaries. The returned-purchase test exposed and corrected duplicate journal numbering. Nine Angular cases, lint/build and the existing browser journey's empty/nonempty variants pass; a nonempty repeat also passes. Original preview built-in inspection is retained; the final retained-source handoff expired before built-in capture, so it is not claimed. Empty rollback/forward restores the published v1 constraint; retained v2 blocks rollback with SQLSTATE 55000. That check passes after correcting its EF exception-wrapper assertion. EF model drift, canonical OpenAPI regeneration/drift and fresh emitter compilation pass; these checks establish the bounded slice, not full release acceptance.

Exact published 1782 CI failed with API 414 pass / 2 fail and E2E 336 pass / 2 fail. The two static route/base-helper checks were corrected and passed locally, published as cd528c48; the revocation click/terminal-denial cohort and Operations CANCEL case pass locally with diagnostics, published as 95cff915. The prior Operations failure cause remains unproven and open for exact-source CI. No full local suite ran. Broader continuity, exact-source full CI, public ACTIVE activation, native zoom/human AT and qualified/live acceptance remain open. Prior empty-subledger details are preserved verbatim in the historical handoff; see `nativeSuccessorOpening` in [status.json](status.json).

## Bank linked-source freshness

Bank creation and review now reject approved linked schedules whose generation differs from the current client generation. A focused existing workflow reproduced stale-source creation before the fix and passed afterward, retaining the original approved reconciliation and refusing a synthetic historical-invalid review. This is a source-adapter prerequisite, not runtime race proof, typed booked-balance retention or T059/Task 18 completion.

## Client-portal unknown upload-start recovery

The existing upload-recovery Playwright journey now drops the response after the API commits one upload intent, verifies the visible `STARTED` receipt and unknown-outcome message, reselects the exact file, then resumes that same intent. It also drops a committed chunk acknowledgement and proves the next resume completes the original file without another intent. The focused method passed 1/1 on `48cd3e68`; TRX and source details are under `portalUploadStartRecovery20261025` in [status.json](status.json). At the latest observation, fast CI `40161352561` had passed Docs Health and Angular while its .NET Release build was still running; full release verification `40161404727` had resolved the exact source and passed its Angular/full-spec job, with build/contracts/publication and .NET Domain/API/E2E jobs still running. No worker/provider acceptance or independent staff acceptance is claimed; WU-034 remains partial.

## WU-004 scoped foreign-key classifier

The migrated-schema check recognizes a firm-scoped FK only when it preserves every child-to-parent column mapping and maps `firm_id` to `firm_id`. Budget approval and preparation receipts now use composite `(firm_id, budget_id)` and `(firm_id, engagement_id)` FKs. Four direct cross-firm probes were refused by the expected constraints after valid receipts and spare budgets were created through existing application workflows. The catalog scan now reports 50 remaining candidate links without fixture rows; those are still unprobed, and WU-004 remains partial alongside append-only and supported-upgrade-path coverage. The focused migrated-schema test passed and EF reports no model drift. Exact source/test digests and boundaries are in [status.json](status.json) and the ignored review evidence.

The budget-receipt follow-up adds an EF migration for those four firm-scoped references. The focused test applies the migration from an empty isolated PostgreSQL schema, creates valid preparation/approval receipts through existing application workflows, then verifies each foreign-firm budget or engagement reference fails with SQLSTATE `23503` on the intended composite FK; each attempt is rolled back. The migration and snapshot have no pending model drift. This does not establish a supported in-place upgrade/downgrade or exercise the remaining links; WU-004 remains partial.
