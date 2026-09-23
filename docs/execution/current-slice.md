# AuditSphereOps — current state and active slice

This file records observed repository state only. The authoritative build contract is [`docs/SPECIFICATION.md`](../SPECIFICATION.md) v5.0. The public repository intentionally excludes tenant identifiers, user principals, credentials, tokens, secret values, and private-provider URLs.

## Current checkout and latest verification snapshot

| Item | Observed value |
|---|---|
| Source implementation checkpoint | `master@5fd9f6971305f3999e2d02347549fda0a3c6ec24`; financial-package route-isolation characterization committed |
| Remote | `origin/master` confirmed to equal `5fd9f6971305f3999e2d02347549fda0a3c6ec24` at 2026-09-23 14:20 UTC |
| SDK | .NET 10; repository solution targets `net10.0` |
| Database | PostgreSQL 18.6 on loopback port 5433 for development only |
| Build | Full solution Release build for test checkpoint `5fd9f69` — passed, 0 warnings/errors |
| Tests | Discovery: 304 (Domain 257, API 6, E2E 41). Focused package-route test 1/1; standalone E2E 41/41; combined solution Domain 257/257, API 6/6 and E2E 41/41; 0 skipped. |
| Migrations | Previous recorded baseline: 91 applied through `20260922140527_M365InvitationEvidenceAction`; no schema migration in these slices |
| Model drift | `dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web --no-build --configuration Release` — no pending model changes after test checkpoint `5fd9f69` on 2026-09-23 |
| Restore drill | Previously passed at `2026-09-22T18:52:20Z`; 91 migrations, accounting/group manifests and release-delivery identities reconciled; not rerun during test recovery |
| Production effects | Disabled locally; no production acceptance claimed |

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
