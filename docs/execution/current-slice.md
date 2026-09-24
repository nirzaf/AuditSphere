# AuditSphereOps — current state and active slice

This file records observed repository state only. The authoritative build contract is [`docs/SPECIFICATION.md`](../SPECIFICATION.md) v5.0. The public repository intentionally excludes tenant identifiers, user principals, credentials, tokens, secret values, and private-provider URLs.

## AC-24 / AS-PAR-002 — financial package refresh after grant revocation

- The financial statement package page now clears package, statement, cash-flow, disclosure, review and artifact projections before resolving the current actor and rechecking the exact client/engagement grants. A failed refresh leaves prior package data cleared.
- `AS-PAR-002-FS-REFRESH-REVOKED-01` revokes the displayed user's active Staff and AccountingPreparer grants while the package remains open, clicks Refresh package, and verifies that Access unavailable replaces the package and its statement data. Focused PostgreSQL-backed browser case passed 1/1; full E2E passed 57/57 with 0 skips; Release solution build passed with 0 warnings/errors; `git diff --check` passed. Code commit `f6a1bacf4336eea8abf75ff0c63d547c28d96fe8` is pushed to `master` and remote-confirmed. Hosted run `35999115290` targets that SHA and was pending as of 2026-09-24T12:26:56Z; no hosted result is inferred.
- No migration, tenant operation or production effect. AS-PAR-002 remains partial.

## AC-24 / AS-PAR-002 — accounting record queue refresh after grant revocation

- The shared accounting mappings/journals/differences page now offers a queue refresh; its existing loader clears prior queue rows and actor state before resolving current identity and grants. `AS-PAR-002-ACCT-RECORD-REVOKED-01` revokes active Staff and AccountingPreparer grants while the synthetic journal queue is open, then verifies refresh removes the journal row and shows Access unavailable. Focused PostgreSQL-backed browser test passed 1/1; full E2E passed 58/58 with 0 skips; Release solution build passed with 0 warnings/errors; `git diff --check` passed. Commit `7c5575e877513b7611f1c25370d3bd12a993c79c` is pushed to `master` and remote-confirmed. No schema migration, tenant operation or production effect.
- Hosted run `35999241668` failed before test execution because discovery found 324 cases while the workflow still expected 323. Fresh local full-solution discovery now finds 325 after the second test was added. The workflow expectation and test documentation are updated to 325 and actionlint passes locally; a corrected hosted run is required. AS-PAR-002 remains partial.

## AC-24 / AS-PAR-002 — exact accounting period revocation refresh

- The period detail now provides a refresh that immediately clears period, PBC and task projections, resolves the current actor, and reauthorizes the exact client before loading the period again. Errors are logged without exposing provider/database details and the old period remains cleared.
- Existing `AS-PAR-002-ACCT-PERIOD-01` now verifies client-scope denial after same-document navigation and, separately, in-place grant revocation after returning to the authorized period. Revoking both valid/mismatched Partner grants then refreshing hides the prior period and shows Access unavailable. Focused PostgreSQL-backed browser test passed 1/1; full E2E passed 56/56 with 0 skipped in 6m04s; Release solution build passed with 0 warnings/errors; `git diff --check` passed. Commit `94bbb08dd1004bb215fb8c32a3635728e6a1930e` is pushed and remote-confirmed. Hosted run `35997764542` was pending for that SHA as of 2026-09-24T12:14:08Z; no hosted pass is inferred.
- No schema migration, tenant operation or production effect. The broader AS-PAR-002 review remains partial.

## AC-24 / AS-PAR-002 — accounting workspace revocation refresh

- The client accounting dashboard now exposes an explicit refresh that wipes its current client, period, task, package and count projections before resolving the actor and current grants again. Revoked or unavailable authorization cannot leave the prior dashboard visible; transient errors return a retryable non-disclosing message.
- `AS-PAR-002-ACCT-WORKSPACE-01` verifies a scoped period is visible initially, unrelated-client and mismatched-engagement records stay hidden, and after revoking all required Partner grants while the page remains open, refresh clears the period and scope summary and shows Access unavailable. Focused PostgreSQL-backed browser test passed 1/1; full E2E passed 56/56 with 0 skipped in 6m04s; Release solution build passed with 0 warnings/errors; `git diff --check` passed. Code commit `3d387ecd69b4bc46722d0f08ff845f489bc0bb3a` is pushed to `master`. Hosted run `35996541118` was pending on that SHA at 2026-09-24T12:02:09Z; no hosted pass is inferred.
- This closes a bounded AC-24 context/revocation slice only. The whole-application AS-PAR-002 audit remains partial. No schema migration, tenant operation or production effect.

## AS-PAR-002 — accounting evidence queue revocation refresh

- The accounting evidence queue now offers a manual refresh that immediately clears the previous rows, re-resolves the actor, rechecks current internal role grants, and reloads only the resulting scope. Revocation and connection errors leave no stale evidence visible; refresh failures show a retryable, non-disclosing message.
- The PostgreSQL-backed Playwright journey `AS-PAR-002-ACCT-QUEUE-01` verifies the assigned user sees the synthetic evidence, an unrelated client cannot see it, and revoking the assigned grant while the page remains open causes refresh to clear it and show Access unavailable. Focused case passed 1/1; full E2E suite passed 56/56 with 0 skipped in 6m03s; Release solution build passed with 0 warnings/errors; `git diff --check` passed. Hosted run `35994986488` is pending on exact pushed SHA `e27f7bceddc15e71ea2632e8ebd88b3f116e631e`; predecessor run `35992006745` was still running its unfiltered suite at the last check. No hosted result is inferred.
- This is one route-level slice only. AS-PAR-002 remains partial pending the remaining route/query/search/count/export/direct-command and already-rendered-content audit. No schema migration, tenant operation or production effect.

## Accounting — preserve foreign-currency amounts in GL imports

- Direct and chunked GL ingestion now preserve each line's normalized original currency, original amount and functional amount rather than forcing the period currency into every source line. Both paths use the same transaction validation; signed functional postings must match debit/credit, same-currency amounts must agree, and normalized source fields participate in the idempotency digest.
- PostgreSQL-backed regressions cover direct and streamed foreign-currency lines, stored source values, normalization/idempotency, and rejection of a mismatched functional posting. The focused two-test run passed 2/2; full Domain Release suite passed 261/261 with 0 skips. Focused E2E practice-time journey passed 1/1; Release solution build passed with 0 warnings/errors; EF found no pending model changes; `git diff --check` passed.
- Hosted run `35971115983` on predecessor `ba1a3ec` discovered 323 tests and passed Domain 261/261 plus API 6/6, then the E2E journey `ProspectTimeBillingAndManualFirmCloseKeepTheirBoundaries` failed waiting for the time-draft success message. Its browser/server artifacts show no persistence exception; the same journey passed locally. The 90-minute hosted job was cancelled before aggregate E2E completion/readiness. This is not a hosted full-suite pass; rerun after this checkpoint.
- This improves imported source fidelity only. Remeasurement still requires a preparer to identify/prove an outstanding balance and enter its classification and carrying amount; a posted GL line is not automatically treated as an open item. Methodology and live acceptance remain separate gates.

## Accounting — versioned component FX translation correction

- Corrected the sibling persisted translation path: component translation no longer labels `translated amount - source amount` as an FX adjustment. New `COMPONENT_TRANSLATION_V2` results store no monetary FX adjustment; translation-reserve computations remain in their separate method-specific schedule. Scope approval/build require V2. Added an append-only calculation version and migration that labels existing rows V1, preserves their bytes/values/status, and changes the unique key so a corrected V2 result can coexist instead of rewriting history. Archive export retains the algorithm version.
- The PostgreSQL integration journey seeded an approved V1 result with the former 264-unit mismatch, then verified a V2 row is separately produced/approved with zero FX adjustment while the V1 evidence remains unchanged. Focused journey passed 1/1; full Domain Release suite passed 260/260 with 0 skipped; Web Release build passed with 0 warnings/errors; EF pending-model-change check passed. No production DB migration or tenant effect.
- Pushed on `master` as `d1450995ffa52d74ea2c142171893db697656659`. Hosted run `35965664362` targets that exact SHA and was in progress at 2026-09-24T06:41:17Z; superseded run `35964126911` was cancelled. No hosted result is inferred.

## Accounting — persisted source-bound currency remeasurement workpaper

- Added an immutable PostgreSQL schedule/item model and migration. Each item pins an exact firm/client/engagement-owned document snapshot and SHA-256, an approved rate-set observation, policy-selected rate date/type, normalized operator-entered balances/classification, calculation outputs and input manifest. A different authorized reviewer revalidates current inputs before approval. Database guards preserve submitted evidence; approval never posts a journal.
- Added the scoped accounting workbench: choices come from the user's client/engagement grants and approved rate/policy sets; it supports up to 500 evidence lines, item-level calculation/source readback, independent approval, recent scoped history, contextual required/optional guidance, and browser-local draft restore/clear. Workpaper history is filtered by exact engagement grants or client-wide grants.
- The focused PostgreSQL integration test passed 1/1, covering monetary vs historical-cost rate selection, evidence lineage, idempotent replay, self-approval rejection, independent approval, readback and append-only database enforcement. Web Release build passed with 0 warnings/errors; EF reported no pending model changes. The focused Playwright workbench/draft test passed 1/1 after the final scope-filter adjustment. The unrelated `ClientScopeJourneyTests.EngagementDetailDeniesUnassignedScopeAndClearsOnRouteChange` journey also passed 1/1 when rerun alone.
- G16 source-link follow-up: each submitted remeasurement line now requires a distinct imported GL line from the same sealed period/batch scope and matching original currency; the schedule digest commits to the exact line/batch/transaction details, the scoped database foreign key prevents cross-engagement linkage, and approval recomputes that digest. GL line and transaction rows are append-only at the database boundary. The UI restores its local draft only after the authenticated context and scoped source choices load; the GL link is explicitly not evidence that the balance remains open. The focused DB and browser cases passed 1/1 each.
- Full local verification after this follow-up passed Domain 261/261, API 6/6 and E2E 56/56 with zero skips; Release solution build passed with zero warnings/errors and EF reported no pending model changes. This is local verification only; no production DB, Microsoft tenant or external effect was touched.
- Serialized project verification then passed: Domain 261/261, API 6/6 and E2E 56/56, all with zero skips (323 discovered in aggregate). The earlier parallel 54/55 E2E timeout was environmental and the exact journey passed alone. Web Release build, EF no-pending-model-changes, `git diff --check`, and actionlint v1.7.7 also passed. No production database, tenant or external effect was performed.
- Hosted run `35969468368` for exact master SHA `d8b0310ba0450d259f47934de3b6b20af49d3631` passed restore/build and migration/model-drift checks, then failed discovery reconciliation because the workflow still expected 321 tests while 323 were present. Updated the expected count to 323; the workflow change passed actionlint and awaits push/hosted rerun. This is a CI guard mismatch, not a test failure.
- The code slice is pushed to `master` as `48bd2e4d88de236989b4cbe20cab8a360ffe451c`; the documentation ledger is pushed as `d8b0310ba0450d259f47934de3b6b20af49d3631`. Hosted run `35969367585` on the code commit was cancelled when the ledger commit triggered the newer run. No hosted pass is claimed yet.

## Accounting — functional-currency remeasurement correctness

- Fixed `CurrencyRemeasurementCalculator`: it now requires the prior carrying amount in functional currency and computes a monetary FX adjustment as closing-rate functional value less that same-currency carrying amount. Historical-cost non-monetary items still use the historical rate and do not report an FX remeasurement gain/loss. This avoids subtracting foreign-currency units from functional-currency units.
- The regression covers a QAR 10 monetary gain, zero FX adjustment for historical-cost non-monetary value, and a signed QAR 10 liability loss. The focused case and persisted-workpaper journey pass; the source-bound workflow and review-only UI are implemented in the section above. Full Domain Release suite passed 261/261 with 0 skipped; Web Release build passed with 0 warnings/errors. Professional methodology and applicable live acceptance remain separate.
- The calculator correction is included in `d1450995ffa52d74ea2c142171893db697656659`. Hosted CI for this current commit is run `35965664362` and remains in progress; no tenant or production effect.

## Accounting — capability-matrix reconciliation

- Reconciled stale G02–G05, G08, G10, G12–G17 descriptions against the current domain/application services, PostgreSQL regressions and the checked AC stories. Advanced group methods are implemented behind method-specific source-bound schedules and separate approvals; unsupported associate/joint-arrangement and common-control cases, auto-enablement, and external release/records/production acceptance remain blocked or separately gated.
- Focused Release tests for advanced consolidation methods, currency operations and foreign-schedule evidence passed 7/7 with 0 skipped on 2026-09-24. `git diff --check` passed. This was a documentation and evidence reconciliation; no runtime, schema, tenant or production changes.
- Documentation commits `5826504` and `f685562` are local on `master`, not pushed. Hosted run `35959313796` still targets remote code SHA `8eb8d1ce2dc658006561692fdf9bd0da8d9f6423`; GitHub reports its unfiltered test step in progress, with no final result available. Do not infer its outcome.

## Accounting — independent materiality approval

- Added a separate append-only `MaterialityApproval` record so an independent, engagement-scoped Manager or Partner can approve a materiality plan without mutating the immutable assessment. The command reauthorizes current scope, rejects self-approval and replay; PostgreSQL enforces one approval per assessment and blocks update/delete. Audit Plan displays the observed approval.
- PostgreSQL focused regression passed 1/1; the complete Domain test project passed 260/260 with 0 skipped; Release web build passed with 0 warnings/errors; EF reports no pending model changes. `scripts/db/restore-drill.sh` passed for the local source DB's existing 91 migrations through `20260922140527_M365InvitationEvidenceAction`; the new migration remains pending there, while the focused PostgreSQL test applies and verifies the new migration in its isolated schema. No tenant or production effect.
- This slice closes independent approval of materiality planning; aggregate assessment was implemented and verified in the separate slice below.
- Commit `c19f4cb73412e876bac2e6b1af1d30a026681464` is pushed to `master` and remote-confirmed. Hosted CI run `35956466558` was cancelled by master-branch concurrency after the execution-ledger follow-up queued a newer run.

## Accounting — reviewed aggregate difference and reporting assessment

- Reused `AuditAreaAssessment` for the human-authored conclusion and its independent review. For `AUDIT_DIFFERENCES`, the service creates a deterministic SHA-256-prefixed snapshot of every current difference plus the latest independently approved materiality plan; user-supplied snapshot JSON cannot substitute. Review and completion recompute the snapshot, so later corrections, reclassification, newly identified items or a new materiality revision make prior approval stale. Completion blocks until a current independent review exists. The UI presents gross, signed/net, unadjusted and corrected totals by currency but explicitly leaves the professional decision to the engagement team.
- PostgreSQL coverage verifies unapproved materiality blocks preparation, a current reviewed conclusion clears the aggregate blocker, and a new difference makes it stale. Focused Playwright verifies required conclusion entry and persisted submission. Full Domain 260/260, API 6/6 and E2E 55/55 passed with 0 skips; solution discovery reconciled to 321; EF reports no pending model changes; Release builds succeeded; actionlint v1.7.7 passed. Two pre-existing E2E flake points were stabilized: a denial-triggered detached revocation button now force-dispatches the intended action, and a duplicate group heading assertion is exact.
- Hosted run `35956630009` on `60fe7fd` failed before tests because the expected inventory was 319 rather than 320 (the separate materiality test had already increased discovery); the current change updates the workflow and docs to 321. No tenant or production effect. This closes C7 locally, not professional sign-off, external signing, release or records acceptance.

## Accounting — current approved group report

- The consolidation workbench now displays the latest approved report as component/taxonomy totals split into component, alignment, elimination and consolidated amounts. The service revalidates the run’s current input manifest before returning amounts; stale approvals show no lines. The report is authorized at group scope and does not expose source-package artifacts to a group-only reviewer.
- `GroupWorkflow_UsesApprovedComponentPackagesWithoutMutatingThem` now checks the group-only report view, denied raw package-artifact access, and stale-report withholding after a perimeter change. PostgreSQL-backed Release test passed 1/1 on 2026-09-24; full Release solution build passed with 0 warnings/errors. Commit `4529b98f4ad09c6b4c8c85715f882f7a05dde77d` is pushed to `master` and confirmed equal to `origin/master`. Hosted run `35954419644` for this exact SHA was pending at 2026-09-24T04:09:22Z. Earlier run `35953670134` tests only base `57e2c82`, not this slice. No schema migration, tenant operation or production effect.

## Accounting — consolidation component freshness

- Extended `GroupWorkflow_UsesApprovedComponentPackagesWithoutMutatingThem` with a PostgreSQL-backed stale-source check: after a group run is built, a changed component package hash causes run approval to fail with `GenerationStale`; restoring the original source hash permits approval. This is a local fault-injection regression of the run-manifest guard, not a new component-replacement workflow.
- Focused Release test passed 1/1 on 2026-09-24. Full solution/hosted CI for this assertion is pending. No runtime or schema change, tenant operation or production effect.

## Accounting — management package is not an assurance release

- Strengthened `ClientPackageView_IsScopedAndSupportsSignedInManagementDecision` to verify that a client’s signed management acknowledgement creates neither a release candidate nor a release. Strengthened the existing package-portal browser journey to assert the UI explicitly says this is not an audit opinion, assurance conclusion, or proof of external posting.
- PostgreSQL domain test passed 1/1; focused Playwright test passed 1/1; full Release solution build passed with 0 warnings/errors. No runtime/schema change, tenant operation or production effect. Hosted CI for this new assertion is pending.

## AS-PAR-002 — Audit fieldwork same-document engagement reauthorization

- Reproduced a stale disclosure: moving from an assigned engagement to an unassigned sibling in the same browser document left the prior adopted audit program visible.
- `AuditFieldwork.razor` now reloads and reauthorizes on route-parameter changes, clears the previous engagement/program/procedure projection first, and discards results from earlier overlapping reads. Commands continue to use the guarded audit-program services.
- Added `AS-PAR-002-AUDIT-FIELDWORK-STALE-ROUTE-01`. The synthetic PostgreSQL-backed browser journey adopts a program for the assigned engagement, navigates in-place to an unassigned sibling and confirms program details are absent, then returns to the authorized engagement and confirms it reloads without a document reload.
- Verification: focused journey 1/1; complete E2E project 51/51, 0 skipped; Release solution build 0 warnings/errors; EF reports no pending model changes; actionlint v1.7.7 passes. No schema migration, tenant operation or production effect. The existing hosted run on predecessor `2ce6184` had not completed when this slice was prepared; its final outcome is recorded after push.

## AS-PAR-002 — Completion checklist route characterization

- Added `AS-PAR-002-COMPLETION-STALE-ROUTE-01`: a synthetic Partner opens completion for an engagement with a persisted financial package, navigates within the same browser document to an unassigned sibling, and confirms the old package ID is absent from the unavailable state.
- The focused browser case passed 1/1 without a runtime change; the existing routed component lifecycle clears the projection. The complete E2E suite then passed 52/52 with 0 skips. No schema migration, tenant operation or production effect.

## E2E — ReviewPoint revocation click stabilization

- In the synthetic current-grant revocation journey, the page removes the disposition button as soon as its expected authorization-denied response clears protected state. Playwright sometimes reported the button detached during the click and retried until timeout.
- The browser test now force-dispatches the already-visible action, then verifies the non-disclosing unavailable state, absent private comment/action, and unchanged `ReviewPoint.Cleared` value. Focused test passed 1/1 and complete PostgreSQL-backed E2E passed 52/52 with 0 skips. No production or tenant effect.

## AS-PAR-002 — Advanced consolidation group-scope route characterization

- Added `AS-PAR-002-ADV-CONSOLIDATION-STALE-ROUTE-01`: an assigned staff actor opens a synthetic advanced-consolidation group scope, navigates within the same browser document to an unauthorized group, and verifies that neither group name nor prior scope ID remains visible.
- Focused PostgreSQL-backed browser case passed 1/1; full Release E2E project passed 53/53 with 0 skips; unfiltered Release discovery reconciled to 318. No runtime or schema change, tenant operation or production effect.

## AS-PAR-002 — Accounting mapping cross-engagement route characterization

- Added `AS-PAR-002-MAPPING-STALE-ROUTE-01`: a synthetic staff actor reads an authorized mapping, then navigates in the same browser document to a real mapping belonging to a sibling engagement for which the actor has no grant. The page renders only the generic unavailable state and clears both mapping IDs, source dataset ID and private rationale.
- The focused PostgreSQL-backed browser case passed 1/1 and the full Release E2E project passed 54/54 with 0 skips; unfiltered Release discovery reconciled to 319 (Domain 259, API 6, E2E 54). Hosted aggregate remains pending. No schema migration, tenant operation or production effect.

## AS-PAR-002 — Client package revocation in the existing browser circuit

- Strengthened `AS-PAR-002-CLIENT-FS-REVOKE-01`: revoke the synthetic client's package grant while management review is open, keep the same document token, and verify the package hash, statement totals and decision control disappear without a document reload; no decision is persisted.
- The PostgreSQL-backed focused browser case passed 1/1. This is a test-only characterization of the current revocation behavior; no runtime/schema change, tenant operation or production effect. Full E2E/hosted verification of this strengthened assertion is pending.

## CI locked restore — exact SDK selection

- Run `35920521850` reached restore, then failed with NU1004: SDK-injected `Microsoft.AspNetCore.App.Internal.Assets` requested `10.0.11`, while the lock file records `10.0.8`. Local SDK `10.0.300` evaluates that package to `10.0.8` and restores the existing locks successfully.
- `global.json` previously allowed `latestPatch`, which can select a newer preinstalled SDK even after setup installs `10.0.300`. It now requires an exact SDK match with `rollForward: disable`. CI installs from `global.json`, checks the policy and selected SDK before restore, and prints SDK diagnostics. Locked restore and all package lock files remain unchanged.
- Local verification: the SDK policy check failed before the fix and passed afterward with SDK `10.0.300`; tool restore, full-solution locked restore, and Release build passed. Hosted run `35921528397` then passed SDK selection, locked restore, Release build, Playwright setup, migration/model-drift checks, and 315-case discovery. Domain passed 259/259; API testing was still running when the 45-minute job limit cancelled the run, so API/E2E hosted results are incomplete. No production acceptance is claimed.

## CI runtime adjustment — hosted run #35921528397

- The CI job executes Domain, API, and E2E test projects sequentially with coverage. The 45-minute job limit cancelled run `35921528397` during API tests after Domain passed 259/259 in 15m57s. This is a timeout, not a reported test failure.
- Raised the job timeout to 90 minutes so the full suite and readiness/artifact steps can complete. Keep hosted status pending until a new run finishes; local aggregate 315/315 does not substitute for hosted completion.

## CI E2E stabilization — hosted run #35926424863

- Run `35926424863` on `30d3191` completed all earlier gates and discovered 315 tests, but the full suite failed at 313/315: two E2E browser scenarios failed. The time-entry journey timed out on its success-message assertion before its longer row wait; the audit-plan case raced role revocation against entering the form.
- Updated the audit-plan scenario to populate the synthetic risk before revocation, assert that the stale pre-revocation actor is denied with `generation.stale` and that no risk is persisted, then verify same-document navigation clears the revoked user's private plan state. This retains the authorization and data-clearing assertions without depending on an in-flight browser race.
- Updated the time-entry journey to wait for the persisted row before checking the success message, with a 30-second UI expectation. No application runtime changes were needed.
- Local verification at `01485f9cbea61c1a0aff053f85fdd9f55bc9a1d6`: both focused journeys passed 2/2, and the complete PostgreSQL-backed Playwright E2E project passed 50/50 with 0 skips in 5m27s. The Release E2E test project rebuilt successfully; `git diff --check` passed. Full solution Domain/API suites were not rerun for these test-only changes.
- Commit `01485f9` was pushed to `master`; `git ls-remote` confirmed exact remote SHA. Hosted CI for the fixed revision is pending. External production/tenant acceptance is unchanged and remains blocked where listed below.

## CI time-entry selector follow-up — hosted run #35936138214

- Run `35936138214` on `d5aa1833` passed SDK selection, locked restore, Release build, Playwright setup, migration/model-drift validation, and 315-case discovery. Domain passed 259/259 and API passed 6/6. The time-entry browser journey failed while waiting for its saved row; the 90-minute job was canceled before an E2E TRX report or full-suite counters could be produced. The recorded 265 tests are Domain and API only, not a 315-test pass.
- Replaced the time-entry journey's positional dropdown selection with the accessible control scoped to the “Record time draft” region, and assert the saved command result before the row. This makes the intended task control explicit and reports a server rejection before waiting on the table.
- At `7433e76ae745145a28dd5bb0ae97af7dc0840f93`, the focused time-entry browser case passed 1/1 and the complete PostgreSQL-backed E2E project passed 50/50 with 0 skips. `git diff --check` passed. Hosted verification of this follow-up remains pending; no full-solution hosted pass is claimed.
- CI failure diagnostics were downloaded from run `35936138214` to `/tmp/auditsphere-ci-35936138214-e2e` for local inspection. No production or Microsoft tenant effects were performed.

## CI workflow validation fix — run #393

- Reproduced GitHub's invalid-workflow error with actionlint: `runner.temp` is unavailable in job-level `env`.
- Initialize `E2E_RUN_ROOT` after checkout using `$RUNNER_TEMP`, `$E2E_RUN_ID`, and `$GITHUB_ENV`; subsequent steps retain the same path used by isolated-scenario artifact uploads.
- Local checks: actionlint v1.7.7 exited 0 after the fix (`-shellcheck= -pyflakes=`); Bash path assertion passed with spaces and a run/attempt suffix. Full .NET suite and hosted GitHub Actions were not rerun for this workflow-only fix. Existing application verification below remains historical.

## Current checkout and latest verification snapshot

| Item | Observed value |
|---|---|
| Source/test checkpoint | `master@7433e76ae745145a28dd5bb0ae97af7dc0840f93`; practice time E2E now uses a scoped accessible selector and checks the command result before the row |
| Remote | `01485f9cbea61c1a0aff053f85fdd9f55bc9a1d6` was the last `origin/master` SHA confirmed before the current test follow-up push |
| SDK | .NET 10; repository solution targets `net10.0` |
| Database | PostgreSQL 18.6 on loopback port 5433 for development only |
| Build | Full solution Release build for `c214cb3` — passed, 0 warnings/errors; changed Release E2E project rebuilt for `01485f9` as part of test run |
| Tests | Full Release solution aggregate passed 315/315 with 0 skipped at `c214cb3`; latest E2E suite passed 50/50 with 0 skips at `7433e76`. Hosted run `35936138214` passed Domain 259/259 and API 6/6, then was canceled at 90 minutes during E2E; hosted full-suite acceptance remains unverified. CI-style discovery remains 315. |
| Migrations | Added `20260923145244_ProposalPreparedByAttribution`; prior-schema PostgreSQL regression upgraded a synthetic legacy proposal and preserved its values with preparer left unknown. The existing local `auditsphere` database remains at its 91-migration baseline. |
| Model drift | `dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web` — no pending model changes for `c214cb3`; no persistence model changes in these slices |
| Restore drill | Passed at `2026-09-23T15:20:43Z` on loopback; restored the existing `auditsphere` source at 91 migrations through `20260922140527_M365InvitationEvidenceAction`. Evidence was redirected to `/tmp`; the check does not apply the new proposal migration to that development database. |
| Production effects | Disabled locally; no production acceptance claimed |

## AS-PAR-002 — Client profile same-document route reauthorization (`c214cb3`)

- `ClientDetail.razor` now reloads and reauthorizes when `ClientId` changes, clears the previous client/contact/engagement projection and draft form, and fences stale reads and command results by route generation.
- Added `AS-PAR-002-CLIENT-STALE-ROUTE-01`: a client-scoped synthetic manager switches within the same browser document to an unassigned client, confirms the old profile, registration number, contact and engagement are hidden, then returns to the authorized client without a page reload.
- Focused browser case passed 1/1; full Release solution build passed with 0 warnings/errors; full solution tests passed 315/315 with 0 skipped (Domain 259, API 6, E2E 50). E2E took 6m29s and Domain 8m08s. Commit `c214cb35743284b7c336ae5d193d4026afb81da0` was pushed and remote-confirmed at 2026-09-23 20:48 UTC. No migration, tenant operation or production effect.

## Accounting Phase-0 source-integrity and clean-package checks (`c214cb3`)

- `ZeroAdjustmentPlan_ProducesSourceEquivalentPackage` passes end to end through mapping approval, an empty adjustment plan, finalization, adjusted-snapshot creation and financial-package build; no dummy journal is inserted and source balances remain unchanged.
- Focused PostgreSQL regressions also pass for equivalent normalized data with distinct raw-file hashes, one-source multi-entity batches producing separate datasets, mixed-entity direct import rejection, and mapping maker/checker self-approval denial. Combined focused result: 5/5.
- These checks confirm the guide's Phase-0 zero-adjustment, raw/normalized identity, entity-boundary and maker/checker foundations already exist in this checkout; the remaining accounting backlog still requires an AC-by-AC audit.

## AS-PAR-002 — Records archive route reauthorization (`72944dd`)

- `RecordsArchive.razor` now reloads and reauthorizes on route-parameter changes, immediately clears the prior manifest/entry projection, and fences stale reads so an earlier request cannot repopulate a newer route.
- Added `AS-PAR-002-ARCH-STALE-ROUTE-01`: the same browser document switches from an authorized synthetic archive to an unrelated client's archive, confirms profile/digest/entry data and archive headings are absent, then returns to the authorized archive without a document reload.
- Release build passed with 0 warnings/errors; fresh discovery is 314 (Domain 259, API 6, E2E 49); focused archive E2E passed 1/1, full E2E 49/49 and full solution tests 314/314 with 0 skips. EF reports no pending model changes. Commit `72944dd8f4d0ab408b0e257275d25ef9e1ca4f55` was pushed to `master` and remote-confirmed at 2026-09-23 20:24 UTC. No migration, tenant operation or production effect. AS-PAR-002 remains partial.

## AS-PAR-002 — Review-point revocation state clearing (`07b0e44`)

- A scope-denied or stale-generation review-point disposition now clears the protected point, status badge and staff actor context. The PostgreSQL-backed browser case revokes the exact synthetic grant while the page remains open, invokes Clear, verifies private comment/blocking state and actions disappear, and confirms no disposition was persisted.
- Added `AS-PAR-002-REV-REVOKE-01`; the CI expected discovery count increased to 312. Focused ReviewPoint journeys passed 2/2. The first full E2E run exposed a separate existing AccountingRecords navigation failure, fixed in the follow-up checkpoint below.
- Commit `07b0e447854eb83b53cae937f14beae1b0639efa` was pushed to `master`; `git ls-remote` confirmed it at 2026-09-23 19:33 UTC. No migration, tenant operation or production effect.

## AS-PAR-002 — Stable actor capture in accounting records (`ba4f81a`)

- `AccountingRecords.LoadCurrentQueueAsync` now uses its resolved actor local for authorization and every EF predicate rather than capturing mutable component state, preventing an overlapping in-app route load from nulling a query parameter.
- The existing `AS-PAR-002-ACCT-RECORD-TABS-01` browser case passed 1/1 after the fix. Full solution Release build passed with 0 warnings/errors; solution tests passed 312/312 (Domain 259, API 6, E2E 47; 0 skipped); EF reports no pending model changes. Fresh discovery reconciles to 312.
- Commit `ba4f81a08b48a006cbefdeb266c961cfee7cea84` was pushed and remote-confirmed at 2026-09-23 19:33 UTC. Hosted GitHub CI was not observed. No migration, tenant operation or production effect. AS-PAR-002 remains partial.

## AS-PAR-002 — ReviewPoint same-document route reauthorization (`07d1a35`)

- Replaced the initial-only load with parameter-change authorization, immediate clearing of the prior point, and generation fencing so stale reads/actions cannot repopulate a newer route. Queries use the resolved actor local, and actions stop updating the UI if the route generation changes while a command is in flight.
- Added `AS-PAR-002-REV-STALE-ROUTE-01`: within one browser document, the authorized synthetic point is followed by an unauthorized point ID; its private comment, status and action disappear, then routing back reloads the authorized point. The test checks a stable document token and no browser page errors.
- Focused ReviewPoint route/revocation tests passed 2/2; full Release build passed with 0 warnings/errors; discovery reconciles to 313; aggregate solution tests passed 313/313 with 0 skips; EF reports no pending model changes. Commit `07d1a352ffa16511ec2b7150f0f0b2a9201ec71d` was pushed and remote-confirmed at 2026-09-23 19:58 UTC. No migration, tenant operation or production effect. AS-PAR-002 remains partial.

## AS-PAR-002 — Audit-plan engagement scope and revocation (`eee833d`)

- `AuditPlan.razor` now checks the current persisted role grant for the exact engagement before reading plan data. Route changes clear the previous projection and draft fields; a generation fence prevents a slow earlier load or command result from repopulating a newer route. Scope-denied or stale-generation command results clear the protected page.
- Added `AS-PAR-002-AUDIT-PLAN-READ-01`: a granted staff identity can read its assigned plan, same-document navigation to a sibling engagement clears private data, revocation prevents the next risk write and leaves no persisted risk, and a user scoped to an unrelated client sees no plan marker. The test allows either stale-session revalidation or command denial after revocation.
- Verification: full Release build passed with 0 warnings/errors; discovery reconciles to 308 (Domain 259, API 6, E2E 43); focused audit-plan test 1/1; full standalone E2E 43/43; Domain 259/259 and API 6/6 in the attempted solution run. That solution-wide run had one intermittent failure in the existing accounting-record queue journey, which rendered its safe generic error state under concurrent project execution; no data was exposed. EF reports no pending model changes. No migration, tenant operation or production effect.
- Commit `eee833d680486c2d70dbe24bcfec93d6961aa8c9` was pushed to `master` and verified equal to `origin/master` at 2026-09-23 16:33 UTC. AS-PAR-002 remains partial.

## AS-PAR-002 — Invoice detail route isolation (`8871834`)

- `InvoiceDetail.razor` now reloads whenever the invoice route parameter changes, clears the previous invoice, line items, allocations, balance and command result first, and fences late reads/actions by route generation. The underlying billing service continues to perform the authoritative exact-client grant check; scope-denied or stale command results clear the page.
- Expanded `AS-PAR-002-INV-01` with two synthetic clients/invoices. The browser navigates in the same document from an authorized invoice to another client’s invoice, verifies both invoice projections remain hidden, then returns and confirms the authorized invoice reloads. Existing separate-identity denial coverage remains.
- Verification: full Release solution build passed with 0 warnings/errors; discovery remains 308; the full standalone E2E project passed 43/43 with 0 skips after the route change, and the final stale-command guard passed the focused invoice case 1/1. EF reports no pending model changes. The last full solution attempt still has the separately recorded intermittent accounting-queue E2E failure. No migration, tenant operation or production effect.
- Commit `887183489292a38a87eb8878f701fca48f96f1fd` was pushed to `master` and verified equal to `origin/master` at 2026-09-23 16:48 UTC. AS-PAR-002 remains partial.

## AS-PAR-002 — Engagement detail route isolation (`27ad0dd`)

- Expanded `AS-PAR-002-ENG-01` to open an authorized synthetic engagement, navigate in the same browser document to an unassigned engagement, and assert that the previous client, work-block state and private hold reason are absent. Returning to the authorized engagement restores its client and hold; a window token and browser diagnostics verify the transition remained in the same document without page errors.
- The PostgreSQL-backed browser test passed before any runtime edit and after expansion (1/1), so the existing page/router lifecycle already clears and reloads this projection. No runtime change was needed. Full standalone E2E passed 43/43 with 0 skips; full Release build passed with 0 warnings/errors; fresh discovery remains 308. Domain 259/259 and API 6/6 were last verified in the prior solution run, whose aggregate E2E had the previously recorded intermittent accounting-record safe load-error (42/43); the aggregate is not reported green.
- Test/catalog commit `27ad0dd5f69cee1858592c8cf8354260c53f2134` was pushed to `master` and `git ls-remote` confirmed the exact SHA at 2026-09-23 17:03 UTC. No migration, tenant operation or production effect. AS-PAR-002 remains partial.

## AS-PAR-002 — Financial package review queue refresh after revocation (`21b7487`)

- Added an explicit Refresh queue action to `FinancialPackageReviews.razor`. It immediately clears queue items, selections and preview, resolves the trusted current actor, and re-runs the existing scope-filtered `GetStaffQueueAsync`; query/connection exceptions are logged and shown as a retryable, non-disclosing message. The page shell remains mounted during refresh.
- Added `AS-PAR-002-FS-QUEUE-REVOKE-01`: an authorized synthetic reviewer opens a package queue, an administrator revokes its exact `AccountingReviewer` grant, then refresh removes the previous package from the still-open page and shows the authorization state without a browser-document reload. The regression failed before the refresh action existed and passed 1/1 after the fix.
- Verification: full Release build passed with 0 warnings/errors; fresh discovery reconciles to 309; Domain 259/259, API 6/6 and standalone E2E 44/44 passed in separate runs with 0 skips. The new case passed 1/1; EF reports no pending model changes. A prior full-solution concurrent run's intermittent accounting-queue error (42/43) is retained as historical evidence; the aggregate command was not rerun for this checkpoint.
- Commit `21b74874e644942dea74a373a1f7d9961f4bd972` was pushed to `master` and `git ls-remote` confirmed the exact SHA at 2026-09-23 17:30 UTC. CI discovery was updated to 309; hosted GitHub CI was not run/observed. No migration, tenant operation or production effect. AS-PAR-002 remains partial.

## AS-PAR-002 client financial-package view after revocation (`6a81076`)

- `ClientFinancialPackage.razor` now clears the previous package and review form before loading or after a scope-denied/stale-generation command result; authorization denial also drops the component's resolved actor. A focused PostgreSQL regression revokes the synthetic client grant, advances its session epoch, and confirms the stale review command records no decision.
- Added `AS-PAR-002-CLIENT-FS-STALE-ROUTE-01` and `AS-PAR-002-CLIENT-FS-REVOKE-01`. The same-document unavailable-route case confirms package identifiers and totals are not retained; the revocation journey reloads the exact package after grant removal and verifies the generic unavailable state, hidden decision form, and no review write.
- Verification at this checkpoint: full Release build passed with 0 warnings/errors; fresh discovery 311 (Domain 259, API 6, E2E 46); Domain 259/259, API 6/6, standalone E2E 46/46, no skips; the two focused E2E cases passed 2/2 and the Domain stale-generation/no-write case passed 1/1. `dotnet ef migrations has-pending-model-changes` reports no pending model changes. The final standalone E2E run passed after an earlier intermittent miss in an existing preparer/reviewer browser test was stabilized with a network-idle wait; the aggregate solution test command was not run.
- Source/test/CI/catalog/strategy commit `6a81076e60a0608a6624b534425f147272d771a8` was pushed to `master`; `git ls-remote` confirmed the exact SHA at 2026-09-23 18:27 UTC. No migration, tenant operation or production effect; hosted GitHub CI was not observed. AS-PAR-002 remains partial.

## AS-PAR-009 persisted proposal detail and scope checks (`5e3687b`)

- Replaced `ProposalDetail.razor` sample values and `Task.Delay` status changes with a firm-wide, internal-only authorized read of the persisted proposal, linked lead/client, opportunity, owner, reviewer metadata and revisions. Parameter changes clear the previous projection; a load-generation check prevents a late earlier request from repopulating a newer route.
- The PostgreSQL-backed browser journey verified persisted fee, terms, status and revision history; same-document unavailable-ID navigation clears prior data; a client-scoped RelationshipManager is denied; and revoking the firm-wide grant prevents the still-open circuit from reopening another proposal.
- Workflow mutations are intentionally not offered: current records lack persisted proposal authorship, independent pricing-review basis, validity and outbound-delivery receipt. Existing status-only commands would misrepresent review or email. The story is therefore still partial, not complete.
- Focused test passed 1/1; full standalone E2E passed 42/42 (4m44s), 0 skips; CI-equivalent discovery is 305; Release build passed with 0 warnings/errors; EF reports no pending model changes. The combined 305-case solution run was not repeated; Domain/API passed in the preceding 304-case combined run and are unchanged in this slice.
- Source/test/CI/docs commit `5e3687ba4ee9914aec5a09c478fc832db6f0e4e1` was pushed to `master` and confirmed with `git ls-remote` at 2026-09-23 14:43 UTC. No schema migration, tenant operation, email or production effect.

## AS-PAR-009 proposal authorship and reviewer separation

- New revisions persist the authenticated preparer. Review rejects self-approval and unattributed legacy drafts; an additive nullable foreign key preserves historical proposals without guessed authorship. Proposal detail displays the recorded author or explicitly marks a legacy author as unknown.
- `AS-PAR-009-PROPOSAL-MAKER-CHECKER-01` and `AS-PAR-009-PROPOSAL-MIGRATION-01` pass against PostgreSQL. The prior-schema test upgrades a synthetic legacy proposal and confirms status/fee survive while author stays null. Existing CRM browser setup uses a distinct Partner reviewer.
- Test/CI/docs commit `ef06660f0214d74da2914281141b55b456d128b9` was pushed to `master` and confirmed at 2026-09-23 15:31 UTC. Release build passed with 0 warnings/errors; discovery reconciles 259 Domain + 6 API + 42 E2E = 307; Domain 259/259, API 6/6 and E2E 42/42 passed in their project runs. EF reports no model drift. The loopback restore rehearsal passed against the existing 91-migration development DB; it did not apply the new migration there.
- Still partial: no firm-approved commercial pricing limits/basis record, explicit validity, durable delivery evidence, or version-bound client response authority/hash. Proposal review/send/response UI remains disabled. No tenant mutation, email, or production effect occurred.

## AS-PAR-002 financial-package route isolation

- Added `AS-PAR-002-FS-STALE-ROUTE-01`: a same-document navigation from an authorized synthetic package to an unavailable package ID must hide the prior package ID and statement totals and show the generic unavailable state.
- The characterization passed 1/1 before any runtime edits, confirming that the existing component clears the previous package projection on this route transition; no runtime change was needed.
- Test/CI/docs commit `5fd9f6971305f3999e2d02347549fda0a3c6ec24` was pushed to `master` and confirmed with `git ls-remote` at 2026-09-23 14:20 UTC. Discovery reconciles 257 Domain + 6 API + 41 E2E = 304; standalone E2E passed 41/41 (4m39s), combined solution passed 257/257 Domain, 6/6 API and 41/41 E2E (5m23s for E2E), no skips. Release build passed with 0 warnings/errors; the CI discovery rule matched 304; EF reports no pending model changes.
- No schema migration, tenant operation or production effect. AS-PAR-002 remains partial; continue the whole-application route/query/search/count/export/direct-command audit.

## AS-PAR-002 client portal PBC recipient-route isolation

- Added `AS-PAR-002-CLIENT-PBC-STALE-ROUTE-01`: navigate within the same browser document from a synthetic request assigned to the authenticated client to another request assigned to a different client identity; verify the generic unavailable view, both markers hidden and the window token retained.
- The focused characterization passed 1/1; the complete E2E project passed 40/40 with no skips. The existing page already clears/remounts route state, so this checkpoint intentionally contains no runtime change.
- The Release build passed with 0 warnings/errors; discovery reconciles to 303. Domain 257/257 and API 6/6 passed in the previous full-solution run and were unchanged here. The combined solution command was not rerun after its earlier E2E trigger assertion was corrected.
- Test/CI commit `5a03350f054fbdb09d8761d546fec106974a89e0` was pushed to `master`; `git ls-remote` confirmed the exact SHA at 2026-09-23 13:57 UTC. EF reports no pending model changes. No migration, tenant operation or production effect.

## AS-PAR-002 PBC inbox route scope and stale-state regression

- Added `AS-PAR-002-PBC-STALE-ROUTE-01`: from an authorized synthetic PBC inbox, the browser performs same-document history/popstate navigation to an unauthorized sibling engagement and confirms the old thread and sibling marker are absent while a window token proves the browser document remained active.
- Changed `PbcRequests.razor` to clear the prior scoped projection and repeat actor, engagement and exact role-grant resolution whenever route parameters change. Loading now resets in a `finally` block.
- The pre-fix same-document test timed out waiting for the denied-state heading, demonstrating missing parameter reauthorization. The corrected focused test passed 1/1; full standalone E2E passed 39/39, no skips. The Release build passed with 0 warnings/errors; Domain passed 257/257 and API 6/6 in the full-solution run; discovery reconciled to 302; EF reports no pending model changes.
- Source commit `d065008600670fa19e8ecbda6b6c97c9e38d5e36` was pushed to `master`; `git ls-remote` confirmed the exact SHA at 2026-09-23 13:44 UTC. The initial combined solution run's E2E attempt failed its earlier browser-trigger assertion; after correction, the focused test and full E2E project passed separately. The combined command was not rerun after correction.
- No schema migration, tenant operation or production effect. AS-PAR-002 remains partial.

## AS-PAR-002 accounting queue query and navigation regression

- Added `AS-PAR-002-ACCT-RECORD-TABS-01`: from the mappings queue, in-app navigation to Adjustments retains the browser document and renders the authorized synthetic journal `AJ-E2E-001`.
- Fixed the PostgreSQL EF translation failure by materializing distinct non-null journal period IDs before `Contains`; asynchronous queue-load exceptions are logged and presented as a recoverable page error instead of an indefinite loading state.
- Source commit `743b177c1530d64b9ce2a3ffd2a08ed983179a72` was pushed to `master`; `git ls-remote` confirmed the exact SHA at 2026-09-23 13:11 UTC.
- Focused browser regression passed 1/1; Release build passed with 0 warnings/errors; discovery 301; Domain 257/257, API 6/6 and full standalone E2E 38/38 passed; EF reports no pending model changes.
- One parallel full-solution invocation had a transient existing journal-revocation browser timeout; the isolated retry passed 1/1 and a subsequent complete E2E run passed 38/38. No schema migration, tenant operation or production effect. AS-PAR-002 remains partial.

Test-environment recovery verified at `2026-09-22T19:42:54Z`: the preceding run ended with 141 passed and 105 failed while PostgreSQL was shutting down. The server log records a smart shutdown request at `2026-09-22T19:25:04Z`; the initiator is unknown. Starting the stopped cluster on `127.0.0.1:5433` restored the test prerequisite. The complete Release suite then passed without application, test, assertion or runner changes; PostgreSQL remained available afterward. The full command and local ignored TRX artifact (`TestResults/failure-investigation.trx`) are recorded in `status.json`. Model drift and the 91 applied migrations were rechecked; external acceptance remains unchanged.

## Historical documentation-only E2E automation blueprint

Observed on 2026-09-22; this is separate from the historical runtime verification above.

- Added [E2E automation strategy and embedded CI examples](../testing/E2E_AUTOMATION_STRATEGY.md) and the [complete existing-test catalog](../testing/TEST_CASE_CATALOG.md).
- SDK 10.0.300 tool restore, locked restore and full solution Release build passed at `b8e4721` before fresh discovery. Source remained unchanged through `f64a1f4`; intervening commits touched only separate evidence documentation.
- Reconciled 246 expanded cases / 235 methods: 229 facts, six theories with 17 full data rows, 37 classes and 36 test-source files. Checked unique IDs, category totals, method multiplicities, exact theory values, source references, traits and 38 custom display names against source and fresh discovery.
- Validated relative links/anchors, Markdown fences/table widths and whitespace; parsed three YAML examples, syntax-checked 15 Bash blocks, three PowerShell blocks and embedded Python. `git diff --check` passed. Actionlint/ShellCheck were unavailable, and editor diagnostics were unavailable during language-server initialization; syntax/source checks are not cloud-workflow execution.
- No full-suite execution, EF drift run, restore drill, browser journey, live-provider operation, notification, Wiki publication, commit or push was performed for this documentation task. Existing evidence and acceptance statuses remain unchanged.
- At that historical point the API/E2E projects, Playwright package and workflow were proposals. They have since been implemented locally; a dedicated shard-manifest/orchestrator script and cryptographic per-host Data Protection isolation remain absent. Live acceptance still requires approved provider composition, runner, grants, custody and policy-required professional decisions.

## Prototype gap-closure scope authorization slice

Implemented in source checkpoint `master@670d5ecbf0f2b5eda928fe89a3234e0abf92a050`, pushed to `origin/master` and verified there at 2026-09-23 08:53 UTC. Documentation/evidence follow-up did not change runtime source.

- Tightened firm-wide authorization for recovery, firm-ledger, CRM, records-profile, consolidation setup, shared accounting catalogs and practice rate cards; a client/engagement-scoped administrator or manager can no longer mutate those firm-wide resources.
- Engagement grants must match both the stored engagement and its client parent. Translation preparation and approval now revalidate the active user/session epoch and require an explicit grant to that consolidation group.
- Financial-package review queues apply active client/engagement/firm grant filtering before the bounded 100-row page and load of review decisions; exact package checks remain in place. Review artifacts are fetched only after the stage-specific authorization succeeds.
- PostgreSQL regression batch: 39/39 passed. Full Domain suite: 253/253 passed; API suite: 6/6 passed. Release solution build passed with 0 warnings/errors; EF model-drift check reports no pending changes; `git diff --check` passed.
- Full solution E2E suite: 9/10 passed. `PracticeBillingLedgerJourneyTests.ProspectTimeBillingAndManualFirmCloseKeepTheirBoundaries` remains failing with `time.rate-missing`; it predates this authorization slice and is not reported as passed.
- On 2026-09-23, the user approved additive adoption of the parity baseline and continuation of AS-PAR-002. No approver name or job title was supplied, so none is attributed. The authoritative SPEC/WBS remain unchanged; this approval does not certify completion of AS-PAR-001's full traceability and review criteria.
- This verifies a bounded subset of AS-PAR-002 only; no overall story, live tenant gate, production readiness, commit or push is claimed.

## Group-scoped identity authorization continuation

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- Consolidation, currency-translation and accounting capability group authorization now use one shared check that re-reads the same-firm user, rejects disabled and client-classified identities, verifies the current session epoch and requires an unrevoked role grant for an existing group in that firm. Denials keep the established non-disclosing `scope.denied` response.
- Added a PostgreSQL regression proving stale-session, disabled-user and client-classified group actors cannot record capability acceptance; no acceptance evidence is written. It failed before the guard, then passed after the fix. The new test and existing foreign-operation translation regression pass 2/2.
- Final full Domain suite: 254/254 passed, 0 skipped; full solution Release build: 0 warnings/errors; EF reports no pending model changes; `git diff --check` and status JSON parsing pass.
- Remaining AS-PAR-002 route/read/UI-clearing and end-to-end scope coverage is not complete. The previously observed full-solution E2E `time.rate-missing` failure was not rerun during this slice. No schema migration, tenant operation, commit or push was performed.

## Finance invoice detail scope and denied-state clearing

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- Replaced direct firm-wide invoice/line/allocation reads in `InvoiceDetail.razor` with the finance-role and client-scope-checked `BillingService.GetInvoiceDetailAsync` query. The query authorizes the invoice's client before returning line text, allocations or balance.
- If the detail query is denied, the page clears invoice, line, allocation and balance state, hides actions and renders the generic unavailable message. Removed the fallback balance calculation that ran after a denied balance query.
- Added a PostgreSQL assertion for an authorized finance manager and a finance manager scoped to another client. Added a browser journey with synthetic invoice data: the correct manager sees the invoice; the other-client manager sees no invoice number, line description or `247.00` balance. Billing DB tests pass 2/2 and the focused Playwright journey passes 1/1.
- Final full solution Release build passes with 0 warnings/errors. The 254-test Domain run predates this last invoice-query/UI slice; affected Billing tests and the browser journey were rerun after it. No database model/migration change was made.
- Final-slice commands: `dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~BillingTests`; `dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --no-restore --filter FullyQualifiedName~FinanceManagerScopedToAnotherClientCannotViewInvoiceOrFallbackBalance`; `dotnet build AuditSphereOps.slnx --no-restore --configuration Release`.
- Remaining AS-PAR-002 audits across other routes, query/search/count/export/download paths, mutation transactions and denied-state UI behavior remain open; no whole-story completion is claimed.

## Additional scoped-read routes and review-point disposition

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- The client PBC request route now authorizes the exact client/engagement and current ClientUser grant before loading the request thread. A revoked/stale result on upload or reply clears the request, communication, upload, and browser-draft state; the open-circuit browser journey proves the reply is not persisted after revocation.
- Engagement detail and completion pages now require a current internal role grant covering the exact engagement before loading client names, holds, review-point counts/comments, representations, package approvals, or EQR state. Completion clears the protected projection when release preparation returns a scope/session denial.
- Client detail now requires a covering client-level grant, so an engagement-only grant cannot expose shared client contacts or sibling engagement metadata. A denied contact-creation result clears the loaded client projection.
- Review-point reads now require a current scoped internal grant. Clear/reopen moved from direct Razor EF writes into a PostgreSQL-transactional application command that locks/revalidates the engagement and point, rechecks current session and role grant, and converges on repeated same-state commands. Denial clears the page projection.
- Regression evidence: PBC service tests 8/8; review-point scoped/replay PostgreSQL test 1/1; combined `ClientScopeJourneyTests` browser suite 6/6 (authorized and denied client/engagement/review access plus mid-circuit PBC revocation); full solution Release build passed with 0 warnings/errors. No schema change or tenant operation was needed.
- Remaining AS-PAR-002 route/query/search/count/export/download/direct-command review and broader mutation stale-session/idempotency coverage are still open. These slices do not complete AS-PAR-002 or the prototype backlog.

## Finding detail scope and revoked-command protection

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- Finding detail now resolves only the stored client/engagement scope, checks a current internal role grant for that exact scope, and loads the finding text, amount, management response and engagement state only after authorization. A denied or missing record renders the same non-disclosing unavailable state; route parameter changes clear prior detail state first.
- If the guarded management-response command sees a revoked grant or stale session, the page clears the finding, engagement and draft projection. The transactional application command remains the mutation boundary.
- Regression evidence: finding detail browser journey passes for authorized staff and denies a manager assigned to an unrelated client without exposing finding text, response or amount; PostgreSQL verifies the response command is denied after grant revocation and the finding remains unchanged. `ClientScopeJourneyTests` passes 7/7; `AuditPlanningTests` passes 22/22; Release solution build passes with 0 warnings/errors.
- No schema migration or tenant operation was needed. Broader AS-PAR-002 query/search/count/export/download and remaining route/mutation audits are still open; no story completion is claimed.

## Audit population detail scope and linked evidence isolation

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- Audit population detail authorizes the stored client/engagement tuple before loading extraction metadata or linked content. Evidence links, source-receipt tokens, selections, selected items, item tests and review counts all use the same firm/client/engagement boundary; route parameter changes clear prior content before the next authorization check.
- Regression evidence: assigned staff can see the synthetic population and receipt, while a manager scoped to another client sees no extraction details, receipt token, filename or monetary total. The complete `ClientScopeJourneyTests` suite passes 8/8; `AuditPlanningTests` passes 22/22; Release solution build passes with 0 warnings/errors.
- No schema migration or tenant operation was needed. This route slice does not complete the AS-PAR-002 application-wide audit.

## Workpaper detail, durable draft and submission-history scope

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- Workpaper detail now authorizes its stored client/engagement tuple before loading the workpaper, durable draft, linked procedure or submission history. Every linked query is constrained to the same scope. If the draft command denies access, the page fails closed rather than falling back to content from the base row.
- A revoked/stale identity is rechecked on draft save, submission and discard failures; access loss clears workpaper, submission and editor state. Ordinary target-generation conflicts remain recoverable without discarding local draft text.
- Regression evidence: the browser journey confirms authorized staff can read the synthetic workpaper and frozen conclusion; an unrelated-client manager cannot. After mid-circuit grant revocation, the next autosave clears the editor and the attempted draft is not persisted. `ClientScopeJourneyTests` passes 9/9; `AuditPlanningTests` passes 22/22; Release solution build passes with 0 warnings/errors.
- No schema migration or tenant operation was needed. This route slice does not complete AS-PAR-002.

## Acceptance-decision route authorization

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- The decision page now checks the persisted Partner role grant against the exact client before reading the client generation or displaying the decision form. A client-scoped Partner remains permitted; an unrelated-client Manager sees only a generic unavailable state.
- Route parameter changes clear prior inputs and authorization state before reloading. If the transactional acceptance command reports a revoked grant or stale session, the page clears the form and generation projection. The command remains responsible for the final current-authorization check.
- Regression evidence: `AcceptanceDecisionRequiresPartnerGrantForExactClient` and the full `ClientScopeJourneyTests` browser class pass 10/10; PostgreSQL `AuditPlanningTests` pass 22/22; full Release solution build passes with 0 warnings/errors; `git diff --check` passes.
- No schema migration or tenant operation was needed. This route slice does not complete AS-PAR-002.

## Accounting evidence queue scope

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- Replaced the queue's role-name shortcut with `AuthorizationDecision` using the persisted, current internal role grants. Visible client and engagement IDs now derive only from active grants for the queue's allowed accounting roles; a scoped Administrator or Partner no longer becomes firm-wide by role name alone.
- Regression evidence: the browser journey seeds synthetic specialist accounting evidence. A Partner scoped to its client sees the row; a Partner scoped to a different client sees an empty queue and no evidence reference. `ClientScopeJourneyTests` passes 11/11; `AuditPlanningTests` passes 22/22; full Release solution build passes with 0 warnings/errors; `git diff --check` passes.
- No schema migration or tenant operation was needed. Mid-circuit read-only view refresh/revocation handling and the remaining AS-PAR-002 route/query/export/download audit remain open.

## Assessment detail scope resolution

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- Assessment routes now resolve only the client ID needed for the scope check, including the compatibility route keyed by acceptance-decision ID. The persisted current internal grant is checked before loading the client profile, full decision, workspace state, questionnaire responses or specialist clearances.
- Route-parameter changes clear the previously loaded client, decision, workspace, question and clearance projection before reloading. The exact decision is loaded only after authorization succeeds.
- Regression evidence: a client-scoped Partner can open a synthetic decision-ID route and sees its client registration/decision status; an unrelated-client Manager receives the unavailable state without either value or workspace details. `ClientScopeJourneyTests` passes 12/12; full Release solution build passes with 0 warnings/errors; `git diff --check` passes.
- No schema migration or tenant operation was needed. This does not close AS-PAR-002; the broader read/export/download/mutation and mid-circuit revocation audit remains open.

## Accounting workspace and record-queue grant scope

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- The accounting workspace and mapping/journal/difference queues now authorize the current internal actor/session against an active accounting-role grant before loading data. Their client and engagement projections use only active grants for those roles; engagement grants are accepted only when the stored client matches the engagement's persisted parent.
- Applied the same parent-consistency check to the accounting evidence queue. A grant attached to a different engagement/client pair can no longer expand these views to that engagement's parent client.
- The exact accounting-period detail route checks the current session and a covering client-level accounting grant before loading its projection. An engagement-only grant is not widened to a client-level period record; route changes clear the previously displayed period before the next scope check.
- Period roll-forward and restatement queues now require a current internal accounting grant and derive client-level period visibility only from firm-wide or direct client grants. Engagement-only grants cannot expose a client-owned period; restatement approval controls are limited to reviewer grants for the exact client or firm-wide scope. Selection loads reauthorize the requested client before reading closed periods/packages, and denied selection clears dependent data.
- Regression evidence: the PostgreSQL-backed Playwright journey combines a direct Partner grant, an unrelated non-accounting Staff grant, and a deliberately mismatched Partner engagement grant. The workspace shows only the directly assigned client; its unrelated period and evidence marker stay hidden; the assigned client's difference remains visible while the unrelated client's difference, name and evidence reference stay hidden. A route-change journey opens an assigned period, then verifies that its content clears and an unrelated client period is denied. A period-maintenance journey verifies that a direct client Partner sees the closed period on both roll-forward and restatement pages, while an engagement-only AccountingPreparer sees neither that period nor the client on either page. `ClientScopeJourneyTests` passes 15/15; Release solution build passes with 0 warnings/errors; `git diff --check` passes.
- No schema migration or tenant operation was needed. Mid-circuit read-only revocation handling and the wider AS-PAR-002 route/query/export/download/mutation audit remain open.

## Firm-ledger and commercial lead read authorization

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- Firm-ledger reads now use the existing `FinanceManager`/`FinanceReviewer` role policy and require an unscoped firm grant. Audit Partner or Administrator role names alone do not provide firm-ledger access. The close-period action is displayed only to a firm-wide `FinanceReviewer`, matching `LedgerService.CloseFiscalPeriodAsync`.
- Commercial lead reads now use the same `Administrator`/`Partner`/`Manager`/`RelationshipManager` role set and firm-wide requirement as the CRM commands. The shared CRM command authorization also rejects client-classified identities; denied or stale command results clear the page projection.
- Synthetic PostgreSQL-backed Playwright coverage verifies a firm-wide FinanceReviewer can read the firm period/account, while a client-scoped FinanceManager and firm-wide audit Partner cannot; a firm-wide RelationshipManager can read leads, while a client-scoped RelationshipManager and a client-classified identity with an erroneous firm-wide RelationshipManager grant cannot. `ClientScopeJourneyTests` passes 16/16, the focused firm-page journey passes after the final regression addition, `PracticeCrmTests` passes 9/9, and Release solution build passes with 0 warnings/errors.
- No schema migration or tenant operation was needed. Remaining AS-PAR-002 work includes other route/query/search/count/export/download and direct-command paths, broader stale-state clearing, and a complete whole-application mutation/idempotency review.

## Practice-time queue scope

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- `PracticeTime.razor` now authorizes the current internal actor and derives work/review scopes only from active grants for the corresponding roles. Engagement grants must match the persisted engagement-to-client parent; engagement-only access no longer expands into sibling tasks or submitted narratives. Client reporting periods are shown only for firm-wide or direct client grants.
- Task and time-entry projections are limited to visible exact client/engagement tuples. Review queues additionally require an applicable manager/partner/admin grant. A non-firmwide actor must select an authorized client period to create a client-scoped task; unscoped task creation requires a current firm-wide grant. `PracticeTimeService` applies the same internal/firm-wide boundary to commands.
- PostgreSQL-backed browser coverage proves an engagement Manager sees its assigned task and narrative but not a sibling engagement's task/narrative or the client-level period. `PracticeTimeTests` passes 7/7; the practice billing/ledger browser journey passes 1/1 after stabilizing its status selectors and case-insensitive CSS-transformed status assertion. The full `ClientScopeJourneyTests` suite subsequently passes 19/19.
- Final Release solution build passes with 0 warnings/errors; EF reports no pending model changes; `jq empty` and `git diff --check` pass. No schema migration, tenant operation, commit or push was performed.
- AS-PAR-002 remains partial: other route/query/search/count/export/download/direct-command paths, stale rendered-state revocation handling, broader mutation/idempotency review and corresponding HTTP/browser coverage remain open. External gates remain unchanged.

## Advanced consolidation read authorization

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- `AdvancedConsolidationWorkflow.razor` now gates its scope name, method, counts, schedules, execution summaries and source-manifest projection through the existing `AuthorizationDecision.AuthorizeGroupAsync` helper. That check re-reads the same-firm active internal user and session epoch, rejects client-classified identities, verifies the group exists, and requires an active role grant for that exact group.
- Added a PostgreSQL-backed Playwright negative case with an intentionally erroneous `AccountingPreparer` group grant on a Client identity. The route shows the generic unavailable state and exposes neither the synthetic group name nor scope ID. The focused test passes 1/1; the full `ClientScopeJourneyTests` suite passes 19/19.
- Full Release solution build passes with 0 warnings/errors; EF reports no pending model changes. No schema migration or tenant operation was needed. AS-PAR-002 and all external gates remain open as previously recorded.

## Firm administration and Microsoft 365 setup read authorization

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- Firm administration and the Microsoft 365 setup page now use `AuthorizationDecision` with an explicit firm-wide `Administrator` role, `InternalOnly`, and current session validation before exposing role rosters, safety state or saved folder templates. The one-time Microsoft bootstrap eligibility path remains separately bound to the configured tenant/object identity and its existing setup capability.
- Added a PostgreSQL-backed Playwright denial case for a Client identity with a deliberately erroneous firm-wide Administrator grant. It sees only the generic unavailable state, not firm safety, role roster or its own user record. `ClientScopeJourneyTests` passes 19/19; `M365SetupJourneyTests` passes 1/1.
- Full Release solution build passes with 0 warnings/errors; EF reports no pending model changes. No schema migration or tenant operation was needed. The whole-application AS-PAR-002 audit remains partial.

## Implemented local capability

- Microsoft 365 onboarding foundation: protected, resumable setup drafts; hashed single-use bootstrap capability; configuration-required UI; exact `(tid, oid)` roster/sign-in role assignment with scoped grants, revocation and last-administrator protection; truthful client workspace state; immutable allowlisted client/engagement folder templates; and exact connection-revision verification/activation evidence. These are local control-plane capabilities only; no Graph directory picker, SharePoint provider effect or fabricated tenant verification is enabled.

- Firm/client/engagement scope authorization, durable operations, trial-balance intake, audit planning, evidence submissions, package generation, records/archive lineage, recovery quarantine, and provider safety fences.
- Durable operations expose redacted administrator recovery state and explicit cancellation dispositions; queued work without a lease can be cancelled atomically, is excluded from worker claims, and cannot publish a partial result. Active work remains subject to lease expiry and reconciliation because cancellation cannot prove an already-started effect did not occur.
- Client accounting profiles, periods, books, chart-of-accounts mappings, versioned trial-balance profiles, signed-net/debit-credit normalization, and atomic multi-entity batches.
- Capability profiles accept only ENTITY_REPORTING/AUDIT_ONLY client scopes or GROUP_REPORTING group scopes; mismatched or unknown service kinds are rejected.
- Client ENTITY_REPORTING and AUDIT_ONLY capability profiles now require an explicit approved service route and affirmative firm acceptance decision; staffing roles alone cannot bypass a missing or prohibited service decision. External component packs remain usable without client-bookkeeping migration.
- Client accounting setup/reporting objects default blank currency input to QAR; raw source/import currencies and FX policy currencies remain explicit.
- Analytical reviews default blank reporting currency to QAR only within the selected client reporting period, retain a deterministic input snapshot and replay hash, disclose negative/seasonal movement flags, and retain journal-risk sample selection, management explanation and corroboration fields.
- Secured analytical-review aggregate summaries enforce client/engagement or explicit group scope, honor effective group membership dates, and return only period/currency totals and counts without client or component identifiers.
- ECL persists an explicit booked amount and calculates the difference against it; ECL and inventory assessments can link only to a non-void proposed adjustment in the same scoped engagement, with legacy ECL booked amounts backfilled from management amounts.
- Analytical-review approval requires and persists a reviewer conclusion alongside the exact replay snapshot/hash; missing conclusions fail closed after source/generation freshness checks.
- Direct and streaming GL imports preserve optional service dates through canonical digests and archive lineage.
- GL completeness bridges optionally bind the approved prior-period TB, persist per-account opening-plus-movement residuals and disclose missing opening or malformed journal evidence.
- Zero-adjustment plans preserve the validated source exactly; posted journals apply only when NOT_REFLECTED, REFLECTED applies zero, and UNKNOWN/PARTIALLY_REFLECTED or changed source decisions block finalization.
- Source-bound reconciliation approvals re-check the exact dataset/batch digest and client input generation, marking changed reconciliations STALE before approval.
- Client-scoped accounting dimension definitions cover branch, cost centre, department, project and intercompany counterparty codes; nonblank GL dimension values are rejected unless defined for the client, including bounded streaming imports.
- Client chart hierarchy parent lookups are restricted to the target chart version; cross-version parent references and posting-account parents are rejected.
- Draft taxonomy nodes can be added incrementally with parents from the same taxonomy version; cross-version parents and cycles are rejected.
- Trial-balance CSV/XLSX imports require and persist the selected reporting period, optional reporting book and normalized basis; import validation checks period currency/basis/book scope, and completeness/TB reconciliation rejects sources with mismatched stored context. Legacy direct fixtures remain nullable for additive migration compatibility.
- Typed GL import, bounded paged reads, account-by-account trial-balance-to-GL completeness bridges, reconciliation workbenches, generation-bound ECL/inventory/specialist/analytical/journal-risk workbenches, and typed asset/payroll/loan/equity/related-party/tax/going-concern forecast schedules.
- Equity, notes, comparatives, closed-period restatement lineage, restricted same-currency consolidation, journal lineage, and close checks.
- Immutable package-review decisions for management, accounting, and partner stages. Decisions are bound to the exact package revision, generation, package hash and persisted rendered-artifact hash/version; evidence modes remain separated; append-only database protection is enforced. Review fails closed when the exact framework/template artifact has not been rendered.
- Consolidation perimeter approval requires an independently accepted group capability profile for the selected method; capability-preparer self-approval is rejected. Component approval requires current management, accounting and partner decisions for the exact package.
- Consolidation component submissions must also match the package's period basis, taxonomy version and mapping-version ID; the deterministic run manifest preserves that exact component lineage.
- Mapping versions bind the exact approved client chart version whenever a client chart exists; missing, out-of-scope or period-ineffective chart applicability is rejected, shown in the mapping workbench, and preserved in records export.
- Externally prepared component packs use typed raw/normalized digests, line-level reconciliation, independent approval, immutable submitted/returned/resubmitted/approved history, and an explicit approved compatibility bridge when dates or bases differ. Only approved, reconciled packs can enter consolidation, and records exports preserve pack provenance/history.
- Consolidation run manifests include component package hashes; approval recomputes current package, intercompany and group-journal inputs and blocks stale runs until a new run is built.
- The bounded foreign-operation profile pins an approved rate set and translation policy to the scope, requires maker/checker approval for each foreign component translation, preserves per-line source/FX lineage in the deterministic manifest, and blocks missing or stale rates/packages. Full FX remeasurement/reserve, NCI, acquisition, ownership-change, nested-group and complex-elimination methods remain disabled pending approved method-specific fixtures.
- The enabled FX profile accepts only explicit `DIRECT` rates; unsupported inverse semantics are rejected rather than silently multiplied with the wrong direction.
- Exchange-rate sets retain immutable version metadata, source, approval and effective date ranges; each observation retains its direction, rate date and rate type, observations outside a declared range are rejected, and scope/translation approval accepts only rate types declared by the approved policy.
- Currency remeasurement, foreign-operation translation and display-only conversion have distinct deterministic calculators. Persisted foreign-operation results retain separate foreign-exchange and rounding adjustments, and approval recomputes both from the exact source amount and approved rate; translation reserve rollforward math is available as a separate method-specific calculation core but is not enabled as a production profile without approved fixtures.
- Advanced consolidation calculation helpers cover acquisition goodwill/bargain purchase with fair-value adjustments, NCI rollforward, ownership changes/disposals, nested double-count detection and asset-transfer/tax elimination math. Approved advanced-method schedules now persist canonical source/input digests, validate method-specific inputs and approved journal/FX evidence at approval, and the guarded execution path persists balanced current/comparative statements, method output manifests and hashes only after current component, group-revision and method-owner checks. Separate reviewer approval revalidates current component-pack inputs, method journal evidence and statement evidence before publication. Repeated execution converges on one verified row. Golden fixtures and PostgreSQL schedule/journal/execution journeys cover all five advanced methods. IFRS method-owner scope is recorded in `STE-METH-APP-001-addendum`; browser and external acceptance remain required before any advanced profile is treated as fully enabled.
- The consolidation workbench links each advanced scope to a scope-bound browser workflow for source-manifest/input drafting, guarded schedule submission, separate schedule/execution approval and execution status. Draft inputs restore locally, required fields are marked, and no browser action bypasses server-side maker/checker or readiness gates. Built-in-browser acceptance now covers all five advanced methods (`FOREIGN_CURRENCY_RESERVE_V1`, `ACQUISITION_NCI_V1`, `OWNERSHIP_CHANGE_V1`, `NESTED_GROUP_V1` and `ASSET_TRANSFER_ELIMINATION_V1`) with approved component/journal or FX fixtures: the preparer submitted each schedule, an administrator approved each schedule, verified execution produced balanced `0.00 / 0.00` comparative/current totals, and an independent reviewer approved each execution. The main workbench reports `1 / 1` executions and `APPROVED` readiness for every advanced scope; no automatic production/profile enablement is exposed.
- FX rate observations and translation-policy codes return controlled idempotency conflicts when repeated, rather than relying on raw database exceptions.
- Shared reporting taxonomy overlays accept only generic industry/group scopes; a client-scoped overlay is rejected so client-specific context cannot enter a firm master template.
- Group membership changes advance a durable group revision; consolidation scopes pin that revision and reject approval/calculation after a perimeter change while preserving historical memberships, scopes and runs.
- Component, translation, intercompany-match and group-journal commands recheck the pinned group revision and fail closed instead of writing stale-scope evidence after a perimeter change.
- Approved ownership edges are retained as evidence, but consolidation scope approval rejects intermediate/nested hierarchies for the enabled calculators until a method-specific calculation and fixture exist; this prevents double counting by construction.
- Structured records exports preserve the related group perimeter, membership, scope, component, ownership/intercompany, consolidation journal/run, exchange-rate and translation lineage without copying unrelated client workpapers.
- Approved-prior-scope group roll-forward preserves the exact prior run hash, approved FX lineage hash/reserve and recurring-elimination manifest; prior journals are lineage only and are not auto-applied.
- Accounting evidence links are typed, scope-checked and bound to reviewed audit procedure results; the staff evidence queue exposes only the actor's explicit client scope, and records export preserves typed accounting lineage.
- Period close runs under a row lock, blocks matching financial packages without current management/accounting/partner approval, and authorized reopen creates an immutable append-only amendment record with the new working revision.
- Controlled roll-forward creates a new draft period, copies prior reporting books as drafts, and creates a hash/evidence-bound opening bridge without copying prior approvals.
- The accounting evidence queue links reviewed accounting evidence to the exact associated audit workpaper, and the accounting dashboard exposes scoped period status plus roll-forward/restatement handoffs.
- The accounting evidence queue treats firm-wide, direct-client and exact-engagement grants distinctly, filters every listed evidence source accordingly, and shows the engagement context beside the client and period; engagement-only access cannot widen to sibling engagements.
- The accounting workspace applies the same direct-client versus exact-engagement boundary to engagement labels, package summaries, imports and sealed-import counts, so a selected context cannot silently widen to sibling engagements.
- The explicit Development/Test-only browser harness seeds a separate `auditsphere_browser` database with a QAR SME, unrelated clients whose identical account code remains client-scoped, a basic same-currency group and an enabled foreign-operation group. Built-in-browser journeys verified client switching, QAR/USD context, exact group scope versions and approved FX policy/rate status; the fixed development identity refuses Production startup, cannot coexist with OIDC and accepts only local return URLs.
- Portfolio projections apply the same active firm-wide, direct-client or exact-engagement grant boundary to counters, holds, durable operations, release candidates and financial-package lists; the client index adds scope-bound search and CSV export, engagement-only grants cannot expand to sibling engagements, and actors without an explicit scope see no firm data and receive actionable guidance.
- The shared layout exposes current-route navigation for the implemented accounting, evidence, package-review, roll-forward, restatement, consolidation and journal workbenches; unsupported workbenches are not presented as links.
- The accounting workspace pins a selected visible period's firm, group, legal entity, engagement, period, book, currency and package version in a draft-restored context header; the selection is display-only and does not broaden authorization.
- The accounting workspace exposes exact links to the selected package and mapping version when those records exist, alongside scoped TB/GL evidence and package-review routes; missing records remain non-clickable rather than becoming guessed links.
- Work tasks can persist an optional exact client reporting-period link and optional `DueDate`; the practice-time page creates these tasks with the same explicit grant boundary, and the accounting dashboard/period detail show the persisted owner, status and deadline before falling back to PBC values. Cross-client period links are rejected by the service and covered by PostgreSQL regression tests.
- Accounting dashboard, period roll-forward and restatement data loaders fail closed on an empty grant set and include only clients covered by an explicit firm-wide, client or engagement grant; the dashboard surfaces the active scope description and missing-scope guidance. Period-linked workflow tasks use the same exact period scope and do not broaden authorization.
- Client-safe validated-package view and signed-in management acknowledgement are available at the restricted client portal route. The portal exposes statement totals and package metadata only; internal review history and workpapers remain staff-only.
- Assigned staff can create file requests only for active, authorized engagements, select the client portal owner and reviewer, and queue description-rich email notifications containing an authenticated portal upload link. Staff and client replies are retained in one append-only timeline beside upload events; the client can upload through the existing request-bound capability, while verified staged files are downloadable only through a fresh staff scope check. An isolated Acceptance worker can deliver queued notifications through mailbox-scoped Microsoft Graph, SMTP or Resend configuration; secrets remain external, unknown outcomes stop without an automatic duplicate send, and local request creation never claims external delivery.
- An internal package-review queue lists only current validated packages in the actor's authorized client/engagement scopes and routes reviewers to the exact-version package surface.
- A validated financial package can now become a release candidate only through the existing guarded approval/release path; the candidate records `FINANCIAL_PACKAGE`, exact package revision/generation/hash, current management/accounting/partner decisions, and the normal checkpoint gate.
- The completion screen exposes package-candidate preparation only to partner/administrator actors after all three current package reviews are approved; repeated preparation reuses the exact candidate.
- ECL and inventory evidence records capture the reconciliation source hash and client input generation; review blocks when either source lineage or generation is stale.
- The enabled ECL and inventory valuation profiles have PostgreSQL golden fixtures and boundary coverage: expected loss, NRV/cost valuation, zero-input behavior, unsupported ECL methods and negative inventory inputs are verified without enabling unapproved methods.
- Bounded GL chunk intake with canonical content digests, transactional batch locking, idempotent retries, contiguous finalization and persisted accepted-count reconciliation.
- Source-bound GL reconciliations reject a sealed batch whose reporting period or currency differs from the selected period; PostgreSQL regression coverage passes.
- GL-backed bank-ledger schedules require a sealed, scoped, hash-matching GL import batch with complete selected-account coverage; the persisted GL control total is derived from source lines and caller mismatches fail closed. PostgreSQL regression coverage and migration `20260921212202_ResolveScheduleControlSource` pass.
- Reconciliation items are bound to the exact source currency, reject future item dates and missing dispositions, normalize accepted currency codes, and retain explicit receivable/payable ageing basis, rule version, bucket, credit treatment and paired settlement evidence; PostgreSQL regression coverage passes.
- Scoped audit-difference summaries preserve gross absolute totals and signed/net totals by currency, with corrected and unadjusted subtotals so offsetting differences remain visible; PostgreSQL regression coverage passes.
- Bounded journal-risk analysis returns deterministic, criteria-versioned review indicators for manual, year-end, high-value, reversal and missing-source-origin journals, with scoped source-origin and debit-amount evidence; it does not make an automatic fraud finding.
- Linked audit differences now retain a `journal-impact.v2` payload from the exact journal revision and approved mapping lineage, including account/statement effects, profit and equity totals, disclosure buckets, explicit unmapped status and a payload hash; PostgreSQL regression coverage passes.
- Difference correction governance now retains optional materiality and qualitative concerns, supports proposed/agreed/rejected/applied-in-reporting/reported-posted-external states with reviewer reasons, and rejects stale, unsupported or hash-mismatched `journal-impact.v2` evidence before verified-reflected status; PostgreSQL regression coverage passes.
- Bank reconciliation now binds an independently approved ledger schedule and bank statement schedule, persists typed ledger/statement/timing/proposed-correction items, computes the unexplained residual without netting proposed corrections, requires same-engagement draft-journal lineage for proposed items, and blocks approval or completion while unreconciled; PostgreSQL regression coverage passes.
- GL completeness calculation can be enqueued as a local durable operation, with sealed-source revision fencing, operation completion lineage and repeat-enqueue idempotency; PostgreSQL regression coverage passes.
- Financial-package builds can be enqueued as local durable calculations, fenced to the approved mapping revision and finalized plan, committed atomically with operation completion, and re-enqueued idempotently; PostgreSQL regression coverage passes.
- Financial-package records inherit the source dataset's selected reporting period, optional book and normalized basis, validate period dates and book/basis/currency lineage, persist the context with scope FKs, and include it in the deterministic package hash; legacy direct fixtures remain nullable for additive compatibility.
- Context-bound adjustment journals inherit and persist the validated source dataset's period, book, basis and currency, reject a book from another period, and preserve that reporting context through management decisions, posting, reversal and source-reflection lineage; PostgreSQL regression coverage passes.
- Financial-package rendering can be enqueued as a local durable calculation, fenced to the exact package revision, and records the deterministic artifact digest for later byte verification; PostgreSQL regression coverage passes.
- Exact canonical package artifacts can be exported through pinned deterministic XLSX, DOCX and PDF renderer versions. Formula-shaped client text remains literal; the workbook preserves exactly one renderer-owned `COUNTA` formula, and runtime validation rejects every other formula, VBA project and external workbook part. The PDFsharp/MigraDoc profile embeds its OFL font, canonicalizes generated metadata and subset identifiers, preserves Unicode text, paginates on A4 and was visually checked after rasterization. Repeated rendering is byte-identical, and every format is persisted against the exact package revision/generation/hash/framework/template. The staff package page exposes tooltip-guided downloads with non-disruptive status handling. Approved business templates remain pending, and live provider/signing/records gates remain independent.
- Financial-package mappings now require the approved taxonomy statement section; package validation records separate statement cross-cast, accounting-equation, equity/profit, comparative-consistency and note-to-face outcomes, and rendered artifacts include adjusted-snapshot, mapping-version and adjustment-plan lineage identifiers. Exact UTF-8 artifact bytes are persisted with framework/template versions and SHA-256 lineage, and package-review decisions reference that artifact; PostgreSQL financial-statement regressions pass.
- Legacy financial packages retain their original template, calculation-engine version and calculation hash beside newly versioned canonical packages; the zero-adjustment compatibility regression verifies both identities remain readable without mutation.
- Legacy trial-balance period links and mapping chart links are backfilled only from a single exact candidate. Zero or multiple matching periods/charts leave the nullable legacy field unchanged and create append-only `AccountingBackfillQuarantine` evidence; the migration-from-previous-schema PostgreSQL fixture verifies both the resolved and quarantined paths.
- The accounting workspace exposes scoped COA/mapping, adjustment-journal and difference queues with exact-record links, removes the invalid unscoped journal route, and shows a period workflow dashboard with required/complete/stale/blocked counts, role-based next-owner guidance, persisted period-linked work-task or PBC owner/due-date values when available, an existing-PBC handoff link, and an explicit `Not recorded` due-date state when no persisted task/PBC due date exists. Its no-package/no-mapping fallback opens a grant-checked read-only period detail with exact task/PBC handoffs. Release build and the 220-test PostgreSQL suite pass at `5074abc`.
- Group consolidation exposes perimeter, component-pack, FX, intercompany and elimination tabs with persisted scope counts/statuses; same-currency and unsupported-method boundaries remain explicit. Release build and the 220-test PostgreSQL suite pass at `5074abc`.
- Package-review selection is draft-retained through the shared storage fallback layer and offers a non-mutating preview that separates eligible stages from partner-role blockers; it never records a partial approval. Release build and the 220-test PostgreSQL suite pass at `5074abc`.
- The PostgreSQL-backed accounting benchmark exercises four clients, 2,000 transactions, 8,000 GL lines, parallel enqueueing, two concurrent durable workers, a 32-line group calculation, six-decimal/high-magnitude amounts and paged reads; one observed run measured enqueue 147.8 ms, worker processing 134.4 ms, first page 43.8 ms and group calculation 3.2 ms.
- Blazor status surfaces for the implemented workflows, including period restatement and truthful release/package gate state.
- Shared Blazor form UX covers contextual action/field tooltips derived from labels/placeholders/IDs, required and optional markers, accessible guidance, stable form names/autocomplete metadata, a keyboard skip link, visible focus-visible states, non-disruptive invalid-field status, and localStorage draft autosave/restore across card and standalone forms. Drafts flush on input/change, tab backgrounding, pagehide and beforeunload through one lifecycle handler; generated scopes avoid repeated-heading collisions, including dynamically added cards; checkbox/radio values restore correctly; restored values raise both native input and Blazor binding change events; and autosave includes controls disabled during an in-flight action. When localStorage is blocked or full, sessionStorage is tried before the in-memory page-session fallback, and reduced durability is reported without interrupting the workflow. Server-backed workpaper drafts remain authoritative; browser file bytes and release keys are intentionally excluded from local storage.
- Adjustment-journal instructions are exported only after exact client/engagement authorization, an accepted source with raw and normalized digests, matching period/book basis and currency, the exact journal revision, and accepted or partial management evidence are re-read. The controlled CSV is formula-neutralized and explicitly marked as not proof of external posting; the journal page exposes tooltip-guided download and non-disruptive status/error handling.
- The staff financial-package page rechecks exact client/engagement authorization and displays/downloads only the persisted artifact bound to the current package revision, generation, hash and template; release and delivery remain separate controls.
- Period roll-forward and restatement selection loads use a generation guard and a visible loading state, lock dependent selectors during the request, and prevent older async responses from replacing a newer client/period selection; the existing draft autosave remains the source of unsaved form resilience.
- The mapping workbench shows immutable current-vs-prior allocation changes, exact-dataset/chart/taxonomy applicability, and bounded token suggestions for unmapped accounts; candidates remain review-only, ambiguous matches are labeled, and no suggestion mutates allocations.
- Consolidation automatic matches now require an explicit enabled elimination nature (receivable/payable, revenue/expense, dividend or investment/equity); outside-perimeter reviews remain review-only, approved group-only journals remain distinct, and the selected nature participates in the deterministic run manifest. Unsupported legacy natures fail closed.
- The loopback restore drill now reconciles accounting package/artifact, consolidation scope/run/line and external component-pack manifests, and fails closed on duplicate release-delivery identities; the current rehearsal restored 91 migrations through `20260922140527_M365InvitationEvidenceAction`; external checkpoint custody and production RPO/RTO remain separate gates.

## Scoped release and portfolio read authorization

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- Portfolio projections now derive firm, client and engagement scopes only from active grants that pass `AuthorizationDecision` for the current internal user/session and the permitted portfolio roles. Engagement scopes are constrained to their stored client parent; firm-only durable-operation counts are no longer included for scoped users. A synthetic Client identity with an erroneous firm-wide Staff grant sees no private portfolio client.
- Release-candidate detail authorizes the stored firm/client/engagement tuple before reading checkpoint, records-protection or signature-lineage metadata. A same-client sibling-engagement Staff identity sees only the generic unavailable state; candidate ID, artifact digest, profile ID and checkpoint reference are absent. The assigned Staff route still renders the expired attestation truthfully and prevents release.
- The period-reopen browser fixture now grants the client-level accounting scope required by its client-owned period detail; the page continues to deny engagement-only users.
- Verification at 2026-09-23 08:46 UTC: full Release solution build, 0 warnings/errors; API tests 6/6; PostgreSQL Domain tests 257/257; Playwright E2E tests 31/31; 294 discovered; EF model-drift check reports no pending changes. No schema migration or tenant operation. The changes are uncommitted, and AS-PAR-002 remains in progress.

## AS-PAR-002 — PBC origin and client portal list scope

Observed on 2026-09-23 in the uncommitted `master` worktree at `f64a1f4ef05b`.

- The browser PBC chunk endpoint now requires a valid same-origin `Origin`; absent, malformed or foreign origins fail before staging. HTTP regression coverage confirms unauthenticated, missing-origin and forged-origin uploads create no chunk receipt or staging directory.
- Client portal PBC-request and validated-package list queries now apply the current `ClientUser` grant at exact client/engagement scope in SQL before loading rows. Sibling-engagement PBC request metadata is absent while the assigned request remains visible in a PostgreSQL-backed Playwright journey.
- Verification for source checkpoint `670d5ec`: API suite 6/6; full PostgreSQL Domain suite 257/257; full Playwright E2E suite 31/31; full Release build 0 warnings/errors; EF model drift none; 294 solution cases discovered; `git diff --check` and status JSON parsing pass. The same commit was pushed to `origin/master` and confirmed with `git ls-remote`. No schema migration, tenant mutation or external-provider acceptance. AS-PAR-002 remains partial.

## AS-PAR-002 — Consolidation overview group authorization

Implemented in source checkpoint `master@f1467e620b9400923fb324fcb57e66d9ced3eba6`, pushed to `origin/master` and verified there at 2026-09-23 09:10 UTC.

- `/app/consolidation` now validates each candidate group grant through the existing `AuthorizationDecision.AuthorizeGroupAsync` policy before loading group names, scopes or readiness counts. Rate-set and translation-policy lookups are limited to IDs referenced by the authorized scopes.
- The PostgreSQL-backed browser regression confirms an assigned staff member can view the granted group, while a client identity carrying an erroneous `AccountingPreparer` group grant is denied both the overview and the advanced workflow without the private group name or scope ID.
- Verification for `f1467e6`: targeted browser case 1/1; full solution Release run API 6/6, Domain 257/257, E2E 31/31; 294/294 discovered, 0 skipped; build 0 warnings/errors; EF reports no pending model changes. `git ls-remote` confirmed the exact commit on `origin/master`. No schema migration or tenant operation. AS-PAR-002 remains partial.

## AS-PAR-002 — Reviewer grant revocation on an open accounting journal

Implemented in source checkpoint `master@b4067353a9d63521e34ab8efc4f11008e5a1ad04`, pushed to `origin/master` and verified there at 2026-09-23 09:50 UTC.

- Added `AS-PAR-002-JOURNAL-STALE-01`: the browser opens a synthetic draft journal with Staff read and AccountingReviewer post grants, then an administrator revokes the reviewer grant through `RoleAdministrationService`. The reviewer-only post action disappears while the still-authorized Staff read projection remains. A post command using the stale session is rejected with `generation.stale`, and PostgreSQL confirms the journal remains Draft.
- Verification: targeted regression 1/1; full E2E 32/32; full solution API 6/6, Domain 257/257 and E2E 32/32 (295 discovered, 0 skipped); Release build 0 warnings/errors; EF reports no pending model changes. The first full E2E attempt had one transient timeout in the existing preparer/reviewer browser journey; that case passed alone and the complete E2E and solution reruns passed. No schema migration or tenant operation. This remains one bounded AS-PAR-002 slice.

## AS-PAR-002 — PBC inbox requires a role covering the target engagement

Implemented in source checkpoint `master@404c6f5f6eac0de948cd28d4f6f88f91085cd0d6`, pushed to `origin/master` and confirmed there at 2026-09-23 10:21 UTC.

- The PBC inbox now applies the same allowed staff-role set used by PBC commands through `AuthorizationDecision` at the exact stored client/engagement scope. A role from a sibling engagement cannot combine with an unrelated role on the target engagement to reveal its request thread.
- Added `AS-PAR-002-PBC-ROLE-READ-01`: the PostgreSQL-backed Playwright fixture gives the synthetic actor `Staff` only on a sibling engagement and `FinanceManager` on the target. The browser receives the generic unavailable state and never sees the private request marker. The test reproduced the exposure before the guard and passes after it.
- Verification: focused regression 1/1; `ClientScopeJourneyTests` 22/22; Release build 0 warnings/errors; API 6/6 and Domain 257/257 in the full solution attempt; that attempt's E2E run had one intermittent unrelated reviewer-browser failure (32/33). The failed case passed alone (1/1), then the full E2E retry passed 33/33; 296 cases are discovered overall, 0 skipped. EF reports no pending model changes. No schema migration or tenant operation. AS-PAR-002 remains partial.

## AS-PAR-002 — Clear staff PBC inbox after scope revocation

Implemented in source checkpoint `master@f10ed0f3ffed4f3e1701f6f25529f109d2815b9f`, pushed to `origin/master` and confirmed there at 2026-09-23 10:59 UTC.

- When a PBC create/send, request-more-files, or staged-upload completion command returns `scope.denied` or `generation.stale`, the staff inbox now switches to its generic unavailable state, clears loaded request/timeline and action projections, and removes the new-request browser draft. Stored client requests and upload evidence are not deleted.
- Added `AS-PAR-002-PBC-STALE-READ-01`: a synthetic staff user opens the inbox and saves a draft; an administrator revokes the exact Staff grant; the next staged-transfer command is denied. The browser verifies that the private request marker and draft disappear, the draft key is removed from local storage, and the persisted request remains unchanged.
- Fresh Release build passed with 0 warnings/errors. Discovery is 297 (Domain 257, API 6, E2E 34); fresh per-project runs passed Domain 257/257, API 6/6 and the full Playwright E2E suite 34/34, with 0 skips. `ClientScopeJourneyTests` passed 23/23; EF reports no pending model changes; `git diff --check` passed. The full solution test command was not run as a single invocation for this checkpoint. No schema migration, tenant operation or production effect. AS-PAR-002 remains partial.

## AS-PAR-002 — Revalidate financial package artifact downloads

Implemented and pushed to `origin/master` in source checkpoint `65bcc63f2fb4e7b125ce827f39b79ce8533c1fb1`; remote equality was checked at 2026-09-23 11:39 UTC.

- Text-artifact downloads now re-read the current package and exact persisted artifact through `FinancialStatementService`, re-authorize the actor against current firm/client/engagement grants and session epoch, and verify the stored SHA-256 before returning any bytes to the browser download hook.
- If a grant is revoked while a package page is open, the stale-session state removes the rendered package/artifact view. The artifact read command returns `generation.stale`, the browser download hook is not invoked, and the stored artifact remains unchanged.
- Added `AS-PAR-002-FS-STALE-DOWNLOAD-01` in `FinancialArtifactJourneyTests.cs`; the test revokes both synthetic package-read grants, checks the browser projection and download hook, asserts the service-level stale-session denial, and verifies retained bytes/hash.
- Verification: full Release solution build passed with 0 warnings/errors; fresh discovery is 298 (Domain 257, API 6, E2E 35); Domain 257/257, API 6/6 and complete Playwright E2E 35/35 passed in separate project runs, 0 skipped. The financial-journey subset initially had one intermittent journal-revocation browser timeout; that case passed alone and the complete E2E rerun passed. Exact-case discovery confirmed 298; EF reports no pending model changes; `git diff --check` passed. Source SHA was pushed to `master` and matched `git ls-remote`. No schema migration, tenant operation or production effect. AS-PAR-002 remains partial.

## AS-PAR-002 — Scope accounting dashboard tasks to assigned engagements

Implemented and pushed to `origin/master` in source checkpoint `00d7bd0985af3d4cdd49fceedfc05be7aeb0ccfd`; remote equality was checked at 2026-09-23 11:52 UTC.

- `/app/accounting` previously selected workflow tasks by client reporting-period ID. An engagement-only accountant therefore saw the selected dashboard assignee from a sibling engagement when both tasks shared the client period.
- The task projection now admits client-level tasks only under a client grant, and engagement tasks only when the engagement is in the actor’s effective scope and its stored client relationship matches.
- Added `AS-PAR-002-ACCT-WORKSPACE-SIBLING-TASK-01`. It reproduces the synthetic sibling assignee disclosure before the filter and verifies only the assigned engagement’s task summary is visible after the fix. Existing accounting-workspace scope regression also passed.
- Verification: full Release solution build passed with 0 warnings/errors; fresh discovery is 299 (Domain 257, API 6, E2E 36); complete Playwright E2E suite passed 36/36, 0 skipped. Domain 257/257 and API 6/6 last passed at source checkpoint `65bcc63` and were not rerun because this slice changes only the Razor query, E2E test and CI expected count. EF reports no pending model changes; `git diff --check` passed. CI expected discovery is 299. No schema migration, tenant operation or production effect. AS-PAR-002 remains partial.

## AS-PAR-002 — Journal detail route-transition regression

Added browser coverage and pushed test/CI checkpoint `9562e38e716462ca8dad76ad86b566fa9ed47fbb`; remote equality was verified at 2026-09-23 12:12 UTC.

- Added `AS-PAR-002-JOURNAL-STALE-ROUTE-01`: while the same browser document is active, navigate from an authorized synthetic journal to an unavailable journal ID. The unavailable state must not retain the previous journal number or private line code.
- The test confirmed the in-app transition stayed within the same document and rendered the non-disclosing unavailable state with no prior journal details. No runtime change was required for this slice.
- Verification: full Release solution build passed with 0 warnings/errors; fresh solution discovery is 300 (Domain 257, API 6, E2E 37); full Playwright E2E suite passed 37/37, 0 skipped. Domain 257/257 and API 6/6 last passed at `65bcc63`; neither project changed in this test-only slice. EF reports no pending model changes; `git diff --check` passed. CI expected discovery is 300. No schema migration, tenant operation or production effect. AS-PAR-002 remains partial.

## Remaining local implementation work

These are product gaps, not claims of production readiness:

- [x] Complete the service-level release handoff from exact package-review decisions to a package-bound release candidate; client management acknowledgement and the staff review queue remain available.
- [x] Bind ECL and inventory review applicability to the exact reconciliation source and client generation.
- [x] Require explicit depreciation method/useful life and persist the calculated closing balance for asset schedules.
- [x] Add typed payroll, loan, equity, related-party, tax and going-concern forecast inputs with evidence-bound approval.
- [x] Link typed accounting evidence to reviewed audit procedure results and expose a scoped evidence queue; preserve the links in records exports.
- [x] Govern package-aware period close and immutable authorized reopen/amendment lineage.
- [x] Add controlled entity-period roll-forward with draft book copies and explicit opening-balance evidence.
- [x] Add the bounded approved foreign-operation translation profile with pinned rate/policy inputs, maker/checker review, source/FX lineage and stale-input blocking.
- [x] Add deterministic method-specific calculation cores for currency remeasurement/translation separation, acquisition/NCI, ownership changes, nested double-count rejection and asset-transfer/tax elimination.
- [x] Record method-owner approval of the IFRS advanced-method scope in `STE-METH-APP-001-addendum`.
- [x] Persist approved source-bound advanced-method schedules with canonical digests, idempotent retry, maker/checker approval and group-revision fencing.
- [x] Validate method-specific advanced schedule inputs at approval using the deterministic FX, acquisition/NCI, ownership, nested-group and asset-transfer calculators.
- [x] Wire persisted schedules into guarded advanced profile execution; persist balanced current/comparative statement evidence, method output manifests and digests, enforce current component/group revision/method-owner gates, revalidate component inputs before separate reviewer approval, and cover idempotent execution in PostgreSQL.
- [x] Add method-specific golden execution fixtures and fail-closed browser gate journeys for all five advanced profiles; keep unsupported or unproven methods visibly `REQUIRED`.
- [x] Complete source-bound method schedules, reviewed journal evidence and execution journeys for all five advanced profiles in PostgreSQL; keep them fail-closed until browser acceptance.
- [x] Complete full end-to-end browser acceptance with approved component/journal or FX fixtures for all five advanced profiles before enabling them; the local Development/Test workbench reports `1 / 1` verified executions and `APPROVED` readiness for each profile. Evidence: `docs/evidence/accounting-browser-advanced-gates-latest.json`.
- [x] Link accounting evidence to reviewed audit workpapers and expose account-area UI for the typed specialist schedules.
- [x] Add the accounting dashboard and cross-workflow navigation for accounting, roll-forward and release handoffs.
- [x] Include accounting/group dependencies in structured records exports while retaining the existing records-profile and legal-hold gates.
- [x] Carry approved group opening consolidation lineage across scope versions without duplicating prior journals.
- [x] Reject component, translation, intercompany-match and group-journal writes against a changed group revision.
- [x] Bind GL reconciliation sources to the selected reporting period and currency; reject cross-period or cross-currency batches.
- [x] Bind reconciliation items to the selected source currency; reject future dates and missing dispositions while preserving explicit as-of date, date basis, bucket rule/bucket, credit treatment and paired settlement links for receivable/payable ageing.
- [x] Route GL completeness calculation through the existing durable operation infrastructure with source revision fencing and idempotent retries.
- [x] Route financial-package builds through the existing durable operation infrastructure with mapping/plan fencing and idempotent retries.
- [x] Bind context-bound financial packages to the selected client reporting period, optional book, basis and currency, including the package hash and period-date validation.
- [x] Bind context-bound adjustment journals to the validated client reporting period, optional book, basis and currency; reject cross-period books and preserve the context through journal lineage.
- [x] Validate nonblank GL dimension values against client-scoped definitions and default accounting setup/reporting currency to QAR without defaulting source evidence.
- [x] Run seeded built-in-browser journeys for the SME, unrelated colliding-code clients, basic group and enabled foreign-operation profile without changing production authentication.
- [x] Route financial-package rendering through the existing durable operation infrastructure with exact package-revision fencing and deterministic artifact-digest verification.
- [x] Export controlled adjustment instructions with exact source, period/book, account, journal revision and management-evidence lineage; keep external posting and final artifact gates independent.
- [x] Export exact-package deterministic XLSX and DOCX artifacts with formula-shaped client values retained as literal text, exactly one renderer-owned workbook formula, no macro/external-link parts, runtime allowlist validation, SHA-256 persistence and scoped Blazor downloads.
- [x] Export an exact-package deterministic, inactive PDF through PDFsharp/MigraDoc with an embedded OFL font, A4 pagination, SHA-256 persistence, structural tests and rasterized visual verification.
- [x] Record Firm Methodology Owner approval `STE-METH-APP-001` for the named v1.0 template families and approved IFRS, straight-line, Provision Matrix ECL and external-books methods; keep provider, signing and records acceptance independent.
- [x] Map Financial Statement Template v1.0 to the exact controlled XLSX, DOCX and PDF renderer profiles under approval `STE-METH-APP-001`; Audit Program and ECL approvals remain methodology/template-family records without financial-package renderer enablement.
- [x] Benchmark representative accounting workloads before production acceptance; the current local workload evidence is recorded above and does not establish production capacity or RPO/RTO.
- [x] Re-run focused tests, full tests, build, migration drift and restore drill for the current coherent slice; repeat this checklist for the next slice.

## External acceptance gates

Local code and PostgreSQL evidence cannot close these gates:

| Gate | Required evidence | Status |
|---|---|---|
| P1 | Live Entra OIDC, runtime identity fixtures, wrong-tenant/disabled denial | `BLOCKED_EXTERNAL` |
| P2 | Selected-resource SharePoint/Graph upload, download, versioning and reconciliation | `BLOCKED_EXTERNAL` |
| P3 | Independently administered release checkpoint store and capability evidence | `BLOCKED_EXTERNAL` |
| P4 | Approved Purview records profile, reviewer fixtures and observed protection behavior | `BLOCKED_EXTERNAL` |
| P5 | Approved signing methodology and exact-byte signature lineage | `BLOCKED_EXTERNAL` |
| P7 | Custodially separate restore rehearsal with measured production RPO/RTO | `BLOCKED_EXTERNAL` |
| P8 | Production secret custody, telemetry, capacity and no-sensitive-log evidence | `BLOCKED_EXTERNAL` |
| P9 | Independent human review and protected-merge evidence | `BLOCKED_EXTERNAL` |
| P10 | Full §47 real-tenant acceptance cycle and professional sign-off | `BLOCKED_EXTERNAL` |

No fixture, local adapter, documentation statement, or browser login is treated as a substitute for the required external evidence.

## Verification commands

```text
dotnet build AuditSphereOps.slnx --no-restore --configuration Release
dotnet test AuditSphereOps.slnx --no-build --no-restore --configuration Release
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore --filter 'FullyQualifiedName~AccountingBenchmarkTests'
dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web
scripts/db/restore-drill.sh
```

The restore evidence is written to [`docs/evidence/restore-drill-latest.json`](../evidence/restore-drill-latest.json). The latest rehearsal restored 91 migrations through `20260922140527_M365InvitationEvidenceAction`, reconciled accounting/group manifests and found zero duplicate release-delivery keys; it is loopback-only and explicitly reports that external checkpoint custody and production RPO/RTO were not run.

## Resume rule

Before starting another slice, re-read `AGENTS.md`, this file, `docs/execution/status.json`, the Git worktree and current remote head. Preserve unrelated dirty work. Implement the smallest coherent local slice, verify it on PostgreSQL, commit and push that slice, then refresh the observed evidence. Never rewrite history or claim an external gate from local evidence.
