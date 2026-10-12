# AuditSphereOps — Current State & Active Slice Handoff

**Status:** CURRENT

**Purpose:** Compact, authoritative handoff of the active implementation slice, recent verified changes, local environment state, and next actions.

**Authority:** Active execution handoff document. Volatile metrics (exact test counts, migration count, verified commit SHA, CI run IDs) belong exclusively to [`docs/execution/status.json`](status.json).

**Audience:** AI coding agents and human developers.

**Chronology:** Sections are newest first. An older section describes the state
when it was written; where a later section supersedes it, the later section is
current. The top-level `verifiedCommit` in `status.json` names the last commit on
which the full suite was run; per-slice records carry their own evidence.

> **Archive:** earlier slice sections, moved verbatim on 2026-10-08, are in [`auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md`](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). They are history, not current authority; the ledger is [`status.json`](status.json).

## v2.2 workflow analytics — procedure review turnaround — 2026-10-12

Practice Analytics now shows average elapsed hours from submission of the exact procedure-result revision to each append-only review decision created in the selected date range. Both `REVIEWED` and `CHANGES_REQUIRED` decisions count. Revision mismatches and decisions timestamped before submission are excluded and counted; an empty valid sample is `n/a`. The query returns aggregate counts only and does not expose reviewer comments or names, change review state, or imply review quality or audit clearance. A `(FirmId, CreatedAt)` index supports the bounded review-period query; migration `20261012052354_WorkflowReviewAnalyticsIndex` is additive and has not been applied to a persistent database.

Verification on the working tree based on `690e1c11`: API Release build passed with 0 errors and two offline `NU1900` warnings; the focused PostgreSQL analytics regression passed 1/1, including both review outcomes, date filtering, invalid chronology and revision mismatch; the Angular analytics contract spec passed 4/4; EF model drift reported no pending changes; OpenAPI drift passed unchanged; and the Angular development server compiled. The built-in browser redirected the analytics route to the staff sign-in guard, so no authenticated data rendering was observed. The Angular production build aborted with exit 134 and no diagnostics. No full suite, live-provider or professional acceptance was run. Exact evidence is in `status.json` under `targetedVerification.workflowReviewTurnaround`.

WF-14 remains partial: per-phase task rollups, client wait, append-only workflow activity, analytics pagination/performance acceptance and the remaining workflow stories are open. No persistent database was migrated.

## v2.2 workflow analytics — task completion and workload — 2026-10-12

The existing bounded Practice Analytics view now reports completed applicable tasks and a separate completion percentage beside on-time, late and overdue counts. The denominator is non-cancelled tasks whose due dates fall within the selected date range; completion requires both `COMPLETED` status and a persisted completion timestamp. Tasks without due dates are excluded, and an empty denominator reports `n/a`. This is operational progress only; it does not satisfy an audit gate, change lifecycle state or produce accounting values.

Verification on the working tree based on `dee458d3`: Release solution build passed with 0 errors and six offline `NU1900` warnings; the targeted Angular analytics contract spec passed 2/2; the focused PostgreSQL analytics regression passed 1/1; OpenAPI drift passed with no contract change. No full suite or Angular production build was run for this slice. The run summary is in `status.json` under `targetedVerification.workflowExperiencePrH`.

The same firm-wide analytics query now groups non-cancelled tasks due in the selected period by staff assignee and shows due, completed, open and overdue counts. Unassigned tasks and assignments that do not resolve to a staff user have separate buckets. Staff names are returned only to the endpoint's existing firm-wide Partner, Administrator and FinanceManager audience. This measure is a task count, not capacity or effort; it does not change task state or audit readiness.

Verification of the workload slice based on `0afdaf9a`: the focused PostgreSQL analytics regression passed 1/1 after an initial sandbox socket denial and elevated rerun; the Angular analytics contract spec passed 3/3; the OpenAPI-emitter Release build passed with 0 errors and two offline `NU1900` warnings; its generated contract matched the committed file. The built-in browser opened the local preview but reached the staff sign-in guard, so the analytics page was not authenticated or visually verified. No full suite or production Angular build was run for this slice.

Per-phase task rollups, client wait, append-only workflow event coverage and broader analytics pagination/performance evidence remain open. Overall WF-14 and v2.2 acceptance remain PARTIAL.

## v2.2 final-release evidence projection — 2026-10-12

The engagement completion query now projects the latest release candidate together with its exact external checkpoint and persisted release row. The view reports candidate and target revisions, manifest SHA-256, checkpoint identity/state/verification time, whether the checkpoint revision and read-back digest match the candidate, and whether the persisted Release identity, checkpoint, target revision and manifest agree. It labels a missing issue `NOT_ISSUED`, a fully matching persisted issue `ISSUED`, and inconsistent state `RECONCILIATION_REQUIRED`. The Angular completion screen displays these distinctions and explicitly says that a candidate is not a release. This is read-only; it adds no release or approval command and no migration.

Verification on the working tree based on `87df1a24`: Release solution build passed with 0 errors and six offline `NU1900` vulnerability-feed warnings; the completion Angular contract spec passed 12/12; the focused PostgreSQL projection test passed 1/1; OpenAPI drift passed with no contract change. The Angular development server compiled the routes, but the built-in browser reached the staff sign-in guard because no local API session was available. The Angular production build aborted with exit 134 and no diagnostics. No full suite, persistent migration, signed-in browser workflow, live provider check or professional acceptance was run. Run results are recorded under `targetedVerification.workflowExperiencePrH` in `status.json`.

This only improves visibility of locally persisted release evidence. Issuance still requires the current approval and verified external checkpoint read-back; live delivery and SharePoint/provider acceptance remain external. Overall WF-13 and v2.2 acceptance remain partial.

## v2.2 Manager Review and rework follow-up — 2026-10-12

`ManagerReviewSummaryQuery` adds a read-only, scope-authorized summary over the existing fieldwork completion evaluation. It reports redacted blockers, procedure and evidence counts, unresolved threads, open confirmations, reconciliation/difference work, and whether the latest Summary Review Memorandum is current. `PartnerHandoffReady` requires both current completion readiness and a current SRM; this projection does not record a review decision, opinion, or lifecycle transition. `GET /api/ui/engagements/{id}/manager-review` serves the summary, and the Angular fieldwork page shows it only to actors who can review selections.

When `WorkflowExperience:Enabled` is true, an authorized `ChangesRequired` review creates one idempotently keyed `WorkTask` and workflow binding in the same transaction as the append-only review decision. The task is tied to the exact procedure/result revision and goes to the prior preparer only while that user remains eligible; otherwise it is unassigned for manager routing. No notification is sent. No migration was added for this follow-up.

The two focused PostgreSQL regression tests passed 2/2, including the returned-work blocker, stable redaction, linked task, prior-preparer assignment and duplicate-review refusal. The API Release build, Angular production build, EF model-drift check and OpenAPI contract drift check passed; the .NET build emitted two `NU1900` vulnerability-feed warnings because NuGet was unreachable. The built-in browser could not load the local fieldwork route because the local Angular server was no longer listening; the existing local tab reached only the staff sign-in guard. No authenticated browser journey or full suite was run. Exact commands and results are in `status.json` under `targetedVerification.workflowManagerReviewPrF`.

This is a partial workflow increment. The canonical lifecycle stage does not regress to Fieldwork on rework; the summary flag does not change lifecycle gates; procedure-level assignment/resubmission restrictions and task notifications remain open. Queue filters for rework and waiting-for-client, evidence dependency evaluators, Kanban/calendar views, and task-plan authoring remain open. The workflow feature stays default-off; no live or professional acceptance is claimed.

## v2.2 workflow experience, PRs D–F — 2026-10-12

WF-04 keeps `WorkTask` as the one operational task/status record and binds optional workflow metadata one-to-one. Existing `TimeEntry` lineage remains attached to the same task. The default-off API exposes scoped task create/read/assign/reassign/complete; creation uses a stable idempotency key and immutable digest, and revisioned actions reject stale requests. Checklist completion does not advance the canonical audit lifecycle.

WF-05 adds a firm-scoped template catalog with immutable revisions, dependency-cycle validation, independent review, publication/supersession and archive evidence. WF-06 adds a preview/execute/receipt path that pins the exact published revision, engagement generation, staffing snapshot and statutory schedule basis. Instantiation creates existing `WorkTask` records and bindings in one transaction. Concurrent same-request execution creates one plan; an ambiguous retry recovers its original receipt even after the schedule changes. A new published template version does not mutate a prior engagement plan. Plan receipts are append-only in PostgreSQL.

PR-F partially advances WF-07/08. `WorkflowTaskQueueQuery` returns My Tasks, Team, Overdue and Completed views scoped from current unexpired role grants, with stable keyset paging and task-owner, FSLI and mapping lineage. The Angular `My work` screen is at `/ui/app/work`. Plan execution now records append-only dependency edges only when they match the exact pinned template steps; task completion is refused until each `TASK_COMPLETED` predecessor is complete. These edges remain operational checklist controls and do not set audit review, lifecycle or approval states. The newer Manager Review follow-up above adds a read-only handoff summary and feature-flagged rework task creation. Waiting-for-client, audit-review and rework queue filters, domain-evidence dependency evaluators, Kanban/calendar views and task-plan authoring screens remain open.

Migrations `20261012000128_WorkflowTaskBindings`, `20261012003440_WorkflowTemplateCatalog`, `20261012004124_WorkflowTemplateOpenRevision`, `20261012010500_WorkflowTaskPlanReceipts` and `20261012015133_WorkflowTaskDependencies` are additive and were not applied to any persistent database. The focused queue/plan PostgreSQL cohort passed 3/3; the API-host OpenAPI contract test passed 1/1; the API Release build passed with two NU1900 vulnerability-feed warnings; EF model drift and OpenAPI drift passed. Angular development serving compiled successfully. The built-in browser loaded the app and showed Access unavailable / Sign in because no local API session was configured; no authenticated queue journey was run. Exact provenance is in `status.json` under `targetedVerification.workflowTaskQueuePrF`.

`WorkflowExperience:Enabled` remains false by default; migrations are not applied to a persistent database. The queue route is present, but local browser access stopped at the staff sign-in guard, and no authenticated queue command was exercised. WF-07/08 and WF-12 remain partial. WF-09/10 automations and PBC follow-up, WF-11 annual recurrence, WF-13/14 completion/archive cards and analytics, the lead-level journey, and WF-15 production acceptance remain open. No live tenant or professional acceptance is claimed.

## v2.2 workflow experience, PRs A–C — 2026-10-11

The engagement page now opens on a read-only seven-phase workflow projection and independently evaluated Dual-Key readiness cards, with the eleven-stage lifecycle kept as a technical panel. The projection derives from the existing canonical stage and persists nothing new; no stage is writable. Advance billing no longer opens on a Partner clearance alone: both keys must be current on the exact dispatched offer. The advance and activation cards read the same milestone state and engagement link that activation checks. The protected letter, fee-agreement and activation commands are unchanged.

Not yet delivered: workflow task persistence and templates (WF-04 to WF-06), queues, automation and recurrence (WF-07 to WF-11), completion and archive cards (WF-13), and the lead-level pre-engagement journey. One owner decision is open: the protected letter guard applies offer identity only when a dispatch record exists, while the read model requires it always. Evidence and the open review findings are recorded in `status.json` under `targetedVerification.workflowExperienceV22`.

## Exact pinned financial statement drafts — 2026-10-12

The FSR presentation slice now saves an immutable draft revision against the exact package inputs, published layout version/hash, comparative package/hash, note-source manifest, builder/renderer versions and display policy. Repeating the same pin request is idempotent. PDF, DOCX and XLSX bytes are stored as package artifacts and linked to that revision; repeat downloads reuse the stored bytes. A source change makes the pin stale: the historical rendered bytes remain retrievable, while an unrendered format cannot be created from the stale snapshot. The package seal manifest records pinned presentation identity and artifact hashes without changing the draft's `UNAPPROVED_DRAFT` status or treating it as an approved/issued statement. Cross-casts for current and comparative note amounts are checked against their own package balances.

Observed on this uncommitted working tree based on `00376915`: the focused PostgreSQL Domain regression passed 30/30; the Angular presentation spec passed 4/4; the API and OpenAPI emitter Release builds passed; EF model drift reported no pending changes; and the OpenAPI contract was regenerated. Angular production build passed using the SQLite cache backend, with the existing `commercial/settings.scss` budget warning. `status.json` records the focused commands and provenance. No full suite or signed-in browser workflow was run for this slice. No development or production database was migrated and no live financial data was changed.

This remains a partial, unapproved draft capability. The manifest preserves source identities, but preparer-entered references are not independently resolved to ledger evidence. Professional framework/display policy approval, the approved golden fixture, full note narratives and disclosure schedules, review and authorization, audited report assembly, signing/legal acceptance, release/archive and production acceptance remain open. Full cash-flow and equity completeness also remains gated by approved methodology and source lineage.

## Dedicated client SharePoint sites default rollout — 2026-10-12

`ClientSites:Enabled` now defaults to `true` in the API, worker and Hostinger Compose stack, with an explicit false override. The rollout still requires one administrator-approved UTC `ClientSites:ClientsCreatedAfter` boundary; an absent or invalid boundary never becomes the beginning of time. Historical clients before the boundary are not selected or migrated. The `client-sites` worker starts as an isolated group and reports blocked readiness when its configuration, certificate, tenant consent or rollout boundary is unavailable.

The API and `worker-pbc` receive the same cutoff. Repository resolution blocks shared-site folder creation for clients covered by the enabled or already-persisted cutoff until a dedicated site is verified, including while the dedicated-site worker is blocked and before it can persist the cutoff. Invalid/mismatched settings block dedicated-site discovery; a saved boundary remains authoritative, and conflicting saved values use the earliest recorded boundary for no-fallback enforcement while earlier clients retain their repositories. When the policy is enabled but neither a valid configured nor persisted boundary exists, workspace review/provisioning now also fails closed instead of routing an unclassified client to the shared site; setting an explicit cutoff or explicitly disabling the policy is required before shared-site provisioning. A verified dedicated site continues to serve its client. Automatic discovery remains idempotent per client, and the existing approved yearly engagement templates remain in use. Administration now shows the effective policy, cutoff, counts, tenant consent, worker heartbeat, site permission evidence, per-client site and membership states, durable operation diagnostics, and the existing Operations recovery route.

The heartbeat migration is part of the source tree and model snapshot. The latest grouped follow-up on 2026-10-12 passed the focused Domain cohort 37/37, workspace provisioning API tests 4/4, PostgreSQL-backed Playwright journey 1/1 and Angular workspace administration spec 4/4. The Release test builds emitted NU1900 warnings because `api.nuget.org` was unreachable; the tests themselves passed against isolated synthetic fixtures. These results are from an uncommitted working tree based on `00376915`, which also contains separate in-progress financial statement presentation edits; the full Domain, API and E2E suites were not run. Markdown health, filename and narrative-metrics gates passed. A read-only built-in Chrome check on 2026-10-12 loaded the acceptance tenant page and rendered the Dedicated client SharePoint sites panel, with no client-site rows. The page did not identify its deployed source commit, so this does not prove the current source revision is deployed. No development, acceptance or production migration was run; no real client site was provisioned and no staff membership reconciliation was performed. Exact commands and provenance are in [`status.json`](status.json). Live acceptance still requires identifying the deployed revision, applying the approved UTC cutoff consistently to API and worker configuration, and verifying a synthetic new-client site and staff membership reconciliation in the real tenant.

## Dynamic statement note references — 2026-10-11

Statement face-line note references now separate stable persisted note codes from display ordinals. The presentation assigns numbers from each code's first face-line position in the published layout, then uses the same number on each linked face line; reordering supplied note rows does not change numbering. Blank/oversized logical IDs and duplicate case-normalized (ID, face destination) pointers are refused. The presentation manifest builder version is v4. A failed `NOTE_CONTENT` check and warning state that the persisted data contains only cross-cast amounts, without ordered narrative, typed note tables or comparative note values. The output remains `UNAPPROVED_DRAFT`; no note content or accounting policy is invented.

Observed: the focused Domain and Angular presentation tests passed. The API Release build passed with NuGet vulnerability-feed warnings; Markdown health, filename, narrative-metric, UI inventory, status JSON and diff checks passed. The OpenAPI schema shape is unchanged by the note-number value update; its drift gate passed after the preceding presentation slice and was not rerun here. No migration or business-data change was made. FSR-008 remains PARTIAL: disclosure applicability, ordered note content, prior schedules, PPE/expense schedule completeness and seal-time pointer checks are open.

## Cross-statement readiness checks and export diagnostics — 2026-10-11

The statement presentation now cross-casts current and comparative equity rollforwards to mapped profit or loss, OCI and SFP balances. Cash-flow readiness requires operating, investing and financing amounts, validates current and comparative bridges independently, and carries the package's persisted `CASH_FLOW_RECONCILED` check, which also validates the selected closing-cash population. PDF, DOCX and XLSX all include the same per-check `PASS`/`FAIL` diagnostics in the presentation basis. Builder version is v3. Missing comparative schedules and incomplete source data remain explicitly failed or unavailable; output remains `UNAPPROVED_DRAFT`.

Observed: the focused Domain and Angular presentation tests passed; the API Release build passed with NuGet vulnerability-feed warnings; Markdown health, filename, narrative-metric, status JSON and `git diff --check` gates passed. The .NET test host initially could not bind its runner socket in the sandbox; the elevated retry ran and passed the focused suite. No full suite or PostgreSQL-backed integration test was run. A signed-in browser export journey was unavailable; the local UI had previously stopped at its unauthenticated access gate. No migration or business-data change was made. FSR remains PARTIAL; this slice does not provide source-record lineage, professional policy approval, complete notes/disclosures, final composition, sealing, signing acceptance or release.

## Cash-flow and equity schedules in statement presentation — 2026-10-11

Working-tree follow-up based on `3012b495`. The typed statement presentation now renders the persisted cash-flow movements and equity rollforward beside the SFP and profit-or-loss statements. PDF, Word and Excel exports use the same model. Cash bridge totals exclude noncash items and remain unavailable when an operating, investing, financing or FX input is missing; a supplied zero FX effect is distinct from an absent effect. Comparative schedule amounts remain unavailable when the comparative package has no matching rows. The builder refuses oversized supplementary schedules rather than silently truncating them.

The output remains `UNAPPROVED_DRAFT`. Readiness checks expose missing schedules, an unreconciled cash bridge, absent comparatives, preparer-entered references without source resolution, and equity not cross-cast to the SFP. This does not add source-record lineage, pin profit-transfer treatment, approve policy, or complete composition, sealing, signing or release. No migration or persisted business-data change was made.

Observed: `FinancialStatementPresentationTests` passed 16/16; the targeted Angular presentation spec passed 4/4; the API project build passed with two `NU1900` vulnerability-feed warnings; and the Angular production build passed with the existing `commercial/settings.scss` component-style budget warning. The full solution build was cancelled after Domain and Application compiled because Infrastructure compilation stopped making progress; it is not counted as passed. The built-in browser rendered the local Angular shell and its `Access unavailable` sign-in gate. Its `/api/ui/session` request was refused because no local API was running, so no authenticated package or export flow was browser-verified. This slice remains partial; the FSR backlog and professional acceptance gates remain open.

## Cash-flow movement evidence and adjusted cash cross-cast — 2026-10-11

The package build contract now requires each cash-flow movement to carry a reporting-period date, activity code, preparer-entered source reference and classification rationale. It also records the selected cash-account codes and separate opening/closing balance references. The metadata is persisted, included in the supplementary hash, surfaced in the scoped package workspace, and included in the canonical package artifact. New noncash operating adjustments are retained alongside noncash investing/financing items but excluded from the cash bridge. The selected adjusted cash-account population must exist in the adjusted source balances and cross-cast exactly to supplied closing cash; no balancing plug is created.

These references and classifications remain preparer inputs: the implementation does not resolve them to source-record identities or independently verify the underlying documents. There is no editor for this new schedule yet, and no approved framework-specific cash-flow classification policy. The increment is therefore PARTIAL. T030 still waits on T029 under the R2R task dependencies; it is not marked complete. It does not deliver the complete indirect method, population lineage, review workflow or professional acceptance described by FSR-005.

Observed on this working tree: Release solution build passed with zero errors and six `NU1900` warnings because the NuGet vulnerability feed was unavailable; Angular CI suite passed 634/634; `FinancialStatementPresentationTests` passed 15/15; Angular production build passed with the existing `commercial/settings.scss` budget warning (7.53 kB against 4.00 kB); EF reported no pending model changes; OpenAPI drift, Markdown, filename, narrative-metric, UI source-inventory and `git diff --check` gates passed. The new PostgreSQL-backed cash-flow cases were compiled but not run because PostgreSQL on `127.0.0.1:5433` did not respond. The built-in browser opened the local Angular package route and showed the expected unauthenticated access guard; no signed-in package data or cash-flow schedule was available for browser verification. This slice remains unapproved and does not close the attached statement backlog.

## A4-safe statement PDF and preview-pinned export — 2026-10-11

The statement PDF now allocates its four columns within the A4 printable width: 21 cm page width minus the configured 2 cm left and right margins, or 17 cm. Deep-row indentation is capped so the label column retains at least 4.5 cm for text. A long-label regression confirms the PDF paginates the wrapped row and every rendered page remains A4. The Angular export request now carries the exact published `layoutVersionId` shown in the preview. The PostgreSQL-backed regression also publishes a successor layout after preview and checks that an explicit export retains the reviewed version, but that integration case could not run locally.

Observed for this working tree based on `1f783baf`: Application Release build passed with one `NU1900` warning because the NuGet vulnerability feed was unreachable; `FinancialStatementPresentationTests` passed 15/15; the targeted Angular presentation spec passed 4/4; and the Angular production build passed with the existing commercial-settings stylesheet budget warning. The modified Domain test assembly compiled. Markdown, filename, narrative-metric, status JSON and diff checks passed. The PostgreSQL service on port 5433 was down; its documented local start attempt failed because a shared-memory block for that data directory was still in use, so it was left untouched. The local in-app browser did not reach the dev server route, and no signed-in/API workflow was assessed. The layout-lineage integration test and browser flow remain unverified. This is still an unapproved draft slice; it does not close the wider FSR backlog or professional, signing, composition, sealing or release gates.

## Typed financial-statement presentation draft — 2026-10-11

The current local slice implements the recommended first FSR presentation milestone. A scoped read and export now build a typed SFP and profit-or-loss presentation from persisted package lines and a published layout for the same client/framework. The server applies display signs, joins current and explicitly bound comparative package lines, links persisted note cross-casts, and blocks unbalanced or incompletely mapped statements. Exports produce deterministic PDF, DOCX and XLSX from that same draft model; Excel numeric cells remain numeric. The package's existing immutable artifact generation and stored bytes are unchanged.

The output is marked `UNAPPROVED_DRAFT`, and amounts use a display policy that still needs professional/design approval. The fixture uses synthetic balances matched to selected reference totals; it is not the NAQAHA ledger or an approved golden fixture. The focused tests and browser boundary evidence are recorded in [`status.json`](status.json). The local browser rendered the Angular shell's unauthenticated access guard; no API session or signed-in package data was available for a full screen journey.

FSR-001/T003 accounting-policy approval remains blocked on qualified owners. Cash-flow and equity statement matrices, complete disclosure policy and dynamic note numbering, full composition/sealing, audited report assembly, management and Partner acceptance, and production/legal sign-off remain open. This partial presentation slice does not complete the R2R task cards or the attached backlog.

## Isolated background mail worker active — 2026-10-11

The acceptance mail queues and pending discovery rows were checked before activation; no
unreviewed recipient was pending. The isolated mail profile was started with the previously
verified sender credential. Its broad parent credential mount was identified and replaced with
an outbound-mail-only read-only mount. The disabled PBC profile now also uses only its own
Selected Site mount; it was not started.

The clean VPS checkout was fast-forwarded to the reviewed Compose change and only the new mail
worker recreated. Its own key is readable and sibling credentials are absent. Logs show successful
queue polling without failures; API readiness and existing worker preservation passed. The
administrator Operations page is accessible and has no browser errors. This verifies idle worker
deployment, not an unexecuted business-mail workflow. Exact evidence is in [`status.json`](status.json).

SharePoint's separate exact-site grant remains unresolved; its Entra consent is present and the
runtime identity still cannot access the approved site. The exact grant receipt is requested
before another provider probe. No tenant-wide scope or site permission was added.

## Separate directory capabilities configured and verified — 2026-10-11

Following exact owner approval, the existing User Creation certificate was matched to its Entra
registration and staged privately on the VPS. Guest Invitation and Group Membership received
separate restricted VPS key pairs; only their public certificates were uploaded to their exact
single-permission app registrations. All identities authenticated for the configured tenant with
exactly their named application role, and their distinct app IDs were saved in the protected
server environment.

Only the API was recreated to enable the reviewed capabilities. Readiness passed and existing
workers were unchanged. AuditSphere persisted verified permission checks for User Creation and
Guest Invitation, plus a successful bounded read for Group Membership. Directory Reader and Mail
also verified successfully; browser console error checks were empty. No user was created, guest
invited or group membership modified. This completes capability setup and read/permission
verification, not full mutation-journey acceptance. Exact observations are in [`status.json`](status.json).

SharePoint remains separately blocked by the approved-target denial. The site's inspected General panel
confirmed the reviewed site identity; no grant or permission scope was changed. Further duplicate target probes remain paused pending exact grant
investigation, and the runtime Selected Site app ID stays unset.

## Restricted outbound mail verified and delivered — 2026-10-11

The local interactive authentication alternative failed because the Exchange authentication
library rejects this Mac's OS version. A temporary official Linux PowerShell container on the VPS
provided an active supported Exchange connection. The initial access checks allowed both the
approved sender and unrelated synthetic mailbox, proving a sender restriction was needed.

A mail-enabled security scope group containing only the approved sender and an exact-app
`RestrictAccess` policy were created. Exchange then allowed the sender and denied the unrelated
fixture. A structurally valid Graph authorization probe with no recipients also denied the
unrelated mailbox. The dedicated token contained only `Mail.Send`; the existing certificate was
reused and no new application credential was added.

The approved self-addressed synthetic message was accepted by Graph and Exchange's filtered
message trace confirmed delivery. The API-only recreation enabled the mail capability; readiness
passed, existing workers were unchanged, and fresh AuditSphere capability verification passed.
The reviewed local Mail metadata was saved and its immutable receipt reconciled; Records remains
unconfigured. The live mail worker was not started, so this is transport and configuration evidence,
not durable commercial/PBC mail workflow acceptance. Temporary verification containers and files
were removed. Exact timestamps, status and correlation evidence are in [`status.json`](status.json).

The prior authentication blocker is resolved by the supported Linux path. SharePoint's exact-site
denial and the disabled mutation capabilities remain separate blockers.

## Live VPS upgrade and directory consent verified — 2026-10-11

After exact owner approval, a fresh catalog-checked database backup was retained, the approved
migrations were applied to the synthetic acceptance database, and only the API was recreated.
Public readiness passed. Existing worker containers and the local development database were
unchanged. Exact image, migration, backup and observation evidence is in [`status.json`](status.json).

The dedicated Directory Reader now has both fixed Web callbacks and its own certificate. Only
its public certificate was uploaded to Entra; the private key remains on the VPS. Directory Reader
and Tenant Consent were enabled. The reviewed Microsoft consent and identity flow completed,
and AuditSphere persisted verified consent and a fresh successful bounded directory read.
Mail and directory mutations remain disabled. Consent verification does not promote the separate
resource revision or activate a workspace.

The deployed time screen renders FSLI attribution. End-of-service access correctly refuses an
administrator without a Finance or Partner grant. The commercial settings screen also renders the pricing
policy form and reports that no actual pricing policy has been configured. These are live screen observations, not a
record-specific business acceptance run. Selected SharePoint remains blocked by the missing
exact-site grant; neither the unrelated control nor the approved target allowed access.

## VPS upgrade rehearsal and selected-site access check — 2026-10-11

The previously authorized SSH access worked despite the expired Hostinger browser terminal.
The clean VPS checkout was fast-forwarded, the published API and migration images were pulled,
and a private custom-format PostgreSQL backup was catalog-checked and restored to a disposable
database. The pending migrations applied there, and the new API returned readiness HTTP 200
against that copy. The rehearsal container, database and temporary environment files were removed.
The live database and existing services were unchanged. Its upgrade awaits the specific migration
approval because the earlier deployment scope allowed API recreation without database migration.
Exact image, migration and backup evidence is in [`status.json`](status.json).

The installed Selected Site certificate matches the public certificate on the approved Entra app.
A live token contained exactly `Sites.Selected` for the configured tenant. Read-only Graph checks
returned HTTP 403 with `accessDenied` for both the approved synthetic site and its unrelated
control. The positive grant is missing; no disposable file write was attempted and no site grant
was changed. The server-side Selected Site app ID remains unset pending the user's exact-site grant.

Automatic approval review rejected selecting the Web callback platform on the separate Directory
Reader app because the earlier callback authorization named the Development app. Exact approval
for the Directory Reader callbacks, certificate and read-only capability enablement has been
requested. Its registration and runtime remain unchanged pending that response.

## Current-tree acceptance verification — 2026-10-11

The complete .NET and Angular suites, Release build, OpenAPI drift check, EF model-drift check,
documentation validators and source inventory now have current-tree evidence. Exact commands,
counts, durations, migration head and the verified code commit are recorded in [`status.json`](status.json).
The PostgreSQL-backed tests used only the isolated `auditsphere_tests` database. The previous
four failing browser journeys were diagnosed and fixed; the pricing-policy maker/checker and
end-of-service professional-gate journeys are now included in browser coverage.

The end-of-service page correctly blocks accrual preparation until a qualified accountant names
the treatment and a Partner confirms it. No firm treatment or chart accounts were supplied, so no
real accrual was prepared or posted. Certificate signing remains implemented and locally verified;
the firm certificate and legal-sufficiency decision are still outstanding. The GitHub Wiki's
Production Environment Settings page already documents the `ReportSigning` configuration, so no
Wiki edit was needed.

The Entra portal now confirms admin consent for `User.Read.All` on the dedicated Directory Reader
registration, alongside its delegated `User.Read` sign-in permission. This is registration-level
consent only: AuditSphere's tenant-connection page still shows no nonce-bound consent attempt, and
its consent action remains unavailable until the deployment-owned consent identity and fixed
callback are configured. Directory verification remains `NOT_VERIFIED` and the Directory Reader
runtime switch remains disabled. The selected SharePoint target fields remain an unsubmitted
browser-tab draft; the user will grant the exact site. Runtime Graph capabilities remain disabled,
and the selected-resource probe and workspace activation remain blocked on that grant and credential
setup. Do not mark those external states complete from local or Entra-registration evidence.

## Hostinger Entra mutation-app permission split — 2026-10-11

The user authorized three dedicated, single-role registrations and admin consent, plus removal of
the duplicate roles from the former combined app. The Entra portal now shows `User.Create` only on
`AuditSphereOps Development Administration`, `User.Invite.All` only (plus delegated `User.Read`)
on `AuditSphereOps Acceptance Guest Invitation`, and `GroupMember.ReadWrite.All` only (plus
delegated `User.Read`) on `AuditSphereOps Acceptance Group Membership`; each application role shows
tenant consent granted. The former combined app's extra configured roles and their leftover tenant
grants were removed.

The dedicated `AuditSphere Acceptance - Directory Reader` registration shows application
`User.Read.All` and delegated `User.Read`, both with tenant consent granted in the Entra portal on
2026-10-11. The separate Mail registration shows `Mail.Send` consent granted. AuditSphere's
Directory Reader and Mail runtime switches remain disabled; no Graph capability verification or
Graph mutation was performed.

This is Entra registration evidence. The live AuditSphere tenant-connection page still reports no
consent attempt, directory `NOT_VERIFIED`, and the three mutation capabilities Disabled. No
role-specific VPS credentials were configured, no Graph mutation was run, and no capability was
enabled. Selected SharePoint remains `BLOCKED_EXTERNAL`; the exact site grant and positive/negative
resource probe are still pending. The previously owner-approved API-only Compose recreation
returned `/health/ready` HTTP 200; the database and workers were not recreated and no migration ran.
`SELECTED_SITE_CLIENT_ID` remains blank in the protected VPS environment until the exact-site grant.
See the M365 permission matrix and Hostinger deployment guide.

## FSR first slice — typed statement presentation (unapproved draft) — 2026-10-11

Working-tree slice on `78979390`, from the NAQAHA implementation stories (FSR-002, FSR-010, FSR-011 first slice). The statement of financial position and of profit or loss are presented from persisted package lines through the published statement layout, in PDF, Word and Excel, with blocking checks for balance, mapping, sign, note cross-cast and display footing. Observed: Release build, EF drift check, OpenAPI regeneration, Domain presentation and statement tests, API UI contract test, Angular presentation and package specs and the production build passed. The fixture is synthetic. Approval (FSR-001), composition, sealing, signing and release remain open; details are in `status.json`.

## ADR-0016 certificate signing of the final report — 2026-10-10

Working-tree slice on `cee546f9`. On the owner's instruction the final report PDF is certificate-signed when the firm's certificate is configured (`ReportSigning:*`), the bundle's certified statements are signed with it, and `ReportSigning:Required` fails closed. With no certificate the image-only behaviour is unchanged and labelled. Observed for this slice: Release build, EF drift check, OpenAPI check, the Domain test classes `ReportCertificateSigningTests` and `AuditDeliverablesTests`, and the `completion.spec.ts` screen specs passed, using a throwaway self-signed certificate. The full suites were not run. The firm's real certificate, its legal acceptance and the Wiki deployment note remain open; details are in `status.json` under ADR-0016.

## STE-NXT-014 end-of-service provision accrual and report certificate signature evidence — 2026-10-10

Working-tree slice on `cee546f9`. The ADR-0010 recording mechanism is implemented: a recorded and Partner-confirmed accounting treatment, manual accrual journals with an entered basis through the journal maker and checker, and a posting gate in Application and in the database. Report certificate signature evidence is added with migration `20261010203413_ReportCertificateSignatureEvidence`, PEM signing credential source, deliverable signature verification endpoint and Angular verification. Observed: Release build, EF drift check, OpenAPI regeneration and Angular build passed. Per user instruction, automated tests were not run locally. No treatment has been confirmed, so end-of-service posting stays blocked. Details are in `status.json` under STE-NXT-014.

## AS-PAR-009 commercial pricing, revocation and delivery receipts — 2026-10-10

Committed at `cee546f9`. Pricing policy maker/checker, append-only approval revocation, quotation validity, signatory-bound acceptance and durable mail receipt states are implemented with migration `20261010185946_CommercialPricingPolicyAndDeliveryReceipts`. Observed: Release build, EF drift check, OpenAPI check, the targeted commercial Domain subset and the Angular build and specs passed. The full Domain, API and E2E suites were not run on this tree. Exact counts and open items are in `status.json` under AS-PAR-009.

## Current verification handoff — 2026-10-10

Legal/FSLI implementation and current-tree STE journeys passed local verification. See the [STE verification tracker](auditsphere-execution-tracker-ste-v21-gap-verification-current.md) and `status.json`. T003 professional sign-off, T014 dependencies and live Microsoft/provider acceptance remain open.

## Hostinger M365 portal follow-up and verified SharePoint target — 2026-10-10

The user authorized the dedicated Graph permission plan and asked to use the existing SharePoint
site intended for acceptance. The Entra browser showed the Directory Reader `User.Read.All` and
Mail `Mail.Send` application permissions granted for the tenant. A dedicated Guest Invitation app
was created with `User.Invite.All` as its only application role (plus delegated `User.Read`), and
the portal confirmed tenant admin consent. These grants are not capability verification in
AuditSphere.

The separate Group Membership registration was prepared as single-tenant, but left at the Register
screen because it states that proceeding agrees to Microsoft Platform Policies. The user must
perform that legal assent. The old Administration app still has `User.Create`, `User.Invite.All`
and `GroupMember.ReadWrite.All`; it remains disabled until the new Group Membership app has been
registered, configured with only its approved application role, and consented. Remove the two
extra roles only after that replacement is verified.

SharePoint Admin Center and read-only site/library requests confirmed an existing synthetic
development site and an empty workpapers library root. A separate same-tenant site can serve as the
negative control and must not receive an app grant. No site-level grant, test upload, or new site
creation occurred. The user authorized a write grant only to the selected site and one disposable
file create/read/delete probe, including a denied request to the control site; execution is pending
the missing server credential/configuration.

The AuditSphere browser page was refreshed after these portal changes. It showed Consent Required,
no consent attempt and directory `NOT_VERIFIED`; all mutation capabilities were Disabled. The
selected-resource form was then saved with the existing site, empty workpapers library and App
mediated profile. AuditSphere reported draft revision 4 in `VALIDATING` and a latest selected-site
result of `BLOCKED_EXTERNAL` (`selected-resource-draft-edited`); this is not a verified site grant.
Mail and Records drafts were Not Configured, and no client workspaces existed. The initial VPS check
found no M365 configuration or certificate files. A later, explicitly authorized setup created a
dedicated Selected Site certificate on the VPS, uploaded only its public certificate to the matching
Entra app, and wrote that app ID and the unrelated control-site URL to the protected deployment
`.env`. No values or certificate material are recorded here.

The tenant page also rendered `BLOCKED_EXTERNAL: configure the separately approved consent identity
and fixed callback`, with no Connect action. The nonce-bound consent handshake has not started. The
consent app ID, certificate and enablement remain absent from the deployment configuration. The
Selected Site flag remains off, and no M365 capability is enabled. A read-only inspection of the
running API container found no `/run/auditsphere/m365` mount and no Selected Site app ID or
certificate-path environment setting. The checked-in Compose file declares the expected mount and
settings, but the running container is not using that configuration. Services were left untouched as
instructed; a separately approved API-container recreation from the current Compose configuration
is required before the certificate can be used.

The statements above describe the earlier blocked state and are superseded by the observation below.

## Hostinger Selected Site exact-grant verification — 2026-10-11

The owner approved a write grant to the existing synthetic `AuditSphere Development` site and one
disposable create/read/delete probe. A temporary single-tenant grantor with delegated
`Sites.FullControl.All` created only that grant; its Entra registration, service principal and
delegated consent were then removed. The runtime app retained only `Sites.Selected`, and the
unrelated same-tenant control site received no grant.

The protected VPS `.env` now has the runtime app configured privately and its credential reference
aligned with the persisted AuditSphere connection (`configuration:SelectedSite`, file mode `0600`).
Only the API container was recreated. `/health/ready` returned HTTP 200; the live database and
worker containers were unchanged and no migration ran. The certificate matched the registration,
and token inspection showed only the `Sites.Selected` application role.

AuditSphere persisted the selected resource as `VERIFIED`, revision 7, diagnostic
`exact-resource-and-control-denial` (observed `2026-10-11T17:36:47.672211Z`). Graph allowed the
approved site, `Internal Workpapers` drive and configured root (HTTP 200), and denied the unrelated
control site (HTTP 403). The disposable file uploaded with HTTP 201, read back byte-for-byte,
deleted with HTTP 204 and then returned HTTP 404. The SharePoint library was empty after the probe.
No workspace was activated and the PBC worker remains stopped.

At the latest tenant-page observation, Directory Reader freshness showed `STALE`; refresh and
reverify its bounded read before relying on current directory data. Records remains
`NOT_CONFIGURED`. This evidence verifies the exact selected-site boundary and a file operation only;
the broader M365, PBC, client workflow and production acceptance gates remain open. Do not place IDs,
account names, secrets or certificate material in repository docs.

## Dedicated client SharePoint sites enabled by default — 2026-10-11

On owner approval, dedicated client SharePoint site provisioning is now enabled by default (`ClientSites:Enabled=true`) in API, worker, and Compose configuration, while preserving explicit administrator disablement (`ClientSites:Enabled=false`).

Key architectural invariants verified:
1. **Rollout safety & explicit boundary**: An explicit UTC rollout boundary (`ClientSites:ClientsCreatedAfter`) is required and persisted to firm configuration. An unspecified boundary fails closed with `client-site-rollout-boundary-required` and never defaults to beginning-of-time (`DateTimeOffset.MinValue`). Existing clients created before the boundary are never automatically migrated or provisioned.
2. **Fail-closed shared-site fallback prohibition**: When the dedicated-site policy applies, PBC provisioning and workspace administration refuse fallback to the shared site (`client-site-required-pending`).
3. **Dedicated isolated worker & heartbeat**: An isolated `client-sites` worker group with a dedicated certificate mount manages site creation and staff membership. `ClientSiteWorkerHeartbeatService` persists periodic worker heartbeats (`ClientSiteWorkerHeartbeat` in `AuditSphereDbContext.ClientSharePointSites.cs` via migration `20261011201129_ClientSiteWorkerHeartbeats`).
4. **Administration & Monitoring**: `/api/ui/administration/microsoft365/client-sites` returns comprehensive `ClientSiteAdministrationView` including policy status, rollout boundary, worker readiness, heartbeat age, and per-client site and membership reconciliation details.
5. **Local verification**: Targeted suites pass cleanly:
   - `WorkspaceProvisioningApiTests`: 4/4 passed
   - `ClientSharePointSitesTests`: 18/18 passed
   - `EngagementWorkspaceProvisioningTests`: 6/6 passed
   - `SharePointClientSiteProviderTests`: 11/11 passed
   - Angular CI specs: 634/634 passed across 115 files
   - Angular production build: passed
   - OpenAPI contract verification: 0 drift
   - Documentation, filename, and narrative-metric checks: passed

> **Archived handoff history:** superseded 2026-10-09 evidence and implementation notes are preserved in the [historical slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#ste-v2-1-evidence-mapping-and-full-suite-runs--2026-10-09).



## PBC follow-up task automation — 2026-10-12

Commit `e9138600834b7bd874f1e20282a2384dcce81417` adds the WF-10 PBC follow-up slice. With `WorkflowExperience:Enabled` enabled, sending a PBC request creates one fieldwork task with a nullable exact-scope request link in the same database transaction as the sent state and queued client communication. A filtered unique index prevents duplicate task bindings; the composite foreign key prevents cross-firm, cross-client, or cross-engagement links. Existing tasks remain unlinked. The queue derives **Waiting for client** from the linked PBC state, and the command handler blocks completion until acceptance or closure. The feature remains controlled by the existing configuration flag; no worker behavior was changed.

Verification on the committed tree: the focused PostgreSQL cohort passed 3/3; the API Release build passed with two NU1900 vulnerability-feed warnings; EF reports no pending model changes; narrative metrics and UI inventory pass. The model migration is source-only and was not applied to a persistent database. The local test runner needed loopback networking for its disposable PostgreSQL fixtures.

Limits: Angular production build aborted with exit 134 and no compiler diagnostics; the local authenticated browser route was not verified. Markdown health and filename checks report only the separately staged user document `STE_Audit_Management_Requirements_v2.2.md`, which remains untouched. The local OpenAPI emitter did not finish, so contract drift is not claimed. Full suites, migration application, live tenant, and professional acceptance were not run. CI runs 38196223449 and 38196223492 were in progress at the time of observation.

## PBC due-date email reminders — 2026-10-12

Commit `1a820666b1b20047021c384f141b218fe2ed83b0` adds an opt-in PBC reminder discovery path to the isolated Acceptance live-mail worker. `PbcReminders:Enabled` defaults to `false`; enabling it requires live-mail worker configuration and an HTTPS portal base URL. The worker applies a configured local time zone, quiet hours, a bounded batch size, one pre-due reminder per due date, and interval-based overdue reminders.

Before queueing, the discovery path locks and rechecks the active engagement, open PBC request, exact client identity and scope grant, first-sign-in requirement, and upload window. A unique append-only receipt fences duplicate attempts. The existing PBC mail discovery/outbox performs delivery; the handler rechecks reminder eligibility before dispatch. Staff timeline status is derived from the durable mail operation so blocked, uncertain, failed, and sent states are not conflated. The reminder contains only the PBC area, due date, and portal link; authoritative request state is not changed.

Verification on the pushed code commit: the focused PostgreSQL `PbcReminderTests` cohort passed 4/4; the API Release build passed with two NU1900 vulnerability-feed warnings; EF model drift reports no pending model changes after a fresh build; narrative metrics passed. The Markdown health and filename validators report only the separately staged `STE_Audit_Management_Requirements_v2.2.md`; it was left untouched and excluded from both commits.

Limits: migration `20261012031933_PbcReminderReceipts` is source-only and was not applied to a persistent database; `PbcReminders:Enabled` remains false; no live email was sent. Full Domain/API/E2E suites and live/provider acceptance were not run. No browser journey was needed because this slice adds no UI.

## Commercial acceptance and lifecycle privacy hardening — 2026-10-12

Code checkpoint `58e3d443df8b8de8a223294889f0459ca1ec015d` hardens the WF-01/WF-03 commercial keys. Unlinked engagement proposal lookup is restricted to the engagement's service route, and a linked fee agreement must belong to the same client. A commercial acceptance now stands only when the proposal has sent/response timestamps, the newest approved quotation matches its fee, and an immutable dispatch record binds that quotation and the same nonempty offer hash cited by both sides. Engagement-letter and fee-agreement creation enforce that same evidence; the fee workspace shows the corresponding blockers, and onboarding readiness receives the API host's automatic-invoice setting. Lifecycle blocker text is now redacted for actors without a firm-wide management grant across both the canonical lifecycle endpoint and workflow projection. Workflow read refusals map request/scope/state failures to HTTP 400/403/409, with unexpected errors left as 500. Angular timestamps now require an explicit UTC offset.

Fixtures and regression cases were aligned to the exact dispatch contract, including missing-dispatch refusal, route isolation, current quotation binding, paid-advance readiness, and restricted lifecycle details. Verification on the code checkpoint: Release solution build succeeded with 0 errors and 6 NU1900 vulnerability-feed warnings; `WorkflowReadHttpStatusTests` passed 7/7; the targeted Angular decoder suite passed 6/6; `git diff --check` passed. GitHub Actions CI run [38201582511](https://github.com/nirzaf/AuditSphere/actions/runs/38201582511) passed: Docs · Health, Build · Angular (production build and full specs), and Build · .NET Release (EF drift, API publication, and OpenAPI drift) all succeeded.

Limits: PostgreSQL-backed Domain and E2E cases were compiled but not executed. PostgreSQL was not listening on 127.0.0.1:5433; startup was refused because the existing data directory reports a pre-existing shared-memory block. The local Angular production build exited 134 without compiler diagnostics, although the CI production build and full Angular suite passed. Local Markdown health and filename checks still report only the separately staged user file `STE_Audit_Management_Requirements_v2.2.md`; it was not modified or committed. Narrative metrics, UI inventory, JSON parsing and `git diff --check` passed. No local browser journey or full .NET suite is claimed. The v2.2 workflow epic remains PARTIAL; production, external-provider, and professional acceptance are still open.
